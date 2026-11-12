using System.Text.Json;
using LiveCG.Core;
using SkiaSharp;

internal static class LinkedOutputChecks
{
    private static string Pointer(ImageJob job) => LinkedOutput.IsFlat(job.OutputPath)
        ? Path.Combine(Path.GetDirectoryName(job.OutputPath)!, ".livecg", job.BroadcastId.ToString("N"), "pointer.js")
        : Path.Combine(job.OutputPath, "pointer.js");

    public static async Task RunFlat(string folder, Action<bool, string> check)
    {
        var origin = Directory.CreateDirectory(Path.Combine(folder, "flat-" + Guid.NewGuid().ToString("N"))).FullName;
        var output = Path.Combine(origin, "공용 출력");
        var source = Path.Combine(origin, "가격 #1.png");
        var replacement = Path.Combine(origin, "새 가격.png");
        using var bitmap = new SKBitmap(160, 100);
        bitmap.Erase(SKColors.Red); SourceImage.SavePng(bitmap, source);
        bitmap.Erase(SKColors.Blue); SourceImage.SavePng(bitmap, replacement);
        var job = new ImageJob { SourcePath = source, Width = 160, Height = 100, Playback = PlaybackSections.NewDefault() };
        job.OutputPath = LinkedOutput.Target(job, output);
        var other = new ImageJob { SourcePath = replacement, Width = 160, Height = 100 };
        other.OutputPath = LinkedOutput.Target(other, output);
        var projectPath = Path.Combine(origin, "작업.livecg.json");
        var video = new VideoOptions();
        await LinkedOutput.ExportAsync(job, video, job.OutputPath, default);
        await LinkedOutput.ExportAsync(other, video, other.OutputPath, default);
        check(Directory.GetFiles(output).Order().SequenceEqual(new[] { job.OutputPath, other.OutputPath }.Order()) &&
            Directory.GetDirectories(output).Single() == Path.Combine(output, ".livecg"), "two HTML sources share one data folder");
        var firstPointer = File.ReadAllText(Pointer(job));
        var otherPointer = File.ReadAllText(Pointer(other));
        var loader = File.ReadAllText(job.OutputPath);
        var changed = job.Snapshot();
        ProjectSources.Update(changed, replacement, 160, 100, true);
        await LinkedOutput.ExportAsync(changed, video, changed.OutputPath, default);
        var secondPointer = File.ReadAllText(Pointer(job));
        check(firstPointer != secondPointer && File.ReadAllText(job.OutputPath) == loader &&
            File.ReadAllText(Pointer(other)) == otherPointer, "refresh changes only the bound source revision");
        check(!Directory.EnumerateFiles(Path.Combine(output, ".livecg"), "*", SearchOption.AllDirectories)
            .Any(path => Path.GetFileName(path).Contains("preview", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(path) == "사용안내.txt"),
            "compact revisions omit preview and guide files");
        try { await LinkedOutput.ExportAsync(other, video, job.OutputPath, default); check(false, "same-name source cannot overwrite HTML"); }
        catch (IOException) { check(File.ReadAllText(job.OutputPath) == loader, "same-name source cannot overwrite HTML"); }
        var portable = ProjectSources.SavePortable(new() { Jobs = [job.Snapshot(), other.Snapshot()], Video = video }, folder);
        var copied = DocumentStore.Load<ProjectDocument>(portable);
        ProjectSources.Resolve(copied, portable);
        check(copied.Jobs.All(j => File.Exists(Pointer(j))) &&
            Path.GetDirectoryName(copied.Jobs[0].OutputPath) == Path.GetDirectoryName(copied.Jobs[1].OutputPath), "portable save copies HTML and shared data");
        DocumentStore.Save(projectPath, ProjectSources.ForSave(new() { Jobs = [job.Snapshot()], Video = video }, projectPath));
        File.WriteAllText(Pointer(job), firstPointer);
        File.WriteAllText(Path.Combine(folder, "linked-browser-test.json"),
            JsonSerializer.Serialize(new { entry = job.OutputPath, pointer = Pointer(job), firstPointer, secondPointer, projectPath }));
    }

    public static async Task<object> Refresh(string folder)
    {
        using var fixture=JsonDocument.Parse(File.ReadAllText(Path.Combine(folder,"linked-browser-test.json")));
        var projectPath=fixture.RootElement.GetProperty("projectPath").GetString()!;
        var project=DocumentStore.Load<ProjectDocument>(projectPath);
        ProjectSources.Resolve(project,projectPath);
        var job=project.Jobs.Single();
        var replacement=Path.Combine(Path.GetDirectoryName(projectPath)!,"새 가격.png");
        using var image=SourceImage.Read(replacement);
        ProjectSources.Update(job,replacement,image.Width,image.Height,true);
        DocumentStore.Save(projectPath,ProjectSources.ForSave(new(){Jobs=[job.Snapshot()],Video=project.Video,OutputDirectory=project.OutputDirectory},projectPath));
        await LinkedOutput.ExportAsync(job,project.Video,job.OutputPath,default);
        var pointer=File.ReadAllText(Pointer(job));
        using var json=JsonDocument.Parse(pointer.Substring("window.LiveCGUpdate(".Length,pointer.Length-"window.LiveCGUpdate(".Length-2));
        return new{revision=json.RootElement.GetProperty("revision").GetString(),entry=LinkedOutput.Entry(job.OutputPath)};
    }
    public static object MoveScene(string folder, string scene)
    {
        using var fixture=JsonDocument.Parse(File.ReadAllText(Path.Combine(folder,"linked-browser-test.json")));
        var projectPath=fixture.RootElement.GetProperty("projectPath").GetString()!;
        var project=DocumentStore.Load<ProjectDocument>(projectPath);
        ProjectSources.Resolve(project,projectPath);
        DocumentStore.Save(projectPath,ProjectSources.ForSave(new(){Jobs=project.Jobs.Select(job=>job.Snapshot()).ToList(),Video=project.Video,OutputDirectory=project.OutputDirectory},projectPath));
        var original=Path.GetDirectoryName(projectPath)!;
        var target=Path.Combine(Path.GetDirectoryName(original)!,"OBS 다시 이전-"+Guid.NewGuid().ToString("N"));
        foreach(var file in Directory.GetFiles(original,"*",SearchOption.AllDirectories))
        {
            var destination=Path.Combine(target,Path.GetRelativePath(original,file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file,destination);
        }
        var movedProjectPath=Path.Combine(target,Path.GetFileName(projectPath));
        var moved=DocumentStore.Load<ProjectDocument>(movedProjectPath);
        ProjectSources.Resolve(moved,movedProjectPath);
        var rebound=Path.Combine(target,"OBS_연결.json");
        var changed=ObsScenePaths.Relink(scene,rebound,moved);
        return new{changed,entry=LinkedOutput.Entry(moved.Jobs[0].OutputPath),scene=rebound};
    }
    public static async Task Run(string folder, Action<bool, string> check)
    {
        var test = Directory.CreateDirectory(Path.Combine(folder, "linked-" + Guid.NewGuid().ToString("N"))).FullName;
        var origin = Directory.CreateDirectory(Path.Combine(test, "작업 PC 폴더")).FullName;
        var source = Path.Combine(origin, "가격.png");
        using var bitmap = new SKBitmap(160, 100); bitmap.Erase(SKColors.Red); SourceImage.SavePng(bitmap, source);
        var job = new ImageJob { SourcePath = source, Width = 160, Height = 100, OutputPath = Path.Combine(origin, "출력", "내 효과"),
            Playback = PlaybackSections.NewDefault(), Regions = [new() { Bounds = new(.1, .1, .8, .8), Clips = [new() { Duration = 1, Options = new() { Kind = EffectKind.Glow } }] }] };
        var video = new VideoOptions { EndHold = 0 };
        await LinkedOutput.ExportAsync(job, video, job.OutputPath, default);
        check(File.Exists(Path.Combine(job.OutputPath, "preview.html")) && File.ReadAllText(Path.Combine(job.OutputPath, "preview.html")).Contains("<base href="), "output retains a standalone preview with revision-relative assets");
        check(File.Exists(Path.Combine(job.OutputPath, "index.html")), "initial output provides a stable index.html");
        var projectPath = Path.Combine(origin, "작업.livecg.json");
        DocumentStore.Save(projectPath, ProjectSources.ForSave(new() { Jobs = [job.Snapshot()], Video = video, OutputDirectory = Path.GetDirectoryName(job.OutputPath)! }, projectPath));
        var saved = DocumentStore.Load<ProjectDocument>(projectPath);
        check(!Path.IsPathRooted(saved.Jobs[0].OutputPath), "saved output folder is relative to the project");
        var legacyJob = job.Snapshot(); legacyJob.BroadcastId = Guid.NewGuid(); legacyJob.OutputPath = "";
        legacyJob.Regions = LayerGraph.Copy(job.Regions);
        await new BrowserExporter().ExportAsync(legacyJob, video, Path.Combine(origin,"이전버전 임의폴더"),false,null,default);
        var legacyProject = Path.Combine(origin,"이전작업.livecg.json");
        DocumentStore.Save(legacyProject,ProjectSources.ForSave(new(){Jobs=[legacyJob]},legacyProject));
        var nestedPath = Path.Combine(origin,"작업 파일","중첩.livecg.json");
        DocumentStore.Save(nestedPath,ProjectSources.ForSave(new(){Jobs=[job.Snapshot()],Video=video},nestedPath));
        var nestedSaved=DocumentStore.Load<ProjectDocument>(nestedPath);
        check(!Path.IsPathRooted(nestedSaved.Jobs[0].SourcePath)&&nestedSaved.Jobs[0].SourcePath.StartsWith(".."),"project in a subfolder saves sibling original paths relatively");
        var oldOutput = job.OutputPath;
        var scene = Path.Combine(test, "obs.json");
        File.WriteAllText(scene, JsonSerializer.Serialize(new { sources = new[] { new { id = "browser_source", settings = new { local_file = Path.Combine(oldOutput, "index.html") } } } }));
        var moved = Path.Combine(test, "방송 PC 다른 폴더명"); Directory.Move(origin, moved);
        projectPath = Path.Combine(moved, "작업.livecg.json");
        var loaded = DocumentStore.Load<ProjectDocument>(projectPath);
        ProjectSources.Resolve(loaded, projectPath);
        var current = loaded.Jobs.Single();
        var nestedMovedPath=Path.Combine(moved,"작업 파일","중첩.livecg.json");
        var nestedMoved=DocumentStore.Load<ProjectDocument>(nestedMovedPath);
        ProjectSources.Resolve(nestedMoved,nestedMovedPath);
        check(File.Exists(nestedMoved.Jobs[0].SourcePath)&&Directory.Exists(nestedMoved.Jobs[0].OutputPath)&&nestedMoved.Jobs[0].SourcePath.StartsWith(moved),"nested project reconnects sibling original and output after transfer");
        var oldDocument = DocumentStore.Load<ProjectDocument>(Path.Combine(moved,"이전작업.livecg.json"));
        ProjectSources.Resolve(oldDocument,Path.Combine(moved,"이전작업.livecg.json"));
        check(oldDocument.Jobs[0].OutputPath==Path.Combine(moved,"이전버전 임의폴더"), "older projects locate exported folders by their existing region identity");
        check(File.Exists(current.SourcePath) && current.OutputPath.StartsWith(moved) && File.Exists(Path.Combine(current.OutputPath, "index.html")), "moved project automatically reconnects original and output folders");
        var rebound = Path.Combine(test, "obs_연결.json");
        var changed = ObsScenePaths.Relink(scene, rebound, loaded);
        using var reboundJson = JsonDocument.Parse(File.ReadAllText(rebound));
        var reboundFile = reboundJson.RootElement.GetProperty("sources")[0].GetProperty("settings").GetProperty("local_file").GetString()!;
        check(changed == 1 && Path.GetFullPath(reboundFile) == Path.Combine(current.OutputPath, "index.html"), "exported OBS scene is rebound to the relocated index");
        var portable = ProjectSources.SavePortable(loaded, Path.Combine(test,"한번에 이동"));
        var portableDoc = DocumentStore.Load<ProjectDocument>(portable); ProjectSources.Resolve(portableDoc,portable);
        check(File.Exists(Path.Combine(portableDoc.Jobs[0].OutputPath,"index.html")), "portable save includes linked output alongside project and original");
        var folderCount=Directory.GetDirectories(current.OutputPath).Length;
        try { ProjectSources.SavePortable(loaded,current.OutputPath); check(false,"portable save cannot recursively include its own output"); }
        catch(InvalidDataException) { check(Directory.GetDirectories(current.OutputPath).Length==folderCount,"portable save cannot recursively include its own output"); }
        var originalRegion = current.Regions[0].Id; var originalClip = current.Regions[0].Clips[0].Id;
        var replaced = Path.Combine(moved, "새 가격.png"); bitmap.Erase(SKColors.Blue); SourceImage.SavePng(bitmap, replaced);
        ProjectSources.Update(current, replaced, 160, 100, true);
        var pointer = Path.Combine(current.OutputPath, "pointer.js");
        await LinkedOutput.ExportAsync(current, video, current.OutputPath, default);
        check(current.Regions[0].Id == originalRegion && current.Regions[0].Clips[0].Id == originalClip, "replacement and refresh retain region and clip identities");
        await LinkedOutput.ExportAsync(current, video, current.OutputPath, default);
        bitmap.Erase(SKColors.Red); SourceImage.SavePng(bitmap, replaced);
        await LinkedOutput.ExportAsync(current, video, current.OutputPath, default);
        var firstPointer = File.ReadAllText(pointer);
        bitmap.Erase(SKColors.Blue); SourceImage.SavePng(bitmap, replaced);
        await LinkedOutput.ExportAsync(current, video, current.OutputPath, default);
        var secondPointer = File.ReadAllText(pointer);
        check(Directory.GetFiles(current.OutputPath,"*_index.html").Length == 1, "image renaming does not accumulate new entry filenames");
        check(Directory.GetDirectories(Path.Combine(current.OutputPath, ".livecg", "revisions")).Length == 2, "repeated refresh keeps only current and previous revisions");
        using (var cancel = new CancellationTokenSource())
        {
            cancel.Cancel();
            try { await LinkedOutput.ExportAsync(current, video, current.OutputPath, cancel.Token); check(false, "cancelled refresh rejected"); }
            catch (OperationCanceledException) { check(File.ReadAllText(pointer) == secondPointer, "cancelled refresh preserves the on-air revision"); }
        }
        var other = current.Snapshot(); other.BroadcastId = Guid.NewGuid();
        try { await LinkedOutput.ExportAsync(other, video, current.OutputPath, default); check(false, "other source cannot overwrite binding"); }
        catch (InvalidOperationException) { check(File.ReadAllText(pointer) == secondPointer, "other source cannot overwrite binding"); }
        var renamedOutput = Path.Combine(moved, "사용자가 바꾼 출력 폴더"); Directory.Move(current.OutputPath, renamedOutput);
        current.OutputPath = "missing-output";
        ProjectSources.Resolve(loaded, projectPath);
        check(current.OutputPath == renamedOutput, "renamed output folder is rediscovered by its saved identity");
        File.WriteAllText(Path.Combine(renamedOutput, "pointer.js"), firstPointer);
        File.WriteAllText(Path.Combine(folder, "linked-browser-test.json"), JsonSerializer.Serialize(new { entry = Path.Combine(renamedOutput, "index.html"), firstPointer, secondPointer, projectPath }));
    }
}
