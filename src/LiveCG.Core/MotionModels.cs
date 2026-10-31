using System.Text.Json.Serialization;

namespace LiveCG.Core;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MotionProperty { X, Y, ScaleX, ScaleY, Rotation, Opacity, AnchorX, AnchorY, CropTop, CropBottom, CropLeft, CropRight }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum KeyEasing { Linear, EaseIn, EaseOut, EaseInOut, Hold }
public record MotionKeyframe
{
    public Guid Id { get; set; }=Guid.NewGuid();
    public int Frame { get; set; }
    public double Value { get; set; }
    public KeyEasing Easing { get; set; }=KeyEasing.EaseInOut;
}
public record MotionTrack
{
    public MotionProperty Property { get; set; }
    public List<MotionKeyframe> Keys { get; set; }=[];
    public double Evaluate(double frame)
    {
        var keys=Keys.OrderBy(k=>k.Frame).ToArray();
        if(keys.Length==0)return MotionDefinition.Default(Property);
        if(frame<=keys[0].Frame)return keys[0].Value;
        for(var i=1;i<keys.Length;i++)if(frame<keys[i].Frame)
        {
            var a=keys[i-1];var b=keys[i];var t=(frame-a.Frame)/(b.Frame-a.Frame);
            t=a.Easing switch{KeyEasing.EaseIn=>t*t,KeyEasing.EaseOut=>1-(1-t)*(1-t),KeyEasing.EaseInOut=>t*t*(3-2*t),KeyEasing.Hold=>0,_=>t};
            return a.Value+(b.Value-a.Value)*t;
        }
        return keys[^1].Value;
    }
}
public record MotionDefinition
{
    public int Fps { get; set; }=30;
    public int LengthFrames { get; set; }=60;
    public List<MotionTrack> Tracks { get; set; }=[];
    [JsonIgnore] public double Duration=>(double)LengthFrames/Fps;
    public static double Default(MotionProperty property)=>property switch{MotionProperty.ScaleX or MotionProperty.ScaleY or MotionProperty.Opacity=>1,MotionProperty.AnchorX or MotionProperty.AnchorY=>.5,_=>0};
    public static bool IsCrop(MotionProperty p)=>p is MotionProperty.CropTop or MotionProperty.CropBottom or MotionProperty.CropLeft or MotionProperty.CropRight;
    public static bool IsUnit(MotionProperty p)=>IsCrop(p)||p is MotionProperty.Opacity or MotionProperty.AnchorX or MotionProperty.AnchorY;
    public static string Name(MotionProperty property)=>property switch{MotionProperty.X=>"위치 X",MotionProperty.Y=>"위치 Y",MotionProperty.ScaleX=>"가로 크기",MotionProperty.ScaleY=>"세로 크기",MotionProperty.Rotation=>"회전",MotionProperty.Opacity=>"불투명도",MotionProperty.AnchorX=>"기준점 X",MotionProperty.AnchorY=>"기준점 Y",MotionProperty.CropTop=>"크롭 · 위",MotionProperty.CropBottom=>"크롭 · 아래",MotionProperty.CropLeft=>"크롭 · 왼쪽",_=>"크롭 · 오른쪽"};
    public double Value(MotionProperty property,double frame)=>Tracks.FirstOrDefault(t=>t.Property==property)?.Evaluate(frame)??Default(property);
    public MotionDefinition Copy()=>this with{Tracks=Tracks.Select(t=>t with{Keys=t.Keys.Select(k=>k with{}).ToList()}).ToList()};
    public void SetKey(MotionProperty property,int frame,double value,KeyEasing easing=KeyEasing.EaseInOut)
    {
        ValidateValue(property,value);frame=Math.Clamp(frame,0,LengthFrames);
        var track=Tracks.FirstOrDefault(t=>t.Property==property);
        if(track is null){track=new(){Property=property};Tracks.Add(track);}
        var key=track.Keys.FirstOrDefault(k=>k.Frame==frame);
        if(key is null){key=new(){Frame=frame};track.Keys.Add(key);}
        key.Value=value;key.Easing=easing;track.Keys=track.Keys.OrderBy(k=>k.Frame).ToList();
    }
    public void ChangeFps(int fps,bool preserveTime)
    {
        if(fps is <1 or >120)throw new InvalidDataException("제작 fps는1~120입니다.");
        if(preserveTime)
        {
            var ratio=(double)fps/Fps;LengthFrames=Math.Max(1,(int)Math.Round(LengthFrames*ratio));
            foreach(var t in Tracks){foreach(var k in t.Keys)k.Frame=(int)Math.Round(k.Frame*ratio);t.Keys=t.Keys.GroupBy(k=>k.Frame).Select(g=>g.Last()).OrderBy(k=>k.Frame).ToList();}
        }
        Fps=fps;Validate();
    }
    public static void ValidateValue(MotionProperty property,double value)
    {
        var limit=IsUnit(property)?(0d,1d):property switch{MotionProperty.ScaleX or MotionProperty.ScaleY=>(0d,20d),MotionProperty.Rotation=>(-36000d,36000d),_=>(-20d,20d)};
        if(!double.IsFinite(value)||value<limit.Item1||value>limit.Item2)throw new InvalidDataException(Name(property)+" 값이 허용 범위를 벗어났습니다.");
    }
    public void Validate()
    {
        if(Fps is <1 or >120||LengthFrames<1||LengthFrames>Fps*300||Tracks is null||Tracks.Count>12)throw new InvalidDataException("키프레임 효과의 fps/길이/채널을 확인하세요.");
        if(Tracks.Select(t=>t.Property).Distinct().Count()!=Tracks.Count)throw new InvalidDataException("중복 모션 채널입니다.");
        var ids=Tracks.Where(t=>t.Keys is not null).SelectMany(t=>t.Keys).Select(k=>k.Id).ToArray();if(ids.Contains(Guid.Empty)||ids.Distinct().Count()!=ids.Length)throw new InvalidDataException("중복되거나 빈 키프레임 ID입니다.");
        foreach(var track in Tracks)
        {
            if(!Enum.IsDefined(track.Property)||track.Keys is null||track.Keys.Count>2000||track.Keys.Select(k=>k.Frame).Distinct().Count()!=track.Keys.Count)throw new InvalidDataException("잘못되거나 중복된 키프레임입니다.");
            foreach(var key in track.Keys){if(key.Frame<0||key.Frame>LengthFrames||!Enum.IsDefined(key.Easing))throw new InvalidDataException("키프레임의 위치/보간을 확인하세요.");ValidateValue(track.Property,key.Value);}
        }
    }
    public static MotionDefinition Example()
    {
        var motion=new MotionDefinition();motion.SetKey(MotionProperty.Opacity,0,0);motion.SetKey(MotionProperty.Opacity,12,1);motion.SetKey(MotionProperty.Opacity,48,1);motion.SetKey(MotionProperty.Opacity,60,0);
        motion.SetKey(MotionProperty.ScaleX,0,.7);motion.SetKey(MotionProperty.ScaleY,0,.7);
        foreach(var p in new[]{MotionProperty.ScaleX,MotionProperty.ScaleY}){motion.SetKey(p,12,1);motion.SetKey(p,24,1.25);motion.SetKey(p,36,1);}
        motion.SetKey(MotionProperty.X,36,0);motion.SetKey(MotionProperty.X,60,1);return motion;
    }
}
