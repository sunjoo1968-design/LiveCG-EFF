using LiveCG.Core;
using SkiaSharp;

internal static class SoftEdgeChecks
{
    public static async Task Run(string folder,Action<bool,string> check)
    {
        var intro=new EffectClip{Duration=1,Options=new(){Kind=EffectKind.ZoomIn}};
        var loop=new EffectClip{Start=1,Duration=2,Options=new(){Kind=EffectKind.Zoom}};
        var exit=new EffectClip{Start=3,Duration=1,Section=ClipSection.Exit};
        var region=new EffectRegion{Clips=[intro,loop,exit]};
        var job=new ImageJob{Width=160,Height=120,SourcePath="fixture.png",Regions=[region],Playback=new(){Enabled=true,ByEffects=true,ExitEnabled=false}};
        var plan=PlaybackPlan.Create(job,new(){Fps=30,EndHold=5});
        check(plan.IntroFrames==30&&plan.LoopFrames==60&&plan.ExitFrames==0&&!plan.ExitEnabled,"effect sections derive boundaries and optional exit excludes end hold");
        var disabledExit=SectionRules.RenderRegions(job);check(!disabledExit[0].Clips.Single(c=>c.Id==exit.Id).Enabled&&exit.Enabled,"disabled exit is filtered non-destructively from preview and both output formats");
        loop.Duration=3;plan=PlaybackPlan.Create(job,new(){Fps=30});check(plan.LoopEndFrame==120,"changing a clip length updates effect-derived section boundary");
        var clock=new PlaybackClock(plan,false);clock.RequestExit(8);check(!clock.ExitRequested&&!clock.Sample(8).Finished,"disabled exit cannot stop emphasis playback");
        job.Playback.ExitEnabled=true;exit.Start=4;plan=PlaybackPlan.Create(job,new(){Fps=30,EndHold=0});check(plan.ExitFrames==30&&plan.ExitEnabled,"optional exit includes its assigned effect");
        var legacy=new ImageJob{Regions=[new(){Clips=[new(){Duration=4}]}],Playback=new(){Enabled=true,IntroEnd=1,LoopEnd=3,ExitDuration=1}};
        check(PlaybackPlan.Create(legacy,new(){EndHold=0}).ExitFrames==30,"missing new JSON fields preserve legacy seconds and exit policy");
        loop.Section=ClipSection.Intro;check(SectionRules.Effective(loop)==ClipSection.Intro&&loop.Copy().Section==ClipSection.Intro,"explicit section overrides effect type and survives clip copying");
        loop.Section=ClipSection.Loop;loop.Start=.5;loop.Duration=3;check(SectionRules.Crossing(job,new()).Count>0,"cross-boundary effects are reported");
        check(SectionRules.SplitBoundaries(job,new())>0&&region.Clips.Any(c=>c.Section==ClipSection.Intro&&c.Options.Kind==EffectKind.Zoom),"boundary split preserves source motion while assigning phase pieces");
        SectionRules.Arrange(job,30);var p=SectionRules.Resolve(job,new());check(region.Clips.Where(c=>SectionRules.Effective(c)==ClipSection.Loop).All(c=>c.Start>=p.IntroEnd-1e-6),"explicit arrange puts loop effects after entrance");
        foreach(var shape in Enum.GetValues<RegionShape>())foreach(var direction in Enum.GetValues<SoftDirection>())
        {
            var r=new EffectRegion{Bounds=new(.25,.25,.5,.5),Shape=shape,Points=shape is RegionShape.Freehand or RegionShape.Polygon?[new(.05,.05),new(.95,.05),new(.95,.95),new(.05,.95)]:[],Softness=new(){Enabled=true,Width=10,Direction=direction}};
            var rect=RegionRaster.Rect(r,160,120);using var mask=RegionRaster.Shape(r,rect,160,120);
            var partial=0;for(var y=0;y<mask.Height;y++)for(var x=0;x<mask.Width;x++)if(mask.GetPixel(x,y).Alpha is >0 and <255)partial++;
            check(partial>0&&mask.GetPixel(mask.Width/2,mask.Height/2).Alpha==255,$"{shape}/{direction} softens boundary while retaining opaque center");
            check(direction==SoftDirection.Inside?rect.Left==40:rect.Left<40,$"{shape}/{direction} source padding matches direction");
        }
        var separate=new EffectRegion{Bounds=new(.25,.25,.5,.5),Softness=new(){Enabled=true,SeparateSides=true,Top=0,Right=0,Bottom=12,Left=0}};
        using(var mask=RegionRaster.Shape(separate,RegionRaster.Rect(separate,160,120),160,120))check(mask.GetPixel(40,0).Alpha==255&&mask.GetPixel(40,59).Alpha<100,"side widths keep top hard and soften only bottom");
        var clone=separate.Copy();clone.Softness.Width=99;check(separate.Softness.Width!=99,"softness copy is independent for undo and templates");
        var path=Path.Combine(folder,"soft-source.png");using var source=new SKBitmap(160,120);
        using(var c=new SKCanvas(source)){c.Clear(SKColors.White);using var paint=new SKPaint{Color=SKColors.Red};c.DrawRect(54,44,52,32,paint);paint.Color=SKColors.White;c.DrawRect(72,52,16,16,paint);}
        PlaybackChecks.Save(source,path);
        var content=new EffectRegion{Bounds=new(.25,.25,.5,.5),ContentOnly=true,Background=BackgroundMode.Solid,BackgroundColor="#FFFFFF",Clips=[new(){Duration=1,Options=new(){Kind=EffectKind.ZoomIn}}]};
        var crop=RegionRaster.Rect(content,160,120);using(var raw=RegionRaster.Raw(source,content,crop,SKColors.White))
        {check(raw.GetPixel(2,2).Alpha==0&&raw.GetPixel(20,20).Red==255&&raw.GetPixel(20,20).Alpha==255,"content-only removes connected background without redrawing foreground");check(raw.GetPixel(40,30)==SKColors.White,"content-only protects enclosed white lettering matching background");}
        foreach(var (name,r) in new[]{("soft-inside",content with{ContentOnly=false,Softness=new(){Enabled=true,Width=15}}),("soft-outside",content with{Softness=new(){Enabled=true,Width=12,Direction=SoftDirection.Outside}}),("soft-both",content with{ContentOnly=false,Softness=new(){Enabled=true,Width=14,Direction=SoftDirection.Both}}),("content-only",content)})
        {
            var fixture=new ImageJob{SourcePath=path,Width=160,Height=120,Regions=[r],Playback=new(){Enabled=true,ByEffects=true,ExitEnabled=false}};var target=Path.Combine(folder,name+".obs");await new BrowserExporter().ExportAsync(fixture,new(){EndHold=0},target,true,null,CancellationToken.None);
            Directory.CreateDirectory(Path.Combine(target,"expected"));using var scene=new EffectScene(source);
            foreach(var t in new[]{0d,.3,.7,1d}){using var image=scene.Render(fixture.Regions,new(),t);PlaybackChecks.Save(image,Path.Combine(target,"expected",t.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture)+".png"));}
            check(File.Exists(Path.Combine(target,"assets",r.Id.ToString("N")+"-shape.png")),name+" shares native raster assets with browser output");
        }
        job=new(){SourcePath=path,Width=160,Height=120,Regions=[content with{Softness=new(){Enabled=true,Width=7,Direction=SoftDirection.Both}}],Playback=new(){Enabled=true,ByEffects=true,ExitEnabled=false}};
        var save=Path.Combine(folder,"soft-roundtrip.json");DocumentStore.Save(save,new ProjectDocument{Jobs=[job]});var loaded=DocumentStore.Load<ProjectDocument>(save);
        check(loaded.Jobs[0].Regions[0].Softness.Direction==SoftDirection.Both&&loaded.Jobs[0].Regions[0].ContentOnly&&loaded.Jobs[0].Playback.ExitEnabled==false,"new region and section settings survive validated JSON round trip");
        var parent=new EffectRegion{Role=RegionRole.Group,Bounds=new(.1,.1,.8,.8)};
        var child=content.Copy();child.ParentId=parent.Id;child.Role=RegionRole.EffectArea;child.Softness=new(){Enabled=true,Width=9,Direction=SoftDirection.Both};child.Clips=[new(){Duration=1,Options=new(){Kind=EffectKind.ZoomIn}}];
        await Fixture("soft-group",[parent,child],source,path);
        var numericPath=Path.Combine(folder,"soft-numeric.png");using var numeric=new SKBitmap(160,120);
        using(var canvas=new SKCanvas(numeric)){canvas.Clear(SKColors.White);using var font=new SKFont(SKTypeface.FromFamilyName("Arial",SKFontStyle.Bold),32);using var paint=new SKPaint{Color=SKColors.Black,IsAntialias=true};canvas.DrawText("123",50,72,font,paint);}PlaybackChecks.Save(numeric,numericPath);
        var number=content.Copy();number.Softness=new(){Enabled=true,Width=5};number.Clips=[new(){Duration=.8,Options=new(){Kind=EffectKind.Bounce}},new(){Start=.8,Duration=.8,Options=new(){Kind=EffectKind.DigitRoll}}];
        await Fixture("soft-numeric",[number],numeric,numericPath);
        var noExit=new ImageJob{SourcePath=path,Width=160,Height=120,Regions=[content.Copy()],Playback=new(){Enabled=true,ByEffects=true,ExitEnabled=false,WebmRepeats=2}};
        noExit.Regions[0].Clips=[new(){Duration=.5,Options=new(){Kind=EffectKind.FadeIn}},new(){Start=.5,Duration=1,Options=new(){Kind=EffectKind.Zoom}}];
        var webm=Path.Combine(folder,"no-exit.webm");await new WebmExporter(RuntimePaths.Ffmpeg).ExportAsync(noExit,new(){Fps=20,EndHold=5},webm,true,null,CancellationToken.None);
        var start=new System.Diagnostics.ProcessStartInfo(RuntimePaths.Ffprobe){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true};foreach(var arg in new[]{"-v","error","-count_frames","-show_streams","-of","json",webm})start.ArgumentList.Add(arg);using var process=System.Diagnostics.Process.Start(start)!;var text=await process.StandardOutput.ReadToEndAsync();await process.WaitForExitAsync();using var json=System.Text.Json.JsonDocument.Parse(text);var stream=json.RootElement.GetProperty("streams")[0];
        check(stream.GetProperty("nb_read_frames").GetString()=="50"&&stream.GetProperty("width").GetInt32()==160&&stream.GetProperty("height").GetInt32()==120,"real no-exit WebM contains entrance plus two loops at unchanged PNG size");
        async Task Fixture(string name,List<EffectRegion> regions,SKBitmap original,string originalPath)
        {
            var fixture=new ImageJob{SourcePath=originalPath,Width=160,Height=120,Regions=regions};var target=Path.Combine(folder,name+".obs");await new BrowserExporter().ExportAsync(fixture,new(){EndHold=0},target,true,null,CancellationToken.None);Directory.CreateDirectory(Path.Combine(target,"expected"));using var scene=new EffectScene(original);
            foreach(var t in new[]{.25,.6,1d,1.5}){using var frame=scene.Render(regions,new(),t);PlaybackChecks.Save(frame,Path.Combine(target,"expected",t.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture)+".png"));}
            check(File.Exists(Path.Combine(target,"player.js")),name+" handles soft child composition or original-pixel numeric effects");
        }
    }
}
