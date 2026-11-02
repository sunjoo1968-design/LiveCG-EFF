namespace LiveCG.Core;

public static class TimelineRules
{
    public static double Snap(double time, int fps) => Math.Round(time * fps, MidpointRounding.AwayFromZero) / fps;
    public static double LastEnd(IEnumerable<EffectRegion> regions){var list=regions.ToList();return list.Where(r=>LayerGraph.Visible(r,list)).SelectMany(r=>r.Clips.Where(c=>c.Enabled).Select(c=>c.End).Concat(r.Masks.Where(m=>m.Enabled).Select(m=>m.End))).DefaultIfEmpty(0).Max();}
    public static VideoOptions Resolve(ImageJob job, VideoOptions options) => options with {Duration=PlaybackPlan.Create(job,options).Duration};
    public static VideoOptions Resolve(IEnumerable<EffectRegion> regions, VideoOptions options)
    {
        options.Validate();
        var frames = Math.Max(1, (int)Math.Ceiling((LastEnd(regions) + options.EndHold) * options.Fps - 1e-8));
        return options with { Duration = (double)frames / options.Fps };
    }
    public static void SetTiming(EffectClip clip, double start, double duration, int fps)
    {
        var next = clip with { Start = Snap(Math.Max(0, start), fps), Duration = Math.Max(1d / fps, Snap(duration, fps)) };
        next.Validate(); clip.Start = next.Start; clip.Duration = next.Duration;
    }
    public static EffectClip Append(EffectRegion region, EffectOptions options, double duration, int fps)
    {
        var clip = new EffectClip { Options = options.Current(),Motion=options.Kind==EffectKind.UserMotion?MotionDefinition.Example():null,Route=options.Kind==EffectKind.TravelRoute?TravelRoute.Example():null };
        SetTiming(clip, region.Clips.Select(c => c.End).DefaultIfEmpty(0).Max(), duration, fps);
        region.Clips.Add(clip); return clip;
    }

    public static void SetTimingWithFollowers(EffectRegion region, EffectClip clip, double start, double duration, int fps, bool ripple,bool followMagnet=false)
    {
        var next = clip.Copy(false); SetTiming(next, start, duration, fps);
        var delta = next.End - clip.End;
        if(followMagnet){var first=region.Clips.Where(c=>c.Id!=clip.Id&&c.Start>=clip.End-1e-8).OrderBy(c=>c.Start).FirstOrDefault();if(first is not null)delta=next.End-first.Start;}
        var changes = (ripple||followMagnet) && Math.Abs(delta) > 1e-9
            ? region.Clips.Where(c => c.Id != clip.Id && c.Start >= clip.End - 1e-8).Select(c => (Clip: c, Next: c with { Start = c.Start + delta })).ToList()
            : [];
        foreach (var change in changes) change.Next.Validate();
        clip.Start = next.Start; clip.Duration = next.Duration;
        foreach (var change in changes) change.Clip.Start = change.Next.Start;
    }
    public static EffectClip? Split(EffectClip clip,double time,int fps)
    {
        var at=Snap(time,fps);if(at<=clip.Start+1e-8||at>=clip.End-1e-8)return null;
        var duration=clip.SourceDuration??clip.Duration;var left=at-clip.Start;var sequence=clip.SequenceId??Guid.NewGuid();var right=clip.Copy();
        right.Start=at;right.Duration=clip.End-at;right.SourceOffset=clip.SourceOffset+left;right.SourceDuration=duration;right.SequenceId=sequence;right.Validate();
        clip.Duration=left;clip.SourceDuration=duration;clip.SequenceId=sequence;return right;
    }

    public static (double Start, double Duration, double? Guide) Magnet(
        IEnumerable<EffectRegion> regions, EffectClip moving, double start, double duration,
        int resize, int fps, double pixelsPerSecond, double cursor, bool enabled, ISet<Guid>? excluded = null)
    {
        start = Snap(Math.Max(0, start), fps); duration = Math.Max(1d / fps, Snap(duration, fps));
        if (!enabled) return (start, duration, null);
        var targets = regions.SelectMany(r => r.Clips).Where(c => c.Id != moving.Id && excluded?.Contains(c.Id) != true)
            .SelectMany(c => new[] { c.Start, c.End }).Append(0).Append(cursor)
            .Select(t => Snap(t, fps)).Distinct().Order().ToArray();
        var edges = resize == 0 ? new[] { start, start + duration } : new[] { resize < 0 ? start : start + duration };
        var best = 8d / pixelsPerSecond + 1e-9; double? guide = null; var shift = 0d;
        foreach (var edge in edges) foreach (var target in targets)
        {
            var distance = Math.Abs(target - edge);
            if (distance >= best || (resize == 0 && start + target - edge < 0) ||
                (resize < 0 && target >= start + duration) || (resize > 0 && target <= start)) continue;
            best = distance; guide = target; shift = target - edge;
        }
        if (guide is null) return (start, duration, null);
        if (resize == 0) start += shift;
        else if (resize > 0) duration += shift;
        else { start += shift; duration -= shift; }
        return (start, duration, guide);
    }
}
