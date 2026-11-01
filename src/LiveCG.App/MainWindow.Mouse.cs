using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using LiveCG.Core;

namespace LiveCG.App;
public partial class MainWindow
{
    public Dictionary<string,MouseValueControl> MouseProperties { get; }=[];
    private void InitializeMouseProperties()
    {
        void Add(string name,double min,double max,string unit,Action apply)
        {
            var input=(Control)FindName(name);EditorState? before=null;
            var mouse=MouseValueControl.Attach(input,min,max,unit,value=>
            {
                if(_loading||_busy)return;var text=value.ToString("0.###",CultureInfo.InvariantCulture);if(input is TextBox box)box.Text=text;else ((ComboBox)input).Text=text;apply();
            },()=>{before=CaptureState();BeginGesture();},commit=>{EndGesture(commit);if(!commit&&before is not null)RestoreEdit(before);before=null;});
            MouseProperties[name]=mouse;
        }
        void Clip(){if(CommitClip(true)){MarkEdited();RefreshTimeline();}}
        void Mask(){MaskPropertiesChanged(this,new RoutedEventArgs());}
        Add("ClipStartBox",0,60,"초",Clip);Add("ClipDurationBox",1d/60,30,"초",Clip);
        Add("EffectSpeedBox",.25,4,"배",Clip);Add("LoopPeriodBox",1d/60,30,"초",Clip);
        Add("MaskStartBox",0,60,"초",Mask);Add("MaskDurationBox",1d/60,60,"초",Mask);Add("MaskOpacityBox",0,100,"%",Mask);Add("MaskFeatherBox",0,50,"px",Mask);Add("MaskExpandBox",-32,32,"px",Mask);
        Add("EndHoldBox",0,30,"초",()=>VideoOptionsChanged(this,new RoutedEventArgs()));
    }
}
