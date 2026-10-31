using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LiveCG.Core;
using SkiaSharp;
using WColor=System.Windows.Media.Color;

namespace LiveCG.App;

public sealed class ColorPlane:FrameworkElement
{
    public double Hue { get; set; }
    public double Saturation { get; set; }
    public event Action<double,double>? Changed;
    public ColorPlane()
    {
        Cursor=Cursors.Cross;MinHeight=160;
        MouseLeftButtonDown+=(_,e)=>{CaptureMouse();Pick(e.GetPosition(this));e.Handled=true;};
        MouseMove+=(_,e)=>{if(IsMouseCaptured)Pick(e.GetPosition(this));};
        MouseLeftButtonUp+=(_,_)=>ReleaseMouseCapture();
    }
    public void Pick(Point point)
    {
        Hue=Math.Clamp(point.X/Math.Max(1,ActualWidth),0,1)*359;
        Saturation=1-Math.Clamp(point.Y/Math.Max(1,ActualHeight),0,1);
        Changed?.Invoke(Hue,Saturation);InvalidateVisual();
    }
    protected override void OnRender(DrawingContext dc)
    {
        var gradient=new LinearGradientBrush{StartPoint=new(0,0),EndPoint=new(1,0)};
        foreach(var item in new[]{(0d,Colors.Red),(1d/6,Colors.Yellow),(2d/6,Colors.Lime),(.5,Colors.Cyan),(4d/6,Colors.Blue),(5d/6,Colors.Magenta),(1d,Colors.Red)})gradient.GradientStops.Add(new(item.Item2,item.Item1));
        var rect=new Rect(0,0,ActualWidth,ActualHeight);dc.DrawRectangle(gradient,null,rect);
        dc.DrawRectangle(new LinearGradientBrush(Colors.Transparent,Colors.White,90),new Pen(Brushes.Gray,1),rect);
        var at=new Point(Hue/359*ActualWidth,(1-Saturation)*ActualHeight);
        foreach(var line in new[]{(Brushes.Black,3d),(Brushes.White,1d)})
        {var p=new Pen(line.Item1,line.Item2);dc.DrawEllipse(null,p,at,5,5);dc.DrawLine(p,new(at.X-9,at.Y),new(at.X+9,at.Y));dc.DrawLine(p,new(at.X,at.Y-9),new(at.X,at.Y+9));}
    }
}
public sealed class ValueStrip:FrameworkElement
{
    public SKColor TopColor { get; set; }=SKColors.Red;
    public double Value { get; set; }=1;
    public event Action<double>? Changed;
    public ValueStrip()
    {
        Width=22;Cursor=Cursors.Cross;
        MouseLeftButtonDown+=(_,e)=>{CaptureMouse();Pick(e.GetPosition(this).Y);e.Handled=true;};
        MouseMove+=(_,e)=>{if(IsMouseCaptured)Pick(e.GetPosition(this).Y);};MouseLeftButtonUp+=(_,_)=>ReleaseMouseCapture();
    }
    public void Pick(double y){Value=1-Math.Clamp(y/Math.Max(1,ActualHeight),0,1);Changed?.Invoke(Value);InvalidateVisual();}
    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(new LinearGradientBrush(WColor.FromRgb(TopColor.Red,TopColor.Green,TopColor.Blue),Colors.Black,90),new Pen(Brushes.Gray,1),new Rect(0,0,ActualWidth,ActualHeight));
        var y=(1-Value)*ActualHeight;
        dc.DrawLine(new Pen(Brushes.Black,4),new(0,y),new(ActualWidth,y));dc.DrawLine(new Pen(Brushes.White,2),new(0,y),new(ActualWidth,y));
    }
}
public sealed class ColorNumber:Grid
{
    public TextBox Input { get; }=new(){Margin=new Thickness(0),MinWidth=45,Padding=new Thickness(5,3,5,3)};
    public int Maximum { get; }
    public bool IsValid=>int.TryParse(Input.Text,out var n)&&n>=0&&n<=Maximum;
    public int Value=>int.TryParse(Input.Text,out var n)?Math.Clamp(n,0,Maximum):0;
    public event Action<int>? Changed;
    private bool _setting;
    public ColorNumber(int maximum)
    {
        Maximum=maximum;ColumnDefinitions.Add(new());ColumnDefinitions.Add(new(){Width=new GridLength(20)});Children.Add(Input);
        var arrows=new StackPanel();Grid.SetColumn(arrows,1);Children.Add(arrows);
        foreach(var delta in new[]{1,-1}){var b=new Button{Content=delta>0?"▴":"▾",Padding=new Thickness(0),Margin=new Thickness(0),Height=15};arrows.Children.Add(b);b.Click+=(_,_)=>{Input.Text=Math.Clamp(Value+delta,0,Maximum).ToString();};}
        Input.TextChanged+=(_,_)=>{Input.BorderBrush=IsValid?new SolidColorBrush(Color.FromRgb(66,83,102)):Brushes.Salmon;if(!_setting&&IsValid)Changed?.Invoke(Value);};
        Input.PreviewKeyDown+=(_,e)=>{if(e.Key is Key.Up or Key.Down){Input.Text=Math.Clamp(Value+(e.Key==Key.Up?1:-1),0,Maximum).ToString();e.Handled=true;}};
    }
    public void Set(int value){if(Input.IsKeyboardFocusWithin&&IsValid&&Value==value)return;_setting=true;Input.Text=value.ToString(CultureInfo.InvariantCulture);_setting=false;}
}
public sealed class ColorPickerWindow:Window
{
    public SKColor SelectedColor { get; private set; }
    public bool PickSourceRequested { get; private set; }
    public ColorPlane Plane { get; }=new();
    public ValueStrip BrightnessStrip { get; }=new();
    public ColorNumber HueInput { get; }=new(359);
    public ColorNumber SaturationInput { get; }=new(255);
    public ColorNumber ValueInput { get; }=new(255);
    public ColorNumber RedInput { get; }=new(255);
    public ColorNumber GreenInput { get; }=new(255);
    public ColorNumber BlueInput { get; }=new(255);
    public ColorNumber AlphaInput { get; }=new(255);
    private readonly TextBox _hex=new();
    private readonly Border _current=new(),_next=new();
    private readonly TextBlock _status=new(){TextWrapping=TextWrapping.Wrap,Foreground=Brushes.Salmon};
    private readonly List<Button> _customButtons=[];
    private readonly List<SKColor> _custom=[];
    private bool _sync;private double _hue,_sat,_value;private int _slot;
    private static string CustomPath=>Path.Combine(DocumentStore.DataDirectory,"custom-colors.json");
    private static string RecentPath=>Path.Combine(DocumentStore.DataDirectory,"recent-colors.json");
    public ColorPickerWindow(SKColor initial)
    {
        Title="색상표 · "+ProductInfo.Credit;Width=730;Height=650;MinWidth=700;MinHeight=600;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        Background=new SolidColorBrush(WColor.FromRgb(30,33,41));Foreground=Brushes.White;
        var root=new DockPanel{Margin=new Thickness(15)};Content=root;
        var bottom=new StackPanel();DockPanel.SetDock(bottom,Dock.Bottom);root.Children.Add(bottom);
        bottom.Children.Add(_status);var actions=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right};bottom.Children.Add(actions);
        AddButton(actions,"확인",()=>Accept());var cancel=AddButton(actions,"취소",()=>Close());cancel.IsCancel=true;
        bottom.Children.Add(new TextBlock{Text=ProductInfo.Credit,FontSize=10,Foreground=Brushes.LightSlateGray,HorizontalAlignment=HorizontalAlignment.Right});
        var grid=new Grid();grid.ColumnDefinitions.Add(new(){Width=new GridLength(230)});grid.ColumnDefinitions.Add(new(){Width=new GridLength(14)});grid.ColumnDefinitions.Add(new());grid.RowDefinitions.Add(new(){Height=new GridLength(250)});grid.RowDefinitions.Add(new(){Height=new GridLength(17)});grid.RowDefinitions.Add(new());root.Children.Add(new ScrollViewer{Content=grid,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        var basic=new StackPanel();grid.Children.Add(basic);Label(basic,"기본 색상");
        var basics=new System.Windows.Controls.Primitives.UniformGrid{Columns=8,Rows=6};basic.Children.Add(basics);
        foreach(var hex in new[]{"#000000","#800000","#008000","#805000","#008080","#00A000","#00C000","#80C000","#000080","#800080","#006080","#A06080","#609090","#A0A080","#00C080","#80E080","#0000FF","#8000FF","#0080FF","#C080FF","#00FFFF","#80C0FF","#80FFFF","#C0FFFF","#800040","#FF0000","#808000","#FF8000","#00C040","#00A080","#4080C0","#8080C0","#804000","#FF00FF","#80FF00","#FFFF00","#0080A0","#A06040","#C0C0C0","#FFFFFF","#804080","#FF80FF","#C0FF00","#FF8080","#4080FF","#FFFF80","#000040","#404040"})
        {var color=SKColor.Parse(hex);var b=Swatch(color);b.Click+=(_,_)=>SetColor(color);basics.Children.Add(b);}
        AddButton(basic,"원본색 찍기",()=>{PickSourceRequested=true;DialogResult=false;}).ToolTip="이 창을 닫고 원본 편집화면의 스포이드를 켭니다. ESC로 취소합니다.";
        var planeGrid=new Grid();planeGrid.ColumnDefinitions.Add(new());planeGrid.ColumnDefinitions.Add(new(){Width=new GridLength(16)});planeGrid.ColumnDefinitions.Add(new(){Width=new GridLength(22)});Grid.SetColumn(planeGrid,2);grid.Children.Add(planeGrid);planeGrid.Children.Add(Plane);Grid.SetColumn(BrightnessStrip,2);planeGrid.Children.Add(BrightnessStrip);
        var customPanel=new StackPanel();Grid.SetRow(customPanel,2);grid.Children.Add(customPanel);Label(customPanel,"사용자 색상");
        _custom.AddRange(ReadColors());while(_custom.Count<16)_custom.Add(SKColors.White);
        var customs=new System.Windows.Controls.Primitives.UniformGrid{Columns=8,Rows=2};customPanel.Children.Add(customs);
        for(var i=0;i<16;i++){var slot=i;var b=Swatch(_custom[i]);_customButtons.Add(b);b.Click+=(_,_)=>{_slot=slot;SetColor(_custom[slot]);UpdateCustoms();};customs.Children.Add(b);}
        AddButton(customPanel,"사용자 색상에 추가",()=>{try{SaveCustomColor(_slot);_slot=(_slot+1)%16;UpdateCustoms();_status.Text="사용자 색상을 저장했습니다.";}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){_status.Text=ex.Message;}});
        Label(customPanel,"현재 색상 / 새 색상");
        var samples=new System.Windows.Controls.Primitives.UniformGrid{Columns=2,Height=65,Margin=new Thickness(3)};samples.Children.Add(_current);samples.Children.Add(_next);customPanel.Children.Add(samples);_current.Background=Brush(initial);_current.ToolTip="현재 색상";_next.ToolTip="새 색상";
        var values=new Grid();values.ColumnDefinitions.Add(new());values.ColumnDefinitions.Add(new());Grid.SetColumn(values,2);Grid.SetRow(values,2);grid.Children.Add(values);
        for(var i=0;i<5;i++)values.RowDefinitions.Add(new(){Height=new GridLength(43)});
        void Field(string label,ColorNumber input,int row,int col){var p=new DockPanel{Margin=new Thickness(3)};var title=new TextBlock{Text=label,Width=43};DockPanel.SetDock(title,Dock.Left);p.Children.Add(title);p.Children.Add(input);Grid.SetRow(p,row);Grid.SetColumn(p,col);values.Children.Add(p);}
        Field("Hue",HueInput,0,0);Field("Red",RedInput,0,1);Field("Sat",SaturationInput,1,0);Field("Green",GreenInput,1,1);Field("Val",ValueInput,2,0);Field("Blue",BlueInput,2,1);Field("Alpha",AlphaInput,3,1);
        var html=new DockPanel{Margin=new Thickness(3)};html.Children.Add(new TextBlock{Text="HTML",Width=43});html.Children.Add(_hex);Grid.SetRow(html,4);Grid.SetColumnSpan(html,2);values.Children.Add(html);
        Plane.Changed+=(h,s)=>SetHsv(h,s*255,_value*255);BrightnessStrip.Changed+=v=>SetHsv(_hue,_sat*255,v*255);
        HueInput.Changed+=v=>SetHsv(v,_sat*255,_value*255);SaturationInput.Changed+=v=>SetHsv(_hue,v,_value*255);ValueInput.Changed+=v=>SetHsv(_hue,_sat*255,v);
        foreach(var field in new[]{RedInput,GreenInput,BlueInput})field.Changed+=_=>{if(!_sync)SetColor(new((byte)RedInput.Value,(byte)GreenInput.Value,(byte)BlueInput.Value,SelectedColor.Alpha));};
        AlphaInput.Changed+=v=>{if(!_sync){SelectedColor=SelectedColor.WithAlpha((byte)v);Sync();}};
        _hex.LostFocus+=(_,_)=>{if(!_sync&&!SetHex(_hex.Text))_status.Text="HTML 색상 형식을 확인하세요.";};
        SetColor(initial);UpdateCustoms();
    }
    private static Button AddButton(Panel panel,string text,Action click){var b=new Button{Content=text,Margin=new Thickness(3),Padding=new Thickness(9,7,9,7)};b.Click+=(_,_)=>click();panel.Children.Add(b);return b;}
    private static void Label(Panel panel,string text)=>panel.Children.Add(new TextBlock{Text=text,Margin=new Thickness(3,3,3,9)});
    private static Button Swatch(SKColor c)=>new(){Background=Brush(c),Height=24,Margin=new Thickness(3),Padding=new Thickness(0),BorderThickness=new Thickness(1),BorderBrush=Brushes.Gray,ToolTip=Hex(c),Template=SwatchTemplate()};
    private static ControlTemplate SwatchTemplate(){var template=new ControlTemplate(typeof(Button));var border=new FrameworkElementFactory(typeof(Border));border.SetBinding(Border.BackgroundProperty,new System.Windows.Data.Binding("Background"){RelativeSource=System.Windows.Data.RelativeSource.TemplatedParent});border.SetBinding(Border.BorderBrushProperty,new System.Windows.Data.Binding("BorderBrush"){RelativeSource=System.Windows.Data.RelativeSource.TemplatedParent});border.SetBinding(Border.BorderThicknessProperty,new System.Windows.Data.Binding("BorderThickness"){RelativeSource=System.Windows.Data.RelativeSource.TemplatedParent});template.VisualTree=border;return template;}
    public static string Hex(SKColor color)=>color.Alpha==255?$"#{color.Red:X2}{color.Green:X2}{color.Blue:X2}":$"#{color.Alpha:X2}{color.Red:X2}{color.Green:X2}{color.Blue:X2}";
    private static Brush Brush(SKColor c)=>new SolidColorBrush(WColor.FromArgb(c.Alpha,c.Red,c.Green,c.Blue));
    public bool SetHex(string value){if(!SKColor.TryParse(value.Trim(),out var color))return false;SetColor(color);return true;}
    public void SetColor(SKColor color){SelectedColor=color;color.ToHsv(out var h,out var s,out var v);_hue=h;_sat=s/100;_value=v/100;Sync();}
    public void SetHsv(double hue,double saturation,double value){if(_sync)return;_hue=Math.Clamp(hue,0,359);_sat=Math.Clamp(saturation/255,0,1);_value=Math.Clamp(value/255,0,1);SelectedColor=SKColor.FromHsv((float)_hue,(float)(_sat*100),(float)(_value*100),SelectedColor.Alpha);Sync();}
    private void Sync()
    {
        _sync=true;HueInput.Set((int)Math.Round(_hue));SaturationInput.Set((int)Math.Round(_sat*255));ValueInput.Set((int)Math.Round(_value*255));RedInput.Set(SelectedColor.Red);GreenInput.Set(SelectedColor.Green);BlueInput.Set(SelectedColor.Blue);AlphaInput.Set(SelectedColor.Alpha);_hex.Text=Hex(SelectedColor);_next.Background=Brush(SelectedColor);
        Plane.Hue=_hue;Plane.Saturation=_sat;Plane.InvalidateVisual();BrightnessStrip.TopColor=SKColor.FromHsv((float)_hue,(float)(_sat*100),100);BrightnessStrip.Value=_value;BrightnessStrip.InvalidateVisual();_status.Text="";_sync=false;
    }
    public bool ValidateInputs()=>new[]{HueInput,SaturationInput,ValueInput,RedInput,GreenInput,BlueInput,AlphaInput}.All(n=>n.IsValid)&&SKColor.TryParse(_hex.Text.Trim(),out _);
    private void Accept(){if(!ValidateInputs()||!SetHex(_hex.Text)){_status.Text="HSV/RGBA 입력 범위와 HTML 색상을 확인하세요.";return;}SaveRecent();DialogResult=true;}
    public IReadOnlyList<SKColor> CustomColors=>_custom;
    public void SaveCustomColor(int slot)
    {
        if(slot is <0 or >=16)throw new ArgumentOutOfRangeException(nameof(slot));var values=_custom.Select(Hex).ToList();values[slot]=Hex(SelectedColor);Directory.CreateDirectory(DocumentStore.DataDirectory);var temporary=CustomPath+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.WriteAllText(temporary,JsonSerializer.Serialize(values));File.Move(temporary,CustomPath,true);_custom[slot]=SelectedColor;UpdateCustoms();}finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
    private void UpdateCustoms(){for(var i=0;i<_customButtons.Count;i++){_customButtons[i].Background=Brush(_custom[i]);_customButtons[i].ToolTip=Hex(_custom[i]);_customButtons[i].BorderBrush=i==_slot?Brushes.Gold:Brushes.Gray;_customButtons[i].BorderThickness=new Thickness(i==_slot?2:1);}}
    private static List<SKColor> ReadColors(){try{var path=File.Exists(CustomPath)?CustomPath:RecentPath;return (JsonSerializer.Deserialize<List<string>>(File.ReadAllText(path))??[]).Take(16).Select(v=>SKColor.TryParse(v,out var c)?c:SKColors.White).ToList();}catch(Exception ex)when(ex is IOException or JsonException){return [];}}
    private void SaveRecent(){try{Directory.CreateDirectory(DocumentStore.DataDirectory);File.WriteAllText(RecentPath,JsonSerializer.Serialize(new[]{Hex(SelectedColor)}));}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){_status.Text=ex.Message;}}
}
