using SkiaSharp;

namespace LiveCG.Core;
/// <summary>One background and extraction policy for quick separation and the precision editor.</summary>
public static class RegionSeparation
{
    public static ExtractionOptions Options(EffectRegion r,SKColor background)=>new(){Mode=ExtractionMode.BackgroundColor,Background=background,Tolerance=r.ContentTolerance,Feather=0,PreserveInterior=true};
    public static SKColor Background(SKBitmap source,EffectRegion r)=>r.Background switch
    {BackgroundMode.Solid=>SKColor.Parse(r.BackgroundColor),BackgroundMode.Transparent=>SKColors.Transparent,_=>Guess(source,RegionRaster.Rect(r,source.Width,source.Height))};
    private static SKColor Guess(SKBitmap source,SKRectI rect)
    {
        var colors=new List<SKColor>();var step=Math.Max(1,Math.Max(rect.Width,rect.Height)/100);
        for(var x=rect.Left;x<rect.Right;x+=step){colors.Add(source.GetPixel(x,Math.Clamp(rect.Top-1,0,source.Height-1)));colors.Add(source.GetPixel(x,Math.Clamp(rect.Bottom,0,source.Height-1)));}
        for(var y=rect.Top;y<rect.Bottom;y+=step){colors.Add(source.GetPixel(Math.Clamp(rect.Left-1,0,source.Width-1),y));colors.Add(source.GetPixel(Math.Clamp(rect.Right,0,source.Width-1),y));}
        if(colors.Count(c=>c.Alpha<32)>colors.Count/2)return SKColors.Transparent;
        var opaque=colors.Where(c=>c.Alpha>=32).ToList();byte Median(Func<SKColor,byte> f)=>opaque.Select(f).Order().ElementAt(opaque.Count/2);
        return new(Median(c=>c.Red),Median(c=>c.Green),Median(c=>c.Blue),Median(c=>c.Alpha));
    }
    public static MaskBuffer InitialMask(SKBitmap source,EffectRegion region)
    {
        var original=ObjectExtractor.PixelRect(region.Bounds,source.Width,source.Height);
        if(RegionRaster.ActiveCutout(region) is { } saved&&saved.Width==original.Width&&saved.Height==original.Height)return MaskBuffer.Decode(saved);
        var rect=RegionRaster.Rect(region,source.Width,source.Height);using var crop=new SKBitmap();if(!source.ExtractSubset(crop,rect))throw new InvalidDataException("분리 영역을 읽지 못했습니다.");
        var mask=ObjectExtractor.Extract(crop,Options(region,Background(source,region)));
        return mask.Crop(new(original.Left-rect.Left,original.Top-rect.Top,original.Right-rect.Left,original.Bottom-rect.Top));
    }
}
