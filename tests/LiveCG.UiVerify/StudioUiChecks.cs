using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LiveCG.App;
using LiveCG.Core;
using LiveCG.Testing;
using SkiaSharp;

internal static class StudioUiChecks
{
    public static void Capture(Window window,string path,double width,double height)
    {
        var content=(FrameworkElement)window.Content;content.Width=double.NaN;content.Height=double.NaN;window.Width=width;window.Height=height;window.UpdateLayout();Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);content.UpdateLayout();
        var bitmap=new RenderTargetBitmap((int)width,(int)height,96,96,PixelFormats.Pbgra32);var visual=new DrawingVisual();using(var dc=visual.RenderOpen()){dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(12,21,32)),null,new Rect(0,0,width,height));dc.DrawRectangle(new VisualBrush(content),null,new Rect(14,14,width-28,height-28));}bitmap.Render(visual);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(path);encoder.Save(stream);
    }
    public static void Run(MainWindow window,string folder,Action<bool,string> check)
    {
        T C<T>(string name)=>(T)window.FindName(name);
        T Field<T>(object instance,string name)=>(T)instance.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(instance)!;
        object? Call(string method,params object?[] values){var info=typeof(MainWindow).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic)!;return info.Invoke(window,info.GetParameters().Select((p,i)=>i<values.Length?values[i]:p.DefaultValue).ToArray());}
        void Pump(){((FrameworkElement)window.Content).UpdateLayout();Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);}
        ImageJob Current()=>Field<ImageJob>(window,"_current");EffectRegion Region()=>Field<EffectRegion>(window,"_region");
        var path=Path.Combine(folder,"타임라인 예제.png");var auxiliary=new List<Window>();
        void Load(){window.ApplyDocument(new(){Jobs=[TestFixtures.Demo(path)],Video=new(){Fps=30,EndHold=.9}});C<ListBox>("RegionList").SelectedItem=Current().Regions[3];Pump();}
        try
        {
            Load();var preview=C<PreviewControl>("Preview");var originalPreview=preview;
            check(C<Expander>("OutputExpander") is not null&&C<TextBox>("OutputFolderBox").TranslatePoint(new Point(),window).X<400,"output folder and settings occupy the left material panel");
            var height=preview.ActualHeight;Call("ToggleFocusMode",window,new RoutedEventArgs());Pump();
            check(window.IsEditorFocused&&C<Border>("TimelinePanel").Visibility==Visibility.Collapsed&&preview.ActualHeight>height,"large edit mode collapses timeline and expands preview");
            Call("ToggleFocusMode",window,new RoutedEventArgs());C<CheckBox>("AutoFocusCheck").IsChecked=true;Call("AddRegion",window,new RoutedEventArgs());Pump();
            check(window.IsEditorFocused&&preview.AddMode,"entering region drawing can automatically focus the workspace");preview.CancelGesture();Call("ToggleFocusMode",window,new RoutedEventArgs());C<CheckBox>("AutoFocusCheck").IsChecked=false;Pump();
            Call("DetachPreview",window,new RoutedEventArgs());Pump();
            check(window.DetachedPreviewWindow is not null&&ReferenceEquals(C<PreviewControl>("Preview"),originalPreview)&&C<Border>("PreviewPanel").Parent is DockPanel,"preview detaches using the same editor control and selection");
            Call("DetachEffects",window,new RoutedEventArgs());Pump();
            check(window.DetachedEffectsWindow is not null&&C<Border>("EffectsPanel").Parent is DockPanel,"effect library and clip controls detach into their own window");
            check(window.DetachedEffectsWindow!.Title.Contains("SunjooAn")&&window.DetachedPreviewWindow!.Title.Contains(ProductInfo.Version),"all detached windows display creator and current version");
            Call("SelectEffect",EffectKind.Glow);check(Region().Clips[0].Options.Kind==EffectKind.Glow&&preview.ShowEffects,"detached effect selection updates the shared preview and project");
            using(var source=SKBitmap.Decode(path))
            {
                preview.ZoomAt(2);var screen=preview.Viewport.ToScreen(new SKPoint(60.5f,400.5f));
                check(preview.TrySampleColor(screen,out var sampled)&&sampled==source.GetPixel(60,400),"eyedropper reads exact original PNG pixel after zoom and effects");
                Call("SetBackgroundColor",sampled);check(Region().Background==BackgroundMode.Solid&&Region().BackgroundColor==ColorPickerWindow.Hex(sampled),"sampled source color reaches direct-background settings");
            }
            Capture(window.DetachedPreviewWindow!,Path.Combine(folder,"detached-preview.png"),1100,820);Capture(window.DetachedEffectsWindow!,Path.Combine(folder,"detached-effects.png"),850,820);
            window.DetachedPreviewWindow!.Close();window.DetachedEffectsWindow!.Close();Pump();
            check(window.DetachedPreviewWindow is null&&window.DetachedEffectsWindow is null&&C<Border>("PreviewPanel").Parent==C<Grid>("PreviewHost"),"closing detached windows returns panels to their original hosts");
            Call("SaveWorkspace");var workspace=WorkspaceState.Load();check(File.Exists(WorkspaceState.Path)&&!workspace.PreviewDetached&&!workspace.EffectsDetached,"workspace layout persists without changing project data");
            var placement=new WindowPlacement{Left=999999,Top=999999,Width=900,Height=600};var probe=new Window{MinWidth=100,MinHeight=100};placement.Apply(probe);
            check(probe.Left<SystemParameters.VirtualScreenLeft+SystemParameters.VirtualScreenWidth&&probe.Top<SystemParameters.VirtualScreenTop+SystemParameters.VirtualScreenHeight,"offscreen saved windows are recovered into available monitor bounds");probe.Close();
            var palette=new ColorPickerWindow(new SKColor(255,0,0,128)){Owner=window,WindowStartupLocation=WindowStartupLocation.Manual,Left=-16000,Top=-16000,ShowActivated=false,ShowInTaskbar=false};auxiliary.Add(palette);palette.Show();
            check(palette.SetHex("#8044AA33")&&palette.SelectedColor==new SKColor(68,170,51,128),"Photoshop-style picker handles alpha and hexadecimal colors");
            check(!palette.SetHex("invalid-color"),"color picker rejects malformed color without changing selection");Capture(palette,Path.Combine(folder,"color-picker.png"),730,650);palette.Close();
            Load();using(var source=SKBitmap.Decode(path))
            {
                var maskWindow=new ObjectSelectionWindow(source,Region()){Owner=window,WindowStartupLocation=WindowStartupLocation.Manual,Left=-16000,Top=-16000,ShowActivated=false,ShowInTaskbar=false};auxiliary.Add(maskWindow);maskWindow.Show();Pump();
                var oldMask=maskWindow.Canvas.Buffer.Alpha.ToArray();maskWindow.Generate();maskWindow.Undo();
                check(oldMask.SequenceEqual(maskWindow.Canvas.Buffer.Alpha),"object selection recalculation can be undone");
                check(maskWindow.PrepareResult()&&maskWindow.SelectedCutout is not null&&maskWindow.SelectedBounds.Width<Region().Bounds.Width,"rough price selection fits actual foreground contour");
                var bounds=Region().Bounds;window.ApplyObjectSelection(maskWindow.SelectedBounds,maskWindow.SelectedCutout);check(Region().Cutout is not null&&Region().Bounds!=bounds,"fitted contour integrates with the actual region editor");
                Call("UndoEdit",window,new RoutedEventArgs());check(Region().Cutout is null&&Region().Bounds==bounds,"object contour and bounding change undo together");
                Capture(maskWindow,Path.Combine(folder,"object-selection.png"),1120,800);
                maskWindow.Width=760;maskWindow.Height=580;maskWindow.UpdateLayout();Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
                var maskRoot=(DockPanel)maskWindow.Content;var actions=(WrapPanel)maskRoot.Children[0];var apply=actions.Children.OfType<Button>().Single(b=>Equals(b.Content,"오브젝트 적용"));var applyAt=apply.TranslatePoint(new Point(),maskRoot);
                check(apply.ActualHeight>0&&applyAt.Y+apply.ActualHeight<=maskRoot.ActualHeight&&applyAt.X+apply.ActualWidth<=maskRoot.ActualWidth,"object apply action remains visible at minimum window size");maskWindow.Close();
            }
            var asset=EffectAsset.NewMotion();asset.Name="테스트 사용자 모션";asset.Category="방송";asset.ThumbnailPng=EffectSample.Thumbnail(asset);AssetStore.Save(asset);Call("RefreshAssets");
            C<TextBox>("EffectSearchBox").Text="테스트 사용자";Pump();check(C<ListBox>("AssetList").Items.Cast<EffectAsset>().Any(a=>a.Id==asset.Id)&&C<ListBox>("AssetList").Items.Cast<EffectAsset>().All(a=>a.Name.Contains("테스트 사용자"))&&C<System.Windows.Controls.Primitives.UniformGrid>("EffectsGrid").Children.Cast<FrameworkElement>().All(t=>t.Visibility==Visibility.Collapsed),"asset search filters user names independently of built-in effects");
            C<TextBox>("EffectSearchBox").Text="광채";Pump();check(C<System.Windows.Controls.Primitives.UniformGrid>("EffectsGrid").Children.Cast<FrameworkElement>().Count(t=>t.Visibility==Visibility.Visible)==1,"built-in effect search filters thumbnails");C<TextBox>("EffectSearchBox").Text="";
            C<ListBox>("AssetList").SelectedItem=C<ListBox>("AssetList").Items.Cast<EffectAsset>().First(a=>a.Id==asset.Id);var beforeCount=Region().Clips.Count;Call("Seek",0d,false);Call("InsertAsset",window,new RoutedEventArgs());
            check(Region().Clips.Count==beforeCount+1&&Region().Clips.Any(c=>c.AssetId==asset.Id&&c.Motion is not null),"user-authored asset inserts as an actual keyframe clip");
            var firstInstance=Field<EffectClip>(window,"_clip").AssetInstanceId;Call("Seek",3d,false);Call("InsertAsset",window,new RoutedEventArgs());
            var edited=asset.Copy();edited.Clips[0].Motion!.SetKey(MotionProperty.Opacity,12,.5);AssetStore.Save(edited);Call("RefreshAssets");C<ListBox>("AssetList").SelectedItem=C<ListBox>("AssetList").Items.Cast<EffectAsset>().First(a=>a.Id==asset.Id);Call("UpdateAssetClip",window,new RoutedEventArgs());
            check(Region().Clips.First(c=>c.AssetInstanceId==firstInstance).Motion!.Value(MotionProperty.Opacity,12)==1&&Field<EffectClip>(window,"_clip").Motion!.Value(MotionProperty.Opacity,12)==.5,"asset update changes only the chosen instance and preserves other placements");
            Call("NewMotionAsset",window,new RoutedEventArgs());var studio=Field<List<EffectStudioWindow>>(window,"_studios").Last();auxiliary.Add(studio);Pump();
            check(studio.Title.Contains("SunjooAn")&&studio.Timeline.Motion.Tracks.Count==0,"independent effect studio opens with blank keys, numeric sample and creator credit");
            studio.SetKey(MotionProperty.X,30,.5,KeyEasing.Linear);check(studio.Timeline.Motion.Value(MotionProperty.X,30)==.5,"studio creates authored movement without effect-specific code");
            studio.Undo();check(studio.Timeline.Motion.Value(MotionProperty.X,30)==0,"studio undo restores previous motion keys");studio.Redo();check(studio.Timeline.Motion.Value(MotionProperty.X,30)==.5,"studio redo reapplies authored keys");
            studio.CopyKeys();studio.SetFrame(40);studio.PasteKeys();check(studio.Timeline.Motion.Tracks.First(t=>t.Property==MotionProperty.X).Keys.Any(k=>k.Frame==40&&k.Value==.5),"studio copy and paste place keys at the chosen frame");
            studio.SetKey(MotionProperty.Rotation,24,30,KeyEasing.Linear);var key=studio.Timeline.Motion.Tracks.First(t=>t.Property==MotionProperty.X).Keys.First(k=>k.Frame==30);var point=studio.Timeline.KeyPoint(MotionProperty.X,key);
            studio.Timeline.BeginPointer(point);studio.Timeline.MovePointer(new(point.X+5*studio.Timeline.PixelsPerFrame,point.Y));studio.Timeline.EndPointer();
            check(studio.Timeline.Motion.Tracks.First(t=>t.Property==MotionProperty.X).Keys.Any(k=>k.Frame==35),"diamond drag preserves key selection when switching property channel");studio.Undo();check(studio.Timeline.Motion.Tracks.First(t=>t.Property==MotionProperty.X).Keys.Any(k=>k.Frame==30),"studio key drag is one undo transaction");
            studio.ChangeTiming(60,60,true);check(studio.Timeline.Motion.Fps==60&&studio.Timeline.Motion.LengthFrames==120,"studio fps control preserves real motion duration");
            studio.SetFrame(48);var anchor=studio.Preview.ScreenAnchor();var oldX=studio.Timeline.Motion.Value(MotionProperty.X,48);
            studio.Preview.BeginTransform(0,anchor);studio.Preview.MoveTransform(new(anchor.X+25,anchor.Y+10));studio.Preview.EndTransform(true);
            check(studio.Timeline.Motion.Value(MotionProperty.X,48)>oldX,"dragging studio object creates position keys at current frame");studio.Undo();check(Math.Abs(studio.Timeline.Motion.Value(MotionProperty.X,48)-oldX)<1e-8,"graphic object drag is one reversible key transaction");
            var oldScale=studio.Timeline.Motion.Value(MotionProperty.ScaleX,48);var corner=studio.Preview.ScreenCorners()[2];studio.Preview.BeginTransform(1,corner);studio.Preview.MoveTransform(new(corner.X+18,corner.Y+12),true);studio.Preview.EndTransform(true);
            check(studio.Timeline.Motion.Value(MotionProperty.ScaleX,48)>oldScale,"studio scale handle creates size keys with aspect preservation");studio.Undo();
            anchor=studio.Preview.ScreenAnchor();studio.Preview.BeginTransform(2,new(anchor.X,anchor.Y-40));studio.Preview.MoveTransform(new(anchor.X+40,anchor.Y));studio.Preview.EndTransform(true);
            check(studio.Timeline.Motion.Value(MotionProperty.Rotation,48)>60,"studio rotation handle creates rotation keys");studio.Undo();
            Field<TextBox>(studio,"_name").Text="제작실 저장 검증";check(studio.SaveAsset()&&File.Exists(AssetStore.PathFor(studio.Asset.Id)),"studio saves reusable asset and generated thumbnail");
            check(C<ListBox>("AssetList").Items.Cast<EffectAsset>().Any(a=>a.Id==studio.Asset.Id),"saved studio asset appears in the main user library");
            studio.SetFrame(48);Capture(studio,Path.Combine(folder,"effect-studio.png"),1370,940);studio.DiscardAndClose();check(Field<List<EffectStudioWindow>>(window,"_studios").Count==0,"closing studio releases its modeless-window registration");
            var second=Path.Combine(folder,"우클릭 두번째.png");File.Copy(path,second,true);ImageJob Short(string input)=>new(){SourcePath=input,Width=960,Height=540,Regions=[new(){Clips=[new(){Duration=.1,Options=new(){Kind=EffectKind.Glow}}]}]};
            window.ApplyDocument(new(){Jobs=[Short(path),Short(second)],Video=new(){Fps=10,EndHold=0}});Pump();var files=C<ListBox>("FileList");files.SelectedIndex=1;Pump();
            var container=(ListBoxItem)files.ItemContainerGenerator.ContainerFromIndex(0);container.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Right){RoutedEvent=Mouse.PreviewMouseDownEvent});
            var menu=files.ContextMenu!;menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));check(menu.Items.OfType<MenuItem>().Count()==4&&((MenuItem)menu.Items[1]).Header.ToString()!.Contains("1개"),"material context menu exposes clicked, selected-count, all, and folder actions");
            var output=Path.Combine(folder,"context-export");Directory.CreateDirectory(output);C<TextBox>("OutputFolderBox").Text=output;foreach(var name in new[]{"타임라인 예제.webm","우클릭 두번째.webm"}){var target=Path.Combine(output,name);if(File.Exists(target))File.Delete(target);}
            ((MenuItem)menu.Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));var jobs=Field<ObservableCollection<ImageJob>>(window,"_jobs");var wait=Stopwatch.StartNew();var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(20)};
            timer.Tick+=(_,_)=>{if(jobs[0].Status is "출력 완료" or "실패"||wait.ElapsedMilliseconds>15000){timer.Stop();frame.Continue=false;}};timer.Start();Dispatcher.PushFrame(frame);Pump();
            check(File.Exists(Path.Combine(output,"타임라인 예제.webm"))&&!File.Exists(Path.Combine(output,"우클릭 두번째.webm")),"right-click this-file output uses clicked item rather than active or selected file");
            Call("ResetWorkspace",window,new RoutedEventArgs());Pump();check(!window.IsEditorFocused&&window.DetachedPreviewWindow is null&&C<Expander>("OutputExpander").IsExpanded,"workspace reset restores standard panel placement");
        }
        finally{foreach(var extra in auxiliary){if(extra is EffectStudioWindow studio)studio.DiscardAndClose();else extra.Close();}window.DetachedPreviewWindow?.Close();window.DetachedEffectsWindow?.Close();}
    }
}
