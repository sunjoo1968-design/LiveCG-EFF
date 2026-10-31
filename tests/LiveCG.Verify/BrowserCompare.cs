using System.Text.Json;
using SkiaSharp;

internal static class BrowserCompare
{
    public static int Run(string root)
    {
        var captures=JsonSerializer.Deserialize<List<Capture>>(File.ReadAllText(Path.Combine(root,"artifacts/v010-browser-captures.json")),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;if(captures.Count<16)throw new InvalidDataException("브라우저 비교 캡처가 부족합니다. verify-browser.mjs를 먼저 성공시키세요.");var results=new List<object>();var failures=0;
        var obsFile=Path.Combine(root,"artifacts/v010-obs-render-captures.json");if(File.Exists(obsFile))captures.AddRange(JsonSerializer.Deserialize<List<Capture>>(File.ReadAllText(obsFile),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!);
        foreach(var item in captures)
        {
            using var expected=SKBitmap.Decode(item.Expected);using var actual=SKBitmap.Decode(item.Actual);double sum=0;var changed=0;var pixels=expected.Width*expected.Height;
            if(expected.Width!=actual.Width||expected.Height!=actual.Height){Console.WriteLine("FAIL screenshot dimensions "+item.Package);failures++;continue;}
            using var diff=new SKBitmap(expected.Width,expected.Height);
            for(var y=0;y<expected.Height;y++)for(var x=0;x<expected.Width;x++)
            {
                var a=expected.GetPixel(x,y);var b=actual.GetPixel(x,y);var factor=item.Premultiplied?1:b.Alpha/255d;var channels=new[]{Math.Abs(a.Red*a.Alpha/255d-b.Red*factor),Math.Abs(a.Green*a.Alpha/255d-b.Green*factor),Math.Abs(a.Blue*a.Alpha/255d-b.Blue*factor),Math.Abs(a.Alpha-b.Alpha)};sum+=channels.Sum();var delta=channels.Max();if(delta>20)changed++;diff.SetPixel(x,y,new SKColor((byte)Math.Min(255,delta*3),0,0,255));
            }
            var mean=sum/(pixels*4);var fraction=(double)changed/pixels;var ok=mean<3&&fraction<.035;
            Console.WriteLine($"{(ok?"PASS":"FAIL")} {item.Package} t={item.Time:0.00} mean={mean:0.000} changed={fraction:P3}");if(!ok)failures++;
            results.Add(new{item.Package,item.Time,Mean=mean,Fraction=fraction,Passed=ok});PlaybackChecks.Save(diff,Path.Combine(Path.GetDirectoryName(item.Actual)!,"diff-"+Path.GetFileName(item.Actual)));
        }
        File.WriteAllText(Path.Combine(root,"artifacts/v010-browser-comparison.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine($"RESULT {captures.Count-failures}/{captures.Count} passed");return failures==0?0:1;
    }
    private sealed record Capture(string Package,double Time,string Expected,string Actual,bool Premultiplied=false);
}
