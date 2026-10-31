using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LiveCG.App;
using LiveCG.Core;
using LiveCG.Testing;
using SkiaSharp;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var root=Path.GetFullPath(args.FirstOrDefault()??Environment.CurrentDirectory);
        var folder=Path.Combine(root,"artifacts","v010-ui");Directory.CreateDirectory(folder);
        Environment.SetEnvironmentVariable("LIVECG_DATA_DIR",Path.Combine(folder,"user-data"));
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        Environment.SetEnvironmentVariable("LIVECG_DISABLE_WORKSPACE_RESTORE","1");
        var app=new App();app.InitializeComponent();var checks=0;var failures=new List<string>();
        if(args.Contains("--window-repro"))return WindowStateChecks.Reproduce(root,args.Contains("--minimal"));
        void Check(bool value,string label){checks++;Console.WriteLine((value?"PASS ":"FAIL ")+label);if(!value)failures.Add(label);}
        object? Call(MainWindow window,string method,params object?[] values)
        {
            var info=typeof(MainWindow).GetMethod(method,BindingFlags.NonPublic|BindingFlags.Instance)!;
            var parameters=info.GetParameters().Select((p,i)=>i<values.Length?values[i]:p.DefaultValue).ToArray();
            return info.Invoke(window,parameters);
        }
        T Field<T>(MainWindow w,string name)=>(T)typeof(MainWindow).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(w)!;
        T Control<T>(MainWindow w,string name)=>(T)w.FindName(name);
        void Pump()=>Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
        void Layout(MainWindow w,double width=1568,double height=960)
        {var c=(FrameworkElement)w.Content;c.Width=width-28;c.Height=height-28;c.Measure(new Size(width,height));c.Arrange(new Rect(0,0,width,height));c.UpdateLayout();Pump();}
        void Snapshot(MainWindow w,string file,double width=1568,double height=960)
        {
            Layout(w,width,height);var bitmap=new RenderTargetBitmap((int)width,(int)height,96,96,PixelFormats.Pbgra32);
            var drawing=new DrawingVisual();using(var dc=drawing.RenderOpen())
            {dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(12,21,32)),null,new Rect(0,0,width,height));dc.DrawRectangle(new VisualBrush((FrameworkElement)w.Content),null,new Rect(14,14,width-28,height-28));}
            bitmap.Render(drawing);
            var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var output=File.Create(Path.Combine(folder,file));png.Save(output);
        }
        static IEnumerable<T> Children<T>(DependencyObject root)where T:DependencyObject
        {for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);if(child is T typed)yield return typed;foreach(var next in Children<T>(child))yield return next;}}
        void Await(Task task)
        {var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(15)};timer.Tick+=(_,_)=>{if(task.IsCompleted){timer.Stop();frame.Continue=false;}};timer.Start();Dispatcher.PushFrame(frame);task.GetAwaiter().GetResult();}
        MainWindow? window=null;
        try
        {
            DocumentStore.Save(DocumentStore.DefaultsPath,new AppDefaults{Video=new(){Fps=60,EndHold=1},Effect=new(){Kind=EffectKind.Sweep,Strength=StrengthLevel.Weak},ClipDuration=1});
            window=new MainWindow{WindowStartupLocation=WindowStartupLocation.Manual,Left=-12000,Top=-12000,ShowInTaskbar=false,ShowActivated=false};window.Show();Layout(window);
            Check(window.FindName("TimelineEditor") is not null,"professional track timeline exists");
            Check(window.FindName("RegionX") is null&&window.FindName("RegionY") is null,"numeric spatial coordinates removed");
            Check(window.FindName("IntensitySlider") is null&&Control<ComboBox>(window,"StrengthBox").Items.Count==5,"strength has exactly five choices");
            Check(window.FindName("ZoomInButton") is not null&&window.FindName("ZoomOutButton") is not null,"precision zoom buttons exist");
            Check(window.FindName("VideoDurationBox") is null,"manual video duration removed");
            Check(Control<System.Windows.Controls.Primitives.UniformGrid>(window,"EffectsGrid").Children.Count==20,"20 effect thumbnails available");
            Check(typeof(MainWindow).GetMethod("Redetect",BindingFlags.NonPublic|BindingFlags.Instance) is null,"automatic detection UI handler removed");
            Check(Control<ComboBox>(window,"FpsBox").Text=="60","saved default fps restored");
            var path=TestFixtures.Create(folder);var secondPath=Path.Combine(folder,"두 번째 PNG.png");File.Copy(path,secondPath,true);
            var job=new ImageJob{SourcePath=path,Width=960,Height=540};
            var secondJob=new ImageJob{SourcePath=secondPath,Width=960,Height=540};
            window.ApplyDocument(new(){Jobs=[job,secondJob],Video=new(){Fps=30,EndHold=.5}});Layout(window);
            Check(job.Regions.Count==0,"PNG begins without automatic regions");
            Check(Control<ComboBox>(window,"FpsBox").Text=="30","loaded project keeps its fps over defaults");
            var preview=Control<PreviewControl>(window,"Preview");var timeline=Control<TimelineControl>(window,"TimelineEditor");
            SKPoint Screen(float x,float y)=>preview.Viewport.ToScreen(new SKPoint(x,y));
            preview.BeginPointer(Screen(45,40));preview.MovePointer(Screen(445,140));
            Check(job.Regions.Count==0,"draft region does not commit before mouse up");
            preview.EndPointer();Pump();
            Check(job.Regions.Count==1&&preview.SelectedRegion?.Id==job.Regions[0].Id,"mouse drag creates and clearly selects first region");
            var first=job.Regions[0];
            Check(Control<TextBlock>(window,"SelectedRegionText").Text.Contains("✓ 01"),"selected area number and check are explicit");
            Call(window,"AddRegion",window,new RoutedEventArgs());preview.BeginPointer(Screen(640,185));preview.MovePointer(Screen(900,345));preview.EndPointer();Pump();
            Check(job.Regions.Count==2&&preview.SelectedRegion?.Id==job.Regions[0].Id,"new manual area is the selected top layer");
            var second=job.Regions[0];
            Call(window,"AddRegion",window,new RoutedEventArgs());preview.BeginPointer(Screen(440,400));preview.MovePointer(Screen(690,480));preview.CancelGesture();
            Check(job.Regions.Count==2,"ESC cancels unfinished region without creating a ghost");
            var anchor=Screen(760,260);var beforeAnchor=preview.Viewport.ToImage(anchor);preview.ZoomAt(4,anchor);
            var afterAnchor=preview.Viewport.ToImage(anchor);
            Check(Math.Abs(beforeAnchor.X-afterAnchor.X)<.001&&Math.Abs(beforeAnchor.Y-afterAnchor.Y)<.001,"zoom keeps mouse anchor at exact source pixel");
            var before=second.Bounds;var center=preview.Viewport.ToScreen(new SKPoint(770,265));
            preview.BeginPointer(center);preview.MovePointer(new(center.X+12,center.Y+6));preview.EndPointer();
            Check(Math.Abs(second.Bounds.X-before.X-12/preview.Viewport.Scale/960)<.00001,"drag movement remains accurate while zoomed");
            var moved=second.Bounds;var rect=preview.Viewport.ToScreen(second.Bounds);
            preview.BeginPointer(new(rect.Right,rect.Bottom));preview.MovePointer(new(rect.Right+10,rect.Bottom+10));preview.CancelGesture();
            Check(second.Bounds==moved,"ESC rolls back in-progress resize");
            Check(PreviewControl.Handles(rect).Length==8,"eight visible resize handles");
            for(var handle=0;handle<8;handle++)
            {
                var resized=RegionGeometry.Resize(new(.2,.2,.3,.3),handle,.01,.01,960,540);
                Check(resized!=new NormalizedRect(.2,.2,.3,.3)&&resized.X>=0&&resized.Y>=0,"resize handle "+handle+" changes the intended bounds");
            }
            Control<CheckBox>(window,"RegionLockCheck").IsChecked=true;Pump();var locked=second.Bounds;preview.BeginPointer(preview.Viewport.ToScreen(new SKPoint((float)((locked.X+locked.Width/2)*960),(float)((locked.Y+locked.Height/2)*540))));
            preview.MovePointer(new(center.X+30,center.Y+30));preview.EndPointer();Check(second.Bounds==locked,"locked region does not accidentally move");Control<CheckBox>(window,"RegionLockCheck").IsChecked=false;Pump();
            preview.NativeSize();Check(Math.Abs(preview.Viewport.Scale*VisualTreeHelper.GetDpi(preview).DpiScaleX-1)<.0001,"native 100 percent is source-pixel accurate");
            preview.Fit(true);preview.ZoomAt(2);preview.Fit(false);
            Check(preview.Viewport.Zoom==1&&preview.Viewport.Pan==default,"original-view reset clears zoom and pan");
            var regions=Control<ListBox>(window,"RegionList");regions.SelectedItem=first;Pump();
            Check(Field<EffectRegion?>(window,"_region")?.Id==first.Id,"region-list selection switches the active mouse region");
            Call(window,"SelectEffect",EffectKind.RiseIn);Call(window,"AddClip",window,new RoutedEventArgs());Call(window,"SelectEffect",EffectKind.Glow);Pump();
            Check(first.Clips.Count==2&&first.Clips[0].Options.Kind==EffectKind.RiseIn&&first.Clips[1].Options.Kind==EffectKind.Glow,"one area supports sequential different effects");
            Check(first.Clips[1].Start==first.Clips[0].End,"new clip appends at previous end");
            var strength=Control<ComboBox>(window,"StrengthBox");strength.SelectedIndex=ProductInfo.StrengthIndex(StrengthLevel.Strong);Pump();
            Check(first.Clips[1].Options.Strength==StrengthLevel.Strong&&first.Clips[0].Options.Strength==StrengthLevel.Weak,"strength changes only the selected clip");
            var clip=first.Clips[1];var clipRect=timeline.ClipRect(first,clip);
            Control<CheckBox>(window,"SnapCheck").IsChecked=false;
            timeline.BeginPointer(new(clipRect.MidX(),clipRect.Top+14));timeline.MovePointer(new(clipRect.MidX()+timeline.PixelsPerSecond*.5,clipRect.Top+14));timeline.EndPointer();Pump();
            Check(Math.Abs(clip.Start-1.5)<1e-8,"timeline body drag changes start time");
            clipRect=timeline.ClipRect(first,clip);
            timeline.BeginPointer(new(clipRect.Right-2,clipRect.Top+14));timeline.MovePointer(new(clipRect.Right-2+timeline.PixelsPerSecond*.4,clipRect.Top+14));timeline.EndPointer();Pump();
            Check(Math.Abs(clip.Duration-1.4)<1e-8,"timeline right handle adjusts duration");
            Check(Control<TextBlock>(window,"AutoDurationText").Text.Contains("3.40"),"output duration updates immediately with timeline");
            var start=clip.Start;var length=clip.Duration;clipRect=timeline.ClipRect(first,clip);
            timeline.BeginPointer(new(clipRect.Left+2,clipRect.Top+14));timeline.MovePointer(new(clipRect.Left+20,clipRect.Top+14));timeline.CancelGesture();Pump();
            Check(clip.Start==start&&clip.Duration==length,"timeline ESC restores start and duration");
            timeline.BeginPointer(new(TimelineControl.LabelWidth+2.5*timeline.PixelsPerSecond,8));timeline.EndPointer();
            Check(Math.Abs(preview.Time-2.5)<1e-8&&preview.ShowEffects,"ruler click seeks preview and effect state");
            Call(window,"AddClipAtCursor",window,new RoutedEventArgs());Check(first.Clips.Count==3&&Math.Abs(first.Clips[2].Start-2.5)<1e-8,"new effect can be placed at playhead, including overlap");
            job.Progress=42.5;job.Status="출력 중";Pump();
            var bar=Children<ProgressBar>(Control<ListBox>(window,"FileList")).FirstOrDefault();
            Check(bar is not null&&Math.Abs(bar.Value-42.5)<1e-8,"left-file progress bar updates live without refreshing selection");
            Check(preview.SelectedRegion?.Id==first.Id,"progress and property edits preserve selected region");
            var fps=Control<ComboBox>(window,"FpsBox");fps.Text="24";fps.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));Pump();
            Call(window,"SaveDefaults",window,new RoutedEventArgs());
            Check(Math.Abs(first.Clips[2].Duration-1.4)<1e-8,"saving defaults after fps change does not rewrite existing clip timing");
            var saved=DocumentStore.Load<AppDefaults>(DocumentStore.DefaultsPath);
            Check(saved.Video.Fps==24&&saved.Effect.Strength==StrengthLevel.Strong&&Math.Abs(saved.ClipDuration-1.4)<1e-8,"defaults save video and selected clip options");
            var reopened=new MainWindow();Pump();
            Check(Control<ComboBox>(reopened,"FpsBox").Text=="24"&&Control<ComboBox>(reopened,"StrengthBox").SelectedIndex==ProductInfo.StrengthIndex(StrengthLevel.Strong),"new application restores three-level defaults");
            typeof(MainWindow).GetField("_dirty",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(reopened,false);reopened.Close();
            WorkflowUiChecks.Run(window,folder,Check);
            StudioUiChecks.Run(window,folder,Check);
            PrecisionUiChecks.Run(window,folder,Check);
            LayerUiChecks.Run(window,folder,Check);
            GroupUiChecks.Run(window,folder,Check);
            DirectUiChecks.Run(window,folder,Check);
            WindowStateChecks.Run(window,Check);
            PlaybackUiChecks.Run(window,folder,Check);
            BroadcastUiChecks.Run(window,folder,Check);
            SoftUiChecks.Run(window,folder,Check);
            ContextUiChecks.Run(window,folder,Check);
            var demo=TestFixtures.Demo(path);window.ApplyDocument(new(){Jobs=[demo],Video=new(){Fps=30,EndHold=.9}});Layout(window);
            Control<CheckBox>(window,"SnapCheck").IsChecked=true;Control<CheckBox>(window,"RippleCheck").IsChecked=true;
            Control<ListBox>(window,"RegionList").SelectedItem=demo.Regions[3];Call(window,"Seek",3.55,true);Pump();
            Control<CheckBox>(window,"HideOverlaysCheck").IsChecked=true;Snapshot(window,"clean-preview.png");Control<CheckBox>(window,"HideOverlaysCheck").IsChecked=false;
            Control<TabControl>(window,"EffectsTabs").SelectedIndex=1;Control<CheckBox>(window,"ClipLoopCheck").IsChecked=true;Snapshot(window,"clip-properties.png");Control<TabControl>(window,"EffectsTabs").SelectedIndex=0;
            Snapshot(window,"timeline-editor.png");Snapshot(window,"timeline-minimum.png",1128,720);
            var editorRoot=(FrameworkElement)window.Content;var effectTabs=Control<TabControl>(window,"EffectsTabs");var timelineScroll=Control<ScrollViewer>(window,"TimelineScroll");
            Check(effectTabs.ActualHeight>100&&timelineScroll.TranslatePoint(new Point(timelineScroll.ActualWidth,0),editorRoot).X<=effectTabs.TranslatePoint(new Point(0,0),editorRoot).X,"minimum layout keeps effect library visible beside timeline");
            Check(window.Title.Contains("SunjooAn")&&window.Title.Contains(ProductInfo.Version),"creator and current version always displayed");
            var batch=Path.Combine(folder,"batch");Directory.CreateDirectory(batch);
            using(var tiny=new SKBitmap(64,48)){using(var c=new SKCanvas(tiny))c.Clear(SKColors.Transparent);using var image=SKImage.FromBitmap(tiny);using var data=image.Encode(SKEncodedImageFormat.Png,100);for(var i=0;i<50;i++)File.WriteAllBytes(Path.Combine(batch,$"자료 {i:00}.png"),data.ToArray());}
            var originalCount=Field<ObservableCollection<ImageJob>>(window,"_jobs").Count;var timer=Stopwatch.StartNew();var beats=0;
            var import=(Task)Call(window,"ImportAsync",(object)Directory.GetFiles(batch,"*.png"))!;
            Dispatcher.CurrentDispatcher.BeginInvoke(()=>{if(!import.IsCompleted)beats++;},DispatcherPriority.Send);Await(import);
            Check(Field<ObservableCollection<ImageJob>>(window,"_jobs").Count==originalCount+50&&(beats>0||timer.ElapsedMilliseconds<100),"50 PNG import remains responsive without OCR");
        }
        catch(Exception e){Console.WriteLine(e);failures.Add(e.ToString());}
        finally
        {
            if(window is not null){typeof(MainWindow).GetField("_dirty",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(window,false);window.Close();}
        }
        File.WriteAllText(Path.Combine(folder,"result.txt"),$"{checks-failures.Count}/{checks} passed\n"+string.Join("\n",failures));
        Console.WriteLine($"RESULT {checks-failures.Count}/{checks} passed");app.Shutdown();return failures.Count==0?0:1;
    }
}
internal static class RectExtensions{public static double MidX(this Rect r)=>(r.Left+r.Right)/2;}


