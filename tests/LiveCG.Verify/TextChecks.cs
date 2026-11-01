using System.Text.Json;
using LiveCG.Core;
using SkiaSharp;

internal static class TextChecks
{
    public static async Task Run(string folder,Action<bool,string> check)
    {
        var dir=Path.Combine(folder,"text");Directory.CreateDirectory(dir);using var source=new SKBitmap(240,120);source.Erase(SKColors.Black);
        using(var canvas=new SKCanvas(source))using(var p=new SKPaint{Color=SKColors.White,IsAntialias=true,Style=SKPaintStyle.Stroke,StrokeWidth=7}){canvas.DrawCircle(45,40,23,p);canvas.DrawRect(85,15,45,48,p);p.StrokeWidth=1;canvas.DrawLine(160.5f,15,160.5f,90,p);}
        using(var canvas=new SKCanvas(source))using(var p=new SKPaint{Color=SKColors.White,IsAntialias=true})using(var font=new SKFont(SKTypeface.FromFamilyName("Malgun Gothic"),24))canvas.DrawText("ㅇㅁ 획 보존",12,111,font,p);
        var path=Path.Combine(dir,"글자 ㅇㅁ.png");SourceImage.SavePng(source,path);using var matte=TextSeparation.Extract(source,SKColors.Black,SKColors.White);
        check(matte.Mask.Alpha[40*240+45]==0&&matte.Mask.Alpha[35*240+105]==0,"text mode removes closed circular and rectangular background counters");
        var support=true;var bg=true;for(var y=0;y<source.Height;y++)for(var x=0;x<source.Width;x++){var c=source.GetPixel(x,y);var a=matte.Mask.Alpha[y*source.Width+x];if(c.Red>0&&a==0)support=false;if(c==SKColors.Black&&a!=0)bg=false;}
        check(support&&bg,"every original non-background text pixel retains nonzero coverage while exact background is removed");
        check(Enumerable.Range(18,70).All(y=>matte.Mask.Alpha[y*240+160]>0),"one-pixel vertical text strokes survive without erosion");
        using var result=matte.Foreground.Copy();using(var mask=matte.Mask.Bitmap())RegionRaster.Multiply(result,mask);using var recomposed=new SKBitmap(240,120);using(var canvas=new SKCanvas(recomposed)){canvas.Clear(SKColors.Black);canvas.DrawBitmap(result,0,0);}
        var error=0;for(var y=0;y<120;y++)for(var x=0;x<240;x++){var a=source.GetPixel(x,y);var b=recomposed.GetPixel(x,y);error=Math.Max(error,Math.Max(Math.Abs(a.Red-b.Red),Math.Max(Math.Abs(a.Green-b.Green),Math.Abs(a.Blue-b.Blue))));}
        check(error<=1,"separated text recomposes on its original background within one byte without thinning");SourceImage.SavePng(result,Path.Combine(dir,"separated.png"));SourceImage.SavePng(recomposed,Path.Combine(dir,"recomposed.png"));
        using var ramp=new SKBitmap(256,3);ramp.Erase(SKColors.Black);for(var x=1;x<256;x++)ramp.SetPixel(x,1,new SKColor((byte)x,(byte)x,(byte)x));using(var text=TextSeparation.Extract(ramp,SKColors.Black,SKColors.White))check(Enumerable.Range(1,255).All(x=>text.Mask.Alpha[256+x]==x),"all antialias coverage levels including alpha1 remain exact");
        using var alphaSource=new SKBitmap(40,8);alphaSource.Erase(SKColors.Transparent);for(var x=1;x<40;x++)alphaSource.SetPixel(x,3,SKColors.White.WithAlpha((byte)(x*6)));using(var text=TextSeparation.Extract(alphaSource,SKColors.Transparent))check(text.Foreground.Bytes.SequenceEqual(alphaSource.Bytes)&&Enumerable.Range(1,39).All(x=>text.Mask.Alpha[3*40+x]==255),"existing transparent PNG edge alpha is preserved exactly without double masking");
        using var colored=new SKBitmap(40,8);colored.Erase(SKColors.Black);colored.SetPixel(20,3,SKColors.Red);using(var text=TextSeparation.Extract(colored,SKColors.Black,SKColors.White))check(text.Mask.Alpha[3*40+20]==255&&text.Foreground.GetPixel(20,3)==SKColors.Red,"incompatible protected ink colors retain original colored strokes conservatively");
        var old=ObjectExtractor.Extract(source,new(){Mode=ExtractionMode.BackgroundColor,Background=SKColors.Black,Feather=0,IncludeShadow=false,PreserveInterior=true});check(old.Alpha[40*240+45]==255,"general-object mode still protects enclosed interior colors");
        var candidate=TextSeparation.HoleCandidate(source,old,45,40,SKColors.Black);check(candidate.Alpha[40*240+45]==255&&candidate.Alpha[35*240+105]==0,"hole picker selects only the clicked background component");var before=(byte[])old.Alpha.Clone();TextSeparation.ApplyCandidate(old,candidate);check(old.Alpha[40*240+45]==0&&Enumerable.Range(0,before.Length).All(i=>candidate.Alpha[i]>0||old.Alpha[i]==before[i]),"hole removal cannot modify non-candidate stroke pixels");
        var inkCandidate=TextSeparation.HoleCandidate(source,old,160,30,SKColors.Black);check(inkCandidate.Alpha.All(a=>a==0),"clicking an ink stroke never selects it as removable background");
        var region=new EffectRegion{Bounds=new(0,0,1,1),ContentOnly=true,SeparationTarget=SeparationTarget.Text,TextInkColor="#FFFFFF",Background=BackgroundMode.Solid,BackgroundColor="#000000",Softness=new(){Enabled=true,Width=15},Clips=[new(){Duration=1,Options=new(){Kind=EffectKind.Zoom}}]};
        using(var raw=ObjectExtractor.CropObject(source,region))check(raw.GetPixel(45,40).Alpha==0&&raw.GetPixel(160,30).Alpha>0,"automatic content-only renderer uses the shared text matte and protects strokes from selection softness");
        var doc=Path.Combine(dir,"text.json");DocumentStore.Save(doc,new ProjectDocument{Jobs=[new(){SourcePath=path,Width=240,Height=120,Regions=[region]}]});var saved=DocumentStore.Load<ProjectDocument>(doc).Jobs[0].Regions[0];check(saved.SeparationTarget==SeparationTarget.Text&&saved.TextInkColor=="#FFFFFF","text target and protected ink color survive project JSON");File.WriteAllText(doc,File.ReadAllText(doc).Replace("\"SeparationTarget\": \"Text\",",""));check(DocumentStore.Load<ProjectDocument>(doc).Jobs[0].Regions[0].SeparationTarget==SeparationTarget.Object,"legacy documents without a separation target retain general-object semantics");
        var job=new ImageJob{SourcePath=path,Width=240,Height=120,Regions=[region]};var target=Path.Combine(folder,"text-strokes.obs");await new BrowserExporter().ExportAsync(job,new(){Fps=30,EndHold=0},target,true,null,CancellationToken.None);
        // Compare actual 30fps frame times; a half-frame seek is quantized by the browser playback clock.
        Directory.CreateDirectory(Path.Combine(target,"expected"));using(var scene=new EffectScene(source))foreach(var t in new[]{.3,.6}){using var frame=scene.Render(job.Regions,new(){Fps=30},t);SourceImage.SavePng(frame,Path.Combine(target,"expected",t.ToString("0.00",System.Globalization.CultureInfo.InvariantCulture)+".png"));}
        check(File.ReadAllText(Path.Combine(target,"data.js")).Contains("Text"),"browser export retains text configuration and shared raster assets");
        using(var cached=new EffectScene(source))
        {
            region.Softness.Enabled=false;region.SeparationTarget=SeparationTarget.Object;cached.WriteBrowserAssets([region],Path.Combine(dir,"object-cache"),null,CancellationToken.None);using var oldImage=SKBitmap.Decode(Path.Combine(dir,"object-cache",region.Id.ToString("N")+"-image.png"));
            region.SeparationTarget=SeparationTarget.Text;cached.WriteBrowserAssets([region],Path.Combine(dir,"text-cache"),null,CancellationToken.None);using var newImage=SKBitmap.Decode(Path.Combine(dir,"text-cache",region.Id.ToString("N")+"-image.png"));
            check(oldImage.GetPixel(45,40).Alpha>0&&newImage.GetPixel(45,40).Alpha==0,"changing separation target invalidates native/browser layer caches");
            region.TextInkColor="#FF0000";cached.WriteBrowserAssets([region],Path.Combine(dir,"ink-cache"),null,CancellationToken.None);using var inkImage=SKBitmap.Decode(Path.Combine(dir,"ink-cache",region.Id.ToString("N")+"-image.png"));check(!inkImage.Bytes.SequenceEqual(newImage.Bytes),"changing protected ink color invalidates cached edge correction");
        }
    }
}
