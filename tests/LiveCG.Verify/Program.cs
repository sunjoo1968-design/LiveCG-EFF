using System.Diagnostics;
using System.Text.Json;
using LiveCG.Core;
using LiveCG.Testing;
using SkiaSharp;

var root=Path.GetFullPath(args.FirstOrDefault()??Environment.CurrentDirectory);
if(args.Contains("--browser-compare"))return BrowserCompare.Run(root);
var folder=Path.Combine(root,"artifacts","v010-core");Directory.CreateDirectory(folder);
Environment.SetEnvironmentVariable("LIVECG_DATA_DIR",Path.Combine(folder,"user-data"));
var failures=new List<string>();var count=0;
void Check(bool ok,string name){count++;Console.WriteLine((ok?"PASS ":"FAIL ")+name);if(!ok)failures.Add(name);}
void Reject(Action action,string name){try{action();Check(false,name);}catch(InvalidDataException){Check(true,name);}}
void Png(SKBitmap b,string path){using var image=SKImage.FromBitmap(b);using var data=image.Encode(SKEncodedImageFormat.Png,100);File.WriteAllBytes(path,data.ToArray());}
if(args.Contains("--visual-repro")){BroadcastChecks.VisualRepro(folder,Check);return failures.Count==0?0:1;}
if(args.Contains("--playback-only")){await PlaybackChecks.Run(folder,Check);Console.WriteLine($"RESULT {count-failures.Count}/{count} passed");return failures.Count==0?0:1;}
Check(Enum.GetValues<EffectKind>().Count(k=>k!=EffectKind.UserMotion)==20,"20 distinct effect primitives without per-video duplicates");
Check(Enum.GetValues<StrengthLevel>().Length==5,"five named strength levels");
var identityRegion=new EffectRegion();var regionHash=identityRegion.GetHashCode();identityRegion.Bounds=new(.2,.2,.2,.2);identityRegion.Name="변경";
Check(identityRegion.GetHashCode()==regionHash,"region identity stays stable during spatial and name edits");
var identityClip=new EffectClip();var clipHash=identityClip.GetHashCode();identityClip.Start=2;identityClip.Options=identityClip.Options with{Kind=EffectKind.Glow};
Check(identityClip.GetHashCode()==clipHash,"clip identity stays stable during timeline edits");
Check(typeof(ProductInfo).Assembly.GetType("LiveCG.Core.AutoDetector") is null,"automatic price detection removed");
var clipRegion=new EffectRegion();
var first=TimelineRules.Append(clipRegion,new(){Kind=EffectKind.RiseIn},.8,30);
var second=TimelineRules.Append(clipRegion,new(){Kind=EffectKind.Glow},1.2,30);
Check(first.Start==0&&Math.Abs(second.Start-.8)<1e-8,"append effects sequentially to one region");
var resolved=TimelineRules.Resolve([clipRegion],new(){Fps=30,EndHold=.5});
Check(resolved.FrameCount==75&&Math.Abs(resolved.Duration-2.5)<1e-8,"duration derives from last end plus tail hold");
TimelineRules.SetTiming(second,2.013,1.117,30);
Check(Math.Abs(second.Start*30-Math.Round(second.Start*30))<1e-6&&Math.Abs(second.Duration*30-Math.Round(second.Duration*30))<1e-6,"clip drag timing snaps to frames");
var changed=TimelineRules.Resolve([clipRegion],new(){EndHold=1});
Check(changed.Duration>resolved.Duration,"moving final clip extends output automatically");
second.Enabled=false;
Check(TimelineRules.Resolve([clipRegion],new(){EndHold=0}).FrameCount==24,"disabled clip excluded from output length");
second.Enabled=true;
Reject(()=>new EffectClip{Start=-1}.Validate(),"negative clip timing rejected");
Reject(()=>new EffectOptions{Strength=(StrengthLevel)99}.Validate(),"invalid strength rejected");
Reject(()=>new VideoOptions{Fps=0}.Validate(),"invalid fps rejected");
Reject(()=>WebmExporter.ValidateOutputNames([new(){SourcePath="C:\\a\\가격.png"},new(){SourcePath="C:\\b\\가격.PNG"}]),"same-stem collision rejected");

