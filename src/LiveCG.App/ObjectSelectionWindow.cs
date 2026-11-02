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
    private void UpdateCursor(){Cursor=HolePickMode?Cursors.Cross:PickColor||BrushMode!=0?(_hover is { } at&&TrySampleColor(at,out _)?Cursors.None:Cursors.Cross):Cursors.Arrow;InvalidateVisual();}
    public void HoverPointer(SKPoint point){_hover=point;UpdateCursor();}
    public bool TrySampleColor(SKPoint screen,out SKColor color){var p=Viewport.ToImage(screen);color=SKColors.Transparent;if(p.X<0||p.Y<0||p.X>=_source.Width||p.Y>=_source.Height)return false;color=_source.GetPixel((int)p.X,(int)p.Y);return true;}
    public event Action? StrokeStarted;
    public event Action? StrokeEnded;
    public event Action<SKColor>? ColorPicked;
    public bool HolePickMode { get; set; }
    public MaskBuffer? PendingRemoval { get; set; }
    public SKColor ComparisonBackground { get; set; }=SKColors.Transparent;
    public event Action<int,int>? HolePicked;
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
            if(HolePickMode){HolePicked?.Invoke((int)at.X,(int)at.Y);return;}
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
        KeyDown+=(_,e)=>{if(e.Key==Key.Escape){PickColor=false;HolePickMode=false;PendingRemoval=null;_hover=null;InvalidateVisual();e.Handled=true;}};
    }
    private void UpdateViewport(){Viewport.Update(ActualWidth,ActualHeight,_source.Width,_source.Height,SKRect.Create(_source.Width,_source.Height));InvalidateVisual();}
    public void Changed(){_dirty=true;InvalidateVisual();}
    public void PaintStroke(IEnumerable<SKPoint> points,bool include){StrokeStarted?.Invoke();foreach(var point in points)Buffer.Brush(point.X,point.Y,BrushSize,include);Changed();StrokeEnded?.Invoke();}
    public void SetForeground(string png){_foreground?.Dispose();_foreground=string.IsNullOrEmpty(png)?null:SKBitmap.Decode(Convert.FromBase64String(png));Changed();}
    public void Fit(){Viewport.Fit(false);UpdateViewport();}
    private void Paint(object? sender,SKPaintSurfaceEventArgs e)
    {
        var c=e.Surface.Canvas;c.Clear(new SKColor(13,23,34));c.Scale(e.Info.Width/(float)Math.Max(1,ActualWidth),e.Info.Height/(float)Math.Max(1,ActualHeight));
        if(_dirty)
        {
            _displayImage?.Dispose();_display?.Dispose();_display=new SKBitmap(_source.Width,_source.Height);
            if(ViewMode<4)
            {
                using var maskBitmap=Buffer.Bitmap();using var maskImage=SKImage.FromBitmap(maskBitmap);using var display=new SKCanvas(_display);display.Clear(ViewMode==3?ComparisonBackground:SKColors.Transparent);
                if(ViewMode==0){using var filter=SKColorFilter.CreateBlendMode(new SKColor(60,235,173,92),SKBlendMode.SrcIn);using var overlay=new SKPaint{ColorFilter=filter};display.DrawImage(maskImage,0,0,overlay);}
                else if(ViewMode==1)display.DrawImage(maskImage,0,0);
                else{display.SaveLayer();if(_foreground is not null){using var image=SKImage.FromBitmap(_foreground);display.DrawImage(image,0,0);}else display.DrawImage(_image,0,0);using var multiply=new SKPaint{BlendMode=SKBlendMode.DstIn};display.DrawImage(maskImage,0,0,multiply);display.Restore();}
            }
            else for(var y=0;y<_source.Height;y++)for(var x=0;x<_source.Width;x++)
            {
                var alpha=Buffer.Alpha[y*Buffer.Width+x];var original=(ViewMode>=2?_foreground:null)?.GetPixel(x,y)??_source.GetPixel(x,y);var cut=original.WithAlpha((byte)(original.Alpha*alpha/255));
                if(ViewMode>=3){var a=cut.Alpha/255d;var bg=ComparisonBackground.Alpha/255d;var total=a+bg*(1-a);byte Mix(byte f,byte b)=>total==0?(byte)0:(byte)Math.Round((f*a+b*bg*(1-a))/total);var merged=new SKColor(Mix(cut.Red,ComparisonBackground.Red),Mix(cut.Green,ComparisonBackground.Green),Mix(cut.Blue,ComparisonBackground.Blue),(byte)Math.Round(total*255));if(ViewMode==4){var raw=_source.GetPixel(x,y);var d=Math.Max(Math.Abs(merged.Alpha-raw.Alpha),Math.Max(Math.Abs(merged.Red-raw.Red),Math.Max(Math.Abs(merged.Green-raw.Green),Math.Abs(merged.Blue-raw.Blue))));merged=new SKColor((byte)Math.Min(255,d*8),0,0);} _display.SetPixel(x,y,merged);}
                else _display.SetPixel(x,y,ViewMode==1?SKColors.White.WithAlpha(alpha):ViewMode==2?cut:new SKColor(60,235,173,(byte)(alpha*.36)));
            }
            _displayImage=SKImage.FromBitmap(_display);_dirty=false;
        }
        var rect=Viewport.ImageRect;using var paint=new SKPaint();
        c.Save();c.ClipRect(rect);for(var y=0;y<ActualHeight;y+=14)for(var x=0;x<ActualWidth;x+=14){paint.Color=((x/14+y/14)%2==0)?new(38,49,62):new(58,69,82);c.DrawRect((float)x,(float)y,14,14,paint);}
        c.Translate(rect.Left,rect.Top);c.Scale((float)Viewport.Scale);
        if(ViewMode==0)c.DrawImage(_image,0,0);if(_displayImage is not null)c.DrawImage(_displayImage,0,0);
        if(PendingRemoval is { } candidate){using var bitmap=candidate.Bitmap();using var image=SKImage.FromBitmap(bitmap);using var filter=SKColorFilter.CreateBlendMode(new SKColor(255,85,75,155),SKBlendMode.SrcIn);using var overlay=new SKPaint{ColorFilter=filter};c.DrawImage(image,0,0,overlay);}c.Restore();
        if(_hover is { } at&&TrySampleColor(at,out _))
        {if(PickColor)PointerOverlay.Eyedropper(c,_source,Viewport.ToImage(at),at,(float)ActualWidth,(float)ActualHeight);else if(BrushMode!=0&&!_pan)PointerOverlay.Brush(c,at,BrushScreenRadius,BrushMode==1);}
    }
    public void Dispose(){_image.Dispose();_displayImage?.Dispose();_display?.Dispose();_foreground?.Dispose();}
}

