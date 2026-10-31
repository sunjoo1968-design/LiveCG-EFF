using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using LiveCG.App;
using LiveCG.Core;
using SkiaSharp;

internal static class PlaybackUiChecks
{
    public static void Run(MainWindow window,string folder,Action<bool,string> check)
    {
        T C<T>(string n)=>(T)window.FindName(n);T F<T>(string n)=>(T)typeof(MainWindow).GetField(n,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
        object? Call(string n,params object?[] v){var m=typeof(MainWindow).GetMethod(n,BindingFlags.Instance|BindingFlags.NonPublic)!;return m.Invoke(window,m.GetParameters().Select((p,i)=>i<v.Length?v[i]:p.DefaultValue).ToArray());}
        void Set(string n,object value)=>typeof(MainWindow).GetField(n,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(window,value);
        void Pump()=>Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
        var png=Path.Combine(folder,"sections.png");using(var b=new SKBitmap(160,100)){b.Erase(SKColors.Transparent);using(var c=new SKCanvas(b))using(var p=new SKPaint{Color=SKColors.Red})c.DrawRect(40,30,80,40,p);using var image=SKImage.FromBitmap(b);using var bytes=image.Encode(SKEncodedImageFormat.Png,100);File.WriteAllBytes(png,bytes.ToArray());}
        var job=new ImageJob{SourcePath=png,Width=160,Height=100,Regions=[new(){Bounds=new(.25,.3,.5,.4),Background=BackgroundMode.Transparent,Clips=[new(){Duration=.5,Options=new(){Kind=EffectKind.FadeIn}},new(){Start=.5,Duration=1.5,Options=new(){Kind=EffectKind.Zoom}}]}]};
        window.ApplyDocument(new(){Jobs=[job],Video=new(){Fps=30,EndHold=0}});C<CheckBox>("AutoFocusCheck").IsChecked=false;if(window.IsEditorFocused)Call("ToggleFocusMode",window,new RoutedEventArgs());Pump();
        ImageJob Job()=>F<ImageJob>("_current");var timeline=C<TimelineControl>("TimelineEditor");
        check(!Job().Playback.Enabled,"old project opens without silently enabling broadcast sections");
        check(C<ComboBox>("OutputKindBox").Items.Count==3&&window.FindName("SectionsButton") is not null,"UI exposes both output types and section settings");
        Job().Playback=new(){Enabled=true,IntroEnd=.5,LoopEnd=2,ExitDuration=1};Call("RefreshTimeline");Call("MarkEdited");Call("ResetHistory");
        Point At(double t)=>new(TimelineControl.LabelWidth+t*timeline.PixelsPerSecond,10);
        timeline.BeginPointer(At(.5));timeline.MovePointer(At(.8),true);timeline.EndPointer();
        check(Math.Abs(Job().Playback.IntroEnd-.8)<.001,"dragging the blue boundary changes entrance duration");
        Call("UndoEdit",window,new RoutedEventArgs());check(Job().Playback.IntroEnd==.5,"section boundary drag is one undoable gesture");Call("RedoEdit",window,new RoutedEventArgs());check(Math.Abs(Job().Playback.IntroEnd-.8)<.001,"redo restores section boundary");
        var before=JsonSerializer.Serialize(Job().Playback);timeline.BeginPointer(At(2));timeline.MovePointer(At(2.5),true);timeline.CancelGesture();check(JsonSerializer.Serialize(Job().Playback)==before,"ESC/cancel restores the section before dragging");
        timeline.BeginPointer(At(.8));timeline.MovePointer(At(3),true);timeline.EndPointer();check(Job().Playback.IntroEnd<Job().Playback.LoopEnd,"entrance boundary cannot cross the repeat boundary");
        C<CheckBox>("SnapCheck").IsChecked=true;timeline.BeginPointer(At(Job().Playback.IntroEnd));timeline.MovePointer(At(.52));timeline.EndPointer();check(Math.Abs(Job().Playback.IntroEnd-.5)<.001,"section snap uses existing effect starts and ends");
        Job().Playback.IntroEnd=.5;Call("RefreshTimeline");timeline.BeginPointer(At(3));timeline.MovePointer(At(3.5),true);timeline.EndPointer();check(Job().Playback.ExitDuration==1.5&&TimelineRules.Resolve(Job(),new(){Fps=30,EndHold=0}).ActualDuration==3.5,"last section handle adjusts exit and automatic video duration");
        using(var image=SKBitmap.Decode(png))using(var scene=new EffectScene(image)){var plan=PlaybackPlan.Create(Job(),new(){Fps=30,EndHold=0});Call("Seek",plan.Duration-1d/30,true);check(C<PreviewControl>("Preview").OutputOpacity<.001,"native effect preview uses the same final transparent exit");}
        C<CheckBox>("BroadcastPreviewCheck").IsChecked=true;Call("Rewind",window,new RoutedEventArgs());Call("TogglePlayback",window,new RoutedEventArgs());Set("_playStart",5d);Call("TickPlayback");
        check(F<double>("_time")>=.5&&F<double>("_time")<2&&F<PlaybackClock>("_broadcastClock") is not null,"native broadcast preview skips repeated entrance and remains in the loop");
        Call("ExitPlayback",window,new RoutedEventArgs());check(F<PlaybackClock>("_broadcastClock").ExitRequested,"native preview exit button requests the tail");Call("StopPlayback");C<CheckBox>("BroadcastPreviewCheck").IsChecked=false;
        Call("SetBusy",true);check(C<Button>("CancelButton").IsEnabled&&!C<ComboBox>("OutputKindBox").IsEnabled&&C<Grid>("EditorGrid").IsEnabled,"output cancellation stays clickable while editing is disabled");Call("SetBusy",false);
        var dialog=new PlaybackWindow(Job(),new(){Fps=30},.75){Owner=window,Left=-15000,Top=-15000,WindowStartupLocation=WindowStartupLocation.Manual,ShowActivated=false,ShowInTaskbar=false};
        try{dialog.Show();Pump();dialog.Values["IntroEnd"].EditValue(.9);dialog.Values["LoopEnd"].EditValue(2.4);check(Math.Abs(dialog.Sections.IntroEnd-.9)<.001&&Math.Abs(dialog.Sections.LoopEnd-2.4)<.001,"section dialog supports mouse sliders for both boundaries");StudioUiChecks.Capture(dialog,Path.Combine(folder,"playback-options.png"),560,660);}finally{dialog.Close();}
        C<ComboBox>("OutputKindBox").SelectedIndex=2;Pump();check(F<VideoOptions>("_video").Output==OutputKind.Both,"both-output choice updates project options");
        var output=Path.Combine(folder,"dual-export-"+Guid.NewGuid().ToString("N"));C<TextBox>("OutputFolderBox").Text=output;
        var task=(Task)Call("ExportAsync",new List<ImageJob>{Job()})!;var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(15)};timer.Tick+=(_,_)=>{if(task.IsCompleted){timer.Stop();frame.Continue=false;}};timer.Start();Dispatcher.PushFrame(frame);task.GetAwaiter().GetResult();
        check(File.Exists(Path.Combine(output,"sections.webm"))&&BrowserExporter.IsPackage(Path.Combine(output,"sections.obs")),"main output command produces both original-named WebM and browser folder");
        check(Job().Status=="출력 완료"&&Job().Detail.Contains(".webm")&&Job().Detail.Contains(".obs"),"file progress reports both completed outputs");
        check(C<ListBox>("FileList").ContextMenu?.Items.OfType<MenuItem>().Any(i=>i.Header.ToString()!.Contains("이 파일 출력"))==true,"right-click selective output uses the shared output pipeline");
        C<ComboBox>("ExportScopeBox").SelectedIndex=2;check(((List<ImageJob>)Call("ExportTargets")!).Single()==Job(),"current-file scope stays independent of output format");
        Call("Seek",1d,true);C<CheckBox>("HideOverlaysCheck").IsChecked=true;StudioUiChecks.Capture(window,Path.Combine(folder,"playback-timeline.png"),1580,1040);StudioUiChecks.Capture(window,Path.Combine(folder,"playback-minimum.png"),1160,780);
        var scroll=C<ScrollViewer>("TimelineScroll");check(scroll.ActualHeight>TimelineControl.RulerHeight+25,"minimum layout keeps a usable track area under section controls");
        C<CheckBox>("HideOverlaysCheck").IsChecked=false;
    }
}
