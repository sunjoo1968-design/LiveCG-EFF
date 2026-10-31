using SkiaSharp;

namespace LiveCG.Core;

/// <summary>One immutable source with cached raster regions. Used by preview and encoder.</summary>
public sealed partial class EffectScene : IDisposable
{
    private static readonly SKSamplingOptions Sampling = new(SKFilterMode.Linear);
    private static readonly byte[] SupportAlpha=Enumerable.Range(0,256).Select(i=>(byte)(i==0?0:255)).ToArray();
    private static readonly byte[] WhiteTable=Enumerable.Repeat((byte)255,256).ToArray();
    private static void MakeSupport(SKBitmap bitmap)
    {using var image=SKImage.FromBitmap(bitmap);using var filter=SKColorFilter.CreateTable(SupportAlpha,WhiteTable,WhiteTable,WhiteTable);using var paint=new SKPaint{ColorFilter=filter,BlendMode=SKBlendMode.Src};using var canvas=new SKCanvas(bitmap);canvas.DrawImage(image,0,0,paint);}
    private readonly SKBitmap _source;
    private readonly SKImage _image;
    private readonly Dictionary<Guid, Layer> _layers = [];
    public int Width => _source.Width;
    public int Height => _source.Height;
    public SKBitmap OriginalFrame()=>_source.Copy();
    public void DrawSource(SKCanvas canvas)=>DrawOriginal(canvas,0,0,null);
    public EffectScene(SKBitmap source) { _source = source; _image = SKImage.FromBitmap(source); }

    public void Validate(IReadOnlyList<EffectRegion> regions)
    {
        LayerGraph.Validate(regions);
        foreach (var region in regions.Where(r => LayerGraph.Visible(r,regions)))
        {
            region.Validate();
            if (region.Clips.Any(c => c.Enabled && c.Options.Kind == EffectKind.DigitRoll) && GetLayer(region).Digits.Count == 0)
                throw new InvalidDataException($"{region.Name}: 롤링할 숫자 칸을 찾을 수 없습니다. 단색 배경의 숫자만 포함하도록 영역을 다시 지정하세요.");
        }
    }

