using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using LiveCG.App;
using LiveCG.Core;
using SkiaSharp;

internal static class OutputUiChecks
{
    public static void Run(MainWindow w,string folder,Action<bool,string> check)
    {
        object? Call(string n,params object?[] args){var method=typeof(MainWindow).GetMethod(n,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)!;return method.Invoke(w,method.GetParameters().Select((p,i)=>i<args.Length?args[i]:p.DefaultValue).ToArray());}
        T C<T>(string n)=>(T)w.FindName(n);T F<T>(string n)=>(T)typeof(MainWindow).GetField(n,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(w)!;
        void Pump()=>Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
        var path=Path.Combine(folder,"타임라인 예제.png");var r=new EffectRegion{Name="가격",Bounds=new(.1,.7,.7,.2),ContentOnly=true,Clips=[new(){Duration=1,Options=new(){Kind=EffectKind.ZoomIn}},new(){Start=1,Duration=2,Options=new(){Kind=EffectKind.Zoom}}]};
        var first=new ImageJob{SourcePath=path,Width=960,Height=540,Playback=PlaybackSections.NewDefault(),Regions=[r]};var other=first.Snapshot();other.SourcePath=Path.Combine(folder,"두 번째 PNG.png");other.Playback.IntroEnd=.3;
        w.ApplyDocument(new(){Jobs=[first,other],Video=new(){Output=OutputKind.Webm,Fps=30,EndHold=0}});Call("ResetHistory");Pump();var job=F<ImageJob>("_current");
        C<Expander>("OutputExpander").IsExpanded=true;Pump();
        check(C<ComboBox>("WebmRepeatsBox").IsVisible&&C<ComboBox>("LoopRestBox").Text=="1"&&C<ComboBox>("RestModeBox").SelectedIndex==0,"left output panel exposes repeat count and default original rest without a dialog");
        check(C<TextBlock>("AutoDurationText").Text.Contains("9.00초")&&C<TextBlock>("AutoDurationText").Text.Contains("270프레임"),"output summary immediately includes two rests for three repeats");
        C<ComboBox>("WebmRepeatsBox").SelectedItem="5";Pump();check(job.Playback.WebmRepeats==5&&C<TextBlock>("AutoDurationText").Text.Contains("15.00초"),"left repeat dropdown directly updates model and final duration");
        C<ComboBox>("LoopRestBox").SelectedItem="0.5";Pump();check(job.Playback.LoopRest==.5&&C<TextBlock>("AutoDurationText").Text.Contains("13.00초"),"left rest dropdown immediately updates final duration");
        C<ComboBox>("RestModeBox").SelectedIndex=1;Pump();check(job.Playback.RestMode==LoopRestMode.Hold,"left rest appearance selector shares the playback model");
        Call("UndoEdit",w,new RoutedEventArgs());Pump();job=F<ImageJob>("_current");check(job.Playback.RestMode==LoopRestMode.Original&&C<ComboBox>("RestModeBox").SelectedIndex==0,"rest setting undo restores both model and left controls");
        var candidate=job.Snapshot();var dialog=new PlaybackWindow(candidate,new(){Fps=30,EndHold=0},0);check((int)dialog.RepeatsBox.SelectedItem==5&&dialog.RestBox.Text=="0.5","section window opens with the current left-panel repeat settings");
        dialog.RepeatsBox.SelectedItem=2;dialog.RestBox.Text="2";dialog.RestBox.RaiseEvent(new System.Windows.Input.KeyboardFocusChangedEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,Environment.TickCount,null,dialog.RestBox){RoutedEvent=System.Windows.Input.Keyboard.LostKeyboardFocusEvent});
        check(candidate.Playback.WebmRepeats==2&&candidate.Playback.LoopRest==2,"section controls change the same serializable repeat policy");job.Playback=dialog.Sections with{};dialog.Close();Call("RefreshTimeline");Pump();check(C<ComboBox>("WebmRepeatsBox").Text=="2"&&C<ComboBox>("LoopRestBox").Text=="2","applying section options refreshes the left output controls");
        C<ListBox>("FileList").SelectAll();Pump();var expected=F<ImageJob>("_current").Playback with{};check(w.ApplyTimingToSelected()==1,"repeat-only batch apply targets other selected files");
        check(F<System.Collections.ObjectModel.ObservableCollection<ImageJob>>("_jobs").All(j=>j.Playback.WebmRepeats==expected.WebmRepeats&&j.Playback.LoopRest==expected.LoopRest),"batch repeat settings leave each file with matching count and rest");
        C<ComboBox>("OutputKindBox").SelectedIndex=2;Pump();C<TextBox>("OutputFolderBox").Text=Path.Combine(folder,"v1-out");C<ComboBox>("ExportScopeBox").SelectedIndex=0;var lines=w.OutputPlanLines();check(lines.Count==4&&lines.Any(l=>l.Contains(".html"))&&lines.Any(l=>l.Contains("프레임")),"output plan lists both formats with named HTML paths and finite frame counts");
        job=F<ImageJob>("_current");job.Playback.Enabled=false;check(w.OutputPlanLines().Any(l=>l.Contains(".html")&&l.Contains("1회")),"one-shot browser output is labeled once instead of infinite in the output plan");job.Playback.Enabled=true;
        check(C<GridSplitter>("LeftWidthSplitter").IsEnabled&&C<GridSplitter>("EffectsWidthSplitter").IsEnabled&&C<GridSplitter>("TimelineSplitter").IsEnabled&&C<GridSplitter>("LayerHeightSplitter").IsEnabled,"timeline, preview, side-panel and layer-list splitters are all adjustable");
        var defaultPreviewWidth=C<Border>("PreviewPanel").ActualWidth;var defaultTimelineWidth=C<Border>("TimelinePanel").ActualWidth;var defaultTimelineHeight=C<Border>("TimelinePanel").ActualHeight;
        w.SetEditorPaneSizes(220,280,360,true);Pump();
        check(C<Border>("PreviewPanel").ActualWidth>defaultPreviewWidth+100&&Math.Abs(C<Border>("PreviewPanel").ActualWidth-C<Border>("TimelinePanel").ActualWidth)<.1&&C<Border>("TimelinePanel").ActualHeight>defaultTimelineHeight+60,"narrowing side panels and raising timeline height widens both preview and timeline panes");
        check(C<TimelineControl>("TimelineEditor").Width>=C<ScrollViewer>("TimelineScroll").ViewportWidth-.5,"widened timeline control fills the full scroll viewport width");
        var savedPanes=WorkspaceState.Load();check(Math.Abs(savedPanes.LeftWidth-220)<.1&&Math.Abs(savedPanes.RightWidth-280)<.1&&Math.Abs(savedPanes.TimelineHeight-360)<.1,"workspace persists custom left, right and timeline pane dimensions");
        Call("DetachPreview",w,new RoutedEventArgs());Pump();
        check(w.DetachedPreviewWindow?.ResizeMode==ResizeMode.CanResize,"detached preview window allows user resizing");
        w.DetachedPreviewWindow?.Close();Pump();
        Call("ResetWorkspace",w,new RoutedEventArgs());Pump();
        check(Math.Abs(w.PreferredLeftWidth-280)<.1&&Math.Abs(w.PreferredRightWidth-335)<.1&&Math.Abs(w.PreferredTimelineHeight-245)<.1,"workspace reset restores default pane widths and timeline height");
        w.SetEditorPaneSizes(280,335,280);w.SetLayerPanelSize(300,w.PreferredRightWidth);Pump();check(C<RowDefinition>("LayerPanelRow").ActualHeight>=200,"layer list height still changes cleanly alongside resizable panes");
        Call("PreviewSingleCycle",w,new RoutedEventArgs());typeof(MainWindow).GetField("_playStart",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(w,2.2d);Call("TickPlayback");check(C<PreviewControl>("Preview").RestDisplay==LoopRestMode.Original,"one-cycle preview displays original source during the rest");Call("StopPlayback");
        w.SetOutputTiming(2,2,LoopRestMode.Original);Call("SaveDefaults",w,new RoutedEventArgs());check(DocumentStore.Load<AppDefaults>(DocumentStore.DefaultsPath).Playback.LoopRest==2,"current repeat and rest options can be saved as defaults");
        var preview=C<PreviewControl>("Preview");C<CheckBox>("AutoFocusCheck").IsChecked=false;if(w.IsEditorFocused)Call("ToggleFocusMode",w,new RoutedEventArgs());preview.Fit(false);Pump();Call("AddRegion",w,new RoutedEventArgs());
        C<Button>("ZoomInButton").Focus();
        check(PointerOverlay.OpenHandCursor is not null&&PointerOverlay.GrabHandCursor is not null&&!ReferenceEquals(PointerOverlay.OpenHandCursor,PointerOverlay.GrabHandCursor),"distinct palm-open and palm-grab cursors are generated");
        preview.SetSpaceHeld(true);
        check(preview.IsSpaceHeld&&ReferenceEquals(preview.Cursor,PointerOverlay.OpenHandCursor)&&preview.AddMode,"holding space switches preview cursor to open palm while preserving draw mode");
        preview.ZoomAt(2);var panBefore=preview.Viewport.Pan;
        preview.BeginPointer(new SKPoint(120,100),true);
        check(preview.IsPanning&&ReferenceEquals(preview.Cursor,PointerOverlay.GrabHandCursor),"dragging while holding space switches to grabbing palm cursor");
        preview.MovePointer(new SKPoint(155,125));preview.EndPointer();
        check(Math.Abs(preview.Viewport.Pan.X-panBefore.X-35)<.01&&Math.Abs(preview.Viewport.Pan.Y-panBefore.Y-25)<.01&&preview.AddMode&&ReferenceEquals(preview.Cursor,PointerOverlay.OpenHandCursor),"space pan moves viewport without cancelling region draw mode");
        preview.SetSpaceHeld(false);preview.Fit(false);Pump();
        check(!preview.IsSpaceHeld&&ReferenceEquals(preview.Cursor,System.Windows.Input.Cursors.Cross)&&preview.AddMode,"releasing space restores crosshair cursor for region designation");
        var start=preview.Viewport.ToScreen(new SKPoint(20,20));var end=preview.Viewport.ToScreen(new SKPoint(120,100));preview.BeginPointer(start);preview.MovePointer(end);preview.EndPointer();Pump();check(preview.SelectedRegion?.ContentOnly==true&&preview.SelectedRegion.CutoutEnabled,"new mouse-created region defaults to background fixed/content-only");
        var regionCountBefore=F<ImageJob>("_current").Regions.Count;var emptyClick=preview.Viewport.ToScreen(new SKPoint(700,180));
        preview.BeginPointer(emptyClick);preview.EndPointer();Pump();
        check(preview.SelectedRegion is null&&F<ImageJob>("_current").Regions.Count==regionCountBefore,"clicking empty canvas after space pan cleanly deselects without adding a region");
        var emptyDragEnd=preview.Viewport.ToScreen(new SKPoint(860,300));
        preview.BeginPointer(emptyClick);preview.MovePointer(emptyDragEnd);preview.EndPointer();Pump();
        check(F<ImageJob>("_current").Regions.Count==regionCountBefore+1&&preview.SelectedRegion is not null,"dragging on empty canvas after releasing space designates a new region directly");
        Call("DeleteRegion",w,new RoutedEventArgs());Pump();
        Call("Notify","반복 사이 원본 유지 · 레이어 높이 조절 가능");StudioUiChecks.Capture(w,Path.Combine(folder,"output-v100.png"),1600,1040);StudioUiChecks.Capture(w,Path.Combine(folder,"output-narrow-v100.png"),1160,780);
        check(C<ListBox>("FileList").ActualHeight>=64,"narrow output panel preserves a visible material list above its settings");
        var imageInputs=new List<string>();using(var bitmap=new SKBitmap(64,48)){bitmap.Erase(SKColors.Coral);using var image=SKImage.FromBitmap(bitmap);foreach(var format in new[]{SKEncodedImageFormat.Jpeg,SKEncodedImageFormat.Webp}){var file=Path.Combine(folder,"새 이미지."+(format==SKEncodedImageFormat.Jpeg?"jpg":"webp"));using var data=image.Encode(format,100);File.WriteAllBytes(file,data.ToArray());imageInputs.Add(file);}}
        var imported=(Task)Call("ImportAsync",(object)imageInputs.ToArray())!;var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(15)};timer.Tick+=(_,_)=>{if(imported.IsCompleted){timer.Stop();frame.Continue=false;}};timer.Start();Dispatcher.PushFrame(frame);imported.GetAwaiter().GetResult();Pump();
        var jobs=F<System.Collections.ObjectModel.ObservableCollection<ImageJob>>("_jobs");check(jobs.Count==4&&jobs.Where(j=>imageInputs.Contains(j.SourcePath)).All(j=>j.Width==64&&j.Height==48),"main importer accepts JPEG and static WebP with their original dimensions");
        C<ListBox>("FileList").SelectedItem=jobs.Last();Pump();check(F<ImageJob>("_current").SourcePath.EndsWith(".webp")&&C<PreviewControl>("Preview").Viewport.Scale>0,"WebP can be selected and edited in the original preview");
        check(new ThumbnailConverter().Convert(imageInputs[1],typeof(object),null!,System.Globalization.CultureInfo.InvariantCulture) is not null,"WebP thumbnail uses the shared graphic decoder");
    }
}
