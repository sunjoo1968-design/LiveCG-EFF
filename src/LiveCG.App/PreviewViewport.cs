using LiveCG.Core;
using SkiaSharp;

namespace LiveCG.App;

public sealed class PreviewViewport
{
    public double ViewWidth { get; private set; } = 1;
    public double ViewHeight { get; private set; } = 1;
    public int ImageWidth { get; private set; } = 1;
    public int ImageHeight { get; private set; } = 1;
    public SKRect Content { get; private set; } = SKRect.Create(1, 1);
    public bool ContentOnly { get; private set; }
    public double Zoom { get; private set; } = 1;
    public SKPoint Pan { get; set; }
    private SKRect FitBounds => ContentOnly ? Content : SKRect.Create(ImageWidth, ImageHeight);
    public double FitScale => Math.Min(ViewWidth / Math.Max(1, FitBounds.Width), ViewHeight / Math.Max(1, FitBounds.Height));
    public double Scale => FitScale * Zoom;
    public SKRect ImageRect
    {
        get
        {
            var b = FitBounds; var left = (ViewWidth - b.Width * Scale) / 2 - b.Left * Scale + Pan.X;
            var top = (ViewHeight - b.Height * Scale) / 2 - b.Top * Scale + Pan.Y;
            return SKRect.Create((float)left, (float)top, (float)(ImageWidth * Scale), (float)(ImageHeight * Scale));
        }
    }
    public void Update(double width, double height, int imageWidth, int imageHeight, SKRect content)
    { ViewWidth = Math.Max(1, width); ViewHeight = Math.Max(1, height); ImageWidth = imageWidth; ImageHeight = imageHeight; Content = content; }
    public SKPoint ToImage(SKPoint point) => new((float)((point.X - ImageRect.Left) / Scale), (float)((point.Y - ImageRect.Top) / Scale));
    public SKPoint ToScreen(SKPoint point) => new((float)(ImageRect.Left + point.X * Scale), (float)(ImageRect.Top + point.Y * Scale));
    public SKRect ToScreen(NormalizedRect rect)
    { var b = rect.Pixels(ImageWidth, ImageHeight); var p = ToScreen(new SKPoint(b.Left, b.Top)); return SKRect.Create(p.X, p.Y, (float)(b.Width * Scale), (float)(b.Height * Scale)); }
    public void ZoomAt(double multiplier, SKPoint anchor)
    {
        var imagePoint = ToImage(anchor); Zoom = Math.Clamp(Zoom * multiplier, .2, 128);
        var shifted = ToScreen(imagePoint); Pan = new(Pan.X + anchor.X - shifted.X, Pan.Y + anchor.Y - shifted.Y);
    }
    public void Fit(bool content) { ContentOnly = content; Zoom = 1; Pan = default; }
    public void Native(double pixelRatio = 1) { var factor = (1 / pixelRatio) / Math.Max(.00001, Scale); ZoomAt(factor, new((float)ViewWidth / 2, (float)ViewHeight / 2)); }
}

public static class RegionGeometry
{
    public static NormalizedRect Move(NormalizedRect b, double dx, double dy) => new(Math.Clamp(b.X + dx, 0, 1 - b.Width), Math.Clamp(b.Y + dy, 0, 1 - b.Height), b.Width, b.Height);
    public static NormalizedRect Resize(NormalizedRect b, int handle, double dx, double dy, int width, int height)
    {
        var l = b.X; var t = b.Y; var r = b.X + b.Width; var bottom = b.Y + b.Height;
        var minW = Math.Min(b.Width, 2d / width); var minH = Math.Min(b.Height, 2d / height);
        if (handle is 0 or 6 or 7) l = Math.Clamp(l + dx, 0, r - minW);
        if (handle is 2 or 3 or 4) r = Math.Clamp(r + dx, l + minW, 1);
        if (handle is 0 or 1 or 2) t = Math.Clamp(t + dy, 0, bottom - minH);
        if (handle is 4 or 5 or 6) bottom = Math.Clamp(bottom + dy, t + minH, 1);
        return new NormalizedRect(l, t, r - l, bottom - t).Clamp();
    }
}
