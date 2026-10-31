using SkiaSharp;

namespace LiveCG.App;

public static class PointerOverlay
{
    private static void Contrast(SKCanvas canvas,Action<SKPaint> draw)
    {
        using var p=new SKPaint{IsAntialias=true,Style=SKPaintStyle.Stroke,Color=SKColors.Black,StrokeWidth=3};draw(p);p.Color=SKColors.White;p.StrokeWidth=1;draw(p);
    }
    public static void Cross(SKCanvas c,SKPoint at,float size=5)=>Contrast(c,p=>{c.DrawLine(at.X-size,at.Y,at.X+size,at.Y,p);c.DrawLine(at.X,at.Y-size,at.X,at.Y+size,p);});
    public static void Brush(SKCanvas c,SKPoint at,float radius,bool include)
    {
        Contrast(c,p=>c.DrawCircle(at,Math.Max(.5f,radius),p));Cross(c,at);
        using var p=new SKPaint{IsAntialias=true,Color=include?new(93,226,177):new(255,127,149),Style=SKPaintStyle.Stroke,StrokeWidth=2};
        c.DrawLine(at.X+8,at.Y-8,at.X+16,at.Y-8,p);if(include)c.DrawLine(at.X+12,at.Y-12,at.X+12,at.Y-4,p);
    }
    public static void Eyedropper(SKCanvas c,SKBitmap source,SKPoint pixel,SKPoint at,float width,float height)
    {
        var ix=Math.Clamp((int)pixel.X,0,source.Width-1);var iy=Math.Clamp((int)pixel.Y,0,source.Height-1);var color=source.GetPixel(ix,iy);
        Cross(c,at,6);
        using var path=new SKPath();path.MoveTo(at.X+3,at.Y-3);path.LineTo(at.X+16,at.Y-16);path.LineTo(at.X+21,at.Y-11);path.LineTo(at.X+8,at.Y+2);path.Close();
        using var paint=new SKPaint{IsAntialias=true,Color=new(20,32,44)};c.DrawPath(path,paint);Contrast(c,p=>c.DrawPath(path,p));
        var left=Math.Clamp(at.X+27,4,Math.Max(4,width-104));var top=Math.Clamp(at.Y+20,4,Math.Max(4,height-134));
        paint.Color=new(16,25,36);c.DrawRoundRect(SKRect.Create(left-4,top-4,100,127),4,4,paint);
        for(var y=-3;y<=3;y++)for(var x=-3;x<=3;x++)
        {
            var r=SKRect.Create(left+(x+3)*12,top+(y+3)*12,12,12);paint.Color=(x+y)%2==0?new(95,105,115):new(145,155,165);c.DrawRect(r,paint);
            paint.Color=source.GetPixel(Math.Clamp(ix+x,0,source.Width-1),Math.Clamp(iy+y,0,source.Height-1));c.DrawRect(r,paint);
        }
        Contrast(c,p=>c.DrawRect(SKRect.Create(left,top,84,84),p));Contrast(c,p=>c.DrawRect(SKRect.Create(left+36,top+36,12,12),p));Cross(c,new(left+42,top+42),3);
        paint.Color=color;c.DrawRect(SKRect.Create(left,top+89,18,18),paint);Contrast(c,p=>c.DrawRect(SKRect.Create(left,top+89,18,18),p));
        paint.Color=SKColors.White;using var font=new SKFont(SKTypeface.FromFamilyName("Arial"),10);c.DrawText(ColorPickerWindow.Hex(color),left+23,top+102,font,paint);
        paint.Color=new(180,200,215);c.DrawText($"{ix}, {iy}",left,top+119,font,paint);
    }
}
