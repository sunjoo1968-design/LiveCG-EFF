using SkiaSharp;

namespace LiveCG.Core;

public static class RegionMask
{
    public static SKPath Create(RegionShape shape, SKRect rect,IReadOnlyList<RegionPoint>? points=null)
    {
        var path = new SKPath();
        if(shape is RegionShape.Freehand or RegionShape.Polygon&&points is {Count:>=3}){path.FillType=SKPathFillType.EvenOdd;path.AddPoly(points.Select(p=>new SKPoint(rect.Left+(float)p.X*rect.Width,rect.Top+(float)p.Y*rect.Height)).ToArray(),true);}
        else if (shape == RegionShape.Ellipse) path.AddOval(rect);
        else if (shape == RegionShape.RoundedRectangle) path.AddRoundRect(rect, Math.Min(rect.Width, rect.Height) * .18f, Math.Min(rect.Width, rect.Height) * .18f);
        else path.AddRect(rect);
        return path;
    }
    public static bool Contains(EffectRegion region, SKPoint point, int width, int height)
    { using var path = Create(region.Shape, region.Bounds.Pixels(width, height),region.Points); return path.Contains(point.X, point.Y); }
}
