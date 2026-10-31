using System.Text.Json.Serialization;
using SkiaSharp;

namespace LiveCG.Core;
[JsonConverter(typeof(JsonStringEnumConverter))]public enum MaskOperation { Add, Subtract, Intersect }
[JsonConverter(typeof(JsonStringEnumConverter))]public enum MaskEndMode { Hold, Release, Zero }
public record MaskTrack
{
    public Guid Id { get; set; }=Guid.NewGuid();public string Name { get; set; }="마스크";
    public bool Enabled { get; set; }=true;public bool Invert { get; set; }public MaskOperation Operation { get; set; }
    public bool StartAtInPoint { get; set; }
    public RegionShape Shape { get; set; }public List<RegionPoint> Points { get; set; }=[];public NormalizedRect Bounds { get; set; }=new(0,0,1,1);
    public double Start { get; set; }public double Duration { get; set; }=3;public double Opacity { get; set; }=1;public double Feather { get; set; }public double Expand { get; set; }public MaskEndMode EndMode { get; set; }
    public MotionDefinition? Motion { get; set; }
    [JsonIgnore]public double End=>Start+Duration;
    [JsonIgnore]public string Label=>$"{(Enabled?"◩":"—")} {Name} · {Operation switch{MaskOperation.Subtract=>"가릴 곳",MaskOperation.Intersect=>"겹친 곳",_=>"보일 곳"}}{(Invert?" 반전":"")}";
    public MaskTrack Copy(bool newId=true)=>this with{Id=newId?Guid.NewGuid():Id,Points=Points.Select(p=>p with{}).ToList(),Motion=Motion?.Copy()};
    public void Validate()
    {
        if(Id==Guid.Empty||string.IsNullOrWhiteSpace(Name)||Name.Length>100||!Enum.IsDefined(Operation)||!Enum.IsDefined(EndMode)||!double.IsFinite(Start+Duration+Opacity+Feather+Expand)||Start<0||Duration<.016||Duration>3600||End>3600||Opacity is <0 or >1||Feather is <0 or >50||Expand is <-32 or >32)throw new InvalidDataException("마스크 시간/불투명도/경계 값을 확인하세요.");
        new EffectRegion{Bounds=Bounds,Shape=Shape,Points=Points}.Validate();Motion?.Validate();
    }
}
public sealed class MaskRenderer:IDisposable
{
    private sealed record Cached(MaskTrack Track,int W,int H,int SourceW,int SourceH,SKRectI Parent,SKBitmap Bitmap);
    private readonly Dictionary<Guid,Cached> _cache=[];
    private const long CacheLimit=128L*1024*1024;
    private static readonly SKSamplingOptions Linear=new(SKFilterMode.Linear);
    public SKBitmap Render(IReadOnlyList<MaskTrack> tracks,int w,int h,int sourceW,int sourceH,SKRectI parent,double time)
    {
        var active=tracks.Where(m=>m.Enabled&&!(m.StartAtInPoint&&time<m.Start)&&!(time>=m.End&&m.EndMode==MaskEndMode.Release)).ToList();var result=new SKBitmap(w,h);using var c=new SKCanvas(result);
        c.Clear(active.Count==0||active[0].Operation!=MaskOperation.Add?SKColors.White:SKColors.Transparent);
        foreach(var track in active)
        {
            using var mask=RenderOne(track,w,h,sourceW,sourceH,parent,time);using var image=SKImage.FromBitmap(mask);using var p=new SKPaint{BlendMode=track.Operation switch{MaskOperation.Subtract=>SKBlendMode.DstOut,MaskOperation.Intersect=>SKBlendMode.DstIn,_=>SKBlendMode.SrcOver}};c.DrawImage(image,0,0,p);
        }
        return result;
    }
    public SKBitmap RenderOne(MaskTrack track,int w,int h,int sourceW,int sourceH,SKRectI parent,double time)
    {
        var output=new SKBitmap(w,h);output.Erase(SKColors.Transparent);if(time<track.Start||(time>=track.End&&track.EndMode==MaskEndMode.Zero))return output;
        if(!_cache.TryGetValue(track.Id,out var cached)||cached.W!=w||cached.H!=h||cached.SourceW!=sourceW||cached.SourceH!=sourceH||cached.Parent!=parent||cached.Track.Bounds!=track.Bounds||cached.Track.Shape!=track.Shape||cached.Track.Expand!=track.Expand||cached.Track.Feather!=track.Feather||!cached.Track.Points.SequenceEqual(track.Points))
        {
            cached?.Bitmap.Dispose();var bitmap=new SKBitmap(w,h);bitmap.Erase(SKColors.Transparent);using(var c=new SKCanvas(bitmap))
            {
                var rect=track.Bounds.Pixels(sourceW,sourceH);rect.Offset(-parent.Left,-parent.Top);using var path=RegionMask.Create(track.Shape,rect,track.Points);using var p=new SKPaint{IsAntialias=true,Color=SKColors.White};
                if(track.Feather>0)p.ImageFilter=SKImageFilter.CreateBlur((float)track.Feather,(float)track.Feather);
                if(track.Expand>=0){c.DrawPath(path,p);if(track.Expand>0){p.Style=SKPaintStyle.Stroke;p.StrokeWidth=(float)track.Expand*2;c.DrawPath(path,p);}}
                else{c.DrawPath(path,p);p.BlendMode=SKBlendMode.DstOut;p.Style=SKPaintStyle.Stroke;p.StrokeWidth=(float)-track.Expand*2;c.DrawPath(path,p);}p.ImageFilter?.Dispose();
            }
            _cache.Remove(track.Id);
            while(_cache.Count>0&&_cache.Values.Sum(x=>(long)x.Bitmap.ByteCount)+bitmap.ByteCount>CacheLimit){var first=_cache.First();first.Value.Bitmap.Dispose();_cache.Remove(first.Key);}
            cached=new(track.Copy(false),w,h,sourceW,sourceH,parent,bitmap);_cache[track.Id]=cached;
        }
        var progress=Math.Clamp((time-track.Start)/track.Duration,0,1);var frame=progress*(track.Motion?.LengthFrames??1);var motion=track.Motion;
        var bounds=track.Bounds.Pixels(sourceW,sourceH);bounds.Offset(-parent.Left,-parent.Top);var ax=(float)(motion?.Value(MotionProperty.AnchorX,frame)??.5);var ay=(float)(motion?.Value(MotionProperty.AnchorY,frame)??.5);var anchor=new SKPoint(bounds.Left+bounds.Width*ax,bounds.Top+bounds.Height*ay);
        using(var c=new SKCanvas(output))using(var image=SKImage.FromBitmap(cached.Bitmap))
        {c.Translate(anchor.X+(float)(motion?.Value(MotionProperty.X,frame)??0)*bounds.Width,anchor.Y+(float)(motion?.Value(MotionProperty.Y,frame)??0)*bounds.Height);c.RotateDegrees((float)(motion?.Value(MotionProperty.Rotation,frame)??0));c.Scale((float)(motion?.Value(MotionProperty.ScaleX,frame)??1),(float)(motion?.Value(MotionProperty.ScaleY,frame)??1));c.Translate(-anchor.X,-anchor.Y);if(motion is not null){var l=(float)motion.Value(MotionProperty.CropLeft,frame);var r=(float)motion.Value(MotionProperty.CropRight,frame);var t=(float)motion.Value(MotionProperty.CropTop,frame);var b=(float)motion.Value(MotionProperty.CropBottom,frame);if(l+r+t+b>0)c.ClipRect(SKRect.Create(bounds.Left+bounds.Width*l,bounds.Top+bounds.Height*t,Math.Max(0,bounds.Width*(1-l-r)),Math.Max(0,bounds.Height*(1-t-b))));}c.DrawImage(image,0,0,Linear);}
        if(track.Invert){using var copy=output.Copy();using var image=SKImage.FromBitmap(copy);using var c=new SKCanvas(output);c.Clear(SKColors.White);using var p=new SKPaint{BlendMode=SKBlendMode.DstOut};c.DrawImage(image,0,0,p);}
        var opacity=track.Opacity*(motion?.Value(MotionProperty.Opacity,frame)??1);if(opacity<1){using var c=new SKCanvas(output);using var p=new SKPaint{BlendMode=SKBlendMode.DstIn,Color=SKColors.White.WithAlpha((byte)(Math.Clamp(opacity,0,1)*255))};c.DrawRect(SKRect.Create(w,h),p);}return output;
    }
    public void Dispose(){foreach(var cached in _cache.Values)cached.Bitmap.Dispose();_cache.Clear();}
}
