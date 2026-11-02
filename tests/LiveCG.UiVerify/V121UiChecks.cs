using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using LiveCG.App;
using LiveCG.Core;
using SkiaSharp;
internal static class V121UiChecks
{
    public static void Run(MainWindow w,string folder,Action<bool,string> check)
    {
        void Pump()=>Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
        object? Call(string name,params object?[] args){var m=typeof(MainWindow).GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public)!;return m.Invoke(w,m.GetParameters().Select((p,i)=>i<args.Length?args[i]:p.DefaultValue).ToArray());}
        T Field<T>(string name)=>(T)typeof(MainWindow).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(w)!;
        static IEnumerable<T> Children<T>(DependencyObject host) where T:DependencyObject{for(var i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(host);i++){var child=System.Windows.Media.VisualTreeHelper.GetChild(host,i);if(child is T value)yield return value;foreach(var nested in Children<T>(child))yield return nested;}}
        using var map=new SKBitmap(640,400);map.Erase(new(20,46,72));var editor=new TravelRouteWindow(map,new(),30){Owner=w,WindowStartupLocation=WindowStartupLocation.Manual,Left=-16000,Top=-16000,ShowActivated=false,ShowInTaskbar=false};
        try
        {
            editor.Show();Pump();var c=editor.Canvas;c.Viewport.Update(c.ActualWidth,c.ActualHeight,640,400,SKRect.Create(640,400));SKPoint At(float x,float y)=>c.Viewport.ToScreen(new SKPoint(x*640,y*400));
            check(editor.Route.Points.Count==0&&editor.PointsList.Items.Count==0,"new route window and list contain no initial markers");
            check(!Children<Button>(editor).First(b=>Equals(b.Content,"타임라인에 적용")).IsEnabled,"incomplete empty route cannot be applied");
            c.Begin(At(.15f,.55f));c.End();check(editor.Route.Points.Count==0,"left clicking empty map does not accidentally add a waypoint");
            c.RightClick(At(.15f,.55f));c.RightClick(At(.5f,.3f));c.RightClick(At(.85f,.55f));check(editor.Route.Points.Count==3&&editor.PointsList.Items[0].ToString()!.StartsWith("출발")&&editor.PointsList.Items[2].ToString()!.StartsWith("도착"),"right clicks create departure, waypoint and arrival roles");
            editor.SetTotalDuration(10);check(Math.Abs(editor.Route.Duration-10)<1e-8&&editor.Route.DurationBudget==10&&editor.TotalTime.Value==10,"total-time UI assigns 10 seconds including stops");
            var moves=editor.Route.Points.Select(p=>p.Move).ToArray();var at=At(.5f,.3f);c.Begin(at);c.Move(At(.3f,.4f));c.End();check(editor.Route.DurationBudget==10&&Math.Abs(editor.Route.Duration-10)<1e-8&&!moves.SequenceEqual(editor.Route.Points.Select(p=>p.Move)),"moving waypoint redistributes the fixed total by new path lengths");editor.Undo();check(Math.Abs(editor.Route.Points[1].X-.5)<1e-6,"undo restores waypoint and its timing allocation together");editor.Redo();check(Math.Abs(editor.Route.Points[1].X-.3)<1e-6,"redo restores edited path and budget");
            var accepted=editor.Route.Copy();editor.SetTotalDuration(1);check(editor.Route.DurationBudget==10&&editor.Route.Duration==accepted.Duration,"too-short total time rolls back instead of accepting impossible stops");
            var menu=c.RightClick(At(.15f,.55f));check(menu?.Items.Cast<MenuItem>().Any(item=>item.Header.ToString()=="출발점으로 지정")==true&&menu.Items.Count>=5,"right click on waypoint opens explicit edit and delete actions");
            c.Selected=1;editor.SetPointRole(0);check(c.Selected==0&&Math.Abs(editor.Route.Points[0].X-.3)<1e-6,"role selector reassigns the unique departure by moving its waypoint");editor.Undo();
            editor.Route.Curved=true;var a=editor.Route.Points[0];var b=editor.Route.Points[1];var midpoint=At((float)(a.X+b.X)/2,(float)(a.Y+b.Y)/2);c.Begin(midpoint);c.Move(midpoint+new SKPoint(0,-35));c.End();check(editor.Route.Points.Count==3&&editor.Route.Points[1].CurvePoints.Count==1&&Math.Abs(editor.Route.Duration-10)<1e-8,"dragging route creates a non-stop curve control and reallocates within total time");
            var firstKnot=editor.Route.Points[1].CurvePoints[0];var originalKnotX=firstKnot.X;var knotAt=At((float)firstKnot.X,(float)firstKnot.Y);c.Begin(knotAt);c.Move(knotAt+new SKPoint(20,10));c.CancelGesture();check(Math.Abs(editor.Route.Points[1].CurvePoints[0].X-originalKnotX)<1e-8,"escape cancels curve dragging and restores its original position");
            c.ShowGuides=false;var count=editor.Route.Points.Count;c.RightClick(At(.95f,.9f));c.Begin(At(.15f,.55f));c.Move(At(.2f,.6f));c.End();check(editor.Route.Points.Count==count&&editor.Route.Points[0].X==a.X,"actual-output preview hides editor markers and blocks accidental edits");c.ShowGuides=true;
            editor.SetTotalDuration(12);editor.Undo();check(editor.Route.DurationBudget==10,"undo includes total-time property edits");editor.Redo();check(editor.Route.DurationBudget==12,"redo includes total-time property edits");
            StudioUiChecks.Capture(editor,Path.Combine(folder,"travel-editor-v121.png"),1280,900);
            while(editor.Route.Points.Count>0){c.Selected=0;editor.DeletePoint();}check(editor.Route.Points.Count==0&&editor.PointsList.Items.Count==0,"delete can return the route to its original empty state");editor.Undo();check(editor.Route.Points.Count==1,"undo restores a deleted final point in incomplete edit state");
        }
        finally{editor.Close();}
        var file=Path.Combine(folder,"v121-selected-map.png");SourceImage.SavePng(map,file);var area=new EffectRegion{Name="선택 지도",Bounds=new(.2,.2,.6,.6)};var job=new ImageJob{SourcePath=file,Width=640,Height=400,Regions=[area]};w.ApplyDocument(new(){Jobs=[job],Video=new(){Fps=30}});Pump();Call("SelectRegion",(object?)null);Call("OpenTravelRoute",w,new RoutedEventArgs());check(((PreviewControl)w.FindName("Preview")).AddMode,"travel without ROI requests drawing instead of assuming the whole image");Call("SetDrawMode",false);Call("SelectRegion",area);
        Exception? error=null;var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(120)};timer.Tick+=(_,_)=>{var dialog=Application.Current.Windows.OfType<TravelRouteWindow>().FirstOrDefault();if(dialog is null)return;timer.Stop();try{check(dialog.Canvas.SourceWidth==384&&dialog.Canvas.SourceHeight==240,"travel editor uses the selected map ROI rather than full image");var c=dialog.Canvas;c.RightClick(c.Viewport.ToScreen(new SKPoint(60,120)));c.RightClick(c.Viewport.ToScreen(new SKPoint(320,100)));Children<Button>(dialog).First(b=>Equals(b.Content,"타임라인에 적용")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}catch(Exception ex){error=ex;dialog.Close();}};timer.Start();Call("OpenTravelRoute",w,new RoutedEventArgs());timer.Stop();if(error is not null)throw error;Pump();
        var current=Field<ImageJob>("_current");var overlay=Field<EffectRegion>("_region");check(overlay.OverlayOnly&&overlay.Bounds==area.Bounds&&overlay.ParentId is null&&overlay.Role==RegionRole.Object,"route creates an independent overlay exactly matching the selected ROI");
        ((ComboBox)w.FindName("CreationRoleBox")).SelectedIndex=0;overlay.Role=RegionRole.Group;var next=new EffectRegion{Bounds=new(.3,.3,.1,.1)};Call("ConfigureNewRegion",next);check(next.ParentId is null,"even legacy route groups cannot automatically capture later effect regions");
        overlay.Role=RegionRole.Object;check(current.Regions.Any(r=>r.Id==area.Id),"map ROI and prior effects remain preserved beside the route overlay");
        var budgetRoute=new TravelRoute{Points=[new(){X=.1,Y=.2,Stop=0},new(){X=.9,Y=.2,Stop=0},new(){X=.9,Y=.8,Stop=0}]};RouteEditing.AllocateDuration(budgetRoute,10,1200,400,30);
        var asset=new EffectAsset{Name="v121 timing asset",Clips=[new(){Duration=10,Route=budgetRoute,Options=new(){Kind=EffectKind.TravelRoute}}]};AssetStore.Save(asset);Call("RefreshAssets");var list=(ListBox)w.FindName("AssetList");list.SelectedItem=list.Items.Cast<EffectAsset>().First(a=>a.Name==asset.Name);Call("SelectRegion",area);Call("InsertAsset",w,new RoutedEventArgs());var inserted=Field<EffectRegion>("_region");var insertedRoute=inserted.Clips[0].Route!;check(Math.Abs(insertedRoute.Duration-10)<1e-8&&Math.Abs(insertedRoute.Points[1].Move/insertedRoute.Points[2].Move-2.1333)<.04,"inserting a budget route rebalances lengths for the selected map aspect ratio");
        asset.Name="v121 trimmed asset";asset.Id=Guid.NewGuid();asset.Clips[0].Duration=3;asset.Clips[0].SourceOffset=2;asset.Clips[0].SourceDuration=10;AssetStore.Save(asset);Call("RefreshAssets");list.SelectedItem=list.Items.Cast<EffectAsset>().First(a=>a.Name==asset.Name);Call("SelectRegion",area);Call("InsertAsset",w,new RoutedEventArgs());var trimmed=Field<EffectRegion>("_region").Clips[0];check(trimmed.Duration==3&&trimmed.SourceOffset==2&&trimmed.SourceDuration==10,"rebalancing a saved route asset preserves deliberate clip trimming");
    }
}
