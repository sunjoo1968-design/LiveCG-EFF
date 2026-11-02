using LiveCG.Core;
using System.IO;
using SkiaSharp;

namespace LiveCG.Testing;
public static class TestFixtures
{
    public static string Create(string folder)
    {
        Directory.CreateDirectory(folder);var path=Path.Combine(folder,"타임라인 예제.png");
        using var bitmap=new SKBitmap(960,540);using var canvas=new SKCanvas(bitmap);canvas.Clear(SKColors.Transparent);
        using var paint=new SKPaint{Color=new SKColor(21,37,55),IsAntialias=true};canvas.DrawRoundRect(SKRect.Create(20,20,920,500),18,18,paint);
        using var font=new SKFont(SKTypeface.FromFamilyName("Malgun Gothic",SKFontStyle.Bold),38);
        paint.Color=SKColors.White;canvas.DrawText("LIVE COMMERCE",50,78,font,paint);
        paint.Color=new(93,226,177);canvas.DrawText("TIMELINE EFFECTS",50,123,font,paint);
        for(var i=0;i<3;i++)
        {
            var x=50+i*295;paint.Color=new(235,241,246);canvas.DrawRoundRect(SKRect.Create(x,165,265,180),14,14,paint);
            paint.Color=i==0?new SKColor(130,90,215):i==1?new SKColor(55,148,185):new SKColor(230,133,62);
            canvas.DrawRoundRect(SKRect.Create(x+75,190,115,100),12,12,paint);
            paint.Color=new(25,40,58);font.Size=25;canvas.DrawText("PRODUCT "+(i+1),x+48,324,font,paint);
        }
        paint.Color=new(112,26,46);canvas.DrawRoundRect(SKRect.Create(50,390,855,95),12,12,paint);
        font.Size=30;paint.Color=SKColors.White;canvas.DrawText("SPECIAL PRICE",76,446,font,paint);
        font.Size=64;canvas.DrawText("87,070",450,463,font,paint);
        using var image=SKImage.FromBitmap(bitmap);using var data=image.Encode(SKEncodedImageFormat.Png,100);File.WriteAllBytes(path,data.ToArray());return path;
    }
    public static ImageJob Demo(string path)
    {
        var job=new ImageJob{SourcePath=path,Width=960,Height=540};
        for(var i=0;i<3;i++)job.Regions.Add(new(){Name="상품 "+(i+1),Bounds=NormalizedRect.FromPixels(SKRect.Create(50+i*295,165,265,180),960,540),Background=BackgroundMode.Solid,BackgroundColor="#152537",
            Clips=[new(){Start=i*.45,Duration=.8,Options=new(){Kind=EffectKind.ZoomIn,Strength=StrengthLevel.Strong}},new(){Start=1.8+i*.2,Duration=1.3,Options=new(){Kind=i==0?EffectKind.Ripple:i==1?EffectKind.Shake:EffectKind.OrbitBurst}}]});
        job.Regions.Add(new(){Name="가격",Bounds=NormalizedRect.FromPixels(SKRect.Create(438,405,245,73),960,540),Clips=[new(){Duration=1.5,Options=new(){Kind=EffectKind.DigitRoll}},new(){Start=1.6,Duration=1.4,Options=new(){Kind=EffectKind.Bounce,Strength=StrengthLevel.Strong}},new(){Start=3.1,Duration=1,Options=new(){Kind=EffectKind.Sweep,Strength=StrengthLevel.Strong}}]});
        return job;
    }
    public static string NumericRedraw(string folder)
    {
        Directory.CreateDirectory(folder);var path=Path.Combine(folder,"synthetic-numeric-redraw.png");using var image=new SKBitmap(1080,1920);image.Erase(new(125,20,32));
        using var c=new SKCanvas(image);using var paint=new SKPaint{Color=new(145,26,39),IsAntialias=true};for(var y=695;y<790;y+=5)for(var x=630;x<985;x+=5)if((x/5+y/5)%2==0)c.DrawRect(x,y,5,5,paint);
        using var font=new SKFont(SKTypeface.FromFamilyName("Arial",SKFontStyle.Bold),50);paint.Color=SKColors.White;var chars="133,470";var positions=new[]{665,691,721,747,762,791,820};for(var i=0;i<chars.Length;i++){font.ScaleX=i==0?.65f:1;c.DrawText(chars[i].ToString(),positions[i],758,font,paint);}font.ScaleX=1;font.Size=45;c.DrawText("원",854,758,font,paint);
        paint.Color=new(224,177,111);c.DrawRoundRect(SKRect.Create(900,719,78,46),15,15,paint);paint.Color=new(65,14,22);font.Size=32;c.DrawText("21%",909,754,font,paint);SourceImage.SavePng(image,path);return path;
    }
}
