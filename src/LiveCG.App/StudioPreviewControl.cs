using System.Windows;
using System.Windows.Input;
using LiveCG.Core;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace LiveCG.App;

public sealed class StudioPreviewControl:SKElement,IDisposable
{
    private readonly SKBitmap _stage;private readonly EffectScene _scene;private readonly EffectRegion _region;
    public EffectAsset Asset { get; set; }
    public MotionDefinition? Motion { get; set; }
    public int Frame { get; set; }
    public double Time { get; set; }
    public bool ShowPath { get; set; }=true;
    public bool EditEnabled { get; set; }=true;
    public PreviewViewport Viewport { get; }=new();
    private int _drag=-1;private SKPoint _start,_oldPan,_anchor;private double _x,_y,_sx,_sy,_angle,_ax,_ay;private bool _pan;
    public event Action? GestureStarted;public event Action<bool>? GestureEnded;public event Action<Dictionary<MotionProperty,double>>? ValuesEdited;
    public StudioPreviewControl(EffectAsset asset,SKBitmap? sample)
    {
        Asset=asset;var stage=EffectSample.Stage(sample);_stage=stage.Bitmap;_region=new(){Bounds=stage.Bounds,Background=BackgroundMode.Transparent};_scene=new(_stage);Focusable=true;
        SizeChanged+=(_,_)=>{Viewport.Update(ActualWidth,ActualHeight,_stage.Width,_stage.Height,SKRect.Create(_stage.Width,_stage.Height));InvalidateVisual();};PaintSurface+=Paint;
        MouseDown+=(_,e)=>
        {
            Focus();var p=e.GetPosition(this);var point=new SKPoint((float)p.X,(float)p.Y);
            if(e.ChangedButton==MouseButton.Middle||Keyboard.IsKeyDown(Key.Space)){_pan=true;_start=point;_oldPan=Viewport.Pan;CaptureMouse();return;}
            if(e.ChangedButton!=MouseButton.Left||!EditEnabled||Motion is null)return;
            var corners=ScreenCorners();using var outline=Outline(corners);var rotate=RotationHandle(corners);
            var mode=SKPoint.Distance(point,rotate)<12?2:SKPoint.Distance(point,corners[2])<12?1:outline.Contains(point.X,point.Y)?0:-1;
            if(mode>=0&&BeginTransform(mode,point))CaptureMouse();
        };
        MouseMove+=(_,e)=>
        {
            var p=e.GetPosition(this);var point=new SKPoint((float)p.X,(float)p.Y);
            if(_pan){Viewport.Pan=new(_oldPan.X+point.X-_start.X,_oldPan.Y+point.Y-_start.Y);InvalidateVisual();return;}
            MoveTransform(point,Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
        };
        MouseUp+=(_,_)=>{EndTransform(true);_pan=false;ReleaseMouseCapture();};
        LostMouseCapture+=(_,_)=>{EndTransform(false);_pan=false;};
        KeyDown+=(_,e)=>{if(e.Key==Key.Escape&&_drag>=0){EndTransform(false);ReleaseMouseCapture();e.Handled=true;}};
        MouseWheel+=(_,e)=>{var p=e.GetPosition(this);Viewport.ZoomAt(e.Delta>0?1.25:.8,new((float)p.X,(float)p.Y));InvalidateVisual();e.Handled=true;};
    }
    private SKRect ObjectRect=>_region.Bounds.Pixels(_stage.Width,_stage.Height);
    private double Value(MotionProperty property)=>Motion?.Value(property,Frame)??MotionDefinition.Default(property);
    public SKPoint ScreenAnchor()
    {var r=ObjectRect;return Viewport.ToScreen(new SKPoint(r.Left+r.Width*(float)(Value(MotionProperty.AnchorX)+Value(MotionProperty.X)),r.Top+r.Height*(float)(Value(MotionProperty.AnchorY)+Value(MotionProperty.Y))));}
    public SKPoint[] ScreenCorners()
    {
        var r=ObjectRect;var anchor=Viewport.ToImage(ScreenAnchor());var ax=Value(MotionProperty.AnchorX);var ay=Value(MotionProperty.AnchorY);var sx=Value(MotionProperty.ScaleX);var sy=Value(MotionProperty.ScaleY);var angle=Value(MotionProperty.Rotation)*Math.PI/180;var cos=Math.Cos(angle);var sin=Math.Sin(angle);
        return new[]{new SKPoint(0,0),new SKPoint(r.Width,0),new SKPoint(r.Width,r.Height),new SKPoint(0,r.Height)}.Select(p=>
        {var x=(p.X-r.Width*ax)*sx;var y=(p.Y-r.Height*ay)*sy;return Viewport.ToScreen(new SKPoint(anchor.X+(float)(x*cos-y*sin),anchor.Y+(float)(x*sin+y*cos)));}).ToArray();
    }
    private static SKPath Outline(SKPoint[] points){var path=new SKPath();path.MoveTo(points[0]);foreach(var point in points.Skip(1))path.LineTo(point);path.Close();return path;}
    private static SKPoint RotationHandle(SKPoint[] corners)
    {var middle=new SKPoint((corners[0].X+corners[1].X)/2,(corners[0].Y+corners[1].Y)/2);var dx=corners[1].X-corners[0].X;var dy=corners[1].Y-corners[0].Y;var length=Math.Max(.001f,MathF.Sqrt(dx*dx+dy*dy));return new(middle.X+dy/length*22,middle.Y-dx/length*22);}
    public bool BeginTransform(int mode,SKPoint screen)
    {
        if(Motion is null||!EditEnabled||mode is <0 or >2)return false;
        _drag=mode;_start=Viewport.ToImage(screen);_anchor=Viewport.ToImage(ScreenAnchor());_x=Value(MotionProperty.X);_y=Value(MotionProperty.Y);_sx=Value(MotionProperty.ScaleX);_sy=Value(MotionProperty.ScaleY);_angle=Value(MotionProperty.Rotation);_ax=Value(MotionProperty.AnchorX);_ay=Value(MotionProperty.AnchorY);GestureStarted?.Invoke();return true;
    }
    public void MoveTransform(SKPoint screen,bool keepAspect=false)
    {
        if(_drag<0||Motion is null)return;var at=Viewport.ToImage(screen);var rect=ObjectRect;var dx=at.X-_start.X;var dy=at.Y-_start.Y;
        if(_drag==0)ValuesEdited?.Invoke(new(){[MotionProperty.X]=Math.Clamp(_x+dx/rect.Width,-20,20),[MotionProperty.Y]=Math.Clamp(_y+dy/rect.Height,-20,20)});
        else if(_drag==1)
        {
            var angle=-_angle*Math.PI/180;var localX=dx*Math.Cos(angle)-dy*Math.Sin(angle);var localY=dx*Math.Sin(angle)+dy*Math.Cos(angle);
            var sx=Math.Clamp(_sx+localX/(rect.Width*Math.Max(.05,1-_ax)),.01,20);var sy=Math.Clamp(_sy+localY/(rect.Height*Math.Max(.05,1-_ay)),.01,20);
            if(keepAspect)sy=Math.Clamp(sx*_sy/Math.Max(.01,_sx),.01,20);ValuesEdited?.Invoke(new(){[MotionProperty.ScaleX]=sx,[MotionProperty.ScaleY]=sy});
        }
        else
        {var delta=(Math.Atan2(at.Y-_anchor.Y,at.X-_anchor.X)-Math.Atan2(_start.Y-_anchor.Y,_start.X-_anchor.X))*180/Math.PI;ValuesEdited?.Invoke(new(){[MotionProperty.Rotation]=Math.Clamp(_angle+delta,-36000,36000)});}
        InvalidateVisual();
    }
    public void EndTransform(bool commit){if(_drag<0)return;_drag=-1;GestureEnded?.Invoke(commit);}
    public void Fit(){Viewport.Fit(false);InvalidateVisual();}
    private void Paint(object? sender,SKPaintSurfaceEventArgs e)
    {
        var c=e.Surface.Canvas;c.Clear(new SKColor(12,21,32));c.Scale(e.Info.Width/(float)Math.Max(1,ActualWidth),e.Info.Height/(float)Math.Max(1,ActualHeight));
        using var p=new SKPaint{IsAntialias=true};var imageRect=Viewport.ImageRect;c.Save();c.ClipRect(imageRect);
        for(var y=0;y<ActualHeight;y+=16)for(var x=0;x<ActualWidth;x+=16){p.Color=(x/16+y/16)%2==0?new(32,43,55):new(45,57,69);c.DrawRect((float)x,(float)y,16,16,p);}
        c.Translate(imageRect.Left,imageRect.Top);c.Scale((float)Viewport.Scale);_region.Clips=Asset.Clips;_scene.Draw(c,[_region],Time);c.Restore();
        if(ShowPath&&Motion is not null)
        {
            var r=ObjectRect;using var path=new SKPath();
            for(var f=0;f<=Motion.LengthFrames;f+=Math.Max(1,Motion.LengthFrames/150))
            {var at=Viewport.ToScreen(new SKPoint(r.Left+r.Width*(float)(Motion.Value(MotionProperty.AnchorX,f)+Motion.Value(MotionProperty.X,f)),r.Top+r.Height*(float)(Motion.Value(MotionProperty.AnchorY,f)+Motion.Value(MotionProperty.Y,f))));if(f==0)path.MoveTo(at);else path.LineTo(at);}
            p.Color=new(93,226,177,180);p.Style=SKPaintStyle.Stroke;p.StrokeWidth=1.5f;c.DrawPath(path,p);
        }
        if(EditEnabled&&Motion is not null)
        {
            var corners=ScreenCorners();using var outline=Outline(corners);var rotate=RotationHandle(corners);var middle=new SKPoint((corners[0].X+corners[1].X)/2,(corners[0].Y+corners[1].Y)/2);
            p.Style=SKPaintStyle.Stroke;p.Color=new(93,226,177);p.StrokeWidth=1.5f;c.DrawPath(outline,p);c.DrawLine(middle,rotate,p);c.DrawCircle(rotate,5,p);
            var anchor=ScreenAnchor();c.DrawCircle(anchor,4,p);c.DrawLine(anchor.X-7,anchor.Y,anchor.X+7,anchor.Y,p);c.DrawLine(anchor.X,anchor.Y-7,anchor.X,anchor.Y+7,p);
            p.Style=SKPaintStyle.Fill;p.Color=SKColors.White;c.DrawRect(corners[2].X-4,corners[2].Y-4,8,8,p);
        }
    }
    public void Dispose(){_scene.Dispose();_stage.Dispose();}
}
