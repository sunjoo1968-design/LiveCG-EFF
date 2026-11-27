using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LiveCG.Core;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace LiveCG.App;

public sealed class ThumbnailConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        try
        {
            using var source=SourceImage.Read((string)value);var scale=Math.Min(1,180d/Math.Max(source.Width,source.Height));using var small=new SKBitmap(Math.Max(1,(int)Math.Round(source.Width*scale)),Math.Max(1,(int)Math.Round(source.Height*scale)));
            using(var canvas=new SKCanvas(small)){canvas.Clear(SKColors.Transparent);using var original=SKImage.FromBitmap(source);canvas.DrawImage(original,SKRect.Create(small.Width,small.Height),new SKSamplingOptions(SKFilterMode.Linear));}
            using var encoded=SKImage.FromBitmap(small);using var png=encoded.Encode(SKEncodedImageFormat.Png,100);using var stream=new MemoryStream(png.ToArray());
            var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;image.StreamSource=stream;image.EndInit(); image.Freeze(); return image;
        }
        catch { return null; }
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed partial class PreviewControl : SKElement, IDisposable
{
    private SKBitmap? _source;
    private SKImage? _background;
    private EffectScene? _scene;
    private ImageJob? _job;
    private SKRect _content;
    private SKPoint _start, _panStart, _oldPan;
    private SKPoint? _hoverPoint, _pendingDraftScreen, _lastScreen;
    private SKPoint _pendingDraftImage;
    private double? _horizontalGuideY;
    private readonly Dictionary<Guid, (Guid SourceId, double FallbackY)> _copiedRegionOrigins = [];
    private NormalizedRect? _before;
    private EffectRegion? _dragRegion, _draft;
    private int _handle = -1;
    private bool _panning, _spaceHeld, _addMode, _creatingMask, _showOverlays = true;
    private readonly List<SKPoint> _pathPoints=[];
    public PreviewViewport Viewport { get; } = new();
    private SKBitmap? _cachedFrame;private SKImage? _cachedImage;private (double Time,bool Effects,double Opacity,LoopRestMode? RestMode,long Revision)? _frameKey;private long _revision;
    public void ModelChanged(){_revision++;InvalidateVisual();}
    public int DigitCount(EffectRegion region)=>_scene?.DigitCount(region)??0;
    private EffectRegion? _selectedRegion;
    public EffectRegion? SelectedRegion { get=>_selectedRegion;set{if(_selectedRegion?.Id!=value?.Id)SelectedPathPoint=-1;_selectedRegion=value;UpdateCursor();} }
    public bool AddMode { get=>_addMode;set{if(_addMode==value)return;_addMode=value;UpdateCursor();InvalidateVisual();} }
    public bool IsSpaceHeld => _spaceHeld;
    public bool IsPanning => _panning;
    public bool ShowEffects { get; set; }
    public bool ShowOverlays { get=>_showOverlays;set{_showOverlays=value;UpdateCursor();InvalidateVisual();} }
    public bool ShowAllRegions { get; set; }
    public RegionShape NewShape { get; set; }
    private bool _pickColorMode;
    public bool PickColorMode { get=>_pickColorMode;set{_pickColorMode=value;if(!value)_pickPoint=null;UpdateCursor();InvalidateVisual();} }
    private SKPoint? _pickPoint;
    public event Action<SKColor>? ColorPicked;
    public double Time { get; set; }
    public double OutputOpacity { get; set; }=1;
    public LoopRestMode? RestDisplay { get; set; }
    public EffectOptions NewRegionEffect { get; set; } = new();
    public event Action<EffectRegion?>? RegionSelected;
    public event Action<EffectRegion>? RegionCreated;
    public event Action<EffectRegion>? MaskCreated;
    public event Action? MaskEdited;
    public event Action? MaskDeleteRequested;
    public bool CreatingMask { get=>_creatingMask;set{_creatingMask=value;UpdateCursor();InvalidateVisual();} }
    private MaskTrack? _selectedMask;
    public MaskTrack? SelectedMask { get=>_selectedMask;set{if(_selectedMask?.Id!=value?.Id)SelectedPathPoint=-1;_selectedMask=value;} }
    private MaskTrack? _maskDrag;
    public event Action? RegionEdited;
    public event Action? GestureStarted;
    public event Action<bool>? GestureEnded;
    public event Action? ViewChanged;
    public event Action? DeleteRequested;
    public event Action<string>? Message;
    private static readonly SKColor[] Colors = [new(93,226,177), new(110,182,255), new(255,190,100), new(231,137,243), new(255,124,151), new(160,220,119)];

    public PreviewControl()
    {
        Focusable = true;
        InputMethod.SetIsInputMethodEnabled(this, false);
        PaintSurface += Paint;
        SizeChanged += (_, _) => UpdateViewport();
        MouseEnter += (_, e) => { SyncSpaceState(); _hoverPoint = PointAt(e); UpdateCursor(); };
        MouseDown += (_, e) =>
        {
            if(e.ChangedButton is not MouseButton.Left and not MouseButton.Middle)return;
            SyncSpaceState();
            Focus(); var p = PointAt(e); _hoverPoint = p;
            var spacePan = e.ChangedButton == MouseButton.Middle || (e.ChangedButton == MouseButton.Left && (_spaceHeld || Keyboard.IsKeyDown(Key.Space)));
            if(spacePan)
            {
                if(BeginPointer(p, true, false)){CaptureMouse();UpdateCursor();e.Handled=true;}
                return;
            }
            if(PickColorMode&&e.ChangedButton==MouseButton.Left)
            {if(TrySampleColor(p,out var color)){PickColorMode=false;_pickPoint=null;ColorPicked?.Invoke(color);UpdateCursor();InvalidateVisual();}e.Handled=true;return;}
            if(AddMode&&NewShape==RegionShape.Polygon&&e.ChangedButton==MouseButton.Left){AddPathPoint(p,e.ClickCount>1);e.Handled=true;return;}
            if (BeginPointer(p, false, Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)))
            { CaptureMouse(); UpdateCursor(); e.Handled = true; }
        };
        MouseMove += (_, e) =>
        {
            SyncSpaceState();
            var p = PointAt(e); _hoverPoint = p;
            if(PickColorMode && !_panning && !_spaceHeld) HoverPointer(p);
            else MovePointer(p, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            UpdateCursor();
        };
        MouseLeave+=(_,_)=>{_hoverPoint=null;_pickPoint=null;UpdateCursor();InvalidateVisual();};
        MouseUp += (_, e) =>
        {
            if(e.ChangedButton is not MouseButton.Left and not MouseButton.Middle)return;
            if (_panning || _pendingDraftScreen is not null || _softTarget is not null || _pointDragging || _dragRegion is not null || _draft is not null)
            { EndPointer(); ReleaseMouseCapture(); UpdateCursor(); e.Handled = true; }
        };
        MouseWheel += (_, e) => { _hoverPoint = PointAt(e); ZoomAt(e.Delta > 0 ? 1.25 : .8, _hoverPoint); UpdateCursor(); e.Handled = true; };
        KeyDown += (_, e) =>
        {
            var key = e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key == Key.System ? e.SystemKey : e.Key;
            if (key is Key.LeftShift or Key.RightShift) UpdateDragConstraint(true);
            if (key == Key.Space) { SetSpaceHeld(true); e.Handled = true; return; }
            if (key == Key.Escape) { PickColorMode=false;_pickPoint=null;CancelGesture(); ReleaseMouseCapture(); UpdateCursor(); e.Handled = true; }
            if(_draft?.Shape==RegionShape.Polygon&&key==Key.Enter){FinishPath();e.Handled=true;}
            if(_draft?.Shape==RegionShape.Polygon&&key==Key.Back){RemovePathPoint();e.Handled=true;}
            if(key==Key.Delete&&PointEditMode){DeleteSelectedPathPoint();e.Handled=true;}
            else if(key==Key.Delete&&SelectedMask is not null){MaskDeleteRequested?.Invoke();e.Handled=true;}
            else if (key == Key.Delete && SelectedRegion is not null) { DeleteRequested?.Invoke(); e.Handled = true; }
        };
        KeyUp += (_, e) =>
        {
            var key = e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key == Key.System ? e.SystemKey : e.Key;
            if (key is Key.LeftShift or Key.RightShift)
                UpdateDragConstraint(Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift));
            if (key == Key.Space) { SetSpaceHeld(false); e.Handled = true; }
        };
        LostKeyboardFocus += (_, _) => { if (!Keyboard.IsKeyDown(Key.Space)) SetSpaceHeld(false); };
        IsVisibleChanged += (_, _) => { if (!IsVisible) SetSpaceHeld(false); };
        LostMouseCapture += (_, _) =>
        {
            if (_panning) { _panning = false; UpdateCursor(); return; }
            if (_pendingDraftScreen is not null) { _pendingDraftScreen = null; UpdateCursor(); return; }
            if (_softTarget is not null || _pointDragging || _dragRegion is not null || _draft is { Shape: not RegionShape.Polygon }) CancelGesture();
        };
    }
    public void RegisterCopiedRegion(Guid copiedRegionId, Guid sourceRegionId, double sourceY) =>
        _copiedRegionOrigins[copiedRegionId] = (sourceRegionId, sourceY);
    public void ClearCopiedRegions() => _copiedRegionOrigins.Clear();
    public bool IsHorizontalGuideActive => _horizontalGuideY is not null && _dragRegion is not null;
    public bool TryGetCopiedOriginY(EffectRegion region, out double originY)
    {
        originY = 0;
        if (!_copiedRegionOrigins.TryGetValue(region.Id, out var info)) return false;
        var liveSource = _job?.Regions.FirstOrDefault(r => r.Id == info.SourceId && r.Id != region.Id);
        originY = liveSource?.Bounds.Y ?? info.FallbackY;
        return true;
    }
    public void UpdateDragConstraint(bool constrain)
    {
        if (!_panning && _dragRegion is not null && _handle < 0 && _maskDrag is null && _lastScreen is { } screen)
            MovePointer(screen, constrain);
    }
    private void SyncSpaceState()
    {
        if (_spaceHeld && !Keyboard.IsKeyDown(Key.Space)) _spaceHeld = false;
    }
    public void SetSpaceHeld(bool held)
    {
        if (_spaceHeld == held) { UpdateCursor(); return; }
        _spaceHeld = held;
        if (held && _pendingDraftScreen is { } startScreen && _job is not null && _source is not null)
        {
            _pendingDraftScreen = null;
            _panning = true;
            _panStart = startScreen;
            _oldPan = Viewport.Pan;
        }
        UpdateCursor();
        InvalidateVisual();
    }
    private int HitResizeHandle(SKPoint screen)
    {
        if (AddMode || _source is null) return -1;
        if (SelectedMask is { } mask)
        {
            var maskHandles = Handles(Viewport.ToScreen(mask.Bounds));
            return Array.FindIndex(maskHandles, h => Math.Abs(screen.X - h.X) <= 8 && Math.Abs(screen.Y - h.Y) <= 8);
        }
        if (SelectedRegion is { Locked: false } selected)
        {
            var handles = Handles(Viewport.ToScreen(selected.Bounds));
            return Array.FindIndex(handles, h => Math.Abs(screen.X - h.X) <= 8 && Math.Abs(screen.Y - h.Y) <= 8);
        }
        return -1;
    }
    public void UpdateCursor()
    {
        if (_panning) { Cursor = PointerOverlay.GrabHandCursor; return; }
        if (_spaceHeld) { Cursor = PointerOverlay.OpenHandCursor; return; }
        if (PickColorMode) { Cursor = _pickPoint is { } at && TrySampleColor(at, out _) ? Cursors.None : Cursors.Cross; return; }
        if (_job is null || _source is null) { Cursor = Cursors.Arrow; return; }
        if (_draft is not null) { Cursor = Cursors.Cross; return; }
        if (AddMode || CreatingMask || (ShowOverlays && _job.Regions.Count == 0))
        {
            Cursor = _hoverPoint is not { } p || Viewport.ImageRect.Contains(p) ? Cursors.Cross : Cursors.Arrow;
            return;
        }
        if (ShowOverlays && _hoverPoint is { } hover)
        {
            if (HitSoftHandle(hover) >= 0) { Cursor = Cursors.SizeAll; return; }
            if (PointEditMode && HitsPathEditTarget(hover)) { Cursor = Cursors.Cross; return; }
            var handle = HitResizeHandle(hover);
            if (handle >= 0)
            {
                Cursor = handle switch
                {
                    0 or 4 => Cursors.SizeNWSE,
                    2 or 6 => Cursors.SizeNESW,
                    1 or 5 => Cursors.SizeNS,
                    _ => Cursors.SizeWE
                };
                return;
            }
            if (Viewport.ImageRect.Contains(hover))
            {
                if (SelectedMask is { } mask)
                {
                    var rect = Viewport.ToScreen(mask.Bounds);
                    using var path = RegionMask.Create(mask.Shape, rect, mask.Points);
                    if (path.Contains(hover.X, hover.Y)) { Cursor = Cursors.SizeAll; return; }
                }
                var imagePoint = Viewport.ToImage(hover);
                var hitRegion = LayerGraph.Rows(_job.Regions).Where(r => r.Mask is null).Select(r => r.Region)
                    .FirstOrDefault(r => LayerGraph.Visible(r, _job.Regions) && RegionMask.Contains(r, imagePoint, _source.Width, _source.Height));
                if (hitRegion is not null) { Cursor = hitRegion.Locked ? Cursors.Arrow : Cursors.SizeAll; return; }
                if (NewShape != RegionShape.Polygon) { Cursor = Cursors.Cross; return; }
            }
        }
        Cursor = Cursors.Arrow;
    }
    private SKPoint PointAt(MouseEventArgs e) { var p = e.GetPosition(this); return new((float)p.X, (float)p.Y); }
    public void SetJob(ImageJob? job)
    {
        _cachedImage?.Dispose();_cachedFrame?.Dispose();_cachedImage=null;_cachedFrame=null;_frameKey=null;_revision++;
        CancelGesture();SelectedMask=null;CreatingMask=false; _scene?.Dispose(); _source?.Dispose(); _scene = null; _source = null; _job = job; SelectedRegion = null;
        if (job is not null && File.Exists(job.SourcePath))
        {
            _source = SourceImage.Read(job) ?? throw new InvalidDataException("이미지를 읽을 수 없습니다.");
            _scene = new(_source);
            var minX = _source.Width; var minY = _source.Height; var maxX = 0; var maxY = 0;
            for (var y = 0; y < _source.Height; y += 4) for (var x = 0; x < _source.Width; x += 4)
                if (_source.GetPixel(x, y).Alpha > 16) { minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x + 4); maxY = Math.Max(maxY, y + 4); }
            _content = maxX > minX ? new(minX, minY, Math.Min(maxX, _source.Width), Math.Min(maxY, _source.Height)) : SKRect.Create(_source.Width, _source.Height);
        }
        Viewport.Fit(false); UpdateViewport();
    }
    private void UpdateViewport()
    {
        if (_source is not null) Viewport.Update(ActualWidth, ActualHeight, _source.Width, _source.Height, _content);
        ViewChanged?.Invoke(); InvalidateVisual();
    }
    public void ZoomAt(double multiplier, SKPoint? anchor = null)
    { Viewport.ZoomAt(multiplier, anchor ?? new((float)ActualWidth / 2, (float)ActualHeight / 2)); ViewChanged?.Invoke(); InvalidateVisual(); }
    public void Fit(bool content) { Viewport.Fit(content); ViewChanged?.Invoke(); InvalidateVisual(); }
    public void NativeSize() { Viewport.Native(VisualTreeHelper.GetDpi(this).DpiScaleX); ViewChanged?.Invoke(); InvalidateVisual(); }
    public void SetBackground(string? path)
    { _background?.Dispose(); _background = null; if (path is not null) { using var b = SourceImage.Read(path) ?? throw new InvalidDataException("배경을 읽을 수 없습니다."); _background = SKImage.FromBitmap(b); } InvalidateVisual(); }
    public bool TrySampleColor(SKPoint screen,out SKColor color)
    {
        color=SKColors.Transparent;if(_source is null)return false;var p=Viewport.ToImage(screen);
        if(p.X<0||p.Y<0||p.X>=_source.Width||p.Y>=_source.Height)return false;
        color=_source.GetPixel((int)p.X,(int)p.Y);return true;
    }
    private void DrawEyedropper(SKCanvas canvas)
    {
        if(!PickColorMode||_pickPoint is not { } at||_source is null||!TrySampleColor(at,out var color))return;
        PointerOverlay.Eyedropper(canvas,_source,Viewport.ToImage(at),at,(float)ActualWidth,(float)ActualHeight);
    }
    public void HoverPointer(SKPoint point){_hoverPoint=point;_pickPoint=point;UpdateCursor();InvalidateVisual();}

    private void Paint(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas; canvas.Clear(new SKColor(10,18,28));
        canvas.Scale(e.Info.Width / (float)Math.Max(1, ActualWidth), e.Info.Height / (float)Math.Max(1, ActualHeight));
        if (_source is null || _scene is null)
        {
            using var p = new SKPaint { Color = new SKColor(140,165,185), IsAntialias = true };
            using var f = new SKFont(SKTypeface.FromFamilyName("Malgun Gothic"), 15);
            canvas.DrawText("PNG 추가 → 마우스로 영역 지정 → 타임라인에 효과 추가", 18, 45, f, p); return;
        }
        var imageRect = Viewport.ImageRect;
        canvas.Save(); canvas.ClipRect(imageRect);
        using var paint = new SKPaint { IsAntialias = true };
        paint.Color = new(30,40,51); canvas.DrawRect(imageRect, paint); paint.Color = new(44,57,70);
        if(ComparisonBackground==0)for (var y = 0; y < ActualHeight; y += 16) for (var x = 0; x < ActualWidth; x += 16)
            if (((int)x / 16 + (int)y / 16) % 2 == 0) canvas.DrawRect((float)x, (float)y, 16, 16, paint);
        if(ComparisonBackground is 1 or 2 or 3){paint.Color=ComparisonBackground==2?SKColors.White:SKColors.Black;canvas.DrawRect(imageRect,paint);if(ComparisonBackground==3){paint.Color=SKColors.White;canvas.DrawRect(SKRect.Create(imageRect.MidX,imageRect.Top,imageRect.Width/2,imageRect.Height),paint);}}
        canvas.Save(); canvas.Translate(imageRect.Left, imageRect.Top); canvas.Scale((float)Viewport.Scale);
        if (_background is not null&&ComparisonBackground==4) canvas.DrawImage(_background, SKRect.Create(_source.Width, _source.Height));
        var key=(Time,ShowEffects,OutputOpacity,RestDisplay,_revision);
        if(_frameKey!=key)
        {
            _cachedImage?.Dispose();_cachedFrame?.Dispose();_cachedFrame=new SKBitmap(_source.Width,_source.Height);using(var cached=new SKCanvas(_cachedFrame))
            {cached.Clear(SKColors.Transparent);var renderRegions=SectionRules.RenderRegions(_job!);if(RestDisplay==LoopRestMode.Original)_scene.DrawSource(cached);else if(RestDisplay!=LoopRestMode.Transparent){if(ShowEffects&&OutputOpacity<1){using var fade=new SKPaint{Color=SKColors.White.WithAlpha((byte)Math.Round(Math.Clamp(OutputOpacity,0,1)*255))};cached.SaveLayer(fade);_scene.Draw(cached,renderRegions,Time,true);cached.Restore();}else _scene.Draw(cached,renderRegions,Time,ShowEffects);}}
            _cachedImage=SKImage.FromBitmap(_cachedFrame);_frameKey=key;
        }
        if(_cachedImage is not null)canvas.DrawImage(_cachedImage,0,0);
        canvas.Restore(); canvas.Restore();
        DrawEyedropper(canvas);
        if (!ShowOverlays) return;
        for (var i = _job!.Regions.Count-1; i>=0; i--)if(LayerGraph.Visible(_job.Regions[i],_job.Regions))DrawRegion(canvas, _job.Regions[i], i, false);
        if (_horizontalGuideY is { } guideY && _dragRegion is not null)
        {
            var screenY = Viewport.ToScreen(new SKPoint(0, (float)(guideY * _source.Height))).Y;
            using var guidePaint = new SKPaint
            {
                Color = new SKColor(93, 226, 177, 210),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1.5f,
                IsAntialias = true,
                PathEffect = SKPathEffect.CreateDash([6f, 4f], 0)
            };
            canvas.DrawLine(Viewport.ImageRect.Left, screenY, Viewport.ImageRect.Right, screenY, guidePaint);
            guidePaint.PathEffect?.Dispose();
        }
        if(SelectedMask is { } mask)DrawRegion(canvas,new(){Id=mask.Id,Name="마스크 · "+mask.Name,Bounds=mask.Bounds,Shape=mask.Shape,Points=mask.Points},1,false);
        DrawPathHandles(canvas);
        DrawSoftGuides(canvas);
        if (_draft is not null)
        {
            if(_pathPoints.Count>0){using var path=new SKPath();var points=_pathPoints.Select(Viewport.ToScreen).ToArray();path.AddPoly(points,_draft.Shape==RegionShape.Freehand&&points.Length>2);using var p=new SKPaint{Color=Colors[0],IsAntialias=true,Style=SKPaintStyle.Stroke,StrokeWidth=2};canvas.DrawPath(path,p);if(_draft.Shape==RegionShape.Polygon)foreach(var at in points){p.Style=SKPaintStyle.Fill;canvas.DrawCircle(at,4,p);}}
            else DrawRegion(canvas,_draft,_job.Regions.Count,true);
        }
        if (AddMode)
        {
            paint.Color = new(30,75,67,230); canvas.DrawRect(8,8,330,28,paint);
            using var font = new SKFont(SKTypeface.FromFamilyName("Malgun Gothic"), 12); paint.Color = Colors[0];
            canvas.DrawText(NewShape==RegionShape.Polygon?"포인트 펜 · 클릭 / Enter 닫기 / Backspace / ESC":"영역 그리기 · 드래그로 지정 / ESC 취소",16,27,font,paint);
        }
    }
    private void DrawRegion(SKCanvas canvas, EffectRegion region, int index, bool draft)
    {
        var rect = Viewport.ToScreen(region.Bounds);
        var selected = draft || (_draft is null && (SelectedMask?.Id==region.Id||(SelectedMask is null&&SelectedRegion?.Id == region.Id))); var color = Colors[index % Colors.Length];
        using var path = RegionMask.Create(region.Shape, rect,region.Points);
        using var p = new SKPaint { Color = color.WithAlpha((byte)(selected ? 22 : 0)), IsAntialias = true };
        if (selected) canvas.DrawPath(path,p);
        p.Style = SKPaintStyle.Stroke; p.Color = color.WithAlpha((byte)(selected ? 255 : 55)); p.StrokeWidth = selected ? 2.5f : 1f;
        canvas.DrawPath(path,p); p.Style=SKPaintStyle.Fill;
        if (!selected && !ShowAllRegions) return;
        var label = $"{index+1:00}  {region.Name}{(region.Locked ? " · 잠금" : "")}{(selected ? "  ✓ 선택됨" : "")}";
        using var font = new SKFont(SKTypeface.FromFamilyName("Malgun Gothic",selected ? SKFontStyle.Bold : SKFontStyle.Normal),11);
        var width = Math.Min((float)ActualWidth-12,font.MeasureText(label)+14);
        var lx = Math.Clamp(rect.Left,4,Math.Max(4,(float)ActualWidth-width-4)); var ly = Math.Clamp(rect.Top-23,3,Math.Max(3,(float)ActualHeight-24));
        p.Color = color.WithAlpha(240);canvas.DrawRoundRect(SKRect.Create(lx,ly,width,21),3,3,p);
        p.Color = new(12,27,34);canvas.DrawText(label,lx+6,ly+15,font,p);
        if (selected && !region.Locked) foreach (var h in Handles(rect))
        {
            var square=SKRect.Create(h.X-4,h.Y-4,8,8);p.Color=SKColors.White;canvas.DrawRect(square,p);
            p.Style=SKPaintStyle.Stroke;p.Color=color;p.StrokeWidth=1.5f;canvas.DrawRect(square,p);p.Style=SKPaintStyle.Fill;
        }
    }
    public static SKPoint[] Handles(SKRect r) => [new(r.Left,r.Top),new(r.MidX,r.Top),new(r.Right,r.Top),new(r.Right,r.MidY),new(r.Right,r.Bottom),new(r.MidX,r.Bottom),new(r.Left,r.Bottom),new(r.Left,r.MidY)];
    public bool BeginPointer(SKPoint screen, bool pan = false, bool cycle = false)
    {
        if (_job is null || _source is null) return false;
        if (pan) { _panning = true; _panStart = screen; _oldPan = Viewport.Pan; _horizontalGuideY = null; UpdateCursor(); return true; }
        if (!ShowOverlays) return false;
        if(AddMode&&NewShape==RegionShape.Polygon){AddPathPoint(screen);return false;}
        _start = Viewport.ToImage(screen); _oldPan = Viewport.Pan; _pendingDraftScreen = null; _lastScreen = screen; _horizontalGuideY = null;
        if(TryBeginSoft(screen))return true;
        if(!AddMode&&PointEditMode&&TryBeginPathEdit(screen))return true;
        if(!AddMode&&SelectedMask is { } mask)
        {
            var rect=Viewport.ToScreen(mask.Bounds);var handles=Handles(rect);var handle=Array.FindIndex(handles,h=>Math.Abs(screen.X-h.X)<=8&&Math.Abs(screen.Y-h.Y)<=8);using var path=RegionMask.Create(mask.Shape,rect,mask.Points);
            if(handle<0&&!path.Contains(screen.X,screen.Y))return false;GestureStarted?.Invoke();_maskDrag=mask;_handle=handle;_dragRegion=new(){Id=mask.Id,Bounds=mask.Bounds,Shape=mask.Shape,Points=mask.Points};_before=mask.Bounds;return true;
        }
        _handle = -1;
        if (!AddMode && SelectedRegion is { Locked:false } selected)
        {
            var handles=Handles(Viewport.ToScreen(selected.Bounds));
            for(var i=0;i<handles.Length;i++)if(Math.Abs(screen.X-handles[i].X)<=8&&Math.Abs(screen.Y-handles[i].Y)<=8){GestureStarted?.Invoke();_handle=i;_dragRegion=selected;_before=selected.Bounds;return true;}
        }
        if (!Viewport.ImageRect.Contains(screen)) return false;
        var normalized = new SKPoint(_start.X/_source.Width,_start.Y/_source.Height);
        var hits = LayerGraph.Rows(_job.Regions).Where(r=>r.Mask is null).Select(r=>r.Region).Where(r=>LayerGraph.Visible(r,_job.Regions)&&RegionMask.Contains(r,_start,_source.Width,_source.Height)).OrderByDescending(r=>r.ParentId is not null).ToList();
        if(AddMode || (_job.Regions.Count==0&&hits.Count==0))
        {
            if(_job.Regions.Count>=100){Message?.Invoke("영역은 파일당 최대 100개입니다.");return false;}
            GestureStarted?.Invoke();
            _draft = new(){Name="영역 "+(_job.Regions.Count+1),Shape=NewShape,ContentOnly=!CreatingMask,CutoutEnabled=true,Bounds=new(normalized.X,normalized.Y,.00001,.00001)};
            _pathPoints.Clear();if(NewShape==RegionShape.Freehand)_pathPoints.Add(_start);
            _before=_draft.Bounds;return true;
        }
        var hit = cycle&&hits.Count>0 ? hits[(hits.FindIndex(r=>r.Id==SelectedRegion?.Id)+1)%hits.Count] :
            hits.FirstOrDefault(r=>r.Id==SelectedRegion?.Id)??hits.FirstOrDefault();
        SelectedRegion=hit;RegionSelected?.Invoke(hit);InvalidateVisual();
        if(hit is null)
        {
            if(NewShape==RegionShape.Polygon)return false;
            _pendingDraftScreen=screen;_pendingDraftImage=_start;
            return true;
        }
        if(hit.Locked)return false;
        GestureStarted?.Invoke();_dragRegion=hit;_before=hit.Bounds;return true;
    }
    public void MovePointer(SKPoint screen, bool constrain = false)
    {
        if(_panning){Viewport.Pan=new(_oldPan.X+screen.X-_panStart.X,_oldPan.Y+screen.Y-_panStart.Y);ViewChanged?.Invoke();InvalidateVisual();return;}
        _lastScreen = screen;
        if(_softTarget is not null){MoveSoft(screen);return;}
        if(_source is null)return;
        if(_pointDragging){MovePathPoint(screen);return;}
        if(_pendingDraftScreen is { } pendingScreen && _job is not null)
        {
            if(SKPoint.Distance(screen,pendingScreen)<4)return;
            _pendingDraftScreen=null;
            if(_job.Regions.Count>=100){Message?.Invoke("영역은 파일당 최대 100개입니다.");return;}
            _start=_pendingDraftImage;
            var nx=Math.Clamp(_start.X/_source.Width,0,1);var ny=Math.Clamp(_start.Y/_source.Height,0,1);
            GestureStarted?.Invoke();
            _draft=new(){Name="영역 "+(_job.Regions.Count+1),Shape=NewShape,ContentOnly=!CreatingMask,CutoutEnabled=true,Bounds=new(nx,ny,.00001,.00001)};
            _pathPoints.Clear();if(NewShape==RegionShape.Freehand)_pathPoints.Add(_start);
            _before=_draft.Bounds;
        }
        var point=Viewport.ToImage(screen);var dx=(point.X-_start.X)/_source.Width;var dy=(point.Y-_start.Y)/_source.Height;
        if(_draft is not null)
        {
            if(_draft.Shape==RegionShape.Polygon){InvalidateVisual();return;}
            if(_draft.Shape==RegionShape.Freehand){var bounded=new SKPoint(Math.Clamp(point.X,0,_source.Width),Math.Clamp(point.Y,0,_source.Height));if(_pathPoints.Count<4096&&SKPoint.Distance(_pathPoints[^1],bounded)>=1)_pathPoints.Add(bounded);InvalidateVisual();return;}
            if (constrain)
            {
                var px=Math.Clamp(point.X,0,_source.Width);var py=Math.Clamp(point.Y,0,_source.Height);
                var side=Math.Min(Math.Abs(px-_start.X),Math.Abs(py-_start.Y));
                point=new(_start.X+Math.Sign(px-_start.X)*side,_start.Y+Math.Sign(py-_start.Y)*side);
            }
            var x=Math.Clamp(point.X/_source.Width,0,1);var y=Math.Clamp(point.Y/_source.Height,0,1);
            var sx=_start.X/_source.Width;var sy=_start.Y/_source.Height;
            _draft.Bounds=new NormalizedRect(Math.Min(sx,x),Math.Min(sy,y),Math.Max(.00001,Math.Abs(x-sx)),Math.Max(.00001,Math.Abs(y-sy))).Clamp();
        }
        else if(_dragRegion is not null && _before is not null)
        {
            if (_handle >= 0)
            {
                _horizontalGuideY = null;
                _dragRegion.Bounds = RegionGeometry.Resize(_before, _handle, dx, dy, _source.Width, _source.Height);
            }
            else if (_maskDrag is null && constrain && TryGetCopiedOriginY(_dragRegion, out var originY))
            {
                var alignedY = Math.Clamp(originY, 0, 1 - _before.Height);
                var movedX = Math.Clamp(_before.X + dx, 0, 1 - _before.Width);
                _dragRegion.Bounds = new NormalizedRect(movedX, alignedY, _before.Width, _before.Height);
                _horizontalGuideY = alignedY;
            }
            else
            {
                _horizontalGuideY = null;
                _dragRegion.Bounds = RegionGeometry.Move(_before, dx, dy);
            }
            if(_maskDrag is not null){_maskDrag.Bounds=_dragRegion.Bounds;MaskEdited?.Invoke();}else RegionEdited?.Invoke();
        }
        else return;
        InvalidateVisual();
    }
    public void EndPointer()
    {
        if(_panning){_panning=false;_lastScreen=null;_horizontalGuideY=null;InvalidateVisual();ViewChanged?.Invoke();UpdateCursor();return;}
        if(_pendingDraftScreen is not null){_pendingDraftScreen=null;_lastScreen=null;_horizontalGuideY=null;UpdateCursor();return;}
        if(EndSoft(true)){_lastScreen=null;_horizontalGuideY=null;UpdateCursor();return;}
        if(_pointDragging){EndPathEdit(true);_lastScreen=null;_horizontalGuideY=null;UpdateCursor();return;}
        if(_draft?.Shape==RegionShape.Polygon){_lastScreen=null;_horizontalGuideY=null;UpdateCursor();return;}
        if(_draft?.Shape==RegionShape.Freehand){FinishPath();_lastScreen=null;_horizontalGuideY=null;UpdateCursor();return;}
        var edited=_draft is not null||_dragRegion is not null;
        if(_draft is not null && _job is not null && _source is not null)
        {
            if(_draft.Bounds.Width*_source.Width>=2&&_draft.Bounds.Height*_source.Height>=2)
            { CommitDraft(); }
            else Message?.Invoke("지정 영역이 너무 작습니다. 2픽셀 이상 드래그하세요.");
        }
        if(_dragRegion is not null){if(_maskDrag is not null)MaskEdited?.Invoke();else RegionEdited?.Invoke();}
        _draft=null;_dragRegion=null;_maskDrag=null;_before=null;_lastScreen=null;_horizontalGuideY=null;_panning=false;_handle=-1;InvalidateVisual();ViewChanged?.Invoke();UpdateCursor();
        if(edited)GestureEnded?.Invoke(true);
    }
    public void AddPathPoint(SKPoint screen,bool finish=false)
    {
        if(_source is null||_job is null||!ShowOverlays||!Viewport.ImageRect.Contains(screen))return;
        if(_draft is null){if(_job.Regions.Count>=100)return;GestureStarted?.Invoke();_draft=new(){Name="영역 "+(_job.Regions.Count+1),Shape=RegionShape.Polygon,ContentOnly=!CreatingMask,CutoutEnabled=true};_pathPoints.Clear();}
        if(_draft.Shape!=RegionShape.Polygon)return;
        if(_pathPoints.Count>=3&&SKPoint.Distance(Viewport.ToScreen(_pathPoints[0]),screen)<8){FinishPath();return;}
        var at=Viewport.ToImage(screen);if(_pathPoints.Count<4096&&(_pathPoints.Count==0||SKPoint.Distance(_pathPoints[^1],at)>.5))_pathPoints.Add(at);
        if(finish)FinishPath();else InvalidateVisual();
    }
    public void RemovePathPoint(){if(_pathPoints.Count>0)_pathPoints.RemoveAt(_pathPoints.Count-1);if(_pathPoints.Count==0)CancelGesture();else InvalidateVisual();}
    public bool FinishPath()
    {
        if(_draft is null||_source is null||_job is null||_pathPoints.Count<3){Message?.Invoke("포인트를3개 이상 지정하세요.");return false;}
        var left=_pathPoints.Min(p=>p.X);var top=_pathPoints.Min(p=>p.Y);var width=_pathPoints.Max(p=>p.X)-left;var height=_pathPoints.Max(p=>p.Y)-top;
        if(width<2||height<2){Message?.Invoke("영역이 너무 작습니다. ESC로 취소하거나 포인트를 추가하세요.");return false;}
        _draft.Bounds=NormalizedRect.FromPixels(SKRect.Create(left,top,width,height),_source.Width,_source.Height);_draft.Points=_pathPoints.Select(p=>new RegionPoint(Math.Clamp((p.X-left)/width,0,1),Math.Clamp((p.Y-top)/height,0,1))).ToList();
        CommitDraft();_pathPoints.Clear();_panning=false;InvalidateVisual();ViewChanged?.Invoke();GestureEnded?.Invoke(true);return true;
    }
    private void CommitDraft()
    {
        if(_draft is null||_job is null)return;var draft=_draft;_draft=null;AddMode=false;
        if(CreatingMask){CreatingMask=false;MaskCreated?.Invoke(draft);}
        else{RegionCreated?.Invoke(draft);_job.Regions.Insert(0,draft);SelectedRegion=draft;RegionSelected?.Invoke(draft);RegionEdited?.Invoke();}
    }
    public void CancelGesture()
    {
        _pendingDraftScreen=null;
        if(EndSoft(false)){_lastScreen=null;_horizontalGuideY=null;UpdateCursor();return;}
        if(_pointDragging)EndPathEdit(false);
        var edited=_draft is not null||_dragRegion is not null;
        if(_dragRegion is not null&&_before is not null)_dragRegion.Bounds=_before;
        if(_maskDrag is not null&&_before is not null){_maskDrag.Bounds=_before;MaskEdited?.Invoke();}_maskDrag=null;
        _draft=null;_pathPoints.Clear();_dragRegion=null;_before=null;_lastScreen=null;_horizontalGuideY=null;_panning=false;_handle=-1;AddMode=false;InvalidateVisual();ViewChanged?.Invoke();UpdateCursor();
        if(edited){RegionEdited?.Invoke();GestureEnded?.Invoke(false);}
    }
    public void Dispose(){DisposeSoftGuides();_cachedImage?.Dispose();_cachedFrame?.Dispose();_scene?.Dispose();_source?.Dispose();_background?.Dispose();}
}