public sealed partial class ObjectSelectionWindow:Window
{
    private readonly SKBitmap _crop;private readonly int _sourceWidth,_sourceHeight;private readonly SKRectI _original;
    private readonly ComboBox _mode=new(),_view=new(),_brush=new();private readonly Slider _tolerance=new(){Minimum=0,Maximum=150,Value=35},_feather=new(){Minimum=0,Maximum=6,Value=1,IsSnapToTickEnabled=true,TickFrequency=1},_size=new(){Minimum=2,Maximum=100,Value=16};
    private readonly CheckBox _shadow=new(){Content="그림자 포함",IsChecked=true},_interior=new(){Content="오브젝트 안쪽 보존",IsChecked=true},_edgeClean=new(){Content="배경색 번짐 제거",IsChecked=true};private readonly TextBlock _info=new(),_radiusText=new();
    private readonly Slider _shrink=new(){Minimum=0,Maximum=3,Value=0,IsSnapToTickEnabled=true,TickFrequency=1};
    public ComboBox TargetBox { get; }=new();
    private readonly StackPanel _objectOptions=new();private readonly StackPanel _textOptions=new();private bool _pickingInk;private string _inkColor="";
    public SeparationTarget SelectedTarget=>(SeparationTarget)Math.Max(0,TargetBox.SelectedIndex);
    public string SelectedInkColor=>_inkColor;
    public bool StrokeProtectionActive=>SelectedTarget==SeparationTarget.Text;
    private sealed record MaskState(MaskBuffer Mask,MaskBuffer Erase,string Foreground,SeparationState Settings,short[] Manual,string Action="분리/보정",string RenderKey="");
    private SKColor _background;private readonly List<MaskState> _undo=[],_redo=[];private MaskState? _strokeBefore;private MaskBuffer _erase;private string _foregroundPng="";private readonly EffectRegion _region;
    public double BrushRadius=>_size.Value;
    public MaskCanvas Canvas { get; }
    public NormalizedRect SelectedBounds { get; private set; }
    public ObjectCutout? SelectedCutout { get; private set; }
    public CheckBox FitBoundsCheck { get; }=new(){Content="분리 결과에 영역 맞춤",ToolTip="끄면 원래 선택 범위를 유지합니다. 켜면 보이는 오브젝트 크기에 맞춰 범위를 줄입니다.",IsChecked=false};
    public SKColor SelectedBackground=>_background;
    public double SelectedTolerance=>_tolerance.Value;
    public ObjectSelectionWindow(SKBitmap source,EffectRegion region)
    {
        Title="정밀 분리 · 보정 · "+ProductInfo.Credit;Width=1120;Height=800;MinWidth=760;MinHeight=580;WindowStartupLocation=WindowStartupLocation.CenterOwner;Background=new SolidColorBrush(Color.FromRgb(23,33,45));Foreground=Brushes.White;
        _sourceWidth=source.Width;_sourceHeight=source.Height;_original=ObjectExtractor.PixelRect(region.Bounds,source.Width,source.Height);using var subset=new SKBitmap();source.ExtractSubset(subset,_original);_crop=subset.Copy();_background=RegionSeparation.Background(source,region);
        _region=region.Copy(false);
        _inkColor=region.TextInkColor;
        SelectedBounds=region.Bounds;SelectedCutout=RegionRaster.ActiveCutout(region);var mask=RegionSeparation.InitialMask(source,region);_tolerance.Value=region.ContentTolerance;_feather.Value=0;_edgeClean.IsChecked=false;
        if(region.SeparationTarget==SeparationTarget.Text&&RegionRaster.ActiveCutout(region) is null){using var text=TextSeparation.Extract(_crop,_background,SKColor.TryParse(_inkColor,out var ink)?ink:null,region.TextSettings);mask=text.Mask;_foregroundPng=ObjectExtractor.EncodeBitmap(text.Foreground);}
        Restrict(mask);_erase=mask.Morph(1,true);
        if(RegionRaster.ActiveCutout(region) is { } existing){mask=MaskBuffer.Decode(existing).Resize(_crop.Width,_crop.Height);_foregroundPng=existing.ForegroundPng;if(existing.ForegroundPng.Length>0&&(existing.Width!=_crop.Width||existing.Height!=_crop.Height)){using var foreground=existing.DecodeForeground()!;using var resized=new SKBitmap(_crop.Width,_crop.Height);using(var c=new SKCanvas(resized)){c.Clear(SKColors.Transparent);using var image=SKImage.FromBitmap(foreground);c.DrawImage(image,SKRect.Create(_crop.Width,_crop.Height));}_foregroundPng=ObjectExtractor.EncodeBitmap(resized);}if(!string.IsNullOrEmpty(existing.ErasePng))_erase=MaskBuffer.Decode(new(){Width=existing.Width,Height=existing.Height,MaskPng=existing.ErasePng}).Resize(_crop.Width,_crop.Height);}
        Canvas=new(_crop,mask){ComparisonBackground=_background};Canvas.SetForeground(_foregroundPng);var root=new DockPanel{Margin=new Thickness(10)};Content=root;
        var actions=new WrapPanel();DockPanel.SetDock(actions,Dock.Bottom);root.Children.Add(actions);
        void Button(Panel host,string text,Action click){var b=new Button{Content=text,Padding=new Thickness(9,6,9,6)};b.Click+=(_,_)=>click();host.Children.Add(b);}
        Button(actions,"되돌리기",Undo);Button(actions,"다시 실행",Redo);Button(actions,"취소",()=>Close());Button(actions,"보정 적용",async()=>{await FlushPreviewAsync();if(PrepareResult())DialogResult=true;});actions.Children.Add(_info);
        var settings=new StackPanel{Margin=new Thickness(5)};var manual=new StackPanel{Margin=new Thickness(5)};var resultView=new StackPanel{Margin=new Thickness(5)};
        var tabs=new TabControl{Width=290};DockPanel.SetDock(tabs,Dock.Right);root.Children.Add(tabs);
        foreach(var pair in new[]{("자동 분리",settings),("브러시 보정",manual),("결과 확인",resultView)})tabs.Items.Add(new TabItem{Header=pair.Item1,Content=new ScrollViewer{Content=pair.Item2,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}});
        void Label(string text)=>settings.Children.Add(new TextBlock{Text=text,Margin=new Thickness(4,9,0,3),TextWrapping=TextWrapping.Wrap});
        settings.Children.Add(new TextBlock{Text="현재 분리 결과에서 이어서 보정합니다.",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.Aquamarine,Margin=new Thickness(4)});
        settings.Children.Add(FitBoundsCheck);
        Label("분리 대상");TargetBox.Items.Add("일반 오브젝트 · 내부 보존");TargetBox.Items.Add("글자 · 획 두께 보존");TargetBox.SelectedIndex=(int)region.SeparationTarget;settings.Children.Add(TargetBox);
        _textOptions.Children.Add(new TextBlock{Text="닫힌 글자 안쪽 배경도 제거합니다. 획 축소·침식·흐림은 적용하지 않습니다. 정확히 배경색인 부분만 지우고 애매한 픽셀은 남깁니다.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(4),Foreground=Brushes.Aquamarine});
        Button(_textOptions,"글자 보호색 스포이드",()=>{_pickingInk=true;Canvas.PickColor=true;Canvas.HolePickMode=false;Canvas.Focus();});Button(_textOptions,"글자 보호색 자동",()=>{_inkColor="";QueuePreview();_info.Text="글자 보호색 자동 · 다시 계산하면 적용됩니다.";});settings.Children.Add(_textOptions);
        Label("자동 분리 다시 계산");foreach(var text in new[]{"자동 후보","PNG 투명도 사용","배경색으로 분리"})_mode.Items.Add(text);_mode.SelectedIndex=2;settings.Children.Add(_mode);
        Button(settings,"영역 배경색 스포이드",()=>{_pickingInk=false;Canvas.HolePickMode=false;Canvas.PickColor=true;Canvas.Focus();});Button(settings,"영역 배경 색상표",()=>{_pickingInk=false;var picker=new ColorPickerWindow(_background){Owner=this};if(picker.ShowDialog()==true){_background=picker.SelectedColor;QueuePreview();}else if(picker.PickSourceRequested){Canvas.PickColor=true;Canvas.Focus();}});
        Button(_objectOptions,"단색 배경 정리 프리셋",ApplySolidCleanup);
        _objectOptions.Children.Add(new TextBlock{Text="배경 허용오차"});_objectOptions.Children.Add(_tolerance);_objectOptions.Children.Add(new TextBlock{Text="오브젝트 윤곽 부드러움"});_objectOptions.Children.Add(_feather);_objectOptions.Children.Add(_shadow);
        _objectOptions.Children.Add(_interior);_interior.ToolTip="일반 오브젝트의 닫힌 내부는 보호합니다. 글자 안쪽을 지우려면 글자 모드를 사용하세요.";
        _objectOptions.Children.Add(_edgeClean);_objectOptions.Children.Add(new TextBlock{Text="오브젝트 윤곽 축소 (0~3px)"});_objectOptions.Children.Add(_shrink);Button(_objectOptions,"오브젝트 윤곽 1px 줄이기",ContractOnePixel);settings.Children.Add(_objectOptions);
        Button(settings,"분리 다시 계산",()=>Generate());Label("옵션을 바꾼 뒤 다시 계산하세요.\n사진 배경은 포함/제외 브러시로 보정할 수 있습니다.");
        resultView.Children.Add(new TextBlock{Text="보기 방식",Margin=new Thickness(4)});foreach(var text in new[]{"원본 + 윤곽 표시","마스크","분리 결과","원래 배경에 재합성","원본과 차이 · 검정=일치"})_view.Items.Add(text);_view.SelectedIndex=0;resultView.Children.Add(_view);
        manual.Children.Add(new TextBlock{Text="브러시 도구"});foreach(var text in new[]{"보기 / 이동","포함 (+)","제외 (−)"})_brush.Items.Add(text);_brush.SelectedIndex=0;manual.Children.Add(_brush);manual.Children.Add(new TextBlock{Text="브러시 반경 · [ / ] · Shift=10px"});_size.Minimum=1;_size.Maximum=200;manual.Children.Add(_size);manual.Children.Add(_radiusText);_radiusText.Text=$"{_size.Value:0}px";
        Button(manual,"안쪽 배경 찍기 · 제거 후보 표시",()=>{Canvas.PickColor=false;Canvas.BrushMode=0;_brush.SelectedIndex=0;Canvas.HolePickMode=true;Canvas.Focus();_info.Text="글자 구멍의 배경을 클릭하세요. 빨간 후보를 확인한 뒤 제거합니다.";});Button(manual,"표시된 배경 후보 제거",ApplyHoleRemoval);Button(manual,"후보 취소",()=>{Canvas.PendingRemoval=null;Canvas.HolePickMode=false;Canvas.InvalidateVisual();});
        Label("휠: 확대 · Space/중간버튼: 이동\n원본 이미지는 수정하지 않습니다.");var viewTools=new WrapPanel();DockPanel.SetDock(viewTools,Dock.Top);root.Children.Add(viewTools);void Zoom(double amount){Canvas.Viewport.ZoomAt(amount,new((float)Canvas.ActualWidth/2,(float)Canvas.ActualHeight/2));Canvas.InvalidateVisual();}Button(viewTools,"−",()=>Zoom(.8));Button(viewTools,"＋",()=>Zoom(1.25));Button(viewTools,"100%",()=>{Canvas.Viewport.Native();Canvas.InvalidateVisual();});Button(viewTools,"화면 맞춤",Canvas.Fit);root.Children.Add(Canvas);
        Canvas.ColorPicked+=color=>{if(_pickingAdditional)return;if(_pickingInk){_inkColor=ColorPickerWindow.Hex(color);_pickingInk=false;_info.Text="글자 보호색 "+_inkColor+" · 다시 계산하면 적용됩니다.";}else{_background=color;Canvas.ComparisonBackground=color;Canvas.Changed();_mode.SelectedIndex=2;_info.Text="영역 배경색 "+ColorPickerWindow.Hex(color)+" · 다시 계산하면 적용됩니다.";}};
        Canvas.HolePicked+=(x,y)=>{Canvas.PendingRemoval=TextSeparation.HoleCandidate(_crop,Canvas.Buffer,x,y,_background);Canvas.InvalidateVisual();_info.Text=Canvas.PendingRemoval.Alpha.Any(a=>a>0)?"빨간 후보는 배경색 픽셀입니다. 후보 제거를 눌러 적용하세요.":"배경색과 정확히 같은 지점을 선택하세요. 글자 획은 선택하지 않습니다.";};
        void TargetChanged(){var text=StrokeProtectionActive;_textOptions.Visibility=text?Visibility.Visible:Visibility.Collapsed;_objectOptions.Visibility=text?Visibility.Collapsed:Visibility.Visible;_mode.Visibility=text?Visibility.Collapsed:Visibility.Visible;FitBoundsCheck.IsEnabled=!text;if(text){FitBoundsCheck.IsChecked=false;_shrink.Value=0;_feather.Value=0;_edgeClean.IsChecked=false;}_info.Text="대상 변경은 다시 계산 후 적용됩니다. 수동 보정은 유지합니다.";}
        TargetBox.SelectionChanged+=(_,_)=>TargetChanged();TargetChanged();
        _brush.SelectionChanged+=(_,_)=>{Canvas.HolePickMode=false;Canvas.PendingRemoval=null;Canvas.BrushMode=_brush.SelectedIndex;};_view.SelectionChanged+=(_,_)=>{Canvas.ViewMode=_view.SelectedIndex;Canvas.Changed();};_size.ValueChanged+=(_,_)=>{Canvas.BrushSize=(float)_size.Value;_radiusText.Text=$"{_size.Value:0}px";};
        Canvas.StrokeStarted+=()=>_strokeBefore=Snapshot();Canvas.StrokeEnded+=()=>{if(_strokeBefore is not null){Record(_strokeBefore);_strokeBefore=null;}};
        PreviewKeyDown+=(_,e)=>{if(MainWindow.IsTextEditing(Keyboard.FocusedElement as DependencyObject))return;if(HandleBrushKey(e.Key,Keyboard.Modifiers)){e.Handled=true;return;}if(Keyboard.Modifiers==ModifierKeys.Control&&e.Key==Key.Z){Undo();e.Handled=true;}else if(Keyboard.Modifiers==(ModifierKeys.Control|ModifierKeys.Shift)&&e.Key==Key.Z){Redo();e.Handled=true;}};
        InitializeLivePreview(settings,manual,actions,region);
        Closed+=(_,_)=>{CloseLivePreview();Canvas.Dispose();_crop.Dispose();};
    }
    private void Restrict(MaskBuffer mask){using var path=RegionMask.Create(_region.Shape,SKRect.Create(mask.Width,mask.Height),_region.Points);for(var y=0;y<mask.Height;y++)for(var x=0;x<mask.Width;x++)if(!path.Contains(x+.5f,y+.5f))mask.Alpha[y*mask.Width+x]=0;}
    private MaskState Snapshot()=>new(Canvas.Buffer.Copy(),_erase.Copy(),_foregroundPng,CaptureSettings(),(short[])_manualPatch.Clone(),RenderKey:_maskSettingsKey);
    private void Restore(MaskState state){CancelLivePreview();Canvas.PendingRemoval=null;Canvas.HolePickMode=false;Canvas.PickColor=false;Canvas.Buffer=state.Mask.Copy();_erase=state.Erase.Copy();_foregroundPng=state.Foreground;_manualPatch=(short[])state.Manual.Clone();RestoreSettings(state.Settings);_maskSettingsKey=state.RenderKey;Canvas.SetForeground(_foregroundPng);_accepted=Snapshot();UpdateUndoButtons();}
    private void Record(MaskState before,string action="분리/보정"){_undo.Add(before with{Action=action});_redo.Clear();while(_undo.Count>2&&(_undo.Count>20||_undo.Sum(m=>(long)m.Mask.Alpha.Length+m.Erase.Alpha.Length+m.Foreground.Length*2+m.Manual.Length*2)>64*1024*1024))_undo.RemoveAt(0);UpdateUndoButtons();}
    public bool HandleBrushKey(Key key,ModifierKeys modifiers)
    {if(modifiers is not ModifierKeys.None and not ModifierKeys.Shift||key is not Key.OemOpenBrackets and not Key.OemCloseBrackets)return false;_size.Value=Math.Clamp(_size.Value+(key==Key.OemCloseBrackets?1:-1)*(modifiers==ModifierKeys.Shift?10:1),_size.Minimum,_size.Maximum);return true;}
    public void ApplySolidCleanup(){if(StrokeProtectionActive){Generate();_view.SelectedIndex=2;return;}_mode.SelectedIndex=2;_tolerance.Value=35;_feather.Value=0;_shadow.IsChecked=false;_interior.IsChecked=true;_edgeClean.IsChecked=true;_shrink.Value=1;Generate();_view.SelectedIndex=2;_info.Text="일반 오브젝트 프리셋 · 내부 보존/1px축소/색번짐 정리";}
    public void ContractOnePixel(){if(StrokeProtectionActive){_info.Text="글자 모드는 획 보호를 위해 윤곽을 줄이지 않습니다.";return;}var before=Snapshot();Canvas.Buffer=Canvas.Buffer.Morph(1,false);Record(before);if(_edgeClean.IsChecked==true){var clean=ObjectExtractor.CleanEdges(_crop,Canvas.Buffer,_background);Canvas.Buffer=clean.Mask;_foregroundPng=clean.Foreground;}Canvas.SetForeground(_foregroundPng);_info.Text="오브젝트 테두리1px 축소 · 되돌리기 가능";}
    public void ApplyHoleRemoval(){if(Canvas.PendingRemoval is not { } candidate||!candidate.Alpha.Any(a=>a>0))return;var before=Snapshot();Record(before);TextSeparation.ApplyCandidate(Canvas.Buffer,candidate);CaptureManual(before.Mask);_accepted=Snapshot();Canvas.PendingRemoval=null;Canvas.HolePickMode=false;Canvas.Changed();_info.Text="배경 후보만 제거했습니다. 글자 획은 유지합니다.";}
    public void Generate()
    {try{
        MaskBuffer next;var foreground="";
        CancelLivePreview();if(StrokeProtectionActive){using var text=TextSeparation.Extract(_crop,_background,SKColor.TryParse(_inkColor,out var ink)?ink:null,SelectedTextSettings);next=text.Mask;foreground=ObjectExtractor.EncodeBitmap(text.Foreground);}
        else next=ObjectExtractor.Extract(_crop,new(){Mode=(ExtractionMode)_mode.SelectedIndex,Background=_background,Tolerance=_tolerance.Value,Feather=(int)_feather.Value,IncludeShadow=_shadow.IsChecked==true,PreserveInterior=_interior.IsChecked==true});
        Restrict(next);var erase=next.Morph(1,true);if(!StrokeProtectionActive){next=next.Morph((int)_shrink.Value,false);if(_edgeClean.IsChecked==true){var clean=ObjectExtractor.CleanEdges(_crop,next,_background);next=clean.Mask;foreground=clean.Foreground;}}
        ApplyManual(next);Record(_accepted??Snapshot());Canvas.Buffer=next;_erase=erase;_foregroundPng=foreground;Canvas.PendingRemoval=null;Canvas.ComparisonBackground=_background;Canvas.SetForeground(_foregroundPng);_maskSettingsKey=SettingsKey();_accepted=Snapshot();_info.Text=StrokeProtectionActive?"글자 분리 완료 · 획 축소0px · 원래 배경 재합성으로 확인하세요.":"오브젝트 분리 미리보기 갱신";
    }catch(Exception ex){_info.Text=ex.Message;}}
    public void Undo(){if(_pendingBefore is { } pending){_redo.Add(Snapshot() with{Action=_pendingAction});Restore(pending);return;}CancelLivePreview();if(_undo.Count==0){_info.Text="되돌릴 변경이 없습니다.";return;}var old=_undo[^1];_redo.Add(Snapshot() with{Action=old.Action});_undo.RemoveAt(_undo.Count-1);Restore(old);_info.Text=old.Action+" · 되돌렸습니다.";}
    public void Redo(){CancelLivePreview();if(_redo.Count==0){_info.Text="다시 실행할 변경이 없습니다.";return;}var next=_redo[^1];_undo.Add(Snapshot() with{Action=next.Action});_redo.RemoveAt(_redo.Count-1);Restore(next);_info.Text=next.Action+" · 다시 실행했습니다.";}
    public bool PrepareResult(){try{var grown=Canvas.Buffer.Morph(1,true);var erase=_erase.Copy();for(var i=0;i<erase.Alpha.Length;i++)erase.Alpha[i]=Math.Max(erase.Alpha[i],grown.Alpha[i]);if(FitBoundsCheck.IsChecked==true){var result=ObjectExtractor.Fit(Canvas.Buffer,_original,_sourceWidth,_sourceHeight,_foregroundPng,erase);SelectedBounds=result.Bounds;SelectedCutout=result.Cutout with{ManualPng=EncodeManual((int)Math.Round(result.Bounds.X*_sourceWidth)-_original.Left,(int)Math.Round(result.Bounds.Y*_sourceHeight)-_original.Top,result.Cutout.Width,result.Cutout.Height)};}else{SelectedBounds=_region.Bounds;SelectedCutout=Canvas.Buffer.Encode() with{ForegroundPng=_foregroundPng,ErasePng=erase.Encode().MaskPng,ManualPng=EncodeManual(0,0,_crop.Width,_crop.Height)};}return true;}catch(Exception ex){_info.Text=ex.Message;return false;}}
}
