using System.Windows;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using System.Globalization;
using System.Windows.Threading;
using LiveCG.Core;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace LiveCG.App;
public sealed class AssetThumbnailConverter:IValueConverter
{
    public object? Convert(object value,Type targetType,object parameter,CultureInfo culture)
    {
        try{if(value is not string{Length:>0} data)return null;using var stream=new MemoryStream(System.Convert.FromBase64String(data));var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.StreamSource=stream;image.EndInit();image.Freeze();return image;}catch(FormatException){return null;}catch(NotSupportedException){return null;}
    }
    public object ConvertBack(object value,Type targetType,object parameter,CultureInfo culture)=>throw new NotSupportedException();
}
public sealed class AssetPreview:SKElement,IDisposable
{
    public static readonly DependencyProperty AssetProperty=DependencyProperty.Register(nameof(Asset),typeof(EffectAsset),typeof(AssetPreview),new PropertyMetadata(null,(d,_)=>((AssetPreview)d).Build()));
    public EffectAsset? Asset { get=>(EffectAsset?)GetValue(AssetProperty);set=>SetValue(AssetProperty,value); }
    private SKBitmap? _bitmap;private EffectScene? _scene;private EffectRegion? _region;private double _time;private readonly DispatcherTimer _clock=new(){Interval=TimeSpan.FromMilliseconds(65)};
    public AssetPreview()
    {
        Height=115;PaintSurface+=(_,e)=>
        {var c=e.Surface.Canvas;c.Clear(new SKColor(26,43,57));if(_scene is null||_region is null)return;var scale=Math.Min(e.Info.Width/400f,e.Info.Height/225f);c.Translate((e.Info.Width-400*scale)/2,(e.Info.Height-225*scale)/2);c.Scale(scale);_scene.Draw(c,[_region],_time);};
        _clock.Tick+=(_,_)=>{if(IsMouseOver&&Asset is not null){_time=(_time+.065)%(Asset.Duration+.6);InvalidateVisual();}};
        Loaded+=(_,_)=>{if(_scene is null)Build();_clock.Start();};Unloaded+=(_,_)=>Dispose();MouseEnter+=(_,_)=>{_time=0;InvalidateVisual();};MouseLeave+=(_,_)=>{_time=(Asset?.Duration??1)*.4;InvalidateVisual();};
    }
    private void Build()
    {
        _scene?.Dispose();_bitmap?.Dispose();_scene=null;_bitmap=null;if(Asset is null)return;
        var stage=EffectSample.Stage(null,400,225);_bitmap=stage.Bitmap;_scene=new(_bitmap);_region=new(){Bounds=stage.Bounds,Background=BackgroundMode.Transparent,Clips=Asset.Clips.Select(c=>c.Copy(false)).ToList()};_time=Asset.Duration*.4;InvalidateVisual();
    }
    public void Dispose(){_clock.Stop();_scene?.Dispose();_bitmap?.Dispose();_scene=null;_bitmap=null;}
}
public sealed class EffectThumbnail : SKElement, IDisposable
{
    private readonly DispatcherTimer _timer;
    private readonly SKBitmap _sample;
    private readonly EffectScene _scene;
    private readonly EffectRegion _region;
    private double _time = .55;
    public EffectKind Kind { get; }
    public bool Selected { get; set; }
    public EffectThumbnail(EffectKind kind)
    {
        Kind=kind;Height=72;Margin=new Thickness(3);Cursor=System.Windows.Input.Cursors.Hand;
        _sample=new SKBitmap(280,140);
        using(var c=new SKCanvas(_sample))
        {
            c.Clear(new SKColor(30,44,60));
            using var p=new SKPaint{IsAntialias=true,Color=new SKColor(111,28,45)};c.DrawRoundRect(SKRect.Create(35,28,210,84),10,10,p);
            using var font=new SKFont(SKTypeface.FromFamilyName("Arial",SKFontStyle.Bold),38);p.Color=SKColors.White;c.DrawText("87,070",74,82,font,p);
        }
        _scene=new(_sample);
        _region=new(){Bounds=new(.14,.22,.72,.56),Clips=[new(){Duration=1.5,Options=new(){Kind=kind,Strength=StrengthLevel.Strong}}]};
        if(kind==EffectKind.TravelRoute){_region.Bounds=new(.05,.05,.9,.9);_region.OverlayOnly=true;_region.Background=BackgroundMode.KeepOriginal;_region.Clips[0].Route=TravelRoute.Example() with{Arrival=false,IconSize=34};_region.Clips[0].Duration=2;}
        PaintSurface+=Paint;
        _timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(65)};
        _timer.Tick+=(_,_)=>{if(IsMouseOver||Selected){_time+=.065;InvalidateVisual();}};
        MouseEnter+=(_,_)=>{_time=0;InvalidateVisual();};
        MouseLeave+=(_,_)=>{if(!Selected){_time=.55;InvalidateVisual();}};
        Loaded+=(_,_)=>_timer.Start();Unloaded+=(_,_)=>_timer.Stop();
    }
    private void Paint(object? sender,SKPaintSurfaceEventArgs e)
    {
        var c=e.Surface.Canvas;c.Clear(Selected?new SKColor(50,140,114):new SKColor(38,54,70));
        c.Save();c.ClipRect(SKRect.Create(3,3,e.Info.Width-6,e.Info.Height*.72f));var scale=Math.Min((e.Info.Width-6)/280f,(e.Info.Height*.72f)/140f);c.Translate((e.Info.Width-280*scale)/2,3);c.Scale(scale);
        _scene.Draw(c,[_region],_time%2.25);c.Restore();
        using var p=new SKPaint{Color=SKColors.White,IsAntialias=true};
        using var font=new SKFont(SKTypeface.FromFamilyName("Malgun Gothic"),Math.Max(10,e.Info.Height*.16f));
        c.DrawText(ProductInfo.EffectName(Kind),6,e.Info.Height-5,font,p);
    }
    public void Dispose(){_timer.Stop();_scene.Dispose();_sample.Dispose();}
}
