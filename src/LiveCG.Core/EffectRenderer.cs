using SkiaSharp;

namespace LiveCG.Core;

public static class EffectRenderer
{
    public static SKBitmap Render(SKBitmap source, IReadOnlyList<EffectRegion> regions, VideoOptions video, double seconds, int maxWidth = 0)
    {
        using var scene = new EffectScene(source);
        return scene.Render(regions, video, seconds, maxWidth);
    }
}