    public SKBitmap Render(IReadOnlyList<EffectRegion> regions, VideoOptions video, double time, int maxWidth = 0)
    {
        var width = maxWidth > 0 ? Math.Min(maxWidth, Width) : Width;
        var height = Math.Max(1, (int)Math.Round((double)Height * width / Width));
        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent); canvas.Scale((float)width / Width);
        Draw(canvas, regions, time); return bitmap;
    }

    private static bool Identity(MotionState s)=>s.Alpha==1&&s.Scale==1&&s.ScaleX==1&&s.ScaleY==1&&s.X==0&&s.Y==0&&s.Angle==0&&!s.HasCrop;
    private static void ClipCrop(SKCanvas canvas,SKRect bounds,MotionState s)
    {if(s.HasCrop)canvas.ClipRect(SKRect.Create(bounds.Left+bounds.Width*s.CropLeft,bounds.Top+bounds.Height*s.CropTop,Math.Max(0,bounds.Width*(1-s.CropLeft-s.CropRight)),Math.Max(0,bounds.Height*(1-s.CropTop-s.CropBottom))));}
    private bool DrawStaticDecorations(SKCanvas canvas,IReadOnlyList<EffectRegion> regions,double time)
    {
        if(regions.Any(r=>r.ParentId is null&&!r.Enabled)||regions.Where(r=>LayerGraph.Visible(r,regions)).Any(r=>r.Masks.Any(m=>m.Enabled)))return false;
        var items=regions.Where(r=>LayerGraph.Visible(r,regions)).Select(r=>(Region:r,Layer:GetLayer(r),Clips:ClipTiming.EffectiveClips(r.Clips,time))).ToList();
        if(items.Any(x=>!Identity(Evaluate(x.Clips,time,x.Layer.Bounds,x.Layer.Digits.Count>0))||x.Clips.Any(c=>c.Options.Kind is EffectKind.DigitRoll or EffectKind.Bounce&&time>=c.Start&&time<c.End)))return false;
        DrawOriginal(canvas,0,0,null);
        foreach(var item in items.AsEnumerable().Reverse()){canvas.Save();canvas.Translate(item.Layer.Bounds.Left,item.Layer.Bounds.Top);foreach(var c in item.Clips)if(time>=c.Start&&(time<c.End||ProductInfo.HoldsDecoration(c.Options.Kind)))DrawDecoration(canvas,item.Layer.Bitmap.Width,item.Layer.Bitmap.Height,c,(float)ClipTiming.Progress(c,time),1,item.Region,item.Layer.Image);canvas.Restore();}
        Prune(regions);return true;
    }
    public void Draw(SKCanvas canvas,IReadOnlyList<EffectRegion> regions,double time,bool effects=true)
    {
        if(effects&&DrawStaticDecorations(canvas,regions,time))return;
        if(regions.Any(r=>r.Role!=RegionRole.Object||r.Masks.Count>0||RegionRaster.Custom(r))){DrawGrouped(canvas,regions,time,effects);return;}
        var prepared=regions.Select((region,i)=>
        {
            var layer=GetLayer(region);var clips=ClipTiming.EffectiveClips(region.Clips,time);
            var state=effects?Evaluate(clips,time,layer.Bounds,layer.Digits.Count>0):new MotionState();
            var numeric=effects&&clips.Any(c=>(c.Options.Kind is EffectKind.DigitRoll or EffectKind.Bounce)&&time>=c.Start&&time<c.End);
            var decor=effects&&clips.Any(c=>!ProductInfo.NeedsLayer(c.Options.Kind)&&time>=c.Start&&(time<c.End||ProductInfo.HoldsDecoration(c.Options.Kind)));
            return(Region:region,Layer:layer,Clips:clips,State:state,Numeric:numeric,Decor:decor);
        }).ToList();
        canvas.SaveLayer();canvas.ClipRect(SKRect.Create(Width,Height));DrawOriginal(canvas,0,0,null);
        if(regions.All(r=>r.Enabled)&&prepared.All(p=>Identity(p.State)&&!p.Numeric&&!p.Decor)){canvas.Restore();Prune(regions);return;}
        // Remove all source instances before drawing any moving layer.
        foreach(var item in prepared)
        {
            if(item.Region.Enabled&&item.Region.Background==BackgroundMode.KeepOriginal)continue;
            var layer=item.Layer;using var erase=new SKPaint{BlendMode=SKBlendMode.DstOut};
            canvas.DrawImage(layer.EraseImage!,layer.Bounds.Left,layer.Bounds.Top,erase);
            canvas.DrawImage(layer.FillImage!,layer.Bounds.Left,layer.Bounds.Top);
        }
        foreach(var item in prepared.AsEnumerable().Reverse())
        {
            if(!item.Region.Enabled||item.State.Alpha<=0)continue;
            var layer=item.Layer;var state=item.State;var clips=item.Clips;
            canvas.Save();canvas.Translate(layer.Bounds.Left+layer.Bitmap.Width*state.AnchorX+state.X,layer.Bounds.Top+layer.Bitmap.Height*state.AnchorY+state.Y);
            canvas.RotateDegrees(state.Angle);canvas.Scale(state.Scale*state.ScaleX,state.Scale*state.ScaleY);canvas.Translate(-layer.Bitmap.Width*state.AnchorX,-layer.Bitmap.Height*state.AnchorY);
            ClipCrop(canvas,SKRect.Create(layer.Bitmap.Width,layer.Bitmap.Height),state);
            using var paint=new SKPaint{IsAntialias=true,Color=SKColors.White.WithAlpha((byte)(Math.Clamp(state.Alpha,0,1)*255))};
            var roll=effects?clips.LastOrDefault(c=>c.Options.Kind==EffectKind.DigitRoll&&time>=c.Start&&time<c.End):null;
            var bounce=effects?clips.LastOrDefault(c=>c.Options.Kind==EffectKind.Bounce&&time>=c.Start&&time<c.End):null;
            if((roll is not null||bounce is not null)&&layer.Digits.Count>0)DrawDigits(canvas,layer,roll,bounce,time,paint);
            else canvas.DrawImage(layer.Image,0,0,Sampling,paint);
            if(effects)foreach(var clip in clips)
            {
                if(time<clip.Start||(time>=clip.End&&!ProductInfo.HoldsDecoration(clip.Options.Kind)))continue;
                DrawDecoration(canvas,layer.Bitmap.Width,layer.Bitmap.Height,clip,(float)ClipTiming.Progress(clip,time),state.Alpha,item.Region,layer.Image);
            }
            canvas.Restore();
        }
        canvas.Restore();Prune(regions);
    }
    private void Prune(IReadOnlyList<EffectRegion> regions)
    {var ids=regions.Select(r=>r.Id).ToHashSet();foreach(var id in _layers.Keys.Where(id=>!ids.Contains(id)).ToArray()){_layers[id].Dispose();_layers.Remove(id);}}

    private void DrawOriginal(SKCanvas canvas, float x, float y, SKPaint? paint)
    {
        var m=canvas.TotalMatrix;
        if(Math.Abs(m.ScaleX-1)<1e-6&&Math.Abs(m.ScaleY-1)<1e-6&&Math.Abs(m.SkewX)+Math.Abs(m.SkewY)<1e-6)canvas.DrawImage(_image,x,y,paint);
        else canvas.DrawImage(_image,x,y,Sampling,paint);
    }

    public static MotionState Evaluate(IEnumerable<EffectClip> clips, double time, SKRect bounds, bool digitBounce = false)
    {
        var list = ClipTiming.EffectiveClips(clips,time);
        var firstEntry = list.FirstOrDefault(c => ProductInfo.IsEntrance(c.Options.Kind)||(c.Options.Kind==EffectKind.UserMotion&&c.Motion?.Value(MotionProperty.Opacity,0)==0));
        var state = new MotionState();
        var user=list.LastOrDefault(c=>c.Options.Kind==EffectKind.UserMotion&&c.Motion is not null&&time>=c.Start);
        if(user?.Motion is { } motion)
        {
            var frame=ClipTiming.Progress(user,time)*motion.LengthFrames;
            state.X=(float)motion.Value(MotionProperty.X,frame)*bounds.Width;state.Y=(float)motion.Value(MotionProperty.Y,frame)*bounds.Height;
            state.ScaleX=(float)motion.Value(MotionProperty.ScaleX,frame);state.ScaleY=(float)motion.Value(MotionProperty.ScaleY,frame);
            state.Angle=(float)motion.Value(MotionProperty.Rotation,frame);state.Alpha=(float)motion.Value(MotionProperty.Opacity,frame);
            state.AnchorX=(float)motion.Value(MotionProperty.AnchorX,frame);state.AnchorY=(float)motion.Value(MotionProperty.AnchorY,frame);
            state.CropTop=(float)motion.Value(MotionProperty.CropTop,frame);state.CropBottom=(float)motion.Value(MotionProperty.CropBottom,frame);state.CropLeft=(float)motion.Value(MotionProperty.CropLeft,frame);state.CropRight=(float)motion.Value(MotionProperty.CropRight,frame);
        }
        if (firstEntry is not null && time < firstEntry.Start) state.Alpha = 0;
        foreach (var clip in list.Where(c => time >= c.Start))
        {
            var p = ClipTiming.Progress(clip, time);
            var power = (float)clip.Options.Power; var speed = (float)clip.Options.Speed;
            var envelope = (float)Math.Pow(Math.Sin(Math.PI * p), 2);
            if (ProductInfo.IsEntrance(clip.Options.Kind))
            {
                var q = Math.Pow(p, 1 / speed); var ease = (float)(1 - Math.Pow(1 - q, 3));
                if(clip.Options.Kind!=EffectKind.WipeRight)state.Alpha *= ease;
                switch (clip.Options.Kind)
                {
                    case EffectKind.RiseIn: state.Y += (1 - ease) * bounds.Height * (.65f + .4f * power); break;
                    case EffectKind.FallIn: state.Y -= (1 - ease) * bounds.Height * (.65f + .4f * power); break;
                    case EffectKind.WipeRight: state.CropRight=Math.Max(state.CropRight,1-ease); break;
                    case EffectKind.ZoomIn: state.Scale *= .12f + .88f * ease; break;
                    case EffectKind.ZoomOutIn: state.Scale *= 1 + (1 - ease) * (.4f + .45f * power); break;
                }
                continue;
            }
            if (time >= clip.End) continue;
            switch (clip.Options.Kind)
            {
                case EffectKind.Zoom: state.Scale *= 1 + .23f * power * (float)Math.Pow(Math.Sin(p * Math.PI * speed), 2); break;
                case EffectKind.Bounce when !digitBounce: state.Y -= MathF.Abs(MathF.Sin((float)p * MathF.PI * 3 * speed)) * bounds.Height * .32f * power * envelope; break;
                case EffectKind.Shake:
                    state.Angle += MathF.Sin((float)p * MathF.PI * 14 * speed) * 8 * power * envelope;
                    state.X += MathF.Sin((float)p * MathF.PI * 20 * speed) * bounds.Width * .035f * power * envelope; break;
                case EffectKind.Float:
                    state.Y -= MathF.Sin((float)p * MathF.PI * 2 * speed) * bounds.Height * .22f * power * envelope;
                    state.Angle += MathF.Sin((float)p * MathF.PI * 2 * speed) * 2 * power * envelope; break;
            }
        }
        return state;
    }

    private static void DrawDecoration(SKCanvas canvas, float width, float height, EffectClip clip, float p, float opacity, EffectRegion region,SKImage? coverage=null)
    {
        var power = (float)clip.Options.Power;
        var edge = Math.Clamp(Math.Min(p, 1 - p) * 9, 0, 1);
        var amplitude = Math.Min(1, .65f * power) * opacity * edge;
        var r = SKRect.Create(width, height);
        using var outline = RegionMask.Create(region.Shape, r,region.Points);
        using var paint = new SKPaint { IsAntialias = true };
        SKColor Tint(SKColor color)=>clip.Options.Color is { } text?SKColor.Parse(text).WithAlpha((byte)(SKColor.Parse(text).Alpha*color.Alpha/255)):color;
        switch (clip.Options.Kind)
        {
            case EffectKind.Sweep:
                var x = (-.4f + 1.8f * p) * width;
                canvas.Save(); canvas.ClipPath(outline, antialias: true);canvas.SaveLayer();
                using (var shader = SKShader.CreateLinearGradient(new(x - width * .14f, 0), new(x + width * .14f, height),
                    [Tint(SKColors.White).WithAlpha(0), Tint(SKColors.White.WithAlpha((byte)(245 * amplitude))), Tint(SKColors.White).WithAlpha(0)], [0, .5f, 1], SKShaderTileMode.Clamp))
                { paint.Shader = shader; canvas.DrawRect(r, paint); }
                if(coverage is not null){using var mask=new SKPaint{BlendMode=SKBlendMode.DstIn};canvas.DrawImage(coverage,SKRect.Create(width,height),mask);}canvas.Restore();canvas.Restore(); break;
            case EffectKind.Glow:
                paint.Color = Tint(new SKColor(255, 218, 88, (byte)(240 * amplitude)));
                paint.Style = SKPaintStyle.Stroke; paint.StrokeWidth = Math.Max(3, height * .07f * power);
                using (var blur = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, Math.Max(3, height * .15f * power)))
                { paint.MaskFilter = blur; canvas.DrawPath(outline, paint); }
                paint.MaskFilter = null; paint.StrokeWidth = Math.Max(2, height * .025f); canvas.DrawPath(outline, paint);
                break;
            case EffectKind.Highlight:
                paint.Color = Tint(new SKColor(255, 225, 32, (byte)(Math.Min(210, 120 * power) * opacity)));
                paint.BlendMode = SKBlendMode.Multiply;
                canvas.Save(); canvas.ClipPath(outline, antialias: true);
                canvas.DrawRoundRect(SKRect.Create(-width * .02f, height * .3f, width * 1.04f * (float)(1 - Math.Pow(1 - p, 3)), height * .65f), height * .12f, height * .12f, paint); canvas.Restore();
                break;
            case EffectKind.Underline:
                paint.Color=Tint(new SKColor(255,225,32,(byte)(Math.Min(210,120*power)*opacity)));
                canvas.DrawRoundRect(SKRect.Create(-width*.03f,height*.86f,width*1.06f*(float)(1-Math.Pow(1-p,3)),Math.Max(2,height*.10f)),height*.05f,height*.05f,paint);break;
            case EffectKind.Sparkle:
                for (var i = 0; i < 8; i++)
                {
                    var sx = width * (i % 4) / 3; var sy = i < 4 ? -height * .08f : height * 1.08f;
                    var size = height * .22f * power * (.4f + .6f * MathF.Abs(MathF.Sin(p * 9 + i))) * edge;
                    paint.Color = Tint(new SKColor(255, 247, 192, (byte)(255 * amplitude)));
                    using var star = new SKPath();
                    star.MoveTo(sx, sy - size); star.LineTo(sx + size * .23f, sy - size * .23f); star.LineTo(sx + size, sy);
                    star.LineTo(sx + size * .23f, sy + size * .23f); star.LineTo(sx, sy + size); star.LineTo(sx - size * .23f, sy + size * .23f);
                    star.LineTo(sx - size, sy); star.LineTo(sx - size * .23f, sy - size * .23f); star.Close(); canvas.DrawPath(star, paint);
                }
                break;
            case EffectKind.Border:
                paint.Style = SKPaintStyle.Stroke; paint.StrokeWidth = Math.Max(3, height * .065f * power);
                using (var shader = SKShader.CreateSweepGradient(new(width / 2, height / 2),
                    [Tint(new SKColor(255, 211, 67, (byte)(255 * amplitude))), Tint(new SKColor(255, 255, 255, (byte)(255 * amplitude))), Tint(new SKColor(255, 211, 67, 30))],
                    [0, .3f, 1], SKShaderTileMode.Clamp, p * 360, p * 360 + 360))
                { paint.Shader = shader; canvas.DrawPath(outline, paint); }
                break;
            case EffectKind.Ripple:
                paint.Style = SKPaintStyle.Stroke; paint.StrokeWidth = Math.Max(4, Math.Min(width, height) * .10f * power);
                paint.Color = Tint(new SKColor(255, 225, 120, (byte)(240 * (1 - p) * amplitude)));
                var rippleRadius = MathF.Sqrt(width * width + height * height) * .5f + p * Math.Min(width, height) * .65f * power;
                canvas.DrawCircle(width / 2, height / 2, rippleRadius, paint);
                break;
            case EffectKind.Rays:
                paint.StrokeWidth = Math.Max(2, height * .04f * power); paint.StrokeCap = SKStrokeCap.Round;
                paint.Color = Tint(new SKColor(255, 229, 99, (byte)(255 * amplitude)));
                for (var i = 0; i < 12; i++)
                {
                    var a = i * MathF.PI / 6; var spread = .15f + p * .45f * power;
                    var inner = new SKPoint(width / 2 + MathF.Cos(a) * width * (.5f + spread), height / 2 + MathF.Sin(a) * height * (.5f + spread));
                    var outer = new SKPoint(inner.X + MathF.Cos(a) * height * .35f * power, inner.Y + MathF.Sin(a) * height * .35f * power);
                    canvas.DrawLine(inner, outer, paint);
                }
                break;
            case EffectKind.OrbitBurst:
                for (var i = 0; i < 10; i++)
                {
                    var a = i * MathF.PI / 5 + p * MathF.PI * 2 * (float)clip.Options.Speed;
                    var radius = .45f + p * .7f * power;
                    paint.Color = Tint((i % 2 == 0 ? new SKColor(255, 230, 78) : new SKColor(112, 243, 222)).WithAlpha((byte)(240 * amplitude)));
                    canvas.DrawCircle(width / 2 + MathF.Cos(a) * width * radius, height / 2 + MathF.Sin(a) * height * radius, Math.Max(2, height * .085f * power * (1 - p * .6f)), paint);
                }
                break;
        }
    }

    private static void DrawDigits(SKCanvas canvas, Layer layer, EffectClip? roll, EffectClip? bounce, double time, SKPaint paint)
    {
        canvas.Save();
        if(roll is not null)canvas.ClipPath(layer.Path,antialias:true);
        canvas.DrawImage(layer.BackingImage, 0, 0, Sampling, paint);
        using var font = new SKFont(SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold), 40);
        using var textPaint = new SKPaint { Color = layer.Ink.WithAlpha(paint.Color.Alpha), IsAntialias = true };
        for (var i = 0; i < layer.Digits.Count; i++)
        {
            var cell = layer.Digits[i]; var target = cell; var offset = 0f;
            if (bounce is not null)
            {
                var p = (float)ClipTiming.Progress(bounce, time);
                var delayed = Math.Clamp((p - (float)i / layer.Digits.Count * .35f) / .65f, 0, 1);
                offset = -MathF.Sin(delayed * MathF.PI) * cell.Height * .75f * (float)bounce.Options.Power;
                target.Offset(0, offset);
            }
            if (roll is null || ClipTiming.Progress(roll, time) >= .5 + .48 * (i + 1d) / layer.Digits.Count)
            { canvas.DrawImage(layer.Image, cell, target, Sampling, paint); continue; }
            var progress = ClipTiming.Progress(roll, time);
            var turns = (1 - Math.Pow(1 - progress, 2)) * 45 * roll.Options.Speed;
            var tick = (int)Math.Floor(turns);
            var number = (int)((unchecked((uint)roll.Id.GetHashCode()) + (uint)i * 17 + (uint)tick * 71) % 10);
            var shift = (float)(turns - tick) * cell.Height;
            canvas.Save(); canvas.ClipRect(target);
            font.Size = cell.Height / .74f;
            for (var row = -1; row <= 0; row++)
            {
                var text = ((number - row + 10) % 10).ToString();
                font.MeasureText(text, out var bounds);
                canvas.DrawText(text, target.MidX - bounds.MidX, target.MidY - bounds.MidY + shift + row * cell.Height, font, textPaint);
            }
            canvas.Restore();
        }
        canvas.Restore();
    }

    private Layer GetLayer(EffectRegion region,IReadOnlyList<EffectRegion>? higher=null)
    {
        higher??=[];
        var rect=RegionRaster.Rect(region,Width,Height);
        if (_layers.TryGetValue(region.Id, out var old))
        {
            if(old.Bounds==(SKRect)rect&&SameGeometry(old.Geometry,region)&&old.Higher.Count==higher.Count&&old.Higher.Zip(higher).All(p=>SameGeometry(p.First,p.Second)))return old;
            old.Dispose(); _layers.Remove(region.Id);
        }
        var background = RegionSeparation.Background(_source,region);
        if(RegionRaster.Custom(region))
        {
            using var raw=RegionRaster.Raw(_source,region,rect,background);
            using var shape=RegionRaster.Shape(region,rect,Width,Height);
            var foreground=raw.Copy();Multiply(foreground,shape);var backingCustom=foreground.Copy();
            var digitsCustom=DigitCells.Find(foreground,background);
            using(var c=new SKCanvas(backingCustom))using(var paint=new SKPaint{Color=region.ContentOnly?SKColors.Transparent:background,BlendMode=SKBlendMode.Src})foreach(var digit in digitsCustom)c.DrawRect(digit,paint);
            using var eraseCustom=RegionRaster.Shape(region,rect,Width,Height,false);
            var custom=new Layer(foreground,backingCustom,rect,region.Background,region.BackgroundColor,background,DigitCells.InkColor(foreground,background),digitsCustom,region,higher,eraseCustom.Copy())
            {RawImage=SKImage.FromBitmap(raw),ShapeImage=SKImage.FromBitmap(shape)};
            _layers.Add(region.Id,custom);return custom;
        }
        using var subset = new SKBitmap();
        if(!_source.ExtractSubset(subset,rect))throw new InvalidDataException("영역을 추출할 수 없습니다.");
        var bitmap = subset.Copy();
        using(var corrected=region.Cutout?.DecodeForeground())if(corrected is not null)using(var image=SKImage.FromBitmap(corrected))using(var c=new SKCanvas(bitmap)){c.Clear(SKColors.Transparent);c.DrawImage(image,SKRect.Create(bitmap.Width,bitmap.Height));}
        SKBitmap? maskBitmap=null;
        if(region.Cutout is { } cutout)
        {
            using var decoded=cutout.Decode();using var maskImage=SKImage.FromBitmap(decoded);
            maskBitmap=new SKBitmap(bitmap.Width,bitmap.Height);using(var mc=new SKCanvas(maskBitmap)){mc.Clear(SKColors.Transparent);mc.DrawImage(maskImage,SKRect.Create(bitmap.Width,bitmap.Height),Sampling);}
            for(var y=0;y<bitmap.Height;y++)for(var x=0;x<bitmap.Width;x++)
            {var c=bitmap.GetPixel(x,y);bitmap.SetPixel(x,y,c.WithAlpha((byte)(c.Alpha*maskBitmap.GetPixel(x,y).Alpha/255)));}
        }
        if (region.Shape != RegionShape.Rectangle)
        {
            using var originalBitmap = bitmap.Copy();
            using var original = SKImage.FromBitmap(originalBitmap);
            using var c = new SKCanvas(bitmap); c.Clear(SKColors.Transparent);
            using var path = RegionMask.Create(region.Shape, SKRect.Create(bitmap.Width, bitmap.Height),region.Points);
            c.ClipPath(path, antialias: true); c.DrawImage(original, 0, 0);
        }
        foreach(var upper in higher)
        {
            var upperRect=ObjectExtractor.PixelRect(upper.Bounds,Width,Height);
            var overlap=SKRectI.Intersect(rect,upperRect);if(overlap.IsEmpty)continue;
            using var coverage=Coverage(upper,upperRect);
            var fill=upper.Background switch{BackgroundMode.Transparent=>SKColors.Transparent,BackgroundMode.Solid=>SKColor.Parse(upper.BackgroundColor),_=>GuessBackground(upperRect)};
            for(var y=overlap.Top;y<overlap.Bottom;y++)for(var x=overlap.Left;x<overlap.Right;x++)
            {
                if(coverage.GetPixel(x-upperRect.Left,y-upperRect.Top).Alpha==0)continue;
                var current=bitmap.GetPixel(x-rect.Left,y-rect.Top);if(current.Alpha==0)continue;
                bitmap.SetPixel(x-rect.Left,y-rect.Top,fill.WithAlpha((byte)(fill.Alpha*current.Alpha/255)));
            }
        }
        var digits = DigitCells.Find(bitmap, background);
        var backing = bitmap.Copy();
        using (var c = new SKCanvas(backing))
        using (var fill = new SKPaint { Color = background, BlendMode = SKBlendMode.Src })
        {
            using var shape = RegionMask.Create(region.Shape, SKRect.Create(bitmap.Width,bitmap.Height),region.Points);c.ClipPath(shape,antialias:true);
            foreach (var digit in digits) c.DrawRect(digit, fill);
        }
        var ink = DigitCells.InkColor(bitmap, background);
        var layer = new Layer(bitmap, backing, rect, region.Background, region.BackgroundColor, background, ink, digits, region, higher,Coverage(region,rect));
        maskBitmap?.Dispose();
        _layers.Add(region.Id, layer); return layer;
    }

    private static bool SameGeometry(EffectRegion a,EffectRegion b)=>a.Id==b.Id&&a.Bounds==b.Bounds&&a.Shape==b.Shape&&a.Softness==b.Softness&&a.ContentOnly==b.ContentOnly&&a.ContentTolerance==b.ContentTolerance&&a.Background==b.Background&&a.BackgroundColor==b.BackgroundColor&&a.CutoutEnabled==b.CutoutEnabled&&a.Points.SequenceEqual(b.Points)&&a.Cutout?.MaskPng==b.Cutout?.MaskPng&&a.Cutout?.ForegroundPng==b.Cutout?.ForegroundPng&&a.Cutout?.ErasePng==b.Cutout?.ErasePng;
    private static EffectRegion Geometry(EffectRegion r)=>new(){Id=r.Id,Bounds=r.Bounds,Shape=r.Shape,Softness=r.Softness with{},ContentOnly=r.ContentOnly,ContentTolerance=r.ContentTolerance,CutoutEnabled=r.CutoutEnabled,Points=r.Points.Select(p=>p with{}).ToList(),Background=r.Background,BackgroundColor=r.BackgroundColor,Cutout=r.Cutout is null?null:r.Cutout with{}};
    private static SKBitmap Coverage(EffectRegion region,SKRectI rect)
    {
        var bitmap=new SKBitmap(rect.Width,rect.Height);bitmap.Erase(SKColors.Transparent);
        using var c=new SKCanvas(bitmap);using var path=RegionMask.Create(region.Shape,SKRect.Create(rect.Width,rect.Height),region.Points);c.ClipPath(path,antialias:true);
        using var white=new SKPaint{Color=SKColors.White};c.DrawRect(SKRect.Create(rect.Width,rect.Height),white);
        if(region.Cutout is { } cutout)
        {
            using var mask=string.IsNullOrEmpty(cutout.ErasePng)?cutout.Decode():SKBitmap.Decode(Convert.FromBase64String(cutout.ErasePng))!;
            using var binary=mask.Copy();for(var y=0;y<binary.Height;y++)for(var x=0;x<binary.Width;x++)binary.SetPixel(x,y,SKColors.White.WithAlpha((byte)(mask.GetPixel(x,y).Alpha>0?255:0)));
            using var image=SKImage.FromBitmap(binary);using var paint=new SKPaint{BlendMode=SKBlendMode.DstIn};c.DrawImage(image,SKRect.Create(rect.Width,rect.Height),paint);
        }
        return bitmap;
    }

    private SKColor GuessBackground(SKRectI rect)
    {
        var colors = new List<SKColor>();
        var step = Math.Max(1, Math.Max(rect.Width, rect.Height) / 100);
        for (var x = rect.Left; x < rect.Right; x += step)
        { colors.Add(_source.GetPixel(x, Math.Clamp(rect.Top - 1, 0, Height - 1))); colors.Add(_source.GetPixel(x, Math.Clamp(rect.Bottom, 0, Height - 1))); }
        for (var y = rect.Top; y < rect.Bottom; y += step)
        { colors.Add(_source.GetPixel(Math.Clamp(rect.Left - 1, 0, Width - 1), y)); colors.Add(_source.GetPixel(Math.Clamp(rect.Right, 0, Width - 1), y)); }
        if (colors.Count(c => c.Alpha < 32) > colors.Count / 2) return SKColors.Transparent;
        var opaque = colors.Where(c => c.Alpha >= 32).ToList();
        byte Median(Func<SKColor, byte> component) => opaque.Select(component).Order().ElementAt(opaque.Count / 2);
        return new(Median(c => c.Red), Median(c => c.Green), Median(c => c.Blue), Median(c => c.Alpha));
    }

    public void Dispose() { foreach (var layer in _layers.Values) layer.Dispose(); _layers.Clear();_maskRenderer.Dispose(); _image.Dispose(); }

    private sealed class Layer : IDisposable
    {
        private readonly SKBitmap bitmap,backing;
        public EffectRegion Geometry { get; }
        public List<EffectRegion> Higher { get; }
        public Layer(SKBitmap bitmap,SKBitmap backing,SKRect bounds,BackgroundMode mode,string color,SKColor background,SKColor ink,List<SKRect> digits,EffectRegion region,IReadOnlyList<EffectRegion> higher,SKBitmap coverage)
        {
            this.bitmap=bitmap;this.backing=backing;Bitmap=bitmap;Image=SKImage.FromBitmap(bitmap);BackingImage=SKImage.FromBitmap(backing);Bounds=bounds;Mode=mode;Color=color;Background=background;Ink=ink;Shape=region.Shape;Digits=digits;MaskData=region.Cutout?.MaskPng;
            Geometry=EffectScene.Geometry(region);Higher=higher.Select(EffectScene.Geometry).ToList();Path=RegionMask.Create(region.Shape,SKRect.Create(bitmap.Width,bitmap.Height),region.Points);
            using(coverage)using(var fillBitmap=new SKBitmap(coverage.Width,coverage.Height))
            {
                MakeSupport(coverage);
                fillBitmap.Erase(SKColors.Transparent);
                for(var y=0;y<coverage.Height;y++)for(var x=0;x<coverage.Width;x++)fillBitmap.SetPixel(x,y,background.WithAlpha((byte)(background.Alpha*coverage.GetPixel(x,y).Alpha/255)));
                EraseImage=SKImage.FromBitmap(coverage);FillImage=SKImage.FromBitmap(fillBitmap);
            }
        }
        public SKBitmap Bitmap { get; }
        public SKImage Image { get; }
        public SKImage BackingImage { get; }
        public SKImage? RawImage { get; init; }
        public SKImage? ShapeImage { get; init; }
        public SKImage? EraseImage { get; }
        public SKImage? FillImage { get; }
        public string? MaskData { get; }
        public SKRect Bounds { get; }
        public BackgroundMode Mode { get; }
        public string Color { get; }
        public SKColor Background { get; }
        public SKColor Ink { get; }
        public RegionShape Shape { get; }
        public SKPath Path { get; }
        public List<SKRect> Digits { get; }
        public void Dispose() { RawImage?.Dispose();ShapeImage?.Dispose();EraseImage?.Dispose();FillImage?.Dispose();Path.Dispose();Image.Dispose();BackingImage.Dispose();bitmap.Dispose();backing.Dispose(); }
    }
}

