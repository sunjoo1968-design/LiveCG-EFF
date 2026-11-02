using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using LiveCG.App;
using LiveCG.Core;
using SkiaSharp;
internal static class V123UiChecks
{
    public static void Run(MainWindow window,string folder,Action<bool,string> check)
    {
        Repro(window,folder,check,LiveCG.Testing.TestFixtures.NumericRedraw(Path.Combine(folder,"v123-synthetic")));
        if(File.Exists(Environment.GetEnvironmentVariable("LIVECG_TEST_PRICE_IMAGE")))Repro(window,folder,check);
    }
    public static void Repro(MainWindow w,string folder,Action<bool,string> check,string? sourcePath=null)
    {
        var file=sourcePath??Environment.GetEnvironmentVariable("LIVECG_TEST_PRICE_IMAGE");if(!File.Exists(file))throw new FileNotFoundException("Set LIVECG_TEST_PRICE_IMAGE to the supplied price PNG for this local repro.");using var source=SourceImage.Read(file)!;
        object? Call(string name,params object?[] args){var m=typeof(MainWindow).GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public)!;return m.Invoke(w,m.GetParameters().Select((p,i)=>i<args.Length?args[i]:p.DefaultValue).ToArray());}
        T Field<T>(string name)=>(T)typeof(MainWindow).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(w)!;
        T C<T>(string name)=>(T)w.FindName(name);void Pump()=>Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
        var dir=Path.Combine(folder,sourcePath is null?"v123-local":"v123-synthetic");Directory.CreateDirectory(dir);w.ApplyDocument(new(){Jobs=[new(){SourcePath=file!,Width=source.Width,Height=source.Height}],Video=new(){Fps=30,EndHold=0}});C<CheckBox>("AutoFocusCheck").IsChecked=false;C<CheckBox>("HideOverlaysCheck").IsChecked=false;C<ComboBox>("ShapeBox").SelectedIndex=0;C<ComboBox>("CreationRoleBox").SelectedIndex=0;Pump();var preview=C<PreviewControl>("Preview");
        var states=new List<object>();EffectRegion Draw(int delta)
        {
            Call("SetDrawMode",true);preview.BeginPointer(preview.Viewport.ToScreen(new SKPoint(659+delta,718+delta)));preview.MovePointer(preview.Viewport.ToScreen(new SKPoint(852+delta,770+delta)));preview.EndPointer();Pump();C<ComboBox>("AreaSeparationTarget").SelectedIndex=1;Pump();Call("SelectEffect",EffectKind.DigitRoll);Pump();var region=Field<EffectRegion>("_region");
            using var crop=ObjectExtractor.CropObject(source,region);using var scene=new EffectScene(source);var clip=region.Clips.First(c=>c.Options.Kind==EffectKind.DigitRoll);using var frame=scene.Render(Field<ImageJob>("_current").Regions,new(),clip.Start+clip.Duration*.2);SourceImage.SavePng(crop,Path.Combine(dir,"crop-"+states.Count+".png"));SourceImage.SavePng(frame,Path.Combine(dir,"frame-"+states.Count+".png"));
            var count=scene.DigitCount(region);Console.WriteLine($"REDRAW step={states.Count} delta={delta} digits={count} preview={preview.DigitCount(region)} role={region.Role} regions={Field<ImageJob>("_current").Regions.Count}");states.Add(new{Delta=delta,Digits=count,PreviewDigits=preview.DigitCount(region),Region=region.Copy(false)});check(count==6,"provided price redraw "+states.Count+" detects six original price digits");return region;
        }
        var first=Draw(0);var firstId=first.Id;Call("DeleteRegion",w,new RoutedEventArgs());Pump();check(Field<ImageJob>("_current").Regions.Count==0,"deleting price region removes its model before the next drawing");var next=Draw(0);check(next.Id!=firstId&&next.Cutout is null&&next.DigitDividers.Count==0,"redrawing starts with a new identity and no inherited precision mask or manual numeric dividers");
        using(var cold=new EffectScene(source)){var cached=(EffectScene)typeof(PreviewControl).GetField("_scene",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(preview)!;var clip=next.Clips[0];using var a=cold.Render(Field<ImageJob>("_current").Regions,new(),clip.Start+clip.Duration*.2);using var b=cached.Render(Field<ImageJob>("_current").Regions,new(),clip.Start+clip.Duration*.2);check(a.Bytes.SequenceEqual(b.Bytes),"redrawn price renders identically through warm preview and a new cold scene");}
        foreach(var delta in new[]{-2,-1,1,2}){Call("DeleteRegion",w,new RoutedEventArgs());Pump();Draw(delta);}
        var selected=Field<EffectRegion>("_region");var precision=new ObjectSelectionWindow(source,selected.Copy(false)){Owner=w,WindowStartupLocation=WindowStartupLocation.Manual,Left=-16000,Top=-16000,ShowInTaskbar=false,ShowActivated=false};
        try{precision.Show();Pump();precision.TextMethod.SelectedIndex=1;var pending=precision.FlushPreviewAsync();var wait=System.Diagnostics.Stopwatch.StartNew();while(!pending.IsCompleted&&wait.ElapsedMilliseconds<15000){Pump();Thread.Sleep(10);}if(!pending.IsCompleted)throw new TimeoutException("precision price preview timed out");pending.GetAwaiter().GetResult();check(precision.PrepareResult(),"range separation of a redrawn price prepares its saved foreground and alpha");w.ApplyObjectSelection(precision.SelectedBounds,precision.SelectedCutout,precision.SelectedBackground,precision.SelectedTolerance,precision.SelectedTarget,precision.SelectedInkColor,precision.SelectedTextSettings);Pump();}finally{precision.Close();}
        check(preview.DigitCount(selected)==6,"saved range separation already finds six price digits before opening numeric-cell editing");
        var beforeCells=((EffectScene)typeof(PreviewControl).GetField("_scene",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(preview)!).DigitBounds(selected);Exception? dialogError=null;var applyTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(80)};
        static IEnumerable<Button> Buttons(DependencyObject host){for(var i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(host);i++){var child=System.Windows.Media.VisualTreeHelper.GetChild(host,i);if(child is Button b)yield return b;foreach(var nested in Buttons(child))yield return nested;}}
        applyTimer.Tick+=(_,_)=>{var editor=Application.Current.Windows.OfType<DigitCellWindow>().FirstOrDefault();if(editor is null)return;applyTimer.Stop();try{Buttons(editor).First(b=>Equals(b.Content,"적용")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}catch(Exception ex){dialogError=ex;editor.Close();}};applyTimer.Start();Call("EditDigitCells",w,new RoutedEventArgs());applyTimer.Stop();if(dialogError is not null)throw dialogError;Pump();var afterCells=((EffectScene)typeof(PreviewControl).GetField("_scene",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(preview)!).DigitBounds(selected);check(beforeCells.SequenceEqual(afterCells)&&selected.DigitDividers.Count==5,"applying the numeric editor's automatic cells preserves the already-correct digit geometry");
        File.WriteAllText(Path.Combine(dir,"states.json"),JsonSerializer.Serialize(states,new JsonSerializerOptions{WriteIndented=true}));
    }
}
