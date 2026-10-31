using System.Diagnostics;
using System.Text.Json;
using LiveCG.Core;
using SkiaSharp;

internal static class StudioChecks
{
    public static async Task Run(string folder,Action<bool,string> check)
    {
        void Reject(Action action,string message){var rejected=false;try{action();}catch(InvalidDataException){rejected=true;}check(rejected,message);}
        var motion=new MotionDefinition();check(motion.Value(MotionProperty.ScaleX,10)==1&&motion.Value(MotionProperty.Opacity,10)==1,"empty key channels preserve neutral defaults");
        motion.SetKey(MotionProperty.X,0,0,KeyEasing.Linear);motion.SetKey(MotionProperty.X,60,1);
        check(motion.Value(MotionProperty.X,30)==.5&&motion.Value(MotionProperty.Y,30)==0,"linear key interpolation and independent channels");
        motion.Tracks[0].Keys[0].Easing=KeyEasing.Hold;check(motion.Value(MotionProperty.X,59)==0&&motion.Value(MotionProperty.X,60)==1,"hold key changes only at next frame");
        motion.Tracks[0].Keys[0].Easing=KeyEasing.EaseIn;check(motion.Value(MotionProperty.X,30)==.25,"ease-in changes interpolation curve");
        motion.Tracks[0].Keys[0].Easing=KeyEasing.EaseOut;check(motion.Value(MotionProperty.X,30)==.75,"ease-out changes interpolation curve");
        motion.Tracks[0].Keys[0].Easing=KeyEasing.EaseInOut;check(motion.Value(MotionProperty.X,30)==.5&&motion.Value(MotionProperty.X,15)<.25,"smooth interpolation has eased endpoints");
        var copy=motion.Copy();copy.SetKey(MotionProperty.X,0,-1);check(motion.Value(MotionProperty.X,0)==0,"motion copies isolate track and key edits");
        copy=motion.Copy();copy.ChangeFps(60,true);check(copy.LengthFrames==120&&copy.Duration==2&&copy.Tracks[0].Keys[1].Frame==120,"fps change preserves actual timing by resampling frame positions");
        copy.ChangeFps(30,false);check(copy.LengthFrames==120&&copy.Duration==4,"fps frame-preserving option keeps key frame numbers");
        Reject(()=>new MotionDefinition{LengthFrames=0}.Validate(),"zero motion length rejected");
        Reject(()=>motion.SetKey(MotionProperty.Opacity,20,2),"invalid opacity key rejected");
        var invalid=motion.Copy();invalid.Tracks[0].Keys.Add(invalid.Tracks[0].Keys[0] with{});Reject(invalid.Validate,"duplicate key positions and identities rejected");
        var animated=new MotionDefinition();animated.SetKey(MotionProperty.Opacity,0,0);animated.SetKey(MotionProperty.Opacity,12,1);animated.SetKey(MotionProperty.X,36,0);animated.SetKey(MotionProperty.X,60,1);
        var clip=new EffectClip{Duration=2,Options=new(){Kind=EffectKind.UserMotion},Motion=animated};
        using var source=new SKBitmap(200,160);using(var c=new SKCanvas(source)){c.Clear(SKColors.Transparent);using var p=new SKPaint{Color=SKColors.Crimson};c.DrawRect(40,50,40,40,p);}
        var region=new EffectRegion{Bounds=new(.2,50d/160,.2,.25),Background=BackgroundMode.Transparent,Clips=[clip]};
        using(var scene=new EffectScene(source))
        {
            using var first=scene.Render([region],new(),0);using var appeared=scene.Render([region],new(),.4);using var moved=scene.Render([region],new(),2.1);
            check(first.GetPixel(50,60).Alpha==0,"user-created fade hides original object at first key");
            check(appeared.GetPixel(50,60).Alpha==255,"user-created fade reaches full opacity at authored frame");
            check(moved.GetPixel(50,60).Alpha==0&&moved.GetPixel(90,60).Alpha==255,"custom movement erases original position and holds final position");
            animated.SetKey(MotionProperty.ScaleX,60,1.5);animated.SetKey(MotionProperty.ScaleY,60,.5);animated.SetKey(MotionProperty.AnchorX,60,0);animated.SetKey(MotionProperty.AnchorY,60,0);
            var state=EffectScene.Evaluate([clip],2.1,SKRect.Create(40,40));check(state.ScaleX==1.5&&state.ScaleY==.5&&state.AnchorX==0,"nonuniform scale and anchor channels reach renderer");
            animated.SetKey(MotionProperty.ScaleX,60,1);animated.SetKey(MotionProperty.ScaleY,60,1);animated.SetKey(MotionProperty.AnchorX,60,.5);animated.SetKey(MotionProperty.AnchorY,60,.5);
            clip.Loop=true;clip.Duration=4;clip.LoopPeriod=2;using var a=scene.Render([region],new(),.3);using var b=scene.Render([region],new(),2.3);
            check(a.Bytes.SequenceEqual(b.Bytes),"user keyframe loop uses the same phase as native effects");clip.Loop=false;clip.Duration=2;
        }
        var job=new ImageJob{SourcePath=Path.Combine(folder,"사용자 모션.png"),Width=200,Height=160,Regions=[region]};
        var snapshot=job.Snapshot();snapshot.Regions[0].Clips[0].Motion!.SetKey(MotionProperty.X,60,2);
        check(animated.Value(MotionProperty.X,60)==1&&snapshot.Regions[0].Clips[0].Id==clip.Id,"export snapshot isolates custom keyframes while preserving clip identity");
        using(var image=SKImage.FromBitmap(source))using(var data=image.Encode(SKEncodedImageFormat.Png,100))File.WriteAllBytes(job.SourcePath,data.ToArray());
        var asset=new EffectAsset{Name="프레임 가격 강조",Category="방송",Clips=[clip.Copy()]};asset.ThumbnailPng=EffectSample.Thumbnail(asset);AssetStore.Save(asset);
        var assets=AssetStore.List(out var errors);check(errors.Count==0&&assets.Any(a=>a.Id==asset.Id),"user motion asset persists in local library");
        var exported=Path.Combine(folder,"내효과.lcgeffect.json");DocumentStore.Save(exported,asset);var imported=AssetStore.Import(exported);
        check(imported.Id!=asset.Id&&imported.Clips[0].Motion!.Value(MotionProperty.X,60)==1,"asset import creates a reusable independent asset");
        var batch=AssetStore.Instantiate(asset,3,24);var batch2=AssetStore.Instantiate(asset,5,30);
        check(batch[0].Start==3&&batch[0].Id!=clip.Id&&batch[0].AssetInstanceId!=batch2[0].AssetInstanceId,"asset insertion has new clip and group identities at project timing");
        batch[0].Motion!.SetKey(MotionProperty.X,60,3);check(asset.Clips[0].Motion!.Value(MotionProperty.X,60)==1,"inserted asset edits do not change saved source asset");
        var previous=File.ReadAllText(AssetStore.PathFor(asset.Id));var broken=asset.Copy();broken.Name="";Reject(()=>AssetStore.Save(broken),"invalid asset save rejected");
        check(File.ReadAllText(AssetStore.PathFor(asset.Id))==previous,"failed asset validation preserves prior file");
        AssetStore.Remove(imported.Id);check(!File.Exists(AssetStore.PathFor(imported.Id))&&Directory.GetFiles(Path.Combine(AssetStore.DirectoryPath,"deleted")).Length>0,"user asset delete preserves a recoverable trash copy");
        var defaults=new AppDefaults{Effect=new(){Kind=EffectKind.UserMotion},Motion=animated.Copy()};var defaultsFile=Path.Combine(folder,"motion-defaults.json");DocumentStore.Save(defaultsFile,defaults);
        check(DocumentStore.Load<AppDefaults>(defaultsFile).Motion!.Value(MotionProperty.X,60)==1,"default settings preserve authored custom motion");
        using(var image=new SKBitmap(100,80))
        {
            using(var c=new SKCanvas(image)){c.Clear(SKColors.White);using var p=new SKPaint{Color=SKColors.Red};c.DrawRect(20,20,60,40,p);}
            var mask=ObjectExtractor.Extract(image,new(){Mode=ExtractionMode.BackgroundColor,Background=SKColors.White,Feather=0,IncludeShadow=false});
            check(mask.Alpha[0]==0&&mask.Alpha[40*100+50]==255,"background separation keeps object and removes approximate surrounding area");
            var fit=ObjectExtractor.Fit(mask,SKRectI.Create(100,80),100,80);check(fit.Cutout.Width==60&&fit.Cutout.Height==40&&Math.Abs(fit.Bounds.X-.2)<1e-8,"object fit crops selection and mask to actual object bounds");
            var restored=MaskBuffer.Decode(fit.Cutout);check(restored.Alpha.All(a=>a==255),"encoded object contour round-trips its alpha");
            var before=mask.Copy();mask.Brush(50,40,5,false);check(mask.Alpha[40*100+50]==0&&before.Alpha[40*100+50]==255,"exclude brush and prior mask copy are independent");
            mask.Brush(5,5,3,true);check(mask.Alpha[5*100+5]==255,"include brush can protect manually chosen image areas");
            var soft=ObjectExtractor.Extract(image,new(){Background=SKColors.White,Feather=2});check(soft.Alpha.Any(a=>a is >0 and <255),"feather option produces soft contour edges");
            var sel=new EffectRegion{Bounds=new(0,0,1,1),Cutout=before.Encode(),Background=BackgroundMode.Transparent,Clips=[new(){Start=1,Options=new(){Kind=EffectKind.FadeIn}}]};
            using var scene=new EffectScene(image);using var hidden=scene.Render([sel],new(),0);
            check(hidden.GetPixel(50,40).Alpha==0&&hidden.GetPixel(5,5)==SKColors.White,"cutout removes only object while preserving pixels outside its contour");
            using var finished=scene.Render([sel],new(),2.1);check(finished.Bytes.SequenceEqual(image.Bytes),"cutout entrance returns exactly to original pixels at identity state");
            using var cropped=ObjectExtractor.CropObject(image,sel);check(cropped.GetPixel(0,0).Alpha==0&&cropped.GetPixel(50,40).Alpha==255,"studio sample contains the extracted object without background");
            Reject(()=>new ObjectCutout{Width=10,Height=10,MaskPng="invalid"}.Validate(),"damaged contour data rejected before rendering");
            var doc=new ProjectDocument{Jobs=[new(){SourcePath=job.SourcePath,Width=100,Height=80,Regions=[sel]}]};var docFile=Path.Combine(folder,"cutout-project.json");DocumentStore.Save(docFile,doc);
            check(DocumentStore.Load<ProjectDocument>(docFile).Jobs[0].Regions[0].Cutout!.MaskPng==sel.Cutout.MaskPng,"project stores object contour independently of reusable motion assets");
            File.WriteAllText(docFile,JsonSerializer.Serialize(new ProjectDocument{Jobs=[job]}).Replace("\"SchemaVersion\":6","\"SchemaVersion\":3"));check(DocumentStore.Load<ProjectDocument>(docFile).SchemaVersion==6,"v0.3 projects upgrade to schema6");
        }
        using(var alphaSource=new SKBitmap(20,20))
        {
            alphaSource.Erase(SKColors.Transparent);alphaSource.SetPixel(10,10,new SKColor(255,0,0,128));var mask=ObjectExtractor.Extract(alphaSource,new(){Feather=0});
            using var crop=ObjectExtractor.CropObject(alphaSource,new(){Bounds=new(0,0,1,1),Cutout=mask.Encode()});check(crop.GetPixel(10,10).Alpha==128,"transparent PNG uses original alpha without multiplying it twice");
        }
        var output=Path.Combine(folder,"사용자 모션.webm");await new WebmExporter(RuntimePaths.Ffmpeg).ExportAsync(job,new(){Fps=30,EndHold=.2},output,true,null,CancellationToken.None);
        var decoded=Path.Combine(folder,"custom-last.png");await RunTool(RuntimePaths.Ffmpeg,"-v","error","-y","-c:v","libvpx-vp9","-i",output,"-ss","2.1","-frames:v","1",decoded);
        using(var frame=SKBitmap.Decode(decoded))check(frame.GetPixel(50,60).Alpha==0&&frame.GetPixel(90,60).Alpha>240,"actual custom WebM preserves animated displacement and alpha");
    }
    private static async Task<string> RunTool(string path,params string[] args)
    {using var p=new Process{StartInfo=new(path){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true}};foreach(var arg in args)p.StartInfo.ArgumentList.Add(arg);p.Start();var text=p.StandardOutput.ReadToEndAsync();var error=p.StandardError.ReadToEndAsync();await p.WaitForExitAsync();if(p.ExitCode!=0)throw new IOException(await error);return await text;}
}
