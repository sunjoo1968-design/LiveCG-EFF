using System.Windows;
using System.Windows.Media;
using LiveCG.Core;
using SkiaSharp;

namespace LiveCG.App;
public partial class MainWindow
{
    private bool _pickingEffectColor;
    private void ShowEffectColor()
    {var options=_clip?.Options??_defaultEffect;var text=options.Color;var color=text is null?ProductInfo.DefaultEffectColor(options.Kind):SKColor.Parse(text);EffectColorSwatch.Background=new SolidColorBrush(Color.FromArgb(color.Alpha,color.Red,color.Green,color.Blue));EffectColorSwatch.ToolTip=text??"효과별 기본색";}
    private void SetEffectColor(SKColor? color)
    {
        if(_busy)return;var options=(_clip?.Options??_defaultEffect) with{Color=color is { } c?ColorPickerWindow.Hex(c):null};
        if(_clip is not null)_clip.Options=options;else _defaultEffect=options;ShowEffectColor();MarkEdited();RefreshTimeline();
    }
    private void ChooseEffectColor(object sender,RoutedEventArgs e)
    {
        if(_busy)return;var options=_clip?.Options??_defaultEffect;var text=options.Color;var picker=new ColorPickerWindow(text is null?ProductInfo.DefaultEffectColor(options.Kind):SKColor.Parse(text)){Owner=_effectsWindow??this};
        if(picker.ShowDialog()==true)SetEffectColor(picker.SelectedColor);
        else if(picker.PickSourceRequested&&_region is not null){PickBackgroundColor(this,new RoutedEventArgs());_pickingEffectColor=true;Notify("원본에서 효과에 쓸 색을 클릭하세요.");}
    }
    private void ResetEffectColor(object sender,RoutedEventArgs e)=>SetEffectColor(null);
}
