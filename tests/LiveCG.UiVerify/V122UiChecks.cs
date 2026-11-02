using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using LiveCG.App;
using LiveCG.Core;
using SkiaSharp;
internal static class V122UiChecks
{
    public static void Run(MainWindow owner,string folder,Action<bool,string> check)
    {
        void Pump()=>Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
        static IEnumerable<T> Children<T>(DependencyObject host) where T:DependencyObject{for(var i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(host);i++){var child=System.Windows.Media.VisualTreeHelper.GetChild(host,i);if(child is T value)yield return value;foreach(var nested in Children<T>(child))yield return nested;}}
        void Await(Task task){var timer=System.Diagnostics.Stopwatch.StartNew();while(!task.IsCompleted&&timer.ElapsedMilliseconds<15000){Pump();Thread.Sleep(10);}if(!task.IsCompleted)throw new TimeoutException("numeric preset preview timed out");task.GetAwaiter().GetResult();Pump();}
        using var map=new SKBitmap(640,400);map.Erase(new(20,46,72));var legacy=TravelRoute.Example();var window=new TravelRouteWindow(map,legacy){Owner=owner,WindowStartupLocation=WindowStartupLocation.Manual,Left=-16000,Top=-16000,ShowActivated=false,ShowInTaskbar=false};
        try
        {
            window.Show();Pump();check(window.Route.DurationBudget==7&&Math.Abs(window.Route.Duration-7)<1e-8,"opening legacy route preserves its total and enables automatic movement allocation");window.PointsList.SelectedIndex=1;Pump();check(window.StopExactBox is {IsVisible:true},"selected waypoint exposes a directly visible precise stop-time input");
            void Enter(string value){window.StopExactBox!.Text=value;Children<Button>(window).First(b=>b.Name=="StopExactApply").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();}
            Enter("0.5");check(window.Route.Points[1].Stop==.5&&window.Route.Duration==7&&window.StopExactBox!.Text=="0.5","precise half-second input and slider update the same stop while preserving total time");window.Undo();check(window.Route.Points[1].Stop==1&&window.StopExactBox!.Text=="1","undo restores precise stop input with its slider and route");window.Redo();check(window.Route.Points[1].Stop==.5,"redo restores the precise stop time");
            Enter("not a number");check(window.Route.Points[1].Stop==.5&&window.StopExactBox!.Text=="0.5","invalid exact stop input preserves the accepted timing");Enter("30");check(window.Route.Points[1].Stop==.5&&window.Route.DurationBudget==7,"stop longer than the whole route is rejected without corrupting its time budget");
            var labels=Children<TextBlock>(window).Select(t=>t.Text).ToList();check(!labels.Any(t=>t.Contains("이전 지점에서 이동"))&&!Children<CheckBox>(window).Any(c=>c.Content?.ToString()?.Contains("길이별 자동 배분")==true),"manual movement slider and its redundant automatic-mode switch are removed");var tabs=Children<TabControl>(window).First();tabs.SelectedIndex=1;Pump();var emphasis=Children<CheckBox>(window).FirstOrDefault(c=>c.Content?.ToString()?.Contains("모든 지점 정차 중 강조")==true);check(emphasis?.IsChecked==true,"emphasis option explicitly covers every stop without names");emphasis!.IsChecked=false;check(!window.Route.Arrival,"all-stop emphasis toggle updates the saved route option");
            tabs.SelectedIndex=0;Pump();StudioUiChecks.Capture(window,Path.Combine(folder,"travel-stops-v122.png"),1280,900);
        }
        finally{window.Close();}
        using var image=new SKBitmap(240,100);image.Erase(SKColors.White);using(var c=new SKCanvas(image))using(var p=new SKPaint{Color=SKColors.Black,IsAntialias=true,Style=SKPaintStyle.Stroke,StrokeWidth=4}){c.DrawCircle(50,45,22,p);p.StrokeWidth=1;c.DrawLine(160.5f,15,160.5f,85,p);}
        var region=new EffectRegion{Bounds=new(0,0,1,1),ContentOnly=true,SeparationTarget=SeparationTarget.Object,Background=BackgroundMode.Solid,BackgroundColor="#FFFFFF"};var dialog=new ObjectSelectionWindow(image,region){Owner=owner,WindowStartupLocation=WindowStartupLocation.Manual,Left=-16000,Top=-16000,ShowActivated=false,ShowInTaskbar=false};
        try
        {
            dialog.Show();Pump();dialog.ApplyNumericPreset();Await(dialog.FlushPreviewAsync());check(dialog.StrokeProtectionActive&&dialog.SelectedTextSettings.RangeMode&&dialog.SelectedInkColor=="","numeric preset selects protected text range extraction with automatic ink inference");check(dialog.Canvas.Buffer.Alpha[45*240+50]==0&&dialog.Canvas.Buffer.Alpha[30*240+160]>0,"numeric preset removes closed backgrounds and retains one-pixel strokes");dialog.Undo();check(!dialog.StrokeProtectionActive,"undo restores the previous general-object separation mode");dialog.Redo();check(dialog.StrokeProtectionActive&&dialog.SelectedTextSettings.RangeMode,"redo restores numeric-first separation settings");
            dialog.Canvas.BrushSize=3;dialog.Canvas.PaintStroke([new(12,12)],true);dialog.ApplyNumericPreset();Await(dialog.FlushPreviewAsync());check(dialog.Canvas.Buffer.Alpha[12*240+12]==255,"numeric preset preserves intentional manual mask corrections");check(dialog.PrepareResult()&&dialog.SelectedCutout!.ForegroundPng.Length>0,"numeric preset saves foreground colors and extraction alpha for effect rendering");StudioUiChecks.Capture(dialog,Path.Combine(folder,"numeric-preset-v122.png"),1120,800);
        }
        finally{dialog.Close();}
    }
}
