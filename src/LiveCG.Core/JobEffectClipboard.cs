namespace LiveCG.Core;

public sealed record RegionEffectSlot(string Name, RegionRole Role, bool HasParent, IReadOnlyList<EffectClip> Clips);

public sealed record PasteApplyResult(bool Applied, bool CreatedRegions);

public sealed class JobEffectBundle
{
    public Guid SourceBroadcastId { get; }
    public string SourceFileName { get; }
    public PlaybackSections Playback { get; }
    public IReadOnlyList<RegionEffectSlot> Slots { get; }
    private readonly List<EffectRegion> _templateRegions;
    private readonly Guid[] _sourceClipSequence;

    private JobEffectBundle(
        Guid sourceBroadcastId,
        string sourceFileName,
        PlaybackSections playback,
        IReadOnlyList<RegionEffectSlot> slots,
        List<EffectRegion> templateRegions,
        Guid[] sourceClipSequence)
    {
        SourceBroadcastId = sourceBroadcastId;
        SourceFileName = sourceFileName;
        Playback = playback;
        Slots = slots;
        _templateRegions = templateRegions;
        _sourceClipSequence = sourceClipSequence;
    }

    public int ClipCount => Slots.Sum(slot => slot.Clips.Count);
    public int AnimatedRegionCount => Slots.Count(slot => slot.Clips.Count > 0);

    public static bool CanCopy(ImageJob? job) =>
        job is not null && job.Regions.Any(region => region.Clips.Count > 0);

    public static JobEffectBundle? FromJob(ImageJob? job)
    {
        if (!CanCopy(job)) return null;
        var ordered = OrderedRegions(job!.Regions);
        var slots = ordered
            .Select(region => new RegionEffectSlot(region.Name, region.Role, region.ParentId is not null, CloneClips(region.Clips)))
            .ToList();
        var templates = LayerGraph.Copy(job.Regions, newIds: true);
        foreach (var region in templates)
        {
            region.Cutout = null;
            region.DigitDividers = [];
            region.Clips = CloneClips(region.Clips);
        }
        var sequence = ordered.SelectMany(region => region.Clips).Select(clip => clip.Id).ToArray();
        return new JobEffectBundle(job.BroadcastId, job.FileName, job.Playback with { }, slots, templates, sequence);
    }

    public bool IsUnchangedSource(ImageJob job) =>
        job.BroadcastId == SourceBroadcastId &&
        OrderedRegions(job.Regions).SelectMany(region => region.Clips).Select(clip => clip.Id).SequenceEqual(_sourceClipSequence);

    public bool ApplyTo(ImageJob? target) => ApplyDetailed(target).Applied;

