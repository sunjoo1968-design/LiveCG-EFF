using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LiveCG.Core;
using SkiaSharp;
using SkiaSharp.Views.WPF;
namespace LiveCG.App;
public sealed class RouteCanvas:SKElement,IDisposable
{
    public PreviewViewport Viewport { get; }=new();public int Selected { get; set; }=-1;public double Time { get; set; }public bool AddMode { get; set; }
    public bool ShowGuides { get; set; }=true;
    public int SourceWidth { get; }public int SourceHeight { get; }
    public event Action? Changed,GestureStarted,GestureEnded,SelectionChanged;
    public event Action<string>? Message;
    private readonly SKImage _map;private readonly Func<TravelRoute> _route;
    private int _drag=-1,_handle,_curveLeg=-1,_curvePoint=-1;private bool _pan;private SKPoint _at,_oldPan;private TravelRoute? _before;
    private readonly record struct Hit(int Leg,int Piece,double T,double Distance,RouteVector At);
    public RouteCanvas(SKBitmap map,Func<TravelRoute> route)
    {
        _map=SKImage.FromBitmap(map);SourceWidth=map.Width;SourceHeight=map.Height;_route=route;Focusable=true;
        SizeChanged+=(_,_)=>{Viewport.Update(ActualWidth,ActualHeight,SourceWidth,SourceHeight,SKRect.Create(SourceWidth,SourceHeight));InvalidateVisual();};PaintSurface+=Paint;
        MouseDown+=(_,e)=>{Focus();var p=e.GetPosition(this);var at=new SKPoint((float)p.X,(float)p.Y);if(e.ChangedButton==MouseButton.Right){var menu=RightClick(at);if(menu is not null){menu.PlacementTarget=this;menu.IsOpen=true;}e.Handled=true;}else if(e.ChangedButton is MouseButton.Left or MouseButton.Middle){Begin(at,e.ChangedButton==MouseButton.Middle||Keyboard.IsKeyDown(Key.Space));e.Handled=true;}};
        MouseMove+=(_,e)=>{var p=e.GetPosition(this);Move(new((float)p.X,(float)p.Y));};MouseUp+=(_,e)=>{if(e.ChangedButton!=MouseButton.Right)End();};LostMouseCapture+=(_,_)=>{if(_drag>=0||_curveLeg>=0||_pan)CancelGesture();};MouseWheel+=(_,e)=>{var p=e.GetPosition(this);Viewport.ZoomAt(e.Delta>0?1.25:.8,new((float)p.X,(float)p.Y));InvalidateVisual();e.Handled=true;};
    }
    private SKPoint Pixel(double x,double y)=>new((float)(x*SourceWidth),(float)(y*SourceHeight));
    private RouteVector Normal(SKPoint screen){var p=Viewport.ToImage(screen);return new(p.X/SourceWidth,p.Y/SourceHeight);}
    private int NearestPoint(SKPoint screen,double radius=12)=>_route().Points.Select((p,i)=>(i,d:SKPoint.Distance(screen,Viewport.ToScreen(Pixel(p.X,p.Y))))).Where(p=>p.d<=radius).OrderBy(p=>p.d).Select(p=>p.i).DefaultIfEmpty(-1).First();
    private Hit? PathHit(SKPoint screen)
    {
        var route=_route();if(route.Points.Count<2)return null;var geometry=RouteRenderer.Geometry(route,SourceWidth,SourceHeight);Hit? best=null;
        for(var leg=0;leg<geometry.Segments.Count;leg++){var segment=geometry.Segments[leg];for(var j=0;j<segment.X.Length-1;j++){var a=Viewport.ToScreen(Pixel(segment.X[j],segment.Y[j]));var b=Viewport.ToScreen(Pixel(segment.X[j+1],segment.Y[j+1]));var dx=b.X-a.X;var dy=b.Y-a.Y;var f=Math.Clamp(((screen.X-a.X)*dx+(screen.Y-a.Y)*dy)/Math.Max(1e-9,dx*dx+dy*dy),0,1);var near=new SKPoint((float)(a.X+dx*f),(float)(a.Y+dy*f));var distance=SKPoint.Distance(screen,near);if(best is null||distance<best.Value.Distance){var pieces=(segment.X.Length-1)/64;var raw=(j+f)/64;var piece=Math.Min(pieces-1,(int)raw);best=new(leg,piece,Math.Clamp(raw-piece,.001,.999),distance,new(segment.X[j]+(segment.X[j+1]-segment.X[j])*f,segment.Y[j]+(segment.Y[j+1]-segment.Y[j])*f));}}}return best;
    }
    private void Change(Action<TravelRoute> action)
    {
        GestureStarted?.Invoke();try{action(_route());Changed?.Invoke();SelectionChanged?.Invoke();InvalidateVisual();}catch(Exception ex){Message?.Invoke(ex.Message);}
    }
    public void AddPoint(SKPoint screen,int? index=null)
    {
        var p=Normal(screen);if(p.X<0||p.X>1||p.Y<0||p.Y>1)return;
        Change(route=>{if(route.Points.Count>=200)throw new InvalidDataException("경유점은 최대200개입니다.");var at=Math.Clamp(index??route.Points.Count,0,route.Points.Count);if(at>0)route.Points[at-1].OutX=route.Points[at-1].OutY=0;if(at<route.Points.Count){route.Points[at].CurvePoints.Clear();route.Points[at].InX=route.Points[at].InY=0;}route.Points.Insert(at,new(){X=p.X,Y=p.Y,Name=""});Selected=at;});
    }
    public void DeleteSelected()=>Change(route=>{RouteEditing.DeleteWaypoint(route,Selected);Selected=Math.Min(Selected,route.Points.Count-1);});
    public void SetEndpoint(bool departure)=>Change(route=>{if(Selected<0||Selected>=route.Points.Count)return;var index=departure?0:route.Points.Count-1;RouteEditing.MoveWaypoint(route,Selected,index);Selected=index;});
    public ContextMenu? RightClick(SKPoint screen)
    {
        if(!ShowGuides)return null;CancelGesture();var point=NearestPoint(screen);var hit=PathHit(screen);ContextMenu? menu=null;
        void Item(string label,Action action){menu??=new ContextMenu();var item=new MenuItem{Header=label};item.Click+=(_,_)=>action();menu.Items.Add(item);}
        if(point>=0){Selected=point;SelectionChanged?.Invoke();Item("이 점 삭제",DeleteSelected);Item("출발점으로 지정",()=>SetEndpoint(true));Item("도착점으로 지정",()=>SetEndpoint(false));Item("이 점 앞에 추가",()=>AddPoint(screen+new SKPoint(-18,-12),point));Item("이 점 뒤에 추가",()=>AddPoint(screen+new SKPoint(18,12),point+1));Item("점 위치 · 정차 수정",()=>{SelectionChanged?.Invoke();InvalidateVisual();});}
        else
        {
            var knot=NearestCurve(screen);if(knot is { } curve){Item("곡선점 삭제",()=>Change(route=>route.Points[curve.Leg+1].CurvePoints.RemoveAt(curve.Index)));Item("이 구간 직선으로",()=>Straighten(curve.Leg));}
            else if(hit is {Distance:<=10} edge){Item("여기에 경유점 삽입",()=>Change(route=>Selected=RouteEditing.InsertWaypoint(route,edge.Leg,edge.Piece,edge.T)));Item("이 구간 직선으로",()=>Straighten(edge.Leg));Item("경로 끝에 추가",()=>AddPoint(screen));}
            else AddPoint(screen);
        }
        InvalidateVisual();return menu;
    }
    private void Straighten(int leg)=>Change(route=>{route.Points[leg].OutX=route.Points[leg].OutY=0;var end=route.Points[leg+1];end.InX=end.InY=0;end.CurvePoints.Clear();});
    private (int Leg,int Index)? NearestCurve(SKPoint screen)
    {
        if(!_route().Curved)return null;for(var i=1;i<_route().Points.Count;i++)for(var j=0;j<_route().Points[i].CurvePoints.Count;j++){var p=_route().Points[i].CurvePoints[j];if(SKPoint.Distance(screen,Viewport.ToScreen(Pixel(p.X,p.Y)))<=8)return(i-1,j);}return null;
    }
    public void Begin(SKPoint screen,bool pan=false,bool insert=false)
    {
        _at=screen;if(!ShowGuides&&!pan)return;if(pan){_pan=true;_oldPan=Viewport.Pan;CaptureMouse();return;}
        var route=_route();var nearest=NearestPoint(screen);
        if(nearest>=0){Selected=nearest;SelectionChanged?.Invoke();_before=route.Copy();GestureStarted?.Invoke();_drag=nearest;_handle=0;CaptureMouse();return;}
        if(Selected>=0&&Selected<route.Points.Count&&route.Curved)
        {
            var selected=route.Points[Selected];var handles=new[]{Pixel(selected.X+selected.InX,selected.Y+selected.InY),Pixel(selected.X+selected.OutX,selected.Y+selected.OutY)};
            for(var i=0;i<2;i++)if(SKPoint.Distance(handles[i],Pixel(selected.X,selected.Y))>.01&&SKPoint.Distance(screen,Viewport.ToScreen(handles[i]))<8){_before=route.Copy();GestureStarted?.Invoke();_drag=Selected;_handle=i+1;CaptureMouse();return;}
        }
        if(!route.Curved)return;var curve=NearestCurve(screen);var hit=PathHit(screen);if(curve is null&&hit is not {Distance:<=10})return;
        _before=route.Copy();GestureStarted?.Invoke();
        try{if(curve is { } found){_curveLeg=found.Leg;_curvePoint=found.Index;}else{var edge=hit!.Value;_curveLeg=edge.Leg;_curvePoint=RouteEditing.InsertCurvePoint(route,edge.Leg,edge.Piece,edge.T);}CaptureMouse();InvalidateVisual();}catch(Exception ex){_before=null;_curveLeg=_curvePoint=-1;Message?.Invoke(ex.Message);}
    }
    public void Move(SKPoint screen)
    {
        if(_pan){Viewport.Pan=_oldPan+screen-_at;InvalidateVisual();return;}var p=Normal(screen);
        if(_curveLeg>=0){var knot=_route().Points[_curveLeg+1].CurvePoints[_curvePoint];knot.X=Math.Clamp(p.X,0,1);knot.Y=Math.Clamp(p.Y,0,1);InvalidateVisual();return;}
        if(_drag<0)return;var point=_route().Points[_drag];if(_handle==1){point.InX=Math.Clamp(p.X-point.X,-1,1);point.InY=Math.Clamp(p.Y-point.Y,-1,1);}else if(_handle==2){point.OutX=Math.Clamp(p.X-point.X,-1,1);point.OutY=Math.Clamp(p.Y-point.Y,-1,1);}else{point.X=Math.Clamp(p.X,0,1);point.Y=Math.Clamp(p.Y,0,1);}InvalidateVisual();
    }
    public void End(){var edited=_drag>=0||_curveLeg>=0;_drag=_curveLeg=_curvePoint=-1;_handle=0;_pan=false;_before=null;ReleaseMouseCapture();if(edited)GestureEnded?.Invoke();InvalidateVisual();}
    public void CancelGesture(){if(_before is { } before){_route().Points=before.Points;_route().Curved=before.Curved;_route().DurationBudget=before.DurationBudget;}_before=null;_drag=_curveLeg=_curvePoint=-1;_pan=false;ReleaseMouseCapture();InvalidateVisual();}
    private void Paint(object? sender,SkiaSharp.Views.Desktop.SKPaintSurfaceEventArgs e)
    {
        var c=e.Surface.Canvas;c.Clear(new(13,23,34));c.Scale(e.Info.Width/(float)Math.Max(1,ActualWidth),e.Info.Height/(float)Math.Max(1,ActualHeight));var r=Viewport.ImageRect;c.Translate(r.Left,r.Top);c.Scale((float)Viewport.Scale);c.DrawImage(_map,0,0);var route=_route();RouteRenderer.Draw(c,route,SourceWidth,SourceHeight,Time/Math.Max(.1,route.Duration));if(!ShowGuides)return;var scale=(float)Viewport.Scale;
        using var paint=new SKPaint{IsAntialias=true,StrokeWidth=1.5f/scale};using var font=new SKFont(SKTypeface.FromFamilyName("Malgun Gothic",SKFontStyle.Bold),12/scale);
        // The complete path and point labels are editing guides only, independent of future-path output.
        paint.Style=SKPaintStyle.Stroke;paint.Color=new(116,185,220,150);var geometry=RouteRenderer.Geometry(route,SourceWidth,SourceHeight);foreach(var s in geometry.Segments){using var path=new SKPath();path.MoveTo(Pixel(s.X[0],s.Y[0]));for(var j=1;j<s.X.Length;j++)path.LineTo(Pixel(s.X[j],s.Y[j]));c.DrawPath(path,paint);}
        for(var i=0;i<route.Points.Count;i++)
        {
            var p=route.Points[i];var at=Pixel(p.X,p.Y);paint.Style=SKPaintStyle.Fill;paint.Color=i==0?new(80,234,142):i==route.Points.Count-1?new(255,139,87):new(83,201,255);
            if(i==route.Points.Count-1&&i>0)c.DrawRect(SKRect.Create(at.X-5/scale,at.Y-5/scale,10/scale,10/scale),paint);else c.DrawCircle(at,5/scale,paint);
            paint.Style=SKPaintStyle.Stroke;paint.Color=i==Selected?SKColors.Yellow:SKColors.Black;c.DrawCircle(at,8/scale,paint);paint.Style=SKPaintStyle.Fill;paint.Color=SKColors.White;c.DrawText(RouteEditing.Role(route,i),at.X+10/scale,at.Y-8/scale,font,paint);
            if(i==Selected&&route.Curved)foreach(var delta in new[]{Pixel(p.InX,p.InY),Pixel(p.OutX,p.OutY)}){if(delta.X==0&&delta.Y==0)continue;paint.Color=SKColors.Orange;paint.Style=SKPaintStyle.Stroke;c.DrawLine(at,at+delta,paint);c.DrawCircle(at+delta,3/scale,paint);}
            if(route.Curved)foreach(var knot in p.CurvePoints){paint.Style=SKPaintStyle.Stroke;paint.Color=SKColors.Orange;c.DrawCircle(Pixel(knot.X,knot.Y),4/scale,paint);}
        }
    }
    public void Fit(){Viewport.Fit(false);InvalidateVisual();}public void Zoom(double amount){Viewport.ZoomAt(amount,new((float)ActualWidth/2,(float)ActualHeight/2));InvalidateVisual();}public void Dispose()=>_map.Dispose();
}
