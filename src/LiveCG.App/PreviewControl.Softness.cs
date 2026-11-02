using System.Text.Json;
using LiveCG.Core;
using SkiaSharp;

namespace LiveCG.App;
public sealed partial class PreviewControl
{
    public int ComparisonBackground { get; set; }
    public bool ShowSoftGuides { get; set; }=true;
    private EffectRegion? _softTarget;private RegionSoftness? _softBefore;private int _softSide;
    private SKPath? _insideGuide,_outsideGuide;private string? _guideKey;
    private SKPoint[] SoftHandles(EffectRegion r)
    {
        var b=r.Bounds.Pixels(_source!.Width,_source.Height);var s=r.Softness;var f=s.Direction==SoftDirection.Inside?-1:s.Direction==SoftDirection.Outside?1:.5f;
        return [new(b.MidX,b.Top-(float)(s.SeparateSides?s.Top:s.Width)*f),new(b.Right+(float)(s.SeparateSides?s.Right:s.Width)*f,b.MidY),new(b.MidX,b.Bottom+(float)(s.SeparateSides?s.Bottom:s.Width)*f),new(b.Left-(float)(s.SeparateSides?s.Left:s.Width)*f,b.MidY)];
    }
    private void DrawSoftGuides(SKCanvas canvas)
    {
        if(!ShowSoftGuides||AddMode||SelectedMask is not null||SelectedRegion is not {Softness.Enabled:true} r||_source is null)return;
        var previewBounds=r.Bounds.Pixels(_source.Width,_source.Height);var previewFactor=Math.Min(1,Math.Max(512,Math.Max(previewBounds.Width,previewBounds.Height)*Viewport.Scale)/Math.Max(previewBounds.Width,previewBounds.Height));var key=JsonSerializer.Serialize(new{r.Bounds,r.Shape,r.Points,r.Softness,_source.Width,_source.Height,previewFactor});
        if(_guideKey!=key)
        {
            _insideGuide?.Dispose();_outsideGuide?.Dispose();_insideGuide=new();_outsideGuide=new();_guideKey=key;
            var factor=previewFactor;var width=Math.Max(1,(int)Math.Round(_source.Width*factor));var height=Math.Max(1,(int)Math.Round(_source.Height*factor));var softness=r.Softness;var clean=r with{Cutout=null,Softness=softness with{Width=softness.Width*factor,Top=softness.Top*factor,Right=softness.Right*factor,Bottom=softness.Bottom*factor,Left=softness.Left*factor}};var rect=RegionRaster.Rect(clean,width,height);using var mask=RegionRaster.Shape(clean,rect,width,height);
            void Edge(SKPath path,int threshold,int x,int y)
            {
                bool On(int px,int py)=>px>=0&&py>=0&&px<mask.Width&&py<mask.Height&&mask.GetPixel(px,py).Alpha>=threshold;
                if(!On(x,y))return;var px=x+rect.Left;var py=y+rect.Top;
                void Line(float a,float b,float c,float d){path.MoveTo((float)(a/factor),(float)(b/factor));path.LineTo((float)(c/factor),(float)(d/factor));}
                if(!On(x-1,y))Line(px,py,px,py+1);if(!On(x+1,y))Line(px+1,py,px+1,py+1);if(!On(x,y-1))Line(px,py,px+1,py);if(!On(x,y+1))Line(px,py+1,px+1,py+1);
            }
            for(var y=0;y<mask.Height;y++)for(var x=0;x<mask.Width;x++){Edge(_insideGuide,254,x,y);Edge(_outsideGuide,1,x,y);}
        }
        canvas.Save();canvas.Translate(Viewport.ImageRect.Left,Viewport.ImageRect.Top);canvas.Scale((float)Viewport.Scale);
        using var p=new SKPaint{Style=SKPaintStyle.Stroke,Color=SKColors.Orange,StrokeWidth=(float)(1.2/Viewport.Scale),IsAntialias=true};
        canvas.DrawPath(_outsideGuide!,p);p.Color=SKColors.Cyan;canvas.DrawPath(_insideGuide!,p);canvas.Restore();
        if(r.Locked)return;var handles=SoftHandles(r);
        for(var i=0;i<4;i++){if(!r.Softness.SeparateSides&&i!=1)continue;var at=Viewport.ToScreen(handles[i]);p.Style=SKPaintStyle.Fill;p.Color=SKColors.Orange;canvas.DrawCircle(at,5,p);p.Style=SKPaintStyle.Stroke;p.Color=SKColors.Black;p.StrokeWidth=1;canvas.DrawCircle(at,5,p);}
    }
    private bool TryBeginSoft(SKPoint screen)
    {
        if(!ShowSoftGuides||AddMode||SelectedMask is not null||SelectedRegion is not {Locked:false,Softness.Enabled:true} r||_source is null)return false;
        var handles=SoftHandles(r);for(var i=0;i<4;i++){if(!r.Softness.SeparateSides&&i!=1)continue;if(SKPoint.Distance(screen,Viewport.ToScreen(handles[i]))>9)continue;GestureStarted?.Invoke();_softTarget=r;_softBefore=r.Softness with{};_softSide=i;return true;}return false;
    }
    private void MoveSoft(SKPoint screen)
    {
        if(_softTarget is not { } r||_source is null)return;var p=Viewport.ToImage(screen);var b=r.Bounds.Pixels(_source.Width,_source.Height);var offset=_softSide switch{0=>b.Top-p.Y,1=>p.X-b.Right,2=>p.Y-b.Bottom,_=>b.Left-p.X};var f=r.Softness.Direction==SoftDirection.Inside?-1:r.Softness.Direction==SoftDirection.Outside?1:2;
        var width=Math.Clamp(offset*f,0,256);var s=r.Softness;
        r.Softness=!s.SeparateSides?s with{Width=width}:_softSide switch{0=>s with{Top=width},1=>s with{Right=width},2=>s with{Bottom=width},_=>s with{Left=width}};RegionEdited?.Invoke();InvalidateVisual();
    }
    private bool EndSoft(bool commit)
    {if(_softTarget is null)return false;if(!commit&&_softBefore is not null)_softTarget.Softness=_softBefore;_softTarget=null;_softBefore=null;RegionEdited?.Invoke();GestureEnded?.Invoke(commit);InvalidateVisual();return true;}
    private void DisposeSoftGuides(){_insideGuide?.Dispose();_outsideGuide?.Dispose();}
}
