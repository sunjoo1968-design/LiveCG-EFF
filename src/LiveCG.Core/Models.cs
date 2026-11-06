using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using SkiaSharp;

namespace LiveCG.Core;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EffectKind { Sweep, Glow, Sparkle, Border, Zoom, Bounce, FadeIn, RiseIn, ZoomIn, ZoomOutIn, Shake, Float, Highlight, Ripple, Rays, OrbitBurst, DigitRoll, UserMotion, WipeRight, FallIn, Underline, TravelRoute }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum StrengthLevel { Weak, Medium, Strong, VeryWeak, VeryStrong }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BackgroundMode { Auto, Transparent, Solid, KeepOriginal }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RegionShape { Rectangle, Ellipse, RoundedRectangle, Freehand, Polygon }
public record RegionPoint(double X,double Y);
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RegionRole { Object, Group, EffectArea }

public static class ProductInfo
{
    public const string Name = "LiveCG-EFF";
    public static string Version => typeof(ProductInfo).Assembly.GetName().Version!.ToString(3);
    public static string Credit => "SunjooAn | v" + Version;
    public static string EffectName(EffectKind kind) => kind switch
    {
        EffectKind.TravelRoute=>"여행 경로",EffectKind.Sweep => "빛 스윕", EffectKind.Glow => "광채", EffectKind.Sparkle => "반짝임",
        EffectKind.Border => "테두리 흐름", EffectKind.Zoom => "확대·축소", EffectKind.Bounce => "숫자 바운스",
        EffectKind.FadeIn => "서서히 등장", EffectKind.RiseIn => "아래에서 등장",
        EffectKind.ZoomIn => "줌인 등장", EffectKind.ZoomOutIn => "줌아웃 등장",
        EffectKind.Shake => "흔들림", EffectKind.Float => "부유",
        EffectKind.Highlight => "형광펜", EffectKind.Ripple => "원형 파동",
        EffectKind.Rays => "방사선", EffectKind.OrbitBurst => "원형 입자 확산", EffectKind.UserMotion => "사용자 모션", EffectKind.WipeRight=>"왼쪽부터 펼치기",EffectKind.FallIn=>"위에서 등장",EffectKind.Underline=>"밑줄 형광펜", _ => "숫자 롤링"
    };
    public static readonly StrengthLevel[] Strengths=[StrengthLevel.VeryWeak,StrengthLevel.Weak,StrengthLevel.Medium,StrengthLevel.Strong,StrengthLevel.VeryStrong];
    public static int StrengthIndex(StrengthLevel level)=>Array.IndexOf(Strengths,level);
    public static StrengthLevel StrengthAt(int index)=>Strengths[Math.Clamp(index,0,Strengths.Length-1)];
    public static string StrengthName(StrengthLevel level) => level switch { StrengthLevel.VeryWeak=>"최약",StrengthLevel.Weak => "약", StrengthLevel.Medium => "중", StrengthLevel.VeryStrong=>"최강", _ => "강" };
    public static bool IsEntrance(EffectKind kind) => kind is EffectKind.FadeIn or EffectKind.RiseIn or EffectKind.ZoomIn or EffectKind.ZoomOutIn or EffectKind.WipeRight or EffectKind.FallIn;
    public static bool HoldsDecoration(EffectKind kind)=>kind is EffectKind.Highlight or EffectKind.Underline or EffectKind.TravelRoute;
    public static SKColor DefaultEffectColor(EffectKind kind)=>kind switch{EffectKind.Glow=>new(255,218,88),EffectKind.Sparkle=>new(255,247,192),EffectKind.Border=>new(255,211,67),EffectKind.Highlight or EffectKind.Underline=>new(255,225,32),EffectKind.Ripple=>new(255,225,120),EffectKind.Rays=>new(255,229,99),EffectKind.OrbitBurst=>new(255,230,78),_=>SKColors.White};
    public static bool NeedsLayer(EffectKind kind) => IsEntrance(kind) || kind is EffectKind.Zoom or EffectKind.Bounce or EffectKind.Shake or EffectKind.Float or EffectKind.DigitRoll or EffectKind.UserMotion;
}

