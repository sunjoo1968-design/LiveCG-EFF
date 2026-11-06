using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace LiveCG.Core;

public sealed record BroadcastRevision(Guid Id, string Revision, string Name, int Width, int Height);
public sealed record BroadcastBinding(BroadcastRevision Active, BroadcastRevision? Previous, bool Immediate);
public static class BroadcastUpdates
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public static string Slot(string root, Guid id) => Path.Combine(Path.GetFullPath(root), id.ToString("N"));
    public static string Entry(string root, Guid id) => Path.Combine(Slot(root, id), "entry.html");

    public static async Task ExportAsync(ImageJob job, VideoOptions video, string zipPath, CancellationToken cancellation)
    {
        if (job.BroadcastId == Guid.Empty) throw new InvalidDataException("방송 자료 ID가 없습니다.");
        var scratch = Path.Combine(Path.GetTempPath(), "LiveCG-transfer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        var temporary = Path.GetFullPath(zipPath) + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var payload = Path.Combine(scratch, "payload");
            await new BrowserExporter().ExportAsync(job, video, payload, false, null, cancellation);
            var revision = new BroadcastRevision(job.BroadcastId, Guid.NewGuid().ToString("N"), job.FileName, job.Width, job.Height);
            File.WriteAllText(Path.Combine(scratch, "transfer.json"), JsonSerializer.Serialize(revision, Json));
            var hashes = Directory.GetFiles(payload, "*", SearchOption.AllDirectories).ToDictionary(
                file => Path.GetRelativePath(scratch, file).Replace(Path.DirectorySeparatorChar, '/'),
                file => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))));
            File.WriteAllText(Path.Combine(scratch, "checksums.json"), JsonSerializer.Serialize(hashes, Json));
            cancellation.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(temporary)!);
            ZipFile.CreateFromDirectory(scratch, temporary);
            cancellation.ThrowIfCancellationRequested();
            File.Move(temporary, zipPath, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            Directory.Delete(scratch, true);
        }
    }

    public static BroadcastRevision Prepare(string root, string zipPath)
    {
        root = Path.GetFullPath(root);
        Directory.CreateDirectory(root);
        using var guard = Lock(root);
        var scratch = Path.Combine(root, ".import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            using (var zip = ZipFile.OpenRead(zipPath))
            {
                if (zip.Entries.Count > 10000 || zip.Entries.Sum(entry => entry.Length) > 1024L * 1024 * 1024)
                    throw new InvalidDataException("갱신 자료의 크기가 너무 큽니다.");
                foreach (var entry in zip.Entries)
                {
                    var name = entry.FullName.Replace(Path.DirectorySeparatorChar, '/');
                    var destination = Path.GetFullPath(Path.Combine(scratch, name));
                    if (!destination.StartsWith(scratch + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                        name.Contains(':') || (entry.ExternalAttributes >> 16 & 0xF000) == 0xA000)
                        throw new InvalidDataException("갱신 자료 경로가 올바르지 않습니다.");
                    if (name.EndsWith('/')) { Directory.CreateDirectory(destination); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    entry.ExtractToFile(destination);
                }
            }
            var revision = JsonSerializer.Deserialize<BroadcastRevision>(File.ReadAllText(Path.Combine(scratch, "transfer.json")), Json)
                ?? throw new InvalidDataException("갱신 정보가 없습니다.");
            Validate(revision);
            var hashes = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(scratch, "checksums.json")), Json)
                ?? throw new InvalidDataException("검증 정보가 없습니다.");
            var payload = Path.Combine(scratch, "payload");
            var files = Directory.GetFiles(payload, "*", SearchOption.AllDirectories);
            if (files.Length != hashes.Count || !BrowserExporter.IsPackage(payload))
                throw new InvalidDataException("브라우저 패키지가 올바르지 않습니다.");
            foreach (var file in files)
            {
                var key = Path.GetRelativePath(scratch, file).Replace(Path.DirectorySeparatorChar, '/');
                if (!hashes.TryGetValue(key, out var expected) ||
                    !Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).Equals(expected, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("자료가 손상되었습니다.");
            }
            if (!File.Exists(Path.Combine(payload, "index.html"))) throw new InvalidDataException("방송 진입 파일이 없습니다.");
            var target = Path.Combine(Slot(root, revision.Id), "revisions", revision.Revision);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            var fingerprint = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
                string.Join("\n", hashes.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Key + "=" + pair.Value)))));
            if (Directory.Exists(target) && File.ReadAllText(Path.Combine(target, ".transfer.sha256")) != fingerprint)
                throw new InvalidDataException("같은 버전 ID에 다른 자료가 있습니다.");
            if (!Directory.Exists(target))
            {
                // The child uses the parent OBS bridge while all package assets remain relative.
                var index = Path.Combine(payload, "index.html");
                File.WriteAllText(index, File.ReadAllText(index).Replace("<head>", "<head>" + Bridge));
                Atomic(Path.Combine(payload, "revision.json"), JsonSerializer.Serialize(revision, Json));
                Atomic(Path.Combine(payload, ".transfer.sha256"), fingerprint);
                Directory.Move(payload, target);
            }
            return revision;
        }
        finally { Directory.Delete(scratch, true); }
    }

    public static IReadOnlyList<BroadcastBinding> List(string root)
    {
        if (!Directory.Exists(root)) return [];
        return Directory.GetDirectories(root)
            .Where(folder => Guid.TryParseExact(Path.GetFileName(folder), "N", out _) && File.Exists(Path.Combine(folder, "pointer.js")))
            .Select(ReadBinding).ToList();
    }

    private static BroadcastBinding ReadBinding(string slot)
    {
        var pointer = File.ReadAllText(Path.Combine(slot, "pointer.js"));
        const string prefix = "window.LiveCGUpdate(";
        if (!pointer.StartsWith(prefix, StringComparison.Ordinal) || !pointer.EndsWith(");", StringComparison.Ordinal))
            throw new InvalidDataException("방송 연결 정보가 올바르지 않습니다.");
        using var document = JsonDocument.Parse(pointer.Substring(prefix.Length, pointer.Length - prefix.Length - 2));
        return document.RootElement.GetProperty("binding").Deserialize<BroadcastBinding>(Json)
            ?? throw new InvalidDataException("방송 연결 정보가 없습니다.");
    }
    public static void Apply(string root, BroadcastRevision revision, bool immediate)
    {
        Validate(revision);
        Directory.CreateDirectory(root);
        using var guard = Lock(root);
        var slot = Slot(root, revision.Id);
        if (!File.Exists(Path.Combine(slot, "revisions", revision.Revision, "index.html")))
            throw new FileNotFoundException("준비된 방송 자료가 없습니다.");
        var existing = File.Exists(Path.Combine(slot, "pointer.js")) ? ReadBinding(slot) : null;
        var previous = existing?.Active == revision ? existing.Previous : existing?.Active;
        var binding = new BroadcastBinding(revision, previous, immediate);
        Atomic(Path.Combine(slot, "entry.html"), Loader());
        Publish(slot, binding);
    }

    public static void Rollback(string root, Guid id)
    {
        using var guard = Lock(root);
        var slot = Slot(root, id);
        var binding = ReadBinding(slot);
        if (binding.Previous is null) throw new InvalidOperationException("이전 반영 자료가 없습니다.");
        var next = new BroadcastBinding(binding.Previous, binding.Active, true);
        Publish(slot, next);
    }

    private static void Publish(string slot, BroadcastBinding binding)
    {
        Atomic(Path.Combine(slot, "pointer.js"), "window.LiveCGUpdate(" + JsonSerializer.Serialize(new
        { revision = binding.Active.Revision, immediate = binding.Immediate, binding }, Json) + ");");
    }

    private static void Validate(BroadcastRevision revision)
    {
        if (revision.Id == Guid.Empty || !Guid.TryParseExact(revision.Revision, "N", out _) ||
            revision.Width <= 0 || revision.Height <= 0 || (long)revision.Width * revision.Height > 80_000_000)
            throw new InvalidDataException("방송 자료 ID 또는 크기가 올바르지 않습니다.");
    }

    private static FileStream Lock(string root) => new(Path.Combine(Path.GetFullPath(root), ".update.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    private static void Atomic(string path, string text)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, text); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private const string Bridge = """
<script>
window.obsstudio={};
window.addEventListener("message",event=>{
 if(event.source!==parent)return;
 const value=event.data;
 if(value?.type==="LiveCGCommand"&&["restart","exit","pause","resume","togglePause"].includes(value.command))window.LiveCG?.[value.command]();
 if(value?.type==="LiveCGEvent"&&["obsSourceActiveChanged","obsSourceVisibleChanged","LiveCGControl"].includes(value.name))window.dispatchEvent(new CustomEvent(value.name,{detail:value.detail}));
});
setInterval(()=>{if(window.LiveCG)parent.postMessage({type:"LiveCGStatus",state:LiveCG.status()},"*");},20);
</script>
""";
    private static string Loader() => """
<!doctype html><html><head><meta charset="utf-8"><title>__CREDIT__</title>
<style>html,body{margin:0;background:transparent;overflow:hidden}iframe{position:absolute;inset:0;width:100%;height:100%;border:0}</style></head><body><script>
let active=null,pending=null,current="",wanted="",immediate=false,last=null,program=null,visible=true;
window.LiveCGUpdate=value=>{
 if(!/^[a-f0-9]{32}$/.test(value.revision))return;
 immediate=!!value.immediate;
 if(value.revision===wanted)return;
 wanted=value.revision;
 if(pending){pending.remove();pending=null;}
 if(wanted===current)return;
 const frame=document.createElement("iframe");frame.style.visibility="hidden";
 frame.src="revisions/"+wanted+"/index.html";frame.dataset.revision=wanted;
 frame.onload=()=>{if(pending!==frame)return;};
 document.body.append(frame);pending=frame;
};
const states=new WeakMap();
function send(frame,value){frame?.contentWindow.postMessage(value,"*");}
function api(frame){
 if(!frame)return null;
 const command=name=>()=>send(frame,{type:"LiveCGCommand",command:name});
 return {status:()=>states.get(frame)??{},restart:command("restart"),exit:command("exit"),pause:command("pause"),resume:command("resume"),togglePause:command("togglePause")};
}
window.addEventListener("message",event=>{
 const frame=[active,pending].find(frame=>frame?.contentWindow===event.source);
 if(frame&&event.data?.type==="LiveCGStatus")states.set(frame,event.data.state);
});
function dispatch(frame,name,detail){send(frame,{type:"LiveCGEvent",name,detail});}
for(const name of ["obsSourceActiveChanged","obsSourceVisibleChanged","LiveCGControl"]){
 window.addEventListener(name,event=>{
  if(name==="obsSourceActiveChanged"&&typeof event.detail?.active==="boolean")program=event.detail.active;
  if(name==="obsSourceVisibleChanged")visible=event.detail?.visible??visible;
  dispatch(active,name,event.detail);
 });
}
window.addEventListener("keydown",event=>{if(event.code==="KeyR")api(active)?.restart();if(event.code==="KeyE")api(active)?.exit();if(event.code==="Space")api(active)?.togglePause();});
window.LiveCG={
 status:()=>({...api(active)?.status(),revision:current,pending:pending?.dataset.revision??null,pendingError:api(pending)?.status()?.error??null}),
 restart:()=>api(active)?.restart(),exit:()=>api(active)?.exit(),pause:()=>api(active)?.pause(),resume:()=>api(active)?.resume()
};
setInterval(()=>{
 const live=api(active),state=live?.status(),next=api(pending);
 if(next?.status().loaded&&!next.status().error){
  next.pause();
  const boundary=state&&last&&(state.time<last.time-.001||state.phase==="쉬기"&&last.phase!=="쉬기");
  if(!active||immediate||!visible||program===false||state?.paused||state?.waitingForProgram||["완료","퇴장 완료"].includes(state?.phase)||boundary){
   const old=active;active=pending;pending=null;current=active.dataset.revision;
   if(program!==false)api(active).restart();
   if(program!==null)dispatch(active,"obsSourceActiveChanged",{active:program});
   dispatch(active,"obsSourceVisibleChanged",{visible});
   active.style.visibility="visible";old?.remove();last=null;return;
  }
 }
 last=state;
},40);
function poll(){
 const script=document.createElement("script");script.src="pointer.js?t="+Date.now();
 script.onload=script.onerror=()=>script.remove();document.head.append(script);
}
poll();setInterval(poll,1000);
</script></body></html>
""".Replace("__CREDIT__", ProductInfo.Credit);
}
