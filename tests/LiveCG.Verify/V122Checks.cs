using LiveCG.Core;
using SkiaSharp;
internal static class V122Checks
{
    public static void Repro(string folder,Action<bool,string> check)
    {
        var dir=Path.Combine(folder,"v122");Directory.CreateDirectory(dir);using var background=new SKBitmap(360,110);for(var y=0;y<110;y++)for(var x=0;x<360;x++)background.SetPixel(x,y,(x/7+y/7)%2==0?new(125,20,32):new(145,26,39));
        using var ink=new SKBitmap(360,110);ink.Erase(SKColors.Transparent);using(var c=new SKCanvas(ink))using(var p=new SKPaint{Color=SKColors.White,IsAntialias=true})using(var font=new SKFont(SKTypeface.FromFamilyName("Arial",SKFontStyle.Bold),90)){for(var i=0;i<4;i++)c.DrawText("8",12+i*90,90,font,p);}
        using var source=background.Copy();using(var c=new SKCanvas(source))c.DrawBitmap(ink,0,0);
        var clip=new EffectClip{Id=Guid.Parse("12200000-1111-2222-3333-444444444444"),Duration=1,Options=new(){Kind=EffectKind.DigitRoll}};
        var region=new EffectRegion{Bounds=new(0,0,1,1),ContentOnly=true,SeparationTarget=SeparationTarget.Text,Background=BackgroundMode.Solid,BackgroundColor="#7D1420",TextInkColor="#FFFFFF",TextSettings=new(){RangeMode=true,BackgroundColors=["#7D1420","#911A27"],ColorTolerance=24},DigitDividers=[.25,.5,.75],Clips=[clip]};
        using var matte=TextSeparation.Extract(source,new(125,20,32),SKColors.White,region.TextSettings);using var separated=matte.Foreground.Copy();using(var mask=matte.Mask.Bitmap())RegionRaster.Multiply(separated,mask);SourceImage.SavePng(source,Path.Combine(dir,"original.png"));SourceImage.SavePng(separated,Path.Combine(dir,"separated.png"));
        var referenceRegion=region.Copy(false);referenceRegion.ContentOnly=false;referenceRegion.Background=BackgroundMode.Transparent;referenceRegion.Cutout=null;
        using var referenceScene=new EffectScene(ink);using var reference=referenceScene.Render([referenceRegion],new(),.2);
        foreach(var saved in new[]{false,true})
        {
            if(saved){var cutout=matte.Mask.Encode();cutout.ForegroundPng=ObjectExtractor.EncodeBitmap(matte.Foreground);region.Cutout=cutout;}
            using var scene=new EffectScene(source);using var actual=scene.Render([region],new(),.2);var wanted=0;var missing=0;
            for(var y=0;y<110;y++)for(var x=0;x<360;x++){var r=reference.GetPixel(x,y);if(r.Alpha<230||r.Red<230)continue;wanted++;var a=actual.GetPixel(x,y);if(a.Red<210||a.Green<210||a.Blue<210)missing++;}
            Console.WriteLine($"ROLL {(saved?"saved":"automatic")}: {missing}/{wanted} reference ink pixels missing");check(wanted>100&&missing<wanted*.02,(saved?"saved precision":"automatic range")+" separated rolling digits keep complete moving glyphs instead of clipping against old glyph outlines");SourceImage.SavePng(actual,Path.Combine(dir,saved?"roll-saved.png":"roll-automatic.png"));
        }
        SourceImage.SavePng(reference,Path.Combine(dir,"reference.png"));
        using var ramp=new SKBitmap(256,12);ramp.Erase(SKColors.Black);for(var x=1;x<256;x++)for(var y=2;y<10;y++)ramp.SetPixel(x,y,new((byte)x,(byte)x,(byte)x));using(var protectedText=TextSeparation.Extract(ramp,SKColors.Black,null,new(){RangeMode=true,ColorTolerance=36}))check(Enumerable.Range(1,255).All(x=>protectedText.Mask.Alpha[5*256+x]>0),"range mode without a manually picked ink color preserves every antialiased digit edge instead of thinning it with background tolerance");
        using var transparent=new SKBitmap(60,30);transparent.Erase(SKColors.Transparent);using(var c=new SKCanvas(transparent))using(var p=new SKPaint{Color=SKColors.Black,IsAntialias=true})c.DrawCircle(20,15,10,p);using(var t=TextSeparation.Extract(transparent,SKColors.Transparent,null,new(){RangeMode=true}))check(t.Mask.Alpha[15*60+20]==255&&t.Mask.Alpha[0]==0,"transparent PNG keeps opaque black number ink instead of treating transparent black as a removal color");
    }
    public static async Task Run(string folder,Action<bool,string> check)
    {
        Repro(folder,check);var dir=Path.Combine(folder,"v122");
        foreach(var dark in new[]{false,true})
        {
            var bg=dark?SKColors.White:new SKColor(125,20,32);var other=dark?new SKColor(249,240,230):new SKColor(145,26,39);var ink=dark?new SKColor(100,38,28):SKColors.White;using var pattern=new SKBitmap(400,140);using var text=new SKBitmap(400,140);text.Erase(SKColors.Transparent);for(var y=0;y<140;y++)for(var x=0;x<400;x++)pattern.SetPixel(x,y,(x/5+y/5)%2==0?bg:other);
            using(var c=new SKCanvas(text))using(var paint=new SKPaint{Color=ink,IsAntialias=true}){using var font=new SKFont(SKTypeface.FromFamilyName("Arial",SKFontStyle.Bold),65);c.DrawText("13,470  23%",3,70,font,paint);paint.Style=SKPaintStyle.Stroke;paint.StrokeWidth=1;c.DrawLine(15.5f,90,15.5f,130,paint);c.DrawCircle(110,109,15,paint);}
            using var source=pattern.Copy();using(var c=new SKCanvas(source))c.DrawBitmap(text,0,0);var settings=new TextSeparationSettings{RangeMode=true,BackgroundColors=[Color(bg),Color(other)],ColorTolerance=65};using var matte=TextSeparation.Extract(source,bg,null,settings);using var foreground=matte.Foreground.Copy();using(var mask=matte.Mask.Bitmap())RegionRaster.Multiply(foreground,mask);using var restored=pattern.Copy();using(var c=new SKCanvas(restored))c.DrawBitmap(foreground,0,0);
            var preserved=true;var clean=true;var error=0;for(var y=0;y<140;y++)for(var x=0;x<400;x++){var actual=source.GetPixel(x,y);var baseColor=pattern.GetPixel(x,y);var a=matte.Mask.Alpha[y*400+x];if(actual!=baseColor&&a==0)preserved=false;if(actual==baseColor&&a!=0)clean=false;var b=restored.GetPixel(x,y);error=Math.Max(error,Math.Max(Math.Abs(b.Red-actual.Red),Math.Max(Math.Abs(b.Green-actual.Green),Math.Abs(b.Blue-actual.Blue))));}
            check(preserved,(dark?"dark":"white")+" price, comma, discount and one-pixel strokes retain every visible source pixel");check(clean,(dark?"light":"textured")+" multi-color background including closed counters is removed without erosion");check(error<=2,"numeric separation recomposes original "+(dark?"dark":"white")+" glyph thickness within two channel bytes");
        }
        using var original=SKBitmap.Decode(Path.Combine(dir,"original.png"));using var matteSaved=TextSeparation.Extract(original,new(125,20,32),SKColors.White,new(){RangeMode=true,BackgroundColors=["#7D1420","#911A27"]});var cutout=matteSaved.Mask.Encode();cutout.ForegroundPng=ObjectExtractor.EncodeBitmap(matteSaved.Foreground);
        var region=new EffectRegion{Bounds=new(0,0,1,1),ContentOnly=true,SeparationTarget=SeparationTarget.Text,TextInkColor="#FFFFFF",TextSettings=new(){RangeMode=true,BackgroundColors=["#7D1420","#911A27"]},Cutout=cutout,Background=BackgroundMode.Solid,BackgroundColor="#7D1420",DigitDividers=[.25,.5,.75],Clips=[new(){Duration=1,Options=new(){Kind=EffectKind.DigitRoll}}]};
        await Fixture("numeric-saved.obs",original,[region],[.2,.6,1,1.2]);var masked=region.Copy();masked.Masks=[new(){Bounds=new(.5,0,.5,1),Operation=MaskOperation.Subtract,Start=0,Duration=2,EndMode=MaskEndMode.Release}];
        using(var scene=new EffectScene(original))using(var frame=scene.Render([masked],new(),.2))check(Enumerable.Range(0,100).All(y=>frame.GetPixel(300,y).Green<210),"intentional timeline masks still suppress rolling glyphs after extraction-mask fix");await Fixture("numeric-mask.obs",original,[masked],[.2,.6]);
        var parent=new EffectRegion{Bounds=new(0,0,1,1),Role=RegionRole.Group,Background=BackgroundMode.Solid,BackgroundColor="#7D1420"};var child=region.Copy();child.Role=RegionRole.EffectArea;child.ParentId=parent.Id;await Fixture("numeric-child.obs",original,[child,parent],[.2,.6]);
        var bounce=region.Copy();bounce.Clips[0].Options.Kind=EffectKind.Bounce;await Fixture("numeric-bounce.obs",original,[bounce],[.2,.6]);
        using var map=new SKBitmap(640,400);map.Erase(new(20,46,72));var route=TravelRoute.Example();var geometry=RouteRenderer.Geometry(route,640,400);
        foreach(var (time,on) in new[]{(0d,true),(.5,true),(1d,false),(2d,false),(3d,true),(3.5,true),(4d,false),(6d,true),(6.5,true),(7d,false),(8d,false)})check(geometry.EmphasizeStop(route,geometry.Sample(route,time),time)==on,"all-waypoint emphasis is "+on+" at "+time+" seconds");
        var line=new EffectRegion{Bounds=new(0,0,1,1),OverlayOnly=true,Background=BackgroundMode.KeepOriginal,Clips=[new(){Duration=route.Duration,Route=route,Options=new(){Kind=EffectKind.TravelRoute}}]};await Fixture("route-Stops.obs",map,[line],[0,.5,1,3,3.5,4,6,7]);route.Points[0].Stop=route.Points[2].Stop=0;var zero=RouteRenderer.Geometry(route,640,400);check(!zero.EmphasizeStop(route,zero.Sample(route,0),0)&&zero.EmphasizeStop(route,zero.Sample(route,2),2)&&!zero.EmphasizeStop(route,zero.Sample(route,5),5),"zero-stop departure and arrival never flash emphasis while intermediate dwell still does");
        async Task Fixture(string name,SKBitmap image,List<EffectRegion> regions,double[] times)
        {
            var imagePath=Path.Combine(dir,name+".png");SourceImage.SavePng(image,imagePath);var job=new ImageJob{SourcePath=imagePath,Width=image.Width,Height=image.Height,Regions=regions};var destination=Path.Combine(folder,name);await new BrowserExporter().ExportAsync(job,new(){Fps=30,EndHold=0},destination,true,null,CancellationToken.None);Directory.CreateDirectory(Path.Combine(destination,"expected"));using var scene=new EffectScene(image);foreach(var t in times){using var frame=scene.Render(regions,new(){Fps=30},t);SourceImage.SavePng(frame,Path.Combine(destination,"expected",t.ToString("0.00",System.Globalization.CultureInfo.InvariantCulture)+".png"));}check(File.ReadAllText(Path.Combine(destination,"data.js")).Contains("numericShape"),name+" exports separate geometric coverage for dynamic digits");
        }
        static string Color(SKColor c)=>$"#{c.Red:X2}{c.Green:X2}{c.Blue:X2}";
    }
}
