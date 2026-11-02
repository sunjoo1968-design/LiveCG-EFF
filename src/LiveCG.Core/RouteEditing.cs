namespace LiveCG.Core;
public readonly record struct RouteVector(double X,double Y)
{
    public static RouteVector Lerp(RouteVector a,RouteVector b,double t)=>new(a.X+(b.X-a.X)*t,a.Y+(b.Y-a.Y)*t);
    public static RouteVector operator +(RouteVector a,RouteVector b)=>new(a.X+b.X,a.Y+b.Y);
    public static RouteVector operator -(RouteVector a,RouteVector b)=>new(a.X-b.X,a.Y-b.Y);
}
public readonly record struct RouteBezier(RouteVector P0,RouteVector C1,RouteVector C2,RouteVector P3)
{
    public RouteVector At(double t)
    {
        var one=RouteVector.Lerp(P0,P3,1d/3);var two=RouteVector.Lerp(P0,P3,2d/3);
        if(Math.Abs(C1.X-one.X)+Math.Abs(C1.Y-one.Y)+Math.Abs(C2.X-two.X)+Math.Abs(C2.Y-two.Y)<1e-10)return RouteVector.Lerp(P0,P3,t);
        var u=1-t;return new(u*u*u*P0.X+3*u*u*t*C1.X+3*u*t*t*C2.X+t*t*t*P3.X,u*u*u*P0.Y+3*u*u*t*C1.Y+3*u*t*t*C2.Y+t*t*t*P3.Y);
    }
}
public readonly record struct RouteIconPose(double Rotation,bool FlipX);
public static class RouteEditing
{
    public static string Role(TravelRoute route,int index)=>index==0?"출발":index==route.Points.Count-1?"도착":"경유 "+index;
    public static RouteIconPose IconPose(RouteDirection direction,double angleDegrees)
    {
        var radians=angleDegrees*Math.PI/180;var left=Math.Cos(radians)<-1e-9;
        if(direction==RouteDirection.Fixed)return new(0,false);
        if(direction==RouteDirection.Horizontal)return new(0,left);
        if(left&&Math.Abs(Math.Cos(radians))>=Math.Abs(Math.Sin(radians))-1e-9)return new(angleDegrees>0?angleDegrees-180:angleDegrees+180,true);
        return new(angleDegrees,false);
    }
    public static RouteBezier Piece(TravelRoute route,int leg,int piece)
    {
        var a=route.Points[leg];var b=route.Points[leg+1];
        if(!route.Curved)return new(new(a.X,a.Y),new(a.X+(b.X-a.X)/3,a.Y+(b.Y-a.Y)/3),new(a.X+2*(b.X-a.X)/3,a.Y+2*(b.Y-a.Y)/3),new(b.X,b.Y));
        var knots=b.CurvePoints;var start=piece==0?new RouteVector(a.X,a.Y):new(knots[piece-1].X,knots[piece-1].Y);var end=piece==knots.Count?new RouteVector(b.X,b.Y):new(knots[piece].X,knots[piece].Y);
        var output=piece==0?new RouteVector(a.OutX,a.OutY):new(knots[piece-1].OutX,knots[piece-1].OutY);var input=piece==knots.Count?new RouteVector(b.InX,b.InY):new(knots[piece].InX,knots[piece].InY);
        if(output.X==0&&output.Y==0)output=new((end.X-start.X)/3,(end.Y-start.Y)/3);if(input.X==0&&input.Y==0)input=new((start.X-end.X)/3,(start.Y-end.Y)/3);
        return new(start,start+output,end+input,end);
    }
    public static int InsertCurvePoint(TravelRoute route,int leg,int piece,double t)
    {
        var a=route.Points[leg];var b=route.Points[leg+1];if(b.CurvePoints.Count>=32||route.Points.Sum(p=>p.CurvePoints.Count)>=512)throw new InvalidDataException("곡선 조절점은 구간당32개·전체512개까지입니다.");
        var bezier=Piece(route,leg,piece);t=Math.Clamp(t,.001,.999);var q0=RouteVector.Lerp(bezier.P0,bezier.C1,t);var q1=RouteVector.Lerp(bezier.C1,bezier.C2,t);var q2=RouteVector.Lerp(bezier.C2,bezier.P3,t);var r0=RouteVector.Lerp(q0,q1,t);var r1=RouteVector.Lerp(q1,q2,t);var at=RouteVector.Lerp(r0,r1,t);
        var output=q0-bezier.P0;var input=q2-bezier.P3;
        if(piece==0){a.OutX=output.X;a.OutY=output.Y;}else{b.CurvePoints[piece-1].OutX=output.X;b.CurvePoints[piece-1].OutY=output.Y;}
        if(piece==b.CurvePoints.Count){b.InX=input.X;b.InY=input.Y;}else{b.CurvePoints[piece].InX=input.X;b.CurvePoints[piece].InY=input.Y;}
        route.Curved=true;b.CurvePoints.Insert(piece,new(){X=at.X,Y=at.Y,InX=r0.X-at.X,InY=r0.Y-at.Y,OutX=r1.X-at.X,OutY=r1.Y-at.Y});return piece;
    }
    public static int InsertWaypoint(TravelRoute route,int leg,int piece,double t)
    {
        if(route.Points.Count>=200)throw new InvalidDataException("경유점은 최대200개입니다.");
        var a=route.Points[leg];var b=route.Points[leg+1];var duration=b.Move;
        if(!route.Curved){var pos=RouteVector.Lerp(new(a.X,a.Y),new(b.X,b.Y),t);a.OutX=a.OutY=b.InX=b.InY=0;b.CurvePoints.Clear();b.Move=Math.Max(.016,duration*(1-t));route.Points.Insert(leg+1,new(){X=pos.X,Y=pos.Y,Name="",Move=Math.Max(.016,duration*t)});return leg+1;}
        var geometry=RouteRenderer.Geometry(route);var s=geometry.Segments[leg];var at=Math.Clamp(piece*64+t*64,0,s.Distance.Length-1);var index=Math.Min(s.Distance.Length-2,(int)at);var length=s.Distance[index]+(s.Distance[index+1]-s.Distance[index])*(at-index);var ratio=s.Length==0?.5:length/s.Length;
        var knot=InsertCurvePoint(route,leg,piece,t);var point=b.CurvePoints[knot];var added=new RoutePoint{X=point.X,Y=point.Y,InX=point.InX,InY=point.InY,OutX=point.OutX,OutY=point.OutY,Move=Math.Max(.016,duration*ratio),Name="",CurvePoints=b.CurvePoints.Take(knot).Select(p=>p with{}).ToList()};b.CurvePoints=b.CurvePoints.Skip(knot+1).Select(p=>p with{}).ToList();b.Move=Math.Max(.016,duration*(1-ratio));route.Points.Insert(leg+1,added);return leg+1;
    }
    public static void DeleteWaypoint(TravelRoute route,int index)
    {
        if(index<0||index>=route.Points.Count)return;route.Points.RemoveAt(index);if(index<route.Points.Count){var next=route.Points[index];next.CurvePoints.Clear();next.InX=next.InY=0;if(index>0)route.Points[index-1].OutX=route.Points[index-1].OutY=0;}
    }
    public static void MoveWaypoint(TravelRoute route,int from,int to)
    {
        if(from<0||from>=route.Points.Count||to<0||to>=route.Points.Count||from==to)return;
        var previous=new Dictionary<RoutePoint,RoutePoint?>(ReferenceEqualityComparer.Instance);for(var i=0;i<route.Points.Count;i++)previous[route.Points[i]]=i==0?null:route.Points[i-1];var point=route.Points[from];route.Points.RemoveAt(from);route.Points.Insert(to,point);
        for(var i=0;i<route.Points.Count;i++)if(!ReferenceEquals(previous[route.Points[i]],i==0?null:route.Points[i-1])){route.Points[i].CurvePoints.Clear();route.Points[i].InX=route.Points[i].InY=0;if(i>0)route.Points[i-1].OutX=route.Points[i-1].OutY=0;}
    }
    public static void AllocateDuration(TravelRoute route,double seconds,double width,double height,int fps)
    {
        if(!double.IsFinite(seconds)||seconds is <.1 or >300||fps is <1 or >240)throw new InvalidDataException("전체 시간은 0.1~300초입니다.");
        var target=Math.Max((int)Math.Ceiling(.1*fps),(int)Math.Round(seconds*fps,MidpointRounding.AwayFromZero));if(route.Points.Count<2){route.DurationBudget=(double)target/fps;return;}
        var stops=route.Points.Select(p=>(int)Math.Round(p.Stop*fps,MidpointRounding.AwayFromZero)).ToArray();var count=route.Points.Count-1;var minimum=Math.Max(1,(int)Math.Ceiling(.016*fps));var remaining=target-stops.Sum()-minimum*count;
        if(remaining<0)throw new InvalidDataException($"정차 합계 {(double)stops.Sum()/fps:0.##}초입니다. 전체 시간을 늘리거나 정차를 줄이세요.");
        var geometry=new RouteGeometry(route,width,height);var weights=geometry.Segments.Select(s=>s.Length).ToArray();var sum=weights.Sum();if(sum<1e-9){Array.Fill(weights,1);sum=count;}
        var allocated=new int[count];var active=Enumerable.Range(0,count).ToList();var available=target-stops.Sum();
        // Reserve the minimum only for very short legs; don't bias every leg toward equal duration.
        while(active.Count>0){var weight=active.Sum(i=>weights[i]);var shortLegs=active.Where(i=>available*(weight>0?weights[i]/weight:1d/active.Count)<minimum).ToArray();if(shortLegs.Length==0)break;foreach(var i in shortLegs){allocated[i]=minimum;available-=minimum;active.Remove(i);}}
        var activeWeight=active.Sum(i=>weights[i]);var raw=active.ToDictionary(i=>i,i=>available*(activeWeight>0?weights[i]/activeWeight:1d/active.Count));foreach(var i in active)allocated[i]=(int)Math.Floor(raw[i]);var extra=target-stops.Sum()-allocated.Sum();foreach(var i in active.OrderByDescending(i=>raw[i]-Math.Floor(raw[i])).ThenBy(i=>i).Take(extra))allocated[i]++;
        for(var i=0;i<route.Points.Count;i++)route.Points[i].Stop=(double)stops[i]/fps;for(var i=0;i<count;i++)route.Points[i+1].Move=(double)allocated[i]/fps;route.DurationBudget=(double)target/fps;
    }
}
