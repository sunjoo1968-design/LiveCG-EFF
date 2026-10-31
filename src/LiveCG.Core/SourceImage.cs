using SkiaSharp;

namespace LiveCG.Core;
public static class SourceImage
{
    private static readonly HashSet<string> Extensions=new(StringComparer.OrdinalIgnoreCase){".png",".jpg",".jpeg",".bmp",".webp"};
    public static bool Supported(string path)=>Extensions.Contains(Path.GetExtension(path));
    public const string Filter="이미지 (PNG/JPEG/BMP/WebP)|*.png;*.jpg;*.jpeg;*.bmp;*.webp";
    public static SKBitmap Read(ImageJob job)
    {
        var image=Read(job.SourcePath);if(image.Width==job.Width&&image.Height==job.Height)return image;
        image.Dispose();throw new InvalidDataException("원본 이미지 크기가 저장한 작업과 다릅니다. 다시 가져오세요.");
    }
    public static SKBitmap Read(string path)
    {
        using var stream=File.OpenRead(path);using var codec=SKCodec.Create(stream)??throw new InvalidDataException("이미지를 읽을 수 없습니다.");
        if(codec.FrameCount>1)throw new InvalidDataException("움직이는 이미지는 지원하지 않습니다. 정지 이미지로 저장해 주세요.");
        var info=codec.Info;if(info.Width<=0||info.Height<=0||(long)info.Width*info.Height>80_000_000)throw new InvalidDataException("이미지는 최대 8천만 픽셀입니다.");
        using var raw=new SKBitmap(new SKImageInfo(info.Width,info.Height,SKColorType.Bgra8888,SKAlphaType.Premul));
        if(codec.GetPixels(raw.Info,raw.GetPixels())!=SKCodecResult.Success)throw new InvalidDataException("이미지 데이터가 손상되었습니다.");
        var origin=(int)codec.EncodedOrigin;var swap=origin>=5;var w=raw.Width;var h=raw.Height;
        var result=new SKBitmap(new SKImageInfo(swap?h:w,swap?w:h,SKColorType.Bgra8888,SKAlphaType.Premul));
        using var canvas=new SKCanvas(result);canvas.Clear(SKColors.Transparent);
        var m=origin switch{2=>new SKMatrix(-1,0,w,0,1,0,0,0,1),3=>new SKMatrix(-1,0,w,0,-1,h,0,0,1),4=>new SKMatrix(1,0,0,0,-1,h,0,0,1),5=>new SKMatrix(0,1,0,1,0,0,0,0,1),6=>new SKMatrix(0,-1,h,1,0,0,0,0,1),7=>new SKMatrix(0,-1,h,-1,0,w,0,0,1),8=>new SKMatrix(0,1,0,-1,0,w,0,0,1),_=>SKMatrix.Identity};
        canvas.SetMatrix(m);canvas.DrawBitmap(raw,0,0);return result;
    }
    public static void SavePng(SKBitmap image,string path){using var encoded=SKImage.FromBitmap(image);using var data=encoded.Encode(SKEncodedImageFormat.Png,100);using var output=File.Create(path);data.SaveTo(output);}
}
