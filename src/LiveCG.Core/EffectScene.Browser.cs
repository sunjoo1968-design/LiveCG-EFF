using SkiaSharp;

namespace LiveCG.Core;

public sealed partial class EffectScene
{
    /// <summary>Reuse the native extraction and edge treatment. Browser only performs time-dependent compositing.</summary>
    public List<object> WriteBrowserAssets(IReadOnlyList<EffectRegion> regions,string directory,Action<double>? progress,CancellationToken token)
    {
        Directory.CreateDirectory(directory);var output=new List<object>();var count=0;
        string Save(SKImage image,string name)
        {token.ThrowIfCancellationRequested();using var data=image.Encode(SKEncodedImageFormat.Png,100);File.WriteAllBytes(Path.Combine(directory,name),data.ToArray());return "assets/"+name;}
        string Bitmap(SKBitmap bitmap,string name){using var image=SKImage.FromBitmap(bitmap);return Save(image,name);}
        foreach(var region in regions)
        {
            token.ThrowIfCancellationRequested();var layer=GetLayer(region);var rect=RegionRaster.Rect(region,Width,Height);var prefix=region.Id.ToString("N");
            using var raw=RawCrop(region,rect);using var shape=ShapeCoverage(region,rect.Width,rect.Height);using var numericShape=ShapeCoverage(region,rect.Width,rect.Height,false);
            var parent=region.ParentId is { } id?regions.First(r=>r.Id==id):region;
            var parentRect=RegionRaster.Rect(parent,Width,Height);var masks=new List<object>();
            var routes=new List<object>();foreach(var clip in region.Clips.Where(c=>c.Route is not null))
            {
                var route=clip.Route!;var icons=new Dictionary<string,string>();foreach(var kind in Enum.GetValues<TravelIcon>()){using var icon=RouteRenderer.Icon(route,kind);icons[kind.ToString()]=Bitmap(icon,prefix+"-"+clip.Id.ToString("N")+"-"+kind+".png");}routes.Add(new{id=clip.Id,segments=RouteRenderer.Geometry(route,rect.Width,rect.Height).Segments,totalLength=RouteRenderer.Geometry(route,rect.Width,rect.Height).TotalLength,color=Css(SKColor.Parse(route.Color)),outlineColor=Css(SKColor.Parse(route.OutlineColor)),icons});
            }
            foreach(var mask in region.Masks)
            {
                token.ThrowIfCancellationRequested();var m=mask.Copy(false);m.Start=0;m.Duration=1;m.Opacity=1;m.Invert=false;m.Motion=null;m.EndMode=MaskEndMode.Hold;
                using var raster=_maskRenderer.RenderOne(m,parentRect.Width,parentRect.Height,Width,Height,parentRect,.5);
                masks.Add(new{id=mask.Id,image=Bitmap(raster,prefix+"-"+mask.Id.ToString("N")+"-mask.png")});
            }
            output.Add(new{id=region.Id,x=rect.Left,y=rect.Top,width=rect.Width,height=rect.Height,customRaster=RegionRaster.Custom(region),
                raw=Bitmap(raw,prefix+"-raw.png"),shape=Bitmap(shape,prefix+"-shape.png"),numericShape=Bitmap(numericShape,prefix+"-numeric-shape.png"),image=Save(layer.Image,prefix+"-image.png"),backing=Save(layer.BackingImage,prefix+"-backing.png"),erase=Save(layer.EraseImage!,prefix+"-erase.png"),fill=Save(layer.FillImage!,prefix+"-fill.png"),
                background=Css(layer.Background),ink=Css(layer.Ink),digits=layer.Digits.Select(r=>new{x=r.Left,y=r.Top,width=r.Width,height=r.Height}).ToArray(),masks,routes});
            progress?.Invoke((double)++count/Math.Max(1,regions.Count));
        }
        return output;
    }
    private static string Css(SKColor color)=>FormattableString.Invariant($"rgba({color.Red},{color.Green},{color.Blue},{color.Alpha/255d:0.########})");
}
