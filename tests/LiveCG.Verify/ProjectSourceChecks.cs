using LiveCG.Core;
using SkiaSharp;

internal static class ProjectSourceChecks
{
    public static void Run(string root, Action<bool, string> check)
    {
        var folder = Directory.CreateDirectory(Path.Combine(root, Guid.NewGuid().ToString("N"))).FullName;
        var source = Path.Combine(folder, "가격.png");
        var replacement = Path.Combine(folder, "새 가격.png");
        using var bitmap = new SKBitmap(80, 60);
        bitmap.Erase(SKColors.Red);
        SourceImage.SavePng(bitmap, source);
        bitmap.Erase(SKColors.Blue);
        SourceImage.SavePng(bitmap, replacement);
        var clip = new EffectClip { Start = 1, Duration = 2, Options = new() { Kind = EffectKind.Glow } };
        var region = new EffectRegion { Bounds = new(.1, .2, .4, .3), Clips = [clip], DigitDividers = [.5] };
        var job = new ImageJob { SourcePath = source, Width = 80, Height = 60, Regions = [region] };
        var path = ProjectSources.SavePortable(new() { Jobs = [job] }, folder);
        var moved = Path.Combine(folder, "다른 PC 작업");
        Directory.Move(Path.GetDirectoryName(path)!, moved);
        path = Path.Combine(moved, Path.GetFileName(path));
        var document = DocumentStore.Load<ProjectDocument>(path);
        var loaded = document.Jobs.Single();
        check(!Path.IsPathRooted(loaded.SourcePath), "portable project uses relative asset paths");
        ProjectSources.Resolve(document, path);
        check(File.Exists(loaded.SourcePath) && loaded.SourcePath.StartsWith(moved), "portable assets resolve after moving the bundle");
        File.Delete(loaded.SourcePath);
        ProjectSources.Resolve(document, path);
        check(!File.Exists(loaded.SourcePath) && loaded.Regions.Single().Clips.Single().Id == clip.Id, "missing source leaves the editable timeline loaded");
        ProjectSources.Update(loaded, source, 80, 60, false);
        check(loaded.Regions.Single().DigitDividers.SequenceEqual(new[] { .5 }), "relink keeps source-specific edit settings");
        ProjectSources.Update(loaded, replacement, 80, 60, true);
        var edited = loaded.Regions.Single();
        var effect = edited.Clips.Single();
        check(edited.Id == region.Id && edited.Bounds == region.Bounds && effect.Id == clip.Id &&
              effect.Start == 1 && effect.Duration == 2 && effect.Options.Kind == EffectKind.Glow, "replacement preserves region and effect identity and timing");
        check(edited.DigitDividers.Count == 0, "replacement discards stale digit boundaries");
        using var current = SourceImage.Read(loaded);
        using var scene = new EffectScene(current);
        using var frame = scene.Render(loaded.Regions, new(), 0);
        check(frame.GetPixel(0, 0) == SKColors.Blue, "renderer uses replacement pixels");
        using var boat = RouteRenderer.Icon(new TravelRoute(), TravelIcon.Boat);
        check(boat.GetPixel(60, 85).Alpha > 0, "boat icon renders");
    }
}
