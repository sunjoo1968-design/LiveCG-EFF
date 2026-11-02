using System.Text.Json;
using LiveCG.Core;
using SkiaSharp;

internal static class WorkflowChecks
{
    public static void Run(string folder,Action<bool,string> check)
    {
        var path=Path.Combine(folder,"타임라인 예제.png");using var source=SKBitmap.Decode(path)!;
        var price=LiveCG.Testing.TestFixtures.Demo(path).Regions[3];
        using(var scene=new EffectScene(source))
        foreach(var kind in Enum.GetValues<EffectKind>().Where(k=>k!=EffectKind.UserMotion&&k!=EffectKind.TravelRoute))
        {
            var region=price with{Clips=[new(){Duration=3,Loop=true,LoopPeriod=1,Options=new(){Kind=kind}}]};
            using var a=scene.Render([region],new(),.3);using var b=scene.Render([region],new(),1.3);
            check(a.Bytes.SequenceEqual(b.Bytes),kind+": loop repeats identical phase inside fixed clip");
            using var end=scene.Render([region],new(),3.1);
            if(!ProductInfo.HoldsDecoration(kind))check(end.Bytes.SequenceEqual(source.Bytes),kind+": loop ends on original image");
        }
        var looping=new EffectClip{Start=1,Duration=6,Loop=true,LoopPeriod=1};
        check(ClipTiming.Progress(looping,2)==0&&ClipTiming.Progress(looping,7)==1,"loop cycle boundary and final endpoint are distinct");
        check(TimelineRules.Resolve([new(){Clips=[looping]}],new(){EndHold=0}).Duration==7,"loop never expands the timeline duration");
        var allShapes=Enum.GetValues<RegionShape>().Where(s=>s is not RegionShape.Freehand and not RegionShape.Polygon).ToArray();
        using(var pattern=new SKBitmap(120,100))
        {
            for(var y=0;y<100;y++)for(var x=0;x<120;x++)pattern.SetPixel(x,y,(x+y)%2==0?SKColors.Blue:SKColors.Green);
            using var scene=new EffectScene(pattern);
            foreach(var shape in allShapes)
            {
                var region=new EffectRegion{Shape=shape,Bounds=new(.2,.2,.6,.6),Background=BackgroundMode.Solid,BackgroundColor="#FFFFFF",Clips=[new(){Start=1,Options=new(){Kind=EffectKind.FadeIn}}]};
                using var hidden=scene.Render([region],new(),0);using var settled=scene.Render([region],new(),2.1);
                check(hidden.GetPixel(60,50)==SKColors.White,shape+": hidden entrance erases inside mask");
                if(shape!=RegionShape.Rectangle)check(hidden.GetPixel(25,21)==pattern.GetPixel(25,21),shape+": bounding-box corner outside mask remains untouched");
                check(settled.Bytes.SequenceEqual(pattern.Bytes),shape+": settled mask preserves all original pixels");
                check(RegionMask.Contains(region,new(60,50),120,100),shape+": hit test includes center");
                if(shape!=RegionShape.Rectangle)check(!RegionMask.Contains(region,new(25,21),120,100),shape+": hit test excludes corner");
            }
        }
        using(var blank=new SKBitmap(640,640))
        {
            blank.Erase(SKColors.Transparent);using var scene=new EffectScene(blank);
            var region=new EffectRegion{Bounds=new(230d/640,295d/640,180d/640,50d/640),Clips=[new(){Options=new(){Kind=EffectKind.Ripple,Strength=StrengthLevel.Strong}}]};
            using var frame=scene.Render([region],new(),.35);
            var minX=640;var maxX=0;var minY=640;var maxY=0;
            for(var y=0;y<640;y++)for(var x=0;x<640;x++)if(frame.GetPixel(x,y).Alpha>5){minX=Math.Min(minX,x);maxX=Math.Max(maxX,x);minY=Math.Min(minY,y);maxY=Math.Max(maxY,y);}
            check(Math.Abs((maxX-minX)-(maxY-minY))<=1&&maxX>minX,"ripple is a complete circle for a wide rectangle");
            var runs=new List<int>();var length=0;
            for(var x=0;x<640;x++){if(frame.GetPixel(x,320).Alpha>5)length++;else if(length>0){runs.Add(length);length=0;}}
            check(runs.Count==2&&runs.All(n=>n>=6),"ripple has one ring with a thick line");
            using var data=frame.Encode(SKEncodedImageFormat.Png,100);File.WriteAllBytes(Path.Combine(folder,"single-circle.png"),data.ToArray());
        }
        var clip=new EffectClip{Start=1,Duration=1};var following=new EffectClip{Start=2.5,Duration=1};var overlap=new EffectClip{Start=1.8,Duration=1};
        var track=new EffectRegion{Clips=[clip,following,overlap]};
        TimelineRules.SetTimingWithFollowers(track,clip,1,2,30,true);
        check(clip.Duration==2&&following.Start==3.5&&overlap.Start==1.8,"ripple edit preserves gap and intentionally overlapping clips");
        TimelineRules.SetTimingWithFollowers(track,clip,1,.5,30,true);
        check(following.Start==2&&overlap.Start==1.8,"shortening shifts only the old following sequence");
        var late=new EffectClip{Start=3599,Duration=.5};track.Clips.Add(late);
        var old=track.Clips.Select(c=>(c.Start,c.Duration)).ToArray();var rejected=false;
        try{TimelineRules.SetTimingWithFollowers(track,clip,1,300,30,true);}catch(InvalidDataException){rejected=true;}
        check(rejected&&old.SequenceEqual(track.Clips.Select(c=>(c.Start,c.Duration))),"out-of-range ripple edit rolls back every clip atomically");
        var other=new EffectRegion{Clips=[new(){Start=4,Duration=1}]};
        var magnet=TimelineRules.Magnet([track,other],clip,4.04,1,0,30,100,10,true);
        check(magnet.Start==4&&magnet.Guide==4,"magnet catches another region start within screen tolerance");
        magnet=TimelineRules.Magnet([other],clip,2.96,1,0,30,100,10,true);
        check(Math.Abs(magnet.Start-3)<1e-8&&magnet.Guide==4,"magnet aligns moving clip end to another start");
        magnet=TimelineRules.Magnet([other],clip,4.2,1,0,30,100,10,true);
        check(magnet.Guide is null,"magnet does not attract outside eight pixels");
        magnet=TimelineRules.Magnet([other],clip,4.04,1,0,30,100,10,false);
        check(magnet.Guide is null&&magnet.Start!=4,"disabled magnet retains frame snapping only");
        magnet=TimelineRules.Magnet([],clip,6.97,1,1,30,100,8,true);
        check(magnet.Guide==8&&Math.Abs(magnet.Start+magnet.Duration-8)<1e-8,"trim end snaps to playhead");
        var document=new ProjectDocument{Jobs=[new(){SourcePath=path,Width=960,Height=540,Regions=[price with{Shape=RegionShape.Ellipse,Clips=[looping]}]}]};
        var saved=Path.Combine(folder,"workflow.livecg.json");DocumentStore.Save(saved,document);var read=DocumentStore.Load<ProjectDocument>(saved);
        check(read.SchemaVersion==6&&read.Jobs[0].Regions[0].Shape==RegionShape.Ellipse&&read.Jobs[0].Regions[0].Clips[0].Loop,"schema6 round trip retains mask and per-clip loop");
        File.WriteAllText(saved,JsonSerializer.Serialize(document).Replace("\"SchemaVersion\":6","\"SchemaVersion\":2"));
        check(DocumentStore.Load<ProjectDocument>(saved).SchemaVersion==6,"schema2 upgrades without losing timeline");
        var defaults=new AppDefaults{Loop=true,LoopPeriod=.5,Editor=new(){DrawKey="D",SelectKey="S",Snap=false,Ripple=false}};
        DocumentStore.Save(saved,defaults);var restored=DocumentStore.Load<AppDefaults>(saved);
        check(restored.Loop&&restored.LoopPeriod==.5&&restored.Editor.DrawKey=="D"&&!restored.Editor.Snap,"defaults retain looping and editor shortcut preferences");
        rejected=false;try{new EditorPreferences{DrawKey="R",SelectKey="R"}.Validate();}catch(InvalidDataException){rejected=true;}
        check(rejected,"duplicate region shortcuts rejected");
        using(var scene=new EffectScene(source))
        {
            foreach(var shape in new[]{RegionShape.Ellipse,RegionShape.RoundedRectangle})
            {
                var region=price with{Shape=shape,Clips=[new(){Options=new(){Kind=EffectKind.DigitRoll}}]};
                using var frame=scene.Render([region],new(),.3);using var mask=RegionMask.Create(shape,region.Bounds.Pixels(960,540));
                var stable=true;var bounds=region.Bounds.Pixels(960,540);
                for(var y=(int)bounds.Top;y<bounds.Bottom;y++)for(var x=(int)bounds.Left;x<bounds.Right;x++)
                    if(!mask.Contains(x-2,y-2)&&!mask.Contains(x+2,y+2)&&!mask.Contains(x-2,y+2)&&!mask.Contains(x+2,y-2)&&frame.GetPixel(x,y)!=source.GetPixel(x,y))stable=false;
                check(stable,shape+": fixed numeric rolling never writes outside its mask");
            }
            var first=price.Copy();first.Clips=[new(){Options=new(){Kind=EffectKind.Zoom}}];
            var later=price.Copy();later.Background=BackgroundMode.Solid;later.BackgroundColor="#701A2E";later.Clips=[new(){Start=2,Options=new(){Kind=EffectKind.FadeIn}}];
            using var overlapFrame=scene.Render([later,first],new(),1.5);
            check(overlapFrame.GetPixel(500,440)==new SKColor(112,26,46),"later hidden region remains hidden over an earlier restored region");
        }
        var anchorTrack=new EffectRegion{Clips=[new(){Start=1.5,Duration=1}]};
        magnet=TimelineRules.Magnet([anchorTrack],clip,0,1.46,1,30,100,10,true,anchorTrack.Clips.Select(c=>c.Id).ToHashSet());
        check(magnet.Guide is null,"ripple followers can be excluded from moving snap anchors");
    }
}

