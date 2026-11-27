using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using LiveCG.App;
using LiveCG.Core;
using SkiaSharp;

internal static class CopiedRegionHorizontalDragChecks
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

        var workDir = Directory.CreateDirectory(Path.Combine(folder, "copied-region-shift")).FullName;
        var imgPath = Path.Combine(workDir, "canvas.png");
        using (var bmp = new SKBitmap(1000, 600))
        {
            bmp.Erase(new SKColor(28, 44, 62));
            SourceImage.SavePng(bmp, imgPath);
        }

        var mask = new MaskTrack { Name = "영역 마스크", Bounds = new(0.15, 0.30, 0.20, 0.12), Duration = 2.0 };
        var originalRegion = new EffectRegion
        {
            Name = "가격 박스",
            Role = RegionRole.Object,
            Bounds = new(0.12, 0.34, 0.24, 0.16),
            Shape = RegionShape.RoundedRectangle,
            ContentOnly = true,
            Background = BackgroundMode.Solid,
            BackgroundColor = "#112233",
            Softness = new RegionSoftness { Enabled = true, Width = 6 },
            Masks = [mask],
            Clips = [new EffectClip { Start = 0.2, Duration = 1.0, Options = new EffectOptions { Kind = EffectKind.Glow, Strength = StrengthLevel.Strong } }]
        };
        var untouchedLayer = new EffectRegion
        {
            Name = "하단 배너",
            Role = RegionRole.Object,
            Bounds = new(0.08, 0.76, 0.50, 0.14),
            Enabled = false
        };

        var job = new ImageJob
        {
            SourcePath = imgPath,
            Width = 1000,
            Height = 600,
            Regions = [originalRegion, untouchedLayer]
        };

        window.ApplyDocument(new ProjectDocument { Jobs = [job], Video = new VideoOptions { Fps = 30 } });
        var preview = C<PreviewControl>("Preview");
        preview.Fit(false);
        Call("SelectRegion", originalRegion, null, true);
        Pump();

        SKPoint CenterScreen(NormalizedRect bounds) =>
            preview.Viewport.ToScreen(new SKPoint((float)((bounds.X + bounds.Width / 2) * 1000), (float)((bounds.Y + bounds.Height / 2) * 600)));
        SKPoint DeltaScreen(SKPoint from, float dxImagePx, float dyImagePx) =>
            new(from.X + (float)(dxImagePx * preview.Viewport.Scale), from.Y + (float)(dyImagePx * preview.Viewport.Scale));

        // 1) Normal (non-copied) region drag WITH Shift must remain free in both X and Y
        var origBefore = originalRegion.Bounds;
        var origStart = CenterScreen(origBefore);
        preview.BeginPointer(origStart);
        preview.MovePointer(DeltaScreen(origStart, 100, 90), constrain: true);
        check(Math.Abs(originalRegion.Bounds.X - (origBefore.X + 0.10)) < 1e-5 &&
              Math.Abs(originalRegion.Bounds.Y - (origBefore.Y + 0.15)) < 1e-5 &&
              !preview.IsHorizontalGuideActive,
            "normal non-copied region moves freely in both X and Y even when Shift is held");
        preview.CancelGesture();
        Pump();
        check(originalRegion.Bounds == origBefore, "cancelled normal drag restores exact original bounds");

        // 2) Duplicate region via DuplicateRegion ('영역 복제') and drag with real-time Shift press/release
        Call("DuplicateRegion", window, new RoutedEventArgs());
        Pump();
        var current = F<ImageJob>("_current");
        var copied = preview.SelectedRegion!;
        check(copied.Id != originalRegion.Id && current.Regions.Count == 3 && copied.Bounds == originalRegion.Bounds,
            "DuplicateRegion creates and selects a copied region at original bounds");

        var dragStart = CenterScreen(copied.Bounds);
        preview.BeginPointer(dragStart);

        // Free diagonal drag without Shift -> moves both X (+0.30) and Y (+0.20)
        var diagonalScreen = DeltaScreen(dragStart, 300, 120);
        preview.MovePointer(diagonalScreen, constrain: false);
        check(Math.Abs(copied.Bounds.X - (originalRegion.Bounds.X + 0.30)) < 1e-5 &&
              Math.Abs(copied.Bounds.Y - (originalRegion.Bounds.Y + 0.20)) < 1e-5 &&
              !preview.IsHorizontalGuideActive,
            "copied region moves freely in both X and Y when Shift is not held");

        // Press Shift mid-drag WITHOUT moving cursor -> snaps Y immediately to originalRegion.Bounds.Y in real time!
        preview.UpdateDragConstraint(true);
        check(Math.Abs(copied.Bounds.X - (originalRegion.Bounds.X + 0.30)) < 1e-5 &&
              Math.Abs(copied.Bounds.Y - originalRegion.Bounds.Y) < 1e-6 &&
              preview.IsHorizontalGuideActive,
            "pressing Shift mid-drag immediately aligns copied region Y to original region Y with live guide");

        // Move mouse further diagonally while holding Shift -> X updates (+0.45), Y stays locked to originalRegion.Bounds.Y!
        var furtherDiagonal = DeltaScreen(dragStart, 450, 180);
        preview.MovePointer(furtherDiagonal, constrain: true);
        check(Math.Abs(copied.Bounds.X - (originalRegion.Bounds.X + 0.45)) < 1e-5 &&
              Math.Abs(copied.Bounds.Y - originalRegion.Bounds.Y) < 1e-6 &&
              Math.Abs(copied.Bounds.Width - originalRegion.Bounds.Width) < 1e-9 &&
              Math.Abs(copied.Bounds.Height - originalRegion.Bounds.Height) < 1e-9,
            "dragging with Shift moves copied region horizontally only while preserving width and height");

        // Release Shift mid-drag -> immediately resumes free 2D movement at cursor!
        preview.UpdateDragConstraint(false);
        check(Math.Abs(copied.Bounds.X - (originalRegion.Bounds.X + 0.45)) < 1e-5 &&
              Math.Abs(copied.Bounds.Y - (originalRegion.Bounds.Y + 0.30)) < 1e-5 &&
              !preview.IsHorizontalGuideActive,
            "releasing Shift mid-drag restores free 2D movement immediately");

        // Hold Shift again, release mouse button, then release Shift -> aligned Y position stays committed!
        preview.UpdateDragConstraint(true);
        var history = F<EditHistory>("_history");
        var undoBeforeDrop = history.UndoCount;
        preview.EndPointer();
        preview.UpdateDragConstraint(false);
        Pump();

        check(Math.Abs(copied.Bounds.X - (originalRegion.Bounds.X + 0.45)) < 1e-5 &&
              Math.Abs(copied.Bounds.Y - originalRegion.Bounds.Y) < 1e-6 &&
              Math.Abs(copied.Bounds.Width - originalRegion.Bounds.Width) < 1e-9 &&
              Math.Abs(copied.Bounds.Height - originalRegion.Bounds.Height) < 1e-9 &&
              !preview.IsHorizontalGuideActive &&
              history.UndoCount == undoBeforeDrop + 1,
            "releasing mouse then releasing Shift commits horizontally aligned position as one undo entry");

        // Check all copied region properties and other layers remain preserved
        check(copied.Shape == RegionShape.RoundedRectangle &&
              copied.Background == BackgroundMode.Solid &&
              copied.BackgroundColor == "#112233" &&
              copied.Softness.Enabled && Math.Abs(copied.Softness.Width - 6) < 1e-9 &&
              copied.Masks.Count == 1 && copied.Clips.Count == 1 &&
              !untouchedLayer.Enabled && untouchedLayer.Bounds == new NormalizedRect(0.08, 0.76, 0.50, 0.14),
            "Shift horizontal drag preserves copied region settings, clips, masks, and other layers");

        // 3) Second drag: move copied region vertically first without Shift, commit, then Shift-drag again -> still aligns to originalRegion.Bounds.Y!
        var secondStart = CenterScreen(copied.Bounds);
        preview.BeginPointer(secondStart);
        preview.MovePointer(DeltaScreen(secondStart, -50, -90), constrain: false);
        preview.EndPointer();
        Pump();
        check(Math.Abs(copied.Bounds.Y - (originalRegion.Bounds.Y - 0.15)) < 1e-5,
            "copied region can be placed at a different Y when dragged without Shift");

        var thirdStart = CenterScreen(copied.Bounds);
        preview.BeginPointer(thirdStart);
        preview.MovePointer(DeltaScreen(thirdStart, 80, 60), constrain: true);
        preview.EndPointer();
        Pump();
        check(Math.Abs(copied.Bounds.Y - originalRegion.Bounds.Y) < 1e-6,
            "subsequent Shift-drag on copied region still snaps back to original source region Y");

        // 4) Undo & Redo preserve copied-region horizontal alignment tracking
        Call("UndoEdit", window, new RoutedEventArgs());
        Pump();
        current = F<ImageJob>("_current");
        var restoredCopy = current.Regions.First(r => r.Id == copied.Id);
        check(Math.Abs(restoredCopy.Bounds.Y - (originalRegion.Bounds.Y - 0.15)) < 1e-5 &&
              preview.TryGetCopiedOriginY(restoredCopy, out var trackedY) &&
              Math.Abs(trackedY - originalRegion.Bounds.Y) < 1e-6,
            "Undo restores previous position and retains original Y reference tracking");

        // 5) Ctrl+C and Ctrl+V on 02 Preview canvas also copies/pastes region with Shift horizontal alignment
        var origLive = current.Regions.First(r => r.Id == originalRegion.Id);
        Call("SelectRegion", origLive, null, true);
        Pump();
        check(window.HandleEditorKey(Key.C, ModifierKeys.Control, preview) &&
              window.HandleEditorKey(Key.V, ModifierKeys.Control, preview),
            "Ctrl+C and Ctrl+V on 02 preview canvas copies and pastes the selected region");
        Pump();
        var canvasCopied = preview.SelectedRegion!;
        var cStart = CenterScreen(canvasCopied.Bounds);
        preview.BeginPointer(cStart);
        preview.MovePointer(DeltaScreen(cStart, 220, -100), constrain: true);
        preview.EndPointer();
        Pump();
        check(canvasCopied.Id != origLive.Id &&
              Math.Abs(canvasCopied.Bounds.X - (origLive.Bounds.X + 0.22)) < 1e-5 &&
              Math.Abs(canvasCopied.Bounds.Y - origLive.Bounds.Y) < 1e-6,
            "region pasted via Ctrl+C/Ctrl+V on canvas also locks Y to original region when Shift-dragging");

        // 6) Combined integration check: Zoom + Spacebar hand-pan + Pane resize + Copied-region Shift horizontal drag
        window.SetEditorPaneSizes(230, 290, 320, false);
        Pump();
        preview.ZoomAt(2.0);
        preview.SetSpaceHeld(true);
        check(preview.IsSpaceHeld && ReferenceEquals(preview.Cursor, PointerOverlay.OpenHandCursor),
            "space hand cursor activates cleanly in integrated preview");
        preview.BeginPointer(new SKPoint(160, 140), pan: true);
        preview.MovePointer(new SKPoint(190, 165));
        preview.EndPointer();
        preview.SetSpaceHeld(false);
        Pump();

        var zoomedStart = CenterScreen(canvasCopied.Bounds);
        preview.BeginPointer(zoomedStart);
        preview.MovePointer(DeltaScreen(zoomedStart, 100, 80), constrain: true);
        preview.EndPointer();
        Pump();
        check(Math.Abs(canvasCopied.Bounds.X - (origLive.Bounds.X + 0.32)) < 1e-5 &&
              Math.Abs(canvasCopied.Bounds.Y - origLive.Bounds.Y) < 1e-6,
            "after pane resize, zoom, and space-pan, Shift-dragging copied region still maintains exact horizontal Y alignment");
        window.SetEditorPaneSizes(280, 335, 280, false);
        preview.Fit(false);
        Pump();
    }
}
