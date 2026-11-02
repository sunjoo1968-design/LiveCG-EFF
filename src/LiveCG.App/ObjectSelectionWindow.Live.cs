using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using LiveCG.Core;
using SkiaSharp;
namespace LiveCG.App;
public sealed partial class ObjectSelectionWindow
{
    private sealed record SeparationState(int Target,int Mode,double Tolerance,double Feather,double Shrink,bool Shadow,bool Interior,bool Clean,SKColor Background,string Ink,TextSeparationSettings Text,bool Fit);
    private readonly CheckBox _live=new(){Content="실시간 미리보기",IsChecked=true};
    public ComboBox TextMethod { get; }=new();public Slider RangeTolerance { get; }=new(){Minimum=0,Maximum=150};public Slider InkTolerance { get; }=new(){Minimum=0,Maximum=150};
    private readonly ListBox _backgroundList=new(){Height=65};private List<string> _backgroundColors=[];private bool _pickingAdditional;
    private readonly DispatcherTimer _previewDelay=new(){Interval=TimeSpan.FromMilliseconds(150)};
    private bool _liveSync,_liveClosed,_dragOptions,_computing;private int _generation;private MaskState? _pendingBefore,_accepted;private short[] _manualPatch=[];private Task? _calculation;private Button? _undoButton,_redoButton;private string _pendingAction="분리 설정";
    public TextSeparationSettings SelectedTextSettings=>new(){RangeMode=TextMethod.SelectedIndex==1,BackgroundColors=[.._backgroundColors],ColorTolerance=RangeTolerance.Value,InkTolerance=InkTolerance.Value};
    public bool PreviewPending=>_pendingBefore is not null||_computing;
    private string _maskSettingsKey="";
    private string SettingsKey(){var s=CaptureSettings();return FormattableString.Invariant($"{s.Target}|{s.Mode}|{s.Tolerance}|{s.Feather}|{s.Shrink}|{s.Shadow}|{s.Interior}|{s.Clean}|{s.Background}|{s.Ink}|{s.Text.RangeMode}|{s.Text.ColorTolerance}|{s.Text.InkTolerance}|{s.Fit}|")+string.Join(';',s.Text.BackgroundColors);}
    private SeparationState CaptureSettings()=>new(TargetBox.SelectedIndex,_mode.SelectedIndex,_tolerance.Value,_feather.Value,_shrink.Value,_shadow.IsChecked==true,_interior.IsChecked==true,_edgeClean.IsChecked==true,_background,_inkColor,SelectedTextSettings,FitBoundsCheck.IsChecked==true);
    private void RestoreSettings(SeparationState state)
    {
        _liveSync=true;TargetBox.SelectedIndex=state.Target;_mode.SelectedIndex=state.Mode;_tolerance.Value=state.Tolerance;_feather.Value=state.Feather;_shrink.Value=state.Shrink;_shadow.IsChecked=state.Shadow;_interior.IsChecked=state.Interior;_edgeClean.IsChecked=state.Clean;_background=state.Background;_inkColor=state.Ink;TextMethod.SelectedIndex=state.Text.RangeMode?1:0;RangeTolerance.Value=state.Text.ColorTolerance;InkTolerance.Value=state.Text.InkTolerance;_backgroundColors=[..state.Text.BackgroundColors];ShowBackgrounds();FitBoundsCheck.IsChecked=state.Fit;Canvas.ComparisonBackground=_background;_liveSync=false;
    }
    private void ShowBackgrounds(){_backgroundList.ItemsSource=null;_backgroundList.ItemsSource=_backgroundColors;}
    private void InitializeLivePreview(StackPanel settings,StackPanel manual,Panel actions,EffectRegion region)
    {
        _liveSync=true;_manualPatch=Enumerable.Repeat((short)-1,_crop.Width*_crop.Height).ToArray();settings.Children.Insert(0,_live);
        if(RegionRaster.ActiveCutout(region) is { } saved)
        {
            if(saved.ManualPng.Length>0){using var manualOriginal=SKBitmap.Decode(Convert.FromBase64String(saved.ManualPng));using var manualImage=new SKBitmap(_crop.Width,_crop.Height);using(var c=new SKCanvas(manualImage)){c.Clear(SKColors.Transparent);using var image=SKImage.FromBitmap(manualOriginal);c.DrawImage(image,SKRect.Create(_crop.Width,_crop.Height));}for(var y=0;y<_crop.Height;y++)for(var x=0;x<_crop.Width;x++){var c=manualImage.GetPixel(x,y);if(c.Alpha>0)_manualPatch[y*_crop.Width+x]=c.Red;}}
            else
            {
                MaskBuffer baseline;if(region.SeparationTarget==SeparationTarget.Text){using var text=TextSeparation.Extract(_crop,_background,SKColor.TryParse(_inkColor,out var ink)?ink:null,region.TextSettings);baseline=text.Mask;}else baseline=ObjectExtractor.Extract(_crop,RegionSeparation.Options(region,_background));
                Restrict(baseline);for(var i=0;i<_manualPatch.Length;i++)if(baseline.Alpha[i]!=Canvas.Buffer.Alpha[i])_manualPatch[i]=Canvas.Buffer.Alpha[i];
            }
        }
        TextMethod.Items.Add("정확한 단색 · 획 보존");TextMethod.Items.Add("색상 범위 · 무늬 배경");TextMethod.SelectedIndex=region.TextSettings.RangeMode?1:0;RangeTolerance.Value=region.TextSettings.ColorTolerance;InkTolerance.Value=region.TextSettings.InkTolerance;_backgroundColors=[..region.TextSettings.BackgroundColors];ShowBackgrounds();
        _textOptions.Children.Add(new TextBlock{Text="분리 방식"});_textOptions.Children.Add(TextMethod);_textOptions.Children.Add(new TextBlock{Text="배경 색상 범위 · 획 축소 없음"});_textOptions.Children.Add(RangeTolerance);_textOptions.Children.Add(new TextBlock{Text="남길 글자색 범위"});_textOptions.Children.Add(InkTolerance);_textOptions.Children.Add(_backgroundList);
        void Button(Panel host,string title,Action action){var b=new Button{Content=title};b.Click+=(_,_)=>action();host.Children.Add(b);}
        var numericPreset=new Button{Content="숫자·가격 우선 · 획 보존"};numericPreset.Click+=(_,_)=>ApplyNumericPreset();_textOptions.Children.Insert(0,numericPreset);
        _textOptions.Children.Insert(1,new TextBlock{Text="가격·할인율 권장: 보호색을 자동 추정하고 옅은 획도 남깁니다. 여러 바탕색은 배경색 추가로 지정하세요.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(3,5,3,5)});
        Button(_textOptions,"배경색 추가 · 스포이드",()=>{_pickingAdditional=true;_pickingInk=false;Canvas.PickColor=true;Canvas.HolePickMode=false;});Button(_textOptions,"선택 배경색 삭제",()=>{if(_backgroundList.SelectedItem is string color){_backgroundColors.Remove(color);ShowBackgrounds();QueuePreview();}});
        Button(manual,"수동 보정만 초기화",()=>{Record(Snapshot());Array.Fill(_manualPatch,(short)-1);_accepted=Snapshot();QueuePreview();});
        _undoButton=actions.Children.OfType<Button>().FirstOrDefault(b=>Equals(b.Content,"되돌리기"));_redoButton=actions.Children.OfType<Button>().FirstOrDefault(b=>Equals(b.Content,"다시 실행"));
        foreach(var slider in new[]{_tolerance,_feather,_shrink,RangeTolerance,InkTolerance})
        {
            slider.ValueChanged+=(_,_)=>QueuePreview(slider==_tolerance?"배경 허용오차":slider==RangeTolerance?"배경 색상 범위":slider==InkTolerance?"글자 보호 범위":slider==_feather?"오브젝트 부드러움":"오브젝트 윤곽");slider.AddHandler(Thumb.DragStartedEvent,new DragStartedEventHandler((_,_)=>{if(_liveSync)return;_dragOptions=true;_pendingBefore??=_accepted??Snapshot();}));slider.AddHandler(Thumb.DragCompletedEvent,new DragCompletedEventHandler((_,_)=>{_dragOptions=false;_previewDelay.Stop();_ = StartPreviewAsync();}));
        }
        foreach(var combo in new[]{TargetBox,_mode,TextMethod})combo.SelectionChanged+=(_,_)=>QueuePreview();foreach(var flag in new[]{_shadow,_interior,_edgeClean,FitBoundsCheck}){flag.Checked+=(_,_)=>QueuePreview();flag.Unchecked+=(_,_)=>QueuePreview();}
        _live.Checked+=(_,_)=>QueuePreview();_previewDelay.Tick+=async(_,_)=>{_previewDelay.Stop();await StartPreviewAsync();};
        Canvas.ColorPicked+=color=>{if(_pickingAdditional){_pickingAdditional=false;if(_backgroundColors.Count<8){_backgroundColors.Add(ColorPickerWindow.Hex(color));ShowBackgrounds();TextMethod.SelectedIndex=1;}}QueuePreview();};
        Canvas.StrokeStarted+=()=>{_generation++;_previewDelay.Stop();};
        Canvas.StrokeEnded+=()=>{if(_undo.Count>0)CaptureManual(_undo[^1].Mask);_accepted=Snapshot();if(_pendingBefore is not null)QueuePreview();};
        PreviewKeyDown+=(_,e)=>{if(Keyboard.Modifiers==ModifierKeys.Control&&e.Key==Key.Y&&!MainWindow.IsTextEditing(Keyboard.FocusedElement as DependencyObject)){Redo();e.Handled=true;}};
        void RangeControls(){RangeTolerance.IsEnabled=InkTolerance.IsEnabled=_backgroundList.IsEnabled=TextMethod.SelectedIndex==1;}TextMethod.SelectionChanged+=(_,_)=>RangeControls();RangeControls();_view.SelectedIndex=2;Canvas.ViewMode=2;Canvas.Changed();_liveSync=false;_maskSettingsKey=SettingsKey();_accepted=Snapshot();UpdateUndoButtons();
    }
    public void ApplyNumericPreset()
    {
        _liveSync=true;TargetBox.SelectedIndex=1;TextMethod.SelectedIndex=1;_inkColor="";RangeTolerance.Value=24;InkTolerance.Value=36;_liveSync=false;QueuePreview("숫자·가격 우선 · 획 보존");
    }
    private void CaptureManual(MaskBuffer before){for(var i=0;i<_manualPatch.Length;i++)if(Canvas.Buffer.Alpha[i]!=before.Alpha[i])_manualPatch[i]=Canvas.Buffer.Alpha[i];}
    private void ApplyManual(MaskBuffer next){for(var i=0;i<Math.Min(_manualPatch.Length,next.Alpha.Length);i++)if(_manualPatch[i]>=0)next.Alpha[i]=(byte)_manualPatch[i];}
    private string EncodeManual(int left,int top,int width,int height)
    {
        using var image=new SKBitmap(width,height);image.Erase(SKColors.Transparent);for(var y=0;y<height;y++)for(var x=0;x<width;x++){var px=x+left;var py=y+top;if(px<0||py<0||px>=_crop.Width||py>=_crop.Height)continue;var value=_manualPatch[py*_crop.Width+px];if(value>=0)image.SetPixel(x,y,new SKColor((byte)value,(byte)value,(byte)value,255));}return ObjectExtractor.EncodeBitmap(image);
    }
    private void UpdateUndoButtons(){if(_undoButton is not null){_undoButton.IsEnabled=_undo.Count>0||_pendingBefore is not null;_undoButton.Content=_pendingBefore is not null?"되돌리기 · "+_pendingAction:_undo.Count>0?"되돌리기 · "+_undo[^1].Action:"되돌리기";}if(_redoButton is not null){_redoButton.IsEnabled=_redo.Count>0;_redoButton.Content=_redo.Count>0?"다시 실행 · "+_redo[^1].Action:"다시 실행";}}
    public void QueuePreview(string action="분리 설정")
    {
        if(_liveSync||_liveClosed||_manualPatch.Length==0)return;_pendingBefore??=_accepted??Snapshot();_pendingAction=action;_redo.Clear();UpdateUndoButtons();_generation++;if(_live.IsChecked!=true){_info.Text="미리보기 대기 · 분리 다시 계산을 누르세요.";return;}_previewDelay.Stop();_previewDelay.Start();
    }
    public async Task FlushPreviewAsync(){_previewDelay.Stop();while(_computing&&!_liveClosed)await Task.Delay(20);if(_pendingBefore is null&&_maskSettingsKey!=SettingsKey())_pendingBefore=_accepted??Snapshot();if(_pendingBefore is not null&&!_liveClosed)await StartPreviewAsync();}
    private async Task StartPreviewAsync()
    {
        if(_computing||_liveClosed||_pendingBefore is null)return;_computing=true;var generation=_generation;var settings=CaptureSettings();using var crop=_crop.Copy();var patch=(short[])_manualPatch.Clone();_info.Text="미리보기 계산 중…";
        _calculation=Task.Run(()=>
        {
            MaskBuffer mask;var fg="";if(settings.Target==1){using var text=TextSeparation.Extract(crop,settings.Background,SKColor.TryParse(settings.Ink,out var ink)?ink:null,settings.Text);mask=text.Mask;fg=ObjectExtractor.EncodeBitmap(text.Foreground);}else{mask=ObjectExtractor.Extract(crop,new(){Mode=(ExtractionMode)settings.Mode,Background=settings.Background,Tolerance=settings.Tolerance,Feather=(int)settings.Feather,IncludeShadow=settings.Shadow,PreserveInterior=settings.Interior});mask=mask.Morph((int)settings.Shrink,false);if(settings.Clean){var clean=ObjectExtractor.CleanEdges(crop,mask,settings.Background);mask=clean.Mask;fg=clean.Foreground;}}
            Restrict(mask);for(var i=0;i<patch.Length;i++)if(patch[i]>=0)mask.Alpha[i]=(byte)patch[i];return(mask,fg);
        });
        try
        {
            var result=await (Task<(MaskBuffer mask,string fg)>)_calculation;if(_liveClosed||generation!=_generation)return;
            Canvas.Buffer=result.mask;_erase=result.mask.Morph(1,true);_foregroundPng=result.fg;Canvas.ComparisonBackground=settings.Background;Canvas.SetForeground(result.fg);
            _maskSettingsKey=SettingsKey();
            if(!_dragOptions){Record(_pendingBefore,_pendingAction);_pendingBefore=null;_accepted=Snapshot();UpdateUndoButtons();}_info.Text="실시간 반영 · 수동 보정 유지 · 글자 모드는 획 축소 없음";
        }catch(Exception ex){if(!_liveClosed)_info.Text=ex.Message;}
        finally{_computing=false;_calculation=null;if(!_liveClosed&&generation!=_generation){_previewDelay.Stop();_previewDelay.Start();}}
    }
    private void CancelLivePreview(){_generation++;_previewDelay.Stop();_pendingBefore=null;}
    private void CloseLivePreview(){_liveClosed=true;CancelLivePreview();}
}
