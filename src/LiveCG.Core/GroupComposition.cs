using SkiaSharp;

namespace LiveCG.Core;

public sealed partial class EffectScene
{
    private readonly MaskRenderer _maskRenderer=new();
    private static void Transform(SKCanvas c,SKRect bounds,MotionState s)
    {var x=bounds.Left+bounds.Width*s.AnchorX;var y=bounds.Top+bounds.Height*s.AnchorY;c.Translate(x+s.X,y+s.Y);c.RotateDegrees(s.Angle);c.Scale(s.Scale*s.ScaleX,s.Scale*s.ScaleY);c.Translate(-x,-y);}
    private SKBitmap RawCrop(EffectRegion region,SKRectI rect)
    {
        if(RegionRaster.Custom(region))return SKBitmap.FromImage(GetLayer(region).RawImage!);
        using var subset=new SKBitmap();if(!_source.ExtractSubset(subset,rect))throw new InvalidDataException("그룹 범위를 읽지 못했습니다.");var bitmap=subset.Copy();using var corrected=region.Cutout?.DecodeForeground();
        if(corrected is not null)using(var c=new SKCanvas(bitmap))using(var image=SKImage.FromBitmap(corrected)){c.Clear(SKColors.Transparent);c.DrawImage(image,SKRect.Create(bitmap.Width,bitmap.Height),Sampling);}return bitmap;
    }
    private SKBitmap ShapeCoverage(EffectRegion region,int w,int h,bool includeCutout=true)
    {
        if(!includeCutout){var layer=GetLayer(region);if(layer.NumericShapeImage is null){using var shape=RegionRaster.Shape(region,RegionRaster.Rect(region,Width,Height),Width,Height,includeCutout:false);layer.NumericShapeImage=SKImage.FromBitmap(shape);}return SKBitmap.FromImage(layer.NumericShapeImage);}
        if(RegionRaster.Custom(region))return SKBitmap.FromImage(GetLayer(region).ShapeImage!);
        var bitmap=new SKBitmap(w,h);bitmap.Erase(SKColors.Transparent);using var c=new SKCanvas(bitmap);using var p=new SKPaint{Color=SKColors.White,IsAntialias=true};using var path=RegionMask.Create(region.Shape,SKRect.Create(w,h),region.Points);c.DrawPath(path,p);
        if(region.Cutout is { } cutout){using var mask=cutout.Decode();using var image=SKImage.FromBitmap(mask);using var cut=new SKPaint{BlendMode=SKBlendMode.DstIn};c.DrawImage(image,SKRect.Create(w,h),Sampling,cut);}return bitmap;
    }
    private static void Multiply(SKBitmap target,SKBitmap mask)
    {using var c=new SKCanvas(target);using var image=SKImage.FromBitmap(mask);using var p=new SKPaint{BlendMode=SKBlendMode.DstIn};c.DrawImage(image,0,0,p);}
    private void DrawGrouped(SKCanvas canvas,IReadOnlyList<EffectRegion> regions,double time,bool effects)
    {
        var roots=regions.Where(r=>r.ParentId is null).ToList();
        var plain=roots.All(r=>r.Enabled)&&regions.Where(r=>LayerGraph.Visible(r,regions)).All(r=>
        {
            if(r.Masks.Any(m=>m.Enabled))return false;if(!effects)return true;var clips=ClipTiming.EffectiveClips(r.Clips,time);var state=Evaluate(clips,time,r.Bounds.Pixels(Width,Height),GetLayer(r).Digits.Count>0);
            return Identity(state)&&!clips.Any(c=>time>=c.Start&&(time<c.End||ProductInfo.HoldsDecoration(c.Options.Kind))&&(!ProductInfo.NeedsLayer(c.Options.Kind)||c.Options.Kind is EffectKind.DigitRoll or EffectKind.Bounce));
        });
        if(plain){DrawOriginal(canvas,0,0,null);return;}
        canvas.SaveLayer();canvas.ClipRect(SKRect.Create(Width,Height));DrawOriginal(canvas,0,0,null);
        foreach(var root in roots)
        {
            if(root.OverlayOnly||root.Enabled&&root.Background==BackgroundMode.KeepOriginal)continue;
            var layer=GetLayer(root);using var p=new SKPaint{BlendMode=SKBlendMode.DstOut};canvas.DrawImage(layer.EraseImage!,layer.Bounds.Left,layer.Bounds.Top,p);canvas.DrawImage(layer.FillImage!,layer.Bounds.Left,layer.Bounds.Top);
        }
        foreach(var root in roots.AsEnumerable().Reverse())
        {
            if(!root.Enabled)continue;var rect=RegionRaster.Rect(root,Width,Height);using var frame=RawCrop(root,rect);var clips=ClipTiming.EffectiveClips(root.Clips,time);var state=effects?Evaluate(clips,time,(SKRect)rect,GetLayer(root).Digits.Count>0):new MotionState();
            if(effects&&root.Role==RegionRole.Group)foreach(var child in regions.Where(r=>r.ParentId==root.Id&&r.Enabled).Reverse())ApplyArea(frame,child,rect,time);
            var roll=effects?clips.LastOrDefault(c=>c.Options.Kind==EffectKind.DigitRoll&&time>=c.Start&&time<c.End):null;
            var bounce=effects?clips.LastOrDefault(c=>c.Options.Kind==EffectKind.Bounce&&time>=c.Start&&time<c.End):null;
            var numeric=(roll is not null||bounce is not null)&&GetLayer(root).Digits.Count>0;
            if(numeric){using var c=new SKCanvas(frame);c.Clear(SKColors.Transparent);using var p=new SKPaint{Color=SKColors.White};DrawDigits(c,GetLayer(root),roll,bounce,time,p);}
            // Input glyphs already carry the extraction alpha. A moving/new glyph must not be cut by the old glyph silhouette.
            using var coverage=ShapeCoverage(root,frame.Width,frame.Height,!numeric);using var masks=_maskRenderer.Render(root.Masks,frame.Width,frame.Height,Width,Height,rect,time);Multiply(coverage,masks);Multiply(frame,coverage);
            canvas.Save();canvas.Translate(rect.Left,rect.Top);Transform(canvas,SKRect.Create(frame.Width,frame.Height),state);ClipCrop(canvas,SKRect.Create(frame.Width,frame.Height),state);using var paint=new SKPaint{Color=SKColors.White.WithAlpha((byte)(Math.Clamp(state.Alpha,0,1)*255)),IsAntialias=true};using(var image=SKImage.FromBitmap(frame))canvas.DrawImage(image,0,0,Sampling,paint);
            if(effects)
            {
                if(root.Masks.Any(m=>m.Enabled))canvas.SaveLayer();
                foreach(var clip in clips)if(time>=clip.Start&&(time<clip.End||ProductInfo.HoldsDecoration(clip.Options.Kind)))DrawDecoration(canvas,frame.Width,frame.Height,clip,(float)ClipTiming.Progress(clip,time),state.Alpha,root,GetLayer(root).Image);
                if(root.Masks.Any(m=>m.Enabled)){using var image=SKImage.FromBitmap(masks);using var p=new SKPaint{BlendMode=SKBlendMode.DstIn};canvas.DrawImage(image,0,0,p);canvas.Restore();}
            }
            canvas.Restore();
        }
        canvas.Restore();Prune(regions);
    }
    private void ApplyArea(SKBitmap frame,EffectRegion area,SKRectI parent,double time)
    {
        if(RegionRaster.Custom(area)){ApplySoftArea(frame,area,parent,time);return;}
        var clips=ClipTiming.EffectiveClips(area.Clips,time);if(clips.Count==0)return;
        var bounds=area.Bounds.Pixels(Width,Height);bounds.Offset(-parent.Left,-parent.Top);var numeric=GetLayer(area).Digits.Count>0&&clips.Any(c=>time>=c.Start&&time<c.End&&c.Options.Kind is EffectKind.DigitRoll or EffectKind.Bounce);var state=Evaluate(clips,time,bounds,numeric);var active=clips.Any(c=>time>=c.Start&&(time<c.End||ProductInfo.IsEntrance(c.Options.Kind)||c.Options.Kind==EffectKind.UserMotion||ProductInfo.HoldsDecoration(c.Options.Kind)));
        if(!active&&Identity(state))return;
        using var masks=_maskRenderer.Render(area.Masks,frame.Width,frame.Height,Width,Height,parent,time);
        if(!Identity(state)||numeric)
        {
            using var original=SKImage.FromBitmap(frame);using var effect=new SKBitmap(frame.Width,frame.Height);effect.Erase(SKColors.Transparent);
            if(numeric)
            {
                var local=area.Copy(false);local.Role=RegionRole.Object;local.ParentId=null;local.Masks=[];local.Bounds=NormalizedRect.FromPixels(bounds,frame.Width,frame.Height);using var scene=new EffectScene(frame);using var rendered=scene.Render([local],new(),time);using var image=SKImage.FromBitmap(rendered);using var c=new SKCanvas(effect);c.DrawImage(image,0,0);
            }
            else using(var c=new SKCanvas(effect))
            {
                var background=area.Background switch{BackgroundMode.Transparent=>SKColors.Transparent,BackgroundMode.Solid=>SKColor.Parse(area.BackgroundColor),_=>GuessBackground(ObjectExtractor.PixelRect(area.Bounds,Width,Height))};
                c.Clear(background);Transform(c,bounds,state);ClipCrop(c,bounds,state);c.DrawImage(original,0,0,Sampling);
            }
            if(state.Alpha<1&&!numeric)
            {
                var bg=area.Background switch{BackgroundMode.Transparent=>SKColors.Transparent,BackgroundMode.Solid=>SKColor.Parse(area.BackgroundColor),_=>GuessBackground(ObjectExtractor.PixelRect(area.Bounds,Width,Height))};
                using var temp=effect.Copy();using var image=SKImage.FromBitmap(temp);using var c=new SKCanvas(effect);c.Clear(bg);using var p=new SKPaint{Color=SKColors.White.WithAlpha((byte)(Math.Clamp(state.Alpha,0,1)*255))};c.DrawImage(image,0,0,p);
            }
            using var coverage=new SKBitmap(frame.Width,frame.Height);coverage.Erase(SKColors.Transparent);
            using(var c=new SKCanvas(coverage))using(var p=new SKPaint{Color=SKColors.White,IsAntialias=true})using(var path=RegionMask.Create(area.Shape,bounds,area.Points)){c.DrawPath(path,p);c.Save();Transform(c,bounds,state);c.DrawPath(path,p);c.Restore();}
            // The sampled group already contains the object's antialiasing. ROI support is not a second edge mask.
            MakeSupport(coverage);
            if(numeric)coverage.Erase(SKColors.White);
            Multiply(coverage,masks);
            // Complementary coverage is shared by both terms; Plus performs a premultiplied lerp.
            Multiply(effect,coverage);using(var c=new SKCanvas(frame))
            {using(var image=SKImage.FromBitmap(coverage))using(var p=new SKPaint{BlendMode=SKBlendMode.DstOut})c.DrawImage(image,0,0,p);using(var image=SKImage.FromBitmap(effect))using(var p=new SKPaint{BlendMode=SKBlendMode.Plus})c.DrawImage(image,0,0,p);}
        }
        if(numeric)return;using var decorations=new SKBitmap(frame.Width,frame.Height);decorations.Erase(SKColors.Transparent);
        using(var c=new SKCanvas(decorations))
        {Transform(c,bounds,state);ClipCrop(c,bounds,state);c.Translate(bounds.Left,bounds.Top);foreach(var clip in clips)if(time>=clip.Start&&(time<clip.End||ProductInfo.HoldsDecoration(clip.Options.Kind)))DrawDecoration(c,bounds.Width,bounds.Height,clip,(float)ClipTiming.Progress(clip,time),state.Alpha,area,GetLayer(area).Image);}
        Multiply(decorations,masks);using(var c=new SKCanvas(frame))using(var image=SKImage.FromBitmap(decorations))c.DrawImage(image,0,0);
    }
    private void ApplySoftArea(SKBitmap frame,EffectRegion area,SKRectI parent,double time)
    {
        if(area.OverlayOnly)
        {
            var b=area.Bounds.Pixels(Width,Height);b.Offset(-parent.Left,-parent.Top);using var decor=new SKBitmap(frame.Width,frame.Height);decor.Erase(SKColors.Transparent);using(var c=new SKCanvas(decor)){c.Translate(b.Left,b.Top);foreach(var clip in ClipTiming.EffectiveClips(area.Clips,time))if(time>=clip.Start&&(time<clip.End||ProductInfo.HoldsDecoration(clip.Options.Kind)))DrawDecoration(c,b.Width,b.Height,clip,(float)ClipTiming.Progress(clip,time),1,area);}using var overlayMasks=_maskRenderer.Render(area.Masks,frame.Width,frame.Height,Width,Height,parent,time);Multiply(decor,overlayMasks);using var overlayImage=SKImage.FromBitmap(decor);using var overlayCanvas=new SKCanvas(frame);overlayCanvas.DrawImage(overlayImage,0,0);return;
        }
        var clips=ClipTiming.EffectiveClips(area.Clips,time);var layer=GetLayer(area);var bounds=layer.Bounds;bounds.Offset(-parent.Left,-parent.Top);var state=Evaluate(clips,time,bounds,layer.Digits.Count>0);
        if(Identity(state)&&!clips.Any(c=>time>=c.Start&&(time<c.End||ProductInfo.HoldsDecoration(c.Options.Kind))))return;
        using var composed=frame.Copy();using(var c=new SKCanvas(composed))
        {
            using(var erase=new SKPaint{BlendMode=SKBlendMode.DstOut})c.DrawImage(layer.EraseImage!,bounds.Left,bounds.Top,erase);c.DrawImage(layer.FillImage!,bounds.Left,bounds.Top);
            c.Save();Transform(c,bounds,state);ClipCrop(c,bounds,state);using var paint=new SKPaint{Color=SKColors.White.WithAlpha((byte)(Math.Clamp(state.Alpha,0,1)*255))};
            var roll=clips.LastOrDefault(x=>x.Options.Kind==EffectKind.DigitRoll&&time>=x.Start&&time<x.End);var bounce=clips.LastOrDefault(x=>x.Options.Kind==EffectKind.Bounce&&time>=x.Start&&time<x.End);
            if((roll is not null||bounce is not null)&&layer.Digits.Count>0){c.Save();c.Translate(bounds.Left,bounds.Top);DrawDigits(c,layer,roll,bounce,time,paint);c.Restore();}else c.DrawImage(layer.Image,bounds.Left,bounds.Top,Sampling,paint);c.Translate(bounds.Left,bounds.Top);
            foreach(var clip in clips)if(time>=clip.Start&&(time<clip.End||ProductInfo.HoldsDecoration(clip.Options.Kind)))DrawDecoration(c,bounds.Width,bounds.Height,clip,(float)ClipTiming.Progress(clip,time),state.Alpha,area,layer.Image);c.Restore();
        }
        using var masks=_maskRenderer.Render(area.Masks,frame.Width,frame.Height,Width,Height,parent,time);Multiply(composed,masks);
        using var canvas=new SKCanvas(frame);using(var mask=SKImage.FromBitmap(masks))using(var erase=new SKPaint{BlendMode=SKBlendMode.DstOut})canvas.DrawImage(mask,0,0,erase);using(var image=SKImage.FromBitmap(composed))using(var plus=new SKPaint{BlendMode=SKBlendMode.Plus})canvas.DrawImage(image,0,0,plus);
    }
}
