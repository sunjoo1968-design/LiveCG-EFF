using System.Text.Json.Serialization;
using SkiaSharp;

namespace LiveCG.Core;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SoftDirection { Inside, Outside, Both }
public record RegionSoftness
{
    public bool Enabled { get; set; }
    public double Width { get; set; }=12;
    public double Strength { get; set; }=1;
    public SoftDirection Direction { get; set; }=SoftDirection.Inside;
    public bool SeparateSides { get; set; }
    public double Top { get; set; }=12;
    public double Right { get; set; }=12;
    public double Bottom { get; set; }=12;
    public double Left { get; set; }=12;
    [JsonIgnore] public double Maximum=>!Enabled?0:SeparateSides?Math.Max(Math.Max(Top,Right),Math.Max(Bottom,Left)):Width;
    [JsonIgnore] public double Padding=>Direction==SoftDirection.Inside?0:Maximum*(Direction==SoftDirection.Both?.5:1);
    public void Validate()
    {if(!Enum.IsDefined(Direction)||new[]{Width,Top,Right,Bottom,Left}.Any(v=>!double.IsFinite(v)||v is <0 or >256)||!double.IsFinite(Strength)||Strength is <.1 or >3)throw new InvalidDataException("소프트 폭 0~256px, 강도 0.1~3을 확인하세요.");}
}

