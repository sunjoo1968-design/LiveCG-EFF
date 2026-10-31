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

internal static class PrecisionUiChecks
{
    public static void Run(MainWindow window,string folder,Action<bool,string> check)
    {
        T C<T>(string name)=>(T)window.FindName(name);
        T Field<T>(string name)=>(T)typeof(MainWindow).GetField(name,BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(window)!;
        object? Call(string method,params object?[] values){var m=typeof(MainWindow).GetMethod(method,BindingFlags.NonPublic|BindingFlags.Instance)!;return m.Invoke(window,m.GetParameters().Select((p,i)=>i<values.Length?values[i]:p.DefaultValue).ToArray());}
        void Pump()=>Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
        var path=Path.Combine(folder,"타임라인 예제.png");var timeline=C<TimelineControl>("TimelineEditor");var preview=C<PreviewControl>("Preview");
        ImageJob Job()=>Field<ImageJob>("_current");
        var groupId=Guid.NewGuid();
        void Load()
        {
            var job=TestFixtures.Demo(path);job.Regions=job.Regions.Take(2).ToList();
            for(var i=0;i<2;i++)job.Regions[i].Clips=[new(){Start=1+i*2,Duration=1,Options=new(){Kind=EffectKind.Glow},AssetInstanceId=groupId}];
            window.ApplyDocument(new(){Jobs=[job],Video=new(){Fps=30,EndHold=.9}});C<CheckBox>("SnapCheck").IsChecked=false;Pump();
        }
        Point Center(int row){var r=timeline.ClipRect(Job().Regions[row],Job().Regions[row].Clips[0]);return new(r.MidX(),r.Top+14);}
        void SelectTwo(){timeline.BeginPointer(Center(0));timeline.EndPointer();timeline.BeginPointer(Center(1),true);timeline.EndPointer();Pump();}
        Load();SelectTwo();check(timeline.SelectedClipIds.Count==2&&C<TextBlock>("ContextPathText").Text.Contains("선택 2개"),"Ctrl-click selects clips across region rows with an explicit count");
        timeline.BeginPointer(Center(0),true);timeline.EndPointer();check(timeline.SelectedClipIds.Count==1&&timeline.SelectedClipIds.Contains(Job().Regions[1].Clips[0].Id),"Ctrl-click toggles a selected clip without clearing the other row");
        timeline.BeginPointer(Center(0),true);timeline.EndPointer();var count=Field<EditHistory>("_history").UndoCount;var at=Center(0);
        timeline.BeginPointer(at);timeline.MovePointer(new(at.X+timeline.PixelsPerSecond*.5,at.Y));timeline.EndPointer();
        check(Job().Regions[0].Clips[0].Start==1.5&&Job().Regions[1].Clips[0].Start==3.5,"group body drag moves all selected rows by the same frame delta");
        check(Field<EditHistory>("_history").UndoCount==count+1,"multi-clip drag creates one undo transaction");
        Call("UndoEdit",window,new RoutedEventArgs());check(Job().Regions[0].Clips[0].Start==1&&Job().Regions[1].Clips[0].Start==3&&timeline.SelectedClipIds.Count==2,"undo restores group timing and multiple selection");
        Call("RedoEdit",window,new RoutedEventArgs());check(Job().Regions[0].Clips[0].Start==1.5&&Job().Regions[1].Clips[0].Start==3.5,"redo reapplies all selected clip timings");
        Call("CopyClip",window,new RoutedEventArgs());Call("SelectRegion",Job().Regions[0]);Call("Seek",6d,false);Call("PasteClip",window,new RoutedEventArgs());
        var pasted=timeline.SelectedClips.ToList();check(pasted.Count==2&&pasted[0].Clip.Start==6&&pasted[1].Clip.Start==8&&Job().Regions.All(r=>r.Clips.Count==2),"group paste preserves time spacing and relative region rows");
        check(pasted.All(x=>x.Clip.AssetInstanceId!=groupId)&&pasted[0].Clip.AssetInstanceId==pasted[1].Clip.AssetInstanceId,"pasted asset group has a fresh shared instance identity");
        var ids=pasted.Select(x=>x.Clip.Id).ToHashSet();Call("DeleteClip",window,new RoutedEventArgs());
        check(Job().Regions.All(r=>r.Clips.Count==1)&&!Job().Regions.SelectMany(r=>r.Clips).Any(c=>ids.Contains(c.Id)),"delete removes exactly the selected clips across rows");
        Call("UndoEdit",window,new RoutedEventArgs());check(Job().Regions.All(r=>r.Clips.Count==2)&&timeline.SelectedClipIds.SetEquals(ids),"undo restores deleted group and its selection");
        Call("RedoEdit",window,new RoutedEventArgs());check(Job().Regions.All(r=>r.Clips.Count==1),"redo deletes the entire group again");
        Call("SelectRegion",Job().Regions[1]);var total=Job().Regions.Sum(r=>r.Clips.Count);var rejected=false;
        try{window.PlanClipboardPaste();}catch(InvalidDataException){rejected=true;}
        check(rejected&&Job().Regions.Sum(r=>r.Clips.Count)==total,"insufficient destination rows reject paste before modifying any clip");
        Load();SelectTwo();at=Center(0);timeline.BeginPointer(at);timeline.MovePointer(new(at.X-3*timeline.PixelsPerSecond,at.Y));
        check(Job().Regions[0].Clips[0].Start==0&&Job().Regions[1].Clips[0].Start==2,"zero boundary clamps the entire group without changing spacing");timeline.CancelGesture();
        check(Job().Regions[0].Clips[0].Start==1&&Job().Regions[1].Clips[0].Start==3,"ESC restores timing on every selected row");
        C<CheckBox>("SnapCheck").IsChecked=true;at=Center(0);timeline.BeginPointer(at);timeline.MovePointer(new(at.X+.97*timeline.PixelsPerSecond,at.Y));timeline.EndPointer();
        check(Math.Abs(Job().Regions[0].Clips[0].Start-(1+29d/30))<1e-8,"group snap excludes all selected clips as magnetic targets");
        StudioUiChecks.Capture(window,Path.Combine(folder,"multi-selection.png"),1568,960);
        Load();C<CheckBox>("HideOverlaysCheck").IsChecked=false;preview.PickColorMode=true;
        var point=preview.Viewport.ToScreen(new SKPoint(60.5f,400.5f));preview.HoverPointer(point);
        using(var source=SKBitmap.Decode(path))check(preview.Cursor==Cursors.None&&preview.TrySampleColor(point,out var color)&&color==source.GetPixel(60,400),"original eyedropper uses software icon and samples its exact source center");
        StudioUiChecks.Capture(window,Path.Combine(folder,"source-eyedropper.png"),1568,960);preview.PickColorMode=false;
        using(var source=SKBitmap.Decode(path))
        {
            var mask=new ObjectSelectionWindow(source,Job().Regions[0]){Owner=window,Left=-16000,Top=-16000,WindowStartupLocation=WindowStartupLocation.Manual,ShowActivated=false,ShowInTaskbar=false};
            try
            {
                mask.Show();Pump();mask.Canvas.BrushMode=1;mask.Canvas.BrushSize=12;mask.Canvas.HoverPointer(mask.Canvas.Viewport.ToScreen(new SKPoint(40,40)));var before=mask.Canvas.BrushScreenRadius;
                mask.Canvas.Viewport.ZoomAt(2,new SKPoint(100,100));check(Math.Abs(mask.Canvas.BrushScreenRadius-before*2)<.01&&mask.Canvas.Cursor==Cursors.None,"brush outline radius follows the actual source brush and zoom");
                mask.Canvas.Fit();var hover=mask.Canvas.Viewport.ToScreen(new SKPoint(40.5f,40.5f));mask.Canvas.HoverPointer(hover);
                StudioUiChecks.Capture(mask,Path.Combine(folder,"brush-cursor.png"),1120,800);mask.Canvas.PickColor=true;
                check(mask.Canvas.TrySampleColor(hover,out var sampled)&&sampled==source.GetPixel((int)(Job().Regions[0].Bounds.X*source.Width)+40,(int)(Job().Regions[0].Bounds.Y*source.Height)+40),"object-background eyedropper samples crop pixels at the indicated center");
                StudioUiChecks.Capture(mask,Path.Combine(folder,"mask-eyedropper.png"),1120,800);check(mask.Icon is not null,"auxiliary editor windows inherit the application icon");
            }finally{mask.Close();}
        }
        var palette=new ColorPickerWindow(new SKColor(209,209,209)){Owner=window,Left=-16000,Top=-16000,WindowStartupLocation=WindowStartupLocation.Manual,ShowActivated=false,ShowInTaskbar=false};
        try
        {
            palette.Show();Pump();palette.SetHsv(120,255,255);check(palette.SelectedColor==SKColors.Lime&&palette.GreenInput.Value==255&&palette.RedInput.Value==0,"HSV input and RGB fields stay synchronized");
            check(palette.Plane.ActualHeight>=249&&palette.BrightnessStrip.ActualHeight>=249,"color plane and brightness strip provide the full reference-style picking surface");
            palette.AlphaInput.Input.Text="128";check(palette.SelectedColor.Alpha==128&&palette.ValidateInputs(),"alpha spin input updates the selected RGBA color");
            palette.RedInput.Input.Text="260";check(!palette.ValidateInputs(),"out-of-range numeric color input cannot be accepted");palette.SetHex("#8044AA33");
            palette.SaveCustomColor(5);check(palette.CustomColors[5]==palette.SelectedColor,"custom color slot stores the selected alpha color");
            var reopened=new ColorPickerWindow(SKColors.Black);check(reopened.CustomColors[5]==palette.SelectedColor,"custom color slots persist across picker instances");reopened.Close();
            StudioUiChecks.Capture(palette,Path.Combine(folder,"color-picker.png"),730,650);check(palette.Icon is not null&&window.Icon is not null,"main and color picker windows expose the branded icon");
        }finally{palette.Close();}
    }
}
