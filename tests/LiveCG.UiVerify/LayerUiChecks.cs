using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using LiveCG.App;
using LiveCG.Core;
using LiveCG.Testing;
using SkiaSharp;

internal static class LayerUiChecks
{
    public static void Run(MainWindow window,string folder,Action<bool,string> check)
    {
        T C<T>(string name)=>(T)window.FindName(name);
        T Field<T>(string name)=>(T)typeof(MainWindow).GetField(name,BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(window)!;
        object? Call(string method,params object?[] values){var m=typeof(MainWindow).GetMethod(method,BindingFlags.NonPublic|BindingFlags.Instance)!;return m.Invoke(window,m.GetParameters().Select((p,i)=>i<values.Length?values[i]:p.DefaultValue).ToArray());}
        void Pump()=>Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
        ImageJob Job()=>Field<ImageJob>("_current");var preview=C<PreviewControl>("Preview");var timeline=C<TimelineControl>("TimelineEditor");var path=Path.Combine(folder,"타임라인 예제.png");
        void Load(bool empty=false){var job=TestFixtures.Demo(path);if(empty)job.Regions=[];window.ApplyDocument(new(){Jobs=[job],Video=new(){Fps=30,EndHold=.5}});C<CheckBox>("AutoFocusCheck").IsChecked=false;C<CheckBox>("HideOverlaysCheck").IsChecked=false;C<CheckBox>("FollowMagnetCheck").IsChecked=false;if(window.IsEditorFocused)Call("ToggleFocusMode",window,new RoutedEventArgs());Pump();}
        SKPoint At(float x,float y)=>preview.Viewport.ToScreen(new SKPoint(x,y));
        Load(true);C<ComboBox>("ShapeBox").SelectedIndex=(int)RegionShape.Polygon;var history=Field<EditHistory>("_history");var count=history.UndoCount;
        preview.BeginPointer(At(60,60));preview.EndPointer();preview.BeginPointer(At(300,60));preview.EndPointer();preview.BeginPointer(At(180,180));preview.EndPointer();
        check(Job().Regions.Count==0,"point pen keeps the region pending until the path is closed");
        preview.RemovePathPoint();preview.AddPathPoint(At(180,180));check(preview.FinishPath()&&Job().Regions[0].Points.Count==3&&Job().Regions[0].Shape==RegionShape.Polygon,"point pen closes clicked vertices into a usable region");
        check(history.UndoCount==count+1,"all point clicks create a single region undo transaction");Call("UndoEdit",window,new RoutedEventArgs());check(Job().Regions.Count==0,"undo removes the complete pen path");Call("RedoEdit",window,new RoutedEventArgs());check(Job().Regions[0].Points.Count==3,"redo restores the pen path points");
        var polygonId=Job().Regions[0].Id;C<ComboBox>("ShapeBox").SelectedIndex=(int)RegionShape.Freehand;preview.BeginPointer(At(350,60));preview.MovePointer(At(430,80));preview.MovePointer(At(420,160));preview.MovePointer(At(360,180));preview.EndPointer();
        check(Job().Regions.Count==2&&Job().Regions[0].Shape==RegionShape.Freehand&&Job().Regions[0].Points.Count>=4&&Job().Regions[1].Id==polygonId,"freehand drag closes a path and inserts it as the top layer");
        C<ComboBox>("ShapeBox").SelectedIndex=(int)RegionShape.Polygon;preview.AddPathPoint(At(600,60));preview.AddPathPoint(At(650,80));preview.CancelGesture();check(Job().Regions.Count==2,"ESC cancels an unfinished pen without creating a layer");
        Call("SetDrawMode",true);preview.AddPathPoint(At(600,60));preview.AddPathPoint(At(650,80));window.HandleEditorKey(Key.V,ModifierKeys.None,preview);check(!preview.AddMode&&!preview.FinishPath()&&Job().Regions.Count==2,"switching to selection mode discards the pending pen transaction");
        var firstId=Job().Regions[0].Id;count=history.UndoCount;timeline.BeginPointer(new(TimelineControl.LabelWidth-18,TimelineControl.RulerHeight+24));timeline.EndPointer();
        check(Job().Regions[1].Id==firstId&&history.UndoCount==count+1,"timeline layer-down control changes compositing order as one edit");Call("UndoEdit",window,new RoutedEventArgs());check(Job().Regions[0].Id==firstId,"undo restores layer order");
        timeline.BeginPointer(new(60,TimelineControl.RulerHeight+24));timeline.MovePointer(new(60,TimelineControl.RulerHeight+TimelineControl.RowHeight+24));timeline.EndPointer();
        check(Job().Regions[1].Id==firstId,"dragging a timeline row header reorders the layer");
        var target=Job().Regions[0];timeline.BeginPointer(new(14,TimelineControl.RulerHeight+24));timeline.EndPointer();check(!target.Enabled&&C<ListBox>("RegionList").Items.Cast<EffectRegion>().First().Label.Contains("숨김"),"timeline eye control hides the layer and clearly marks its row");
        Call("UndoEdit",window,new RoutedEventArgs());check(Job().Regions[0].Enabled,"undo restores layer visibility");
        StudioUiChecks.Capture(window,Path.Combine(folder,"path-layers.png"),1568,960);
        Load();Call("SelectRegion",Job().Regions[3],Job().Regions[3].Clips[0]);Call("Seek",.5d,false);count=history.UndoCount;
        check(window.SplitAtCursor()==1&&Job().Regions[3].Clips.Count==4&&timeline.SelectedClip!.SourceOffset==.5,"scissors split the selected actual timeline effect at the playhead");
        check(history.UndoCount==count+1,"scissors are one reversible transaction");Call("UndoEdit",window,new RoutedEventArgs());check(Job().Regions[3].Clips.Count==3&&Job().Regions[3].Clips[0].Duration==1.5,"undo restores the uncut original clip");
        check(window.HandleEditorKey(Key.K,ModifierKeys.Control,timeline)&&Job().Regions[3].Clips.Count==4,"Ctrl+K invokes scissors from the timeline");
        var fragments=Job().Regions[3].Clips.Where(c=>c.SequenceId is not null).ToList();var sequence=fragments[0].SequenceId;timeline.SetSelection(fragments.Select(c=>c.Id),fragments[0]);Call("CopyClip",window,new RoutedEventArgs());Call("Seek",5d,false);Call("PasteClip",window,new RoutedEventArgs());
        check(timeline.SelectedClips.Count==2&&timeline.SelectedClips.Select(x=>x.Clip.SequenceId).Distinct().Count()==1&&timeline.SelectedClip!.SequenceId!=sequence,"copying split fragments creates an independent continuous sequence");
        StudioUiChecks.Capture(window,Path.Combine(folder,"split-timeline.png"),1568,960);
        Load();var region=Job().Regions[0];Call("SelectRegion",region,region.Clips[0]);C<CheckBox>("SnapCheck").IsChecked=false;C<CheckBox>("RippleCheck").IsChecked=false;C<CheckBox>("FollowMagnetCheck").IsChecked=true;
        var rect=timeline.ClipRect(region,region.Clips[0]);timeline.BeginPointer(new(rect.Right-2,rect.Top+14));timeline.MovePointer(new(rect.Right-2+.4*timeline.PixelsPerSecond,rect.Top+14));timeline.EndPointer();
        check(Math.Abs(region.Clips[1].Start-region.Clips[0].End)<1e-8,"mouse growth with follower magnet pulls the next clip flush to the new end");
        rect=timeline.ClipRect(region,region.Clips[0]);timeline.BeginPointer(new(rect.Right-2,rect.Top+14));timeline.MovePointer(new(rect.Right-2-.5*timeline.PixelsPerSecond,rect.Top+14));timeline.EndPointer();
        check(Math.Abs(region.Clips[1].Start-region.Clips[0].End)<1e-8,"mouse shrink with follower magnet keeps the next clip attached");
        Call("SaveDefaults",window,new RoutedEventArgs());check(DocumentStore.Load<AppDefaults>(DocumentStore.DefaultsPath).Editor.FollowMagnet,"follower magnet option persists as a saved default");
        using(var source=SKBitmap.Decode(path))
        {
            var mask=new ObjectSelectionWindow(source,Job().Regions[3]){Owner=window,Left=-16000,Top=-16000,WindowStartupLocation=WindowStartupLocation.Manual,ShowActivated=false,ShowInTaskbar=false};
            try
            {
                mask.Show();Pump();var size=mask.BrushRadius;check(mask.HandleBrushKey(Key.OemCloseBrackets,ModifierKeys.None)&&mask.BrushRadius==size+1,"right bracket increases object brush radius by one source pixel");
                check(mask.HandleBrushKey(Key.OemOpenBrackets,ModifierKeys.Shift)&&mask.BrushRadius==size-9,"Shift bracket changes brush radius by ten pixels");
                check(!mask.HandleBrushKey(Key.OemCloseBrackets,ModifierKeys.Control),"brush shortcuts do not consume unrelated modifier combinations");
                var pixels=mask.Canvas.Buffer.Alpha.ToArray();mask.ContractOnePixel();check(!pixels.SequenceEqual(mask.Canvas.Buffer.Alpha),"one-pixel cleanup button modifies the actual object mask");mask.Undo();check(pixels.SequenceEqual(mask.Canvas.Buffer.Alpha),"mask cleanup can be undone without losing the object");
                mask.ApplySolidCleanup();check(mask.PrepareResult()&&mask.SelectedCutout!.ForegroundPng.Length>0&&mask.SelectedCutout.ErasePng.Length>0,"explicit precision cleanup preserves cleaned colors and separate source erase coverage");
                var copy=Job().Regions[3].Copy(false);copy.Bounds=mask.SelectedBounds;copy.Cutout=mask.SelectedCutout;var reopen=new ObjectSelectionWindow(source,copy);check(reopen.Canvas.Buffer.Alpha.SequenceEqual(MaskBuffer.Decode(copy.Cutout!).Alpha),"reopening fitted object preserves its saved mask instead of recalculating it");reopen.Close();
                mask.ApplySolidCleanup();check(mask.Canvas.ViewMode==2,"solid background preset opens the actual cleaned result preview");
                StudioUiChecks.Capture(mask,Path.Combine(folder,"mask-edge-cleanup.png"),1120,800);
            }finally{mask.Close();}
        }
        C<CheckBox>("FollowMagnetCheck").IsChecked=false;
    }
}
