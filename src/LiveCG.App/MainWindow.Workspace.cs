using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LiveCG.Core;
using Microsoft.Win32;
using SkiaSharp;

namespace LiveCG.App;

public partial class MainWindow
{
    private Window? _previewWindow,_effectsWindow;
    private WorkspaceState _workspace=new();private bool _focusMode,_shuttingDown,_workspaceReady;
    private double _timelineHeight=245;
    private IReadOnlyList<EffectAsset> _assets=[];
    private readonly List<EffectStudioWindow> _studios=[];
    public bool IsEditorFocused=>_focusMode;
    public Window? DetachedPreviewWindow=>_previewWindow;
    public Window? DetachedEffectsWindow=>_effectsWindow;
    private void InitializeWorkspace()
    {
        Preview.ColorPicked+=color=>{if(_pickingEffectColor){_pickingEffectColor=false;SetEffectColor(color);}else SetBackgroundColor(color);};AddFileMenu();RefreshAssets();
        Loaded+=(_,_)=>
        {
            if(Environment.GetEnvironmentVariable("LIVECG_DISABLE_WORKSPACE_RESTORE")=="1"){_workspaceReady=true;return;}
            _workspace=WorkspaceState.Load();_workspaceReady=true;
            SetLayerPanelSize(_workspace.LayerPanelHeight,_workspace.RightWidth);
            _workspace.Main?.Apply(this);EditorGrid.ColumnDefinitions[0].Width=new GridLength(Math.Clamp(_workspace.LeftWidth,240,450));
            _timelineHeight=Math.Clamp(_workspace.TimelineHeight,170,700);AutoFocusCheck.IsChecked=_workspace.AutoFocus;OutputExpander.IsExpanded=false;
            SetFocusMode(_workspace.FocusMode);
            if(_workspace.PreviewDetached)DetachPreview(this,new RoutedEventArgs());if(_workspace.EffectsDetached)DetachEffects(this,new RoutedEventArgs());
        };
    }
    private void ToggleFocusMode(object sender,RoutedEventArgs e)=>SetFocusMode(!_focusMode);
    private void SetFocusMode(bool focus)
    {
        if(!_focusMode&&EditorGrid.RowDefinitions[2].Height.IsAbsolute&&EditorGrid.RowDefinitions[2].Height.Value>0)_timelineHeight=EditorGrid.RowDefinitions[2].Height.Value;
        _focusMode=focus;TimelinePanel.Visibility=focus?Visibility.Collapsed:Visibility.Visible;TimelineSplitter.Visibility=focus?Visibility.Collapsed:Visibility.Visible;
        EditorGrid.RowDefinitions[2].MinHeight=focus?0:170;EditorGrid.RowDefinitions[2].Height=focus?new GridLength(0):new GridLength(_previewWindow is null?_timelineHeight:1,_previewWindow is null?GridUnitType.Pixel:GridUnitType.Star);
        EditorGrid.RowDefinitions[1].Height=new GridLength(focus?0:7);FocusModeButton.Content=focus?"편집 완료 / 타임라인":"편집 크게";
        if(_previewWindow is not null)_previewWindow.Activate();
    }
    private void AutoFocusChanged(object sender,RoutedEventArgs e){if(_workspaceReady)_workspace.AutoFocus=AutoFocusCheck.IsChecked==true;}
    private Window FloatPanel(Grid host,FrameworkElement panel,string title,WindowPlacement? placement,Action<WindowPlacement> restored)
    {
        host.Children.Remove(panel);var dock=new DockPanel();var back=new Button{Content="메인 화면으로 돌리기",HorizontalAlignment=HorizontalAlignment.Right};DockPanel.SetDock(back,Dock.Top);dock.Children.Add(back);dock.Children.Add(panel);
        var window=new Window{Title=title+" · "+ProductInfo.Credit,Content=dock,Width=title.StartsWith("효과")?850:1100,Height=820,MinWidth=480,MinHeight=420,ResizeMode=ResizeMode.NoResize,Owner=this,Background=Background,Foreground=Foreground,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        back.Click+=(_,_)=>window.Close();window.PreviewKeyDown+=EditorKeyDown;
        WindowPlacement? beforeClose=null;window.Closing+=(_,_)=>beforeClose=WindowPlacement.Capture(window);
        window.Closed+=(_,_)=>{dock.Children.Remove(panel);host.Children.Add(panel);restored(beforeClose??WindowPlacement.Capture(window));};
        if(Environment.GetEnvironmentVariable("LIVECG_DISABLE_WORKSPACE_RESTORE")=="1"){window.WindowStartupLocation=WindowStartupLocation.Manual;window.Left=-16000;window.Top=-16000;window.ShowInTaskbar=false;window.ShowActivated=false;}
        else if(placement is not null){window.WindowStartupLocation=WindowStartupLocation.Manual;placement.Apply(window);}window.Show();return window;
    }
    private void DetachPreview(object sender,RoutedEventArgs e)
    {
        if(_busy)return;if(_previewWindow is not null){_previewWindow.Activate();return;}
        _previewWindow=FloatPanel(PreviewHost,PreviewPanel,"원본 편집",_workspace.Preview,saved=>
        {_workspace.Preview=saved;_previewWindow=null;EditorGrid.RowDefinitions[0].Height=new GridLength(1,GridUnitType.Star);SetFocusMode(_focusMode);});
        EditorGrid.RowDefinitions[0].Height=new GridLength(0);SetFocusMode(_focusMode);
    }
    private void DetachEffects(object sender,RoutedEventArgs e)
    {
        if(_busy)return;if(_effectsWindow is not null){_effectsWindow.Activate();return;}
        _effectsWindow=FloatPanel(EffectsHost,EffectsPanel,"효과 보관함 / 클립",_workspace.Effects,saved=>
        {_workspace.Effects=saved;_effectsWindow=null;foreach(var thumb in _thumbnails)thumb.Height=72;EditorGrid.ColumnDefinitions[4].MinWidth=300;EffectsWidthSplitter.Visibility=Visibility.Visible;EditorGrid.ColumnDefinitions[3].Width=new GridLength(10);ApplyPanelSizes();});
        foreach(var thumb in _thumbnails)thumb.Height=128;
        _effectsWindow.MinHeight=580;EditorGrid.ColumnDefinitions[4].MinWidth=0;EditorGrid.ColumnDefinitions[4].Width=new GridLength(0);EditorGrid.ColumnDefinitions[3].Width=new GridLength(0);EffectsWidthSplitter.Visibility=Visibility.Collapsed;
    }
    private void ResetWorkspace(object sender,RoutedEventArgs e)
    {
        if(_busy)return;AreaDetailsPopup.IsOpen=false;_previewWindow?.Close();_effectsWindow?.Close();SetLayerPanelSize(240,335);_timelineHeight=245;SetFocusMode(false);AutoFocusCheck.IsChecked=true;
        EditorGrid.ColumnDefinitions[0].Width=new GridLength(280);EditorGrid.ColumnDefinitions[4].Width=new GridLength(335);OutputExpander.IsExpanded=false;Preview.Fit(false);_workspace=new();SaveWorkspace();
    }
    private void SaveWorkspace()
    {
        if(!_workspaceReady)return;
        _workspace.Main=WindowPlacement.Capture(this);_workspace.PreviewDetached=_previewWindow is not null;_workspace.EffectsDetached=_effectsWindow is not null;
        _workspace.RightWidth=_rightPanelWidth;_workspace.LayerPanelHeight=_layerPanelHeight;
        if(_previewWindow is not null)_workspace.Preview=WindowPlacement.Capture(_previewWindow);if(_effectsWindow is not null)_workspace.Effects=WindowPlacement.Capture(_effectsWindow);
        _workspace.FocusMode=_focusMode;_workspace.AutoFocus=AutoFocusCheck.IsChecked==true;_workspace.OutputExpanded=OutputExpander.IsExpanded;_workspace.LeftWidth=EditorGrid.ColumnDefinitions[0].ActualWidth;_workspace.TimelineHeight=_timelineHeight;
        try{_workspace.Save();}catch(IOException ex){Notify("화면 배치 저장: "+ex.Message);}
    }
    private bool ConfirmStudiosClose()=>_studios.All(studio=>studio.ConfirmClose());
    private void CloseWorkspace(){if(_shuttingDown)return;SaveWorkspace();_shuttingDown=true;foreach(var studio in _studios.ToArray())studio.Close();AreaDetailsPopup.IsOpen=false;_previewWindow?.Close();_effectsWindow?.Close();}
    private void SetWorkspaceBusy(bool busy){PreviewPanel.IsEnabled=!busy;EffectsPanel.IsEnabled=!busy;if(_previewWindow is not null)_previewWindow.IsEnabled=!busy;if(_effectsWindow is not null)_effectsWindow.IsEnabled=!busy;}
    private void AddFileMenu()
    {
        var menu=new ContextMenu();ImageJob? clicked=null;
        FileList.PreviewMouseRightButtonDown+=(_,e)=>
        {var element=e.OriginalSource as DependencyObject;while(element is not null&&element is not ListBoxItem)element=element is Visual?VisualTreeHelper.GetParent(element):null;clicked=(element as ListBoxItem)?.DataContext as ImageJob;};
        void Add(string name,Func<List<ImageJob>> targets){var item=new MenuItem{Header=name};item.Click+=async(_,_)=>await ExportAsync(targets());menu.Items.Add(item);}
        Add("이 파일 출력",()=>clicked is null?[]:[clicked]);Add("선택한 파일 출력",SelectedJobs);Add("전체 파일 출력",()=>_jobs.ToList());
        menu.Items.Add(new Separator());var open=new MenuItem{Header="출력 폴더 열기"};open.Click+=OpenOutputFolder;menu.Items.Add(open);
        menu.Opened+=(_,_)=>{menu.Items.Cast<object>().OfType<MenuItem>().First().IsEnabled=clicked is not null&&!_busy;foreach(var item in menu.Items.Cast<object>().OfType<MenuItem>().Skip(1))item.IsEnabled=!_busy;var selected=(MenuItem)menu.Items[1];selected.Header=$"선택한 {SelectedJobs().Count}개 파일 출력";selected.IsEnabled=!_busy&&SelectedJobs().Count>0;};FileList.ContextMenu=menu;
        void SourceCommand(string name, Action action){var item=new MenuItem{Header=name};item.Click+=(_,_)=>action();menu.Items.Add(item);}
        menu.Items.Add(new Separator());
        SourceCommand("원본 다시 연결",()=>ChangeSource(clicked,false));
        SourceCommand("누락 원본 폴더 지정",RelinkFolder);
        SourceCommand("원본 이미지 교체",()=>ChangeSource(clicked,true));
    }
    private void OpenOutputFolder(object sender,RoutedEventArgs e)
    {
        try
        {
            var folder = OutputFolderBox.Text.Trim();
            if (_video.Output != OutputKind.Webm && _current is not null)
                folder = Path.GetDirectoryName(LinkedOutput.Entry(LinkedOutput.SourceTarget(_current, folder)))!;
            if (Directory.Exists(folder)) Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
            else Notify("먼저 자료를 출력하세요.");
        }
        catch (Exception ex) { Error(ex); }
    }
    private void PickBackgroundColor(object sender,RoutedEventArgs e)
    {
        if(_busy||_region is null)return;_pickingEffectColor=false;HideOverlaysCheck.IsChecked=false;Preview.CancelGesture();Preview.PickColorMode=true;Preview.Focus();_previewWindow?.Activate();Notify("원본 이미지의 색을 클릭하세요. ESC로 취소합니다.");
    }
    private void ChooseBackgroundColor(object sender,RoutedEventArgs e)
    {
        if(_busy||_region is null)return;var color=SKColor.TryParse(_region.BackgroundColor,out var c)?c:SKColors.White;
        var picker=new ColorPickerWindow(color){Owner=_previewWindow??this};if(picker.ShowDialog()==true)SetBackgroundColor(picker.SelectedColor);else if(picker.PickSourceRequested)PickBackgroundColor(this,new RoutedEventArgs());
    }
    private void SetBackgroundColor(SKColor color)
    {
        if(_region is null)return;var previous=_loading;_loading=true;BackgroundBox.SelectedIndex=(int)BackgroundMode.Solid;BackgroundColorBox.Text=ColorPickerWindow.Hex(color);_loading=previous;
        _region.Background=BackgroundMode.Solid;_region.BackgroundColor=ColorPickerWindow.Hex(color);ColorSwatch.Background=new SolidColorBrush(System.Windows.Media.Color.FromArgb(color.Alpha,color.Red,color.Green,color.Blue));MarkEdited();LoadContextLabels();Preview.InvalidateVisual();
    }
    private void FitObject(object sender,RoutedEventArgs e)
    {
        if(_busy||_current is null||_region is null)return;
        try{using var source=SourceImage.Read(_current.SourcePath)??throw new InvalidDataException("원본을 읽지 못했습니다.");var candidate=_region.Copy(false);candidate.CutoutEnabled=true;var dialog=new ObjectSelectionWindow(source,candidate){Owner=_previewWindow??this};
            if(dialog.ShowDialog()==true)ApplyObjectSelection(dialog.SelectedBounds,dialog.SelectedCutout,dialog.SelectedBackground,dialog.SelectedTolerance,dialog.SelectedTarget,dialog.SelectedInkColor,dialog.SelectedTextSettings);}
        catch(Exception ex){Error(ex);}
    }
    private void ClearObject(object sender,RoutedEventArgs e){if(_busy||_region is null)return;_region.Cutout=null;_region.CutoutEnabled=true;_region.ContentOnly=true;MarkEdited();LoadSoftnessFields();Preview.InvalidateVisual();Notify("현재 배경색·허용오차의 자동 분리로 전환했습니다.");}
    public void ApplyObjectSelection(NormalizedRect bounds,ObjectCutout? cutout,SKColor? background=null,double? tolerance=null,SeparationTarget? target=null,string? inkColor=null,TextSeparationSettings? textSettings=null)
    {if(_busy||_region is null)return;cutout?.Validate();if(textSettings is not null)_region.TextSettings=textSettings.Copy();if(target is { } selectedTarget)_region.SeparationTarget=selectedTarget;if(inkColor is not null)_region.TextInkColor=inkColor;if(_region.SeparationTarget==SeparationTarget.Text)_region.Softness=_region.Softness with{Enabled=false};var changedBounds=_region.Bounds!=bounds.Clamp();_region.Bounds=bounds.Clamp();_region.Cutout=cutout;_region.CutoutEnabled=true;_region.ContentOnly=cutout is not null;if(changedBounds)_region.Shape=RegionShape.Rectangle;if(background is { } color){_region.Background=color.Alpha==0?BackgroundMode.Transparent:BackgroundMode.Solid;_region.BackgroundColor=ColorPickerWindow.Hex(color);}if(tolerance is { } value)_region.ContentTolerance=value;var oldLoading=_loading;_loading=true;BackgroundBox.SelectedIndex=(int)_region.Background;BackgroundColorBox.Text=_region.BackgroundColor;_loading=oldLoading;SelectRegion(_region,_clip);MarkEdited();Preview.InvalidateVisual();Notify("정밀 보정 적용 · 자동 분리 중복 없음");}
    private EffectAsset? SelectedAsset=>AssetList.SelectedItem as EffectAsset;
    private void RefreshAssets(){_assets=AssetStore.List(out var errors);FilterEffects(this,new TextChangedEventArgs(TextBox.TextChangedEvent,UndoAction.None));if(errors.Count>0)Notify("일부 에셋을 읽지 못했습니다: "+errors[0]);}
    private void FilterEffects(object sender,TextChangedEventArgs e)
    {
        if(EffectSearchBox is null||AssetList is null)return;var query=EffectSearchBox.Text.Trim();
        foreach(var thumbnail in _thumbnails)thumbnail.Visibility=ProductInfo.EffectName(thumbnail.Kind).Contains(query,StringComparison.OrdinalIgnoreCase)?Visibility.Visible:Visibility.Collapsed;
        FilterAssetSearch(sender,e);
    }
    private void FilterAssetSearch(object sender,TextChangedEventArgs e)
    {if(AssetList is null)return;var query=AssetSearchBox?.Text.Trim()??"";AssetList.ItemsSource=_assets.Where(a=>(a.Name+" "+a.Category).Contains(query,StringComparison.OrdinalIgnoreCase)).ToList();}
    private SKBitmap? CurrentObjectSample()
    {
        if(_current is null||_region is null)return null;using var source=SourceImage.Read(_current.SourcePath);return source is null?null:ObjectExtractor.CropObject(source,_region);
    }
    private void OpenStudio(EffectAsset asset,bool useSource=true)
    {
        if(_busy)return;if(asset.Clips.Count==1&&asset.Clips.FirstOrDefault(c=>c.Route is not null) is { } travel){OpenRouteEditor(null,asset);return;}using var sample=useSource?CurrentObjectSample():null;var studio=new EffectStudioWindow(asset,sample){Owner=this};_studios.Add(studio);studio.Closed+=(_,_)=>_studios.Remove(studio);studio.Saved+=saved=>{RefreshAssets();AssetList.SelectedItem=_assets.FirstOrDefault(a=>a.Id==saved.Id);};studio.InsertRequested+=saved=>{RefreshAssets();AssetList.SelectedItem=_assets.FirstOrDefault(a=>a.Id==saved.Id);InsertAsset(this,new RoutedEventArgs());};if(Environment.GetEnvironmentVariable("LIVECG_DISABLE_WORKSPACE_RESTORE")=="1"){studio.WindowStartupLocation=WindowStartupLocation.Manual;studio.Left=-16000;studio.Top=-16000;studio.ShowInTaskbar=false;studio.ShowActivated=false;}studio.Show();
    }
    private void NewMotionAsset(object sender,RoutedEventArgs e)=>OpenStudio(EffectAsset.NewMotion(),false);
    private void SaveEffectCombo(object sender,RoutedEventArgs e)
    {
        if(_region is null||_region.Clips.Count==0){Notify("저장할 효과가 있는 영역을 선택하세요.");return;}
        var clips=_region.Clips.Select(c=>c.Copy()).ToList();var start=clips.Min(c=>c.Start);foreach(var clip in clips)clip.Start-=start;OpenStudio(new(){Name=_region.Name+" 효과",Clips=clips});
    }
    private void EditAsset(object sender,RoutedEventArgs e){if(SelectedAsset is { } asset)OpenStudio(asset.Copy());}
    private void InsertAsset(object sender,RoutedEventArgs e)
    {
        if(_busy||_region is null||SelectedAsset is not { } asset)return;
        try
        {
            var travel=asset.Clips.Any(c=>c.Route is not null);if(travel&&_current!.Regions.Count>=100)throw new InvalidDataException("영역은 파일당 최대100개입니다.");
            var targetRegion=travel?new EffectRegion{Name=asset.Name,Bounds=_region.Bounds,OverlayOnly=true,Background=BackgroundMode.KeepOriginal}:_region;
            var clips=AssetStore.Instantiate(asset,_time,_video.Fps);var rect=ObjectExtractor.PixelRect(targetRegion.Bounds,_current!.Width,_current.Height);
            foreach(var clip in clips.Where(c=>c.Route is not null))
            {
                var route=clip.Route!;var oldDuration=route.Duration;
                if(route.DurationBudget is { } budget){RouteEditing.AllocateDuration(route,budget,rect.Width,rect.Height,_video.Fps);if(clip.SourceDuration is null&&clip.SourceOffset==0&&Math.Abs(clip.Duration-TimelineRules.Snap(oldDuration,_video.Fps))<1e-8)clip.Duration=route.Duration;clip.LoopPeriod=route.Duration;}
                clip.Section=route.Repeat?ClipSection.Loop:ClipSection.Intro;if(route.Repeat){var introEnd=_current.Regions.SelectMany(r=>r.Clips).Where(c=>SectionRules.Effective(c)==ClipSection.Intro).Select(c=>c.End).DefaultIfEmpty(0).Max();clip.Start=Math.Max(clip.Start,introEnd);}clip.Validate();
            }
            if(targetRegion.Clips.Count+clips.Count>4000)throw new InvalidDataException("영역당 최대4,000클립입니다.");
            if(travel){_current.Regions.Insert(0,targetRegion);_current.Playback.Enabled=true;_current.Playback.ByEffects=true;}targetRegion.Clips.AddRange(clips);SelectRegion(targetRegion,clips[0]);MarkEdited();RefreshRegionList();RefreshTimeline();
        }
        catch(Exception ex){Error(ex);}
    }
    private void UpdateAssetClip(object sender,RoutedEventArgs e)
    {
        if(_busy||_region is null||_clip is null||SelectedAsset is not { } asset)return;
        try{var group=_region.Clips.Where(c=>_clip.AssetInstanceId is { } instance?c.AssetInstanceId==instance:c.Id==_clip.Id).ToList();var clips=AssetStore.Instantiate(asset,group.Min(c=>c.Start),_video.Fps);foreach(var c in group)_region.Clips.Remove(c);_region.Clips.AddRange(clips);SelectRegion(_region,clips[0]);MarkEdited();RefreshTimeline();}
        catch(Exception ex){Error(ex);}
    }
    private void DuplicateAsset(object sender,RoutedEventArgs e){if(SelectedAsset is not { } asset)return;try{var copy=asset.Copy(true);copy.Name+=" 복사";AssetStore.Save(copy);RefreshAssets();}catch(Exception ex){Error(ex);}}
    private void DeleteAsset(object sender,RoutedEventArgs e){if(SelectedAsset is not { } asset)return;if(MessageBox.Show(this,"내 효과 ‘"+asset.Name+"’를 보관함에서 삭제할까요?","에셋 삭제",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;try{AssetStore.Remove(asset.Id);RefreshAssets();}catch(Exception ex){Error(ex);}}
    private void FavoriteAsset(object sender,RoutedEventArgs e){if(SelectedAsset is not { } asset)return;asset.Favorite=!asset.Favorite;AssetStore.Save(asset);RefreshAssets();}
    private void ImportAsset(object sender,RoutedEventArgs e){if(_busy)return;var dialog=new OpenFileDialog{Filter="LiveCG 효과 에셋|*.lcgeffect.json;*.json",Multiselect=true};if(dialog.ShowDialog(this)==true)try{foreach(var path in dialog.FileNames)AssetStore.Import(path);RefreshAssets();}catch(Exception ex){Error(ex);}}
    private void ExportAsset(object sender,RoutedEventArgs e){if(SelectedAsset is not { } asset)return;var dialog=new SaveFileDialog{Filter="LiveCG 효과 에셋|*.lcgeffect.json",FileName=string.Concat(asset.Name.Select(c=>Path.GetInvalidFileNameChars().Contains(c)?'_':c))};if(dialog.ShowDialog(this)==true)try{DocumentStore.Save(dialog.FileName,asset);}catch(Exception ex){Error(ex);}}
}
