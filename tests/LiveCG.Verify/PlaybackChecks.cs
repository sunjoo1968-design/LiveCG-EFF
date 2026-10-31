using System.Diagnostics;
using System.Text.Json;
using LiveCG.Core;
using SkiaSharp;

internal static class PlaybackChecks
{
    public static async Task Run(string folder,Action<bool,string> check)
    {
        var png=Path.Combine(folder,"구간 50% & 테스트.png");
        using(var b=new SKBitmap(160,100)){b.Erase(SKColors.Transparent);using(var c=new SKCanvas(b))using(var p=new SKPaint{Color=SKColors.Red,IsAntialias=true})c.DrawRoundRect(SKRect.Create(40,30,80,40),8,8,p);Save(b,png);}
        var job=new ImageJob{SourcePath=png,Width=160,Height=100,Playback=new(){Enabled=true,IntroEnd=.5,LoopEnd=1.5,ExitDuration=.5,WebmRepeats=3},Regions=[new(){Bounds=new(.25,.3,.5,.4),Background=BackgroundMode.Transparent,Clips=[new(){Duration=.5,Options=new(){Kind=EffectKind.FadeIn}},new(){Start=.5,Duration=1,Options=new(){Kind=EffectKind.Zoom}}]}]};
        var video=new VideoOptions{Fps=10,EndHold=0,Crf=10};var plan=PlaybackPlan.Create(job,video);
        check(plan.IntroFrames==5&&plan.LoopFrames==10&&plan.ExitFrames==5&&plan.WebmFrameCount==40,"sections become exact half-open frame ranges and three finite cycles");
        check(new[]{0,4,5,14,15,24,25,34,35,39}.Select(plan.WebmSourceFrame).SequenceEqual(new[]{0,4,5,14,5,14,5,14,15,19}),"WebM maps each loop without duplicating its boundary frame");
        check(TimelineRules.Resolve(job,video).FrameCount==20&&plan.WebmDuration==4,"editing duration stays distinct from finite repeated WebM duration");
        check(Math.Abs(plan.LoopTime(99.75)-.75)<1e-8,"broadcast loop stays within the emphasis section after many cycles");
        check(plan.Opacity(1.4)==1&&plan.Opacity(1.5)==1&&plan.Opacity(1.9)<1e-8,"fade starts fully visible and reaches zero on the last exit frame");
        var clock=new PlaybackClock(plan,true);clock.RequestExit(.7);check(!clock.Sample(1.4).Finished&&clock.Sample(1.5).Time==1.5&&clock.Sample(2.1).Finished,"exit waits until current loop finishes then plays the tail once");
        var early=new PlaybackClock(plan,true);early.RequestExit(.1);check(early.Sample(.4).Time==.4&&early.Sample(1.5).Time==1.5,"early exit request preserves entrance and first loop");
        var immediate=new PlaybackClock(plan,false);immediate.RequestExit(.7);check(immediate.Sample(.7).Time==1.5&&immediate.Sample(1.3).Finished,"optional immediate exit jumps to exit in-point");
        void Reject(Action action,string name){try{action();check(false,name);}catch(InvalidDataException){check(true,name);}}
        Reject(()=>new PlaybackSections{IntroEnd=2,LoopEnd=1}.Validate(),"reversed section boundaries rejected");Reject(()=>new PlaybackSections{IntroEnd=double.NaN}.Validate(),"nonfinite section boundary rejected");Reject(()=>new PlaybackSections{WebmRepeats=101}.Validate(),"unbounded WebM repetitions rejected");
        var enormous=job.Snapshot();enormous.Playback.LoopEnd=100;enormous.Playback.WebmRepeats=100;Reject(()=>PlaybackPlan.Create(enormous,video).ValidateWebm(),"oversized finite WebM rejected before rendering");
        var legacy=job.Snapshot();legacy.Playback.Enabled=false;var oldPlan=PlaybackPlan.Create(legacy,video);check(oldPlan.WebmFrameCount==15&&oldPlan.Opacity(100)==1,"disabled sections preserve the old timeline and no fade");
        var round=Path.Combine(folder,"sections.json");DocumentStore.Save(round,new ProjectDocument{Jobs=[job],Video=video with{Output=OutputKind.Both}});var loaded=DocumentStore.Load<ProjectDocument>(round);
        check(loaded.Jobs[0].Playback==job.Playback&&loaded.Video.Output==OutputKind.Both,"sections and both-output preference round-trip project JSON");
        var template=Path.Combine(folder,"sections-template.json");DocumentStore.Save(template,new TemplateDocument{Regions=job.Regions,Playback=job.Playback});check(DocumentStore.Load<TemplateDocument>(template).Playback==job.Playback,"template retains playback sections");
        var defaults=Path.Combine(folder,"sections-defaults.json");DocumentStore.Save(defaults,new AppDefaults{Playback=job.Playback});check(DocumentStore.Load<AppDefaults>(defaults).Playback==job.Playback,"defaults retain section policy");
        var snapshot=job.Snapshot();snapshot.Playback.IntroEnd=.2;check(job.Playback.IntroEnd==.5,"export snapshot does not alias playback settings");
        var output=Path.Combine(folder,"section-output.webm");await new WebmExporter(RuntimePaths.Ffmpeg).ExportAsync(job,video,output,true,null,CancellationToken.None);
        var probe=await Run(RuntimePaths.Ffprobe,"-v","error","-count_frames","-show_streams","-of","json",output);using(var doc=JsonDocument.Parse(probe)){var stream=doc.RootElement.GetProperty("streams")[0];check(stream.GetProperty("nb_read_frames").GetString()=="40"&&stream.GetProperty("width").GetInt32()==160,"actual WebM has the repeated frame count and original size");}
        foreach(var (at,transparent) in new[]{(0d,true),(.5,false),(1.5,false),(2.5,false),(3.9,true)})
        {
            var frame=Path.Combine(folder,$"section-{at:0.0}.png");await Run(RuntimePaths.Ffmpeg,"-v","error","-y","-c:v","libvpx-vp9","-i",output,"-ss",at.ToString(System.Globalization.CultureInfo.InvariantCulture),"-frames:v","1",frame);using var b=SKBitmap.Decode(frame);check(transparent?b.GetPixel(80,50).Alpha<5:b.GetPixel(80,50).Alpha>245,$"WebM {at:0.0}s preserves single entrance, repeated emphasis and transparent exit");
        }
        var browser=Path.Combine(folder,"한글 로컬 & 패키지.obs");var exporter=new BrowserExporter();await exporter.ExportAsync(job,video,browser,true,null,CancellationToken.None);
        check(BrowserExporter.IsPackage(browser)&&File.Exists(Path.Combine(browser,"original.png"))&&File.Exists(Path.Combine(browser,"preview.html")),"local browser package contains original PNG, source and separate controls");
        check(!Directory.EnumerateFiles(browser,"*.webm",SearchOption.AllDirectories).Any()&&File.ReadAllText(Path.Combine(browser,"data.js")).Contains("sourceName"),"browser package is PNG/Canvas rather than wrapped video");
        var sentinel=Path.Combine(browser,"preserve.txt");File.WriteAllText(sentinel,"before");
        try{await exporter.ExportAsync(job,video,browser,false,null,CancellationToken.None);check(false,"browser refuses overwrite without authorization");}catch(IOException){check(File.ReadAllText(sentinel)=="before","browser refuses overwrite and preserves previous output");}
        using(var cts=new CancellationTokenSource())
        {
            var signals=new ImmediateProgress(p=>cts.Cancel());try{await exporter.ExportAsync(job,video,browser,true,signals,cts.Token);check(false,"browser cancellation throws");}catch(OperationCanceledException){check(File.Exists(sentinel),"mid-export browser cancellation preserves existing package");}
        }
        var other=Path.Combine(folder,"unrelated.obs");Directory.CreateDirectory(other);File.WriteAllText(Path.Combine(other,"user.txt"),"original");try{await exporter.ExportAsync(job,video,other,true,null,CancellationToken.None);check(false,"unrelated directory protected");}catch(IOException){check(File.Exists(Path.Combine(other,"user.txt")),"unrelated directory is never replaced by browser output");}
        check(!Directory.EnumerateDirectories(folder,"*.partial").Any(),"browser cancellation removes temporary files");
        await exporter.ExportAsync(job,video,browser,true,null,CancellationToken.None);check(!File.Exists(sentinel)&&BrowserExporter.IsPackage(browser),"completed package atomically replaces an earlier LiveCG package");
        var expected=Path.Combine(browser,"expected");Directory.CreateDirectory(expected);using(var b=SKBitmap.Decode(png))using(var scene=new EffectScene(b))foreach(var t in new[]{0d,.2,.5,1d,1.9}){using var frame=scene.Render(job.Regions,video,t);PlaybackPlan.ApplyOpacity(frame,plan.Opacity(t));Save(frame,Path.Combine(expected,$"{t:0.00}.png"));}
        await CreateBrowserFixtures(folder,check);
    }
    private static async Task CreateBrowserFixtures(string folder,Action<bool,string> check)
    {
        var path=Path.Combine(folder,"all-effects.png");using var bitmap=new SKBitmap(660,900);bitmap.Erase(SKColors.Transparent);var regions=new List<EffectRegion>();
        using(var c=new SKCanvas(bitmap))using(var p=new SKPaint{Color=SKColors.Red,IsAntialias=true})using(var font=new SKFont(SKTypeface.FromFamilyName("Arial",SKFontStyle.Bold),35))
        {
            var i=0;foreach(var kind in Enum.GetValues<EffectKind>())
            {
                var x=50+i%3*210;var y=35+i/3*125;p.Color=new SKColor(112,26,46);c.DrawRoundRect(SKRect.Create(x,y,140,70),12,12,p);p.Color=SKColors.White;c.DrawText("87,070",x+8,y+47,font,p);
                regions.Add(new(){Name=kind.ToString(),Bounds=NormalizedRect.FromPixels(SKRect.Create(x,y,140,70),660,900),Background=kind is EffectKind.DigitRoll or EffectKind.Bounce?BackgroundMode.Solid:BackgroundMode.Transparent,BackgroundColor="#701A2E",Shape=RegionShape.RoundedRectangle,Clips=[new(){Duration=1.5,Options=new(){Kind=kind},Motion=kind==EffectKind.UserMotion?MotionDefinition.Example():null}]});i++;
            }
        }
        Save(bitmap,path);var job=new ImageJob{SourcePath=path,Width=660,Height=900,Regions=regions};var video=new VideoOptions{Fps=30,EndHold=.3};var target=Path.Combine(folder,"all-effects.obs");await new BrowserExporter().ExportAsync(job,video,target,true,null,CancellationToken.None);
        Directory.CreateDirectory(Path.Combine(target,"expected"));using(var scene=new EffectScene(bitmap))foreach(var t in new[]{0d,.6,1.4,1.6}){using var frame=scene.Render(regions,video,t);Save(frame,Path.Combine(target,"expected",$"{t:0.00}.png"));}
        var group=new EffectRegion{Role=RegionRole.Group,Bounds=new(.02,.02,.96,.96),Background=BackgroundMode.Transparent,Clips=[new(){Duration=.5,Options=new(){Kind=EffectKind.RiseIn}},new(){Start=.5,Duration=1.5,Options=new(){Kind=EffectKind.Float}}]};
        var child=new EffectRegion{Role=RegionRole.EffectArea,ParentId=group.Id,Bounds=regions[0].Bounds,Shape=RegionShape.Ellipse,Background=BackgroundMode.Transparent,Clips=[new(){Duration=2,Options=new(){Kind=EffectKind.Zoom}},new(){Duration=2,Options=new(){Kind=EffectKind.Ripple}}],Masks=[new(){Bounds=regions[0].Bounds,Shape=RegionShape.Ellipse,Feather=2,StartAtInPoint=true,Duration=1.5,EndMode=MaskEndMode.Release}]};
        var motion=new MotionDefinition{LengthFrames=60};motion.SetKey(MotionProperty.X,0,0);motion.SetKey(MotionProperty.X,60,.2);group.Masks=[new(){Bounds=new(.45,.05,.35,.3),Shape=RegionShape.Ellipse,Operation=MaskOperation.Subtract,Start=.2,Duration=1.5,StartAtInPoint=true,EndMode=MaskEndMode.Release,Feather=1,Expand=1,Motion=motion},new(){Bounds=new(.05,.55,.25,.15),Shape=RegionShape.Ellipse,Operation=MaskOperation.Intersect,Invert=true,Opacity=.85,Start=.8,Duration=.6,StartAtInPoint=true,EndMode=MaskEndMode.Release,Feather=2,Expand=-1}];
        var numericChild=new EffectRegion{Role=RegionRole.EffectArea,ParentId=group.Id,Bounds=regions[16].Bounds,Background=BackgroundMode.Solid,BackgroundColor="#701A2E",Clips=[new(){Duration=2,Options=new(){Kind=EffectKind.DigitRoll}},new(){Duration=2,Options=new(){Kind=EffectKind.Zoom}},new(){Duration=2,Options=new(){Kind=EffectKind.Sweep}}]};
        job.Regions=[group,child,numericChild];target=Path.Combine(folder,"group-masks.obs");await new BrowserExporter().ExportAsync(job,video,target,true,null,CancellationToken.None);Directory.CreateDirectory(Path.Combine(target,"expected"));using(var scene=new EffectScene(bitmap))foreach(var t in new[]{0d,.6,1.2,2.1}){using var frame=scene.Render(job.Regions,video,t);Save(frame,Path.Combine(target,"expected",$"{t:0.00}.png"));}
        check(File.Exists(Path.Combine(target,"assets",group.Id.ToString("N")+"-"+group.Masks[0].Id.ToString("N")+"-mask.png")),"group package includes native feathered mask rasters and local-effect assets");
        var shapes=new[]{RegionShape.Ellipse,RegionShape.Freehand,RegionShape.Polygon};var pathRegions=new List<EffectRegion>();
        for(var i=0;i<3;i++){var r=regions[i].Copy();r.Shape=shapes[i];if(i>0)r.Points=[new(0,0),new(1,1),new(0,1),new(1,0)];r.Clips=[new(){Duration=1.5,Loop=true,LoopPeriod=.5,Options=new(){Kind=i==0?EffectKind.Zoom:EffectKind.Sweep}}];pathRegions.Add(r);}
        using(var cut=new SKBitmap(140,70)){cut.Erase(SKColors.Transparent);using(var c=new SKCanvas(cut))using(var p=new SKPaint{Color=SKColors.White,IsAntialias=true})c.DrawCircle(70,35,32,p);using var image=SKImage.FromBitmap(cut);using var data=image.Encode(SKEncodedImageFormat.Png,100);pathRegions[0].Cutout=new(){Width=140,Height=70,MaskPng=Convert.ToBase64String(data.ToArray())};}
        job.Regions=pathRegions;target=Path.Combine(folder,"paths-cutout.obs");await new BrowserExporter().ExportAsync(job,video,target,true,null,CancellationToken.None);Directory.CreateDirectory(Path.Combine(target,"expected"));using(var scene=new EffectScene(bitmap))foreach(var t in new[]{.1,.6,1.6}){using var frame=scene.Render(job.Regions,video,t);Save(frame,Path.Combine(target,"expected",$"{t:0.00}.png"));}
        check(Directory.GetFiles(Path.Combine(target,"assets"),"*-shape.png").Length==3,"browser supports extracted alpha, ellipses and even-odd freehand/pen paths");
    }
    internal static void Save(SKBitmap bitmap,string path){using var image=SKImage.FromBitmap(bitmap);using var data=image.Encode(SKEncodedImageFormat.Png,100);File.WriteAllBytes(path,data.ToArray());}
    private static async Task<string> Run(string exe,params string[] args)
    {using var p=new Process{StartInfo=new(exe){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true}};foreach(var arg in args)p.StartInfo.ArgumentList.Add(arg);p.Start();var text=p.StandardOutput.ReadToEndAsync();var error=p.StandardError.ReadToEndAsync();await p.WaitForExitAsync();if(p.ExitCode!=0)throw new IOException(await error);return await text;}
}
