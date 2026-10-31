using System.Diagnostics;
using LiveCG.Core;
using SkiaSharp;

internal static class DirectChecks
{
    public static async Task Run(string folder,Action<bool,string> check)
    {
        using var r=new MaskRenderer();var mask=new MaskTrack{Start=1,Duration=1,Bounds=new(.25,.25,.5,.5),StartAtInPoint=true,EndMode=MaskEndMode.Release};var rect=SKRectI.Create(48,32);
        using(var before=r.Render([mask],48,32,48,32,rect,.5))check(before.GetPixel(0,0).Alpha==255,"new mask leaves its parent unchanged before its in-point");
        using(var active=r.Render([mask],48,32,48,32,rect,1.5))check(active.GetPixel(0,0).Alpha==0&&active.GetPixel(24,16).Alpha==255,"new mask is applied inside its dragged interval");
        using(var after=r.Render([mask],48,32,48,32,rect,2.1))check(after.GetPixel(0,0).Alpha==255,"new mask releases its parent after its out-point");
        mask.StartAtInPoint=false;using(var old=r.Render([mask],48,32,48,32,rect,.5))check(old.GetPixel(0,0).Alpha==0,"legacy mask preserves its pre-start zero-coverage behavior");
        var longMask=new MaskTrack{Duration=900,StartAtInPoint=true};longMask.Validate();var file=Path.Combine(folder,"mouse-mask.json");DocumentStore.Save(file,new TemplateDocument{Regions=[new(){Masks=[longMask]}]});var copy=DocumentStore.Load<TemplateDocument>(file).Regions[0].Masks[0];check(copy.Duration==900&&copy.StartAtInPoint,"long parent mask and in-point option round-trip without a new schema");
        var png=Path.Combine(folder,"mask-in-out.png");using(var bitmap=new SKBitmap(48,32)){bitmap.Erase(SKColors.Transparent);using(var c=new SKCanvas(bitmap))using(var p=new SKPaint{Color=SKColors.Blue})c.DrawRect(8,8,32,16,p);using var image=SKImage.FromBitmap(bitmap);using var data=image.Encode(SKEncodedImageFormat.Png,100);File.WriteAllBytes(png,data.ToArray());}
        mask.StartAtInPoint=true;mask.Operation=MaskOperation.Subtract;var output=Path.Combine(folder,"mask-in-out.webm");await new WebmExporter(RuntimePaths.Ffmpeg).ExportAsync(new(){SourcePath=png,Width=48,Height=32,Regions=[new(){Role=RegionRole.Group,Bounds=new(0,0,1,1),Background=BackgroundMode.Transparent,Masks=[mask]}]},new(){Fps=15,EndHold=.6},output,true,null,CancellationToken.None);
        foreach(var t in new[]{.5,1.5,2.3})
        {
            var decoded=Path.Combine(folder,$"in-out-{t:0.0}.png");using var p=new Process{StartInfo=new(RuntimePaths.Ffmpeg){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true}};foreach(var arg in new[]{"-v","error","-y","-c:v","libvpx-vp9","-i",output,"-ss",t.ToString(System.Globalization.CultureInfo.InvariantCulture),"-frames:v","1",decoded})p.StartInfo.ArgumentList.Add(arg);p.Start();var error=p.StandardError.ReadToEndAsync();await p.WaitForExitAsync();if(p.ExitCode!=0)throw new IOException(await error);using var image=SKBitmap.Decode(decoded);check(t==1.5?image.GetPixel(24,16).Alpha==0:image.GetPixel(24,16).Alpha==255,$"actual WebM {t:0.0}s matches the mask in/out interval");
        }
    }
}