    public PasteApplyResult ApplyDetailed(ImageJob? target)
    {
        if (target is null) return new(false, false);

        var nextPlayback = Playback with { };
        nextPlayback.Validate();

        if (target.Regions.Count == 0)
        {
            var created = LayerGraph.Copy(_templateRegions, newIds: true);
            if (created.Count == 0) return new(false, false);
            foreach (var region in created)
            {
                region.Cutout = null;
                region.DigitDividers = [];
                region.Clips = CloneClips(region.Clips);
                region.Validate();
            }
            LayerGraph.Validate(created);
            target.Regions = created;
            target.Playback = nextPlayback;
            return new(true, true);
        }

        var targetRegions = OrderedRegions(target.Regions);
        var onlyChildAnimatedOnSource = AnimatedRegionCount == 1 &&
                                        Slots.Any(slot => slot.HasParent && slot.Clips.Count > 0) &&
                                        targetRegions.All(region => region.ParentId is null);
        var candidates = (Slots.Count > targetRegions.Count && Slots.Any(slot => slot.Clips.Count == 0)) || onlyChildAnimatedOnSource
            ? Slots.Where(slot => slot.Clips.Count > 0).ToList()
            : Slots.ToList();
        if (candidates.Count == 0) return new(false, false);

        var assigned = new Dictionary<Guid, List<EffectClip>>(target.Regions.Count);
        var usedCandidate = new bool[candidates.Count];

        foreach (var targetRegion in targetRegions)
        {
            if (IsDefaultRegionName(targetRegion.Name)) continue;
            var matchIndex = -1;
            for (var i = 0; i < candidates.Count; i++)
            {
                if (usedCandidate[i] || IsDefaultRegionName(candidates[i].Name)) continue;
                if (string.Equals(candidates[i].Name, targetRegion.Name, StringComparison.OrdinalIgnoreCase))
                {
                    matchIndex = i;
                    break;
                }
            }
            if (matchIndex >= 0)
            {
                usedCandidate[matchIndex] = true;
                assigned[targetRegion.Id] = CloneClips(candidates[matchIndex].Clips);
            }
        }

        if (candidates.Count == 1 && assigned.Count == 0)
        {
            var single = candidates[0];
            var hasRoute = single.Clips.Any(clip => clip.Options.Kind == EffectKind.TravelRoute);
            if (targetRegions.Count == 1 || hasRoute)
            {
                var chosen = targetRegions.FirstOrDefault(region => region.Id != target.BlurFocus?.BackgroundId) ?? targetRegions[0];
                assigned[chosen.Id] = CloneClips(single.Clips);
            }
            else
            {
                var priorAnimatedChildren = targetRegions
                    .Where(region => region.ParentId is not null && region.Clips.Count > 0)
                    .ToList();
                var rootAnimated = targetRegions.Any(region => region.ParentId is null && region.Clips.Count > 0);
                if (priorAnimatedChildren.Count > 0 && !rootAnimated)
                {
                    foreach (var child in priorAnimatedChildren)
                        assigned[child.Id] = CloneClips(single.Clips);
                }
                else
                {
                    var eligibleRoots = targetRegions
                        .Where(region => region.ParentId is null &&
                                         region.Id != target.BlurFocus?.BackgroundId &&
                                         !(region.Masks.Count > 0 && region.Clips.Count == 0))
                        .ToList();
                    if (eligibleRoots.Count == 0)
                        eligibleRoots.Add(targetRegions.FirstOrDefault(region => region.Id != target.BlurFocus?.BackgroundId) ?? targetRegions[0]);
                    foreach (var root in eligibleRoots)
                        assigned[root.Id] = CloneClips(single.Clips);
                }
            }
        }
        else
        {
            var nextIndex = 0;
            foreach (var targetRegion in targetRegions)
            {
                if (assigned.ContainsKey(targetRegion.Id)) continue;
                if (targetRegion.Id == target.BlurFocus?.BackgroundId)
                {
                    var remainingCandidates = 0;
                    for (var i = nextIndex; i < candidates.Count; i++)
                        if (!usedCandidate[i]) remainingCandidates++;
                    var remainingNonBgTargets = targetRegions.Count(region => !assigned.ContainsKey(region.Id) && region.Id != target.BlurFocus?.BackgroundId);
                    if (remainingCandidates <= remainingNonBgTargets)
                    {
                        assigned[targetRegion.Id] = [];
                        continue;
                    }
                }
                while (nextIndex < candidates.Count && usedCandidate[nextIndex]) nextIndex++;
                if (nextIndex < candidates.Count)
                {
                    usedCandidate[nextIndex] = true;
                    assigned[targetRegion.Id] = CloneClips(candidates[nextIndex].Clips);
                    nextIndex++;
                }
                else
                {
                    assigned[targetRegion.Id] = [];
                }
            }
        }

        foreach (var region in target.Regions)
            if (!assigned.ContainsKey(region.Id))
                assigned[region.Id] = [];

        foreach (var region in target.Regions)
            (region with { Clips = assigned[region.Id] }).Validate();

        foreach (var region in target.Regions)
            region.Clips = assigned[region.Id];
        target.Playback = nextPlayback;
        return new(true, false);
    }

    public static List<EffectClip> CloneClips(IEnumerable<EffectClip> clips)
    {
        var instances = new Dictionary<Guid, Guid>();
        var sequences = new Dictionary<Guid, Guid>();
        var result = new List<EffectClip>();
        foreach (var item in clips)
        {
            var copy = item.Copy(newId: true);
            if (copy.Motion is { } motion)
            {
                copy.Motion = motion with
                {
                    Tracks = motion.Tracks.Select(track => track with
                    {
                        Keys = track.Keys.Select(key => key with { Id = Guid.NewGuid() }).ToList()
                    }).ToList()
                };
            }
            if (copy.AssetInstanceId is { } instanceId)
            {
                if (!instances.TryGetValue(instanceId, out var nextInstance))
                    instances[instanceId] = nextInstance = Guid.NewGuid();
                copy.AssetInstanceId = nextInstance;
            }
            if (copy.SequenceId is { } sequenceId)
            {
                if (!sequences.TryGetValue(sequenceId, out var nextSequence))
                    sequences[sequenceId] = nextSequence = Guid.NewGuid();
                copy.SequenceId = nextSequence;
            }
            copy.Validate();
            result.Add(copy);
        }
        return result;
    }

    public static List<EffectRegion> OrderedRegions(IReadOnlyList<EffectRegion> regions)
    {
        var result = new List<EffectRegion>(regions.Count);
        void Visit(EffectRegion region)
        {
            result.Add(region);
            if (region.Role == RegionRole.Group)
            {
                foreach (var child in regions.Where(candidate => candidate.ParentId == region.Id))
                    Visit(child);
            }
        }
        foreach (var root in regions.Where(region => region.ParentId is null))
            Visit(root);
        foreach (var orphan in regions.Where(region => !result.Contains(region)))
            result.Add(orphan);
        return result;
    }

    private static bool IsDefaultRegionName(string? name) =>
        string.IsNullOrWhiteSpace(name) || string.Equals(name.Trim(), "영역", StringComparison.Ordinal);
}
