using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LiveCG.Core;
using Microsoft.Win32;
using SkiaSharp;

namespace LiveCG.App;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<ImageJob> _jobs=[];
    private readonly List<EffectThumbnail> _thumbnails=[];
    private readonly DispatcherTimer _clock=new(){Interval=TimeSpan.FromMilliseconds(33)};
    private readonly Stopwatch _playClock=new();
    private VideoOptions _video=new();
    private EffectOptions _defaultEffect=new();
    private double _defaultClipDuration=1,_time,_playStart;
    private ImageJob? _current;
    private EffectRegion? _region;
    private EffectClip? _clip;
    private CancellationTokenSource? _workCts;
    private bool _loading=true,_busy,_dirty,_playing;
    private int _selectionEpoch;
    private string? _projectPath;
    public MainWindow()
    {
        InitializeComponent();Title=$"{ProductInfo.Name} · {ProductInfo.Credit}";CreditText.Text=ProductInfo.Credit;FileList.ItemsSource=_jobs;
        var area=SystemParameters.WorkArea;MinWidth=Math.Min(MinWidth,area.Width*.9);MinHeight=Math.Min(MinHeight,area.Height*.9);
        Width=Math.Min(Width,area.Width*.95);Height=Math.Min(Height,area.Height*.95);
        EditorGrid.SizeChanged+=(_,args)=>
        {
            var compact=args.NewSize.Height<500;
            if(compact&&EditorGrid.RowDefinitions[2].Height.Value>280)EditorGrid.RowDefinitions[2].Height=new GridLength(260);
        };
        Choices(FpsBox,["24","30","60"]);foreach(var quality in Qualities)QualityBox.Items.Add(quality);
        Choices(ShapeBox,["사각형","원 / 타원","둥근 사각형","자유손","포인트 펜"]);Choices(ExportScopeBox,["전체 파일","선택 파일","현재 파일"]);ExportScopeBox.SelectedIndex=0;
        Choices(LoopPeriodBox,["0.25","0.5","1","1.5","2","3","5"]);
        Choices(EndHoldBox,["0","0.5","1","2","3"]);Choices(ClipDurationBox,["0.5","0.8","1","1.5","2","3","5"]);
        Choices(EffectSpeedBox,["0.25","0.5","1","1.5","2","4"]);Choices(StrengthBox,ProductInfo.Strengths.Select(ProductInfo.StrengthName).ToArray());
        Choices(BackgroundBox,["테두리색 추정","투명","직접 색상","원본 유지"]);
        foreach(var kind in Enum.GetValues<EffectKind>().Where(k=>k!=EffectKind.UserMotion))
        {
            var thumbnail=new EffectThumbnail(kind);thumbnail.MouseLeftButtonDown+=(_,_)=>SelectEffect(kind);
            _thumbnails.Add(thumbnail);EffectsGrid.Children.Add(thumbnail);
        }
        Preview.RegionSelected+=region=>SelectRegion(region);
        Preview.RegionEdited+=()=>{MarkEdited();RefreshRegionList();RefreshTimeline();UpdateSelectionLabel();};
        Preview.GestureStarted+=BeginGesture;Preview.GestureEnded+=EndGesture;
        Preview.ViewChanged+=UpdateViewLabel;Preview.Message+=Notify;
        Preview.DeleteRequested+=()=>DeleteRegion(this,new RoutedEventArgs());
        TimelineEditor.SelectionChanged+=(region,clip)=>{Preview.CancelGesture();StopPlayback();SelectRegion(region,clip,false);};
        TimelineEditor.Seeked+=time=>{StopPlayback();Seek(time,true);};
        TimelineEditor.Edited+=()=>{MarkEdited();RefreshTimeline();LoadClipFields();LoadMaskFields();};
        TimelineEditor.GestureStarted+=BeginGesture;TimelineEditor.GestureEnded+=EndGesture;AddTimelineMenu();
        TimelineEditor.DeleteRequested+=()=>DeleteClip(this,new RoutedEventArgs());TimelineEditor.Message+=Notify;
        TimelineEditor.MoveLayerRequested+=MoveLayer;TimelineEditor.ToggleLayerRequested+=ToggleLayer;
        _clock.Tick+=(_,_)=>TickPlayback();
        InitializePlayback();InitializeGroups();InitializeMouseProperties();InitializeSoftness();InitializeLayerPanel();LoadUserDefaults();_loading=false;ShowVideo();ShowEditorOptions();SelectRegion(null);TimelineEditor.SetJob(null,_video);ResetHistory();InitializeWorkspace();_clock.Start();
        Loaded+=async(_,_)=>{var paths=Environment.GetCommandLineArgs().Skip(1).Where(SourceImage.Supported).ToArray();if(paths.Length>0)await ImportAsync(paths);};
    }
    private VideoOptions ResolvedVideo=>_current is null?TimelineRules.Resolve([], _video):TimelineRules.Resolve(_current,_video);
    private static void Choices(ComboBox box,string[] values){foreach(var value in values)box.Items.Add(value);}
    private static string F(double value)=>value.ToString("0.###",CultureInfo.InvariantCulture);
    private static double Number(string text,string label)
    {if(double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out var value)&&double.IsFinite(value))return value;throw new InvalidDataException(label+"에 올바른 숫자를 입력하세요.");}
    private void Notify(string text)=>StatusText.Text=text;
    private void MarkEdited(bool allFiles=false)
    {
        _dirty=true;
        foreach(var job in allFiles?_jobs:_current is null?Enumerable.Empty<ImageJob>():new[]{_current})
        {job.Progress=0;job.Status=job.Regions.Count==0?"영역 지정 대기":$"편집 중 · 영역 {job.Regions.Count}개";job.Detail="";}
        RecordEdit();
    }
    private void Error(Exception ex){Notify(ex.Message);MessageBox.Show(this,ex.Message,"LiveCG-EFF · 확인",MessageBoxButton.OK,MessageBoxImage.Warning);}
    private void SetBusy(bool busy){_busy=busy;TimelinePanel.IsEnabled=!busy;FileList.IsEnabled=!busy;ExportButton.IsEnabled=!busy;RetryButton.IsEnabled=!busy;CancelButton.IsEnabled=busy;OutputKindBox.IsEnabled=!busy;OutputFolderBox.IsEnabled=!busy;ExportScopeBox.IsEnabled=!busy;FpsBox.IsEnabled=!busy;QualityBox.IsEnabled=!busy;EndHoldBox.IsEnabled=!busy;if(MouseProperties.TryGetValue("EndHoldBox",out var mouse))mouse.IsEnabled=!busy;SetWorkspaceBusy(busy);RefreshOutputSettings();UpdateHistoryButtons();}
    private void RefreshFiles()=>CountText.Text=$"{_jobs.Count}개 이미지 · 직접 지정 / 파일별 진행 바";
    private List<ImageJob> SelectedJobs()=>FileList.SelectedItems.Cast<ImageJob>().ToList();
    private void QueueEdit(Action action)
    {
        if(_loading)return;var epoch=_selectionEpoch;
        Dispatcher.BeginInvoke(()=>{if(!_loading&&epoch==_selectionEpoch)action();},DispatcherPriority.Background);
    }

    private async void AddFiles(object sender,RoutedEventArgs e)
    {if(_busy)return;var dialog=new OpenFileDialog{Filter=SourceImage.Filter,Multiselect=true};if(dialog.ShowDialog(this)==true)await ImportAsync(dialog.FileNames);}
    private async void AddFolder(object sender,RoutedEventArgs e)
    {if(_busy)return;var dialog=new OpenFolderDialog{Title="이미지 폴더"};if(dialog.ShowDialog(this)==true)try{await ImportAsync(Directory.EnumerateFiles(dialog.FolderName).Where(SourceImage.Supported));}catch(Exception ex){Error(ex);}}
    private void FilesDragOver(object sender,DragEventArgs e){e.Effects=!_busy&&e.Data.GetDataPresent(DataFormats.FileDrop)?DragDropEffects.Copy:DragDropEffects.None;e.Handled=true;}
    private async void FilesDropped(object sender,DragEventArgs e)
    {
        if(_busy||!e.Data.GetDataPresent(DataFormats.FileDrop))return;
        try{await ImportAsync(((string[])e.Data.GetData(DataFormats.FileDrop)).SelectMany(p=>Directory.Exists(p)?Directory.EnumerateFiles(p).Where(SourceImage.Supported):[p]));}catch(Exception ex){Error(ex);}
    }
    private async Task ImportAsync(IEnumerable<string> paths)
    {
        if(_busy)return;
        var files=paths.Where(SourceImage.Supported).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(p=>!_jobs.Any(j=>j.SourcePath.Equals(p,StringComparison.OrdinalIgnoreCase))).ToArray();
        if(_jobs.Count+files.Length>1000){Notify("한 작업에 최대 1,000개까지 등록할 수 있습니다.");return;}
        if(files.Length==0){Notify("추가할 새 이미지가 없습니다.");return;}
        SetBusy(true);StopPlayback();_workCts=new();var token=_workCts.Token;var failures=new List<string>();
        try
        {
            foreach(var path in files)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var job=await Task.Run(()=>{using var image=SourceImage.Read(path)??throw new InvalidDataException("이미지를 읽을 수 없습니다.");return new ImageJob{SourcePath=path,Width=image.Width,Height=image.Height,Playback=_defaultPlayback with{}};},token);
                    _jobs.Add(job);_dirty=true;RefreshFiles();ResetHistory();
                }catch(Exception ex)when(ex is not OperationCanceledException){failures.Add(Path.GetFileName(path)+": "+ex.Message);}
            }
            Notify(failures.Count==0?"이미지 등록 완료 · 원하는 곳을 마우스로 드래그해 지정하세요.":string.Join(" / ",failures));
        }
        catch(OperationCanceledException){Notify("등록을 취소했습니다. 이미 추가한 파일은 유지됩니다.");}
        finally
        {
            _workCts.Dispose();_workCts=null;SetBusy(false);
            if(_current is null&&_jobs.Count>0)FileList.SelectedItem=_jobs[0];
            ResetHistory();
        }
    }
    private void FileSelectionChanged(object sender,SelectionChangedEventArgs e)
    {
        if(_loading||_busy)return;
        var next=e.AddedItems.Cast<ImageJob>().LastOrDefault()??FileList.SelectedItem as ImageJob;
        if(next is not null&&next!=_current)DisplayJob(next);
    }
    private void DisplayJob(ImageJob? job)
    {
        if(_clip is not null)CommitClip(false);CommitRegion(false);CommitMask(false);ClearMaskSelection();StopPlayback();
        _loading=true;_current=job;_region=null;_clip=null;_time=0;
        try{Preview.SetJob(job);}catch(Exception ex){Error(ex);}
        Preview.ShowEffects=false;EffectsPreviewCheck.IsChecked=false;RegionList.ItemsSource=job?.Regions;
        TimelineEditor.SetJob(job,_video);_loading=false;SelectRegion(LayerGraph.Rows(job?.Regions??[]).FirstOrDefault()?.Region);RefreshRegionList();
        ImageSizeText.Text=job is null?"":$"{job.Width} × {job.Height}";RefreshTimeline();UpdateViewLabel();
    }
    private void RemoveFiles(object sender,RoutedEventArgs e)
    {
        if(_busy)return;var removed=SelectedJobs();foreach(var job in removed)_jobs.Remove(job);
        if(_current is not null&&removed.Contains(_current)){_region=null;_clip=null;_current=null;DisplayJob(_jobs.FirstOrDefault());}
        _dirty=true;RefreshFiles();
    }
    private void SelectAllFiles(object sender,RoutedEventArgs e){if(!_busy)FileList.SelectAll();}

    private void RefreshRegionList()
    {
        var previous=_loading;_loading=true;RegionList.Tag=_current?.SourcePath??"";LayerCountText.Text=(_current?.Regions.Count??0)+"개";RegionList.ItemsSource=LayerGraph.Rows(_current?.Regions??[]).Where(r=>r.Mask is null).Select(r=>r.Region).ToList();RegionList.SelectedItem=_region;_loading=previous;
    }
    private void SelectRegion(EffectRegion? region,EffectClip? clip=null,bool chooseFirst=true)
    {
        if(_clip is not null)CommitClip(false);CommitRegion(false);
        CommitMask(false);ClearMaskSelection();
        _region=region;
        _clip=clip??(region?.Clips.Contains(_clip!)==true?_clip:chooseFirst?region?.Clips.OrderBy(c=>c.Start).FirstOrDefault():null);
        _selectionEpoch++;_loading=true;
        if(RegionList.Items.Count!=(_current?.Regions.Count??0))RegionList.Items.Refresh();
        RegionList.SelectedItem=region;RegionNameBox.Text=region?.Name??"";
        RegionLockCheck.IsChecked=region?.Locked??false;BackgroundBox.SelectedIndex=(int)(region?.Background??BackgroundMode.Auto);
        BackgroundColorBox.Text=region?.BackgroundColor??"#FFFFFF";BackgroundColorBox.IsEnabled=region?.Background==BackgroundMode.Solid;
        if(SKColor.TryParse(region?.BackgroundColor??"#FFFFFF",out var swatch))ColorSwatch.Background=new SolidColorBrush(Color.FromArgb(swatch.Alpha,swatch.Red,swatch.Green,swatch.Blue));
        ShapeBox.SelectedIndex=(int)_editor.NewShape;
        Preview.SelectedRegion=region;TimelineEditor.SelectedRegion=region;TimelineEditor.SelectedClip=_clip;
        _loading=false;LoadClipFields();LoadGroupFields();UpdateSelectionLabel();TimelineEditor.InvalidateVisual();Preview.InvalidateVisual();
        if(!_restoring&&!_gestureActive)_history.UpdateSelection(CaptureState());
    }
    private void RegionSelectionChanged(object sender,SelectionChangedEventArgs e){if(!_loading){Preview.CancelGesture();SelectRegion(RegionList.SelectedItem as EffectRegion);}}
    private void LoadClipFields()
    {
        var previous=_loading;_loading=true;
        var options=_clip?.Options??_defaultEffect;
        ClipStartBox.Text=F(_clip?.Start??_time);ClipDurationBox.Text=F(_clip?.Duration??_defaultClipDuration);
        StrengthBox.SelectedIndex=ProductInfo.StrengthIndex(options.Strength);EffectSpeedBox.Text=F(options.Speed);ClipEnabledCheck.IsChecked=_clip?.Enabled??true;
        ClipLoopCheck.IsChecked=_clip?.Loop??_defaultLoop;LoopPeriodBox.Text=F(_clip?.LoopPeriod??_defaultLoopPeriod);
        EffectLabel.Text=_clip is null?$"{ProductInfo.EffectName(options.Kind)} · ＋버튼으로 클립 추가":$"{ProductInfo.EffectName(options.Kind)} · 선택 클립";
        ShowEffectColor();
        LoadSoftnessFields();
        foreach(var thumbnail in _thumbnails){thumbnail.Selected=thumbnail.Kind==options.Kind;thumbnail.InvalidateVisual();}
        _loading=previous;
    }
    private void UpdateSelectionLabel()
    {
        SelectedRegionText.Text=_region?.Name??"영역 선택";var index=_region is null?-1:_current?.Regions.IndexOf(_region)??-1;
        SelectedRegionText.ToolTip=index<0?"선택 영역 없음 · 직접 지정 후 효과를 추가하세요":$"✓ {index+1:00}  {_region!.Name} 선택됨 · {_region.Clips.Count}개 효과{(_region.Locked?" · 위치 잠금":"")}{(TimelineEditor.SelectedClipIds.Count>1?$" · {TimelineEditor.SelectedClipIds.Count}개 클립 선택":"")}";
    }
    private bool CommitRegion(bool report)
    {
        if(_loading||_region is null)return true;
        try
        {
            _region.Name=string.IsNullOrWhiteSpace(RegionNameBox.Text)?"영역":RegionNameBox.Text.Trim();
            _region.Locked=RegionLockCheck.IsChecked==true;
            if(BackgroundBox.SelectedIndex>=0)_region.Background=(BackgroundMode)BackgroundBox.SelectedIndex;
            if(_region.Background==BackgroundMode.Solid)
            {if(!SKColor.TryParse(BackgroundColorBox.Text.Trim(),out _))throw new InvalidDataException("배경색은 #RRGGBB 형식으로 입력하세요.");_region.BackgroundColor=BackgroundColorBox.Text.Trim();}
            return true;
        }catch(Exception ex){if(report)Error(ex);return false;}
    }
    private void RegionPropertiesChanged(object sender,RoutedEventArgs e)
    {if(_loading)return;if(CommitRegion(true)){MarkEdited();RefreshRegionList();UpdateSelectionLabel();TimelineEditor.InvalidateVisual();Preview.InvalidateVisual();}}
    private void BackgroundChanged(object sender,RoutedEventArgs e)
    {if(_loading)return;BackgroundColorBox.IsEnabled=BackgroundBox.SelectedIndex==(int)BackgroundMode.Solid;RegionPropertiesChanged(sender,e);}
    private bool CommitClip(bool report)
    {
        if(_loading)return true;
        try
        {
            var options=(_clip?.Options??_defaultEffect) with {Strength=ProductInfo.StrengthAt(StrengthBox.SelectedIndex),Speed=Number(EffectSpeedBox.Text,"속도")};
            options.Validate();var duration=Number(ClipDurationBox.Text,"클립 길이");if(duration is <.016 or >300)throw new InvalidDataException("클립 길이는 0.016~300초입니다.");
            var loop=ClipLoopCheck.IsChecked==true;var period=Number(LoopPeriodBox.Text,"반복 주기");
            new EffectClip{Loop=loop,LoopPeriod=period}.Validate();
            if(_clip is not null)
            {
                var start=Number(ClipStartBox.Text,"시작 시간");if(start<0)throw new InvalidDataException("시작 시간은 0 이상이어야 합니다.");
                var startChanged=Math.Abs(start-Number(F(_clip.Start),"시작 시간"))>1e-8;
                var durationChanged=Math.Abs(duration-Number(F(_clip.Duration),"클립 길이"))>1e-8;
                var candidate=_clip with{Start=startChanged?TimelineRules.Snap(start,_video.Fps):_clip.Start,
                    Duration=durationChanged?Math.Max(1d/_video.Fps,TimelineRules.Snap(duration,_video.Fps)):_clip.Duration,Options=options,Loop=loop,LoopPeriod=period};
                candidate.Validate();
                if(durationChanged&&_region is not null)TimelineRules.SetTimingWithFollowers(_region,_clip,candidate.Start,candidate.Duration,_video.Fps,_editor.Ripple,_editor.FollowMagnet);
                else{_clip.Start=candidate.Start;_clip.Duration=candidate.Duration;}
                _clip.Options=options;_clip.Enabled=ClipEnabledCheck.IsChecked==true;_clip.Loop=loop;_clip.LoopPeriod=period;
            }
            else{_defaultEffect=options;_defaultClipDuration=duration;_defaultLoop=loop;_defaultLoopPeriod=period;}
            return true;
        }catch(Exception ex){if(report)Error(ex);return false;}
    }
    private void ClipOptionsChanged(object sender,RoutedEventArgs e)
    {if(_loading)return;if(CommitClip(true)){MarkEdited();LoadClipFields();RefreshTimeline();}}
    private void ClipComboChanged(object sender,SelectionChangedEventArgs e)=>QueueEdit(()=>ClipOptionsChanged(sender,e));
    private void SelectEffect(EffectKind kind)
    {
        if(_busy||!CommitClip(true))return;
        if(_clip is not null)_clip.Options=_clip.Options with {Kind=kind};
        else _defaultEffect=_defaultEffect with {Kind=kind};
        if(_region is not null&&_clip is null)AddNewClip(false);
        else{MarkEdited();LoadClipFields();RefreshTimeline();if(_clip is not null)Seek(_clip.Start+_clip.Duration*.45,true);}
    }
    private void AddRegion(object sender,RoutedEventArgs e)
    {
        if(_busy||_current is null){Notify("먼저 이미지를 추가하세요.");return;}
        SetDrawMode(!Preview.AddMode);
    }
    private void DuplicateRegion(object sender,RoutedEventArgs e)
    {
        if(_busy||_region is null||_current is null||!CommitClip(true)||!CommitRegion(true))return;
        if(_current.Regions.Count>=100){Notify("영역은 파일당 최대 100개입니다.");return;}
        var source=_current.Regions.Where(r=>r.Id==_region.Id||r.ParentId==_region.Id).ToList();if(_current.Regions.Count+source.Count>100){Notify("영역은 파일당 최대100개입니다.");return;}var copies=LayerGraph.Copy(source);var copy=copies[source.IndexOf(_region)];if(_region.Role==RegionRole.EffectArea){copy.Role=_region.Role;copy.ParentId=_region.ParentId;}copy.Name+=" 복사";copy.Locked=false;_current.Regions.InsertRange(0,copies);SelectRegion(copy);MarkEdited();RefreshTimeline();
    }
    private void DeleteRegion(object sender,RoutedEventArgs e)
    {
        if(_busy||_current is null||_region is null)return;Preview.CancelGesture();TimelineEditor.CancelGesture();var id=_region.Id;_current.Regions.RemoveAll(r=>r.Id==id||r.ParentId==id);_region=null;_clip=null;
        SelectRegion(_current.Regions.FirstOrDefault());RefreshRegionList();MarkEdited();RefreshTimeline();
    }
    private void AddClip(object sender,RoutedEventArgs e)=>AddNewClip(false);
    private void AddClipAtCursor(object sender,RoutedEventArgs e)=>AddNewClip(true);
    private void AddNewClip(bool atCursor)
    {
        if(_busy||_region is null){Notify("먼저 효과를 적용할 영역을 선택하세요.");return;}
        if(!CommitClip(true))return;
        var options=(_clip?.Options??_defaultEffect).Current();
        var clip=TimelineRules.Append(_region,options,_clip?.Duration??_defaultClipDuration,_video.Fps);
        clip.Loop=_clip?.Loop??_defaultLoop;clip.LoopPeriod=_clip?.LoopPeriod??_defaultLoopPeriod;
        if(clip.Options.Kind==EffectKind.UserMotion)clip.Motion=_clip?.Motion?.Copy()??_defaultMotion?.Copy()??MotionDefinition.Example();
        if(atCursor)TimelineRules.SetTiming(clip,_time,clip.Duration,_video.Fps);
        _clip=clip;_selectionEpoch++;LoadClipFields();TimelineEditor.SelectedClip=clip;MarkEdited();RefreshRegionList();UpdateSelectionLabel();RefreshTimeline();
        Seek(clip.Start+clip.Duration*.45,true);
    }
    private void DuplicateClip(object sender,RoutedEventArgs e)
    {
        if(_busy||_region is null||_clip is null||!CommitClip(true))return;
        var clone=_clip.Copy();if(clone.SequenceId is not null)clone.SequenceId=Guid.NewGuid();clone.Start=_region.Clips.Select(c=>c.End).DefaultIfEmpty(0).Max();clone.Validate();
        _region.Clips.Add(clone);_clip=clone;_selectionEpoch++;LoadClipFields();TimelineEditor.SelectedClip=clone;MarkEdited();RefreshRegionList();RefreshTimeline();
    }
    private void DeleteClip(object sender,RoutedEventArgs e)
    {
        if(_selectedMask is not null){DeleteMask(sender,e);return;}
        if(_busy||_region is null||_clip is null)return;TimelineEditor.CancelGesture();var selected=TimelineEditor.SelectedClips.ToList();if(selected.Count==0)selected.Add((_region,_clip));foreach(var item in selected)item.Region.Clips.Remove(item.Clip);_clip=_region.Clips.OrderBy(c=>c.Start).FirstOrDefault();_selectionEpoch++;
        LoadClipFields();TimelineEditor.SelectedClip=_clip;MarkEdited();RefreshRegionList();UpdateSelectionLabel();RefreshTimeline();
    }
    private void ApplyRegions(object sender,RoutedEventArgs e)
    {
        if(_busy||_current is null||!CommitClip(true)||!CommitRegion(true))return;
        var targets=SelectedJobs().Where(j=>j!=_current).ToList();if(targets.Count==0){Notify("Ctrl/Shift로 적용 대상 파일을 함께 선택하세요.");return;}
        if(MessageBox.Show(this,$"선택한 {targets.Count}개 파일의 기존 영역과 타임라인을 교체할까요?","일괄 적용",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
        foreach(var job in targets){job.Regions=LayerGraph.Copy(_current.Regions);job.Playback=_current.Playback with{};job.Status="영역·클립 적용";job.Progress=0;}
        MarkEdited(true);Notify("선택 파일에 영역과 타임라인을 비율 좌표로 적용했습니다.");
    }

    private void RefreshTimeline()
    {
        var previousLoading=_loading;_loading=true;SectionModeBox.SelectedIndex=_current?.Playback.ByEffects==false?1:0;_loading=previousLoading;var resolved=ResolvedVideo;var plan=_current is null?null:PlaybackPlan.Create(_current,_video);AutoDurationText.Text=$"{resolved.ActualDuration:0.00}초 · {resolved.FrameCount:N0}프레임"+(plan?.Enabled==true?$"\nWebM {plan.WebmDuration:0.##}초 · {plan.Repeats}회 / OBS 무한":"");
        Preview.OutputOpacity=plan?.Opacity(_time)??1;
        ExitPlaybackButton.IsEnabled=!_busy&&_current?.Playback.Enabled==true&&_current.Playback.ExitEnabled!=false;
        RefreshOutputSettings();TimelineEditor.SetVideo(_video);TimelineEditor.CursorTime=_time;TimelineEditor.InvalidateVisual();
        TimeText.Text=$"{_time:0.00} / {resolved.ActualDuration:0.00}초";LoadContextLabels();Preview.InvalidateVisual();
    }
    private void ShowVideo()
    {
        var previous=_loading;_loading=true;FpsBox.Text=_video.Fps.ToString();ShowQuality();EndHoldBox.Text=F(_video.EndHold);OutputKindBox.SelectedIndex=(int)_video.Output;ExportButton.Content="출력 시작";_loading=previous;RefreshTimeline();
    }
    private bool CommitVideo(bool report)
    {
        if(_loading)return true;
        try
        {
            var fps=Number(FpsBox.Text,"fps");var crf=(QualityBox.SelectedItem as QualityChoice)?.Crf??_video.Crf;if(fps!=Math.Truncate(fps))throw new InvalidDataException("fps에는 정수를 입력하세요.");
            var next=_video with {Fps=checked((int)fps),Crf=checked((int)crf),EndHold=Number(EndHoldBox.Text,"끝 여유")};next.Validate();_video=next;return true;
        }catch(Exception ex){if(report)Error(ex);return false;}
    }
    private void VideoOptionsChanged(object sender,RoutedEventArgs e){if(!_loading&&!_busy&&CommitVideo(true)){MarkEdited(true);RefreshTimeline();}}
    private void VideoComboChanged(object sender,SelectionChangedEventArgs e)=>QueueEdit(()=>VideoOptionsChanged(sender,e));
    private void Seek(double time,bool effects)
    {
        Preview.RestDisplay=null;_time=Math.Max(0,time);Preview.Time=_time;Preview.OutputOpacity=_current is null?1:PlaybackPlan.Create(_current,_video).Opacity(_time);TimelineEditor.CursorTime=_time;
        if(effects){EffectsPreviewCheck.IsChecked=true;Preview.ShowEffects=true;}
        TimeText.Text=$"{_time:0.00} / {ResolvedVideo.ActualDuration:0.00}초";Preview.InvalidateVisual();TimelineEditor.InvalidateVisual();
    }
    private void TogglePlayback(object sender,RoutedEventArgs e)
    {
        if(_current is null)return;if(_playing){StopPlayback(true);return;}
        if(!CommitClip(true)||!CommitRegion(true)||!CommitMask(true)||!CommitVideo(true))return;
        if(_time>=ResolvedVideo.ActualDuration)_time=0;
        if(BroadcastPreviewCheck.IsChecked==true&&_current.Playback.Enabled){if(_broadcastClock is null){_broadcastClock=new(PlaybackPlan.Create(_current,_video),_current.Playback.ExitAtLoopEnd);_broadcastElapsed=0;}_playStart=_broadcastElapsed;}
        else{_broadcastClock=null;_playStart=_time;}
        _playing=true;_playClock.Restart();PlayButton.Content="Ⅱ 일시정지";EffectsPreviewCheck.IsChecked=true;Preview.ShowEffects=true;
    }
    private void StopPlayback(bool keepBroadcast=false){if(_playing&&_broadcastClock is not null)_broadcastElapsed=_playStart+_playClock.Elapsed.TotalSeconds;_playing=false;_playClock.Stop();if(!keepBroadcast){_broadcastClock=null;_broadcastElapsed=0;}PlayButton.Content="▶ 재생";_singleCycle=false;if(!keepBroadcast)Preview.RestDisplay=null;}
    private void Rewind(object sender,RoutedEventArgs e){StopPlayback();Seek(0,true);}
    private void PreviewModeChanged(object sender,RoutedEventArgs e){if(Preview is not null){Preview.ShowEffects=EffectsPreviewCheck.IsChecked==true;Preview.InvalidateVisual();}}
    private void UpdateViewLabel()
    {
        if(ZoomText is null)return;ZoomText.Text=$"{Preview.Viewport.Scale*VisualTreeHelper.GetDpi(Preview).DpiScaleX*100:0}%";
        DrawModeButton.Content=Preview.AddMode?"✓ 그리기 모드":"＋ 직접 지정";
    }
    private void ZoomIn(object sender,RoutedEventArgs e)=>Preview.ZoomAt(1.25);
    private void ZoomOut(object sender,RoutedEventArgs e)=>Preview.ZoomAt(.8);
    private void NativeZoom(object sender,RoutedEventArgs e)=>Preview.NativeSize();
    private void FitContent(object sender,RoutedEventArgs e)=>Preview.Fit(true);
    private void ResetZoom(object sender,RoutedEventArgs e)=>Preview.Fit(false);
    private void TimelineZoomIn(object sender,RoutedEventArgs e)=>TimelineEditor.Zoom(1.25);
    private void TimelineZoomOut(object sender,RoutedEventArgs e)=>TimelineEditor.Zoom(.8);
    private void ChooseBackground(object sender,RoutedEventArgs e)
    {var dialog=new OpenFileDialog{Filter="이미지|*.png;*.jpg;*.jpeg;*.bmp"};if(dialog.ShowDialog(this)==true)try{Preview.SetBackground(dialog.FileName);CompareBackgroundBox.SelectedIndex=4;}catch(Exception ex){Error(ex);}}
    private void ClearBackground(object sender,RoutedEventArgs e){Preview.SetBackground(null);CompareBackgroundBox.SelectedIndex=0;}

    private void LoadUserDefaults()
    {
        try
        {
            var defaults=File.Exists(DocumentStore.DefaultsPath)?DocumentStore.Load<AppDefaults>(DocumentStore.DefaultsPath):new(){Playback=PlaybackSections.NewDefault()};
            if(File.Exists(DocumentStore.DefaultsPath))
            {
                using var json=System.Text.Json.JsonDocument.Parse(File.ReadAllText(DocumentStore.DefaultsPath));
                if(!json.RootElement.TryGetProperty("Playback",out var policy)||!policy.TryGetProperty("LoopRest",out _))defaults.Playback.LoopRest=1;
            }
            _video=defaults.Video;_defaultPlayback=defaults.Playback with{};_defaultEffect=defaults.Effect;_defaultClipDuration=defaults.ClipDuration;_defaultLoop=defaults.Loop;_defaultLoopPeriod=defaults.LoopPeriod;_defaultMotion=defaults.Motion?.Copy();_editor=defaults.Editor;OutputFolderBox.Text=defaults.OutputDirectory;
        }catch(Exception ex){_video=new();_defaultPlayback=PlaybackSections.NewDefault();_defaultEffect=new();_defaultClipDuration=1;OutputFolderBox.Text="";Notify("기본값을 읽지 못해 초기값을 사용합니다: "+ex.Message);}
    }
    private void SaveDefaults(object sender,RoutedEventArgs e)
    {
        if(_busy||!CommitClip(true)||!CommitVideo(true)||!CommitOutputTiming())return;
        try
        {
            var defaults=new AppDefaults{Video=_video with{},Playback=(_current?.Playback??_defaultPlayback) with{},Effect=(_clip?.Options??_defaultEffect).Current(),ClipDuration=_clip?.Duration??_defaultClipDuration,OutputDirectory=OutputFolderBox.Text.Trim(),Loop=_clip?.Loop??_defaultLoop,LoopPeriod=_clip?.LoopPeriod??_defaultLoopPeriod,Motion=_clip?.Motion?.Copy()??_defaultMotion?.Copy(),Editor=_editor with{}};
            DocumentStore.Save(DocumentStore.DefaultsPath,defaults);_defaultPlayback=defaults.Playback with{};_defaultEffect=defaults.Effect with{};_defaultClipDuration=defaults.ClipDuration;Notify("효과·구간·출력 옵션을 새 작업 기본값으로 저장했습니다.");
        }catch(Exception ex){Error(ex);}
    }
    private void ResetDefaults(object sender,RoutedEventArgs e)
    {
        if(_busy||MessageBox.Show(this,"저장된 기본값을 초기값으로 복원할까요? 현재 영역과 클립은 유지합니다.","기본값 복원",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
        try
        {var defaults=new AppDefaults{Playback=PlaybackSections.NewDefault()};DocumentStore.Save(DocumentStore.DefaultsPath,defaults);_video=defaults.Video;_defaultPlayback=defaults.Playback;_defaultEffect=defaults.Effect;_defaultClipDuration=defaults.ClipDuration;_defaultLoop=false;_defaultLoopPeriod=1;_defaultMotion=null;_editor=defaults.Editor;ShowVideo();ShowEditorOptions();LoadClipFields();Notify("기본값을 복원했습니다.");}
        catch(Exception ex){Error(ex);}
    }
    private bool SaveProjectInternal()
    {
        if(!CommitClip(true)||!CommitRegion(true)||!CommitMask(true)||!CommitVideo(true))return false;
        var dialog=new SaveFileDialog{Filter="LiveCG 작업|*.livecg.json",FileName=Path.GetFileName(_projectPath??"방송자막.livecg.json")};if(dialog.ShowDialog(this)!=true)return false;
        try{DocumentStore.Save(dialog.FileName,new ProjectDocument{Jobs=_jobs.Select(j=>j.Snapshot()).ToList(),Video=_video with{},OutputDirectory=OutputFolderBox.Text.Trim()});_projectPath=dialog.FileName;_dirty=false;Notify("작업과 타임라인을 저장했습니다.");return true;}
        catch(Exception ex){Error(ex);return false;}
    }
    private void SaveProject(object sender,RoutedEventArgs e){if(!_busy)SaveProjectInternal();}
    private bool ConfirmDiscard()
    {
        if(!_dirty)return true;
        var result=MessageBox.Show(this,"현재 작업과 타임라인을 저장할까요?","미저장 변경",MessageBoxButton.YesNoCancel);
        return result==MessageBoxResult.No||(result==MessageBoxResult.Yes&&SaveProjectInternal());
    }
    private void NewProject(object sender,RoutedEventArgs e)
    {
        if(_busy||!ConfirmDiscard())return;
        _region=null;_clip=null;_current=null;_jobs.Clear();_projectPath=null;LoadUserDefaults();ShowVideo();DisplayJob(null);ShowEditorOptions();RefreshFiles();_dirty=false;ResetHistory();
    }
    private void OpenProject(object sender,RoutedEventArgs e)
    {
        if(_busy||!ConfirmDiscard())return;var dialog=new OpenFileDialog{Filter="LiveCG 작업|*.livecg.json;*.json"};if(dialog.ShowDialog(this)!=true)return;
        try
        {
            var doc=DocumentStore.Load<ProjectDocument>(dialog.FileName);var missing=doc.Jobs.Where(j=>!File.Exists(j.SourcePath)).ToList();
            if(missing.Count>0)throw new FileNotFoundException("원본 이미지를 찾을 수 없습니다. 원래 위치로 복원한 뒤 다시 여세요.\n"+string.Join("\n",missing.Take(4).Select(j=>j.SourcePath)));
            ApplyDocument(doc);_projectPath=dialog.FileName;_dirty=false;Notify("영역·타임라인·영상 옵션을 복원했습니다.");
        }catch(Exception ex){Error(ex);}
    }
    public void ApplyDocument(ProjectDocument document,bool resetHistory=true)
    {
        document.Validate();StopPlayback();_loading=true;_region=null;_clip=null;_current=null;
        _jobs.Clear();foreach(var job in document.Jobs){job.Status="작업 복원";_jobs.Add(job);}
        _video=document.Video;OutputFolderBox.Text=document.OutputDirectory;_loading=false;ShowVideo();RefreshFiles();
        DisplayJob(_jobs.FirstOrDefault());if(_jobs.Count>0){_loading=true;FileList.SelectedItem=_jobs[0];_loading=false;}
        if(resetHistory)ResetHistory();
    }
    private void SaveTemplate(object sender,RoutedEventArgs e)
    {
        if(_busy||_current is null||!CommitClip(true)||!CommitRegion(true)||!CommitMask(true)||!CommitVideo(true))return;
        var dialog=new SaveFileDialog{Filter="LiveCG 템플릿|*.livecg-template.json",FileName="효과타임라인.livecg-template.json"};
        if(dialog.ShowDialog(this)==true)try{DocumentStore.Save(dialog.FileName,new TemplateDocument{Regions=LayerGraph.Copy(_current.Regions),Playback=_current.Playback with{},Video=_video with{}});Notify("영역·구간·영상 옵션을 템플릿으로 저장했습니다.");}catch(Exception ex){Error(ex);}
    }
    private void ApplyTemplate(object sender,RoutedEventArgs e)
    {
        if(_busy)return;var targets=SelectedJobs();if(targets.Count==0){Notify("템플릿을 적용할 파일을 선택하세요.");return;}
        var dialog=new OpenFileDialog{Filter="LiveCG 템플릿|*.livecg-template.json;*.json"};if(dialog.ShowDialog(this)!=true)return;
        try
        {
            var template=DocumentStore.Load<TemplateDocument>(dialog.FileName);
            if(MessageBox.Show(this,$"선택한 {targets.Count}개 파일의 영역·타임라인과 영상 옵션을 교체할까요?","템플릿 적용",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
            foreach(var job in targets){job.Regions=LayerGraph.Copy(template.Regions);job.Playback=template.Playback with{};job.Status="템플릿 적용";job.Progress=0;}
            _region=null;_clip=null;_video=template.Video;ShowVideo();DisplayJob(_current);MarkEdited(true);
        }catch(Exception ex){Error(ex);}
    }
    private void ChooseOutput(object sender,RoutedEventArgs e)
    {if(_busy)return;var dialog=new OpenFolderDialog{Title="출력 폴더 · WebM / OBS 패키지"};if(dialog.ShowDialog(this)==true){OutputFolderBox.Text=dialog.FolderName;_dirty=true;}}
    private void OutputFolderChanged(object sender,TextChangedEventArgs e){if(!_loading)_dirty=true;}

    private async void ExportFiles(object sender,RoutedEventArgs e)=>await ExportAsync(ExportTargets());
    private async void RetryFailed(object sender,RoutedEventArgs e)=>await ExportAsync(_jobs.Where(j=>j.Status=="실패").ToList());
    private async Task ExportAsync(List<ImageJob> jobs)
    {
        if(_busy||!CommitClip(true)||!CommitRegion(true)||!CommitMask(true)||!CommitVideo(true)||!CommitOutputTiming())return;
        if(jobs.Count==0){Notify("출력할 PNG가 없습니다.");return;}
        try
        {
            WebmExporter.ValidateOutputNames(jobs);
            if(string.IsNullOrWhiteSpace(OutputFolderBox.Text)){ChooseOutput(this,new RoutedEventArgs());if(string.IsNullOrWhiteSpace(OutputFolderBox.Text))return;}
            var output=Path.GetFullPath(OutputFolderBox.Text.Trim());Directory.CreateDirectory(output);
            var video=_video with{};var kinds=video.Output==OutputKind.Both?new[]{OutputKind.Webm,OutputKind.Browser}:new[]{video.Output};
            var encoder=kinds.Contains(OutputKind.Webm)?RuntimePaths.Ffmpeg:"";var snapshots=jobs.Select(j=>j.Snapshot()).ToList();
            string Target(ImageJob j,OutputKind kind)=>Path.Combine(output,kind==OutputKind.Browser?BrowserExporter.OutputName(j):j.OutputName);
            bool Exists(string path)=>File.Exists(path)||Directory.Exists(path);
            if(jobs.Any(j=>j.Regions.All(r=>r.Clips.Count==0))&&MessageBox.Show(this,"효과가 없는 파일은 정지 영상으로 출력합니다. 계속할까요?","출력 확인",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
            var overwrite=false;var existing=jobs.Sum(j=>kinds.Count(k=>Exists(Target(j,k))));
            if(existing>0)
            {
                var result=MessageBox.Show(this,$"같은 이름 {existing}개: 예=덮어쓰기 / 아니요=건너뛰기 / 취소=중단","파일 충돌",MessageBoxButton.YesNoCancel);
                if(result==MessageBoxResult.Cancel)return;overwrite=result==MessageBoxResult.Yes;
            }
            foreach(var snapshot in snapshots){var plan=PlaybackPlan.Create(snapshot,video);if(kinds.Contains(OutputKind.Webm))plan.ValidateWebm();if(kinds.Contains(OutputKind.Browser)&&(!Exists(Target(snapshot,OutputKind.Browser))||overwrite))BrowserExporter.CheckTarget(Target(snapshot,OutputKind.Browser),overwrite);}
            SetBusy(true);StopPlayback();_workCts=new();var token=_workCts.Token;var failures=0;var completed=0;
            try
            {
                for(var i=0;i<jobs.Count;i++)
                {
                    token.ThrowIfCancellationRequested();var job=jobs[i];var snapshot=snapshots[i];var index=i;var errors=new List<string>();var written=new List<string>();var skipped=0;
                    for(var k=0;k<kinds.Length;k++)
                    {
                        token.ThrowIfCancellationRequested();var kind=kinds[k];var part=k;var target=Target(snapshot,kind);
                        if(Exists(target)&&!overwrite){skipped++;continue;}
                        job.Status="출력 중";Notify($"{i+1}/{jobs.Count} · {Path.GetFileName(target)}");
                        var progress=new Progress<double>(p=>{job.Progress=(part+p)/kinds.Length*100;ExportProgress.Value=(index+(part+p)/kinds.Length)/jobs.Count*100;ProgressText.Text=$"{index+1}/{jobs.Count} · {kind}";});
                        try
                        {
                            await Task.Run(()=>kind==OutputKind.Browser?new BrowserExporter().ExportAsync(snapshot,video,target,overwrite,progress,token):new WebmExporter(encoder).ExportAsync(snapshot,video,target,overwrite,progress,token),token);
                            written.Add(target);
                        }
                        catch(Exception ex)when(ex is not OperationCanceledException){errors.Add(Path.GetFileName(target)+": "+ex.Message);}
                    }
                    job.Progress=100;job.Detail=string.Join("\n",written.Concat(errors));
                    if(errors.Count>0){job.Status="실패";failures++;}else if(skipped==kinds.Length)job.Status="건너뜀";else{job.Status="출력 완료";completed++;}
                }
                ExportProgress.Value=100;ProgressText.Text=$"완료 {completed} · 실패 {failures}";Notify($"출력 종료 · {output}");
                if(failures>0)MessageBox.Show(this,string.Join("\n",jobs.Where(j=>j.Status=="실패").Take(4).Select(j=>j.FileName+": "+j.Detail)),"실패 항목 · 재시도 가능");
            }
            catch(OperationCanceledException){foreach(var job in jobs.Where(j=>j.Status=="출력 중")){job.Status="취소";job.Detail="완료된 파일과 기존 출력은 유지합니다.";}Notify("출력을 취소했습니다.");}
            finally{_workCts.Dispose();_workCts=null;SetBusy(false);}
        }
        catch(Exception ex){Error(ex);}
    }
    private void CancelExport(object sender,RoutedEventArgs e)=>_workCts?.Cancel();
    private void WindowClosing(object? sender,CancelEventArgs e)
    {
        if(_busy){e.Cancel=true;_workCts?.Cancel();Notify("현재 작업을 취소하고 있습니다. 멈춘 뒤 다시 닫으세요.");return;}
        if(!ConfirmStudiosClose()||!ConfirmDiscard()){e.Cancel=true;return;}
        CloseWorkspace();_clock.Stop();(Resources["LayerThumbnail"] as IDisposable)?.Dispose();Preview.Dispose();foreach(var t in _thumbnails)t.Dispose();
    }
    private void ShowHelp(object sender,RoutedEventArgs e)=>MessageBox.Show(this,
        "1. 도형은 다음 그리기에만 적용됩니다. 기존 도형은 선택 도형 변경, 펜/자유손은 점 편집으로 점 이동·선분 추가·Delete 삭제합니다.\n"+
        "2. 편집 크게는 타임라인을 접습니다. 지정 시 크게를 켜면 자동 확대 배치합니다. 창 분리는 별도 창, 닫기는 복귀입니다.\n"+
        "3. 영역 빠른 조절: 소프트·내용만·배경색. 정밀 분리·보정은 현재 마스크에서 이어서 편집, 범위 맞춤은 선택 옵션. 브러시 [ / ] 1px, Shift+[ / ] 10px.\n"+
        "4. 위 행이 위 레이어입니다. 자동 잘라내기는 해제했습니다. 큰 영역은 그룹, 안쪽 배지 등은 그룹 안 효과 영역으로 연결하세요.\n"+
        "5. 내 효과 > 새 모션에서 프레임별 위치·크기·회전·불투명도·기준점을 편집합니다. 불투명도 0은 숨김, 100은 표시입니다.\n"+
        "6. 제작실의 키와 기존 효과 조합을 에셋 저장 후 삽입하세요. 마우스 이동·크기·회전은 현재 프레임에 자동 키를 만듭니다.\n"+
        "7. Ctrl+클릭은 효과 다중 선택/해제. 함께 이동·Delete 삭제·Ctrl+C/V 복사/붙여넣기·Ctrl+Z/Ctrl+Shift+Z 되돌리기/다시.\n"+
        "8. 직접 배경색은 중심 픽셀 확대경의 스포이드 또는 HSV/RGBA 색상표로 고릅니다. 사용자색16칸을 저장할 수 있습니다.\n"+
        "9. 뒤 클립 자석은 다음 블록을 새 끝에 붙입니다. ✂ 분할/Ctrl+K는 현재 커서에서 나누고 원래 모션·루프 진행을 유지합니다.\n"+
        "10. 왼쪽에서 출력 폴더·화질·범위를 정합니다. 휠·+/− 확대, Space/중간 버튼 이동. 현재 옵션은 기본값으로 저장할 수 있습니다.\n\n"+
        "숫자 롤링은 숫자만 지정하세요. 원 단위 보호는 보류입니다. 복잡한 배경과 가려진 사진은 완벽히 복원하지 못합니다.\n"+
        "11. 그룹·마스크에서 마스크를 그리면 타임라인 하위 행이 생깁니다. 더하기/빼기/교차·반전과 시간·경계·불투명도를 조정합니다.\n"+
        "12. 마스크 편집은 휠/+−/100%/맞춤·Space 이동과 본체 이동·8핸들 크기·위 원 회전·기준점 도구를 제공합니다.\n"+
        "13. 속성 슬라이더는 즉시 반영, 값의 ↔ 부분 좌우 드래그는 세밀 조절, Shift는1/10입니다. 정밀 입력은 펼쳐 사용합니다.\n"+
        "14. 새 마스크는 부모 효과 구간만큼 만들고 양끝을 드래그합니다. 구간 후 원본 복귀가 기본이며 기존 마스크 설정은 유지합니다.\n"+
        "15. 구간 설정을 적용한 뒤 색상 바의 경계를 드래그합니다. 방송 재생은 등장 한 번·강조 반복·퇴장 버튼 순서입니다.\n"+
        "16. 좌측 출력 형식에서 WebM/OBS 브라우저/둘 다를 고릅니다. WebM은 지정 횟수 반복, 브라우저는 무한 반복합니다.\n"+
        "17. OBS 브라우저 소스에 index.html을 넣고 원본 크기/FPS로 설정합니다. 상호작용 R=재등장, E=퇴장, Space=일시정지입니다.\n"+
        "원본 유지 배경은 의도적인 원본 잔상을 남깁니다. 원본 크기·파일명·투명·무음을 유지합니다. 사용안내/README.md를 참조하세요.\n\n"+ProductInfo.Credit,"사용법 · "+ProductInfo.Credit);
}