public sealed class MotionState
{
    public float CropTop { get; set; }public float CropBottom { get; set; }public float CropLeft { get; set; }public float CropRight { get; set; }
    public bool HasCrop=>CropTop>0||CropBottom>0||CropLeft>0||CropRight>0;
    public float Alpha { get; set; } = 1;
    public float Scale { get; set; } = 1;
    public float ScaleX { get; set; }=1;
    public float ScaleY { get; set; }=1;
    public float AnchorX { get; set; }=.5f;
    public float AnchorY { get; set; }=.5f;
    public float X { get; set; }
    public float Y { get; set; }
    public float Angle { get; set; }
}

public static class DigitCells
{
    private static bool IsInk(SKColor c, SKColor background) => c.Alpha > 80 && (background.Alpha < 32 || Math.Max(Math.Abs(c.Red - background.Red), Math.Max(Math.Abs(c.Green - background.Green), Math.Abs(c.Blue - background.Blue))) > 65);
    public static List<SKRect> Find(SKBitmap image, SKColor background)
    {
        var runs = new List<SKRect>(); var start = -1; var top = image.Height; var bottom = 0;
        for (var x = 0; x <= image.Width; x++)
        {
            var count = 0; var min = image.Height; var max = 0;
            if (x < image.Width) for (var y = 0; y < image.Height; y++) if (IsInk(image.GetPixel(x, y), background)) { count++; min = Math.Min(min, y); max = Math.Max(max, y + 1); }
            if (count >= Math.Max(1, image.Height * .025))
            { if (start < 0) { start = x; top = min; bottom = max; } else { top = Math.Min(top, min); bottom = Math.Max(bottom, max); } }
            else if (start >= 0) { runs.Add(SKRect.Create(start, top, x - start, bottom - top)); start = -1; }
        }
        var height = runs.Select(r => r.Height).DefaultIfEmpty(0).Max();
        return runs.Where(r => r.Height >= Math.Max(7, height * .68) && r.Width >= r.Height * .17 && r.Width <= r.Height * 1.15)
            .Select(r=>SKRect.Intersect(SKRect.Create(0,0,image.Width,image.Height),new SKRect(r.Left-2,r.Top-2,r.Right+2,r.Bottom+2))).ToList();
    }
    public static SKColor InkColor(SKBitmap image, SKColor background)
    {
        var colors = new List<SKColor>();
        for (var y = 0; y < image.Height; y += 2) for (var x = 0; x < image.Width; x += 2)
            if (IsInk(image.GetPixel(x, y), background)) colors.Add(image.GetPixel(x, y));
        if (colors.Count == 0) return SKColors.White;
        return new(colors.Select(c => c.Red).Order().ElementAt(colors.Count / 2), colors.Select(c => c.Green).Order().ElementAt(colors.Count / 2), colors.Select(c => c.Blue).Order().ElementAt(colors.Count / 2));
    }
}
