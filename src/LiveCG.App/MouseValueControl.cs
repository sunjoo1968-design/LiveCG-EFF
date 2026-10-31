using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace LiveCG.App;

public sealed class MouseValueControl:StackPanel
{
    public Slider Slider { get; }=new(){Minimum=0,Maximum=100,SmallChange=1,IsMoveToPointEnabled=true,MinWidth=70,Margin=new Thickness(2,0,2,0)};
    private readonly TextBlock _label=new(){Foreground=new SolidColorBrush(Color.FromRgb(117,235,201)),FontSize=11,Margin=new Thickness(3,2,3,0)};
    private bool _sync,_gesture;private double _beforeValue,_labelX;public string Unit { get; set; }="";
    public event Action<double>? ValueEdited;public event Action? GestureStarted;public event Action<bool>? GestureEnded;
    public double Value=>Slider.Value;
    public MouseValueControl()
    {
        Children.Add(_label);Children.Add(Slider);Slider.ValueChanged+=(_,_)=>{ShowValue();if(!_sync)ValueEdited?.Invoke(Slider.Value);};
        Slider.PreviewMouseLeftButtonDown+=(_,_)=>BeginEdit();Slider.PreviewMouseLeftButtonUp+=(_,_)=>EndEdit(true);Slider.LostMouseCapture+=(_,_)=>EndEdit(true);
        Slider.KeyDown+=(_,e)=>{if(e.Key==Key.Escape){EndEdit(false);e.Handled=true;}};
        _label.Cursor=Cursors.SizeWE;_label.ToolTip="좌우 드래그로 세밀하게 조절 · Shift는 1/10씩";_label.MouseLeftButtonDown+=(_,e)=>{Slider.Focus();BeginEdit();_labelX=e.GetPosition(_label).X;_label.CaptureMouse();e.Handled=true;};_label.MouseMove+=(_,e)=>{if(_label.IsMouseCaptured&&_gesture)ScrubBy(e.GetPosition(_label).X-_labelX,Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));};_label.MouseLeftButtonUp+=(_,e)=>{EndEdit(true);_label.ReleaseMouseCapture();e.Handled=true;};_label.LostMouseCapture+=(_,_)=>EndEdit(true);
    }
    private void ShowValue()=>_label.Text=Slider.Value.ToString("0.###",CultureInfo.InvariantCulture)+Unit+"  ↔";
    public void Configure(double min,double max,string unit="",double step=.01){_sync=true;Slider.Minimum=min;Slider.Maximum=Math.Max(min+.001,max);Slider.SmallChange=step;Slider.LargeChange=Math.Max(step,(max-min)/20);Slider.TickFrequency=step;Unit=unit;_sync=false;ShowValue();}
    public void SetSilently(double value){_sync=true;if(value>Slider.Maximum)Slider.Maximum=Math.Ceiling(value*1.25);if(value<Slider.Minimum)Slider.Minimum=Math.Floor(value*1.25);Slider.Value=Math.Clamp(value,Slider.Minimum,Slider.Maximum);_sync=false;ShowValue();}
    public void BeginEdit(){if(_gesture)return;_beforeValue=Value;_gesture=true;GestureStarted?.Invoke();}
    public void ScrubBy(double pixels,bool fine=false){var step=Unit=="초"?.01:Unit=="px"?.1:1;EditValue(_beforeValue+pixels*step*(fine?.1:1));}
    public void EditValue(double value){Slider.Value=Math.Clamp(value,Slider.Minimum,Slider.Maximum);}
    public void EndEdit(bool commit){if(!_gesture)return;_gesture=false;if(!commit){SetSilently(_beforeValue);Slider.ReleaseMouseCapture();_label.ReleaseMouseCapture();}GestureEnded?.Invoke(commit);}
    public static MouseValueControl Attach(Control input,double min,double max,string unit,Action<double> changed,Action? begin=null,Action<bool>? end=null)
    {
        var parent=input.Parent as Panel??throw new InvalidOperationException("속성 패널을 찾지 못했습니다.");var at=parent.Children.IndexOf(input);parent.Children.Remove(input);var host=new StackPanel();parent.Children.Insert(at,host);
        var mouse=new MouseValueControl();mouse.Configure(min,max,unit);host.Children.Add(mouse);var exact=new Expander{Header="정밀 입력",FontSize=10,Foreground=new SolidColorBrush(Color.FromRgb(153,177,195)),Content=input};host.Children.Add(exact);
        string Read()=>input is TextBox text?text.Text:((ComboBox)input).Text;
        void Sync(){if(double.TryParse(Read(),NumberStyles.Float,CultureInfo.InvariantCulture,out var n)&&double.IsFinite(n))mouse.SetSilently(n);}
        if(input is TextBox box)box.TextChanged+=(_,_)=>Sync();else if(input is ComboBox combo){combo.AddHandler(TextBox.TextChangedEvent,new TextChangedEventHandler((_,_)=>Sync()));combo.SelectionChanged+=(_,_)=>Sync();}
        mouse.ValueEdited+=changed;if(begin is not null)mouse.GestureStarted+=begin;if(end is not null)mouse.GestureEnded+=end;Sync();return mouse;
    }
}
