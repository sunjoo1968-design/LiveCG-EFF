using LiveCG.Core;
using SkiaSharp;

namespace LiveCG.App;
public sealed partial class PreviewControl
{
    public bool PointEditMode { get; set; }
    public int SelectedPathPoint { get; private set; }=-1;
    private bool _pointDragging;private EffectRegion? _pointBefore;private List<SKPoint> _pointWorld=[];private EffectRegion? _pointRegion;private MaskTrack? _pointMask;
    private EffectRegion? PathTarget()=>SelectedMask is { } m?new(){Id=m.Id,Bounds=m.Bounds,Shape=m.Shape,Points=m.Points}:SelectedRegion;
    public SKPoint[] PathScreenPoints()
    {var t=PathTarget();if(t is null||_source is null||t.Shape is not (RegionShape.Polygon or RegionShape.Freehand))return [];var r=t.Bounds.Pixels(_source.Width,_source.Height);return t.Points.Select(p=>Viewport.ToScreen(new SKPoint(r.Left+(float)p.X*r.Width,r.Top+(float)p.Y*r.Height))).ToArray();}
    private void DrawPathHandles(SKCanvas c)
    {
        if(!PointEditMode||AddMode)return;using var p=new SKPaint{IsAntialias=true};var points=PathScreenPoints();for(var i=0;i<points.Length;i++){p.Color=i==SelectedPathPoint?new SKColor(255,193,95):SKColors.White;c.DrawRect(points[i].X-4,points[i].Y-4,8,8,p);p.Style=SKPaintStyle.Stroke;p.Color=new(65,157,206);p.StrokeWidth=1.5f;c.DrawRect(points[i].X-4,points[i].Y-4,8,8,p);p.Style=SKPaintStyle.Fill;}
    }
    private void BeginPathTransaction(EffectRegion target)
    {
        _pointRegion=SelectedMask is null?SelectedRegion:null;_pointMask=SelectedMask;_pointBefore=target.Copy(false);var r=target.Bounds.Pixels(_source!.Width,_source.Height);_pointWorld=target.Points.Select(p=>new SKPoint(r.Left+(float)p.X*r.Width,r.Top+(float)p.Y*r.Height)).ToList();GestureStarted?.Invoke();_pointDragging=true;
    }
    private bool TryBeginPathEdit(SKPoint screen)
    {
        var target=PathTarget();if(target is null||target.Locked||_source is null)return false;var points=PathScreenPoints();if(points.Length<3)return false;
        var index=Array.FindIndex(points,p=>SKPoint.Distance(p,screen)<=9);
        if(index>=0){BeginPathTransaction(target);SelectedPathPoint=index;InvalidateVisual();return true;}
        for(var i=0;i<points.Length;i++)
        {
            var a=points[i];var b=points[(i+1)%points.Length];var dx=b.X-a.X;var dy=b.Y-a.Y;var t=Math.Clamp(((screen.X-a.X)*dx+(screen.Y-a.Y)*dy)/Math.Max(.001f,dx*dx+dy*dy),0,1);var at=new SKPoint(a.X+t*dx,a.Y+t*dy);
            if(SKPoint.Distance(at,screen)>7)continue;BeginPathTransaction(target);SelectedPathPoint=i+1;_pointWorld.Insert(i+1,Viewport.ToImage(at));ApplyPathWorld();return true;
        }
        return false;
    }
    public void MovePathPoint(SKPoint screen)
    {if(!_pointDragging||_source is null||SelectedPathPoint<0||SelectedPathPoint>=_pointWorld.Count)return;var at=Viewport.ToImage(screen);_pointWorld[SelectedPathPoint]=new(Math.Clamp(at.X,0,_source.Width),Math.Clamp(at.Y,0,_source.Height));ApplyPathWorld();}
    private void ApplyPathWorld()
    {
        if(_source is null||_pointWorld.Count<3)return;var l=_pointWorld.Min(p=>p.X);var t=_pointWorld.Min(p=>p.Y);var w=Math.Max(2,_pointWorld.Max(p=>p.X)-l);var h=Math.Max(2,_pointWorld.Max(p=>p.Y)-t);
        l=Math.Min(l,_source.Width-w);t=Math.Min(t,_source.Height-h);var bounds=NormalizedRect.FromPixels(SKRect.Create(l,t,w,h),_source.Width,_source.Height);var points=_pointWorld.Select(p=>new RegionPoint(Math.Clamp((p.X-l)/w,0,1),Math.Clamp((p.Y-t)/h,0,1))).ToList();
        if(_pointMask is { } m){m.Bounds=bounds;m.Points=points;MaskEdited?.Invoke();}else if(_pointRegion is { } r){if(r.Bounds!=bounds)r.Cutout=null;r.Bounds=bounds;r.Points=points;RegionEdited?.Invoke();}InvalidateVisual();
    }
    private void EndPathEdit(bool commit)
    {
        if(!_pointDragging)return;_pointDragging=false;
        if(!commit&&_pointBefore is { } old){if(_pointMask is { } m){m.Bounds=old.Bounds;m.Points=old.Points;MaskEdited?.Invoke();}else if(_pointRegion is { } r){r.Bounds=old.Bounds;r.Points=old.Points;r.Cutout=old.Cutout;RegionEdited?.Invoke();}}
        _pointBefore=null;_pointRegion=null;_pointMask=null;_pointWorld.Clear();GestureEnded?.Invoke(commit);InvalidateVisual();
    }
    public bool DeleteSelectedPathPoint()
    {
        var t=PathTarget();if(t is null||t.Locked||t.Points.Count<=3||SelectedPathPoint<0||SelectedPathPoint>=t.Points.Count)return false;
        BeginPathTransaction(t);_pointWorld.RemoveAt(SelectedPathPoint);SelectedPathPoint=Math.Min(SelectedPathPoint,_pointWorld.Count-1);ApplyPathWorld();EndPathEdit(true);return true;
    }
}
