using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using LiveCG.App;
using LiveCG.Core;
using SkiaSharp;

internal static class GroupUiChecks
{
    public static void Run(MainWindow window,string folder,Action<bool,string> check)
    {
        T C<T>(string name)=>(T)window.FindName(name);
        T Field<T>(string name)=>(T)typeof(MainWindow).GetField(name,BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(window)!;
        object? Call(string method,params object?[] values){var m=typeof(MainWindow).GetMethod(method,BindingFlags.NonPublic|BindingFlags.Instance)!;return m.Invoke(window,m.GetParameters().Select((p,i)=>i<values.Length?values[i]:p.DefaultValue).ToArray());}
        void Pump()=>Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
        ImageJob Job()=>Field<ImageJob>("_current");var preview=C<PreviewControl>("Preview");var timeline=C<TimelineControl>("TimelineEditor");var path=Path.Combine(folder,"타임라인 예제.png");
        window.ApplyDocument(new(){Jobs=[new(){SourcePath=path,Width=960,Height=540}],Video=new(){Fps=30,EndHold=.5}});
        C<CheckBox>("AutoFocusCheck").IsChecked=false;C<CheckBox>("HideOverlaysCheck").IsChecked=false;C<ComboBox>("CreationRoleBox").SelectedIndex=0;C<ComboBox>("ShapeBox").SelectedIndex=(int)RegionShape.Rectangle;Pump();
        SKPoint At(float x,float y)=>preview.Viewport.ToScreen(new SKPoint(x,y));
        void Draw(float x,float y,float w,float h){Call("SetDrawMode",true);preview.BeginPointer(At(x,y));preview.MovePointer(At(x+w,y+h));preview.EndPointer();Pump();}
        Draw(50,50,400,400);var group=Job().Regions.Single();check(group.Role==RegionRole.Group&&group.ParentId is null,"first mouse region becomes an explicit group");
        Draw(100,100,80,80);var child=Job().Regions.First(r=>r.Id!=group.Id);check(child.Role==RegionRole.EffectArea&&child.ParentId==group.Id,"drawing inside the selected group creates a local effect area");
        C<ComboBox>("CreationRoleBox").SelectedIndex=3;Draw(500,100,80,80);check(Job().Regions[0].Role==RegionRole.Object&&Job().Regions[0].ParentId is null,"creation selector supports independent objects outside groups");
        Call("SelectRegion",group);check(!window.SetRegionRelation(group,RegionRole.EffectArea,child.Id,out _)&&group.Role==RegionRole.Group,"invalid parent assignment rolls back without corrupting the graph");
        Call("DrawNewMask",window,new RoutedEventArgs());preview.NewShape=RegionShape.Ellipse;preview.BeginPointer(At(80,80));preview.MovePointer(At(220,220));preview.EndPointer();Pump();var mask=group.Masks.Single();
        check(Job().Regions.Count==3&&mask.Shape==RegionShape.Ellipse&&preview.SelectedMask==mask&&timeline.SelectedMask==mask,"drawing a mask adds a selected child track without adding a duplicate region");
        check(LayerGraph.Rows(Job().Regions).Any(r=>r.Mask==mask&&r.Depth==1),"mask is displayed as a child row under its owning group");
        Call("SetDrawMode",false);var original=mask.Bounds;var center=At((float)((original.X+original.Width/2)*960),(float)((original.Y+original.Height/2)*540));preview.BeginPointer(center);preview.MovePointer(new(center.X+20,center.Y+10));preview.EndPointer();Pump();check(mask.Bounds!=original,"mouse mask center moves its source geometry");var moved=mask.Bounds;
        preview.BeginPointer(At((float)((moved.X+moved.Width)*960),(float)((moved.Y+moved.Height)*540)));preview.MovePointer(At((float)((moved.X+moved.Width)*960+25),(float)((moved.Y+moved.Height)*540+25)));preview.CancelGesture();check(mask.Bounds==moved,"ESC restores the mask before an unfinished resize");
        var rect=timeline.MaskRect(mask);timeline.SnapEnabled=false;timeline.BeginPointer(new(rect.Left+rect.Width/2,rect.Top+14));timeline.MovePointer(new(rect.Left+rect.Width/2+timeline.PixelsPerSecond,rect.Top+14),true);timeline.EndPointer();check(Math.Abs(mask.Start-1)<.01,"mask bar mouse drag changes its frame-aligned start");var id=mask.Id;
        Call("UndoEdit",window,new RoutedEventArgs());group=Job().Regions.First(r=>r.Id==group.Id);mask=group.Masks.Single();check(mask.Start==0&&preview.SelectedMask?.Id==id,"undo restores mask timing and mask selection together");Call("RedoEdit",window,new RoutedEventArgs());group=Job().Regions.First(r=>r.Id==group.Id);mask=group.Masks.Single();check(Math.Abs(mask.Start-1)<.01,"redo restores the mask timeline transaction");
        rect=timeline.MaskRect(mask);var oldDuration=mask.Duration;timeline.BeginPointer(new(rect.Right-2,rect.Top+14));timeline.MovePointer(new(rect.Right-2+.5*timeline.PixelsPerSecond,rect.Top+14),true);timeline.EndPointer();check(Math.Abs(mask.Duration-oldDuration-.5)<.01,"mask end handle changes duration without changing its animation keys");
        window.SelectMask(group,mask);C<ComboBox>("MaskOperationBox").SelectedIndex=(int)MaskOperation.Subtract;C<ComboBox>("MaskFeatherBox").Text="2";Call("MaskPropertiesChanged",window,new RoutedEventArgs());check(mask.Operation==MaskOperation.Subtract&&mask.Feather==2,"mask inspector commits operation and source-pixel feather");
        Call("CopyClip",window,new RoutedEventArgs());Call("Seek",2d,false);Call("PasteClip",window,new RoutedEventArgs());check(group.Masks.Count==2&&group.Masks[1].Id!=mask.Id&&group.Masks[1].Start==2,"mask copy/paste creates an independent track at the playhead");
        var second=group.Masks[1];rect=timeline.MaskRect(second);timeline.BeginPointer(new(14,rect.Top+14));timeline.EndPointer();check(!second.Enabled,"mask eye toggle disables only the selected mask");
        Call("SelectRegion",group);var before=LayerGraph.Rows(Job().Regions).Count;Call("ToggleCollapse",group);check(LayerGraph.Rows(Job().Regions).Count<before,"group collapse hides its masks and local effect rows");Call("ToggleCollapse",group);
        var prior=Job().Regions.Count;Call("DuplicateRegion",window,new RoutedEventArgs());var selected=Field<EffectRegion>("_region");check(Job().Regions.Count==prior+2&&selected.Role==RegionRole.Group&&selected.Masks.Count==2&&Job().Regions.Any(r=>r.ParentId==selected.Id),"group duplication copies local effects and masks with remapped IDs");Call("DeleteRegion",window,new RoutedEventArgs());check(Job().Regions.Count==prior&&!Job().Regions.Any(r=>r.ParentId==selected.Id),"deleting a group removes its local effect rows");
        Call("SelectRegion",group);window.SelectMask(group,mask);var saved=Path.Combine(folder,"ui-groups.json");DocumentStore.Save(saved,new ProjectDocument{Jobs=[Job().Snapshot()]});var read=DocumentStore.Load<ProjectDocument>(saved);check(read.Jobs[0].Regions.Any(r=>r.ParentId==group.Id)&&read.Jobs[0].Regions.First(r=>r.Id==group.Id).Masks.Count==2,"WPF-authored group and masks survive project save/load");
        StudioUiChecks.Capture(window,Path.Combine(folder,"group-mask-timeline.png"),1568,1000);
        var editMask=mask.Copy(false);editMask.Duration=4;var dialog=new MaskAnimationWindow(path,editMask){Owner=window,WindowStartupLocation=WindowStartupLocation.Manual,Left=-14000,Top=-14000,ShowActivated=false,ShowInTaskbar=false};
        try
        {
            dialog.Show();Pump();check(dialog.Title.Contains("SunjooAn")&&dialog.Title.Contains(ProductInfo.Version),"mask keyframe window displays creator and version");
            dialog.SetKey(MotionProperty.X,0,0,KeyEasing.Linear);dialog.SetKey(MotionProperty.X,30,.5);check(dialog.Result.Value(MotionProperty.X,15)>.2&&dialog.Result.Value(MotionProperty.X,15)<.3,"mask editor adds keys with linear interpolation");
            dialog.Undo();check(dialog.Result.Tracks.First(t=>t.Property==MotionProperty.X).Keys.Count==1,"mask editor undo removes the last key");dialog.Redo();check(dialog.Result.Tracks.First(t=>t.Property==MotionProperty.X).Keys.Count==2,"mask editor redo restores the key");
            dialog.SetFrame(30);dialog.CopyKeys();dialog.SetFrame(60);dialog.PasteKeys();check(dialog.Result.Tracks.First(t=>t.Property==MotionProperty.X).Keys.Any(k=>k.Frame==60),"mask editor copies and pastes keys at another frame");
            dialog.SetKey(MotionProperty.Rotation,0,15);dialog.SetKey(MotionProperty.ScaleX,0,1.2);dialog.SetKey(MotionProperty.Opacity,0,.5);dialog.SetKey(MotionProperty.AnchorX,0,.25);dialog.SetFrame(15);check(dialog.Preview.Track.Motion==dialog.Result&&Math.Abs(dialog.Preview.Time-editMask.Start-editMask.Duration*15/dialog.Result.LengthFrames)<1e-8,"mask preview uses actual source and current frame across rotation scale opacity and anchor channels");
            var key=dialog.Result.Tracks.First(t=>t.Property==MotionProperty.X).Keys.First(k=>k.Frame==30);var keyPoint=dialog.Timeline.KeyPoint(MotionProperty.X,key);dialog.Timeline.BeginPointer(keyPoint);dialog.Timeline.MovePointer(new(keyPoint.X+5*dialog.Timeline.PixelsPerFrame,keyPoint.Y));dialog.Timeline.EndPointer();check(key.Frame==35,"mask animation diamond drag moves the actual key frame");
            dialog.Undo();check(dialog.Result.Tracks.First(t=>t.Property==MotionProperty.X).Keys.Any(k=>k.Frame==30),"mask animation drag is a single undo transaction");
            key=dialog.Result.Tracks.First(t=>t.Property==MotionProperty.X).Keys.First(k=>k.Frame==30);dialog.Timeline.SelectedKeys.Add(key.Id);typeof(MaskAnimationWindow).GetMethod("DeleteKeys",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(dialog,null);check(!dialog.Result.Tracks.SelectMany(t=>t.Keys).Any(k=>k.Id==key.Id),"mask animation selected-key deletion removes only that key");dialog.Undo();
            StudioUiChecks.Capture(dialog,Path.Combine(folder,"mask-animation.png"),1210,850);StudioUiChecks.Capture(dialog,Path.Combine(folder,"mask-animation-minimum.png"),900,650);
        }finally{dialog.Close();}
        window.ApplyDocument(read);group=Job().Regions.First(r=>r.Id==group.Id);Call("SelectRegion",group);var baseline=group.Masks.Count;
        Call("DrawNewMask",window,new RoutedEventArgs());preview.NewShape=RegionShape.Freehand;preview.BeginPointer(At(240,100));preview.MovePointer(At(300,100));preview.MovePointer(At(290,180));preview.MovePointer(At(240,170));preview.EndPointer();check(group.Masks.Count==baseline+1&&group.Masks.Last().Shape==RegionShape.Freehand&&group.Masks.Last().Points.Count>=4,"freehand mask uses the same closed-path mouse tool as regions");
        Call("DrawNewMask",window,new RoutedEventArgs());preview.NewShape=RegionShape.Polygon;preview.AddPathPoint(At(320,100));preview.AddPathPoint(At(380,100));preview.AddPathPoint(At(350,180));preview.FinishPath();check(group.Masks.Count==baseline+2&&group.Masks.Last().Points.Count==3,"point pen can create a separate timeline mask");
        var polygon=group.Masks.Last();var previous=polygon.Bounds;var corner=At((float)((previous.X+previous.Width)*960),(float)((previous.Y+previous.Height)*540));preview.BeginPointer(corner);preview.MovePointer(new(corner.X+20,corner.Y+20));preview.EndPointer();check(polygon.Bounds.Width>previous.Width&&polygon.Bounds.Height>previous.Height,"mask corner handle commits a resized path");
        rect=timeline.MaskRect(polygon);timeline.BeginPointer(new(TimelineControl.LabelWidth-34,rect.Top+14));timeline.EndPointer();check(group.Masks[^2].Id==polygon.Id,"mask up arrow changes explicit composition order within its owner");
        Call("SetDrawMode",false);C<ComboBox>("CreationRoleBox").SelectedIndex=0;
    }
}
