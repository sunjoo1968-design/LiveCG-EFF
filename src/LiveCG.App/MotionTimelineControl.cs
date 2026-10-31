using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using LiveCG.Core;

namespace LiveCG.App;

public sealed class MotionTimelineControl:FrameworkElement
{
    public const double LabelWidth=110,RulerHeight=28,RowHeight=36;
    public MotionDefinition Motion { get; set; }=new();
    public int CursorFrame { get; set; }
    public MotionProperty SelectedProperty { get; set; }
    public HashSet<Guid> SelectedKeys { get; }=[];
    public double PixelsPerFrame { get; private set; }=9;
    private double _startX;private Dictionary<Guid,int>? _before;private bool _scrub;
    public event Action<int>? Seeked;public event Action? SelectionChanged;public event Action? Edited;public event Action? GestureStarted;public event Action<bool>? GestureEnded;
    public event Action<MotionProperty,int>? AddRequested;public event Action? DeleteRequested;
    public MotionTimelineControl()
    {
        Focusable=true;ClipToBounds=true;
        MouseLeftButtonDown+=(_,e)=>{Focus();if(e.ClickCount==2){var p=e.GetPosition(this);var row=(int)((p.Y-RulerHeight)/RowHeight);if(p.Y>=RulerHeight&&row>=0&&row<Enum.GetValues<MotionProperty>().Length)AddRequested?.Invoke((MotionProperty)row,FrameAt(p.X));}else{BeginPointer(e.GetPosition(this),Keyboard.Modifiers.HasFlag(ModifierKeys.Control));CaptureMouse();}e.Handled=true;};
        MouseMove+=(_,e)=>MovePointer(e.GetPosition(this));MouseLeftButtonUp+=(_,_)=>{EndPointer();ReleaseMouseCapture();};LostMouseCapture+=(_,_)=>{if(_before is not null)CancelGesture();_scrub=false;};
        MouseWheel+=(_,e)=>{if(Keyboard.Modifiers.HasFlag(ModifierKeys.Control)){Zoom(e.Delta>0?1.25:.8);e.Handled=true;}};
        KeyDown+=(_,e)=>{if(e.Key==Key.Escape){CancelGesture();ReleaseMouseCapture();e.Handled=true;}if(e.Key==Key.Delete){DeleteRequested?.Invoke();e.Handled=true;}};
    }
    public void Refresh(){Width=Math.Max(600,LabelWidth+(Motion.LengthFrames+3)*PixelsPerFrame);Height=RulerHeight+Enum.GetValues<MotionProperty>().Length*RowHeight+6;InvalidateVisual();}
    public void Zoom(double factor){PixelsPerFrame=Math.Clamp(PixelsPerFrame*factor,2,35);Refresh();}
    private int FrameAt(double x)=>Math.Clamp((int)Math.Round((x-LabelWidth)/PixelsPerFrame),0,Motion.LengthFrames);
    public Point KeyPoint(MotionProperty property,MotionKeyframe key)=>new(LabelWidth+key.Frame*PixelsPerFrame,RulerHeight+(int)property*RowHeight+RowHeight/2);
    private static Brush B(string hex)=>new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
    private void Text(DrawingContext dc,string text,double x,double y,Brush brush,double size=11)=>dc.DrawText(new FormattedText(text,CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,new Typeface("Malgun Gothic"),size,brush,VisualTreeHelper.GetDpi(this).PixelsPerDip),new(x,y));
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);dc.DrawRectangle(B("#101B27"),null,new Rect(0,0,Width,Height));var grid=new Pen(B("#2D4052"),1);
        var step=Math.Max(1,Math.Max(Motion.Fps/4,Motion.LengthFrames/1000));
        for(var f=0;f<=Motion.LengthFrames;f+=step){var x=LabelWidth+f*PixelsPerFrame;dc.DrawLine(grid,new(x,20),new(x,Height));Text(dc,f+"f",x+2,4,B("#91ADBE"),10);}
        foreach(var property in Enum.GetValues<MotionProperty>())
        {
            var y=RulerHeight+(int)property*RowHeight;if(property==SelectedProperty)dc.DrawRectangle(B("#1A3C35"),null,new Rect(0,y,Width,RowHeight));
            dc.DrawLine(grid,new(0,y+RowHeight),new(Width,y+RowHeight));dc.DrawRectangle(B("#192B3B"),null,new Rect(0,y,LabelWidth,RowHeight));Text(dc,MotionDefinition.Name(property),8,y+10,B("#D6E7F0"));
            var keys=Motion.Tracks.FirstOrDefault(t=>t.Property==property)?.Keys.OrderBy(k=>k.Frame).ToArray()??[];
            if(keys.Length>1)dc.DrawLine(new Pen(B("#438F7E"),2),KeyPoint(property,keys[0]),KeyPoint(property,keys[^1]));
            foreach(var key in keys)
            {
                var point=KeyPoint(property,key);var diamond=new StreamGeometry();using(var c=diamond.Open()){c.BeginFigure(new(point.X,point.Y-6),true,true);c.LineTo(new(point.X+6,point.Y),true,false);c.LineTo(new(point.X,point.Y+6),true,false);c.LineTo(new(point.X-6,point.Y),true,false);}
                dc.DrawGeometry(SelectedKeys.Contains(key.Id)?Brushes.White:B("#5DE2B1"),new Pen(B("#17212D"),1),diamond);
            }
        }
        var cursor=LabelWidth+CursorFrame*PixelsPerFrame;dc.DrawLine(new Pen(B("#FF7E97"),2),new(cursor,0),new(cursor,Height));
    }
    public void BeginPointer(Point point,bool toggle=false)
    {
        _startX=point.X;var row=(int)((point.Y-RulerHeight)/RowHeight);
        if(point.Y>=RulerHeight&&row>=0&&row<Enum.GetValues<MotionProperty>().Length)
        {
            SelectedProperty=(MotionProperty)row;var key=Motion.Tracks.FirstOrDefault(t=>t.Property==SelectedProperty)?.Keys.FirstOrDefault(k=>Math.Abs(KeyPoint(SelectedProperty,k).X-point.X)<9);
            if(key is not null&&point.X>=LabelWidth)
            {
                if(!toggle&&!SelectedKeys.Contains(key.Id))SelectedKeys.Clear();if(toggle&&!SelectedKeys.Add(key.Id))SelectedKeys.Remove(key.Id);else SelectedKeys.Add(key.Id);
                CursorFrame=key.Frame;SelectionChanged?.Invoke();Seeked?.Invoke(CursorFrame);GestureStarted?.Invoke();_before=Motion.Tracks.SelectMany(t=>t.Keys).Where(k=>SelectedKeys.Contains(k.Id)).ToDictionary(k=>k.Id,k=>k.Frame);InvalidateVisual();return;
            }
            if(!toggle)SelectedKeys.Clear();SelectionChanged?.Invoke();
        }
        if(point.X>=LabelWidth){_scrub=true;CursorFrame=FrameAt(point.X);Seeked?.Invoke(CursorFrame);}InvalidateVisual();
    }
    public void MovePointer(Point point)
    {
        if(_scrub){CursorFrame=FrameAt(point.X);Seeked?.Invoke(CursorFrame);InvalidateVisual();return;}if(_before is null||_before.Count==0)return;
        var delta=(int)Math.Round((point.X-_startX)/PixelsPerFrame);delta=Math.Clamp(delta,-_before.Values.Min(),Motion.LengthFrames-_before.Values.Max());
        foreach(var track in Motion.Tracks)
        {var frames=track.Keys.Select(k=>_before.TryGetValue(k.Id,out var frame)?frame+delta:k.Frame).ToArray();if(frames.Distinct().Count()!=frames.Length)return;}
        foreach(var key in Motion.Tracks.SelectMany(t=>t.Keys))if(_before.TryGetValue(key.Id,out var frame))key.Frame=frame+delta;
        Edited?.Invoke();InvalidateVisual();
    }
    public void EndPointer(){var changed=_before is not null;_before=null;_scrub=false;if(changed)GestureEnded?.Invoke(true);Refresh();}
    public void CancelGesture(){if(_before is null)return;foreach(var key in Motion.Tracks.SelectMany(t=>t.Keys))if(_before.TryGetValue(key.Id,out var frame))key.Frame=frame;_before=null;_scrub=false;Edited?.Invoke();GestureEnded?.Invoke(false);Refresh();}
}
