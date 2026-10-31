using System.Text.Json.Serialization;
using SkiaSharp;

namespace LiveCG.Core;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OutputKind { Webm, Browser, Both }

public record PlaybackSections
{
    public bool Enabled { get; set; }
    public double IntroEnd { get; set; }=1;
    public double LoopEnd { get; set; }=4;
    public double ExitDuration { get; set; }=1;
    public int WebmRepeats { get; set; }=3;
    public bool ExitFade { get; set; }=true;
    public bool ReplayOnShow { get; set; }=true;
    public bool ExitAtLoopEnd { get; set; }=true;
    public void Validate()
    {
        if(!double.IsFinite(IntroEnd+LoopEnd+ExitDuration)||IntroEnd<0||LoopEnd<=IntroEnd||LoopEnd>3600||ExitDuration is <0 or >30||WebmRepeats is <1 or >100)
            throw new InvalidDataException("구간을 확인하세요. 등장 끝 < 반복 끝, 퇴장 0~30초, WebM 반복 1~100회입니다.");
    }
    public static PlaybackSections FromTimeline(ImageJob job,VideoOptions video)
    {
        var total=TimelineRules.Resolve(job.Regions,video).ActualDuration;
        var entrance=job.Regions.SelectMany(r=>r.Clips).Where(c=>c.Enabled&&ProductInfo.IsEntrance(c.Options.Kind)).Select(c=>c.End).DefaultIfEmpty(Math.Min(1,total/3)).Max();
        return new(){Enabled=true,IntroEnd=TimelineRules.Snap(Math.Min(entrance,Math.Max(0,total-1d/video.Fps)),video.Fps),LoopEnd=Math.Max(1d/video.Fps,TimelineRules.Snap(total,video.Fps))};
    }
}

/// <summary>All sections use half-open, integer-frame intervals. Editing time stays independent of repeated output time.</summary>
public sealed record PlaybackPlan(int Fps,int IntroFrames,int LoopEndFrame,int EndFrame,bool Enabled,int Repeats,bool ExitFade)
{
    public int LoopFrames=>LoopEndFrame-IntroFrames;
    public int ExitFrames=>EndFrame-LoopEndFrame;
    public int WebmFrameCount=>Enabled?checked(IntroFrames+LoopFrames*Repeats+ExitFrames):EndFrame;
    public double Duration=>(double)EndFrame/Fps;
    public double WebmDuration=>(double)WebmFrameCount/Fps;
    public static PlaybackPlan Create(ImageJob job,VideoOptions video)
    {
        video.Validate();job.Playback.Validate();
        var end=TimelineRules.Resolve(job.Regions,video).FrameCount;var p=job.Playback;
        if(!p.Enabled)return new(video.Fps,0,end,end,false,1,false);
        var a=Math.Max(0,(int)Math.Round(p.IntroEnd*video.Fps,MidpointRounding.AwayFromZero));
        var b=Math.Max(a+1,(int)Math.Round(p.LoopEnd*video.Fps,MidpointRounding.AwayFromZero));
        var exit=Math.Max(0,(int)Math.Round(p.ExitDuration*video.Fps,MidpointRounding.AwayFromZero));
        end=Math.Max(end,b+exit);
        return new(video.Fps,a,b,end,true,p.WebmRepeats,p.ExitFade);
    }
    public void ValidateWebm()
    {if(WebmDuration>3630)throw new InvalidDataException("반복을 포함한 WebM이 1시간30초를 넘습니다. 반복 횟수 또는 구간 길이를 줄이세요.");}
    public int WebmSourceFrame(int frame)
    {
        frame=Math.Clamp(frame,0,WebmFrameCount-1);
        if(!Enabled||frame<IntroFrames)return frame;
        var offset=frame-IntroFrames;
        return offset<LoopFrames*Repeats?IntroFrames+offset%LoopFrames:LoopEndFrame+offset-LoopFrames*Repeats;
    }
    public double LoopTime(double elapsed)
    {
        elapsed=Math.Max(0,elapsed);
        if(!Enabled)return Math.Min(Duration,elapsed);
        var a=(double)IntroFrames/Fps;var len=(double)LoopFrames/Fps;
        return elapsed<a?elapsed:a+(elapsed-a)%len;
    }
    public double ExitTime(double elapsed)=>Math.Min(Duration,(double)LoopEndFrame/Fps+Math.Max(0,elapsed));
    public double Opacity(double sourceTime)
    {
        if(!Enabled||!ExitFade||sourceTime*Fps<LoopEndFrame)return 1;
        if(ExitFrames<=1)return 0;
        return Math.Clamp((EndFrame-1-sourceTime*Fps)/(ExitFrames-1d),0,1);
    }
    public static void ApplyOpacity(SKBitmap frame,double alpha)
    {
        if(alpha>=1)return;
        using var c=new SKCanvas(frame);using var p=new SKPaint{BlendMode=SKBlendMode.DstIn,Color=SKColors.White.WithAlpha((byte)Math.Round(Math.Clamp(alpha,0,1)*255))};c.DrawRect(SKRect.Create(frame.Width,frame.Height),p);
    }
}

/// <summary>Clock shared by native preview and browser specification: entrance once, loop until exit requested.</summary>
public sealed class PlaybackClock(PlaybackPlan plan,bool exitAtLoopEnd)
{
    private double? _exitAt;
    public bool ExitRequested=>_exitAt is not null;
    public void RequestExit(double elapsed)
    {
        if(_exitAt is not null)return;
        if(!plan.Enabled){_exitAt=elapsed;return;}
        var a=(double)plan.IntroFrames/plan.Fps;var loop=(double)plan.LoopFrames/plan.Fps;
        _exitAt=exitAtLoopEnd?(elapsed<a?a+loop:a+(Math.Floor((elapsed-a)/loop+1e-9)+1)*loop):elapsed;
    }
    public (double Time,bool Finished) Sample(double elapsed)
    {
        if(_exitAt is { } at&&elapsed>=at){var time=plan.ExitTime(elapsed-at);return(time,time>=plan.Duration);}
        return(plan.LoopTime(elapsed),!plan.Enabled&&elapsed>=plan.Duration);
    }
}
