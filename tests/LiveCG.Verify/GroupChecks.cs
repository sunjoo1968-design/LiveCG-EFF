using System.Diagnostics;
using System.Text.Json;
using LiveCG.Core;
using SkiaSharp;

internal static class GroupChecks
{
    internal static EffectClip Motion(double x=0,double y=0,double scale=1,double angle=0)
    {var m=new MotionDefinition();foreach(var (p,v) in new[]{(MotionProperty.X,x),(MotionProperty.Y,y),(MotionProperty.ScaleX,scale),(MotionProperty.ScaleY,scale),(MotionProperty.Rotation,angle)}){m.SetKey(p,0,v);m.SetKey(p,60,v);}return new(){Duration=2,Options=new(){Kind=EffectKind.UserMotion},Motion=m};}
    public static async Task Run(string folder,Action<bool,string> check)
    {
        void Reject(Action action,string label){try{action();check(false,label);}catch(InvalidDataException){check(true,label);}}
        void Save(SKBitmap b,string path){using var i=SKImage.FromBitmap(b);using var d=i.Encode(SKEncodedImageFormat.Png,100);File.WriteAllBytes(path,d.ToArray());}
        using var source=new SKBitmap(160,100);using(var c=new SKCanvas(source)){c.Clear(SKColors.White);using var p=new SKPaint{Color=SKColors.Blue};c.DrawRect(20,20,100,60,p);p.Color=SKColors.Red;c.DrawRect(50,40,20,20,p);}
        var group=new EffectRegion{Role=RegionRole.Group,Name="쿠폰",Bounds=new(.125,.2,.625,.6),Background=BackgroundMode.Solid,BackgroundColor="#FFFFFF"};
        var child=new EffectRegion{Role=RegionRole.EffectArea,ParentId=group.Id,Name="배지",Bounds=new(.3125,.4,.125,.2),Background=BackgroundMode.Solid,BackgroundColor="#0000FF",Clips=[Motion(2)]};
        var regions=new List<EffectRegion>{child,group};LayerGraph.Validate(regions);
        check(LayerGraph.Rows(regions).Select(r=>r.Region.Id).SequenceEqual(new[]{group.Id,child.Id}),"graph rows show group above its local effect regardless of serialized order");
        using(var scene=new EffectScene(source))
        {
            using var f=scene.Render(regions,new(),.5);check(f.GetPixel(55,50)==SKColors.Blue&&f.GetPixel(95,50)==SKColors.Red,"local group effect replaces the old badge and creates one moved badge");
            check(Enumerable.Range(0,100).Sum(y=>Enumerable.Range(0,160).Count(x=>f.GetPixel(x,y)==SKColors.Red))==400,"local replacement exposes exactly one badge without duplicating its source layer");
            child.Enabled=false;using var disabled=scene.Render(regions,new(),.5);check(disabled.Bytes.SequenceEqual(source.Bytes),"disabling a local effect restores the untouched group including its badge");child.Enabled=true;
            group.Clips=[Motion(.1)];using var inherited=scene.Render(regions,new(),.5);check(inherited.GetPixel(105,50)==SKColors.Red&&inherited.GetPixel(55,50)==SKColors.Blue,"parent translation is applied once after the child replacement");
            group.Enabled=false;using var hidden=scene.Render(regions,new(),.5);check(hidden.GetPixel(105,50)==SKColors.White&&hidden.GetPixel(25,25)==SKColors.White,"hidden group suppresses its local effects and owned source");group.Enabled=true;group.Clips=[];
            child.Clips=[new(){Start=1,Duration=1,Options=new(){Kind=EffectKind.FadeIn}}];using var before=scene.Render(regions,new(),.5);check(before.GetPixel(55,50)==SKColors.Blue,"scheduled local entrance stays hidden until its start");child.Clips=[Motion(2)];
        }
        var mask=new MaskTrack{Name="배지 빼기",Bounds=child.Bounds,Operation=MaskOperation.Subtract,Duration=2};group.Masks.Add(mask);
        using(var r=new MaskRenderer())
        {
            var box=new SKRectI(20,20,120,80);
            using var sub=r.Render(group.Masks,100,60,160,100,box,.5);check(sub.GetPixel(35,30).Alpha==0&&sub.GetPixel(5,5).Alpha==255,"subtract mask uses parent-local coordinates");
            mask.Operation=MaskOperation.Add;using var add=r.Render(group.Masks,100,60,160,100,box,.5);check(add.GetPixel(35,30).Alpha==255&&add.GetPixel(5,5).Alpha==0,"first additive mask initializes transparent coverage");
            mask.Operation=MaskOperation.Intersect;using var intersect=r.Render(group.Masks,100,60,160,100,box,.5);check(intersect.Bytes.SequenceEqual(add.Bytes),"intersection starts with the full parent");
            mask.Invert=true;using var inverted=r.Render(group.Masks,100,60,160,100,box,.5);check(inverted.GetPixel(35,30).Alpha==0&&inverted.GetPixel(5,5).Alpha==255,"inverted mask reverses its coverage");
            mask.Invert=false;mask.Opacity=.5;using var half=r.Render(group.Masks,100,60,160,100,box,.5);check(half.GetPixel(35,30).Alpha is >=126 and <=128,"mask opacity controls alpha rather than darkening its colors");mask.Opacity=1;
            mask.Start=1;using var waiting=r.RenderOne(mask,100,60,160,100,box,.5);check(waiting.GetPixel(35,30).Alpha==0,"mask has zero coverage before its start");
            mask.EndMode=MaskEndMode.Hold;using var hold=r.Render(group.Masks,100,60,160,100,box,4);check(hold.GetPixel(35,30).Alpha==255&&hold.GetPixel(5,5).Alpha==0,"mask holds its final coverage by default");
            mask.EndMode=MaskEndMode.Release;using var release=r.Render(group.Masks,100,60,160,100,box,4);check(release.GetPixel(35,30).Alpha==255&&release.GetPixel(5,5).Alpha==255,"released mask restores full coverage after its end");
            mask.EndMode=MaskEndMode.Zero;using var zero=r.Render(group.Masks,100,60,160,100,box,4);check(zero.GetPixel(35,30).Alpha==0,"zero end mode closes the intersected output");
            mask.Start=0;mask.EndMode=MaskEndMode.Hold;mask.Expand=2;using var expanded=r.RenderOne(mask,100,60,160,100,box,.5);check(expanded.GetPixel(29,30).Alpha>0,"positive expansion grows the mask boundary");
            mask.Expand=-2;using var shrunk=r.RenderOne(mask,100,60,160,100,box,.5);check(shrunk.GetPixel(30,30).Alpha==0&&shrunk.GetPixel(35,30).Alpha==255,"negative expansion contracts the mask by source pixels");
            mask.Expand=0;mask.Feather=2;using var soft=r.RenderOne(mask,100,60,160,100,box,.5);check(soft.GetPixel(29,30).Alpha is >0 and <255,"feather produces fractional edge coverage");
            mask.Feather=0;mask.Motion=new();mask.Motion.SetKey(MotionProperty.X,0,0,KeyEasing.Linear);mask.Motion.SetKey(MotionProperty.X,60,1);using var animated=r.RenderOne(mask,100,60,160,100,box,1);check(animated.GetPixel(35,30).Alpha==0&&animated.GetPixel(55,30).Alpha==255,"mask position keys interpolate within the timeline interval");
            mask.Motion.SetKey(MotionProperty.Opacity,0,.5);mask.Motion.SetKey(MotionProperty.Opacity,60,.5);using var animAlpha=r.RenderOne(mask,100,60,160,100,box,1);check(animAlpha.GetPixel(55,30).Alpha is >=126 and <=128,"mask animation opacity multiplies static opacity");
        }
        mask.Motion=null;mask.Operation=MaskOperation.Subtract;
        using(var scene=new EffectScene(source)){child.Enabled=false;using var f=scene.Render(regions,new(),.5);check(f.GetPixel(55,50)==SKColors.White&&f.GetPixel(25,25)==SKColors.Blue,"explicit group subtract mask cuts only when requested");child.Enabled=true;}
        group.Masks.Clear();child.Masks.Add(mask);using(var scene=new EffectScene(source)){using var f=scene.Render(regions,new(),.5);check(f.GetPixel(55,50)==SKColors.Red,"local subtract mask excludes the child operation instead of cutting its parent source");}child.Masks.Clear();
        group.Masks.Add(mask);var copies=LayerGraph.Copy(regions);check(copies[0].ParentId==copies[1].Id&&copies[1].Id!=group.Id&&copies[1].Masks[0].Id!=mask.Id,"deep graph duplication remaps parent and mask identities");copies[1].Masks[0].Name="copy";check(mask.Name!="copy","group copies isolate mask mutations");
        var snapshot=new ImageJob{Regions=regions}.Snapshot();check(snapshot.Regions[0].ParentId==group.Id&&snapshot.Regions[1].Masks[0].Id==mask.Id,"export snapshots preserve graph identities without aliasing");
        Reject(()=>LayerGraph.Validate([child]),"orphan effect areas are rejected");Reject(()=>LayerGraph.Validate([group,group.Copy(false)]),"duplicate group IDs are rejected");Reject(()=>new MaskTrack{Opacity=2}.Validate(),"invalid mask opacity rejected");Reject(()=>new MaskTrack{Duration=0}.Validate(),"zero mask duration rejected");
        group.Collapsed=true;check(LayerGraph.Rows(regions).Count==1,"collapsed group hides its descendants in the row model");group.Collapsed=false;
        var docFile=Path.Combine(folder,"groups-schema6.json");DocumentStore.Save(docFile,new ProjectDocument{Jobs=[new(){SourcePath="source.png",Width=160,Height=100,Regions=regions}]});var doc=DocumentStore.Load<ProjectDocument>(docFile);check(doc.SchemaVersion==6&&doc.Jobs[0].Regions[0].ParentId==group.Id&&doc.Jobs[0].Regions[1].Masks.Count==1,"schema6 persists group relationships and timeline masks");
        var legacy=Path.Combine(folder,"v05-independent.json");File.WriteAllText(legacy,JsonSerializer.Serialize(new ProjectDocument{SchemaVersion=5,Jobs=[new(){SourcePath="old.png",Width=160,Height=100,Regions=[new(){Name="old1"},new(){Name="old2"}]}]}));var old=DocumentStore.Load<ProjectDocument>(legacy);check(old.SchemaVersion==6&&old.Jobs[0].Regions.All(r=>r.Role==RegionRole.Object&&r.ParentId is null)&&old.Jobs[0].Regions[0].Name=="old1","schema5 migration preserves independent regions and their order");
        mask.Duration=4;check(Math.Abs(TimelineRules.Resolve(regions,new(){EndHold=0}).ActualDuration-4)<.01,"enabled mask end participates in automatic video duration");group.Enabled=false;check(TimelineRules.LastEnd(regions)==0,"hidden group and its child masks do not extend output");group.Enabled=true;group.Masks.Clear();
        foreach(var shape in Enum.GetValues<RegionShape>())
        foreach(var role in new[]{RegionRole.Object,RegionRole.Group})
        {
            using var white=new SKBitmap(96,96);white.Erase(SKColors.White);var region=new EffectRegion{Role=role,Shape=shape,Bounds=new(.25,.25,.5,.5),Background=BackgroundMode.Solid,BackgroundColor="#FFFFFF",Clips=[Motion(.013,.017,1.071,7)]};if(shape is RegionShape.Freehand or RegionShape.Polygon)region.Points=[new(0,0),new(1,0),new(.8,1),new(.1,.8)];
            using var scene=new EffectScene(white);using var f=scene.Render([region],new(),.5);var min=Enumerable.Range(0,96).SelectMany(y=>Enumerable.Range(0,96).Select(x=>f.GetPixel(x,y))).Min(c=>Math.Min(c.Red,Math.Min(c.Green,c.Blue)));
            check(min>=254&&Enumerable.Range(0,96).All(y=>Enumerable.Range(0,96).All(x=>f.GetPixel(x,y).Alpha==255)),shape+" / "+role+": white-on-white transformed edges have no black or alpha seam");
        }
        using(var transparent=new SKBitmap(96,96))
        {
            transparent.Erase(new SKColor(255,255,255,128));var r=new EffectRegion{Role=RegionRole.Group,Bounds=new(.2,.2,.6,.6),Shape=RegionShape.Ellipse,Background=BackgroundMode.Transparent,Clips=[Motion(.2,0,1.1)]};using var scene=new EffectScene(transparent);using var f=scene.Render([r],new(),.5);check(Enumerable.Range(0,96).All(y=>Enumerable.Range(0,96).All(x=>{var c=f.GetPixel(x,y);return c.Alpha==0||c.Red>=250&&c.Green>=250&&c.Blue>=250;})),"premultiplied semitransparent samples preserve white foreground hue");
        }
        foreach(var shape in Enum.GetValues<RegionShape>())
        {
            var color=new SKColor(38,24,65);using var dark=new SKBitmap(96,96);dark.Erase(color);var r=new EffectRegion{Role=RegionRole.Group,Shape=shape,Bounds=new(.2,.2,.6,.6),Background=BackgroundMode.Solid,BackgroundColor="#261841",Clips=[Motion(.017,.013,1.07,9)]};if(shape is RegionShape.Freehand or RegionShape.Polygon)r.Points=[new(0,0),new(1,0),new(.8,1),new(0,.8)];using var scene=new EffectScene(dark);using var f=scene.Render([r],new(),.5);var maxError=Enumerable.Range(0,96).SelectMany(y=>Enumerable.Range(0,96).Select(x=>{var c=f.GetPixel(x,y);return Math.Max(Math.Abs(c.Red-color.Red),Math.Max(Math.Abs(c.Green-color.Green),Math.Abs(c.Blue-color.Blue)));})).Max();Console.WriteLine($"EDGE {shape}: max RGB error {maxError}; samples "+string.Join(", ",Enumerable.Range(0,96).SelectMany(y=>Enumerable.Range(0,96).Select(x=>(x,y,c:f.GetPixel(x,y)))).Where(p=>Math.Abs(p.c.Red-color.Red)>1||Math.Abs(p.c.Blue-color.Blue)>1||Math.Abs(p.c.Green-color.Green)>1).Take(4)));Save(f,Path.Combine(folder,$"edge-{shape}.png"));check(maxError<=2&&Enumerable.Range(0,96).All(y=>Enumerable.Range(0,96).All(x=>f.GetPixel(x,y).Alpha==255)),shape+": dark background stays within 2/255 color quantization without an alpha seam");
        }
        var pricePath=LiveCG.Testing.TestFixtures.Create(folder);var priceJob=LiveCG.Testing.TestFixtures.Demo(pricePath);using(var prices=SKBitmap.Decode(pricePath))
        {
            var priceGroup=new EffectRegion{Role=RegionRole.Group,Bounds=new(0,0,1,1)};var priceArea=priceJob.Regions[3].Copy();priceArea.Role=RegionRole.EffectArea;priceArea.ParentId=priceGroup.Id;priceArea.Clips=[new(){Duration=1,Options=new(){Kind=EffectKind.DigitRoll}}];using var scene=new EffectScene(prices);scene.Validate([priceGroup,priceArea]);using var rolling=scene.Render([priceGroup,priceArea],new(),.3);using var end=scene.Render([priceGroup,priceArea],new(),1.2);check(!rolling.Bytes.SequenceEqual(prices.Bytes)&&end.Bytes.SequenceEqual(prices.Bytes),"group local numeric rolling remains active and settles to exact original");
        }
        group.Clips=[Motion(.1)];child.Clips=[Motion(2)];var liveMask=new MaskTrack{Bounds=new(.125,.2,.625,.6),Duration=2,Operation=MaskOperation.Intersect};group.Masks.Add(liveMask);
        using var alphaSource=new SKBitmap(160,100);using(var ac=new SKCanvas(alphaSource)){ac.Clear(SKColors.Transparent);using var ap=new SKPaint{Color=SKColors.Blue};ac.DrawRect(20,20,100,60,ap);ap.Color=SKColors.Red;ac.DrawRect(50,40,20,20,ap);}group.Background=BackgroundMode.Transparent;group.Masks.Add(new(){Name="명시적 구멍",Bounds=new(.2,.25,.1,.1),Operation=MaskOperation.Subtract,Duration=2});var png=Path.Combine(folder,"group-source.png");Save(alphaSource,png);var output=Path.Combine(folder,"group-masks.webm");await new WebmExporter(RuntimePaths.Ffmpeg).ExportAsync(new(){SourcePath=png,Width=160,Height=100,Regions=regions},new(){Fps=15,EndHold=.2},output,true,null,CancellationToken.None);
        var decoded=Path.Combine(folder,"group-decoded.png");using var p2=new Process{StartInfo=new(RuntimePaths.Ffmpeg){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true}};foreach(var a in new[]{"-v","error","-y","-c:v","libvpx-vp9","-i",output,"-ss","0.5","-frames:v","1",decoded})p2.StartInfo.ArgumentList.Add(a);p2.Start();var error=p2.StandardError.ReadToEndAsync();await p2.WaitForExitAsync();if(p2.ExitCode!=0)throw new IOException(await error);
        using var result=SKBitmap.Decode(decoded);check(result.GetPixel(0,0).Alpha==0&&result.GetPixel(50,30).Alpha==0,"actual grouped WebM preserves transparent canvas and an explicit subtract-mask hole");check(result.Width==160&&result.Height==100&&result.GetPixel(105,50).Red>220&&result.GetPixel(55,50).Blue>220,"actual WebM preserves source size, parent transform and one local badge");
        using var hd=new SKBitmap(1920,1080);hd.Erase(SKColors.White);var hdGroup=new EffectRegion{Role=RegionRole.Group,Bounds=new(0,0,1,1)};var hdArea=new EffectRegion{Role=RegionRole.EffectArea,ParentId=hdGroup.Id,Bounds=new(.3,.3,.2,.2),Shape=RegionShape.Ellipse,Clips=[Motion(.2,0,1.1,5)]};hdArea.Masks.Add(new(){Bounds=new(.25,.25,.4,.4),Feather=2,Duration=2});
        using(var scene=new EffectScene(hd)){using(var warmup=scene.Render([hdGroup,hdArea],new(),.4)){}var clock=Stopwatch.StartNew();for(var i=0;i<3;i++){using var f=scene.Render([hdGroup,hdArea],new(),.5+i*.01);}clock.Stop();File.WriteAllText(Path.Combine(folder,"group-performance.txt"),$"1920x1080 group + local ellipse + feather mask: {clock.Elapsed.TotalMilliseconds/3:0.0} ms/frame, warm cache");check(clock.Elapsed.TotalSeconds<15,"1080p group and feather mask render within a bounded smoke-test interval");}
    }
}
