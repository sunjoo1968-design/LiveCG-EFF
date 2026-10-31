using System.Diagnostics;
using System.Text.Json;
using LiveCG.Core;
using SkiaSharp;

internal static class V1Checks
{
    public static async Task Run(string folder,Action<bool,string> check)
    {
        var dir=Path.Combine(folder,"v1");Directory.CreateDirectory(dir);var png=Path.Combine(dir,"쿠폰 50% & 샘플.png");
        using var source=new SKBitmap(160,100);source.Erase(SKColors.White);using(var canvas=new SKCanvas(source))using(var paint=new SKPaint{Color=SKColors.Red})canvas.DrawRect(40,30,80,40,paint);SourceImage.SavePng(source,png);
        var video=new VideoOptions{Fps=10,EndHold=0,Crf=10};var job=new ImageJob{SourcePath=png,Width=160,Height=100,Playback=new(){Enabled=true,IntroEnd=.5,LoopEnd=1.5,ExitEnabled=false,WebmRepeats=3,LoopRest=1},Regions=[new(){Bounds=new(.25,.3,.5,.4),Background=BackgroundMode.Solid,BackgroundColor="#FFFFFF",Clips=[new(){Start=.5,Duration=1,Options=new(){Kind=EffectKind.Zoom}}]}]};
        var plan=PlaybackPlan.Create(job,video);check(plan.RestFrames==10&&plan.WebmFrameCount==55&&plan.WebmDuration==5.5,"finite WebM includes exactly N-1 rests and skips the final rest");
        check(new[]{14,15,24,25,34,35,44,45,54}.Select(plan.WebmRest).SequenceEqual(new[]{false,true,true,false,false,true,true,false,false}),"rest boundaries are half-open integer frames");
        check(plan.WebmSourceFrame(25)==5&&plan.WebmSourceFrame(45)==5&&plan.WebmSourceFrame(54)==14,"each repeat restarts at the emphasis in-point after its rest");
        var oneJob=job.Snapshot();oneJob.Playback.WebmRepeats=1;var once=PlaybackPlan.Create(oneJob,video);check(once.WebmFrameCount==15&&!Enumerable.Range(0,15).Any(once.WebmRest),"one finite repeat never appends a rest");
        var clock=new PlaybackClock(plan,true);check(clock.Sample(1.5).IsRest&&clock.Sample(2.4).IsRest&&!clock.Sample(2.5).IsRest&&clock.Sample(2.5).Time==.5,"broadcast clock repeats effect plus rest without repeating entrance");
        using(var scene=new EffectScene(source))
        {
            using var original=plan.RenderFrame(scene,job.Regions,video,1.4,true);check(original.Bytes.SequenceEqual(source.Bytes),"original rest restores all source pixels instead of frozen animation or hidden layers");
            using var transparent=(plan with{RestMode=LoopRestMode.Transparent}).RenderFrame(scene,job.Regions,video,1.4,true);check(transparent.GetPixel(80,50).Alpha==0,"transparent rest clears the output alpha");
            using var hold=(plan with{RestMode=LoopRestMode.Hold}).RenderFrame(scene,job.Regions,video,1.4,true);using var last=scene.Render(job.Regions,video,1.4);check(hold.Bytes.SequenceEqual(last.Bytes),"hold rest renders the last loop frame exactly");
        }
        var exitJob=job.Snapshot();exitJob.Playback.ExitEnabled=true;exitJob.Playback.ExitDuration=.5;var exiting=PlaybackPlan.Create(exitJob,video);var wait=new PlaybackClock(exiting,true);wait.RequestExit(.8);check(wait.Sample(1.5).Time==1.5&&!wait.Sample(1.5).IsRest,"exit at loop end skips the next rest");
        wait=new PlaybackClock(exiting,true);wait.RequestExit(1.8);check(wait.Sample(1.8).Time==1.5&&!wait.Sample(1.8).IsRest,"exit requested during rest begins immediately");
        var saved=Path.Combine(dir,"round.json");DocumentStore.Save(saved,new ProjectDocument{Jobs=[job]});check(DocumentStore.Load<ProjectDocument>(saved).Jobs[0].Playback.LoopRest==1,"project persists repeat rest options");
        File.WriteAllText(saved,File.ReadAllText(saved).Replace("\"LoopRest\": 1,",""));check(DocumentStore.Load<ProjectDocument>(saved).Jobs[0].Playback.LoopRest==0,"old project without rest property keeps gap-free timing");
        check(PlaybackSections.NewDefault().LoopRest==1&&PlaybackSections.NewDefault().RestMode==LoopRestMode.Original,"new-work defaults use a one-second original rest");
        void Reject(PlaybackSections p){try{p.Validate();check(false,"invalid rest rejected");}catch(InvalidDataException){check(true,"invalid rest rejected");}}
        Reject(job.Playback with{LoopRest=double.NaN});Reject(job.Playback with{LoopRest=-1});Reject(job.Playback with{LoopRest=31});
        foreach(var format in new[]{SKEncodedImageFormat.Png,SKEncodedImageFormat.Jpeg,SKEncodedImageFormat.Webp})
        {
            var file=Path.Combine(dir,"이미지."+(format==SKEncodedImageFormat.Jpeg?"jpg":format.ToString().ToLowerInvariant()));using var image=SKImage.FromBitmap(source);using var bytes=image.Encode(format,100);File.WriteAllBytes(file,bytes.ToArray());using var read=SourceImage.Read(file);check(read.Width==160&&read.Height==100,$"{format} source preserves original size");
            var j=job.Snapshot();j.SourcePath=file;var target=Path.Combine(dir,format+".obs");await new BrowserExporter().ExportAsync(j,video,target,true,null,CancellationToken.None);
            check(File.ReadAllBytes(Path.Combine(target,"original.png")).Take(8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}),$"{format} browser asset is encoded as real PNG");
            check(File.Exists(Path.Combine(target,BrowserExporter.EntryName(j)))&&File.Exists(Path.Combine(target,BrowserExporter.PreviewName(j)))&&File.ReadAllText(Path.Combine(target,"사용안내.txt")).Contains(BrowserExporter.EntryName(j)),"browser package names both HTML entry points after the original source");
        }
        var bmp=Path.Combine(dir,"픽셀.bmp");using(var output=new BinaryWriter(File.Create(bmp))){output.Write((ushort)0x4D42);output.Write(54+8*8*3);output.Write(0);output.Write(54);output.Write(40);output.Write(8);output.Write(8);output.Write((ushort)1);output.Write((ushort)24);output.Write(new byte[24]);output.Write(Enumerable.Repeat((byte)120,8*8*3).ToArray());}using(var read=SourceImage.Read(bmp))check(read.Width==8&&read.Height==8,"BMP source is accepted by the shared decoder");
        var jpg=File.ReadAllBytes(Path.Combine(dir,"이미지.jpg"));var exif=Convert.FromHexString("FFE1002245786966000049492A0008000000010012010300010000000600000000000000");var rotated=Path.Combine(dir,"회전.jpg");File.WriteAllBytes(rotated,jpg[..2].Concat(exif).Concat(jpg[2..]).ToArray());using(var read=SourceImage.Read(rotated))check(read.Width==100&&read.Height==160&&read.GetPixel(50,80).Red>200,"JPEG EXIF orientation is applied before editing and output");
        check(SourceImage.Supported("A.JPEG")&&SourceImage.Supported("a.webp")&&!SourceImage.Supported("a.psd"),"import extensions accept static graphic formats case-insensitively");
        var changed=job.Snapshot();changed.Width=10;try{using var image=SourceImage.Read(changed);check(false,"changed original size rejected");}catch(InvalidDataException){check(true,"changed source dimensions are detected before preview or export");}
        var duplicate=job.Snapshot();duplicate.SourcePath=Path.ChangeExtension(job.SourcePath,"jpg");try{WebmExporter.ValidateOutputNames([job,duplicate]);check(false,"same basename collision rejected");}catch(InvalidDataException){check(true,"same basename across PNG and JPG is rejected before output");}
        var webm=Path.Combine(dir,"rest.webm");await new WebmExporter(RuntimePaths.Ffmpeg).ExportAsync(job,video,webm,true,null,CancellationToken.None);
        var metadata=await Run(RuntimePaths.Ffprobe,"-v","error","-count_frames","-show_streams","-of","json",webm);using(var json=JsonDocument.Parse(metadata))check(json.RootElement.GetProperty("streams")[0].GetProperty("nb_read_frames").GetString()=="55","actual encoded WebM contains the computed rest-inclusive frame count");
        var restPng=Path.Combine(dir,"rest-frame.png");await Run(RuntimePaths.Ffmpeg,"-v","error","-y","-c:v","libvpx-vp9","-i",webm,"-ss","1.8","-frames:v","1",restPng);using(var frame=SKBitmap.Decode(restPng))check(frame.GetPixel(80,50).Red>220&&frame.GetPixel(10,10).Red>240,"actual WebM rest displays the original foreground and background");
        // Top-level fixture is consumed by real Chromium and OBS verification.
        var browser=Path.Combine(folder,"v1-rest.obs");await new BrowserExporter().ExportAsync(job,video,browser,true,null,CancellationToken.None);
        using(var scene=new EffectScene(source))foreach(var mode in new[]{LoopRestMode.Hold,LoopRestMode.Transparent}){using var frame=(plan with{RestMode=mode}).RenderFrame(scene,job.Regions,video,1.4,true);SourceImage.SavePng(frame,Path.Combine(browser,mode+".png"));}
    }
    private static async Task<string> Run(string exe,params string[] args){using var p=new Process{StartInfo=new(exe){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true}};foreach(var a in args)p.StartInfo.ArgumentList.Add(a);p.Start();var text=p.StandardOutput.ReadToEndAsync();var error=p.StandardError.ReadToEndAsync();await p.WaitForExitAsync();if(p.ExitCode!=0)throw new IOException(await error);return await text;}
}
