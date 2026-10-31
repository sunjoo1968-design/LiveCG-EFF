using LiveCG.Core;
using SkiaSharp;

internal static class BroadcastChecks
{
    public static async Task Run(string folder,Action<bool,string> check)
    {
        VisualRepro(folder,check);
        check(ProductInfo.Strengths.Select(s=>new EffectOptions{Strength=s}.Power).SequenceEqual(new[]{.18,.55,1,1.65,2.4}),"five ordered strengths retain legacy powers and extend both ends");
        check(ProductInfo.StrengthAt(ProductInfo.StrengthIndex(StrengthLevel.Weak))==StrengthLevel.Weak&&(int)StrengthLevel.Strong==2,"legacy enum values and UI choices stay independent");
        var blank=EffectAsset.NewMotion();check(blank.Clips[0].Motion!.Tracks.Count==0&&blank.Clips[0].Motion!.Value(MotionProperty.Opacity,0)==1,"new motion starts fully visible with no sample animation keys");
        var path=Path.Combine(folder,"broadcast-colors.png");using var source=new SKBitmap(160,100);source.Erase(SKColors.Transparent);using(var c=new SKCanvas(source))using(var p=new SKPaint{Color=new SKColor(65,8,42),IsAntialias=true})c.DrawCircle(80,50,30,p);PlaybackChecks.Save(source,path);
        var r=new EffectRegion{Role=RegionRole.Group,Bounds=new(0,0,1,1),Background=BackgroundMode.Transparent};
        using(var scene=new EffectScene(source))
        {
            foreach(var property in new[]{MotionProperty.CropTop,MotionProperty.CropBottom,MotionProperty.CropLeft,MotionProperty.CropRight})
            {
                var motion=new MotionDefinition();motion.SetKey(property,0,.5);r.Clips=[new(){Options=new(){Kind=EffectKind.UserMotion},Motion=motion}];using var frame=scene.Render([r],new(),.3);
                var hidden=property switch{MotionProperty.CropTop=>new SKPointI(80,35),MotionProperty.CropBottom=>new(80,65),MotionProperty.CropLeft=>new(65,50),_=>new(95,50)};var shown=property switch{MotionProperty.CropTop=>new SKPointI(80,65),MotionProperty.CropBottom=>new(80,35),MotionProperty.CropLeft=>new(95,50),_=>new(65,50)};
                check(frame.GetPixel(hidden.X,hidden.Y).Alpha==0&&frame.GetPixel(shown.X,shown.Y).Alpha==255,property+": keyframe crops only the chosen side");
            }
            r.Clips=[new(){Duration=1,Options=new(){Kind=EffectKind.WipeRight}}];using(var frame=scene.Render([r],new(),.2))check(frame.GetPixel(60,50).Alpha==255&&frame.GetPixel(100,50).Alpha==0,"left-to-right entrance reveals by crop without fading visible pixels");
            var state=EffectScene.Evaluate([new(){Options=new(){Kind=EffectKind.FallIn}}],.2,SKRect.Create(160,100));check(state.Y<0,"new top entrance moves downward from above");
            r.Bounds=NormalizedRect.FromPixels(SKRect.Create(50,20,60,60),160,100);r.Clips=[new(){Options=new(){Kind=EffectKind.Ripple,Color="#FF00FFFF"}}];using(var frame=scene.Render([r],new(),.5)){var found=false;for(var y=0;y<100;y++)for(var x=0;x<160;x++){var c=frame.GetPixel(x,y);found|=source.GetPixel(x,y).Alpha==0&&c.Alpha>20&&c.Green>200&&c.Blue>200&&c.Red<20;}check(found,"ripple accepts a custom cyan color outside the object");}r.Bounds=new(0,0,1,1);
            r.Clips=[new(){Options=new(){Kind=EffectKind.Underline,Color="#FFFF00FF"}}];using(var frame=scene.Render([r],new(),1.2))check(frame.GetPixel(80,90).Red>200&&frame.GetPixel(80,90).Blue>200&&frame.GetPixel(80,90).Alpha>0,"underline marker remains beneath the text after its clip");
        }
        r.Clips=[new(){Duration=1,Options=new(){Kind=EffectKind.WipeRight,Strength=StrengthLevel.VeryWeak}},new(){Start=1,Duration=1,Options=new(){Kind=EffectKind.Sweep,Color="#FFFF00FF"}},new(){Start=2,Duration=1,Options=new(){Kind=EffectKind.Underline,Color="#FF00FFFF"}}];
        var motionCrop=new MotionDefinition();motionCrop.SetKey(MotionProperty.CropLeft,0,0);motionCrop.SetKey(MotionProperty.CropLeft,60,.8);r.Clips.Add(new(){Start=3,Duration=2,Options=new(){Kind=EffectKind.UserMotion},Motion=motionCrop});
        var job=new ImageJob{SourcePath=path,Width=160,Height=100,Regions=[r]};var video=new VideoOptions{Fps=30,EndHold=.2};var target=Path.Combine(folder,"broadcast-polish.obs");await new BrowserExporter().ExportAsync(job,video,target,true,null,CancellationToken.None);
        Directory.CreateDirectory(Path.Combine(target,"expected"));using(var scene=new EffectScene(source))foreach(var t in new[]{.2,.6,1.4,2.5,3.5,4.5}){using var frame=scene.Render([r],video,t);PlaybackChecks.Save(frame,Path.Combine(target,"expected",$"{t:0.00}.png"));}
        var doc=Path.Combine(folder,"broadcast-options.json");DocumentStore.Save(doc,new ProjectDocument{Jobs=[job],Video=video});var read=DocumentStore.Load<ProjectDocument>(doc);check(read.Jobs[0].Regions[0].Clips[1].Options.Color=="#FFFF00FF"&&read.Jobs[0].Regions[0].Clips[3].Motion!.Tracks[0].Property==MotionProperty.CropLeft,"color, crop keys and new strengths survive project save/load");
    }
    public static void VisualRepro(string folder,Action<bool,string> check)
    {
        using var source=new SKBitmap(160,100);source.Erase(SKColors.Transparent);using(var c=new SKCanvas(source))using(var p=new SKPaint{Color=SKColors.Crimson,IsAntialias=true})c.DrawRoundRect(SKRect.Create(40,25,80,50),8,8,p);
        var region=new EffectRegion{Bounds=new(0,0,1,1),Role=RegionRole.Group,Background=BackgroundMode.Transparent,Clips=[new(){Duration=1,Options=new(){Kind=EffectKind.Sweep}}]};
        using(var scene=new EffectScene(source))using(var frame=scene.Render([region],new(),.5)){check(frame.GetPixel(80,5).Alpha==0,"sweep keeps transparent PNG padding transparent");PlaybackChecks.Save(frame,Path.Combine(folder,"sweep-padding.png"));}
        using var circle=new SKBitmap(160,100);using(var c=new SKCanvas(circle)){c.Clear(SKColors.White);using var p=new SKPaint{Color=SKColors.Red};c.DrawRect(0,0,80,100,p);p.Color=new SKColor(65,8,42);p.IsAntialias=true;c.DrawCircle(80,50,30,p);}
        region=new(){Bounds=NormalizedRect.FromPixels(SKRect.Create(50,20,60,60),160,100),Shape=RegionShape.Ellipse,Role=RegionRole.Group,Background=BackgroundMode.Auto,Clips=[new(){Duration=.5,Options=new(){Kind=EffectKind.Zoom}},new(){Start=.5,Duration=1,Options=new(){Kind=EffectKind.Ripple}}]};
        using(var scene=new EffectScene(circle))using(var frame=scene.Render([region],new(),.75))
        {var max=0;for(var y=18;y<82;y++)for(var x=48;x<112;x++){var r=Math.Sqrt((x+.5-80)*(x+.5-80)+(y+.5-50)*(y+.5-50));if(r<29||r>31.2)continue;var a=circle.GetPixel(x,y);var b=frame.GetPixel(x,y);max=Math.Max(max,new[]{Math.Abs(a.Red-b.Red),Math.Abs(a.Green-b.Green),Math.Abs(a.Blue-b.Blue),Math.Abs(a.Alpha-b.Alpha)}.Max());}check(max<=2,$"completed zoom then ripple preserves original circle edge (max difference {max})");PlaybackChecks.Save(circle,Path.Combine(folder,"circle-original.png"));PlaybackChecks.Save(frame,Path.Combine(folder,"circle-ripple.png"));}
    }
}
