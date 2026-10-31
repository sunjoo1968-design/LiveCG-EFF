using System.Text.Json.Serialization;

namespace LiveCG.Core;
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ClipSection { Auto, Intro, Loop, Exit }
public static class SectionRules
{
    public static IReadOnlyList<EffectRegion> RenderRegions(ImageJob job)
    {if(job.Playback.ExitEnabled!=false||!job.Regions.Any(r=>r.Clips.Any(c=>c.Section==ClipSection.Exit)))return job.Regions;return job.Regions.Select(r=>r with{Clips=r.Clips.Select(c=>c.Section==ClipSection.Exit?c with{Enabled=false}:c).ToList()}).ToList();}
    public static ClipSection Effective(EffectClip c)=>c.Section!=ClipSection.Auto?c.Section:ProductInfo.IsEntrance(c.Options.Kind)?ClipSection.Intro:ClipSection.Loop;
    public static string Name(ClipSection section)=>section switch{ClipSection.Intro=>"등장",ClipSection.Loop=>"반복",ClipSection.Exit=>"퇴장",_=>"자동"};
    public static PlaybackSections Resolve(ImageJob job,VideoOptions video)
    {
        var p=job.Playback with{};if(!p.ByEffects)return p;
        var clips=job.Regions.Where(r=>LayerGraph.Visible(r,job.Regions)).SelectMany(r=>r.Clips).Where(c=>c.Enabled).ToList();
        p.IntroEnd=TimelineRules.Snap(clips.Where(c=>Effective(c)==ClipSection.Intro).Select(c=>c.End).DefaultIfEmpty(0).Max(),video.Fps);
        p.LoopEnd=Math.Max(p.IntroEnd+1d/video.Fps,TimelineRules.Snap(clips.Where(c=>Effective(c)==ClipSection.Loop).Select(c=>c.End).DefaultIfEmpty(p.IntroEnd+1).Max(),video.Fps));
        if(p.ExitEnabled==true)p.ExitDuration=Math.Clamp(clips.Where(c=>Effective(c)==ClipSection.Exit).Select(c=>c.End-p.LoopEnd).DefaultIfEmpty(p.ExitDuration).Max(),0,30);
        return p;
    }
    public static List<EffectClip> Crossing(ImageJob job,VideoOptions video)
    {var p=Resolve(job,video);return job.Regions.SelectMany(r=>r.Clips).Where(c=>c.Enabled&&((c.Start<p.IntroEnd-1e-6&&c.End>p.IntroEnd+1e-6)||(c.Start<p.LoopEnd-1e-6&&c.End>p.LoopEnd+1e-6))).ToList();}
    public static int SplitBoundaries(ImageJob job,VideoOptions video)
    {
        var p=Resolve(job,video);var count=0;
        foreach(var r in job.Regions)foreach(var edge in new[]{p.IntroEnd,p.LoopEnd})foreach(var c in r.Clips.ToList())
        {var right=TimelineRules.Split(c,edge,video.Fps);if(right is null)continue;c.Section=edge==p.IntroEnd?ClipSection.Intro:ClipSection.Loop;right.Section=edge==p.IntroEnd?ClipSection.Loop:ClipSection.Exit;r.Clips.Add(right);count++;}
        return count;
    }
    public static void Arrange(ImageJob job,int fps)
    {
        double cursor=0;
        foreach(var section in new[]{ClipSection.Intro,ClipSection.Loop,ClipSection.Exit})
        {var clips=job.Regions.SelectMany(r=>r.Clips).Where(c=>c.Enabled&&Effective(c)==section).ToList();if(clips.Count==0)continue;var first=clips.Min(c=>c.Start);foreach(var c in clips)c.Start=TimelineRules.Snap(cursor+c.Start-first,fps);cursor=clips.Max(c=>c.End);}
    }
}