public record NormalizedRect(double X, double Y, double Width, double Height)
{
    public NormalizedRect Clamp()
    {
        if (!double.IsFinite(X + Y + Width + Height)) throw new InvalidDataException("영역 좌표가 올바르지 않습니다.");
        var x = Math.Clamp(X, 0, .99999); var y = Math.Clamp(Y, 0, .99999);
        return new(x, y, Math.Clamp(Width, Math.Min(.00001,1-x), 1 - x), Math.Clamp(Height, Math.Min(.00001,1-y), 1 - y));
    }
    public SKRect Pixels(int width, int height)
    {
        var r = Clamp();
        return SKRect.Create((float)(r.X * width), (float)(r.Y * height), (float)(r.Width * width), (float)(r.Height * height));
    }
    public static NormalizedRect FromPixels(SKRect r, int width, int height) => new NormalizedRect((double)r.Left / width, (double)r.Top / height, (double)r.Width / width, (double)r.Height / height).Clamp();
}

public record EffectOptions
{
    public EffectKind Kind { get; set; } = EffectKind.Sweep;
    public StrengthLevel Strength { get; set; } = StrengthLevel.Medium;
    public double Speed { get; set; } = 1;
    public string? Color { get; set; }
    [JsonIgnore] public double Power => Strength switch { StrengthLevel.VeryWeak=>.18,StrengthLevel.Weak => .55, StrengthLevel.Medium => 1,StrengthLevel.VeryStrong=>2.4, _ => 1.65 };
    // Only read from v0.1 documents; never used by the new editor.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? Intensity { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? Duration { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? Period { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? Repeat { get; set; }
    public EffectOptions Current() => this with { Strength = Intensity is { } i ? i < .34 ? StrengthLevel.Weak : i < .67 ? StrengthLevel.Medium : StrengthLevel.Strong : Strength, Intensity = null, Duration = null, Period = null, Repeat = null };
    public void Validate()
    {
        if (!Enum.IsDefined(Kind) || !Enum.IsDefined(Strength) || !double.IsFinite(Speed) || Speed is < .25 or > 4 || Color is not null&&!SKColor.TryParse(Color,out _))
            throw new InvalidDataException("효과 옵션을 확인하세요. 강도는 최약·약·중·강·최강, 속도는 0.25~4배, 색상은 올바른 색상표 값입니다.");
    }
}

