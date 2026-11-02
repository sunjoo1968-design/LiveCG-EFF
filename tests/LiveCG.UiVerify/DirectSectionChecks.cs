using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using LiveCG.App;
using LiveCG.Core;
using SkiaSharp;

internal static class DirectSectionChecks
{
    public static void Run(MainWindow w,string folder,Action<bool,string> check)
    {
        object? Call(string name,params object?[] args){var m=typeof(MainWindow).GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public)!;return m.Invoke(w,m.GetParameters().Select((p,i)=>i<args.Length?args[i]:p.DefaultValue).ToArray());}
        T C<T>(string name)=>(T)w.FindName(name);ImageJob Job()=>(ImageJob)typeof(MainWindow).GetField("_current",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(w)!;
        void Pump()=>Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
        var file=Path.Combine(folder,"직접 구간 글자.png");using var source=new SKBitmap(240,120);source.Erase(SKColors.Black);using(var canvas=new SKCanvas(source))using(var p=new SKPaint{Color=SKColors.White,Style=SKPaintStyle.Stroke,StrokeWidth=6,IsAntialias=true}){canvas.DrawCircle(50,50,25,p);canvas.DrawRect(100,20,50,60,p);}SourceImage.SavePng(source,file);
        var region=new EffectRegion{Name="글자 ㅇㅁ",Bounds=new(0,0,1,1),ContentOnly=true,Background=BackgroundMode.Solid,BackgroundColor="#000000",Clips=[new(){Duration=1,Options=new(){Kind=EffectKind.ZoomIn}},new(){Start=1,Duration=2,Options=new(){Kind=EffectKind.Zoom}}]};
        w.ApplyDocument(new(){Jobs=[new(){SourcePath=file,Width=240,Height=120,Regions=[region],Playback=PlaybackSections.NewDefault()}],Video=new(){Fps=30,EndHold=0}});Call("ResetHistory");Pump();var timeline=C<TimelineControl>("TimelineEditor");Point At(double t)=>new(TimelineControl.LabelWidth+t*timeline.PixelsPerSecond,10);
        timeline.MovePointer(At(1));check(timeline.Cursor==Cursors.SizeWE,"automatic section boundaries expose a resize cursor");
        timeline.BeginPointer(At(1));timeline.MovePointer(At(1.4),true);timeline.EndPointer();Pump();check(!Job().Playback.ByEffects&&Math.Abs(Job().Playback.IntroEnd-1.4)<.001&&C<ComboBox>("SectionModeBox").SelectedIndex==1,"dragging an automatic boundary freezes resolved values and switches to direct mode");
        check(Job().Regions[0].Clips[1].Start==1&&Job().Regions[0].Clips[0].Duration==1,"section dragging leaves authored effect clips untouched");Call("UndoEdit",w,new RoutedEventArgs());Pump();check(Job().Playback.ByEffects&&SectionRules.Resolve(Job(),new(){Fps=30}).IntroEnd==1,"section undo restores automatic mode as well as boundaries");
        timeline.BeginPointer(At(1));timeline.MovePointer(At(1.7),true);timeline.CancelGesture();Pump();check(Job().Playback.ByEffects,"canceling an automatic section gesture restores its original mode");
        timeline.BeginPointer(At(2));timeline.MovePointer(At(3),true);timeline.EndPointer();Pump();check(Math.Abs(Job().Playback.IntroEnd-2)<.001&&Math.Abs(Job().Playback.LoopEnd-4)<.001,"dragging the repeat body moves both boundaries without changing its duration");
        C<ComboBox>("SectionModeBox").SelectedIndex=0;Pump();check(Job().Playback.ByEffects&&PlaybackPlan.Create(Job(),new(){Fps=30}).LoopEndFrame==90,"effect-fit selection restores automatically resolved section timing");
        timeline.BeginPointer(At(1));timeline.MovePointer(At(7),true);timeline.EndPointer();Pump();check(Math.Abs(Job().Playback.LoopEnd-Job().Playback.IntroEnd-1d/30)<.001,"entrance resize cannot collapse the repeat below one frame");
        C<ComboBox>("SectionModeBox").SelectedIndex=0;Pump();timeline.BeginPointer(At(3));timeline.MovePointer(At(3.04));timeline.EndPointer();Pump();check(Math.Abs(Job().Playback.LoopEnd-3)<.001,"section boundaries snap to existing effect ends");
        check(w.FindName("ClipList") is null&&w.FindName("RegionRoleBox") is null,"redundant effect list and parallel group-role editor are removed");
        C<ComboBox>("PlaybackViewBox").SelectedIndex=0;Pump();check(C<CheckBox>("BroadcastPreviewCheck").IsChecked==true&&C<CheckBox>("LoopPreviewCheck").Visibility==Visibility.Collapsed,"OBS preview mode shares playback state and hides irrelevant edit-repeat options");C<ComboBox>("PlaybackViewBox").SelectedIndex=1;Pump();check(C<CheckBox>("BroadcastPreviewCheck").IsChecked==false&&C<CheckBox>("LoopPreviewCheck").Visibility==Visibility.Visible,"editing preview mode restores its own repeat control");
        C<ComboBox>("AreaSeparationTarget").SelectedIndex=1;Pump();check(Job().Regions[0].SeparationTarget==SeparationTarget.Text&&!C<CheckBox>("AreaSoftEnabled").IsEnabled,"text target is immediately available outside clip properties and disables stroke-softening");
        C<ComboBox>("AreaContentMode").SelectedIndex=0;Pump();check(!Job().Regions[0].ContentOnly&&C<CheckBox>("AreaSoftEnabled").IsEnabled,"returning to whole-region effects immediately restores its own softness controls");C<ComboBox>("AreaContentMode").SelectedIndex=1;Pump();
        var textRegion=Job().Regions[0];var dialog=new ObjectSelectionWindow(source,textRegion){Owner=w,WindowStartupLocation=WindowStartupLocation.Manual,Left=-16000,Top=-16000,ShowActivated=false,ShowInTaskbar=false};
        try{dialog.Show();Pump();check(dialog.StrokeProtectionActive&&dialog.Canvas.Buffer.Alpha[50*240+50]==0,"precision editor opens with closed text counters already removed");var before=(byte[])dialog.Canvas.Buffer.Alpha.Clone();dialog.ContractOnePixel();check(before.SequenceEqual(dialog.Canvas.Buffer.Alpha),"text mode refuses direct contour contraction");dialog.ApplySolidCleanup();check(before.SequenceEqual(dialog.Canvas.Buffer.Alpha),"text cleanup preset cannot invoke the old one-pixel erosion");check(!dialog.FitBoundsCheck.IsEnabled,"text mode preserves the selected bounds including faint edge pixels");check(dialog.PrepareResult()&&dialog.SelectedCutout?.ForegroundPng.Length>0,"precision text result persists both matte and corrected edge colors");Call("Notify","구간 직접 조절 · 글자 획 보존");StudioUiChecks.Capture(dialog,Path.Combine(folder,"text-precision-v110.png"),1120,800);}finally{dialog.Close();}
        var manual=textRegion.Copy(false);manual.Cutout=new MaskBuffer(240,120,Enumerable.Repeat((byte)255,240*120).ToArray()).Encode();var holes=new ObjectSelectionWindow(source,manual){Owner=w,WindowStartupLocation=WindowStartupLocation.Manual,Left=-16000,Top=-16000,ShowActivated=false,ShowInTaskbar=false};
        try{holes.Show();Pump();check(holes.Canvas.Buffer.Alpha[50*240+50]==255,"saved manual text masks are preserved until an explicit calculation or hole edit");holes.Canvas.PendingRemoval=TextSeparation.HoleCandidate(source,holes.Canvas.Buffer,50,50,SKColors.Black);holes.ApplyHoleRemoval();check(holes.Canvas.Buffer.Alpha[50*240+50]==0&&holes.Canvas.Buffer.Alpha[50*240+25]>0,"hole candidate application clears only background and preserves the ring stroke");holes.Undo();check(holes.Canvas.Buffer.Alpha[50*240+50]==255,"hole edits support undo without destroying the saved manual mask");}finally{holes.Close();}
        C<ComboBox>("SectionModeBox").SelectedIndex=0;Call("Notify","자동 구간도 바로 드래그 · 글자 분리 · 메뉴 통합");StudioUiChecks.Capture(w,Path.Combine(folder,"direct-editor-v110.png"),1600,1040);StudioUiChecks.Capture(w,Path.Combine(folder,"direct-editor-narrow-v110.png"),1160,780);
    }
}
