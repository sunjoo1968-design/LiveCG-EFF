using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using LiveCG.Core;
using SkiaSharp;

namespace LiveCG.App;
public sealed class LayerRowConverter:IValueConverter
{
    public object Convert(object value,Type type,object parameter,CultureInfo culture)
    {
        if(value is not EffectRegion r)return "";
        return parameter?.ToString() switch
        {
            "indent"=>new Thickness(r.ParentId is null?0:16,0,0,0),
            "eye"=>r.Enabled?"◉":"○","lock"=>r.Locked?"▣":"□",
            "fold"=>r.Role==RegionRole.Group?(r.Collapsed?"▸":"▾"):"",
            "counts"=>$"{(r.Role==RegionRole.Group?"그룹 · ":r.ParentId is not null?"부분 · ":"")}효과 {r.Clips.Count}"+(r.Masks.Count>0?$" · 마스크 {r.Masks.Count}":""),
            _=>r.Name
        };
    }
    public object ConvertBack(object value,Type type,object parameter,CultureInfo culture)=>throw new NotSupportedException();
}
public sealed class LayerThumbnailConverter:IMultiValueConverter,IDisposable
{
    private SKBitmap? _source;private SKImage? _image;private string _path="";private long _stamp;
    private readonly Dictionary<Guid,(int Key,BitmapSource Image)> _cache=[];
    public object? Convert(object[] values,Type type,object parameter,CultureInfo culture)
    {
        if(values.Length<2||values[0] is not EffectRegion r||values[1] is not string path||!File.Exists(path))return null;
        try
        {
            var stamp=File.GetLastWriteTimeUtc(path).Ticks;
            if(_path!=path||_stamp!=stamp||_source is null){Dispose();_path=path;_stamp=stamp;_source=SourceImage.Read(path);if(_source is null)return null;_image=SKImage.FromBitmap(_source);}
            var hash=new HashCode();hash.Add(r.Bounds);hash.Add(r.Shape);foreach(var point in r.Points)hash.Add(point);var key=hash.ToHashCode();
            if(_cache.TryGetValue(r.Id,out var cached)&&cached.Key==key)return cached.Image;
            var rect=ObjectExtractor.PixelRect(r.Bounds,_source!.Width,_source.Height);using var bitmap=new SKBitmap(48,34);using var canvas=new SKCanvas(bitmap);canvas.Clear(new SKColor(27,41,52));
            var scale=Math.Min(46f/rect.Width,32f/rect.Height);var dest=SKRect.Create((48-rect.Width*scale)/2,(34-rect.Height*scale)/2,rect.Width*scale,rect.Height*scale);
            using(var shape=RegionMask.Create(r.Shape,dest,r.Points)){canvas.Save();canvas.ClipPath(shape,antialias:true);canvas.DrawImage(_image!,rect,dest,new SKSamplingOptions(SKFilterMode.Linear));canvas.Restore();}
            using var image=SKImage.FromBitmap(bitmap);using var data=image.Encode(SKEncodedImageFormat.Png,100);using var stream=new MemoryStream(data.ToArray());var display=new BitmapImage();display.BeginInit();display.CacheOption=BitmapCacheOption.OnLoad;display.StreamSource=stream;display.EndInit();display.Freeze();_cache[r.Id]=(key,display);return display;
        }catch(IOException){return null;}catch(ArgumentException){return null;}
    }
    public object[] ConvertBack(object value,Type[] types,object parameter,CultureInfo culture)=>throw new NotSupportedException();
    public void Dispose(){_image?.Dispose();_source?.Dispose();_image=null;_source=null;_cache.Clear();_path="";_stamp=0;}
}