public record EffectClip
{
    public ClipSection Section { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();
    public double Start { get; set; }
    public double Duration { get; set; } = 1;
    public EffectOptions Options { get; set; } = new();
    public bool Enabled { get; set; } = true;
    public bool Loop { get; set; }
    public double LoopPeriod { get; set; } = 1;
    public double SourceOffset { get; set; }
    public double? SourceDuration { get; set; }
    public Guid? SequenceId { get; set; }
    public MotionDefinition? Motion { get; set; }
    public TravelRoute? Route { get; set; }
    public Guid? AssetId { get; set; }
    public Guid? AssetInstanceId { get; set; }
    public string? AssetName { get; set; }
    public virtual bool Equals(EffectClip? other) => other is not null && Id == other.Id;
    public override int GetHashCode() => Id.GetHashCode();
    [JsonIgnore] public double End => Start + Duration;
    [JsonIgnore] public string DisplayName=>string.IsNullOrWhiteSpace(AssetName)?ProductInfo.EffectName(Options.Kind):AssetName+" · "+ProductInfo.EffectName(Options.Kind);
    [JsonIgnore] public string Label => $"{Start:0.##}–{End:0.##}초  {DisplayName} · {ProductInfo.StrengthName(Options.Strength)}{(Loop ? " ↻" : "")}";
    public EffectClip Copy(bool newId = true) => this with { Id = newId ? Guid.NewGuid() : Id, Options = Options with { }, Motion = Motion?.Copy(),Route=Route?.Copy() };
    public void Validate()
    {
        if (!double.IsFinite(Start + Duration) || Start < 0 || Duration is < .016 or > 300 || End > 3600)
            throw new InvalidDataException("클립의 시작·길이를 확인하세요. 전체 길이는 최대 1시간입니다.");
        if(!Enum.IsDefined(Section))throw new InvalidDataException("클립 구간이 올바르지 않습니다.");
        Options.Validate();
        if(Options.Kind==EffectKind.TravelRoute&&Route is null)throw new InvalidDataException("여행 경로를 전용 편집창에서 지정하세요.");Route?.Validate();
        if(!double.IsFinite(SourceOffset)||SourceOffset<0||SourceOffset>300||SourceDuration is { } total&&(!double.IsFinite(total)||total<.016||total>300))throw new InvalidDataException("분할 클립의 원본 구간이 올바르지 않습니다.");
        if(Options.Kind == EffectKind.UserMotion && Motion is null)throw new InvalidDataException("사용자 모션에 키프레임 정보가 없습니다.");
        Motion?.Validate();
        if (!double.IsFinite(LoopPeriod) || LoopPeriod is < .016 or > 300) throw new InvalidDataException("반복 주기는 0.016~300초입니다.");
    }
}

public record VideoOptions
{
    public OutputKind Output { get; set; }=OutputKind.Browser;
    public double Duration { get; set; } = 1;
    public int Fps { get; set; } = 30;
    public int Crf { get; set; } = 18;
    public double EndHold { get; set; } = 1;
    [JsonIgnore] public int FrameCount => Math.Max(1, (int)Math.Ceiling(Duration * Fps - .00000001));
    [JsonIgnore] public double ActualDuration => (double)FrameCount / Fps;
    public void Validate()
    {
        if (!Enum.IsDefined(Output) || !double.IsFinite(Duration + EndHold) || Duration is <= 0 or > 3630 || Fps is < 1 or > 60 || Crf is < 0 or > 50 || EndHold is < 0 or > 30)
            throw new InvalidDataException("영상 옵션을 확인하세요. fps 1~60, CRF 0~50, 끝 여유 0~30초입니다.");
    }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SeparationTarget { Object, Text }
public record EffectRegion
{
    public bool OverlayOnly { get; set; }
    public List<double> DigitDividers { get; set; }=[];
    public TextSeparationSettings TextSettings { get; set; }=new();
    public SeparationTarget SeparationTarget { get; set; }
    public string TextInkColor { get; set; }="";
    public RegionSoftness Softness { get; set; } = new();
    public bool ContentOnly { get; set; }
    public double ContentTolerance { get; set; } = 24;
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "영역";
    public RegionRole Role { get; set; }
    public Guid? ParentId { get; set; }
    public bool Collapsed { get; set; }
    public List<MaskTrack> Masks { get; set; }=[];
    public NormalizedRect Bounds { get; set; } = new(.1, .1, .3, .1);
    public RegionShape Shape { get; set; }
    public List<RegionPoint> Points { get; set; }=[];
    public ObjectCutout? Cutout { get; set; }
    public bool CutoutEnabled { get; set; }=true;
    public List<EffectClip> Clips { get; set; } = [];
    public BackgroundMode Background { get; set; } = BackgroundMode.Auto;
    public string BackgroundColor { get; set; } = "#FFFFFF";
    public bool Locked { get; set; }
    public bool Enabled { get; set; } = true;
    public virtual bool Equals(EffectRegion? other) => other is not null && Id == other.Id;
    public override int GetHashCode() => Id.GetHashCode();
    [JsonPropertyName("Effect"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public EffectOptions? LegacyEffect { get; set; }
    [JsonIgnore] public string Label => $"{(Enabled?"◉":"— 숨김")} {(Role==RegionRole.Group?"▣ ":ParentId is not null?"↳ ":"")}{Name}{(Locked ? " 🔒" : "")} · {Clips.Count}개 효과";
    public EffectRegion Copy(bool newIds = true) => this with { Id = newIds ? Guid.NewGuid() : Id,DigitDividers=[..DigitDividers],TextSettings=TextSettings.Copy(), Softness=Softness with{}, Clips = Clips.Select(c => c.Copy(newIds)).ToList(), Points=Points.Select(p=>p with{}).ToList(), Masks=Masks.Select(m=>m.Copy(newIds)).ToList(), Cutout = Cutout is null ? null : Cutout with{} };
    public void Validate()
    {
        Bounds = Bounds.Clamp();
        if(TextSettings is null)throw new InvalidDataException("글자 분리 설정이 없습니다.");TextSettings.Validate();if(DigitDividers is null||DigitDividers.Count>30||DigitDividers.Any(v=>!double.IsFinite(v)||v is <=0 or >=1))throw new InvalidDataException("숫자 칸 경계를 확인하세요.");
        if(!Enum.IsDefined(SeparationTarget)||TextInkColor is null||(TextInkColor.Length>0&&!SKColor.TryParse(TextInkColor,out _)))throw new InvalidDataException("분리 대상/글자 보호 색상을 확인하세요.");
        if(Softness is null)throw new InvalidDataException("경계 설정이 없습니다.");Softness.Validate();
        if(!double.IsFinite(ContentTolerance)||ContentTolerance is <0 or >150)throw new InvalidDataException("배경 분리 허용오차는 0~150입니다.");
        Cutout?.Validate();
        if(!Enum.IsDefined(Role)||Masks is null||Masks.Count>100)throw new InvalidDataException("그룹/마스크 정보가 올바르지 않습니다.");foreach(var mask in Masks)mask.Validate();if(Masks.Select(m=>m.Id).Distinct().Count()!=Masks.Count)throw new InvalidDataException("중복된 마스크 ID입니다.");
        if(Points is null||Points.Count>4096||Points.Any(p=>p is null||!double.IsFinite(p.X+p.Y)||p.X<0||p.Y<0||p.X>1||p.Y>1)||(Shape is RegionShape.Freehand or RegionShape.Polygon&&Points.Count<3))throw new InvalidDataException("영역 경로는 유효한 포인트3~4096개가 필요합니다.");
        if (!Enum.IsDefined(Shape) || !Enum.IsDefined(Background) || !SKColor.TryParse(BackgroundColor, out _) || Clips is null || Clips.Count > 4000)
            throw new InvalidDataException("영역 배경 또는 클립 정보가 올바르지 않습니다.");
        foreach (var clip in Clips) clip.Validate();
        if (Clips.Select(c => c.Id).Distinct().Count() != Clips.Count) throw new InvalidDataException("중복된 효과 클립 ID입니다.");
    }
}

public class ImageJob : INotifyPropertyChanged
{
    private string _status = "영역 지정 대기", _detail = "";
    private double _progress;
    public Guid BroadcastId { get; set; } = Guid.NewGuid();
    public string SourceFingerprint { get; set; } = "";
    public long SourceBytes { get; set; }
    public string SourcePath { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
    public List<EffectRegion> Regions { get; set; } = [];
    public PlaybackSections Playback { get; set; } = new();
    [JsonIgnore] public string FileName => Path.GetFileName(SourcePath);
    [JsonIgnore] public string OutputName => Path.GetFileNameWithoutExtension(SourcePath) + ".webm";
    [JsonIgnore] public string Status { get => _status; set { _status = value; Changed(); Changed(nameof(ProgressColor)); } }
    [JsonIgnore] public string Detail { get => _detail; set { _detail = value; Changed(); } }
    [JsonIgnore] public double Progress { get => _progress; set { _progress = Math.Clamp(value, 0, 100); Changed(); } }
    [JsonIgnore] public string ProgressColor => Status == "실패" ? "#FF7386" : Status == "취소" ? "#E4AE60" : Status == "건너뜀" ? "#7891A5" : "#5DE2B1";
    public ImageJob Snapshot() => new() { BroadcastId = BroadcastId, SourceFingerprint = SourceFingerprint, SourceBytes = SourceBytes, SourcePath = SourcePath, Width = Width, Height = Height, Regions = LayerGraph.Copy(Regions,false), Playback=Playback with{} };
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? property = null) => PropertyChanged?.Invoke(this, new(property));
}

public class ProjectDocument
{
    public int SchemaVersion { get; set; } = 6;
    public List<ImageJob> Jobs { get; set; } = [];
    public VideoOptions Video { get; set; } = new();
    public string OutputDirectory { get; set; } = "";
    public void Validate()
    {
        if (SchemaVersion != 6 || Jobs is null || Jobs.Count > 1000) throw new InvalidDataException("지원하지 않거나 잘못된 작업 형식입니다.");
        Video.Validate();
        foreach (var job in Jobs)
        {
            if(job.Playback is null)throw new InvalidDataException("재생 구간 정보가 없습니다.");job.Playback.Validate();
            if (string.IsNullOrWhiteSpace(job.SourcePath) || job.Width <= 0 || job.Height <= 0 || job.Regions is null || job.Regions.Count > 100)
                throw new InvalidDataException("작업 이미지 또는 영역 정보가 올바르지 않습니다.");
            if (job.Regions.Select(r => r.Id).Distinct().Count() != job.Regions.Count) throw new InvalidDataException("중복된 영역 ID입니다.");
            foreach (var r in job.Regions) r.Validate();
            LayerGraph.Validate(job.Regions);
        }
    }
}

public class TemplateDocument
{
    public PlaybackSections Playback { get; set; } = new();
    public int SchemaVersion { get; set; } = 6;
    public List<EffectRegion> Regions { get; set; } = [];
    public VideoOptions Video { get; set; } = new();
    public void Validate()
    {
        if (SchemaVersion != 6 || Regions is null || Regions.Count > 100) throw new InvalidDataException("템플릿 형식이 올바르지 않습니다.");
        Video.Validate();if(Playback is null)throw new InvalidDataException("재생 구간 정보가 없습니다.");Playback.Validate(); foreach (var r in Regions) r.Validate();LayerGraph.Validate(Regions);
    }
}

public class AppDefaults
{
    public PlaybackSections Playback { get; set; } = new();
    public int SchemaVersion { get; set; } = 6;
    public VideoOptions Video { get; set; } = new();
    public EffectOptions Effect { get; set; } = new();
    public double ClipDuration { get; set; } = 1;
    public string OutputDirectory { get; set; } = "";
    public bool Loop { get; set; }
    public double LoopPeriod { get; set; } = 1;
    public MotionDefinition? Motion { get; set; }
    public EditorPreferences Editor { get; set; } = new();
    public void Validate()
    {
        if (SchemaVersion != 6 || !double.IsFinite(ClipDuration) || ClipDuration is < .016 or > 300) throw new InvalidDataException("지원하지 않는 기본값 파일입니다.");
        Video.Validate();if(Playback is null)throw new InvalidDataException("재생 구간 정보가 없습니다.");Playback.Validate(); Effect.Validate();
        if(Effect.Kind==EffectKind.UserMotion&&Motion is null)throw new InvalidDataException("기본 사용자 모션 정보가 없습니다.");Motion?.Validate();
        new EffectClip { Loop = Loop, LoopPeriod = LoopPeriod }.Validate();
        if (Editor is null) throw new InvalidDataException("편집 옵션이 없습니다.");
        Editor.Validate();
    }
}

public record EditorPreferences
{
    public bool Snap { get; set; } = true;
    public bool Ripple { get; set; } = true;
    public bool FollowMagnet { get; set; }
    public bool ShowAllRegions { get; set; }
    public bool HideOverlays { get; set; }
    public bool ShortcutsEnabled { get; set; } = true;
    public string DrawKey { get; set; } = "R";
    public string SelectKey { get; set; } = "V";
    public RegionShape NewShape { get; set; }
    public void Validate()
    {
        static bool Letter(string? key) => key is { Length: 1 } && key[0] is >= 'A' and <= 'Z';
        if (!Letter(DrawKey) || !Letter(SelectKey) || DrawKey == SelectKey || !Enum.IsDefined(NewShape))
            throw new InvalidDataException("영역 단축키는 서로 다른 영문자 A~Z로 지정하세요.");
    }
}
