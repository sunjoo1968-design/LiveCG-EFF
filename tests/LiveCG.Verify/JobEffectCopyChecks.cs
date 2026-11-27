using LiveCG.Core;

internal static class JobEffectCopyChecks
{
    public static void Run(Action<bool, string> check)
    {
        var emptyJob = new ImageJob { SourcePath = @"C:\test\empty.png", Width = 800, Height = 600 };
        check(!JobEffectBundle.CanCopy(null) && !JobEffectBundle.CanCopy(emptyJob) && JobEffectBundle.FromJob(emptyJob) is null,
            "job effect copy rejects null or regionless jobs");

        emptyJob.Regions.Add(new EffectRegion { Name = "빈 영역" });
        check(!JobEffectBundle.CanCopy(emptyJob) && JobEffectBundle.FromJob(emptyJob) is null,
            "job effect copy rejects jobs with zero clips");

        var motion = new MotionDefinition { Fps = 30, LengthFrames = 42 };
        motion.SetKey(MotionProperty.X, 0, -0.25, KeyEasing.EaseOut);
        motion.SetKey(MotionProperty.X, 42, 0, KeyEasing.Linear);
        motion.SetKey(MotionProperty.Rotation, 0, -12, KeyEasing.EaseInOut);
        motion.SetKey(MotionProperty.Rotation, 42, 0, KeyEasing.EaseInOut);

        var sharedSequence = Guid.NewGuid();
        var sharedInstance = Guid.NewGuid();
        var assetId = Guid.NewGuid();
        var sourceRegion = new EffectRegion
        {
            Name = "영역",
            Bounds = new(0.05, 0.1, 0.4, 0.2),
            DigitDividers = [0.2, 0.6],
            Clips =
            [
                new EffectClip
                {
                    Start = 0.2,
                    Duration = 0.7,
                    Section = ClipSection.Intro,
                    Options = new EffectOptions { Kind = EffectKind.WipeFromRight, Strength = StrengthLevel.Strong, Speed = 1.5, Color = "#FF8844" }
                },
                new EffectClip
                {
                    Start = 0.9,
                    Duration = 0.8,
                    Section = ClipSection.Loop,
                    Loop = true,
                    LoopPeriod = 0.4,
                    SequenceId = sharedSequence,
                    SourceOffset = 0,
                    SourceDuration = 1.6,
                    AssetId = assetId,
                    AssetInstanceId = sharedInstance,
                    AssetName = "강조 조합",
                    Options = new EffectOptions { Kind = EffectKind.Glow, Strength = StrengthLevel.VeryStrong, Speed = 2.0, Color = "#FFEE55" }
                },
                new EffectClip
                {
                    Start = 1.7,
                    Duration = 0.8,
                    Section = ClipSection.Loop,
                    SequenceId = sharedSequence,
                    SourceOffset = 0.8,
                    SourceDuration = 1.6,
                    AssetId = assetId,
                    AssetInstanceId = sharedInstance,
                    AssetName = "강조 조합",
                    Motion = motion,
                    Options = new EffectOptions { Kind = EffectKind.UserMotion, Strength = StrengthLevel.Medium, Speed = 1.25 }
                },
                new EffectClip
                {
                    Start = 2.5,
                    Duration = 0.6,
                    Section = ClipSection.Exit,
                    Enabled = false,
                    Options = new EffectOptions { Kind = EffectKind.WipeFromBottom, Strength = StrengthLevel.Weak, Speed = 0.75 }
                }
            ]
        };

        var sourceJob = new ImageJob
        {
            SourcePath = @"C:\test\01_source.png",
            Width = 960,
            Height = 540,
            Playback = new PlaybackSections
            {
                Enabled = true,
                ByEffects = false,
                IntroEnd = 0.9,
                LoopEnd = 2.5,
                ExitEnabled = true,
                ExitDuration = 0.6,
                WebmRepeats = 4,
                LoopRest = 1.25,
                RestMode = LoopRestMode.Hold,
                ExitFade = false,
                ExitAtLoopEnd = false
            },
            Regions = [sourceRegion]
        };

        var bundle = JobEffectBundle.FromJob(sourceJob)!;
        check(bundle is not null && bundle.ClipCount == 4 && bundle.IsUnchangedSource(sourceJob),
            "job effect bundle captures all clips and recognizes unchanged source");

        var mask = new MaskTrack { Name = "보호 마스크", Bounds = new(0.2, 0.2, 0.5, 0.5), Duration = 2.0 };
        var targetRegionId = Guid.NewGuid();
        var targetBroadcastId = Guid.NewGuid();
        var targetRegion = new EffectRegion
        {
            Id = targetRegionId,
            Name = "할인 가격",
            Role = RegionRole.Object,
            Bounds = new(0.32, 0.58, 0.48, 0.24),
            Shape = RegionShape.RoundedRectangle,
            Locked = true,
            Enabled = false,
            ContentOnly = true,
            ContentTolerance = 38,
            SeparationTarget = SeparationTarget.Text,
            TextInkColor = "#112233",
            Background = BackgroundMode.Solid,
            BackgroundColor = "#223344",
            DigitDividers = [0.25, 0.5, 0.75],
            Masks = [mask],
            Clips = [new EffectClip { Start = 0, Duration = 0.5, Options = new EffectOptions { Kind = EffectKind.Bounce } }]
        };
        var blurBgId = Guid.NewGuid();
        var blurBgRegion = new EffectRegion
        {
            Id = blurBgId,
            Name = "블러 배경",
            Bounds = new(0, 0, 1, 1)
        };
        var targetJob = new ImageJob
        {
            BroadcastId = targetBroadcastId,
            SourcePath = @"C:\test\02_target.png",
            OutputPath = @"C:\out\02_target.html",
            OutputAliases = [@"C:\alias\02.html"],
            Width = 1080,
            Height = 1920,
            BlurFocus = new BlurFocus { BackgroundId = blurBgId, BlurPixels = 14, Steps = [new BlurFocusStep(targetRegionId, StrengthLevel.Strong)] },
            Regions = [blurBgRegion, targetRegion]
        };

        check(bundle!.ApplyTo(targetJob), "bundle applies to target job with existing regions");
        check(blurBgRegion.Clips.Count == 0 && targetRegion.Clips.Count == 4,
            "single-slot bundle skips blur-focus background layer and applies to target content layer");

        check(targetJob.SourcePath == @"C:\test\02_target.png" &&
              targetJob.Width == 1080 && targetJob.Height == 1920 &&
              targetJob.BroadcastId == targetBroadcastId &&
              targetJob.OutputPath == @"C:\out\02_target.html" &&
              targetJob.OutputAliases.Single() == @"C:\alias\02.html" &&
              targetJob.BlurFocus?.BackgroundId == blurBgId,
            "paste preserves target image, dimensions, output bindings, and blur focus");

        check(targetRegion.Id == targetRegionId &&
              targetRegion.Name == "할인 가격" &&
              targetRegion.Bounds == new NormalizedRect(0.32, 0.58, 0.48, 0.24) &&
              targetRegion.Shape == RegionShape.RoundedRectangle &&
              targetRegion.Locked && !targetRegion.Enabled &&
              targetRegion.SeparationTarget == SeparationTarget.Text &&
              targetRegion.TextInkColor == "#112233" &&
              targetRegion.Background == BackgroundMode.Solid &&
              targetRegion.BackgroundColor == "#223344" &&
              targetRegion.DigitDividers.SequenceEqual([0.25, 0.5, 0.75]) &&
              targetRegion.Masks.Single().Id == mask.Id,
            "paste preserves target position, size, shape, separation, masks, and layer settings");

        var pasted = targetRegion.Clips;
        check(pasted[0].Options.Kind == EffectKind.WipeFromRight &&
              Math.Abs(pasted[0].Start - 0.2) < 1e-9 &&
              Math.Abs(pasted[0].Duration - 0.7) < 1e-9 &&
              pasted[0].Section == ClipSection.Intro &&
              pasted[0].Options.Strength == StrengthLevel.Strong &&
              Math.Abs(pasted[0].Options.Speed - 1.5) < 1e-9 &&
              pasted[0].Options.Color == "#FF8844" &&
              pasted[1].Options.Kind == EffectKind.Glow &&
              pasted[1].Loop && Math.Abs(pasted[1].LoopPeriod - 0.4) < 1e-9 &&
              pasted[2].Options.Kind == EffectKind.UserMotion &&
              Math.Abs(pasted[2].Motion!.Value(MotionProperty.X, 0) - (-0.25)) < 1e-9 &&
              Math.Abs(pasted[2].SourceOffset - 0.8) < 1e-9 &&
              pasted[3].Options.Kind == EffectKind.WipeFromBottom &&
              !pasted[3].Enabled && pasted[3].Section == ClipSection.Exit,
            "paste preserves clip order, direction, delay, duration, section, loop, motion, and enabled state");

        check(pasted.All(clip => sourceRegion.Clips.All(src => src.Id != clip.Id)) &&
              pasted[1].SequenceId is not null && pasted[1].SequenceId == pasted[2].SequenceId && pasted[1].SequenceId != sharedSequence &&
              pasted[1].AssetInstanceId is not null && pasted[1].AssetInstanceId == pasted[2].AssetInstanceId && pasted[1].AssetInstanceId != sharedInstance,
            "paste assigns fresh clip, sequence, and asset-instance IDs while keeping internal pairings");

        check(targetJob.Playback == sourceJob.Playback,
            "paste copies playback section timing, rest delay, and repeat settings");

        pasted[0].Options.Strength = StrengthLevel.VeryWeak;
        pasted[2].Motion!.Tracks[0].Keys[0].Value = 0.5;
        check(sourceRegion.Clips[0].Options.Strength == StrengthLevel.Strong &&
              Math.Abs(sourceRegion.Clips[2].Motion!.Value(MotionProperty.X, 0) - (-0.25)) < 1e-9,
            "mutating pasted clips does not alias source job or clipboard");

        // Newly imported regionless target receives clean proportional regions without source pixel cutouts/dividers
        var freshTarget = new ImageJob
        {
            SourcePath = @"C:\test\03_fresh.png",
            Width = 1920,
            Height = 1080
        };
        var freshResult = bundle.ApplyDetailed(freshTarget);
        check(freshResult.Applied && freshResult.CreatedRegions &&
              freshTarget.SourcePath == @"C:\test\03_fresh.png" &&
              freshTarget.Width == 1920 && freshTarget.Height == 1080 &&
              freshTarget.Regions.Count == 1 &&
              freshTarget.Regions[0].Id != sourceRegion.Id &&
              freshTarget.Regions[0].Cutout is null &&
              freshTarget.Regions[0].DigitDividers.Count == 0 &&
              freshTarget.Regions[0].Clips.Count == 4 &&
              freshTarget.Playback == sourceJob.Playback,
            "pasting onto a regionless material creates clean regions with copied effects while keeping target image");

        // Multi-layer named & hierarchical matching
        var multiSourceGroup = new EffectRegion
        {
            Name = "배너 그룹",
            Role = RegionRole.Group,
            Collapsed = true,
            Bounds = new(0.1, 0.1, 0.8, 0.8),
            Clips = [new EffectClip { Start = 0, Duration = 0.6, Options = new EffectOptions { Kind = EffectKind.RiseIn } }]
        };
        var multiSourceChild = new EffectRegion
        {
            Name = "뱃지",
            Role = RegionRole.EffectArea,
            ParentId = multiSourceGroup.Id,
            Bounds = new(0.2, 0.2, 0.2, 0.2),
            Clips = [new EffectClip { Start = 0.5, Duration = 1.0, Options = new EffectOptions { Kind = EffectKind.PopReturn } }]
        };
        var multiSourceTitle = new EffectRegion
        {
            Name = "타이틀",
            Bounds = new(0.1, 0.05, 0.5, 0.1),
            Clips = [new EffectClip { Start = 0.15, Duration = 0.9, Options = new EffectOptions { Kind = EffectKind.WipeFromTop } }]
        };
        var multiSource = new ImageJob
        {
            SourcePath = @"C:\test\multi_src.png",
            Width = 800,
            Height = 600,
            Regions = [multiSourceGroup, multiSourceChild, multiSourceTitle]
        };
        var multiBundle = JobEffectBundle.FromJob(multiSource)!;

        var targetTitle = new EffectRegion { Name = "타이틀", Bounds = new(0.4, 0.7, 0.5, 0.2) };
        var targetGroup = new EffectRegion { Name = "배너 그룹", Role = RegionRole.Group, Collapsed = true, Bounds = new(0.05, 0.05, 0.5, 0.5) };
        var targetChild = new EffectRegion { Name = "뱃지", Role = RegionRole.EffectArea, ParentId = targetGroup.Id, Bounds = new(0.1, 0.1, 0.2, 0.2) };
        var multiTarget = new ImageJob
        {
            SourcePath = @"C:\test\multi_dst.png",
            Width = 1200,
            Height = 900,
            Regions = [targetTitle, targetGroup, targetChild]
        };

        check(multiBundle.ApplyTo(multiTarget) &&
              targetTitle.Clips.Single().Options.Kind == EffectKind.WipeFromTop &&
              Math.Abs(targetTitle.Clips.Single().Start - 0.15) < 1e-9 &&
              targetGroup.Clips.Single().Options.Kind == EffectKind.RiseIn &&
              targetChild.Clips.Single().Options.Kind == EffectKind.PopReturn &&
              targetGroup.Collapsed && targetChild.ParentId == targetGroup.Id &&
              multiTarget.Regions[0] == targetTitle,
            "multi-layer paste matches custom names and collapsed group children without altering layer order or hierarchy");

        var doc = new ProjectDocument { Jobs = [sourceJob, targetJob, freshTarget, multiSource, multiTarget] };
        doc.Validate();
        check(true, "pasted multi-job project passes full schema validation");
    }
}
