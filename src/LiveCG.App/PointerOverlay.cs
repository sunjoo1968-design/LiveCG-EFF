using System.IO;
using System.Windows.Input;
using SkiaSharp;

namespace LiveCG.App;

public static class PointerOverlay
{
    private static readonly Lazy<Cursor> OpenHand = new(() => CreateHandCursor(false));
    private static readonly Lazy<Cursor> GrabHand = new(() => CreateHandCursor(true));
    public static Cursor OpenHandCursor => OpenHand.Value;
    public static Cursor GrabHandCursor => GrabHand.Value;

    private static Cursor CreateHandCursor(bool closed)
    {
        try
        {
            const int size = 32;
            using var bitmap = new SKBitmap(new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Premul));
            using (var canvas = new SKCanvas(bitmap))
            {
                canvas.Clear(SKColors.Transparent);
                DrawHandCursor(canvas, closed);
            }
            const int xorSize = size * size * 4;
            const int andSize = size * 4;
            const int dibHeaderSize = 40;
            const int resSize = dibHeaderSize + xorSize + andSize;
            var stream = new MemoryStream(22 + resSize);
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                writer.Write((ushort)0);
                writer.Write((ushort)2);
                writer.Write((ushort)1);
                writer.Write((byte)size);
                writer.Write((byte)size);
                writer.Write((byte)0);
                writer.Write((byte)0);
                writer.Write((ushort)15);
                writer.Write((ushort)15);
                writer.Write((uint)resSize);
                writer.Write((uint)22);
                writer.Write((uint)dibHeaderSize);
                writer.Write(size);
                writer.Write(size * 2);
                writer.Write((ushort)1);
                writer.Write((ushort)32);
                writer.Write((uint)0);
                writer.Write((uint)(xorSize + andSize));
                writer.Write(0);
                writer.Write(0);
                writer.Write((uint)0);
                writer.Write((uint)0);
                for (var y = size - 1; y >= 0; y--)
                {
                    for (var x = 0; x < size; x++)
                    {
                        var c = bitmap.GetPixel(x, y);
                        writer.Write(c.Blue);
                        writer.Write(c.Green);
                        writer.Write(c.Red);
                        writer.Write(c.Alpha);
                    }
                }
                for (var y = size - 1; y >= 0; y--)
                {
                    var maskRow = 0u;
                    for (var x = 0; x < size; x++)
                        if (bitmap.GetPixel(x, y).Alpha == 0) maskRow |= 1u << (31 - x);
                    writer.Write((byte)((maskRow >> 24) & 0xFF));
                    writer.Write((byte)((maskRow >> 16) & 0xFF));
                    writer.Write((byte)((maskRow >> 8) & 0xFF));
                    writer.Write((byte)(maskRow & 0xFF));
                }
            }
            stream.Position = 0;
            return new Cursor(stream);
        }
        catch
        {
            return Cursors.Hand;
        }
    }

    private static void DrawHandCursor(SKCanvas canvas, bool closed)
    {
        (SKPoint A, SKPoint B)[] fingers = closed
            ? [(new(11.0f, 11.8f), new(11.0f, 17.5f)), (new(14.8f, 10.8f), new(14.8f, 17.5f)), (new(18.6f, 11.2f), new(18.6f, 18.0f)), (new(22.2f, 12.6f), new(22.2f, 18.5f))]
            : [(new(11.2f, 6.5f), new(11.2f, 17.2f)), (new(15.0f, 5.0f), new(15.0f, 17.2f)), (new(18.8f, 6.0f), new(18.8f, 17.5f)), (new(22.4f, 8.8f), new(22.4f, 18.5f))];
        var thumbA = closed ? new SKPoint(7.4f, 16.5f) : new SKPoint(5.8f, 16.0f);
        var thumbB = closed ? new SKPoint(11.2f, 20.5f) : new SKPoint(10.8f, 21.5f);
        var palm = closed ? new SKRect(9.2f, 14.2f, 24.0f, 25.8f) : new SKRect(9.4f, 14.5f, 24.2f, 26.8f);

        void RenderPass(SKColor color, float fingerWidth, float palmInflate, float dx, float dy)
        {
            using var stroke = new SKPaint { IsAntialias = true, Color = color, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round, StrokeWidth = fingerWidth };
            using var fill = new SKPaint { IsAntialias = true, Color = color, Style = SKPaintStyle.Fill };
            canvas.Save();
            canvas.Translate(dx, dy);
            foreach (var (a, b) in fingers) canvas.DrawLine(a, b, stroke);
            canvas.DrawLine(thumbA, thumbB, stroke);
            var inflated = SKRect.Inflate(palm, palmInflate, palmInflate);
            canvas.DrawRoundRect(inflated, 5.5f + palmInflate, 5.5f + palmInflate, fill);
            canvas.Restore();
        }

        RenderPass(new SKColor(0, 0, 0, 85), 5.6f, 1.4f, 0.8f, 1.2f);
        RenderPass(new SKColor(16, 24, 34), 5.6f, 1.4f, 0f, 0f);
        RenderPass(SKColors.White, 3.1f, 0f, 0f, 0f);

        using var groove = new SKPaint { IsAntialias = true, Color = new SKColor(28, 42, 56, 210), Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeWidth = 1.15f };
        var grooveTop = closed ? 11.4f : 13.0f;
        var grooveBottom = closed ? 15.6f : 16.4f;
        canvas.DrawLine(13.1f, grooveTop, 13.1f, grooveBottom, groove);
        canvas.DrawLine(16.9f, grooveTop - 0.3f, 16.9f, grooveBottom, groove);
        canvas.DrawLine(20.6f, grooveTop + 0.5f, 20.6f, grooveBottom + 0.4f, groove);
    }
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