var path=TestFixtures.Create(folder);
var demo=TestFixtures.Demo(path);
var clone=demo.Snapshot();
Check(clone.Regions[3].Clips[0].Id==demo.Regions[3].Clips[0].Id,"export snapshot preserves deterministic effect identity");
clone.Regions[3].Clips[0].Options.Strength=StrengthLevel.Weak;
Check(demo.Regions[3].Clips[0].Options.Strength==StrengthLevel.Medium,"snapshot options do not alias original");
var defaults=new AppDefaults{Effect=new(){Kind=EffectKind.OrbitBurst,Strength=StrengthLevel.Strong},ClipDuration=2.5,Video=new(){Fps=60,EndHold=.5}};
var defaultsFile=Path.Combine(folder,"defaults.json");DocumentStore.Save(defaultsFile,defaults);
var read=DocumentStore.Load<AppDefaults>(defaultsFile);
Check(read.Effect.Strength==StrengthLevel.Strong&&read.ClipDuration==2.5&&read.Video.Fps==60,"new defaults round-trip all three-level / timing options");
var singleFrameDefaults=Path.Combine(folder,"single-frame-defaults.json");DocumentStore.Save(singleFrameDefaults,new AppDefaults{ClipDuration=1d/60});
Check(Math.Abs(DocumentStore.Load<AppDefaults>(singleFrameDefaults).ClipDuration-1d/60)<1e-9,"single-frame clip duration can be saved as default");
var project=new ProjectDocument{Jobs=[demo],Video=new(){Fps=24,EndHold=1}};
var projectFile=Path.Combine(folder,"demo.livecg.json");DocumentStore.Save(projectFile,project);
var restored=DocumentStore.Load<ProjectDocument>(projectFile);
Check(restored.SchemaVersion==6&&restored.Jobs[0].Regions[3].Clips.Count==3&&restored.Video.Fps==24,"multi-track project round-trip");
Check(restored.Video.Fps!=read.Video.Fps,"saved project independent of global defaults");
var templateFile=Path.Combine(folder,"demo-template.json");
DocumentStore.Save(templateFile,new TemplateDocument{Regions=demo.Regions.Select(r=>r.Copy()).ToList()});
Check(DocumentStore.Load<TemplateDocument>(templateFile).Regions.Count==4,"template retains multiple regions and clips");
var oldContents=File.ReadAllText(defaultsFile);defaults.ClipDuration=-1;Reject(()=>DocumentStore.Save(defaultsFile,defaults),"invalid save rejected");
Check(File.ReadAllText(defaultsFile)==oldContents,"invalid save preserves previous defaults");
var legacy=Path.Combine(folder,"legacy.json");
File.WriteAllText(legacy,"""{"SchemaVersion":1,"Jobs":[{"SourcePath":"C:\\old.png","Width":1080,"Height":1920,"Regions":[{"Name":"가격","Bounds":{"X":0.1,"Y":0.2,"Width":0.4,"Height":0.1},"Effect":{"Kind":"Zoom","Intensity":1,"Duration":1,"Period":3,"Repeat":true,"Speed":1}}]}],"Video":{"Duration":6,"Fps":30,"Crf":20}}""");
var migrated=DocumentStore.Load<ProjectDocument>(legacy);
Check(migrated.SchemaVersion==6&&migrated.Jobs[0].Regions[0].Clips.Count==2,"v0.1 repeating effect migrates to timeline clips");
Check(migrated.Jobs[0].Regions[0].Clips[0].Options.Strength==StrengthLevel.Strong&&TimelineRules.Resolve(migrated.Jobs[0],migrated.Video).FrameCount==180,"migration retains strong effect and original duration");
File.WriteAllText(legacy,"""{"SchemaVersion":1,"Effect":{"Kind":"Sweep","Intensity":0.2,"Duration":2},"Video":{"Duration":6,"Fps":30,"Crf":20}}""");
Check(DocumentStore.Load<AppDefaults>(legacy).Effect.Strength==StrengthLevel.Weak,"legacy intensity maps to weak / medium / strong");
File.WriteAllText(legacy,"{broken");Reject(()=>DocumentStore.Load<ProjectDocument>(legacy),"corrupt JSON rejected");

