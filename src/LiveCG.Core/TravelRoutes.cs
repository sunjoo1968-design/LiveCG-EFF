using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using SkiaSharp;

namespace LiveCG.Core;
[JsonConverter(typeof(JsonStringEnumConverter))] public enum TravelIcon { Plane, Car, Camper, Bicycle, Custom }
[JsonConverter(typeof(JsonStringEnumConverter))] public enum RouteLineStyle { Solid, Dashed, Dotted, Arrows }
[JsonConverter(typeof(JsonStringEnumConverter))] public enum RouteDirection { Follow, Horizontal, Fixed }
public record RouteCurvePoint
{
    public double X { get; set; }public double Y { get; set; }
    public double InX { get; set; }public double InY { get; set; }public double OutX { get; set; }public double OutY { get; set; }
}
public record RoutePoint
{
    public double X { get; set; }=.2; public double Y { get; set; }=.5;
    public double InX { get; set; } public double InY { get; set; } public double OutX { get; set; } public double OutY { get; set; }
    public string Name { get; set; }="경유지";public double Stop { get; set; }=1;public double Move { get; set; }=2;
    public TravelIcon? Icon { get; set; }
    public List<RouteCurvePoint> CurvePoints { get; set; }=[];
    public RoutePoint Copy()=>this with{CurvePoints=CurvePoints.Select(p=>p with{}).ToList()};
}
public record TravelRoute
{
    public List<RoutePoint> Points { get; set; }=[];
    public static TravelRoute Example()=>new(){Points=[new(){X=.15,Y=.65,Name="출발"},new(){X=.5,Y=.3,Name="경유지"},new(){X=.85,Y=.6,Name="도착"}]};
    public double? DurationBudget { get; set; }
    public TravelIcon Icon { get; set; }=TravelIcon.Plane;public string CustomPng { get; set; }="";
    public RouteDirection Direction { get; set; }=RouteDirection.Follow;public bool Animate { get; set; }public bool Curved { get; set; }public bool Repeat { get; set; }=true;
    public RouteLineStyle LineStyle { get; set; }=RouteLineStyle.Dashed;public string Color { get; set; }="#58F5A6";public string OutlineColor { get; set; }="#102947";
    public double LineWidth { get; set; }=4;public double Opacity { get; set; }=1;public double Outline { get; set; }=2;public double IconSize { get; set; }=42;
    public bool FuturePath { get; set; }public bool Arrival { get; set; }=true;
    [JsonIgnore]public double Duration=>Points.Sum(p=>p.Stop)+Points.Skip(1).Sum(p=>p.Move);
    public TravelRoute Copy()=>this with{Points=Points.Select(p=>p.Copy()).ToList()};
    public void Validate()
    {
        if(Points is null||Points.Count is <2 or >200||!Enum.IsDefined(Icon)||!Enum.IsDefined(Direction)||!Enum.IsDefined(LineStyle)||!SKColor.TryParse(Color,out _)||!SKColor.TryParse(OutlineColor,out _)||CustomPng is null||CustomPng.Length>2000000)throw new InvalidDataException("여행 경로·색상·아이콘을 확인하세요.");
        foreach(var p in Points){if(p is null||p.Name is null||p.Name.Length>100||new[]{p.X,p.Y,p.InX,p.InY,p.OutX,p.OutY,p.Stop,p.Move}.Any(v=>!double.IsFinite(v))||p.X is <0 or >1||p.Y is <0 or >1||Math.Abs(p.InX)>1||Math.Abs(p.InY)>1||Math.Abs(p.OutX)>1||Math.Abs(p.OutY)>1||p.Stop is <0 or >30||p.Move is <.016 or >300||p.Icon is { } kind&&!Enum.IsDefined(kind)||p.CurvePoints is null||p.CurvePoints.Count>32)throw new InvalidDataException("경유점 좌표·이동·정차 시간을 확인하세요.");foreach(var q in p.CurvePoints)if(q is null||new[]{q.X,q.Y,q.InX,q.InY,q.OutX,q.OutY}.Any(v=>!double.IsFinite(v))||q.X is <0 or >1||q.Y is <0 or >1||new[]{q.InX,q.InY,q.OutX,q.OutY}.Any(v=>Math.Abs(v)>1))throw new InvalidDataException("곡선 조절점을 확인하세요.");}
        if(Points.Sum(p=>p.CurvePoints.Count)>512||DurationBudget is { } budget&&(!double.IsFinite(budget)||budget is <.1 or >300||Math.Abs(Duration-budget)>.0001))throw new InvalidDataException("전체 시간에 맞춰 이동 시간을 다시 배분하세요.");
        if((Icon==TravelIcon.Custom||Points.Any(p=>p.Icon==TravelIcon.Custom))&&CustomPng.Length==0)throw new InvalidDataException("사용자 아이콘 PNG / WebP 파일을 불러오세요.");
        if(!double.IsFinite(Duration)||Duration is <.1 or >300||!double.IsFinite(LineWidth+Opacity+Outline+IconSize)||LineWidth is <1 or >30||Opacity is <0 or >1||Outline is <0 or >10||IconSize is <8 or >200)throw new InvalidDataException("경로는 최대300초, 선1~30px, 아이콘8~200px입니다.");
        if(CustomPng.Length>0){using var codec=SKCodec.Create(new SKMemoryStream(Convert.FromBase64String(CustomPng)));if(codec is null||codec.Info.Width>512||codec.Info.Height>512)throw new InvalidDataException("사용자 아이콘은 최대512px PNG입니다.");}
    }
}
public sealed record RouteSegment(double Start,double Duration,double Length,double[] X,double[] Y,double[] Distance);
public sealed record RouteSample(double X,double Y,double Angle,double Distance,int Point,int Segment,double Fraction,bool Stopped,TravelIcon Icon);
public sealed class RouteGeometry
{
    public List<RouteSegment> Segments { get; }=[];public double TotalLength { get; }
    public RouteGeometry(TravelRoute route,double width=1,double height=1)
    {
        double clock=route.Points.FirstOrDefault()?.Stop??0,total=0;
        for(var i=1;i<route.Points.Count;i++)
        {
            var b=route.Points[i];var pieces=route.Curved?b.CurvePoints.Count+1:1;var xs=new double[pieces*64+1];var ys=new double[xs.Length];var ds=new double[xs.Length];
            for(var piece=0;piece<pieces;piece++){var bezier=RouteEditing.Piece(route,i-1,piece);for(var j=0;j<=64;j++){var index=piece*64+j;var p=bezier.At(j/64d);xs[index]=p.X;ys[index]=p.Y;if(index>0)ds[index]=ds[index-1]+Math.Sqrt(Math.Pow((xs[index]-xs[index-1])*width,2)+Math.Pow((ys[index]-ys[index-1])*height,2));}}
            Segments.Add(new(clock,b.Move,ds[^1],xs,ys,ds));clock+=b.Move+b.Stop;total+=ds[^1];
        }TotalLength=total;
    }
    public RouteSample Sample(TravelRoute route,double seconds)
    {
        if(route.Points.Count==0)return new(0,0,0,0,0,0,0,true,route.Icon);var icon=route.Points[0].Icon??route.Icon;var first=route.Points[0];if(seconds<first.Stop)return new(first.X,first.Y,Segments.Count==0?0:Math.Atan2(Segments[0].Y[1]-first.Y,Segments[0].X[1]-first.X),0,0,0,0,true,icon);
        double completed=0;for(var i=0;i<Segments.Count;i++)
        {
            var s=Segments[i];var p=route.Points[i+1];icon=route.Points[i].Icon??icon;
            if(seconds<s.Start+s.Duration){var f=Math.Clamp((seconds-s.Start)/s.Duration,0,1);var d=f*s.Length;var j=1;while(j<s.Distance.Length-1&&s.Distance[j]<d)j++;var mix=(d-s.Distance[j-1])/Math.Max(1e-12,s.Distance[j]-s.Distance[j-1]);var x=s.X[j-1]+(s.X[j]-s.X[j-1])*mix;var y=s.Y[j-1]+(s.Y[j]-s.Y[j-1])*mix;return new(x,y,Math.Atan2(s.Y[j]-s.Y[j-1],s.X[j]-s.X[j-1]),completed+d,i,i,f,false,icon);}
            completed+=s.Length;if(seconds<s.Start+s.Duration+p.Stop||i==Segments.Count-1)return new(p.X,p.Y,Math.Atan2(s.Y[^1]-s.Y[^2],s.X[^1]-s.X[^2]),completed,i+1,i,1,true,p.Icon??icon);
        }return new(first.X,first.Y,0,0,0,0,0,true,icon);
    }
    public bool EmphasizeStop(TravelRoute route,RouteSample sample,double seconds)
    {
        if(!route.Arrival||!sample.Stopped||sample.Point<0||sample.Point>=route.Points.Count)return false;
        var stop=route.Points[sample.Point].Stop;var start=sample.Point==0?0:Segments[sample.Point-1].Start+Segments[sample.Point-1].Duration;
        return stop>0&&seconds>=start&&seconds<start+stop;
    }
}
public static class RouteRenderer
{
    private sealed class Cache { public string Key="",Custom="";public RouteGeometry? Geometry;public Dictionary<TravelIcon,SKBitmap> Icons=[]; }
    private static readonly ConditionalWeakTable<TravelRoute,Cache> Caches=new();
    public static RouteGeometry Geometry(TravelRoute route,double width=1,double height=1)
    {
        var cache=Caches.GetOrCreateValue(route);var key=string.Join(';',route.Points.Select(p=>FormattableString.Invariant($"{p.X},{p.Y},{p.InX},{p.InY},{p.OutX},{p.OutY},{p.Stop},{p.Move}:")+string.Join('/',p.CurvePoints.Select(q=>FormattableString.Invariant($"{q.X},{q.Y},{q.InX},{q.InY},{q.OutX},{q.OutY}")))))+FormattableString.Invariant($"{route.Curved}:{width}:{height}");
        if(cache.Geometry is null||cache.Key!=key){cache.Key=key;cache.Geometry=new(route,width,height);}return cache.Geometry;
    }
    public static SKBitmap Icon(TravelRoute route,TravelIcon kind)
    {
        if(kind==TravelIcon.Custom&&route.CustomPng.Length>0)return SKBitmap.Decode(Convert.FromBase64String(route.CustomPng));
        var bitmap=new SKBitmap(128,128);bitmap.Erase(SKColors.Transparent);using var c=new SKCanvas(bitmap);using var p=new SKPaint{Color=SKColors.White,IsAntialias=true,Style=SKPaintStyle.Fill};using var edge=new SKPaint{Color=new(14,37,64),IsAntialias=true,Style=SKPaintStyle.Stroke,StrokeWidth=4,StrokeJoin=SKStrokeJoin.Round};using var path=new SKPath();
        if(kind is TravelIcon.Plane or TravelIcon.Custom){path.MoveTo(113,64);path.LineTo(76,53);path.LineTo(48,15);path.LineTo(34,15);path.LineTo(50,55);path.LineTo(21,56);path.LineTo(10,40);path.LineTo(4,40);path.LineTo(12,64);path.LineTo(4,88);path.LineTo(10,88);path.LineTo(21,72);path.LineTo(50,73);path.LineTo(34,113);path.LineTo(48,113);path.LineTo(76,75);path.Close();c.DrawPath(path,p);c.DrawPath(path,edge);}
        else if(kind==TravelIcon.Bicycle){p.Style=SKPaintStyle.Stroke;p.Color=new(255,233,114);p.StrokeWidth=6;c.DrawCircle(29,88,21,p);c.DrawCircle(99,88,21,p);c.DrawLine(29,88,52,51,p);c.DrawLine(52,51,72,88,p);c.DrawLine(72,88,29,88,p);c.DrawLine(52,51,86,51,p);c.DrawLine(86,51,72,88,p);c.DrawLine(86,51,99,88,p);c.DrawLine(86,51,83,40,p);c.DrawLine(77,40,93,40,p);c.DrawLine(43,49,58,49,p);}
        else{var camper=kind==TravelIcon.Camper;c.DrawRoundRect(new(9,camper?31:54,114,91),9,9,p);if(!camper)c.DrawRoundRect(new(32,35,89,75),8,8,p);c.DrawRoundRect(new(9,camper?31:54,114,91),9,9,edge);p.Color=new(70,171,219);c.DrawRect(new(78,43,101,62),p);if(camper){c.DrawRect(new(24,43,48,64),p);c.DrawRect(new(56,44,69,85),p);}else c.DrawRect(new(42,43,65,58),p);p.Color=new(14,37,64);c.DrawCircle(31,91,13,p);c.DrawCircle(94,91,13,p);p.Color=SKColors.LightGray;c.DrawCircle(31,91,6,p);c.DrawCircle(94,91,6,p);}
        return bitmap;
    }
    public static void Draw(SKCanvas canvas,TravelRoute route,float w,float h,double progress,float opacity=1)
    {
        if(route.Points.Count<2)return;var geometry=Geometry(route,w,h);var sample=geometry.Sample(route,Math.Clamp(progress,0,1)*route.Duration);using var full=new SKPath();using var past=new SKPath();double used=0;
        foreach(var s in geometry.Segments){full.MoveTo((float)s.X[0]*w,(float)s.Y[0]*h);past.MoveTo((float)s.X[0]*w,(float)s.Y[0]*h);for(var j=1;j<s.X.Length;j++){full.LineTo((float)s.X[j]*w,(float)s.Y[j]*h);if(used+s.Distance[j]<=sample.Distance)past.LineTo((float)s.X[j]*w,(float)s.Y[j]*h);else if(used+s.Distance[j-1]<sample.Distance){past.LineTo((float)sample.X*w,(float)sample.Y*h);}}used+=s.Length;}
        using var p=new SKPaint{IsAntialias=true,Style=SKPaintStyle.Stroke,StrokeWidth=(float)route.LineWidth,StrokeCap=SKStrokeCap.Round,StrokeJoin=SKStrokeJoin.Round};
        var alpha=route.Opacity*opacity;var lineColor=SKColor.Parse(route.Color);var borderColor=SKColor.Parse(route.OutlineColor);if(route.FuturePath){p.Color=lineColor.WithAlpha((byte)(lineColor.Alpha*60/255d*alpha));canvas.DrawPath(full,p);}if(route.Outline>0){p.StrokeWidth=(float)(route.LineWidth+route.Outline*2);p.Color=borderColor.WithAlpha((byte)(borderColor.Alpha*alpha));canvas.DrawPath(past,p);}
        p.StrokeWidth=(float)route.LineWidth;p.Color=lineColor.WithAlpha((byte)(lineColor.Alpha*alpha));if(route.LineStyle is RouteLineStyle.Dashed or RouteLineStyle.Dotted)p.PathEffect=SKPathEffect.CreateDash(route.LineStyle==RouteLineStyle.Dotted?[1,(float)route.LineWidth*2]:[12,8],0);canvas.DrawPath(past,p);p.PathEffect?.Dispose();p.PathEffect=null;
        if(route.LineStyle==RouteLineStyle.Arrows)foreach(var s in geometry.Segments)for(var j=8;j<s.X.Length;j+=16){var before=geometry.Segments.TakeWhile(v=>v!=s).Sum(v=>v.Length);if(before+s.Distance[j]>sample.Distance)continue;canvas.Save();canvas.Translate((float)s.X[j]*w,(float)s.Y[j]*h);canvas.RotateDegrees((float)(Math.Atan2((s.Y[j]-s.Y[j-1])*h,(s.X[j]-s.X[j-1])*w)*180/Math.PI));p.Style=SKPaintStyle.Stroke;using var arrow=new SKPath();arrow.MoveTo(-7,-5);arrow.LineTo(0,0);arrow.LineTo(-7,5);canvas.DrawPath(arrow,p);canvas.Restore();}
        if(geometry.EmphasizeStop(route,sample,Math.Clamp(progress,0,1)*route.Duration)){p.Color=lineColor.WithAlpha((byte)(lineColor.Alpha*200/255d*alpha));p.StrokeWidth=2;canvas.DrawCircle((float)sample.X*w,(float)sample.Y*h,(float)route.IconSize*.65f,p);}
        var angle=Math.Atan2(Math.Sin(sample.Angle)*h,Math.Cos(sample.Angle)*w)*180/Math.PI;canvas.Save();canvas.Translate((float)sample.X*w,(float)sample.Y*h);var pose=RouteEditing.IconPose(route.Direction,angle);canvas.RotateDegrees((float)pose.Rotation);if(pose.FlipX)canvas.Scale(-1,1);if(route.Animate&&!sample.Stopped){canvas.Translate(0,(float)Math.Sin(progress*route.Duration*8)*2);canvas.RotateDegrees((float)Math.Sin(progress*route.Duration*6)*3);}
        var cache=Caches.GetOrCreateValue(route);if(cache.Custom!=route.CustomPng){foreach(var old in cache.Icons.Values)old.Dispose();cache.Icons.Clear();cache.Custom=route.CustomPng;}if(!cache.Icons.TryGetValue(sample.Icon,out var icon)){icon=Icon(route,sample.Icon);cache.Icons.Add(sample.Icon,icon);}using var image=SKImage.FromBitmap(icon);using var ip=new SKPaint{Color=SKColors.White.WithAlpha((byte)(255*opacity)),IsAntialias=true};var size=(float)route.IconSize;var scale=Math.Min(size/icon.Width,size/icon.Height);canvas.DrawImage(image,SKRect.Create(-icon.Width*scale/2,-icon.Height*scale/2,icon.Width*scale,icon.Height*scale),new SKSamplingOptions(SKFilterMode.Linear),ip);canvas.Restore();
    }
}
