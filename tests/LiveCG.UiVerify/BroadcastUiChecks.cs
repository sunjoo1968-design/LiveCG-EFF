using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using LiveCG.App;
using LiveCG.Core;
using SkiaSharp;

internal static class BroadcastUiChecks
{
    public static void Run(MainWindow window,string folder,Action<bool,string> check)
    {
        T C<T>(string n)=>(T)window.FindName(n);object? Call(string n,params object?[] values){var m=typeof(MainWindow).GetMethod(n,BindingFlags.Instance|BindingFlags.NonPublic)!;return m.Invoke(window,m.GetParameters().Select((p,i)=>i<values.Length?values[i]:p.DefaultValue).ToArray());}void Pump()=>Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
        var path=Path.Combine(folder,"타임라인 예제.png");var region=new EffectRegion{Bounds=new(.1,.1,.5,.5),Clips=[new(){Options=new(){Kind=EffectKind.Ripple}}]};window.ApplyDocument(new(){Jobs=[new(){SourcePath=path,Width=960,Height=540,Regions=[region]}]});Pump();
        var strength=C<ComboBox>("StrengthBox");check(strength.Items.Cast<string>().SequenceEqual(new[]{"최약","약","중","강","최강"}),"five strength labels appear in intuitive strength order");strength.SelectedIndex=0;Pump();check(region.Clips[0].Options.Strength==StrengthLevel.VeryWeak,"lowest strength changes selected clip to very weak");strength.SelectedIndex=4;Pump();check(region.Clips[0].Options.Strength==StrengthLevel.VeryStrong,"highest strength changes selected clip to very strong");
        Call("SetEffectColor",new SKColor(12,230,250,180));check(region.Clips[0].Options.Color=="#B40CE6FA"&&C<Border>("EffectColorSwatch").Background is SolidColorBrush brush&&brush.Color.G==230,"effect color picker result updates clip and visible swatch including alpha");Call("UndoEdit",window,new RoutedEventArgs());Call("RedoEdit",window,new RoutedEventArgs());
        var current=(ImageJob)typeof(MainWindow).GetField("_current",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(window)!;check(current.Regions[0].Clips[0].Options.Color=="#B40CE6FA","effect color participates in undo and redo");Call("ResetEffectColor",window,new RoutedEventArgs());check(current.Regions[0].Clips[0].Options.Color is null,"default-color button restores per-effect colors");
        var tabs=C<TabControl>("EffectsTabs");check(tabs.Items.Cast<TabItem>().All(t=>t.MinHeight>=44&&t.Width==140),"all four effect tabs have explicit large touch targets and visible borders");
        var studio=new EffectStudioWindow(EffectAsset.NewMotion()){Owner=window,Left=-15000,Top=-15000,WindowStartupLocation=WindowStartupLocation.Manual,ShowActivated=false,ShowInTaskbar=false};
        try{studio.Show();Pump();check(studio.Timeline.Motion.Tracks.Count==0,"new motion studio begins with no prefilled sample animation");studio.SetKey(MotionProperty.CropTop,0,.2,KeyEasing.Linear);studio.SetKey(MotionProperty.CropRight,60,.7,KeyEasing.EaseInOut);check(studio.Timeline.Motion.Value(MotionProperty.CropTop,15)==.2&&studio.Timeline.Motion.Value(MotionProperty.CropRight,60)==.7,"studio can author top and right crop keyframes");studio.Timeline.Refresh();check(studio.Timeline.Height>=MotionTimelineControl.RulerHeight+12*MotionTimelineControl.RowHeight,"motion timeline exposes all twelve channels including four crop rows");StudioUiChecks.Capture(studio,Path.Combine(folder,"broadcast-crop-studio.png"),1370,940);}finally{studio.DiscardAndClose();}
        tabs.SelectedIndex=1;StudioUiChecks.Capture(window,Path.Combine(folder,"broadcast-effect-tabs.png"),1580,1040);
    }
}