/// <summary>Shared source-space geometry and alpha for native rendering and exported browser assets.</summary>
public static class RegionRaster
{
    public static bool Custom(EffectRegion r)=>r.OverlayOnly||r.Softness.Enabled||r.ContentOnly||(!r.CutoutEnabled&&r.Cutout is not null);
    public static ObjectCutout? ActiveCutout(EffectRegion r)=>r.CutoutEnabled?r.Cutout:null;
    public static SKRectI Rect(EffectRegion r,int w,int h)
    {var b=ObjectExtractor.PixelRect(r.Bounds,w,h);var p=(int)Math.Ceiling(r.Softness.Padding)+((r.Softness.Padding>0)?2:0);return new(Math.Max(0,b.Left-p),Math.Max(0,b.Top-p),Math.Min(w,b.Right+p),Math.Min(h,b.Bottom+p));}
    public static SKPath Path(EffectRegion r,SKRectI rect,int w,int h)
    {var b=r.Bounds.Pixels(w,h);b.Offset(-rect.Left,-rect.Top);return RegionMask.Create(r.Shape,b,r.Points);}
    public static SKBitmap Shape(EffectRegion r,SKRectI rect,int w,int h,bool soft=true)
    {
        var b=new SKBitmap(rect.Width,rect.Height);b.Erase(SKColors.Transparent);
        using(var c=new SKCanvas(b))using(var p=new SKPaint{Color=SKColors.White,IsAntialias=true})using(var path=Path(r,rect,w,h))c.DrawPath(path,p);
        if(soft&&!(r.SeparationTarget==SeparationTarget.Text&&r.ContentOnly)&&r.Softness.Enabled&&r.Softness.Maximum>0)
        {
            var width=b.Width;var height=b.Height;var n=width*height;var inside=new bool[n];
            for(var y=0;y<height;y++)for(var x=0;x<width;x++)inside[y*width+x]=b.GetPixel(x,y).Alpha>=128;
            var toIn=Distance(inside,width,height,true);var toOut=Distance(inside,width,height,false);var bounds=r.Bounds.Pixels(w,h);bounds.Offset(-rect.Left,-rect.Top);
            for(var y=0;y<height;y++)for(var x=0;x<width;x++)
            {
                var s=r.Softness;var feather=s.Width;
                if(s.SeparateSides){var nearest=Math.Abs(y-bounds.Top);feather=s.Top;var d=Math.Abs(x-bounds.Right);if(d<nearest){nearest=d;feather=s.Right;}d=Math.Abs(y-bounds.Bottom);if(d<nearest){nearest=d;feather=s.Bottom;}if(Math.Abs(x-bounds.Left)<nearest)feather=s.Left;}
                if(feather<=0)continue;var i=y*width+x;var distance=inside[i]?toOut[i]-.5:-(toIn[i]-.5);
                // A full-rectangle mask has no outside seed in the bitmap. Include its true edge.
                if(inside[i])distance=Math.Min(distance,Math.Min(Math.Min(x+.5,width-x-.5),Math.Min(y+.5,height-y-.5)));
                var t=Math.Clamp(s.Direction switch{SoftDirection.Inside=>distance/feather,SoftDirection.Outside=>1+distance/feather,_=>.5+distance/feather},0,1);
                var smooth=t*t*(3-2*t);var a=Math.Pow(smooth,1/s.Strength);
                b.SetPixel(x,y,SKColors.White.WithAlpha((byte)Math.Round(a*255)));
            }
        }
        if(ActiveCutout(r) is { } cutout)
        {using var decoded=!soft&&!string.IsNullOrEmpty(cutout.ErasePng)?SKBitmap.Decode(Convert.FromBase64String(cutout.ErasePng)):cutout.Decode();using var image=SKImage.FromBitmap(decoded);using var mask=new SKBitmap(rect.Width,rect.Height);mask.Erase(SKColors.Transparent);using(var c=new SKCanvas(mask)){var original=ObjectExtractor.PixelRect(r.Bounds,w,h);c.DrawImage(image,SKRect.Create(original.Left-rect.Left,original.Top-rect.Top,original.Width,original.Height));}Multiply(b,mask);}
        return b;
    }
    public static SKBitmap Raw(SKBitmap source,EffectRegion r,SKRectI rect,SKColor background)
    {
        using var crop=new SKBitmap();if(!source.ExtractSubset(crop,rect))throw new InvalidDataException("영역을 읽을 수 없습니다.");var output=crop.Copy();
        if(r.OverlayOnly){output.Erase(SKColors.Transparent);return output;}
        using(var corrected=ActiveCutout(r)?.DecodeForeground())if(corrected is not null){using var c=new SKCanvas(output);c.Clear(SKColors.Transparent);using var image=SKImage.FromBitmap(corrected);var original=ObjectExtractor.PixelRect(r.Bounds,source.Width,source.Height);c.DrawImage(image,SKRect.Create(original.Left-rect.Left,original.Top-rect.Top,original.Width,original.Height));}
        if(r.ContentOnly&&ActiveCutout(r) is null)
        {
            MaskBuffer mask;
            if(r.SeparationTarget==SeparationTarget.Text){using var text=TextSeparation.Extract(output,background,SKColor.TryParse(r.TextInkColor,out var ink)?ink:null,r.TextSettings);output.Dispose();output=text.Foreground.Copy();mask=text.Mask;}
            else mask=ObjectExtractor.Extract(output,RegionSeparation.Options(r,background));
            using var alpha=mask.Bitmap();Multiply(output,alpha);
        }
        return output;
    }
    public static void Multiply(SKBitmap target,SKBitmap mask)
    {using var c=new SKCanvas(target);using var image=SKImage.FromBitmap(mask);using var p=new SKPaint{BlendMode=SKBlendMode.DstIn};c.DrawImage(image,0,0,p);}
    private static float[] Distance(bool[] mask,int w,int h,bool target)
    {
        var a=new float[mask.Length];for(var i=0;i<a.Length;i++)a[i]=mask[i]==target?0:1e6f;
        for(var y=0;y<h;y++)for(var x=0;x<w;x++){var i=y*w+x;if(x>0)a[i]=Math.Min(a[i],a[i-1]+1);if(y>0){a[i]=Math.Min(a[i],a[i-w]+1);if(x>0)a[i]=Math.Min(a[i],a[i-w-1]+1.414214f);if(x+1<w)a[i]=Math.Min(a[i],a[i-w+1]+1.414214f);}}
        for(var y=h-1;y>=0;y--)for(var x=w-1;x>=0;x--){var i=y*w+x;if(x+1<w)a[i]=Math.Min(a[i],a[i+1]+1);if(y+1<h){a[i]=Math.Min(a[i],a[i+w]+1);if(x>0)a[i]=Math.Min(a[i],a[i+w-1]+1.414214f);if(x+1<w)a[i]=Math.Min(a[i],a[i+w+1]+1.414214f);}}return a;
    }
}
