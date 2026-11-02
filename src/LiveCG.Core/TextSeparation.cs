using SkiaSharp;

namespace LiveCG.Core;
/// <summary>Conservative text matte. No erosion, dilation, blur, or tolerance-based removal of ink support.</summary>
public sealed class TextMatte(MaskBuffer mask,SKBitmap foreground):IDisposable
{
    public MaskBuffer Mask { get; }=mask;
    public SKBitmap Foreground { get; }=foreground;
    public void Dispose()=>Foreground.Dispose();
}
public static class TextSeparation
{
    private static bool Same(SKColor a,SKColor b)=>a.Red==b.Red&&a.Green==b.Green&&a.Blue==b.Blue;
    private static int Contrast(SKColor a,SKColor b){var r=a.Red-b.Red;var g=a.Green-b.Green;var c=a.Blue-b.Blue;return r*r+g*g+c*c;}
    public static long Calculations;
    private static SKColor? InferInk(SKColor[] pixels,SKColor[] backgrounds)
    {
        if(backgrounds.Length==0)return null;
        var bins=new Dictionary<int,(int Count,int Contrast,SKColor Color)>();
        foreach(var c in pixels){if(c.Alpha!=255)continue;var score=int.MaxValue;var candidate=true;foreach(var bg in backgrounds){if(Math.Max(Math.Abs(c.Red-bg.Red),Math.Max(Math.Abs(c.Green-bg.Green),Math.Abs(c.Blue-bg.Blue)))<=65){candidate=false;break;}score=Math.Min(score,Contrast(c,bg));}if(!candidate)continue;var key=(c.Red/8<<10)|(c.Green/8<<5)|c.Blue/8;bins.TryGetValue(key,out var old);bins[key]=(old.Count+1,Math.Max(old.Contrast,score),score>old.Contrast?c:old.Color);}
        return bins.Count==0?null:bins.Values.OrderByDescending(v=>v.Count).ThenByDescending(v=>v.Contrast).First().Color;
    }
    public static TextMatte Extract(SKBitmap source,SKColor background,SKColor? ink=null,TextSeparationSettings? settings=null)
    {
        Interlocked.Increment(ref Calculations);
        var w=source.Width;var h=source.Height;var n=checked(w*h);if(n>16_000_000)throw new InvalidDataException("글자 분리 범위는 최대1,600만 픽셀입니다.");
        var colors=new SKColor[n];var contrast=new int[n];var horizontal=new int[n];var alpha=new byte[n];var output=source.Copy();
        for(var y=0;y<h;y++)for(var x=0;x<w;x++){var i=y*w+x;colors[i]=source.GetPixel(x,y);contrast[i]=colors[i].Alpha==255?Contrast(colors[i],background):0;}
        var backgrounds=(settings?.BackgroundColors.Select(SKColor.Parse)??[]).Append(background).Where(c=>c.Alpha==255).Distinct().ToArray();
        // Automatic protection must run before the background range can discard faint antialiased number edges.
        if(settings?.RangeMode==true)ink??=InferInk(colors,backgrounds);
        for(var y=0;y<h;y++)for(var x=0;x<w;x++){var best=y*w+x;for(var dx=-3;dx<=3;dx++){var xx=x+dx;if(xx>=0&&xx<w&&contrast[y*w+xx]>contrast[best])best=y*w+xx;}horizontal[y*w+x]=best;}
        for(var y=0;y<h;y++)for(var x=0;x<w;x++)
        {
            var i=y*w+x;var c=colors[i];if(c.Alpha==0){alpha[i]=0;continue;}
            if(c.Alpha<255||background.Alpha<255){alpha[i]=255;continue;}
            if(Same(c,background)){alpha[i]=0;continue;}
            var best=horizontal[i];for(var dy=-3;dy<=3;dy++){var yy=y+dy;if(yy>=0&&yy<h&&contrast[horizontal[yy*w+x]]>contrast[best])best=horizontal[yy*w+x];}
            var f=ink??colors[best];var r=(double)f.Red-background.Red;var g=(double)f.Green-background.Green;var b=(double)f.Blue-background.Blue;var length=r*r+g*g+b*b;
            alpha[i]=255;if(length<1)continue;
            var coverage=Math.Clamp(((c.Red-background.Red)*r+(c.Green-background.Green)*g+(c.Blue-background.Blue)*b)/length,0,1);
            var error=Math.Max(Math.Abs(background.Red+coverage*r-c.Red),Math.Max(Math.Abs(background.Green+coverage*g-c.Green),Math.Abs(background.Blue+coverage*b-c.Blue)));
            if(coverage<=0||error>1)continue;
            alpha[i]=(byte)Math.Clamp((int)Math.Round(coverage*255),1,255);output.SetPixel(x,y,f.WithAlpha(255));
        }
        if(settings?.RangeMode==true)
        {
            double Distance(SKColor a,SKColor b)=>Math.Max(Math.Abs(a.Red-b.Red),Math.Max(Math.Abs(a.Green-b.Green),Math.Abs(a.Blue-b.Blue)));
            for(var y=0;y<h;y++)for(var x=0;x<w;x++)
            {
                var i=y*w+x;var c=colors[i];if(c.Alpha<255)continue;
                if(backgrounds.Any(bg=>Same(c,bg))){alpha[i]=0;continue;}
                var protectedEdge=false;var bestError=double.MaxValue;var bestCoverage=0d;var minCoverage=1d;var maxCoverage=0d;
                if(ink is { } f)foreach(var bg in backgrounds)
                {
                    double r=f.Red-bg.Red,g=f.Green-bg.Green,b=f.Blue-bg.Blue;var length=r*r+g*g+b*b;if(length<1)continue;
                    var a=((c.Red-bg.Red)*r+(c.Green-bg.Green)*g+(c.Blue-bg.Blue)*b)/length;
                    if(a<=0||a>1)continue;var error=Math.Max(Math.Abs(bg.Red+a*r-c.Red),Math.Max(Math.Abs(bg.Green+a*g-c.Green),Math.Abs(bg.Blue+a*b-c.Blue)));
                    if(error<=1){protectedEdge=true;minCoverage=Math.Min(minCoverage,a);maxCoverage=Math.Max(maxCoverage,a);if(error<bestError){bestError=error;bestCoverage=a;}}
                }
                if(protectedEdge)
                {
                    // More than one possible background can explain this edge. Keep its original sample rather than change stroke weight.
                    if(maxCoverage-minCoverage>2d/255){alpha[i]=255;output.SetPixel(x,y,c);}
                    else{alpha[i]=(byte)Math.Clamp((int)Math.Round(bestCoverage*255),1,255);output.SetPixel(x,y,ink!.Value.WithAlpha(255));}
                    continue;
                }
                if(ink is { } color&&Distance(c,color)<=settings.InkTolerance){alpha[i]=255;output.SetPixel(x,y,c);continue;}
                if(backgrounds.Any(bg=>Distance(c,bg)<=settings.ColorTolerance)){alpha[i]=0;continue;}
                // Unknown colors are retained; range mode never erodes a contour to hide uncertainty.
                alpha[i]=255;output.SetPixel(x,y,c);
            }
        }
        return new(new(w,h,alpha),output);
    }
    /// <summary>Return only background pixels in the clicked component; foreground cannot be selected by this tool.</summary>
    public static MaskBuffer HoleCandidate(SKBitmap source,MaskBuffer current,int x,int y,SKColor background)
    {
        var w=source.Width;var h=source.Height;var result=new byte[w*h];if(x<0||y<0||x>=w||y>=h||background.Alpha<255||!Same(source.GetPixel(x,y),background))return new(w,h,result);
        var seen=new bool[result.Length];var queue=new int[result.Length];var head=0;var tail=0;
        void Add(int index){if(seen[index])return;seen[index]=true;var color=source.GetPixel(index%w,index/w);if(color.Alpha!=255||!Same(color,background))return;queue[tail++]=index;}
        Add(y*w+x);while(head<tail){var i=queue[head++];if(current.Alpha[i]>0)result[i]=255;var xx=i%w;var yy=i/w;if(xx>0)Add(i-1);if(xx+1<w)Add(i+1);if(yy>0)Add(i-w);if(yy+1<h)Add(i+w);}return new(w,h,result);
    }
    public static void ApplyCandidate(MaskBuffer mask,MaskBuffer candidate){if(mask.Width!=candidate.Width||mask.Height!=candidate.Height)throw new InvalidDataException("후보 영역 크기가 다릅니다.");for(var i=0;i<mask.Alpha.Length;i++)if(candidate.Alpha[i]>0)mask.Alpha[i]=0;}
}
