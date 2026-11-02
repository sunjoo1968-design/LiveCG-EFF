using SkiaSharp;
namespace LiveCG.Core;
public sealed record DigitRollSlot(SKRect Window,float[] FontSizes);
/// <summary>Original glyph rectangles are immutable. Rolling viewports use the space between glyph centers.</summary>
public static class DigitRollLayout
{
    public static List<DigitRollSlot> Create(IReadOnlyList<SKRect> cells,int width,IReadOnlyList<double>? dividers=null)
    {
        var manual=dividers?.Count>0?dividers.Append(0).Append(1).Distinct().Order().Select(d=>(float)(d*width)).ToArray():null;var slots=new List<DigitRollSlot>(cells.Count);
        using var font=new SKFont(SKTypeface.FromFamilyName("Arial",SKFontStyle.Bold),40);
        for(var i=0;i<cells.Count;i++)
        {
            var cell=cells[i];var left=i==0?0:(cells[i-1].MidX+cell.MidX)/2;var right=i==cells.Count-1?width:(cell.MidX+cells[i+1].MidX)/2;
            if(manual is not null){var at=Array.FindIndex(manual,x=>x>cell.MidX);if(at>0){left=manual[at-1];right=manual[at];}}
            var available=Math.Max(1,2*Math.Min(cell.MidX-left,right-cell.MidX));var sizes=new float[10];
            for(var digit=0;digit<10;digit++){font.Size=cell.Height/.74f;font.MeasureText(digit.ToString(),out var bounds);sizes[digit]=bounds.Width<=available?font.Size:font.Size*Math.Min(1,available/(bounds.Width+1));}
            slots.Add(new(new(left,cell.Top,right,cell.Bottom),sizes));
        }
        return slots;
    }
}
