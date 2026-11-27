using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using LiveCG.App;
using LiveCG.Core;
using SkiaSharp;

internal static class JobEffectCopyUiChecks
{
    public static void Run(MainWindow window, string folder, Action<bool, string> check)
    {
        T C<T>(string name) => (T)window.FindName(name);
        T F<T>(string name) => (T)typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        object? Call(string name, params object?[] args)
        {
            var method = typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
            return method.Invoke(window, method.GetParameters().Select((p, i) => i < args.Length ? args[i] : p.DefaultValue).ToArray());
        }
        void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

        var workDir = Directory.CreateDirectory(Path.Combine(folder, "job-effect-copy")).FullName;
        string MakePng(string name, int w, int h, SKColor color)
        {
            var file = Path.Combine(workDir, name);
            using var bmp = new SKBitmap(w, h);
            bmp.Erase(color);
            SourceImage.SavePng(bmp, file);
            return file;
        }

        var p1 = MakePng("01_원본.png", 960, 540, SKColors.DarkSlateBlue);
        var p2 = MakePng("02_대상A.png", 1080, 1080, SKColors.DarkOliveGreen);
        var p3 = MakePng("03_대상B.png", 1280, 720, SKColors.Maroon);
        var p4 = MakePng("04_영역없음.png", 800, 600, SKColors.Teal);

        var srcJob = new ImageJob
        {
            SourcePath = p1,
            Width = 960,
            Height = 540,
            Playback = new PlaybackSections { Enabled = true, ByEffects = true, LoopRest = 1.5, WebmRepeats = 5, RestMode = LoopRestMode.Hold },
            Regions =
            [
                new EffectRegion
                {
                    Name = "원본 가격",
                    Bounds = new(0.1, 0.7, 0.5, 0.18),
                    Clips =
                    [
                        new EffectClip { Start = 0.3, Duration = 0.6, Section = ClipSection.Intro, Options = new EffectOptions { Kind = EffectKind.WipeFromRight, Strength = StrengthLevel.Strong, Speed = 1.5 } },
                        new EffectClip { Start = 0.9, Duration = 1.4, Section = ClipSection.Loop, Loop = true, LoopPeriod = 0.7, Options = new EffectOptions { Kind = EffectKind.Glow, Strength = StrengthLevel.Medium, Color = "#FFCC22" } }
                    ]
                }
            ]
        };

        var targetGroupA = new EffectRegion
        {
            Name = "대상A 그룹",
            Role = RegionRole.Group,
            Bounds = new(0.25, 0.15, 0.6, 0.5),
            Shape = RegionShape.Ellipse,
            Locked = true,
            Clips = [new EffectClip { Start = 0, Duration = 0.5, Options = new EffectOptions { Kind = EffectKind.Bounce } }]
        };
        var targetChildA = new EffectRegion
        {
            Name = "대상A 부분",
            Role = RegionRole.EffectArea,
            ParentId = targetGroupA.Id,
            Bounds = new(0.3, 0.2, 0.2, 0.2)
        };
        var targetJobA = new ImageJob
        {
            SourcePath = p2,
            OutputPath = Path.Combine(workDir, "02_대상A.html"),
            Width = 1080,
            Height = 1080,
            Regions = [targetGroupA, targetChildA]
        };

        var targetRegionB1 = new EffectRegion
        {
            Name = "배지1",
            Bounds = new(0.05, 0.05, 0.3, 0.2),
            Enabled = false
        };
        var targetRegionB2 = new EffectRegion
        {
            Name = "배지2",
            Bounds = new(0.55, 0.6, 0.35, 0.25),
            Shape = RegionShape.RoundedRectangle
        };
        var targetJobB = new ImageJob
        {
            SourcePath = p3,
            Width = 1280,
            Height = 720,
            Regions = [targetRegionB1, targetRegionB2]
        };

        var regionlessJob = new ImageJob
        {
            SourcePath = p4,
            Width = 800,
            Height = 600
        };

        typeof(MainWindow).GetField("_jobEffectsClipboard", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, null);
        Call("UpdateJobEffectButtons");

        window.ApplyDocument(new ProjectDocument { Jobs = [srcJob, targetJobA, targetJobB, regionlessJob], Video = new VideoOptions { Fps = 30 } });
        Pump();

        var copyBtn = C<Button>("CopyJobEffectsButton");
        var pasteBtn = C<Button>("PasteJobEffectsButton");
        var pasteMenu = C<MenuItem>("PasteJobEffectsMenu");
        var fileList = C<ListBox>("FileList");
        var timeline = C<TimelineControl>("TimelineEditor");
        var preview = C<PreviewControl>("Preview");

        check(copyBtn.IsEnabled && !pasteBtn.IsEnabled && !pasteMenu.IsEnabled,
            "01 panel and edit menu expose copy/paste actions with paste disabled before copying");
        check(fileList.ContextMenu is { } ctx && ctx.Items.OfType<MenuItem>().Count(m => m.Header?.ToString()?.Contains("효과") == true) == 2,
            "01 file list right-click menu exposes effect bundle copy and paste items");

        // Pressing Ctrl+C while focus is on Timeline ALSO populates job effect clipboard & enables 01 paste button
        check(window.HandleEditorKey(Key.C, ModifierKeys.Control, timeline) && pasteBtn.IsEnabled && pasteMenu.IsEnabled,
            "Ctrl+C from timeline/editor also prepares 01 material effect bundle clipboard");

        // Even if user multi-selects [01, 04(regionless)] first (switching _current to 04), clicking Copy finds 01!
        typeof(MainWindow).GetField("_jobEffectsClipboard", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, null);
        Call("UpdateJobEffectButtons");
        fileList.SelectedItems.Clear();
        fileList.SelectedItems.Add(srcJob);
        fileList.SelectedItems.Add(regionlessJob);
        Pump();
        copyBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Pump();
        check(pasteBtn.IsEnabled, "clicking Copy when multi-selected falls back to the selected material that has effects");

        // Select all 4 jobs (including source & freshly imported regionless job) and paste via Ctrl+V while focus is on CopyJobEffectsButton (inside MaterialPanel)
        Call("SelectAllFiles", window, new RoutedEventArgs());
        Pump();
        var history = F<EditHistory>("_history");
        var undoBefore = history.UndoCount;

        check(window.HandleEditorKey(Key.V, ModifierKeys.Control, copyBtn),
            "Ctrl+V while focus is on a button inside 01 panel routes to batch material paste");
        Pump();

        var jobs = F<ObservableCollection<ImageJob>>("_jobs");
        var aAfter = jobs[1];
        var bAfter = jobs[2];
        var freshAfter = jobs[3];

        check(history.UndoCount == undoBefore + 1,
            "batch paste across multiple materials records a single atomic undo step");
        check(fileList.SelectedItems.Count == 4,
            "batch paste preserves multi-selection in 01 file list");
        check(freshAfter.SourcePath == p4 && freshAfter.Width == 800 && freshAfter.Height == 600 &&
              freshAfter.Regions.Count == 1 && freshAfter.Regions[0].Clips.Count == 2 &&
              preview.ShowEffects && C<CheckBox>("EffectsPreviewCheck").IsChecked == true,
            "regionless material receives clean regions and active preview immediately enables ShowEffects");

        check(aAfter.SourcePath == p2 && aAfter.Width == 1080 && aAfter.Height == 1080 &&
              aAfter.OutputPath.EndsWith("02_대상A.html") &&
              aAfter.Regions[0].Id == targetGroupA.Id &&
              aAfter.Regions[0].Bounds == new NormalizedRect(0.25, 0.15, 0.6, 0.5) &&
              aAfter.Regions[0].Shape == RegionShape.Ellipse &&
              aAfter.Regions[0].Locked &&
              aAfter.Regions[1].ParentId == targetGroupA.Id &&
              aAfter.Regions[0].Clips.Count == 2 &&
              aAfter.Regions[0].Clips[0].Options.Kind == EffectKind.WipeFromRight &&
              Math.Abs(aAfter.Regions[0].Clips[0].Start - 0.3) < 1e-9 &&
              Math.Abs(aAfter.Regions[0].Clips[0].Duration - 0.6) < 1e-9 &&
              aAfter.Regions[0].Clips[1].Options.Kind == EffectKind.Glow &&
              aAfter.Regions[1].Clips.Count == 0 &&
              Math.Abs(aAfter.Playback.LoopRest - 1.5) < 1e-9 && aAfter.Playback.WebmRepeats == 5,
            "Target A receives full effect bundle while preserving image, output path, bounds, shape, lock, and group hierarchy");

        check(bAfter.SourcePath == p3 && bAfter.Width == 1280 && bAfter.Height == 720 &&
              !bAfter.Regions[0].Enabled &&
              bAfter.Regions[0].Bounds == new NormalizedRect(0.05, 0.05, 0.3, 0.2) &&
              bAfter.Regions[1].Bounds == new NormalizedRect(0.55, 0.6, 0.35, 0.25) &&
              bAfter.Regions[0].Clips.Count == 2 && bAfter.Regions[1].Clips.Count == 2 &&
              bAfter.Regions[0].Clips[0].Id != bAfter.Regions[1].Clips[0].Id,
            "Target B keeps layer visibility and bounds while receiving independent clip copies");

        // Single Undo reverts ALL 3 pasted targets (including returning freshAfter back to 0 regions!)
        Call("UndoEdit", window, new RoutedEventArgs());
        Pump();
        jobs = F<ObservableCollection<ImageJob>>("_jobs");
        check(jobs[1].Regions[0].Clips.Count == 1 &&
              jobs[1].Regions[0].Clips[0].Options.Kind == EffectKind.Bounce &&
              jobs[1].OutputPath.EndsWith("02_대상A.html") &&
              jobs[2].Regions[0].Clips.Count == 0 &&
              jobs[2].Regions[1].Clips.Count == 0 &&
              jobs[3].Regions.Count == 0,
            "single Undo reverts all pasted materials including returning auto-created regions to empty");

        // Single Redo reapplies to all 3 targets!
        Call("RedoEdit", window, new RoutedEventArgs());
        Pump();
        jobs = F<ObservableCollection<ImageJob>>("_jobs");
        check(jobs[1].Regions[0].Clips.Count == 2 &&
              jobs[2].Regions[0].Clips.Count == 2 &&
              jobs[3].Regions.Count == 1 && jobs[3].Regions[0].Clips.Count == 2,
            "single Redo reapplies effect bundle to all target materials");
    }
}
