using LiveCG.Core;
using SkiaSharp;
internal static class V121Checks
{
    public static async Task Run(string folder,Action<bool,string> check)
    {
        Repro(check);
        foreach(var angle in new[]{-180d,-150,-135,-30,0,30,135,150,180})
        {
            var pose=RouteEditing.IconPose(RouteDirection.Follow,angle);var radians=pose.Rotation*Math.PI/180;
            var forwardX=Math.Cos(radians)*(pose.FlipX?-1:1);var forwardY=Math.Sin(radians)*(pose.FlipX?-1:1);
            check(Math.Cos(radians)>0&&Math.Abs(forwardX-Math.Cos(angle*Math.PI/180))<1e-8&&Math.Abs(forwardY-Math.Sin(angle*Math.PI/180))<1e-8,"roof up with forward heading at "+angle+" degrees");
        }
        foreach(var angle in new[]{-110d,-90,75,90,110}){var pose=RouteEditing.IconPose(RouteDirection.Follow,angle);check(pose.Rotation==angle&&!pose.FlipX,"vertical-family heading retained at "+angle);}
        check(RouteEditing.IconPose(RouteDirection.Horizontal,180)==new RouteIconPose(0,true)&&RouteEditing.IconPose(RouteDirection.Fixed,180)==new RouteIconPose(0,false),"explicit horizontal and fixed direction modes retained");
        var route=new TravelRoute{Points=[new(){X=.1,Y=.2,Stop=0},new(){X=.9,Y=.2,Stop=0},new(){X=.9,Y=.8,Stop=0}]};
        RouteEditing.AllocateDuration(route,10,1200,400,30);check(Math.Abs(route.Duration-10)<1e-9&&Math.Abs(route.Points[1].Move/route.Points[2].Move-4)<.08,"10 seconds allocate by actual 4:1 pixel lengths within one frame rounding");
        check(route.Points.Skip(1).All(p=>Math.Abs(p.Move*30-Math.Round(p.Move*30))<1e-8),"automatic leg times use whole frames");
        var minimal=new TravelRoute{Points=[new(){X=.1,Y=.2,Stop=0},new(){X=.9,Y=.2,Stop=0}]};RouteEditing.AllocateDuration(minimal,.1,640,400,24);minimal.Validate();check(minimal.Duration==.125,"minimum total duration rounds up to a valid 24fps frame boundary");
        var tiny=new TravelRoute{Points=[new(){X=0,Y=0,Stop=0},new(){X=0,Y=0,Stop=0},new(){X=1,Y=1,Stop=0}]};RouteEditing.AllocateDuration(tiny,10,640,400,30);check(tiny.Points[1].Move==1d/30&&tiny.Duration==10,"zero-length leg reserves one frame and gives remaining time to the actual journey");
        route.Points[1].Stop=2;RouteEditing.AllocateDuration(route,10,1200,400,30);check(Math.Abs(route.Duration-10)<1e-9&&route.Points[1].Move<8,"total duration includes waypoint stops");
        var before=route.Copy();try{RouteEditing.AllocateDuration(route,1,1200,400,30);check(false,"insufficient duration rejects without changing timing");}catch(InvalidDataException){check(route.Points.Select(p=>p.Move).SequenceEqual(before.Points.Select(p=>p.Move))&&route.DurationBudget==10,"insufficient duration rejects without changing timing");}
        route.Points.ForEach(p=>p.Stop=0);RouteEditing.AllocateDuration(route,2.013,1200,400,60);check(route.DurationBudget==121d/60&&Math.Abs(route.Duration-121d/60)<1e-8,"total budget snaps to requested output frame rate");
        var curve=new TravelRoute{Curved=true,Points=[new(){X=.1,Y=.5,Stop=0,OutX=.2,OutY=-.3},new(){X=.9,Y=.5,Stop=0,InX=-.2,InY=.3}]};var original=RouteEditing.Piece(curve,0,0);const double split=.37;var knot=RouteEditing.InsertCurvePoint(curve,0,0,split);
        bool same=true;for(var i=0;i<=100;i++){var t=i/100d;var expected=original.At(t);var actual=t<=split?RouteEditing.Piece(curve,0,0).At(t/split):RouteEditing.Piece(curve,0,1).At((t-split)/(1-split));same&=Math.Abs(expected.X-actual.X)+Math.Abs(expected.Y-actual.Y)<1e-8;}check(same,"inserting a curve control preserves the entire existing Bezier before dragging");
        curve.Points[1].CurvePoints[knot].Y=.25;RouteEditing.InsertCurvePoint(curve,0,1,.5);check(curve.Points.Count==2&&curve.Points[1].CurvePoints.Count==2&&RouteRenderer.Geometry(curve).Segments[0].X.Length==193,"multiple bend controls add samples without extra waypoints or stops");
        var copied=curve.Copy();copied.Points[1].CurvePoints[0].Y=.8;check(curve.Points[1].CurvePoints[0].Y==.25,"curve controls deep copy for history and assets");
        var straightened=curve.Copy();straightened.Curved=false;RouteEditing.InsertWaypoint(straightened,0,0,.5);straightened.Curved=true;check(straightened.Points[2].CurvePoints.Count==0&&Math.Abs(RouteRenderer.Geometry(straightened).Segments[0].Y[32]-.5)<1e-8,"inserting into a straightened leg cannot resurrect its obsolete hidden curve controls");
        var oldStops=curve.Points.Sum(p=>p.Stop);RouteEditing.InsertWaypoint(curve,0,1,.5);check(curve.Points.Count==3&&curve.Points.Sum(p=>p.Stop)==oldStops+.5,"context waypoint insertion creates a real optional stop");
        RouteEditing.MoveWaypoint(curve,2,0);check(RouteEditing.Role(curve,0)=="출발"&&RouteEditing.Role(curve,1)=="경유 1"&&RouteEditing.Role(curve,2)=="도착","endpoint role edits keep one departure and arrival by reordering");while(curve.Points.Count>0)RouteEditing.DeleteWaypoint(curve,0);check(curve.Points.Count==0&&RouteRenderer.Geometry(curve).Segments.Count==0,"deleting every waypoint returns to a safe empty editor");
        using var map=new SKBitmap(640,400);map.Erase(new(20,46,72));
        var left=new TravelRoute{Icon=TravelIcon.Camper,Points=[new(){X=.85,Y=.6,Name="DO NOT OUTPUT A"},new(){X=.15,Y=.3,Name="DO NOT OUTPUT B"},new(){X=.85,Y=.6,Icon=TravelIcon.Bicycle}]};
        var vertical=new TravelRoute{Icon=TravelIcon.Plane,Points=[new(){X=.5,Y=.15},new(){X=.55,Y=.85},new(){X=.4,Y=.15,Icon=TravelIcon.Car}]};
        var bends=new TravelRoute{Curved=true,Icon=TravelIcon.Car,LineStyle=RouteLineStyle.Arrows,Points=[new(){X=.1,Y=.55},new(){X=.9,Y=.55}]};RouteEditing.InsertCurvePoint(bends,0,0,.35);bends.Points[1].CurvePoints[0].Y=.2;RouteEditing.InsertCurvePoint(bends,0,1,.55);bends.Points[1].CurvePoints[1].Y=.8;RouteEditing.AllocateDuration(bends,10,640,400,30);
        await Fixture("Upright",left,new(0,0,1,1),[0,2,4.5,7]);await Fixture("Vertical",vertical,new(0,0,1,1),[0,2,4.5,7]);await Fixture("Bends",bends,new(0,0,1,1),[.5,2,5,9.5]);await Fixture("ROI",bends.Copy(),new(.2,.25,.6,.45),[2,5,9.5]);
        async Task Fixture(string name,TravelRoute data,NormalizedRect bounds,double[] times)
        {
            if(data.DurationBudget is { } budget)RouteEditing.AllocateDuration(data,budget,bounds.Width*640,bounds.Height*400,30);data.Validate();var directory=Path.Combine(folder,"route-"+name+".obs");var path=Path.Combine(folder,"v121-map.png");SourceImage.SavePng(map,path);var job=new ImageJob{SourcePath=path,Width=640,Height=400,Regions=[new(){Bounds=bounds,OverlayOnly=true,Background=BackgroundMode.KeepOriginal,Clips=[new(){Duration=data.Duration,Route=data,Options=new(){Kind=EffectKind.TravelRoute}}]}]};
            await new BrowserExporter().ExportAsync(job,new(){Fps=30,EndHold=0},directory,true,null,CancellationToken.None);Directory.CreateDirectory(Path.Combine(directory,"expected"));using var scene=new EffectScene(map);foreach(var t in times){using var frame=scene.Render(job.Regions,new(){Fps=30},t);SourceImage.SavePng(frame,Path.Combine(directory,"expected",t.ToString("0.00",System.Globalization.CultureInfo.InvariantCulture)+".png"));check(frame.GetPixel(0,0)==map.GetPixel(0,0),"new "+name+" route leaves outside map pixels unchanged at "+t);}
            var file=Path.Combine(folder,"v121-route.json");DocumentStore.Save(file,new ProjectDocument{Jobs=[job]});var saved=DocumentStore.Load<ProjectDocument>(file).Jobs[0].Regions[0].Clips[0].Route!;check(saved.Points.Sum(p=>p.CurvePoints.Count)==data.Points.Sum(p=>p.CurvePoints.Count)&&saved.DurationBudget==data.DurationBudget,"new "+name+" route project preserves bends and duration budget");
        }
    }
    public static void Repro(Action<bool,string> check)
    {
        check(new TravelRoute().Points.Count==0,"new route opens with no pre-positioned departure or arrival points");
        check(!new TravelRoute().FuturePath&&new TravelRoute().Arrival&&new TravelRoute().Repeat,"new route defaults hide future path and enable arrival emphasis and repeat");
        var route=new TravelRoute{Points=[new(){X=.1,Y=.5},new(){X=.9,Y=.5}]};var straight=RouteRenderer.Geometry(route).Segments[0].Y.ToArray();route.Curved=true;var curved=RouteRenderer.Geometry(route).Segments[0].Y;check(straight.SequenceEqual(curved),"enabling smooth curves alone preserves the straight path");
        route.Points[0].Name="FIRST";route.Points[1].Name="LAST";using var a=new SKBitmap(400,240);a.Erase(SKColors.Transparent);using(var c=new SKCanvas(a))RouteRenderer.Draw(c,route,400,240,0);route.Points[0].Name="DIFFERENT";using var b=new SKBitmap(400,240);b.Erase(SKColors.Transparent);using(var c=new SKCanvas(b))RouteRenderer.Draw(c,route,400,240,0);check(a.Bytes.SequenceEqual(b.Bytes),"arrival emphasis never renders waypoint names into the output");
    }
}
