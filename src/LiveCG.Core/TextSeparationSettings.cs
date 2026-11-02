using SkiaSharp;
namespace LiveCG.Core;
public record TextSeparationSettings
{
    public bool RangeMode { get; set; }
    public List<string> BackgroundColors { get; set; }=[];
    public double ColorTolerance { get; set; }=24;
    public double InkTolerance { get; set; }=36;
    public TextSeparationSettings Copy()=>this with{BackgroundColors=[..BackgroundColors]};
    public void Validate(){if(BackgroundColors is null||BackgroundColors.Count>8||BackgroundColors.Any(c=>!SKColor.TryParse(c,out _))||!double.IsFinite(ColorTolerance+InkTolerance)||ColorTolerance is <0 or >150||InkTolerance is <0 or >150)throw new InvalidDataException("배경 색상 범위·글자 보호 범위를 확인하세요.");}
}
