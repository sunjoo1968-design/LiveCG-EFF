using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LiveCG.Core;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace LiveCG.App;

public sealed class MaskCanvas:SKElement,IDisposable
{
    private readonly SKBitmap _source;private readonly SKImage _image;private SKBitmap? _display,_foreground;private SKImage? _displayImage;private bool _dirty=true,_drawing,_pan;private SKPoint _last,_oldPan;
    public PreviewViewport Viewport { get; }=new();
    public MaskBuffer Buffer { get; set; }
    private int _brushMode;private bool _pickColor;private float _brushSize=16;private SKPoint? _hover;
    public int BrushMode { get=>_brushMode;set{_brushMode=value;UpdateCursor();} }
    public int ViewMode { get; set; }
    public float BrushSize { get=>_brushSize;set{_brushSize=value;InvalidateVisual();} }
    public bool PickColor { get=>_pickColor;set{_pickColor=value;UpdateCursor();} }
    public float BrushScreenRadius=>(float)(BrushSize*Viewport.Scale);
    private void UpdateCursor(){Cursor=PickColor||BrushMode!=0?(_hover is { } at&&TrySampleColor(at,out _)?Cursors.None:Cursors.Cross):Cursors.Arrow;InvalidateVisual();}
    public void HoverPointer(SKPoint point){_hover=point;UpdateCursor();}
    public bool TrySampleColor(SKPoint screen,out SKColor color){var p=Viewport.ToImage(screen);color=SKColors.Transparent;if(p.X<0||p.Y<0||p.X>=_source.Width||p.Y>=_source.Height)return false;color=_source.GetPixel((int)p.X,(int)p.Y);return true;}
    public event Action? StrokeStarted;
    public event Action? StrokeEnded;
    public event Action<SKColor>? ColorPicked;
    public MaskCanvas(SKBitmap source,MaskBuffer mask)
    {
        _source=source;_image=SKImage.FromBitmap(source);Buffer=mask;Focusable=true;PaintSurface+=Paint;SizeChanged+=(_,_)=>UpdateViewport();
        MouseDown+=(_,e)=>
        {
            Focus();var p=e.GetPosition(this);var point=new SKPoint((float)p.X,(float)p.Y);HoverPointer(point);
            if(e.ChangedButton==MouseButton.Middle||Keyboard.IsKeyDown(Key.Space)){_pan=true;_last=point;_oldPan=Viewport.Pan;CaptureMouse();return;}
            if(e.ChangedButton!=MouseButton.Left)return;var at=Viewport.ToImage(point);
            if(at.X<0||at.Y<0||at.X>=_source.Width||at.Y>=_source.Height)return;
            if(PickColor){PickColor=false;ColorPicked?.Invoke(_source.GetPixel((int)at.X,(int)at.Y));return;}
            if(BrushMode==0)return;StrokeStarted?.Invoke();_drawing=true;_last=at;CaptureMouse();Buffer.Brush(at.X,at.Y,BrushSize,BrushMode==1);Changed();
        };
        MouseMove+=(_,e)=>
        {
            var p=e.GetPosition(this);var screen=new SKPoint((float)p.X,(float)p.Y);HoverPointer(screen);
            if(_pan){Viewport.Pan=new(_oldPan.X+screen.X-_last.X,_oldPan.Y+screen.Y-_last.Y);InvalidateVisual();return;}
            if(!_drawing)return;var at=Viewport.ToImage(screen);var distance=SKPoint.Distance(_last,at);var steps=Math.Max(1,(int)(distance/Math.Max(1,BrushSize/3)));
            for(var i=1;i<=steps;i++)Buffer.Brush(_last.X+(at.X-_last.X)*i/steps,_last.Y+(at.Y-_last.Y)*i/steps,BrushSize,BrushMode==1);_last=at;Changed();
        };
        MouseUp+=(_,_)=>{if(_drawing){_drawing=false;StrokeEnded?.Invoke();}_pan=false;ReleaseMouseCapture();};
        LostMouseCapture+=(_,_)=>{if(_drawing){_drawing=false;StrokeEnded?.Invoke();}_pan=false;};
        MouseWheel+=(_,e)=>{var p=e.GetPosition(this);Viewport.ZoomAt(e.Delta>0?1.25:.8,new((float)p.X,(float)p.Y));InvalidateVisual();e.Handled=true;};
        MouseLeave+=(_,_)=>{_hover=null;InvalidateVisual();};
        KeyDown+=(_,e)=>{if(e.Key==Key.Escape){PickColor=false;_hover=null;InvalidateVisual();e.Handled=true;}};
    }
    private void UpdateViewport(){Viewport.Update(ActualWidth,ActualHeight,_source.Width,_source.Height,SKRect.Create(_source.Width,_source.Height));InvalidateVisual();}
    public void Changed(){_dirty=true;InvalidateVisual();}
    public void SetForeground(string png){_foreground?.Dispose();_foreground=string.IsNullOrEmpty(png)?null:SKBitmap.Decode(Convert.FromBase64String(png));Changed();}
    public void Fit(){Viewport.Fit(false);UpdateViewport();}
    private void Paint(object? sender,SKPaintSurfaceEventArgs e)
    {
        var c=e.Surface.Canvas;c.Clear(new SKColor(13,23,34));c.Scale(e.Info.Width/(float)Math.Max(1,ActualWidth),e.Info.Height/(float)Math.Max(1,ActualHeight));
        if(_dirty)
        {
            _displayImage?.Dispose();_display?.Dispose();_display=new SKBitmap(_source.Width,_source.Height);
            for(var y=0;y<_source.Height;y++)for(var x=0;x<_source.Width;x++)
            {var alpha=Buffer.Alpha[y*Buffer.Width+x];var original=(ViewMode==2?_foreground:null)?.GetPixel(x,y)??_source.GetPixel(x,y);_display.SetPixel(x,y,ViewMode==1?SKColors.White.WithAlpha(alpha):ViewMode==2?original.WithAlpha((byte)(original.Alpha*alpha/255)):new SKColor(60,235,173,(byte)(alpha*.36)));}
            _displayImage=SKImage.FromBitmap(_display);_dirty=false;
        }
        var rect=Viewport.ImageRect;using var paint=new SKPaint();
        c.Save();c.ClipRect(rect);for(var y=0;y<ActualHeight;y+=14)for(var x=0;x<ActualWidth;x+=14){paint.Color=((x/14+y/14)%2==0)?new(38,49,62):new(58,69,82);c.DrawRect((float)x,(float)y,14,14,paint);}
        c.Translate(rect.Left,rect.Top);c.Scale((float)Viewport.Scale);
        if(ViewMode==0)c.DrawImage(_image,0,0);if(_displayImage is not null)c.DrawImage(_displayImage,0,0);c.Restore();
        if(_hover is { } at&&TrySampleColor(at,out _))
        {if(PickColor)PointerOverlay.Eyedropper(c,_source,Viewport.ToImage(at),at,(float)ActualWidth,(float)ActualHeight);else if(BrushMode!=0&&!_pan)PointerOverlay.Brush(c,at,BrushScreenRadius,BrushMode==1);}
    }
    public void Dispose(){_image.Dispose();_displayImage?.Dispose();_display?.Dispose();_foreground?.Dispose();}
}

