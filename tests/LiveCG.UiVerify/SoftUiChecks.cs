using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using LiveCG.App;
using LiveCG.Core;
using SkiaSharp;

internal static class SoftUiChecks
{
    public static void Run(MainWindow window,string folder,Action<bool,string> check)
    {
        void Pump()=>Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
        object? Call(string n,params object?[] values){var m=typeof(MainWindow).GetMethod(n,BindingFlags.Instance|BindingFlags.NonPublic)!;return m.Invoke(window,m.GetParameters().Select((p,i)=>i<values.Length?values[i]:p.DefaultValue).ToArray());}
        T Field<T>(string n)=>(T)typeof(MainWindow).GetField(n,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
        var r=new EffectRegion{Bounds=new(.2,.2,.4,.4),Softness=new(){Enabled=true,Width=16,Direction=SoftDirection.Outside},Clips=[new(){Duration=1,Options=new(){Kind=EffectKind.ZoomIn}},new(){Start=1,Duration=2,Options=new(){Kind=EffectKind.Zoom}}]};
        var job=new ImageJob{SourcePath=Path.Combine(folder,"타임라인 예제.png"),Width=960,Height=540,Regions=[r],Playback=new(){Enabled=true,ByEffects=true,ExitEnabled=false}};
        window.ApplyDocument(new(){Jobs=[job],Video=new(){EndHold=0}});Pump();var current=Field<ImageJob>("_current");var region=current.Regions[0];Call("SelectRegion",region,region.Clips[0],false);Call("ResetHistory");Pump();
        check(window.SoftValues.Count==7,"region softness exposes mouse controls for width, strength, four sides and content tolerance");
        window.SoftValues["Width"].BeginEdit();window.SoftValues["Width"].EditValue(24);window.SoftValues["Width"].EndEdit(true);Pump();
        check(Math.Abs(region.Softness.Width-24)<.01,"mouse softness edit updates selected region immediately");
        Call("UndoEdit",window,new RoutedEventArgs());Pump();current=Field<ImageJob>("_current");region=current.Regions[0];check(region.Softness.Width==16,"region softness participates in whole-editor undo");
        Call("RedoEdit",window,new RoutedEventArgs());Pump();current=Field<ImageJob>("_current");region=current.Regions[0];check(region.Softness.Width==24,"region softness redo restores edited width");
        var preview=(PreviewControl)window.FindName("Preview");preview.SelectedRegion=region;preview.AddMode=false;preview.ShowOverlays=true;preview.ShowSoftGuides=true;preview.SelectedMask=null;preview.Fit(false);Pump();
        var rect=region.Bounds.Pixels(960,540);var handle=preview.Viewport.ToScreen(new SKPoint(rect.Right+24,rect.MidY));
        check(preview.BeginPointer(handle),"soft boundary handle can be grabbed independently of region resize");
        preview.MovePointer(preview.Viewport.ToScreen(new SKPoint(rect.Right+36,rect.MidY)));preview.EndPointer();Pump();check(Math.Abs(region.Softness.Width-36)<.1,"dragging feather handle changes source-pixel softness width");
        check(region.Bounds==r.Bounds,"feather handle leaves selection geometry unchanged");
        preview.BeginPointer(preview.Viewport.ToScreen(new SKPoint(rect.Right+36,rect.MidY)));preview.MovePointer(preview.Viewport.ToScreen(new SKPoint(rect.Right+48,rect.MidY)));preview.CancelGesture();Pump();check(Math.Abs(region.Softness.Width-36)<.1,"cancel restores feather gesture without changing previous edit");
        var compare=(ComboBox)window.FindName("CompareBackgroundBox");compare.SelectedIndex=3;Pump();check(preview.ComparisonBackground==3,"black/white comparison changes only preview backdrop");
        var dialog=new PlaybackWindow(current.Snapshot(),new(){Fps=30},.5){Owner=window,ShowInTaskbar=false,ShowActivated=false,WindowStartupLocation=WindowStartupLocation.Manual,Left=-15000,Top=-15000};
        try
        {
            dialog.Show();Pump();dialog.SelectClips([region.Clips[1].Id]);dialog.AssignSelected(ClipSection.Exit);
            check(dialog.Sections.ByEffects&&dialog.Sections.ExitEnabled==true,"effect-based assignment enables optional exit");
            check(region.Clips[1].Section==ClipSection.Auto,"cancelable settings edit does not mutate original job");
            StudioUiChecks.Capture(dialog,Path.Combine(folder,"effect-sections-v011.png"),650,790);
        }finally{dialog.Close();}
        var loopWindow=window.CreateLoopComparison();check(loopWindow is not null&&loopWindow.Title.Contains(ProductInfo.Credit),"loop seam comparison exposes two native frames and creator/version");loopWindow?.Close();
        Call("ShowSoftnessProperties",window,new RoutedEventArgs());Call("Notify","소프트 경계 · 효과별 구간 · 배경 비교 미리보기");Pump();StudioUiChecks.Capture(window,Path.Combine(folder,"soft-editor-v011.png"),1580,1040);
    }
}