using var source=SKBitmap.Decode(path)!;
using(var scene=new EffectScene(source))
{
    var price=demo.Regions[3].Copy(false);price.Clips=[];
    using var unchanged=scene.Render([price],new(),0);
    Check(unchanged.Bytes.SequenceEqual(source.Bytes),"regions without clips leave source pixels unchanged");
    foreach(var kind in Enum.GetValues<EffectKind>().Where(k=>k!=EffectKind.UserMotion))
    {
        var effectRegion=price with {Clips=[new(){Duration=1,Options=new(){Kind=kind,Strength=StrengthLevel.Strong}}]};
        using var frame=scene.Render([effectRegion],new(),.35);
        Check(!frame.Bytes.SequenceEqual(source.Bytes),kind+": visible effect at active time");
        Check(frame.Width==960&&frame.Height==540&&frame.GetPixel(0,0).Alpha==0,kind+": dimensions and untouched transparent canvas");
        Png(frame,Path.Combine(folder,kind+".png"));
        if(!ProductInfo.HoldsDecoration(kind))
        {
            using var end=scene.Render([effectRegion],new(),1.1);
            Check(end.Bytes.SequenceEqual(source.Bytes),kind+": settles to original pixels");
        }
    }
    var rolling=price with{Clips=[new(){Duration=1,Options=new(){Kind=EffectKind.DigitRoll}}]};
    scene.Validate([rolling]);
    using var originalFrame=scene.Render([rolling],new(),.3);
    using var cloneScene=new EffectScene(source);
    using var clonedFrame=cloneScene.Render([rolling.Copy(false)],new(),.3);
    Check(originalFrame.Bytes.SequenceEqual(clonedFrame.Bytes),"preview / export share deterministic rolling frames");
}
var bounds=SKRect.Create(200,50);
var scales=Enum.GetValues<StrengthLevel>().Select(level=>EffectScene.Evaluate([new(){Options=new(){Kind=EffectKind.Zoom,Strength=level}}],.5,bounds).Scale).ToArray();
Check(scales[0]<scales[1]&&scales[1]<scales[2]&&scales[2]>=1.3,"three strengths produce distinct, stronger zoom impact");
var intro=new EffectClip{Start=1,Duration=1,Options=new(){Kind=EffectKind.RiseIn}};
Check(EffectScene.Evaluate([intro],.5,bounds).Alpha==0,"entrance hides region before its clip");
Check(EffectScene.Evaluate([intro],1.3,bounds).Alpha is >0 and <1&&EffectScene.Evaluate([intro],1.3,bounds).Y>0,"rise entrance moves upwards with fading");
Check(EffectScene.Evaluate([intro],2.1,bounds).Alpha==1&&EffectScene.Evaluate([intro],2.1,bounds).Y==0,"entrance stays revealed after clip");
intro.Options.Speed=.25;
Check(EffectScene.Evaluate([intro],2.1,bounds).Alpha==1&&EffectScene.Evaluate([intro],2.1,bounds).Y==0,"slow entrance still completes within its timeline clip");
var progressJob=new ImageJob();var updates=new List<string?>();progressJob.PropertyChanged+=(_,e)=>updates.Add(e.PropertyName);progressJob.Progress=42.5;progressJob.Status="실패";
Check(updates.Contains("Progress")&&updates.Contains("ProgressColor")&&progressJob.Progress==42.5,"per-file progress / status notifications");
using(var small=new SKBitmap(95,65))
{
    using(var c=new SKCanvas(small)){c.Clear(SKColors.Transparent);using var p=new SKPaint{Color=SKColors.Coral};c.DrawRect(10,10,60,35,p);}
    var input=Path.Combine(folder,"홀수 원본.png");Png(small,input);
    var output=Path.Combine(folder,"홀수 원본.webm");
    var job=new ImageJob{SourcePath=input,Width=95,Height=65,Regions=[new(){Bounds=new(.1,.1,.7,.7),Clips=[new(){Start=.2,Duration=.5,Options=new(){Kind=EffectKind.Glow}},new(){Start=.7,Duration=.4,Options=new(){Kind=EffectKind.Zoom}}]}]};
    var video=new VideoOptions{Fps=20,EndHold=.4};var exporter=new WebmExporter(RuntimePaths.Ffmpeg);
    var signals=new List<double>();
    await exporter.ExportAsync(job,video,output,true,new ImmediateProgress(p=>signals.Add(p)),CancellationToken.None);
    var data=await Run(RuntimePaths.Ffprobe,"-v","error","-count_frames","-show_streams","-of","json",output);
    using(var json=JsonDocument.Parse(data))
    {
        var streams=json.RootElement.GetProperty("streams");var v=streams[0];
        Check(streams.GetArrayLength()==1&&v.GetProperty("codec_name").GetString()=="vp9","VP9 WebM with no audio");
        Check(v.GetProperty("width").GetInt32()==95&&v.GetProperty("height").GetInt32()==65,"odd source dimensions retained");
        Check(v.GetProperty("nb_read_frames").GetString()=="30","encoded duration follows timeline exactly: 1.5 seconds / 20 fps");
    }
    Check(signals.Count>=30&&signals[^1]==1,"live encoder progress reaches complete");
    var decoded=Path.Combine(folder,"alpha.png");await Run(RuntimePaths.Ffmpeg,"-y","-v","error","-c:v","libvpx-vp9","-i",output,"-frames:v","1",decoded);
    using(var alpha=SKBitmap.Decode(decoded))Check(alpha.GetPixel(0,0).Alpha==0&&alpha.GetPixel(25,25).Alpha==255,"decoded alpha preserved outside effect");
    var previous=File.ReadAllBytes(output);
    using var cancelled=new CancellationTokenSource();cancelled.Cancel();
    try{await exporter.ExportAsync(job,video,output,true,null,cancelled.Token);Check(false,"cancel before export throws");}catch(OperationCanceledException){Check(true,"cancel before export throws");}
    Check(previous.SequenceEqual(File.ReadAllBytes(output)),"pre-cancel preserves existing output");
    using var during=new CancellationTokenSource();
    try{await exporter.ExportAsync(job,video,output,true,new ImmediateProgress(_=>during.Cancel()),during.Token);Check(false,"cancel after first frame throws");}catch(OperationCanceledException){Check(true,"cancel after first frame throws");}
    Check(previous.SequenceEqual(File.ReadAllBytes(output)),"mid-export cancel preserves existing output");
    Check(!Directory.GetFiles(folder,"*.partial.webm").Any(),"no incomplete output left behind");
}
if(args.Contains("--extended"))
{
    var output=Path.Combine(folder,"타임라인 예제.webm");
    await new WebmExporter(RuntimePaths.Ffmpeg).ExportAsync(demo,new(){Fps=30,EndHold=.9},output,true,null,CancellationToken.None);
    var metadata=await Run(RuntimePaths.Ffprobe,"-v","error","-count_frames","-show_streams","-of","json",output);
    using var json=JsonDocument.Parse(metadata);var stream=json.RootElement.GetProperty("streams")[0];
    Check(stream.GetProperty("nb_read_frames").GetString()=="150","four-region demo: automatic 5-second timeline / 150 frames");
}
WorkflowChecks.Run(folder,Check);
await StudioChecks.Run(folder,Check);
await LayerChecks.Run(folder,Check);
await GroupChecks.Run(folder,Check);
await DirectChecks.Run(folder,Check);
await PlaybackChecks.Run(folder,Check);
await BroadcastChecks.Run(folder,Check);
await SoftEdgeChecks.Run(folder,Check);
await ContextChecks.Run(folder,Check);
await V1Checks.Run(folder,Check);
await TextChecks.Run(folder,Check);
Console.WriteLine($"RESULT {count-failures.Count}/{count} passed");
File.WriteAllText(Path.Combine(folder,"result.txt"),$"{count-failures.Count}/{count} passed\n"+string.Join("\n",failures));
return failures.Count==0?0:1;

static async Task<string> Run(string executable,params string[] args)
{
    using var process=new Process{StartInfo=new(executable){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true}};
    foreach(var arg in args)process.StartInfo.ArgumentList.Add(arg);process.Start();
    var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();await process.WaitForExitAsync();
    if(process.ExitCode!=0)throw new IOException(await error);return await output;
}
internal sealed class ImmediateProgress(Action<double> action):IProgress<double>{public void Report(double value)=>action(value);}
