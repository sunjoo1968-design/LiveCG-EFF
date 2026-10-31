using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using LiveCG.Core;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace LiveCG.App;
public sealed class MaskMotionPreview:SKElement,IDisposable
{
    private readonly SKBitmap _source;private readonly SKImage _image;private readonly MaskRenderer _renderer=new();
    public MaskTrack Track { get; set; }public double Time { get; set; }public bool MaskOnly { get; set; }public bool AnchorTool { get; set; }
    public PreviewViewport Viewport { get; }=new();
    public event Action? GestureStarted;public event Action<bool>? GestureEnded;public event Action<Dictionary<MotionProperty,double>>? ValuesEdited;public event Action? ViewChanged;
    private int _drag=-1,_handle=4;private bool _pan;private SKPoint _start,_oldPan,_anchor;private double _x,_y,_sx,_sy,_angle,_ax,_ay;
    public MaskMotionPreview(SKBitmap source,MaskTrack track)
    {
        _source=source;_image=SKImage.FromBitmap(source);Track=track;Focusable=true;ToolTip="본체: 이동 · 8개 점: 크기 · 위 원: 회전 · 기준점 도구/Alt+중심: 기준점 · 휠: 확대 · Space/중간 버튼: 화면 이동";PaintSurface+=Paint;SizeChanged+=(_,_)=>{Viewport.Update(ActualWidth,ActualHeight,_source.Width,_source.Height,SKRect.Create(_source.Width,_source.Height));InvalidateVisual();ViewChanged?.Invoke();};
        MouseDown+=(_,e)=>{if(e.ChangedButton is not MouseButton.Left and not MouseButton.Middle)return;Focus();var p=e.GetPosition(this);if(BeginPointer(new((float)p.X,(float)p.Y),e.ChangedButton==MouseButton.Middle||Keyboard.IsKeyDown(Key.Space)))CaptureMouse();e.Handled=true;};
        MouseMove+=(_,e)=>{var p=e.GetPosition(this);MovePointer(new((float)p.X,(float)p.Y),Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));};
        MouseUp+=(_,_)=>{EndPointer(true);ReleaseMouseCapture();};LostMouseCapture+=(_,_)=>EndPointer(false);
        MouseWheel+=(_,e)=>{var p=e.GetPosition(this);ZoomAt(e.Delta>0?1.25:.8,new((float)p.X,(float)p.Y));e.Handled=true;};
        KeyDown+=(_,e)=>{if(e.Key==Key.Escape){EndPointer(false);ReleaseMouseCapture();e.Handled=true;}};
    }
    private SKRect Bounds=>Track.Bounds.Pixels(_source.Width,_source.Height);
    private double Frame=>Math.Clamp((Time-Track.Start)/Track.Duration,0,1)*(Track.Motion?.LengthFrames??1);
    private double Value(MotionProperty p)=>Track.Motion?.Value(p,Frame)??MotionDefinition.Default(p);
    public SKPoint ScreenAnchor(){var r=Bounds;return Viewport.ToScreen(new SKPoint(r.Left+r.Width*(float)(Value(MotionProperty.AnchorX)+Value(MotionProperty.X)),r.Top+r.Height*(float)(Value(MotionProperty.AnchorY)+Value(MotionProperty.Y))));}
    public SKPoint[] ScreenCorners()
    {
        var r=Bounds;var a=Viewport.ToImage(ScreenAnchor());var rad=Value(MotionProperty.Rotation)*Math.PI/180;return new[]{new SKPoint(0,0),new SKPoint(r.Width,0),new SKPoint(r.Width,r.Height),new SKPoint(0,r.Height)}.Select(p=>{var x=(p.X-r.Width*Value(MotionProperty.AnchorX))*Value(MotionProperty.ScaleX);var y=(p.Y-r.Height*Value(MotionProperty.AnchorY))*Value(MotionProperty.ScaleY);return Viewport.ToScreen(new SKPoint(a.X+(float)(x*Math.Cos(rad)-y*Math.Sin(rad)),a.Y+(float)(x*Math.Sin(rad)+y*Math.Cos(rad))));}).ToArray();
    }
    private static SKPoint Mid(SKPoint a,SKPoint b)=>new((a.X+b.X)/2,(a.Y+b.Y)/2);
    public SKPoint[] ScreenHandles(){var c=ScreenCorners();return [c[0],Mid(c[0],c[1]),c[1],Mid(c[1],c[2]),c[2],Mid(c[2],c[3]),c[3],Mid(c[3],c[0])];}
    public SKPoint RotationHandle(){var c=ScreenCorners();var mid=Mid(c[0],c[1]);var dx=c[1].X-c[0].X;var dy=c[1].Y-c[0].Y;var length=Math.Max(.001f,MathF.Sqrt(dx*dx+dy*dy));return new(mid.X+dy/length*27,mid.Y-dx/length*27);}
    public bool BeginPointer(SKPoint screen,bool pan=false)
    {
        if(pan){_pan=true;_start=screen;_oldPan=Viewport.Pan;return true;}
        var handles=ScreenHandles();var h=Array.FindIndex(handles,p=>SKPoint.Distance(p,screen)<10);using var path=new SKPath();path.AddPoly(ScreenCorners(),true);
        var mode=AnchorTool||Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)&&SKPoint.Distance(ScreenAnchor(),screen)<14?3:SKPoint.Distance(RotationHandle(),screen)<12?2:h>=0?1:path.Contains(screen.X,screen.Y)?0:-1;
        return mode>=0&&BeginTransform(mode,screen,h>=0?h:4);
    }
    public bool BeginTransform(int mode,SKPoint screen,int handle=4)
    {
        if(mode is <0 or >3)return false;_drag=mode;_handle=handle;_start=Viewport.ToImage(screen);_anchor=Viewport.ToImage(ScreenAnchor());_x=Value(MotionProperty.X);_y=Value(MotionProperty.Y);_sx=Value(MotionProperty.ScaleX);_sy=Value(MotionProperty.ScaleY);_angle=Value(MotionProperty.Rotation);_ax=Value(MotionProperty.AnchorX);_ay=Value(MotionProperty.AnchorY);GestureStarted?.Invoke();return true;
    }
    public void MovePointer(SKPoint screen,bool keepAspect=false)
    {
        if(_pan){Viewport.Pan=new(_oldPan.X+screen.X-_start.X,_oldPan.Y+screen.Y-_start.Y);InvalidateVisual();ViewChanged?.Invoke();return;}MoveTransform(screen,keepAspect);
    }
    public void MoveTransform(SKPoint screen,bool keepAspect=false)
    {
        if(_drag<0)return;var r=Bounds;var p=Viewport.ToImage(screen);var dx=p.X-_start.X;var dy=p.Y-_start.Y;var rad=-_angle*Math.PI/180;var lx=dx*Math.Cos(rad)-dy*Math.Sin(rad);var ly=dx*Math.Sin(rad)+dy*Math.Cos(rad);
        if(_drag==0)ValuesEdited?.Invoke(new(){[MotionProperty.X]=Math.Clamp(_x+dx/r.Width,-20,20),[MotionProperty.Y]=Math.Clamp(_y+dy/r.Height,-20,20)});
        else if(_drag==1)
        {
            var sx=_sx;var sy=_sy;var left=_handle is 0 or 6 or 7;var right=_handle is 2 or 3 or 4;var top=_handle is 0 or 1 or 2;var bottom=_handle is 4 or 5 or 6;
            if(left||right)sx=Math.Clamp(_sx+lx/(r.Width*(left?-Math.Max(.05,_ax):Math.Max(.05,1-_ax))),.01,20);if(top||bottom)sy=Math.Clamp(_sy+ly/(r.Height*(top?-Math.Max(.05,_ay):Math.Max(.05,1-_ay))),.01,20);
            if(keepAspect){if(left||right)sy=Math.Clamp(sx*_sy/Math.Max(.01,_sx),.01,20);else sx=Math.Clamp(sy*_sx/Math.Max(.01,_sy),.01,20);}ValuesEdited?.Invoke(new(){[MotionProperty.ScaleX]=sx,[MotionProperty.ScaleY]=sy});
        }
        else if(_drag==2){var delta=(Math.Atan2(p.Y-_anchor.Y,p.X-_anchor.X)-Math.Atan2(_start.Y-_anchor.Y,_start.X-_anchor.X))*180/Math.PI;ValuesEdited?.Invoke(new(){[MotionProperty.Rotation]=Math.Clamp(_angle+delta,-36000,36000)});}
        else
        {
            var ax=Math.Clamp(_ax+lx/(r.Width*Math.Max(.01,_sx)),0,1);var ay=Math.Clamp(_ay+ly/(r.Height*Math.Max(.01,_sy)),0,1);var dax=(ax-_ax)*r.Width;var day=(ay-_ay)*r.Height;var cos=Math.Cos(-rad);var sin=Math.Sin(-rad);var worldX=dax*_sx*cos-day*_sy*sin;var worldY=dax*_sx*sin+day*_sy*cos;
            ValuesEdited?.Invoke(new(){[MotionProperty.AnchorX]=ax,[MotionProperty.AnchorY]=ay,[MotionProperty.X]=Math.Clamp(_x+(worldX-dax)/r.Width,-20,20),[MotionProperty.Y]=Math.Clamp(_y+(worldY-day)/r.Height,-20,20)});
        }
        InvalidateVisual();
    }
    public void EndTransform(bool commit){if(_drag<0)return;_drag=-1;GestureEnded?.Invoke(commit);}
    public void EndPointer(bool commit){_pan=false;EndTransform(commit);}
    public void ZoomAt(double multiplier,SKPoint? anchor=null){Viewport.ZoomAt(multiplier,anchor??new((float)ActualWidth/2,(float)ActualHeight/2));InvalidateVisual();ViewChanged?.Invoke();}
    public void Fit(){Viewport.Fit(false);InvalidateVisual();ViewChanged?.Invoke();}
    public void Native(){Viewport.Native(VisualTreeHelper.GetDpi(this).DpiScaleX);InvalidateVisual();ViewChanged?.Invoke();}
    private void Paint(object? sender,SKPaintSurfaceEventArgs e)
    {
        var c=e.Surface.Canvas;c.Clear(new SKColor(13,23,34));c.Scale(e.Info.Width/(float)Math.Max(1,ActualWidth),e.Info.Height/(float)Math.Max(1,ActualHeight));c.Save();var r=Viewport.ImageRect;c.Translate(r.Left,r.Top);c.Scale((float)Viewport.Scale);
        if(!MaskOnly)c.DrawImage(_image,0,0);else{using var p=new SKPaint();for(var y=0;y<_source.Height;y+=20)for(var x=0;x<_source.Width;x+=20){p.Color=((x/20+y/20)%2==0)?new(44,55,66):new(70,81,92);c.DrawRect(x,y,20,20,p);}}
        using(var mask=_renderer.RenderOne(Track,_source.Width,_source.Height,_source.Width,_source.Height,SKRectI.Create(_source.Width,_source.Height),Time))using(var image=SKImage.FromBitmap(mask))using(var paint=new SKPaint()){if(!MaskOnly)paint.ColorFilter=SKColorFilter.CreateBlendMode(new SKColor(161,144,255,120),SKBlendMode.SrcIn);c.DrawImage(image,0,0,paint);paint.ColorFilter?.Dispose();}c.Restore();
        using var pen=new SKPaint{IsAntialias=true,Color=new(169,159,255),Style=SKPaintStyle.Stroke,StrokeWidth=1.5f};using var outline=new SKPath();outline.AddPoly(ScreenCorners(),true);c.DrawPath(outline,pen);var rot=RotationHandle();c.DrawLine(Mid(ScreenCorners()[0],ScreenCorners()[1]),rot,pen);c.DrawCircle(rot,6,pen);var a=ScreenAnchor();c.DrawCircle(a,5,pen);c.DrawLine(a.X-8,a.Y,a.X+8,a.Y,pen);c.DrawLine(a.X,a.Y-8,a.X,a.Y+8,pen);pen.Style=SKPaintStyle.Fill;pen.Color=SKColors.White;foreach(var h in ScreenHandles())c.DrawRect(h.X-4,h.Y-4,8,8,pen);
    }
    public void Dispose(){_image.Dispose();_renderer.Dispose();}
}
