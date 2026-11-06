using System.Text.Json;

namespace LiveCG.Core;

public static class LinkedOutput
{
    public static async Task ExportAsync(ImageJob job, VideoOptions video, string target, CancellationToken cancellation, IProgress<double>? progress = null)
    {
        cancellation.ThrowIfCancellationRequested();
        target = Path.GetFullPath(target);
        BrowserExporter.CheckTarget(target, true);
        if (BrowserExporter.IsPackage(target) && HasOtherId(target, job.BroadcastId)) throw new InvalidOperationException("다른 자료의 출력 폴더입니다. 출력 폴더를 변경하세요.");
        var created = !Directory.Exists(target);
        Directory.CreateDirectory(target);
        if (created) Atomic(Path.Combine(target, "manifest.json"), JsonSerializer.Serialize(new { format = "LiveCG-EFF OBS package", broadcastId = job.BroadcastId }));
        using var guard = new FileStream(Path.Combine(target, ".refresh.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var storage = Path.Combine(target, ".livecg");
        var revisions = Path.Combine(storage, "revisions");
        Directory.CreateDirectory(revisions);
        var revision = Guid.NewGuid().ToString("N");
        var fresh = Path.Combine(revisions, revision);
        var pointer = Path.Combine(target, "pointer.js");
        string? previous = null;
        if (File.Exists(pointer))
        {
            var text = File.ReadAllText(pointer);
            const string prefix = "window.LiveCGUpdate(";
            if (text.StartsWith(prefix, StringComparison.Ordinal))
            {
                using var document = JsonDocument.Parse(text.Substring(prefix.Length, text.Length - prefix.Length - 2));
                previous = document.RootElement.GetProperty("revision").GetString();
            }
        }
        var committed = false;
        try
        {
            await new BrowserExporter().ExportAsync(job, video, fresh, false, progress, cancellation);
            var child = Path.Combine(fresh, "index.html");
            File.WriteAllText(child, File.ReadAllText(child).Replace("<head>", "<head>" + BroadcastUpdates.Bridge));
            var loader = BroadcastUpdates.Loader().Replace("revisions/", ".livecg/revisions/");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "index.html" };
            foreach (var alias in Directory.GetFiles(target, "*_index.html", SearchOption.TopDirectoryOnly))
                names.Add(Path.GetFileName(alias));
            if (names.Count == 1) names.Add(BrowserExporter.EntryName(job));
            // A stable loader keeps both the standard index and previously registered aliases alive.
            foreach (var name in names) Atomic(Path.Combine(target, name), loader);
            var preview = File.ReadAllText(Path.Combine(fresh, "preview.html")).Replace("<head>", "<head><base href='.livecg/revisions/" + revision + "/'>");
            var previewNames = Directory.GetFiles(target, "*_preview.html").Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (previewNames.Count == 0) previewNames.Add(BrowserExporter.PreviewName(job));
            previewNames.Add("preview.html");
            foreach (var name in previewNames) Atomic(Path.Combine(target, name!), preview);
            Atomic(Path.Combine(target, "사용안내.txt"), ProductInfo.Credit + Environment.NewLine + "OBS에는 이 폴더의 index.html을 등록하세요. 작업에서 저장·방송 갱신하면 같은 폴더에 반영됩니다. 다른 PC로 옮길 때 작업·원본·출력 폴더를 함께 보관하세요.");
            Atomic(Path.Combine(target, "manifest.json"), JsonSerializer.Serialize(new
            {
                format = "LiveCG-EFF OBS package", mode = "linked-folder", broadcastId = job.BroadcastId,
                version = ProductInfo.Version, creator = "SunjooAn", width = job.Width, height = job.Height, entry = "index.html"
            }));
            cancellation.ThrowIfCancellationRequested();
            Atomic(pointer, "window.LiveCGUpdate(" + JsonSerializer.Serialize(new { revision, immediate = true }) + ");");
            committed = true;
            foreach (var old in Directory.GetDirectories(revisions))
            {
                var name = Path.GetFileName(old);
                if (name == revision || name == previous || !Guid.TryParseExact(name, "N", out _)) continue;
                try { Directory.Delete(old, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
        catch
        {
            if (!committed && Directory.Exists(fresh)) Directory.Delete(fresh, true);
            throw;
        }
    }

    private static bool HasOtherId(string folder, Guid id)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "manifest.json")));
        return json.RootElement.TryGetProperty("broadcastId", out var value) && value.GetGuid() != id;
    }

    public static bool Matches(string folder, Guid id)
    {
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "manifest.json")));
            return json.RootElement.TryGetProperty("broadcastId", out var value) && value.GetGuid() == id;
        }
        catch (Exception error) when (error is IOException or JsonException or FormatException or UnauthorizedAccessException) { return false; }
    }

    public static bool MatchesLegacy(string folder, ImageJob job)
    {
        if (job.Regions.Count == 0 || File.Exists(Path.Combine(folder, "revision.json"))) return false;
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "manifest.json")));
            if (json.RootElement.TryGetProperty("broadcastId", out _) || !BrowserExporter.IsPackage(folder)) return false;
            var data = Path.Combine(folder, "data.js");
            return File.Exists(data) && File.ReadAllText(data).Contains(job.Regions[0].Id.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException) { return false; }
    }
    private static void Atomic(string path, string text)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, text); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
