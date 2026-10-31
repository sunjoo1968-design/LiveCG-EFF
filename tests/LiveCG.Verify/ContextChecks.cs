using LiveCG.Core;
using SkiaSharp;

internal static class ContextChecks
{
    public static async Task Run(string folder,Action<bool,string> check)
    {
        using var source=new SKBitmap(160,120);using(var c=new SKCanvas(source)){c.Clear(SKColors.White);using var p=new SKPaint{Color=SKColors.Red};c.DrawRect(60,44,40,32,p);}
        var region=new EffectRegion{Bounds=new(.25,.25,.5,.5),Background=BackgroundMode.Solid,BackgroundColor="#FFFFFF",ContentOnly=true,ContentTolerance=24,Softness=new(){Enabled=true,Width=10,Direction=SoftDirection.Both},Clips=[new(){Duration=1,Options=new(){Kind=EffectKind.FadeIn}}]};
        var original=ObjectExtractor.PixelRect(region.Bounds,160,120);var padded=RegionRaster.Rect(region,160,120);
        using(var crop=new SKBitmap()){source.ExtractSubset(crop,padded);var quick=ObjectExtractor.Extract(crop,RegionSeparation.Options(region,RegionSeparation.Background(source,region)));var expected=quick.Crop(new(original.Left-padded.Left,original.Top-padded.Top,original.Right-padded.Left,original.Bottom-padded.Top));check(RegionSeparation.InitialMask(source,region).Alpha.SequenceEqual(expected.Alpha),"precision initial mask is the exact quick mask with outer soft padding removed");}
        var bytes=Enumerable.Repeat((byte)128,original.Width*original.Height).ToArray();region.Cutout=new MaskBuffer(original.Width,original.Height,bytes).Encode();
        using(var raw=RegionRaster.Raw(source,region,padded,SKColors.White))check(raw.GetPixel(original.Left-padded.Left+2,original.Top-padded.Top+2).Alpha==255,"manual mask preserves white pixels without a second automatic background extraction");
        var noSoft=region.Copy(false);noSoft.Softness.Enabled=false;var rect=RegionRaster.Rect(noSoft,160,120);
        using(var shape=RegionRaster.Shape(noSoft,rect,160,120))check(shape.GetPixel(2,2).Alpha==128,"manual partial coverage is applied exactly once");
        using(var sample=ObjectExtractor.CropObject(source,noSoft))check(sample.GetPixel(2,2).Alpha==128,"motion-studio sample respects the same single-pass manual coverage");
        var dot=new MaskBuffer(original.Width,original.Height,new byte[original.Width*original.Height]);dot.Brush(40,30,2,true);var expanded=dot.Morph(2,true);var edge=noSoft.Copy(false);edge.Cutout=dot.Encode() with{ErasePng=expanded.Encode().MaskPng};
        using(var shape=RegionRaster.Shape(edge,rect,160,120))using(var erase=RegionRaster.Shape(edge,rect,160,120,false))check(shape.GetPixel(44,30).Alpha==0&&erase.GetPixel(44,30).Alpha==255,"precision foreground and source-erasure coverage stay separate to prevent contour ghosts");
        var saved=region.Cutout.MaskPng;region.ContentOnly=false;region.CutoutEnabled=false;
        using(var shape=RegionRaster.Shape(region,padded,160,120))check(shape.GetPixel(shape.Width/2,shape.Height/2).Alpha==255&&region.Cutout.MaskPng==saved,"whole-region mode bypasses precision mask non-destructively");
        region.CutoutEnabled=true;region.ContentOnly=true;check(RegionSeparation.InitialMask(source,region).Alpha.SequenceEqual(bytes),"re-enabling manual separation reuses saved alpha without recalculating it");
        var path=Path.Combine(folder,"context-source.png");PlaybackChecks.Save(source,path);
        region.CutoutEnabled=false;region.ContentOnly=false;var json=Path.Combine(folder,"context-roundtrip.json");DocumentStore.Save(json,new ProjectDocument{Jobs=[new(){SourcePath=path,Width=160,Height=120,Regions=[region]}]});var loaded=DocumentStore.Load<ProjectDocument>(json).Jobs[0].Regions[0];check(!loaded.CutoutEnabled&&loaded.Cutout?.MaskPng==saved,"inactive manual mask and enable state survive project save/load");
        var legacy=System.Text.Json.JsonSerializer.Deserialize<EffectRegion>("{\"Name\":\"legacy\"}")!;check(legacy.CutoutEnabled,"missing enabled flag retains legacy cutout semantics");
        region.CutoutEnabled=true;region.ContentOnly=true;var job=new ImageJob{SourcePath=path,Width=160,Height=120,Regions=[region]};var target=Path.Combine(folder,"manual-separation.obs");await new BrowserExporter().ExportAsync(job,new(){EndHold=0},target,true,null,CancellationToken.None);
        Directory.CreateDirectory(Path.Combine(target,"expected"));using var scene=new EffectScene(source);foreach(var t in new[]{.25,.6,1d}){using var frame=scene.Render([region],new(),t);PlaybackChecks.Save(frame,Path.Combine(target,"expected",t.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture)+".png"));}
        check(File.Exists(Path.Combine(target,"data.js")),"manual separation is exported through the shared native/browser raster path");
        var parent=new EffectRegion{Role=RegionRole.Group,Bounds=new(.1,.1,.8,.8)};var child=region.Copy();child.ParentId=parent.Id;child.Role=RegionRole.EffectArea;child.CutoutEnabled=false;child.ContentOnly=false;child.Softness.Enabled=false;child.Clips=[new(){Duration=1,Options=new(){Kind=EffectKind.ZoomIn}}];job.Regions=[parent,child];target=Path.Combine(folder,"inactive-manual-group.obs");
        await new BrowserExporter().ExportAsync(job,new(){EndHold=0},target,true,null,CancellationToken.None);Directory.CreateDirectory(Path.Combine(target,"expected"));using var grouped=new EffectScene(source);
        foreach(var t in new[]{.25,.6,1d}){using var frame=grouped.Render(job.Regions,new(),t);PlaybackChecks.Save(frame,Path.Combine(target,"expected",t.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture)+".png"));}
        check(File.ReadAllText(Path.Combine(target,"data.js")).Contains("\"customRaster\": true"),"disabled saved masks retain correct browser child composition mode");
    }
}