public sealed class ObjectSelectionWindow:Window
{
    private readonly SKBitmap _crop;private readonly int _sourceWidth,_sourceHeight;private readonly SKRectI _original;
    private readonly ComboBox _mode=new(),_view=new(),_brush=new();private readonly Slider _tolerance=new(){Minimum=0,Maximum=150,Value=35},_feather=new(){Minimum=0,Maximum=6,Value=1,IsSnapToTickEnabled=true,TickFrequency=1},_size=new(){Minimum=2,Maximum=100,Value=16};
    private readonly CheckBox _shadow=new(){Content="그림자 포함",IsChecked=true},_interior=new(){Content="오브젝트 안쪽 보존",IsChecked=true},_edgeClean=new(){Content="배경색 번짐 제거",IsChecked=true};private readonly TextBlock _info=new(),_radiusText=new();
    private readonly Slider _shrink=new(){Minimum=0,Maximum=3,Value=0,IsSnapToTickEnabled=true,TickFrequency=1};
    private sealed record MaskState(MaskBuffer Mask,MaskBuffer Erase,string Foreground);
    private SKColor _background;private readonly List<MaskState> _undo=[],_redo=[];private MaskState? _strokeBefore;private MaskBuffer _erase;private string _foregroundPng="";private readonly EffectRegion _region;
    public double BrushRadius=>_size.Value;
    public MaskCanvas Canvas { get; }
    public NormalizedRect SelectedBounds { get; private set; }
    public ObjectCutout? SelectedCutout { get; private set; }
    public ObjectSelectionWindow(SKBitmap source,EffectRegion region)
    {
        Title="오브젝트에 맞추기 · "+ProductInfo.Credit;Width=1120;Height=800;MinWidth=760;MinHeight=580;WindowStartupLocation=WindowStartupLocation.CenterOwner;Background=new SolidColorBrush(Color.FromRgb(23,33,45));Foreground=Brushes.White;
        _sourceWidth=source.Width;_sourceHeight=source.Height;_original=ObjectExtractor.PixelRect(region.Bounds,source.Width,source.Height);using var subset=new SKBitmap();source.ExtractSubset(subset,_original);_crop=subset.Copy();_background=ObjectExtractor.GuessSurrounding(source,_original);
        _region=region.Copy(false);
        SelectedBounds=region.Bounds;SelectedCutout=region.Cutout;var mask=ObjectExtractor.Extract(_crop,new(){Background=_background});
        Restrict(mask);_erase=mask.Morph(1,true);
        if(region.Cutout is { } existing&&existing.Width==_crop.Width&&existing.Height==_crop.Height){mask=MaskBuffer.Decode(existing);_foregroundPng=existing.ForegroundPng;if(!string.IsNullOrEmpty(existing.ErasePng))_erase=MaskBuffer.Decode(new(){Width=existing.Width,Height=existing.Height,MaskPng=existing.ErasePng});}
        else{var clean=ObjectExtractor.CleanEdges(_crop,mask,_background);mask=clean.Mask;_foregroundPng=clean.Foreground;}
        Canvas=new(_crop,mask);Canvas.SetForeground(_foregroundPng);var root=new DockPanel{Margin=new Thickness(10)};Content=root;
        var actions=new WrapPanel();DockPanel.SetDock(actions,Dock.Bottom);root.Children.Add(actions);
        void Button(Panel host,string text,Action click){var b=new Button{Content=text,Padding=new Thickness(9,6,9,6)};b.Click+=(_,_)=>click();host.Children.Add(b);}
        Button(actions,"되돌리기",Undo);Button(actions,"다시 실행",Redo);Button(actions,"화면 맞춤",Canvas.Fit);Button(actions,"취소",()=>Close());Button(actions,"오브젝트 적용",()=>{if(PrepareResult())DialogResult=true;});actions.Children.Add(_info);
        var settings=new StackPanel{Margin=new Thickness(5)};var settingsScroll=new ScrollViewer{Width=245,Content=settings,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};DockPanel.SetDock(settingsScroll,Dock.Right);root.Children.Add(settingsScroll);
        void Label(string text)=>settings.Children.Add(new TextBlock{Text=text,Margin=new Thickness(4,9,0,3),TextWrapping=TextWrapping.Wrap});
        Label("분리 방법");foreach(var text in new[]{"자동 후보","PNG 투명도 사용","배경색으로 분리"})_mode.Items.Add(text);_mode.SelectedIndex=0;settings.Children.Add(_mode);
        Button(settings,"스포이드 · 원본 배경색",()=>{Canvas.PickColor=true;Canvas.Focus();});Button(settings,"배경 색상표",()=>{var picker=new ColorPickerWindow(_background){Owner=this};if(picker.ShowDialog()==true)_background=picker.SelectedColor;else if(picker.PickSourceRequested){Canvas.PickColor=true;Canvas.Focus();}});
        Button(settings,"단색 배경 빠른 정리",ApplySolidCleanup);
        Label("허용오차 — 높으면 배경을 더 제거");settings.Children.Add(_tolerance);Label("가장자리 부드럽게");settings.Children.Add(_feather);settings.Children.Add(_shadow);
        settings.Children.Add(_interior);_interior.ToolTip="바깥에 연결된 배경만 지워 닫힌 오브젝트 안의 같은 색 글자를 보존합니다.";
        settings.Children.Add(_edgeClean);Label("테두리 안쪽 축소 (0~3px)");settings.Children.Add(_shrink);Button(settings,"테두리 1px 줄이기",ContractOnePixel);
        Button(settings,"분리 다시 계산",()=>Generate());Label("옵션을 바꾼 뒤 다시 계산하세요.\n사진 배경은 포함/제외 브러시로 보정할 수 있습니다.");
        Label("미리보기");foreach(var text in new[]{"원본 + 윤곽 표시","마스크","분리 결과"})_view.Items.Add(text);_view.SelectedIndex=0;settings.Children.Add(_view);
        Label("브러시");foreach(var text in new[]{"보기 / 이동","포함 (+)","제외 (−)"})_brush.Items.Add(text);_brush.SelectedIndex=0;settings.Children.Add(_brush);Label("브러시 반경 · [ / ] · Shift=10px");_size.Minimum=1;_size.Maximum=200;settings.Children.Add(_size);settings.Children.Add(_radiusText);_radiusText.Text=$"{_size.Value:0}px";
        Label("휠: 확대 · Space/중간버튼: 이동\n원본 PNG는 수정하지 않습니다.");root.Children.Add(Canvas);
        Canvas.ColorPicked+=color=>{_background=color;_mode.SelectedIndex=2;_info.Text=ColorPickerWindow.Hex(color)+" · 다시 계산을 누르세요.";};
        _brush.SelectionChanged+=(_,_)=>Canvas.BrushMode=_brush.SelectedIndex;_view.SelectionChanged+=(_,_)=>{Canvas.ViewMode=_view.SelectedIndex;Canvas.Changed();};_size.ValueChanged+=(_,_)=>{Canvas.BrushSize=(float)_size.Value;_radiusText.Text=$"{_size.Value:0}px";};
        Canvas.StrokeStarted+=()=>_strokeBefore=Snapshot();Canvas.StrokeEnded+=()=>{if(_strokeBefore is not null){Record(_strokeBefore);_strokeBefore=null;}};
        PreviewKeyDown+=(_,e)=>{if(MainWindow.IsTextEditing(Keyboard.FocusedElement as DependencyObject))return;if(HandleBrushKey(e.Key,Keyboard.Modifiers)){e.Handled=true;return;}if(Keyboard.Modifiers==ModifierKeys.Control&&e.Key==Key.Z){Undo();e.Handled=true;}else if(Keyboard.Modifiers==(ModifierKeys.Control|ModifierKeys.Shift)&&e.Key==Key.Z){Redo();e.Handled=true;}};
        Closed+=(_,_)=>{Canvas.Dispose();_crop.Dispose();};
    }
    private void Restrict(MaskBuffer mask){using var path=RegionMask.Create(_region.Shape,SKRect.Create(mask.Width,mask.Height),_region.Points);for(var y=0;y<mask.Height;y++)for(var x=0;x<mask.Width;x++)if(!path.Contains(x+.5f,y+.5f))mask.Alpha[y*mask.Width+x]=0;}
    private MaskState Snapshot()=>new(Canvas.Buffer.Copy(),_erase.Copy(),_foregroundPng);
    private void Restore(MaskState state){Canvas.Buffer=state.Mask;_erase=state.Erase;_foregroundPng=state.Foreground;Canvas.SetForeground(_foregroundPng);}
    private void Record(MaskState before){_undo.Add(before);_redo.Clear();while(_undo.Count>20||_undo.Sum(m=>(long)m.Mask.Alpha.Length+m.Erase.Alpha.Length+m.Foreground.Length*2)>64*1024*1024)_undo.RemoveAt(0);}
    public bool HandleBrushKey(Key key,ModifierKeys modifiers)
    {if(modifiers is not ModifierKeys.None and not ModifierKeys.Shift||key is not Key.OemOpenBrackets and not Key.OemCloseBrackets)return false;_size.Value=Math.Clamp(_size.Value+(key==Key.OemCloseBrackets?1:-1)*(modifiers==ModifierKeys.Shift?10:1),_size.Minimum,_size.Maximum);return true;}
    public void ApplySolidCleanup(){_mode.SelectedIndex=2;_tolerance.Value=35;_feather.Value=0;_shadow.IsChecked=false;_interior.IsChecked=true;_edgeClean.IsChecked=true;_shrink.Value=1;Generate();_view.SelectedIndex=2;_info.Text="내부 보존 ·1px축소 ·색번짐 정리. 가는 글자는 축소0px로 조정하세요.";}
    public void ContractOnePixel(){var before=Snapshot();Canvas.Buffer=Canvas.Buffer.Morph(1,false);Record(before);if(_edgeClean.IsChecked==true){var clean=ObjectExtractor.CleanEdges(_crop,Canvas.Buffer,_background);Canvas.Buffer=clean.Mask;_foregroundPng=clean.Foreground;}Canvas.SetForeground(_foregroundPng);_info.Text="테두리1px 축소 · 되돌리기 가능";}
    public void Generate()
    {try{var next=ObjectExtractor.Extract(_crop,new(){Mode=(ExtractionMode)_mode.SelectedIndex,Background=_background,Tolerance=_tolerance.Value,Feather=(int)_feather.Value,IncludeShadow=_shadow.IsChecked==true,PreserveInterior=_interior.IsChecked==true});Restrict(next);var erase=next.Morph(1,true);next=next.Morph((int)_shrink.Value,false);var foreground="";if(_edgeClean.IsChecked==true){var clean=ObjectExtractor.CleanEdges(_crop,next,_background);next=clean.Mask;foreground=clean.Foreground;}Record(Snapshot());Canvas.Buffer=next;_erase=erase;_foregroundPng=foreground;Canvas.SetForeground(_foregroundPng);}catch(Exception ex){_info.Text=ex.Message;}}
    public void Undo(){if(_undo.Count==0)return;_redo.Add(Snapshot());var old=_undo[^1];_undo.RemoveAt(_undo.Count-1);Restore(old);}
    public void Redo(){if(_redo.Count==0)return;_undo.Add(Snapshot());var next=_redo[^1];_redo.RemoveAt(_redo.Count-1);Restore(next);}
    public bool PrepareResult(){try{var grown=Canvas.Buffer.Morph(1,true);var erase=_erase.Copy();for(var i=0;i<erase.Alpha.Length;i++)erase.Alpha[i]=Math.Max(erase.Alpha[i],grown.Alpha[i]);var result=ObjectExtractor.Fit(Canvas.Buffer,_original,_sourceWidth,_sourceHeight,_foregroundPng,erase);SelectedBounds=result.Bounds;SelectedCutout=result.Cutout;return true;}catch(Exception ex){_info.Text=ex.Message;return false;}}
}
