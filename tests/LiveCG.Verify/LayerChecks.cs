using System.Text.Json;
using System.Diagnostics;
using LiveCG.Core;
using SkiaSharp;

internal static class LayerChecks
{
    public static async Task Run(string folder,Action<bool,string> check)
    {
        void Reject(Action action,string message){var rejected=false;try{action();}catch(InvalidDataException){rejected=true;}check(rejected,message);}
        EffectClip Move(double x){var m=new MotionDefinition();m.SetKey(MotionProperty.X,0,x);m.SetKey(MotionProperty.X,60,x);return new(){Duration=2,Options=new(){Kind=EffectKind.UserMotion},Motion=m};}
        using var source=new SKBitmap(160,100);using(var c=new SKCanvas(source)){c.Clear(SKColors.White);using var p=new SKPaint{Color=SKColors.Blue};c.DrawRect(20,20,100,60,p);p.Color=SKColors.Red;c.DrawRect(50,40,20,20,p);}
        var upper=new EffectRegion{Name="위 객체",Bounds=NormalizedRect.FromPixels(SKRect.Create(50,40,20,20),160,100),Background=BackgroundMode.Solid,BackgroundColor="#0000FF",Clips=[Move(2)]};
        var lower=new EffectRegion{Name="아래 판",Bounds=NormalizedRect.FromPixels(SKRect.Create(20,20,100,60),160,100),Background=BackgroundMode.Solid,BackgroundColor="#FFFFFF"};
        using(var scene=new EffectScene(source))
        {
            using var moved=scene.Render([upper,lower],new(),.5);check(moved.GetPixel(55,50)==SKColors.Red&&moved.GetPixel(95,50)==SKColors.Red,"independent upper motion leaves the lower original content intact");
            check(Enumerable.Range(0,100).Sum(y=>Enumerable.Range(0,160).Count(x=>moved.GetPixel(x,y)==SKColors.Red))==800,"independent objects keep their source crop without implicit ownership masks");
            using var reordered=scene.Render([lower,upper],new(),.5);check(reordered.GetPixel(55,50)==SKColors.Red&&reordered.GetPixel(95,50)==SKColors.Blue,"changing layer order invalidates ownership and changes the top surface");
            upper.Enabled=false;using var hidden=scene.Render([upper,lower],new(),.5);check(hidden.GetPixel(55,50)==SKColors.Red&&hidden.GetPixel(95,50)==SKColors.Blue,"hidden upper independent layer preserves lower original crop");
            using var hiddenRaw=scene.Render([upper,lower],new(),.5);check(hiddenRaw.GetPixel(55,50)==SKColors.Red,"hidden independent layer never auto-cuts a lower crop");
            upper.Enabled=true;lower.Enabled=false;using var hiddenLower=scene.Render([upper,lower],new(),.5);check(hiddenLower.GetPixel(25,25)==SKColors.White&&hiddenLower.GetPixel(95,50)==SKColors.Red,"hidden lower layer is removed while visible upper layer remains");
            lower.Enabled=true;upper.Clips=[Move(0)];using var identity=scene.Render([upper,lower],new(),.5);check(identity.Bytes.SequenceEqual(source.Bytes),"visible identity layers settle on exact original pixels");
        }
        var triangle=new EffectRegion{Shape=RegionShape.Polygon,Points=[new(0,0),new(1,0),new(0,1)],Bounds=new(.1,.1,.6,.6),Background=BackgroundMode.Solid,BackgroundColor="#FFFFFF",Enabled=false};triangle.Validate();
        check(RegionMask.Contains(triangle,new(25,20),160,100)&&!RegionMask.Contains(triangle,new(100,65),160,100),"point path hit testing follows the actual polygon rather than its bounds");
        using(var scene=new EffectScene(source)){using var frame=scene.Render([triangle],new(),0);check(frame.GetPixel(25,25)==SKColors.White&&frame.GetPixel(100,65)==source.GetPixel(100,65),"polygon hide removes only its path in the common renderer");}
        var pathCopy=triangle.Copy();pathCopy.Points[0]=new(1,1);check(triangle.Points[0]==new RegionPoint(0,0),"custom path copies isolate point edits");
        Reject(()=>new EffectRegion{Shape=RegionShape.Freehand,Points=[new(0,0),new(1,1)]}.Validate(),"unfinished custom paths cannot be saved");
        var oldDoc=new ProjectDocument{SchemaVersion=4,Jobs=[new(){SourcePath="fixture.png",Width=160,Height=100,Regions=[lower,upper]}]};var oldPath=Path.Combine(folder,"v04-order.json");File.WriteAllText(oldPath,JsonSerializer.Serialize(oldDoc));var read=DocumentStore.Load<ProjectDocument>(oldPath);
        check(read.SchemaVersion==6&&read.Jobs[0].Regions[0].Id==upper.Id,"schema4 migration preserves old last-layer priority as first-row priority");
        var customPath=Path.Combine(folder,"path.json");DocumentStore.Save(customPath,new TemplateDocument{Regions=[triangle]});check(DocumentStore.Load<TemplateDocument>(customPath).Regions[0].Points.SequenceEqual(triangle.Points),"schema5 templates preserve custom path points and visibility");
        using var coupon=new SKBitmap(40,30);using(var c=new SKCanvas(coupon)){c.Clear(SKColors.White);using var p=new SKPaint{Color=SKColors.Red};c.DrawRect(5,5,30,20,p);p.Color=SKColors.White;c.DrawRect(12,10,7,7,p);}
        var protectedMask=ObjectExtractor.Extract(coupon,new(){Feather=0});var rawMask=ObjectExtractor.Extract(coupon,new(){Feather=0,PreserveInterior=false});
        check(protectedMask.Alpha[12*40+14]==255&&protectedMask.Alpha[0]==0,"outside-connected background extraction preserves same-color interior lettering by default");
        check(rawMask.Alpha[12*40+14]==0,"interior protection can be disabled for global color removal");
        var contracted=protectedMask.Morph(1,false);check(contracted.Alpha[5*40+5]==0&&contracted.Alpha[6*40+6]>0,"one pixel contraction removes the outer rim without erasing the interior");
        check(contracted.Alpha[12*40+14]==255,"border contraction keeps protected lettering inside the object");
        using var fringe=new SKBitmap(24,24);using(var c=new SKCanvas(fringe)){c.Clear(SKColors.White);using var p=new SKPaint{Color=new(255,128,128)};c.DrawRect(5,5,14,14,p);p.Color=SKColors.Red;c.DrawRect(6,6,12,12,p);}
        var before=fringe.Bytes.ToArray();var mask=ObjectExtractor.Extract(fringe,new(){Feather=0});var clean=ObjectExtractor.CleanEdges(fringe,mask,SKColors.White);
        using(var colors=SKBitmap.Decode(Convert.FromBase64String(clean.Foreground)))check(colors.GetPixel(5,12).Green==0&&clean.Mask.Alpha[12*24+5] is >100 and <160,"edge cleanup removes white matte color while recovering fractional coverage");
        var fitted=ObjectExtractor.Fit(clean.Mask.Morph(1,false),SKRectI.Create(24,24),24,24,clean.Foreground,mask.Morph(1,true));fitted.Cutout.Validate();
        var rect=ObjectExtractor.PixelRect(fitted.Bounds,24,24);check(rect.Width==fitted.Cutout.Width&&rect.Height==fitted.Cutout.Height,"fitted correction image keeps exact crop dimensions when reopened");
        var legacyRect=ObjectExtractor.PixelRect(new(0.1666666716337204,0.1666666716337204,0.6666666865348816,0.6666666865348816),24,24);check(legacyRect.Width==16&&legacyRect.Height==16,"legacy single-precision bounds do not gain an extra mask pixel");
        check(fitted.Cutout.ForegroundPng.Length>0&&fitted.Cutout.ErasePng.Length>0,"fit stores independent corrected color and original-source erase coverage");
        var moving=new EffectRegion{Bounds=fitted.Bounds,Cutout=fitted.Cutout,Background=BackgroundMode.Solid,BackgroundColor="#FFFFFF",Clips=[Move(.3)]};using(var scene=new EffectScene(fringe)){using var frame=scene.Render([moving],new(),.5);check(frame.GetPixel(5,12)==SKColors.White,"source erase coverage removes the original one-pixel fringe after contraction and motion");}
        check(fringe.Bytes.SequenceEqual(before),"matte cleanup and fitting never modify original PNG pixels");
        check(ObjectExtractor.PixelRect(new(0,0,.00001,.00001),24,24).Width==1,"subpixel selections retain a valid one-pixel raster extent");
        check(ObjectExtractor.GuessSurrounding(coupon,new(5,5,35,25))==SKColors.White,"background guess uses outside pixels even when object fills its selected box");
        var correctedPath=Path.Combine(folder,"corrected.json");DocumentStore.Save(correctedPath,new TemplateDocument{Regions=[moving]});var correction=DocumentStore.Load<TemplateDocument>(correctedPath).Regions[0].Cutout!;
        check(correction.ForegroundPng==fitted.Cutout.ForegroundPng&&correction.ErasePng==fitted.Cutout.ErasePng,"correction and erase masks survive document serialization");
        foreach(var kind in new[]{EffectKind.FadeIn,EffectKind.RiseIn,EffectKind.Zoom,EffectKind.Highlight,EffectKind.UserMotion})
        {
            var original=new EffectClip{Start=1,Duration=2,Options=new(){Kind=kind},Motion=kind==EffectKind.UserMotion?MotionDefinition.Example():null};var left=original.Copy(false);var right=TimelineRules.Split(left,2,30)!;
            check(left.Duration==1&&right.Start==2&&right.SourceOffset==1&&left.SequenceId==right.SequenceId,kind+": scissors preserve source window and continuation identity");
            var same=true;foreach(var t in new[]{1.2,1.8,2.0,2.3,2.9,3.1}){var a=EffectScene.Evaluate([original],t,SKRect.Create(100,80));var b=EffectScene.Evaluate([left,right],t,SKRect.Create(100,80));same&=Math.Abs(a.Alpha-b.Alpha)<1e-5&&Math.Abs(a.X-b.X)<1e-5&&Math.Abs(a.Y-b.Y)<1e-5&&Math.Abs(a.Scale-b.Scale)<1e-5;}
            check(same,kind+": split fragments evaluate identically across the cut without restart or double entrance");
        }
        var looping=new EffectClip{Duration=6,Loop=true,LoopPeriod=.8,Options=new(){Kind=EffectKind.Sweep}};var l=looping.Copy(false);var r=TimelineRules.Split(l,2.3,30)!;
        check(new[]{2.3,2.31,2.8,3.5,5.9}.All(t=>Math.Abs(ClipTiming.Progress(looping,t)-ClipTiming.Progress(r,t))<1e-8),"loop split preserves its original phase on the right fragment");
        check(TimelineRules.Split(r,r.Start,30) is null&&TimelineRules.Split(r,r.End,30) is null,"scissors ignore clip boundaries instead of creating zero-length pieces");
        var last=TimelineRules.Split(r,3.1,30)!;check(Math.Abs(last.SourceOffset-3.1)<1e-8&&last.SourceDuration==6,"repeated cuts accumulate the original source offset");
        var asset=new EffectAsset{Name="분할 에셋",Clips=[l,r,last]};var instances=AssetStore.Instantiate(asset,0,30);check(instances.Select(c=>c.SequenceId).Distinct().Count()==1&&instances[0].SequenceId!=l.SequenceId,"asset insertion remaps split sequences as an independent coherent group");
        var first=new EffectClip{Start=0,Duration=1};var second=new EffectClip{Start=2,Duration=1};var third=new EffectClip{Start=4,Duration=1};var track=new EffectRegion{Clips=[first,second,third]};
        TimelineRules.SetTimingWithFollowers(track,first,0,1.5,30,false,true);check(second.Start==1.5&&third.Start==3.5,"follow magnet closes the front gap while preserving later block spacing");
        TimelineRules.SetTimingWithFollowers(track,first,0,.5,30,false,true);check(second.Start==.5&&third.Start==2.5,"follow magnet pulls the trailing block back on mouse shrink");
        upper.Enabled=true;lower.Enabled=true;lower.Shape=RegionShape.Freehand;lower.Points=[new(0,0),new(1,0),new(1,1),new(0,1)];upper.Shape=RegionShape.Polygon;upper.Points=[new(0,0),new(1,0),new(0,1)];
        var motion=new MotionDefinition();motion.SetKey(MotionProperty.X,0,0,KeyEasing.Linear);motion.SetKey(MotionProperty.X,60,2);var rootClip=new EffectClip{Duration=2,Options=new(){Kind=EffectKind.UserMotion},Motion=motion};var rightClip=TimelineRules.Split(rootClip,1,30)!;upper.Clips=[rootClip,rightClip];
        var png=Path.Combine(folder,"레이어 경로.png");using(var image=SKImage.FromBitmap(source))using(var data=image.Encode(SKEncodedImageFormat.Png,100))File.WriteAllBytes(png,data.ToArray());
        var output=Path.Combine(folder,"레이어 경로 분할.webm");await new WebmExporter(RuntimePaths.Ffmpeg).ExportAsync(new(){SourcePath=png,Width=160,Height=100,Regions=[upper,lower]},new(){Fps=15,EndHold=.2},output,true,null,CancellationToken.None);
        foreach(var time in new[]{.5,1.5})
        {
            var decoded=Path.Combine(folder,$"layer-{time:0.0}.png");await RunTool(RuntimePaths.Ffmpeg,"-v","error","-y","-c:v","libvpx-vp9","-i",output,"-ss",time.ToString(System.Globalization.CultureInfo.InvariantCulture),"-frames:v","1",decoded);
            using var frame=SKBitmap.Decode(decoded);var old=frame.GetPixel(55,45);var shifted=frame.GetPixel(time<1?65:85,45);check(frame.Width==160&&frame.Height==100&&old.Red>220&&old.Blue<30&&shifted.Red>220&&shifted.Blue<30,$"actual WebM at {time:0.0}s preserves custom layer path, independent crop and split playback");
        }
    }
    private static async Task RunTool(string path,params string[] args){using var p=new Process{StartInfo=new(path){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true}};foreach(var arg in args)p.StartInfo.ArgumentList.Add(arg);p.Start();var output=p.StandardOutput.ReadToEndAsync();var error=p.StandardError.ReadToEndAsync();await p.WaitForExitAsync();if(p.ExitCode!=0)throw new IOException(await error);await output;}
}
