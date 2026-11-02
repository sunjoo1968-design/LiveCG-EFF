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

internal static class WorkflowUiChecks
{
    public static void Run(MainWindow window,string folder,Action<bool,string> check)
    {
        T C<T>(string name)=>(T)window.FindName(name);
        T Field<T>(string name)=>(T)typeof(MainWindow).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
        object? Call(string method,params object?[] values)
        {var info=typeof(MainWindow).GetMethod(method,BindingFlags.NonPublic|BindingFlags.Instance)!;return info.Invoke(window,info.GetParameters().Select((p,i)=>i<values.Length?values[i]:p.DefaultValue).ToArray());}
        void Pump()=>Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
        void EditText(string name,string text){var control=C<ComboBox>(name);control.Text=text;control.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));Pump();}
        var path=Path.Combine(folder,"타임라인 예제.png");
        ImageJob Current()=>Field<ImageJob>("_current");
        EffectRegion Region()=>Field<EffectRegion>("_region");
        EffectClip Clip()=>Field<EffectClip>("_clip");
        void Select(int index){C<ListBox>("RegionList").SelectedItem=Current().Regions[index];Pump();}
        void Load()
        {window.ApplyDocument(new(){Jobs=[TestFixtures.Demo(path)],Video=new(){Output=OutputKind.Webm,Fps=30,EndHold=.9}});Pump();C<CheckBox>("HideOverlaysCheck").IsChecked=false;}
        Load();var preview=C<PreviewControl>("Preview");var timeline=C<TimelineControl>("TimelineEditor");
        var history=Field<EditHistory>("_history");
        check(!C<ComboBox>("QualityBox").IsEditable&&C<ComboBox>("QualityBox").Text.Contains("고화질"),"output quality uses readable labels without numeric input");
        C<ComboBox>("QualityBox").SelectedIndex=0;Pump();check(Field<VideoOptions>("_video").Crf==10,"highest-quality label maps to encoder quality");
        window.ApplyDocument(new(){Jobs=[TestFixtures.Demo(path)],Video=new(){Output=OutputKind.Webm,Crf=20}});Pump();
        check(C<ComboBox>("QualityBox").Text.Contains("이전")&&Field<VideoOptions>("_video").Crf==20,"legacy custom quality is preserved rather than silently remapped");
        Load();Select(3);
        var oldBounds=Region().Bounds;var rect=preview.Viewport.ToScreen(oldBounds);var point=new SKPoint(rect.MidX,rect.MidY);
        C<CheckBox>("HideOverlaysCheck").IsChecked=true;Pump();
        check(!preview.ShowOverlays&&!preview.BeginPointer(point),"clean preview hides selection and blocks accidental spatial edits");
        check(Region().Bounds==oldBounds,"clean preview leaves authored region untouched");
        C<CheckBox>("HideOverlaysCheck").IsChecked=false;C<CheckBox>("ShowAllRegionsCheck").IsChecked=false;Pump();
        check(preview.ShowOverlays&&!preview.ShowAllRegions,"normal editing shows active region without all labels");
        C<ComboBox>("ShapeBox").SelectedIndex=1;Call("ChangeSelectedShape",window,new RoutedEventArgs());Pump();check(Region().Shape==RegionShape.Ellipse,"explicit selected-shape command changes the actual effect mask");
        window.HandleEditorKey(Key.Z,ModifierKeys.Control,timeline);Pump();check(Region().Shape==RegionShape.Rectangle,"undo restores region shape");
        window.HandleEditorKey(Key.Z,ModifierKeys.Control|ModifierKeys.Shift,timeline);Pump();check(Region().Shape==RegionShape.Ellipse,"redo reapplies region shape");
        Load();Select(0);C<CheckBox>("SnapCheck").IsChecked=false;
        var before=Region().Bounds;rect=preview.Viewport.ToScreen(before);var start=new SKPoint(rect.MidX,rect.MidY);var count=history.UndoCount;
        preview.BeginPointer(start);for(var i=1;i<=8;i++)preview.MovePointer(new(start.X+i,start.Y));preview.EndPointer();Pump();
        check(history.UndoCount==count+1,"eight mouse move events create exactly one undo entry");
        window.HandleEditorKey(Key.Z,ModifierKeys.Control,preview);Pump();check(Region().Bounds==before,"undo restores mouse move bounds");
        window.HandleEditorKey(Key.Y,ModifierKeys.Control,preview);Pump();check(Region().Bounds!=before,"redo restores mouse move result");
        count=history.UndoCount;rect=preview.Viewport.ToScreen(Region().Bounds);preview.BeginPointer(new(rect.Right,rect.Bottom));preview.MovePointer(new(rect.Right+20,rect.Bottom+10));preview.CancelGesture();Pump();
        check(history.UndoCount==count,"cancelled resize does not create an undo entry");
        Call("SelectRegion",(object?)null);C<ComboBox>("ShapeBox").SelectedIndex=1;
        check(window.HandleEditorKey(Key.R,ModifierKeys.None,preview)&&preview.AddMode,"configured region-draw shortcut enters drawing mode");
        var a=preview.Viewport.ToScreen(new SKPoint(50,40));var b=preview.Viewport.ToScreen(new SKPoint(210,270));
        preview.BeginPointer(a);preview.MovePointer(b,true);preview.EndPointer();Pump();
        var circle=Region();check(circle.Shape==RegionShape.Ellipse&&Math.Abs(circle.Bounds.Width*960-circle.Bounds.Height*540)<.001,"Shift draw produces a true circle in source pixels");
        var manualCount=Current().Regions.Count;window.HandleEditorKey(Key.Z,ModifierKeys.Control,preview);Pump();check(Current().Regions.Count==manualCount-1,"undo removes a newly drawn region");
        check(!window.HandleEditorKey(Key.R,ModifierKeys.None,new TextBox())&&!window.HandleEditorKey(Key.V,ModifierKeys.Control,new TextBox()),"text editing keeps its own typing and clipboard shortcuts");
        check(!window.HandleEditorKey(Key.V,ModifierKeys.Control|ModifierKeys.Alt,timeline),"Ctrl+Alt combinations do not trigger clip paste");
        window.HandleEditorKey(Key.D2,ModifierKeys.None,preview);check(Region().Id==Current().Regions[1].Id,"number shortcut selects the intended region");
        window.HandleEditorKey(Key.Tab,ModifierKeys.None,timeline);check(Region().Id==Current().Regions[2].Id,"Tab in timeline advances region selection");
        Select(3);C<CheckBox>("ClipLoopCheck").IsChecked=true;EditText("LoopPeriodBox","0.5");
        check(Clip().Loop&&Clip().LoopPeriod==.5,"per-clip loop controls update the saved model");
        var copied=Clip().Copy(false);window.HandleEditorKey(Key.C,ModifierKeys.Control,timeline);Select(1);Call("Seek",4d,false);
        window.HandleEditorKey(Key.V,ModifierKeys.Control,timeline);Pump();
        check(Clip().Id!=copied.Id&&Clip().Start==4&&Clip().Loop&&Clip().LoopPeriod==.5&&Clip().Options.Kind==copied.Options.Kind,"cross-region paste creates an independent clip at playhead including loop options");
        var pastedCount=Region().Clips.Count;window.HandleEditorKey(Key.Z,ModifierKeys.Control,timeline);Pump();check(Region().Clips.Count==pastedCount-1,"paste can be undone");
        Load();Select(0);C<CheckBox>("RippleCheck").IsChecked=true;
        var followingStart=Region().Clips[1].Start;var oldEnd=Clip().End;EditText("ClipDurationBox","1.4");
        check(Math.Abs(Region().Clips[1].Start-followingStart-(Clip().End-oldEnd))<1e-8,"property duration edit moves following effects while preserving gap");
        window.HandleEditorKey(Key.Z,ModifierKeys.Control,timeline);Pump();check(Clip().Duration==.8&&Region().Clips[1].Start==followingStart,"duration and following movement undo together");
        EditText("ClipDurationBox","1.01");check(C<ComboBox>("ClipDurationBox").Text=="1","duration field displays the actual frame-snapped value");
        Load();Select(0);C<CheckBox>("SnapCheck").IsChecked=true;C<CheckBox>("RippleCheck").IsChecked=false;
        var target=Current().Regions[1].Clips[1].Start;var moving=Clip();var r=timeline.ClipRect(Region(),moving);
        timeline.BeginPointer(new(r.Left+r.Width/2,r.Top+14));timeline.MovePointer(new(r.Left+r.Width/2+(target+.04-moving.Start)*timeline.PixelsPerSecond,r.Top+14));
        check(Math.Abs(moving.Start-target)<1e-8&&timeline.SnapGuide==target,"timeline drag snaps to another region and displays its guide");timeline.EndPointer();
        r=timeline.ClipRect(Region(),moving);timeline.BeginPointer(new(r.Left+r.Width/2,r.Top+14));timeline.MovePointer(new(r.Left+r.Width/2+.04*timeline.PixelsPerSecond,r.Top+14),true);
        check(timeline.SnapGuide is null,"Alt bypass disables magnetic guide during actual timeline gesture");timeline.CancelGesture();
        r=timeline.ClipRect(Region(),Clip());timeline.BeginPointer(new(r.Left+r.Width/2,r.Top+14));Call("DeleteClip",window,new RoutedEventArgs());timeline.MovePointer(new(r.Left+40,r.Top+14));timeline.CancelGesture();
        check(Current().Regions[0].Clips.Count==1,"deleting during a drag cancels the gesture safely");
        var secondPath=Path.Combine(folder,"선택 출력 두번째.png");File.Copy(path,secondPath,true);
        ImageJob Short(string input)=>new(){SourcePath=input,Width=960,Height=540,Regions=[new(){Clips=[new(){Duration=.1,Options=new(){Kind=EffectKind.Glow}}]}]};
        window.ApplyDocument(new(){Jobs=[Short(path),Short(secondPath)],Video=new(){Output=OutputKind.Webm,Fps=10,EndHold=0}});Pump();
        var files=C<ListBox>("FileList");files.UnselectAll();files.SelectedIndex=1;Pump();C<ComboBox>("ExportScopeBox").SelectedIndex=1;
        var targets=(List<ImageJob>)Call("ExportTargets")!;check(targets.Count==1&&targets[0].SourcePath==secondPath,"selected export scope contains only selected PNGs");
        C<ComboBox>("ExportScopeBox").SelectedIndex=2;check(((List<ImageJob>)Call("ExportTargets")!).Single()==Current(),"current-file scope follows the active PNG");
        C<ComboBox>("ExportScopeBox").SelectedIndex=0;check(((List<ImageJob>)Call("ExportTargets")!).Count==2,"all-files export scope preserves batch output");
        var output=Path.Combine(folder,"selected-export");Directory.CreateDirectory(output);C<TextBox>("OutputFolderBox").Text=output;
        foreach(var name in new[]{"타임라인 예제.webm","선택 출력 두번째.webm"}){var file=Path.Combine(output,name);if(File.Exists(file))File.Delete(file);}
        var task=(Task)Call("ExportAsync",targets)!;var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(15)};
        timer.Tick+=(_,_)=>{if(task.IsCompleted){timer.Stop();frame.Continue=false;}};timer.Start();Dispatcher.PushFrame(frame);task.GetAwaiter().GetResult();Pump();
        check(File.Exists(Path.Combine(output,"선택 출력 두번째.webm"))&&!File.Exists(Path.Combine(output,"타임라인 예제.webm"))&&targets[0].Progress==100,"selected-file export produces only its WebM and completes progress");
        Load();Select(3);C<CheckBox>("ClipLoopCheck").IsChecked=true;EditText("LoopPeriodBox","0.5");C<CheckBox>("SnapCheck").IsChecked=false;
        Call("SaveDefaults",window,new RoutedEventArgs());var saved=DocumentStore.Load<AppDefaults>(DocumentStore.DefaultsPath);
        check(saved.Loop&&saved.LoopPeriod==.5&&!saved.Editor.Snap,"default save includes per-effect loop and editor options");
    }
}
