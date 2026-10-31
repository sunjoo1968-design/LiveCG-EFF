using SkiaSharp;

namespace LiveCG.Core;

public record ObjectCutout
{
    public int Width { get; set; }
    public int Height { get; set; }
    public string MaskPng { get; set; }="";
    public string ForegroundPng { get; set; }="";
    public string ErasePng { get; set; }="";
    public void Validate()
    {
        if(Width<1||Height<1||(long)Width*Height>16000000||string.IsNullOrEmpty(MaskPng)||MaskPng.Length>16000000)throw new InvalidDataException("오브젝트 마스크 크기/내용이 올바르지 않습니다.");
        try{var bytes=Convert.FromBase64String(MaskPng);using var codec=SKCodec.Create(new SKMemoryStream(bytes));if(codec is null||codec.Info.Width!=Width||codec.Info.Height!=Height)throw new InvalidDataException("마스크 이미지 크기가 다릅니다.");}
        catch(FormatException ex){throw new InvalidDataException("마스크 데이터가 손상됐습니다.",ex);}
        foreach(var png in new[]{ForegroundPng,ErasePng})if(!string.IsNullOrEmpty(png))
        {if(png.Length>32000000)throw new InvalidDataException("보정 이미지가 너무 큽니다.");try{using var codec=SKCodec.Create(new SKMemoryStream(Convert.FromBase64String(png)));if(codec is null||codec.Info.Width!=Width||codec.Info.Height!=Height)throw new InvalidDataException("보정 이미지 크기가 다릅니다.");}catch(FormatException ex){throw new InvalidDataException("보정 이미지가 손상됐습니다.",ex);}}
    }
    public SKBitmap Decode()
    {
        Validate();
        try
        {
            var bytes=Convert.FromBase64String(MaskPng);using var codec=SKCodec.Create(new SKMemoryStream(bytes));
            if(codec is null||codec.Info.Width!=Width||codec.Info.Height!=Height)throw new InvalidDataException("마스크 이미지 크기가 다릅니다.");
            return SKBitmap.Decode(bytes)??throw new InvalidDataException("마스크 이미지를 읽지 못했습니다.");
        }catch(FormatException ex){throw new InvalidDataException("마스크 데이터가 손상됐습니다.",ex);}
    }
    public SKBitmap? DecodeForeground()=>string.IsNullOrEmpty(ForegroundPng)?null:SKBitmap.Decode(Convert.FromBase64String(ForegroundPng))??throw new InvalidDataException("보정 색상을 읽지 못했습니다.");
}
public enum ExtractionMode { Auto, Transparency, BackgroundColor }
public record ExtractionOptions
{
    public ExtractionMode Mode { get; set; }
    public SKColor? Background { get; set; }
    public double Tolerance { get; set; }=35;
    public int Feather { get; set; }=1;
    public bool IncludeShadow { get; set; }=true;
    public bool PreserveInterior { get; set; }=true;
}
public sealed class MaskBuffer(int width,int height,byte[] alpha)
{
    public int Width { get; }=width;
    public int Height { get; }=height;
    public byte[] Alpha { get; }=alpha;
    public MaskBuffer Copy()=>new(Width,Height,(byte[])Alpha.Clone());
    public MaskBuffer Morph(int radius,bool expand)
    {
        if(radius is <0 or >6)throw new InvalidDataException("테두리 정리는0~6px입니다.");if(radius==0)return Copy();var temp=new byte[Alpha.Length];var result=new byte[Alpha.Length];
        for(var y=0;y<Height;y++)for(var x=0;x<Width;x++){var value=expand?0:255;for(var k=-radius;k<=radius;k++){var a=x+k<0||x+k>=Width?0:Alpha[y*Width+x+k];value=expand?Math.Max(value,a):Math.Min(value,a);}temp[y*Width+x]=(byte)value;}
        for(var y=0;y<Height;y++)for(var x=0;x<Width;x++){var value=expand?0:255;for(var k=-radius;k<=radius;k++){var a=y+k<0||y+k>=Height?0:temp[(y+k)*Width+x];value=expand?Math.Max(value,a):Math.Min(value,a);}result[y*Width+x]=(byte)value;}return new(Width,Height,result);
    }
    public void Brush(float x,float y,float radius,bool include)
    {
        radius=Math.Max(1,radius);
        for(var py=Math.Max(0,(int)(y-radius));py<Math.Min(Height,y+radius+1);py++)for(var px=Math.Max(0,(int)(x-radius));px<Math.Min(Width,x+radius+1);px++)
            if((px-x)*(px-x)+(py-y)*(py-y)<=radius*radius)Alpha[py*Width+px]=(byte)(include?255:0);
    }
    public SKRectI TightBounds()
    {
        var l=Width;var t=Height;var r=0;var b=0;
        for(var y=0;y<Height;y++)for(var x=0;x<Width;x++)if(Alpha[y*Width+x]>3){l=Math.Min(l,x);t=Math.Min(t,y);r=Math.Max(r,x+1);b=Math.Max(b,y+1);}
        if(r<=l||b<=t)throw new InvalidDataException("선택된 오브젝트가 없습니다. 허용오차나 브러시를 조정하세요.");
        return new(l,t,r,b);
    }
    public MaskBuffer Crop(SKRectI rect)
    {
        var bytes=new byte[rect.Width*rect.Height];for(var y=0;y<rect.Height;y++)Array.Copy(Alpha,(rect.Top+y)*Width+rect.Left,bytes,y*rect.Width,rect.Width);return new(rect.Width,rect.Height,bytes);
    }
    public SKBitmap Bitmap()
    {
        var bitmap=new SKBitmap(Width,Height);
        for(var y=0;y<Height;y++)for(var x=0;x<Width;x++)bitmap.SetPixel(x,y,SKColors.White.WithAlpha(Alpha[y*Width+x]));return bitmap;
    }
    public ObjectCutout Encode()
    {using var bitmap=Bitmap();using var image=SKImage.FromBitmap(bitmap);using var data=image.Encode(SKEncodedImageFormat.Png,100);return new(){Width=Width,Height=Height,MaskPng=Convert.ToBase64String(data.ToArray())};}
    public static MaskBuffer Decode(ObjectCutout mask)
    {using var bitmap=mask.Decode();var bytes=new byte[bitmap.Width*bitmap.Height];for(var y=0;y<bitmap.Height;y++)for(var x=0;x<bitmap.Width;x++)bytes[y*bitmap.Width+x]=bitmap.GetPixel(x,y).Alpha;return new(bitmap.Width,bitmap.Height,bytes);}
}
public static class ObjectExtractor
{
    public static SKBitmap CropObject(SKBitmap source,EffectRegion region)
    {
        var rect=PixelRect(region.Bounds,source.Width,source.Height);using var subset=new SKBitmap();if(!source.ExtractSubset(subset,rect))throw new InvalidDataException("영역을 추출하지 못했습니다.");
        var bitmap=subset.Copy();
        if(region.Cutout is { } cutout)
        {
            using var corrected=cutout.DecodeForeground();if(corrected is not null)using(var correctedImage=SKImage.FromBitmap(corrected))using(var canvas=new SKCanvas(bitmap)){canvas.Clear(SKColors.Transparent);canvas.DrawImage(correctedImage,SKRect.Create(bitmap.Width,bitmap.Height));}
            using var mask=cutout.Decode();using var image=SKImage.FromBitmap(mask);using var scaled=new SKBitmap(bitmap.Width,bitmap.Height);
            using(var canvas=new SKCanvas(scaled)){canvas.Clear(SKColors.Transparent);canvas.DrawImage(image,SKRect.Create(bitmap.Width,bitmap.Height));}
            for(var y=0;y<bitmap.Height;y++)for(var x=0;x<bitmap.Width;x++){var c=bitmap.GetPixel(x,y);bitmap.SetPixel(x,y,c.WithAlpha((byte)(c.Alpha*scaled.GetPixel(x,y).Alpha/255)));}
        }
        using var original=bitmap.Copy();using(var canvas=new SKCanvas(bitmap))using(var image=SKImage.FromBitmap(original))using(var path=RegionMask.Create(region.Shape,SKRect.Create(bitmap.Width,bitmap.Height),region.Points))
        {canvas.Clear(SKColors.Transparent);canvas.ClipPath(path,antialias:true);canvas.DrawImage(image,0,0);}return bitmap;
    }
    public static SKRectI PixelRect(NormalizedRect bounds,int width,int height)
    {
        // Older documents stored float divisions; snap subpixel round-off at integer edges.
        var left=Math.Clamp((int)Math.Floor(bounds.X*width+.001),0,width-1);var top=Math.Clamp((int)Math.Floor(bounds.Y*height+.001),0,height-1);
        return new(left,top,Math.Clamp((int)Math.Ceiling((bounds.X+bounds.Width)*width-.001),left+1,width),Math.Clamp((int)Math.Ceiling((bounds.Y+bounds.Height)*height-.001),top+1,height));
    }
    public static SKColor GuessSurrounding(SKBitmap source,SKRectI rect)
    {
        if(rect.Left==0&&rect.Top==0&&rect.Right==source.Width&&rect.Bottom==source.Height)return GuessBackground(source);
        var samples=new List<SKColor>();var step=Math.Max(1,Math.Max(rect.Width,rect.Height)/100);
        for(var x=rect.Left;x<rect.Right;x+=step){if(rect.Top>0)samples.Add(source.GetPixel(x,rect.Top-1));if(rect.Bottom<source.Height)samples.Add(source.GetPixel(x,rect.Bottom));}
        for(var y=rect.Top;y<rect.Bottom;y+=step){if(rect.Left>0)samples.Add(source.GetPixel(rect.Left-1,y));if(rect.Right<source.Width)samples.Add(source.GetPixel(rect.Right,y));}
        if(samples.Count==0)return GuessBackground(source);if(samples.Count(c=>c.Alpha<32)>samples.Count/2)return SKColors.Transparent;var opaque=samples.Where(c=>c.Alpha>=32).ToArray();byte Median(Func<SKColor,byte> component)=>opaque.Select(component).Order().ElementAt(opaque.Length/2);return new(Median(c=>c.Red),Median(c=>c.Green),Median(c=>c.Blue));
    }
    public static SKColor GuessBackground(SKBitmap source)
    {
        var colors=new List<SKColor>();var step=Math.Max(1,Math.Max(source.Width,source.Height)/100);
        for(var x=0;x<source.Width;x+=step){colors.Add(source.GetPixel(x,0));colors.Add(source.GetPixel(x,source.Height-1));}
        for(var y=0;y<source.Height;y+=step){colors.Add(source.GetPixel(0,y));colors.Add(source.GetPixel(source.Width-1,y));}
        if(colors.Count(c=>c.Alpha<32)>colors.Count/2)return SKColors.Transparent;
        var opaque=colors.Where(c=>c.Alpha>=32).ToArray();
        byte Median(Func<SKColor,byte> component)=>opaque.Select(component).Order().ElementAt(opaque.Length/2);
        return new(Median(c=>c.Red),Median(c=>c.Green),Median(c=>c.Blue));
    }
    public static MaskBuffer Extract(SKBitmap crop,ExtractionOptions options)
    {
        if((long)crop.Width*crop.Height>16000000||options.Tolerance is <0 or >255||options.Feather is <0 or >6)throw new InvalidDataException("분리 범위·허용오차·가장자리를 확인하세요.");
        var background=options.Background??GuessBackground(crop);var mode=options.Mode;
        if(mode==ExtractionMode.Auto)mode=background.Alpha<32?ExtractionMode.Transparency:ExtractionMode.BackgroundColor;
        var bytes=new byte[crop.Width*crop.Height];var tolerance=options.Tolerance*(options.IncludeShadow?.65:1);
        for(var y=0;y<crop.Height;y++)for(var x=0;x<crop.Width;x++)
        {
            var c=crop.GetPixel(x,y);byte value;
            if(mode==ExtractionMode.Transparency)value=(byte)(c.Alpha>0?255:0);
            else
            {
                var distance=Math.Sqrt(Math.Pow(c.Red-background.Red,2)+Math.Pow(c.Green-background.Green,2)+Math.Pow(c.Blue-background.Blue,2));
                value=(byte)(c.Alpha==0?0:Math.Clamp((distance-tolerance)/12,0,1)*255);
            }
            bytes[y*crop.Width+x]=value;
        }
        if(mode==ExtractionMode.BackgroundColor&&options.PreserveInterior)
        {
            var visited=new bool[bytes.Length];var queue=new int[bytes.Length];var head=0;var tail=0;
            void Enqueue(int index){if(visited[index]||bytes[index]==255)return;visited[index]=true;queue[tail++]=index;}
            for(var x=0;x<crop.Width;x++){Enqueue(x);Enqueue((crop.Height-1)*crop.Width+x);}for(var y=0;y<crop.Height;y++){Enqueue(y*crop.Width);Enqueue(y*crop.Width+crop.Width-1);}
            while(head<tail){var i=queue[head++];var x=i%crop.Width;var y=i/crop.Width;if(x>0)Enqueue(i-1);if(x+1<crop.Width)Enqueue(i+1);if(y>0)Enqueue(i-crop.Width);if(y+1<crop.Height)Enqueue(i+crop.Width);}
            for(var i=0;i<bytes.Length;i++)bytes[i]=(byte)(visited[i]||crop.GetPixel(i%crop.Width,i/crop.Width).Alpha==0?0:255);
        }
        if(options.Feather>0)
        {
            var temp=new byte[bytes.Length];var radius=options.Feather;
            for(var y=0;y<crop.Height;y++)for(var x=0;x<crop.Width;x++){var sum=0;var n=0;for(var k=-radius;k<=radius;k++)if(x+k>=0&&x+k<crop.Width){sum+=bytes[y*crop.Width+x+k];n++;}temp[y*crop.Width+x]=(byte)(sum/n);}
            for(var y=0;y<crop.Height;y++)for(var x=0;x<crop.Width;x++){var sum=0;var n=0;for(var k=-radius;k<=radius;k++)if(y+k>=0&&y+k<crop.Height){sum+=temp[(y+k)*crop.Width+x];n++;}bytes[y*crop.Width+x]=(byte)(sum/n);}
        }
        return new(crop.Width,crop.Height,bytes);
    }
    public static string EncodeBitmap(SKBitmap bitmap){using var image=SKImage.FromBitmap(bitmap);using var data=image.Encode(SKEncodedImageFormat.Png,100);return Convert.ToBase64String(data.ToArray());}
    public static (MaskBuffer Mask,string Foreground) CleanEdges(SKBitmap source,MaskBuffer mask,SKColor background,int radius=2)
    {
        var result=mask.Copy();var inner=mask.Morph(radius,false);using var foreground=source.Copy();
        for(var y=0;y<mask.Height;y++)for(var x=0;x<mask.Width;x++)
        {
            var i=y*mask.Width+x;if(mask.Alpha[i]==0||inner.Alpha[i]>240)continue;
            var c=source.GetPixel(x,y);var best=0d;var fg=c;
            for(var dy=-radius-1;dy<=radius+1;dy++)for(var dx=-radius-1;dx<=radius+1;dx++)
            {var px=x+dx;var py=y+dy;if(px<0||py<0||px>=mask.Width||py>=mask.Height||inner.Alpha[py*mask.Width+px]<200)continue;var n=source.GetPixel(px,py);var contrast=Math.Pow(n.Red-background.Red,2)+Math.Pow(n.Green-background.Green,2)+Math.Pow(n.Blue-background.Blue,2);var score=contrast/(1+.1*(dx*dx+dy*dy));if(score>best){best=score;fg=n;}}
            var r=(double)fg.Red-background.Red;var g=(double)fg.Green-background.Green;var b=(double)fg.Blue-background.Blue;var denominator=r*r+g*g+b*b;if(best<1||denominator<1)continue;
            var coverage=Math.Clamp(((c.Red-background.Red)*r+(c.Green-background.Green)*g+(c.Blue-background.Blue)*b)/denominator,0,1);
            if(coverage<.999){result.Alpha[i]=(byte)Math.Round(mask.Alpha[i]*coverage);foreground.SetPixel(x,y,fg.WithAlpha(c.Alpha));}
        }
        return(result,EncodeBitmap(foreground));
    }
    public static (NormalizedRect Bounds,ObjectCutout Cutout) Fit(MaskBuffer mask,SKRectI original,int sourceWidth,int sourceHeight,string? foreground=null,MaskBuffer? erase=null)
    {
        var tight=mask.TightBounds();if(erase is not null){var e=erase.TightBounds();tight=new(Math.Min(tight.Left,e.Left),Math.Min(tight.Top,e.Top),Math.Max(tight.Right,e.Right),Math.Max(tight.Bottom,e.Bottom));}var bounds=SKRect.Create(original.Left+tight.Left,original.Top+tight.Top,tight.Width,tight.Height);
        var cutout=mask.Crop(tight).Encode();if(erase is not null)cutout.ErasePng=erase.Crop(tight).Encode().MaskPng;
        if(!string.IsNullOrEmpty(foreground)){using var full=SKBitmap.Decode(Convert.FromBase64String(foreground));using var cropped=new SKBitmap();if(full is null||!full.ExtractSubset(cropped,tight))throw new InvalidDataException("보정 색상 범위가 다릅니다.");cutout.ForegroundPng=EncodeBitmap(cropped);}
        return(NormalizedRect.FromPixels(bounds,sourceWidth,sourceHeight),cutout);
    }
}
