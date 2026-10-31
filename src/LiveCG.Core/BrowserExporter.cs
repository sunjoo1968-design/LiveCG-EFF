using System.Reflection;
using System.Text;
using System.Text.Json;
using SkiaSharp;

namespace LiveCG.Core;

public sealed class BrowserExporter
{
    private const string Marker="LiveCG-EFF OBS package";
    private static readonly Encoding Utf8=new UTF8Encoding(false);
    private static readonly JsonSerializerOptions Json=new(){PropertyNamingPolicy=JsonNamingPolicy.CamelCase,WriteIndented=true};
    public static string OutputName(ImageJob job)=>Path.GetFileNameWithoutExtension(job.SourcePath)+".obs";
    public static bool IsPackage(string path)
    {
        try{using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(path,"manifest.json")));return doc.RootElement.GetProperty("format").GetString()==Marker;}
        catch(Exception e)when(e is IOException or JsonException or KeyNotFoundException or InvalidOperationException){return false;}
    }
    public static void CheckTarget(string path,bool overwrite)
    {
        if(File.Exists(path))throw new IOException("출력 폴더 이름의 파일이 이미 있습니다: "+path);
        if(!Directory.Exists(path))return;
        if(!overwrite)throw new IOException("같은 이름의 OBS 패키지가 있습니다.");
        if(!IsPackage(path))throw new IOException("LiveCG가 생성한 패키지가 아닌 폴더는 교체하지 않습니다: "+path);
    }
    public async Task ExportAsync(ImageJob job,VideoOptions video,string target,bool overwrite,IProgress<double>? progress,CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();new ProjectDocument{Jobs=[job],Video=video}.Validate();
        var plan=PlaybackPlan.Create(job,video);target=Path.GetFullPath(target);CheckTarget(target,overwrite);
        var parent=Path.GetDirectoryName(target)!;Directory.CreateDirectory(parent);
        var temporary=Path.Combine(parent,"."+Path.GetFileName(target)+"."+Guid.NewGuid().ToString("N")+".partial");
        var backup=temporary+".previous";Directory.CreateDirectory(temporary);
        try
        {
            using var source=SKBitmap.Decode(job.SourcePath)??throw new InvalidDataException("PNG를 읽을 수 없습니다.");
            if(source.Width!=job.Width||source.Height!=job.Height)throw new InvalidDataException("PNG 크기가 작업을 저장한 뒤 달라졌습니다. 다시 가져오세요.");
            using var scene=new EffectScene(source);scene.Validate(SectionRules.RenderRegions(job));
            File.Copy(job.SourcePath,Path.Combine(temporary,"original.png"));
            var assets=scene.WriteBrowserAssets(job.Regions,Path.Combine(temporary,"assets"),p=>progress?.Report(p*.8),cancellation);
            var regions=LayerGraph.Copy(SectionRules.RenderRegions(job),false);foreach(var r in regions)r.Cutout=null;
            var payload=new{version=ProductInfo.Version,credit=ProductInfo.Credit,sourceName=job.FileName,width=job.Width,height=job.Height,fps=video.Fps,source="original.png",plan,playback=job.Playback,regions,assets,
                seeds=job.Regions.SelectMany(r=>r.Clips).GroupBy(c=>c.Id).ToDictionary(g=>g.Key.ToString(),g=>unchecked((uint)g.Key.GetHashCode()))};
            await File.WriteAllTextAsync(Path.Combine(temporary,"data.js"),"window.LIVECG_DATA = "+JsonSerializer.Serialize(payload,Json)+";\n",Utf8,cancellation);
            foreach(var name in new[]{"player.js","index.html","preview.html"})
            {
                using var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("LiveCG.Core.Browser."+name)??throw new FileNotFoundException("브라우저 출력 리소스가 없습니다: "+name);
                using var reader=new StreamReader(stream);await File.WriteAllTextAsync(Path.Combine(temporary,name),await reader.ReadToEndAsync(cancellation),Utf8,cancellation);
            }
            var manifest=new{format=Marker,version=ProductInfo.Version,creator="SunjooAn",sourceName=job.FileName,width=job.Width,height=job.Height,fps=video.Fps,sections=job.Playback};
            await File.WriteAllTextAsync(Path.Combine(temporary,"manifest.json"),JsonSerializer.Serialize(manifest,Json),Utf8,cancellation);
            await File.WriteAllTextAsync(Path.Combine(temporary,"사용안내.txt"),Guide(job,video),Utf8,cancellation);
            cancellation.ThrowIfCancellationRequested();CheckTarget(target,overwrite);
            if(Directory.Exists(target))Directory.Move(target,backup);
            try{Directory.Move(temporary,target);}catch{if(Directory.Exists(backup)&&!Directory.Exists(target))Directory.Move(backup,target);throw;}
            if(Directory.Exists(backup))Directory.Delete(backup,true);
            progress?.Report(1);
        }
        finally{if(Directory.Exists(temporary))Directory.Delete(temporary,true);}
    }
    private static string Guide(ImageJob job,VideoOptions video)=>$"""
        LiveCG-EFF v{ProductInfo.Version} · 제작자 SunjooAn
        원본: {job.FileName} · {job.Width} × {job.Height} · {video.Fps} fps · 투명/무음

        1. preview.html을 더블클릭하여 재등장/반복/퇴장을 확인합니다. 체크무늬와 조작 버튼은 방송에 나오지 않습니다.
        2. OBS 소스 추가 > 브라우저 > 로컬 파일 체크 > 이 폴더의 index.html 선택.
        3. 너비 {job.Width}, 높이 {job.Height}, 사용자 지정 FPS {video.Fps} 설정.
        4. '보이지 않으면 소스 종료', '장면 활성화 시 새로고침'은 모두 끕니다. 재등장은 패키지 설정이 제어합니다.
        5. 소스 우클릭 > 상호작용에서 R=재등장, E=퇴장, Space=일시정지/계속. 눈 버튼으로 숨기기 전에 E를 눌러야 퇴장이 보입니다.
        6. 등장·반복·퇴장 사용 시 반복 구간만 무한 재생합니다. 꺼짐이면 전체를 1회 재생 후 마지막 프레임을 유지합니다.
        7. JS 자동화는 window.LiveCG.restart()/exit()/pause()/resume()/status()를 사용할 수 있습니다.
           OBS 브라우저 사용자 이벤트 LiveCGControl의 detail: command="exit", target="all". target을 생략해도 모든 LiveCG소스에 전달되므로, 개별 제어는 target에 이 manifest.json의 sourceName을 사용하세요.
        8. 폴더 전체를 함께 보관하세요. 원본 PNG나 영역 변경 후에는 앱에서 패키지를 다시 출력하세요(영상 인코딩은 필요하지 않습니다).
        9. Canvas와 Skia의 안티앨리어싱/블러/텍스트는 약간 다를 수 있습니다. 실제 방송 전 OBS에서 미리 확인하세요.

        OBS 설정 안내: https://obsproject.com/kb/browser-source
        브라우저 이벤트: https://github.com/obsproject/obs-browser
        """;
}
