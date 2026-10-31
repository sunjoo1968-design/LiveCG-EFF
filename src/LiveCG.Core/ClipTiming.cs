namespace LiveCG.Core;

public static class ClipTiming
{
    public static double Progress(EffectClip clip, double time)
    {
        var elapsed=Math.Clamp(time-clip.Start,0,clip.Duration)+clip.SourceOffset;
        var total=clip.SourceDuration??clip.Duration;
        if(elapsed>=total-1e-10)return 1;
        if (!clip.Loop) return Math.Clamp(elapsed/total,0,1);
        var period = Math.Max(.016, clip.LoopPeriod);
        var cycle = elapsed / period;
        var fraction = cycle - Math.Floor(cycle + 1e-10);
        return Math.Max(0, fraction);
    }
    public static List<EffectClip> EffectiveClips(IEnumerable<EffectClip> clips,double time)
    {
        var list=clips.Where(c=>c.Enabled).OrderBy(c=>c.Start).ToList();var chosen=list.Where(c=>c.SequenceId is not null).GroupBy(c=>c.SequenceId).ToDictionary(g=>g.Key!.Value,g=>g.LastOrDefault(c=>c.Start<=time)??g.First());
        return list.Where(c=>c.SequenceId is null||chosen[c.SequenceId.Value].Id==c.Id).ToList();
    }
}
