using System.IO.Compression;
using System.Text.Json;
using LiveCG.Core;
using SkiaSharp;

internal static class BroadcastUpdateChecks
{
    public static async Task Run(string folder, Action<bool, string> check)
    {
        var fixtureFolder = Path.Combine(folder, "broadcast");
        folder = Path.Combine(fixtureFolder, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var source = Path.Combine(folder, "가격 예시.png");
        using var image = new SKBitmap(160, 100);
        image.Erase(SKColors.Red);
        SourceImage.SavePng(image, source);
        var job = new ImageJob { SourcePath = source, Width = 160, Height = 100,
            Playback = PlaybackSections.NewDefault(),
            Regions = [new() { Bounds = new(.1, .1, .8, .8), Clips = [new() { Duration = 1, Options = new() { Kind = EffectKind.Glow } }] }] };
        var video = new VideoOptions { EndHold = 0 };
        var originalId = job.BroadcastId;
        var snapshot = job.Snapshot();
        check(snapshot.BroadcastId == originalId, "project snapshots preserve broadcast identity");
        var project = Path.Combine(folder, "작업.livecg.json");
        DocumentStore.Save(project, new ProjectDocument { Jobs = [job] });
        check(DocumentStore.Load<ProjectDocument>(project).Jobs.Single().BroadcastId == originalId, "saved projects preserve broadcast identity");
        var portablePath = ProjectSources.SavePortable(new ProjectDocument { Jobs = [job] }, folder);
        var portableFolder = Path.GetDirectoryName(portablePath)!;
        var imageFolder = Directory.GetDirectories(Path.Combine(portableFolder, "자료")).Single();
        var changedFolder = Path.Combine(portableFolder, "다른 폴더 혜택1");
        Directory.Move(imageFolder, changedFolder);
        var oldName = Directory.GetFiles(changedFolder).Single();
        File.Move(oldName, Path.Combine(changedFolder, "자료 이름 변경.png"));
        var movedProject = DocumentStore.Load<ProjectDocument>(portablePath);
        ProjectSources.Resolve(movedProject, portablePath);
        check(File.Exists(movedProject.Jobs[0].SourcePath) && movedProject.Jobs[0].BroadcastId == originalId,
            "saved fingerprint reconnects source after internal folder and image renaming");
        var zip1 = Path.Combine(folder, "first.zip");
        await BroadcastUpdates.ExportAsync(job, video, zip1, default);
        var root = Path.Combine(folder, "다른 PC 자료");
        var first = BroadcastUpdates.Prepare(root, zip1);
        check(!File.Exists(BroadcastUpdates.Entry(root, originalId)), "import preparation cannot change the on-air entry");
        BroadcastUpdates.Apply(root, first, false);
        var entry = BroadcastUpdates.Entry(root, originalId);
        var pointer = Path.Combine(Path.GetDirectoryName(entry)!, "pointer.js");
        var oldPointer = File.ReadAllText(pointer);
        var renamed = Path.Combine(folder, "이름 변경 혜택1.png");
        File.Move(source, renamed);
        job.SourcePath = renamed;
        image.Erase(SKColors.Blue); SourceImage.SavePng(image, renamed);
        var zip2 = Path.Combine(folder, "folder-name-independent.zip");
        await BroadcastUpdates.ExportAsync(job, video, zip2, default);
        var second = BroadcastUpdates.Prepare(root, zip2);
        check(BroadcastUpdates.Prepare(root, zip2) == second, "reimporting the same immutable revision is safe");
        var blocked = false;
        using (var locked = new FileStream(pointer, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            try { BroadcastUpdates.Apply(root, second, false); }
            catch (IOException) { blocked = true; }
        }
        check(blocked && File.ReadAllText(pointer) == oldPointer && BroadcastUpdates.List(root).Single().Active == first,
            "failed pointer replacement retains the current broadcast binding");
        check(second.Id == first.Id && second.Name != first.Name && first.Revision != second.Revision, "renamed source keeps identity with a new immutable revision");
        check(File.ReadAllText(pointer) == oldPointer, "prepared replacement leaves the current pointer unchanged");
        BroadcastUpdates.Apply(root, second, false);
        check(BroadcastUpdates.Entry(root, second.Id) == entry && BroadcastUpdates.List(root).Single().Previous == first, "update preserves OBS entry and keeps rollback revision");
        BroadcastUpdates.Apply(root, second, true);
        check(BroadcastUpdates.List(root).Single().Immediate && BroadcastUpdates.List(root).Single().Previous == first,
            "reapplying a pending revision can request immediate activation without losing rollback");
        BroadcastUpdates.Rollback(root, originalId);
        check(BroadcastUpdates.List(root).Single().Active == first && File.ReadAllText(pointer).Contains(first.Revision), "rollback atomically reselects the previous revision");
        BroadcastUpdates.Apply(root, second, true);
        var third = job.Snapshot(); third.BroadcastId = Guid.NewGuid();
        var zip3 = Path.Combine(folder, "duplicate.zip");
        await BroadcastUpdates.ExportAsync(third, video, zip3, default);
        BroadcastUpdates.Apply(root, BroadcastUpdates.Prepare(root, zip3), true);
        check(BroadcastUpdates.List(root).Count == 2, "new identity remains a separate broadcast source");
        var traversal = Path.Combine(folder, "traversal.zip");
        using (var zip = ZipFile.Open(traversal, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(zip.CreateEntry("../escaped.txt").Open())) writer.Write("invalid");
        try { BroadcastUpdates.Prepare(root, traversal); check(false, "path traversal rejected"); }
        catch (InvalidDataException) { check(!File.Exists(Path.Combine(root, "escaped.txt")), "path traversal rejected"); }
        var corrupt = Path.Combine(folder, "corrupt.zip"); File.Copy(zip2, corrupt, true);
        using (var zip = ZipFile.Open(corrupt, ZipArchiveMode.Update))
        {
            zip.GetEntry("payload/original.png")!.Delete();
            using var writer = new StreamWriter(zip.CreateEntry("payload/original.png").Open()); writer.Write("bad");
        }
        try { BroadcastUpdates.Prepare(root, corrupt); check(false, "corrupted transfer rejected"); }
        catch (InvalidDataException) { check(BroadcastUpdates.List(root).First(b => b.Active.Id == originalId).Active == second, "corrupted transfer leaves current revision intact"); }
        var shortLoops = new List<object>();
        foreach (var fps in new[] { 1, 60 })
        {
            var shortJob = job.Snapshot(); shortJob.BroadcastId = Guid.NewGuid();
            shortJob.Regions[0].Clips[0].Duration = 1d / fps;
            shortJob.Playback = new() { Enabled = true, IntroEnd = 0, LoopEnd = 1d / fps, ExitEnabled = false, LoopRest = 0 };
            var shortVideo = video with { Fps = fps };
            var shortZip = Path.Combine(folder, "short-" + fps + ".zip");
            await BroadcastUpdates.ExportAsync(shortJob, shortVideo, shortZip, default);
            var shortFirst = BroadcastUpdates.Prepare(root, shortZip);
            BroadcastUpdates.Apply(root, shortFirst, true);
            await BroadcastUpdates.ExportAsync(shortJob, shortVideo, shortZip, default);
            var shortSecond = BroadcastUpdates.Prepare(root, shortZip);
            check(PlaybackPlan.Create(shortJob, shortVideo).LoopFrames == 1, "single-frame loop fixture at " + fps + "fps");
            shortLoops.Add(new { entry = BroadcastUpdates.Entry(root, shortJob.BroadcastId), first = shortFirst, second = shortSecond, fps });
        }
        // Leave immutable packages and explicit pointers for the real local-file browser test.
        BroadcastUpdates.Apply(root, first, true);
        File.WriteAllText(Path.Combine(fixtureFolder, "browser-test.json"), JsonSerializer.Serialize(new { root, entry, first, second, shortLoops }));
    }
}
