using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LiveCG.Core;
using SkiaSharp;

namespace LiveCG.App;

public sealed class EffectStudioWindow:Window
{
    public EffectAsset Asset { get; private set; }
    public MotionTimelineControl Timeline { get; }=new();
    public StudioPreviewControl Preview { get; }
    public MouseValueControl PropertyMouse { get; private set; }=null!;
    public MouseValueControl LengthMouse { get; private set; }=null!;
    public event Action<EffectAsset>? Saved;
    public event Action<EffectAsset>? InsertRequested;
    private readonly TextBox _name=new(){Width=200},_category=new(){Width=120},_description=new(){Width=220};
    private readonly ComboBox _property=new(),_easing=new(),_fps=new(){IsEditable=true},_kind=new(),_strength=new();
    private readonly TextBox _frameBox=new(),_value=new(),_length=new(),_start=new(),_duration=new(),_speed=new(),_period=new();
    private readonly CheckBox _preserveTime=new(){Content="fps 변경 시 시간 유지",IsChecked=true},_autoKey=new(){Content="드래그로 자동 키",IsChecked=true},_showPath=new(){Content="이동 경로",IsChecked=true},_loopClip=new(){Content="선택 클립 반복"},_loopPreview=new(){Content="미리보기 반복",IsChecked=true};
    private readonly ListBox _clips=new(){Height=125,DisplayMemberPath="Label"};private readonly TextBlock _info=new(){TextWrapping=TextWrapping.Wrap},_timeLabel=new();private readonly Slider _scrub=new(){Minimum=0};
    private readonly EditHistory _history=new();private readonly DispatcherTimer _timer=new(){Interval=TimeSpan.FromMilliseconds(25)};private readonly Stopwatch _clock=new();
    private EffectClip _motionClip=null!,_selectedClip=null!;private List<(MotionProperty Property,MotionKeyframe Key)> _clipboard=[];
    private bool _loading=true,_gesture,_playing,_closeApproved;private string _savedJson="";private EditorState? _beforeGesture;private double _time,_playStart;
    private MotionDefinition Motion=>_motionClip.Motion!;
    public int CurrentFrame=>Math.Clamp((int)Math.Round((_time-_motionClip.Start)*Motion.Fps),0,Motion.LengthFrames);
    public bool IsDirty=>State().Json!=_savedJson;
    public EffectStudioWindow(EffectAsset asset,SKBitmap? sample=null)
    {
        Asset=asset.Copy();EnsureMotion();_selectedClip=_motionClip;Title="효과 제작실 · "+ProductInfo.Credit;Width=1370;Height=940;MinWidth=960;MinHeight=700;WindowStartupLocation=WindowStartupLocation.CenterOwner;Background=new SolidColorBrush(Color.FromRgb(12,21,32));Foreground=Brushes.White;
        Preview=new(Asset,sample);var root=new DockPanel{Margin=new Thickness(12)};Content=root;
        var top=new WrapPanel();DockPanel.SetDock(top,Dock.Top);root.Children.Add(top);
        top.Children.Add(new TextBlock{Text="이름"});top.Children.Add(_name);top.Children.Add(new TextBlock{Text="분류"});top.Children.Add(_category);top.Children.Add(new TextBlock{Text="설명"});top.Children.Add(_description);
        AddButton(top,"에셋 저장",()=>SaveAsset());AddButton(top,"저장하고 삽입",()=>{if(SaveAsset())InsertRequested?.Invoke(Asset.Copy());});AddButton(top,"예제 모션",()=>{Stop();_motionClip.Motion=MotionDefinition.Example();_motionClip.Duration=2;Record();RefreshAll();});
        var footer=new DockPanel{Margin=new Thickness(4,6,4,0)};DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);var credit=new TextBlock{Text=ProductInfo.Credit,HorizontalAlignment=HorizontalAlignment.Right};DockPanel.SetDock(credit,Dock.Right);footer.Children.Add(credit);footer.Children.Add(_info);
        var body=new Grid();body.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});body.ColumnDefinitions.Add(new(){Width=new GridLength(10)});body.ColumnDefinitions.Add(new(){Width=new GridLength(285)});
        body.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star),MinHeight=180});body.RowDefinitions.Add(new(){Height=new GridLength(7)});body.RowDefinitions.Add(new(){Height=new GridLength(330),MinHeight=180});root.Children.Add(body);
        var previewPanel=new DockPanel{Background=new SolidColorBrush(Color.FromRgb(23,33,45))};body.Children.Add(previewPanel);
        var tools=new WrapPanel{Margin=new Thickness(3)};DockPanel.SetDock(tools,Dock.Bottom);previewPanel.Children.Add(tools);
        AddButton(tools,"재생 / 정지",TogglePlay);AddButton(tools,"처음",()=>SeekTime(0));AddButton(tools,"−1f",()=>SetFrame(CurrentFrame-1));AddButton(tools,"＋1f",()=>SetFrame(CurrentFrame+1));AddButton(tools,"＋10f",()=>SetFrame(CurrentFrame+10));AddButton(tools,"화면 맞춤",Preview.Fit);tools.Children.Add(_autoKey);tools.Children.Add(_showPath);tools.Children.Add(_loopPreview);tools.Children.Add(_timeLabel);
        DockPanel.SetDock(_scrub,Dock.Bottom);previewPanel.Children.Add(_scrub);previewPanel.Children.Add(Preview);
        var splitter=new GridSplitter{Height=5,HorizontalAlignment=HorizontalAlignment.Stretch,Background=Brushes.DimGray,ResizeDirection=GridResizeDirection.Rows,ResizeBehavior=GridResizeBehavior.PreviousAndNext};Grid.SetRow(splitter,1);body.Children.Add(splitter);
        var timelinePanel=new DockPanel();Grid.SetRow(timelinePanel,2);body.Children.Add(timelinePanel);var timelineTools=new WrapPanel();DockPanel.SetDock(timelineTools,Dock.Top);timelinePanel.Children.Add(timelineTools);
        AddButton(timelineTools,"키 추가/적용",AddKeyFromFields);AddButton(timelineTools,"키 삭제",DeleteSelectedKeys);AddButton(timelineTools,"복사",CopyKeys);AddButton(timelineTools,"붙여넣기",PasteKeys);AddButton(timelineTools,"되돌리기",Undo);AddButton(timelineTools,"다시",Redo);AddButton(timelineTools,"−",()=>Timeline.Zoom(.8));AddButton(timelineTools,"＋",()=>Timeline.Zoom(1.25));
        timelinePanel.Children.Add(new ScrollViewer{Content=Timeline,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        var fields=new StackPanel{Margin=new Thickness(4)};var scroll=new ScrollViewer{Content=fields,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};Grid.SetColumn(scroll,2);Grid.SetRowSpan(scroll,3);body.Children.Add(scroll);
        Label(fields,"키프레임 모션");Label(fields,"제작 fps");foreach(var fps in new[]{24,30,60,120})_fps.Items.Add(fps.ToString());fields.Children.Add(_fps);fields.Children.Add(_preserveTime);
        Label(fields,"선택 모션 길이 (프레임)");fields.Children.Add(_length);AddButton(fields,"길이 / fps 적용",ApplyTiming);
        Label(fields,"속성");foreach(var p in Enum.GetValues<MotionProperty>())_property.Items.Add(MotionDefinition.Name(p));fields.Children.Add(_property);
        Label(fields,"프레임");fields.Children.Add(_frameBox);Label(fields,"값 — 위치·크기·불투명도·기준점은 %, 회전은 °");fields.Children.Add(_value);
        Label(fields,"다음 키까지 움직임");foreach(var text in new[]{"일정한 속도","서서히 시작","서서히 정지","부드러운 시작/정지","순간 변경"})_easing.Items.Add(text);fields.Children.Add(_easing);AddButton(fields,"키 추가 / 선택 키 수정",AddKeyFromFields);
        Label(fields,"효과 조합 — 선택한 모션의 프레임을 편집합니다");fields.Children.Add(_clips);
        var native=new WrapPanel();fields.Children.Add(native);AddButton(native,"효과 추가",AddNative);AddButton(native,"클립 복제",DuplicateNative);AddButton(native,"클립 삭제",DeleteNative);
        Label(fields,"효과 종류");foreach(var kind in Enum.GetValues<EffectKind>())_kind.Items.Add(ProductInfo.EffectName(kind));fields.Children.Add(_kind);
        AddButton(fields,"장식 색상",()=>{var picker=new ColorPickerWindow(_selectedClip.Options.Color is { } color?SKColor.Parse(color):ProductInfo.DefaultEffectColor(_selectedClip.Options.Kind)){Owner=this};if(picker.ShowDialog()==true){_selectedClip.Options=_selectedClip.Options with{Color=ColorPickerWindow.Hex(picker.SelectedColor)};Record();RefreshAll();}});
        AddButton(fields,"장식 기본색 복원",()=>{_selectedClip.Options=_selectedClip.Options with{Color=null};Record();RefreshAll();});
        var times=new System.Windows.Controls.Primitives.UniformGrid{Columns=2};fields.Children.Add(times);Label(times,"시작(초)");Label(times,"길이(초)");times.Children.Add(_start);times.Children.Add(_duration);
        Label(fields,"강도 / 속도");foreach(var text in ProductInfo.Strengths.Select(ProductInfo.StrengthName))_strength.Items.Add(text);fields.Children.Add(_strength);fields.Children.Add(_speed);fields.Children.Add(_loopClip);Label(fields,"반복 주기(초)");fields.Children.Add(_period);AddButton(fields,"선택 클립 속성 적용",ApplyClip);
        PropertyMouse=MouseValueControl.Attach(_value,-2000,2000,"%",v=>{if(!_loading)SetKey(Timeline.SelectedProperty,CurrentFrame,v/(Timeline.SelectedProperty==MotionProperty.Rotation?1:100),(KeyEasing)Math.Max(0,_easing.SelectedIndex));},BeginGesture,EndGesture);
        LengthMouse=MouseValueControl.Attach(_length,1,9000,"f",v=>{if(_loading)return;var copy=Motion.Copy();var length=Math.Clamp((int)Math.Round(v),1,copy.Fps*300);var ratio=(double)length/copy.LengthFrames;foreach(var track in copy.Tracks){foreach(var key in track.Keys)key.Frame=(int)Math.Round(key.Frame*ratio);track.Keys=track.Keys.GroupBy(k=>k.Frame).Select(g=>g.Last()).ToList();}copy.LengthFrames=length;_motionClip.Motion=copy;_motionClip.Duration=copy.Duration;_time=Math.Min(_time,Asset.Duration);Record();RefreshAll();},BeginGesture,EndGesture);
        void MouseClip(TextBox box,double min,double max,string unit){MouseValueControl.Attach(box,min,max,unit,v=>{if(_loading)return;box.Text=F(v);ApplyClip();},BeginGesture,EndGesture);}
        MouseClip(_start,0,60,"초");MouseClip(_duration,1d/60,30,"초");MouseClip(_speed,.25,4,"배");MouseClip(_period,1d/60,30,"초");
        _name.Text=Asset.Name;_category.Text=Asset.Category;_description.Text=Asset.Description;
        foreach(var box in new[]{_name,_category,_description})box.LostFocus+=(_,_)=>{if(!_loading){CommitMetadata();Record();}};
        _property.SelectionChanged+=(_,_)=>{if(!_loading){Timeline.SelectedProperty=(MotionProperty)Math.Max(0,_property.SelectedIndex);Timeline.SelectedKeys.Clear();LoadKey();Timeline.InvalidateVisual();}};
        _clips.SelectionChanged+=(_,_)=>{if(!_loading&&_clips.SelectedItem is EffectClip clip){_selectedClip=clip;if(clip.Motion is not null)_motionClip=clip;RefreshAll();}};
        Timeline.Seeked+=SetFrame;Timeline.SelectionChanged+=()=>{var prior=_loading;_loading=true;_property.SelectedIndex=(int)Timeline.SelectedProperty;_loading=prior;LoadKey();};Timeline.Edited+=()=>{LoadKey();UpdatePreview();};Timeline.GestureStarted+=BeginGesture;Timeline.GestureEnded+=EndGesture;
        Timeline.AddRequested+=(property,frame)=>SetKey(property,frame,Motion.Value(property,frame),Motion.Tracks.FirstOrDefault(t=>t.Property==property)?.Keys.FirstOrDefault(k=>k.Frame==frame)?.Easing??KeyEasing.EaseInOut);Timeline.DeleteRequested+=DeleteSelectedKeys;
        Preview.GestureStarted+=BeginGesture;Preview.GestureEnded+=EndGesture;Preview.ValuesEdited+=values=>{if(_autoKey.IsChecked!=true)return;foreach(var item in values)Motion.SetKey(item.Key,CurrentFrame,item.Value);var property=values.ContainsKey(MotionProperty.AnchorX)?MotionProperty.AnchorX:values.ContainsKey(MotionProperty.Rotation)?MotionProperty.Rotation:values.ContainsKey(MotionProperty.ScaleX)?MotionProperty.ScaleX:MotionProperty.X;Timeline.SelectedProperty=property;var prior=_loading;_loading=true;_property.SelectedIndex=(int)property;_loading=prior;Timeline.Refresh();LoadKey();UpdatePreview();};
        _autoKey.Checked+=(_,_)=>Preview.EditEnabled=true;_autoKey.Unchecked+=(_,_)=>Preview.EditEnabled=false;_showPath.Checked+=(_,_)=>{Preview.ShowPath=true;UpdatePreview();};_showPath.Unchecked+=(_,_)=>{Preview.ShowPath=false;UpdatePreview();};
        _scrub.ValueChanged+=(_,_)=>{if(!_loading)SeekTime(_scrub.Value/Motion.Fps);};
        _frameBox.KeyDown+=(_,e)=>{if(e.Key==Key.Enter&&int.TryParse(_frameBox.Text,out var frame)){SetFrame(frame);e.Handled=true;}};_value.KeyDown+=(_,e)=>{if(e.Key==Key.Enter){AddKeyFromFields();e.Handled=true;}};
        PreviewKeyDown+=KeyDownEditor;AddKeyMenu();_timer.Tick+=(_,_)=>{if(!_playing)return;var time=_playStart+_clock.Elapsed.TotalSeconds;if(time>Asset.Duration){if(_loopPreview.IsChecked==true)time%=Asset.Duration;else{time=Asset.Duration;Stop();}}SeekTime(time,false);};_timer.Start();
        Closing+=(_,e)=>{if(!ConfirmClose())e.Cancel=true;};Closed+=(_,_)=>{_timer.Stop();Preview.Dispose();};_loading=false;RefreshAll();_savedJson=State().Json;_history.Reset(State());
    }
    private static void AddButton(Panel panel,string label,Action action){var button=new Button{Content=label,Padding=new Thickness(8,5,8,5)};button.Click+=(_,_)=>action();panel.Children.Add(button);}
    private static void Label(Panel panel,string text)=>panel.Children.Add(new TextBlock{Text=text,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(3,8,2,3),Foreground=new SolidColorBrush(Color.FromRgb(174,198,213))});
    private static string F(double number)=>number.ToString("0.###",CultureInfo.InvariantCulture);
    private static double Number(string text){if(double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out var n)&&double.IsFinite(n))return n;throw new InvalidDataException("올바른 숫자를 입력하세요.");}
    private void EnsureMotion()
    {
        _motionClip=Asset.Clips.FirstOrDefault(c=>c.Options.Kind==EffectKind.UserMotion&&c.Motion is not null)!;
        if(_motionClip is null){var length=Math.Max(1,Asset.Duration);_motionClip=new(){Duration=length,Options=new(){Kind=EffectKind.UserMotion},Motion=new(){LengthFrames=(int)Math.Ceiling(length*30)}};Asset.Clips.Insert(0,_motionClip);}
    }
    private EditorState State()=>new(JsonSerializer.Serialize(new EffectAsset{Id=Asset.Id,Name=Asset.Name,Category=Asset.Category,Description=Asset.Description,Favorite=Asset.Favorite,Clips=Asset.Clips}),null,null,_selectedClip?.Id,_time);
    private void Record(){if(_loading||_gesture)return;_history.Record(State());}
    private void BeginGesture(){Stop();Record();_beforeGesture=State();_gesture=true;}
    private void EndGesture(bool commit){_gesture=false;if(commit)Record();else if(_beforeGesture is not null)Restore(_beforeGesture);_beforeGesture=null;RefreshAll();}
    private void Restore(EditorState state)
    {Asset=JsonSerializer.Deserialize<EffectAsset>(state.Json)!;EnsureMotion();_selectedClip=Asset.Clips.FirstOrDefault(c=>c.Id==state.Clip)??_motionClip;if(_selectedClip.Motion is not null)_motionClip=_selectedClip;_time=state.Time;RefreshAll();}
    public void Undo(){Stop();if(_history.Undo() is { } state)Restore(state);}
    public void Redo(){Stop();if(_history.Redo() is { } state)Restore(state);}
    private void RefreshAll()
    {
        _loading=true;_name.Text=Asset.Name;_category.Text=Asset.Category;_description.Text=Asset.Description;Timeline.Motion=Motion;_fps.Text=Motion.Fps.ToString();_length.Text=Motion.LengthFrames.ToString();_clips.ItemsSource=Asset.Clips.OrderBy(c=>c.Start).ToList();_clips.SelectedItem=_selectedClip;
        _kind.SelectedIndex=(int)_selectedClip.Options.Kind;_start.Text=F(_selectedClip.Start);_duration.Text=F(_selectedClip.Duration);_strength.SelectedIndex=ProductInfo.StrengthIndex(_selectedClip.Options.Strength);_speed.Text=F(_selectedClip.Options.Speed);_loopClip.IsChecked=_selectedClip.Loop;_period.Text=F(_selectedClip.LoopPeriod);_property.SelectedIndex=(int)Timeline.SelectedProperty;
        _scrub.Maximum=Math.Max(1,Math.Ceiling(Asset.Duration*Motion.Fps));_scrub.Value=Math.Clamp(_time*Motion.Fps,0,_scrub.Maximum);_loading=false;Timeline.Refresh();LoadKey();UpdatePreview();
    }
    private void LoadKey()
    {
        var previous=_loading;_loading=true;var key=Motion.Tracks.SelectMany(t=>t.Keys).FirstOrDefault(k=>Timeline.SelectedKeys.Contains(k.Id));
        _frameBox.Text=(key?.Frame??CurrentFrame).ToString();var property=Timeline.SelectedProperty;PropertyMouse?.Configure(property==MotionProperty.Rotation?-360:(MotionDefinition.IsUnit(property)||property is MotionProperty.ScaleX or MotionProperty.ScaleY)?0:-2000,property==MotionProperty.Rotation?360:MotionDefinition.IsUnit(property)?100:2000,property==MotionProperty.Rotation?"°":"%");var value=key?.Value??Motion.Value(property,CurrentFrame);_value.Text=F(property==MotionProperty.Rotation?value:value*100);_easing.SelectedIndex=(int)(key?.Easing??KeyEasing.EaseInOut);_loading=previous;
    }
    private void UpdatePreview(){Preview.Asset=Asset;Preview.Motion=Motion;Preview.Frame=CurrentFrame;Preview.Time=_time;Preview.InvalidateVisual();Timeline.CursorFrame=CurrentFrame;Timeline.InvalidateVisual();_timeLabel.Text=$"{Math.Round(_time*Motion.Fps)}f / {Math.Ceiling(Asset.Duration*Motion.Fps)}f · {F(_time)}초";}
    public void SetFrame(int frame){Stop();frame=Math.Clamp(frame,0,Motion.LengthFrames);if(Motion.Tracks.SelectMany(t=>t.Keys).Any(k=>Timeline.SelectedKeys.Contains(k.Id)&&k.Frame!=frame))Timeline.SelectedKeys.Clear();SeekTime(_motionClip.Start+(double)frame/Motion.Fps);}
    private void SeekTime(double time,bool stop=true){if(stop)Stop();_time=Math.Clamp(time,0,Math.Max(.016,Asset.Duration));var prior=_loading;_loading=true;_scrub.Value=_time*Motion.Fps;_loading=prior;LoadKey();UpdatePreview();}
    private void Stop(){_playing=false;_clock.Stop();}
    private void TogglePlay(){if(_playing){Stop();return;}if(_time>=Asset.Duration)_time=0;_playStart=_time;_clock.Restart();_playing=true;}
    public void SetKey(MotionProperty property,int frame,double value,KeyEasing easing)
    {try{Stop();Motion.SetKey(property,frame,value,easing);Timeline.SelectedProperty=property;Timeline.SelectedKeys.Clear();Timeline.SelectedKeys.Add(Motion.Tracks.First(t=>t.Property==property).Keys.First(k=>k.Frame==Math.Clamp(frame,0,Motion.LengthFrames)).Id);_time=_motionClip.Start+(double)frame/Motion.Fps;Record();RefreshAll();}catch(Exception ex){_info.Text=ex.Message;}}
    private void AddKeyFromFields(){try{var property=Timeline.SelectedProperty;var frame=checked((int)Number(_frameBox.Text));var value=Number(_value.Text);SetKey(property,frame,property==MotionProperty.Rotation?value:value/100,(KeyEasing)Math.Max(0,_easing.SelectedIndex));}catch(Exception ex){_info.Text=ex.Message;}}
    public void DeleteSelectedKeys(){foreach(var track in Motion.Tracks)track.Keys.RemoveAll(k=>Timeline.SelectedKeys.Contains(k.Id));Timeline.SelectedKeys.Clear();Record();RefreshAll();}
    public void CopyKeys(){var keys=Motion.Tracks.SelectMany(t=>t.Keys.Where(k=>Timeline.SelectedKeys.Contains(k.Id)).Select(k=>(t.Property,Key:k with{}))).ToList();if(keys.Count==0)return;var first=keys.Min(k=>k.Key.Frame);_clipboard=keys.Select(k=>(k.Property,k.Key with{Frame=k.Key.Frame-first})).ToList();_info.Text=$"{_clipboard.Count}개 키 복사됨";}
    public void PasteKeys(){try{if(_clipboard.Any(k=>k.Key.Frame+CurrentFrame>Motion.LengthFrames))throw new InvalidDataException("붙여넣을 키가 모션 길이를 넘습니다.");foreach(var item in _clipboard)Motion.SetKey(item.Property,item.Key.Frame+CurrentFrame,item.Key.Value,item.Key.Easing);Record();RefreshAll();}catch(Exception ex){_info.Text=ex.Message;}}
    public void ChangeTiming(int fps,int frames,bool preserveTime)
    {
        var candidate=Motion.Copy();var oldLength=candidate.LengthFrames;if(fps!=candidate.Fps)candidate.ChangeFps(fps,preserveTime);if(fps==Motion.Fps||frames!=oldLength)candidate.LengthFrames=frames;candidate.Validate();_motionClip.Motion=candidate;_motionClip.Duration=candidate.Duration;Record();RefreshAll();
    }
    private void ApplyTiming(){try{ChangeTiming(checked((int)Number(_fps.Text)),checked((int)Number(_length.Text)),_preserveTime.IsChecked==true);}catch(Exception ex){_info.Text=ex.Message;}}
    private void AddNative(){var clip=new EffectClip{Start=Asset.Duration,Duration=1,Options=new(){Kind=EffectKind.Sweep}};if(clip.End>300){_info.Text="에셋 길이는 최대300초입니다.";return;}Asset.Clips.Add(clip);_selectedClip=clip;Record();RefreshAll();}
    private void DuplicateNative(){try{var clip=_selectedClip.Copy();if(clip.SequenceId is not null)clip.SequenceId=Guid.NewGuid();clip.Start=Asset.Duration;clip.Validate();if(clip.End>300)throw new InvalidDataException("에셋 최대 길이는300초입니다.");Asset.Clips.Add(clip);_selectedClip=clip;Record();RefreshAll();}catch(Exception ex){_info.Text=ex.Message;}}
    private void DeleteNative(){if(Asset.Clips.Count<=1)return;Asset.Clips.Remove(_selectedClip);EnsureMotion();_selectedClip=_motionClip;Record();RefreshAll();}
    private void ApplyClip()
    {
        try
        {
            var clip=_selectedClip.Copy(false);clip.Start=Number(_start.Text);clip.Duration=Number(_duration.Text);clip.Options=clip.Options with{Kind=(EffectKind)_kind.SelectedIndex,Strength=ProductInfo.StrengthAt(_strength.SelectedIndex),Speed=Number(_speed.Text)};clip.Loop=_loopClip.IsChecked==true;clip.LoopPeriod=Number(_period.Text);
            if(clip.Options.Kind==EffectKind.UserMotion){clip.Motion??=new();clip.Motion.LengthFrames=Math.Max(1,(int)Math.Round(clip.Duration*clip.Motion.Fps));}else clip.Motion=null;
            clip.Validate();if(clip.End>300)throw new InvalidDataException("에셋 최대 길이는300초입니다.");var index=Asset.Clips.IndexOf(_selectedClip);Asset.Clips[index]=clip;_selectedClip=clip;EnsureMotion();if(clip.Motion is not null)_motionClip=clip;Record();RefreshAll();
        }catch(Exception ex){_info.Text=ex.Message;}
    }
    private void CommitMetadata(){Asset.Name=_name.Text.Trim();Asset.Category=_category.Text.Trim();Asset.Description=_description.Text.Trim();}
    public bool SaveAsset(){try{CommitMetadata();Asset.Validate();Asset.ThumbnailPng=EffectSample.Thumbnail(Asset);AssetStore.Save(Asset);_savedJson=State().Json;Record();Saved?.Invoke(Asset.Copy());_info.Text="내 효과 에셋 저장 완료";return true;}catch(Exception ex){_info.Text=ex.Message;return false;}}
    public bool ConfirmClose(){CommitMetadata();if(_closeApproved||!IsDirty)return true;var answer=MessageBox.Show(this,"제작 중인 효과 에셋을 저장할까요?","효과 제작실",MessageBoxButton.YesNoCancel);if(answer==MessageBoxResult.Cancel)return false;if(answer==MessageBoxResult.Yes&&!SaveAsset())return false;_closeApproved=true;return true;}
    public void DiscardAndClose(){_closeApproved=true;Close();}
    private void AddKeyMenu(){var menu=new ContextMenu();void Add(string label,Action action){var item=new MenuItem{Header=label};item.Click+=(_,_)=>action();menu.Items.Add(item);}Add("키 복사",CopyKeys);Add("커서에 붙여넣기",PasteKeys);Add("키 삭제",DeleteSelectedKeys);Add("되돌리기",Undo);Add("다시 실행",Redo);Timeline.ContextMenu=menu;}
    private void KeyDownEditor(object sender,KeyEventArgs e)
    {
        if(MainWindow.IsTextEditing(Keyboard.FocusedElement as DependencyObject))return;var modifiers=Keyboard.Modifiers;
        if(modifiers==ModifierKeys.Control){if(e.Key==Key.Z)Undo();else if(e.Key==Key.Y)Redo();else if(e.Key==Key.C)CopyKeys();else if(e.Key==Key.V)PasteKeys();else return;e.Handled=true;}
        else if(modifiers==(ModifierKeys.Control|ModifierKeys.Shift)&&e.Key==Key.Z){Redo();e.Handled=true;}
        else if(e.Key is Key.Left or Key.Right){SetFrame(CurrentFrame+(e.Key==Key.Left?-1:1)*(modifiers.HasFlag(ModifierKeys.Shift)?10:1));e.Handled=true;}
        else if(e.Key==Key.Home){SetFrame(0);e.Handled=true;}else if(e.Key==Key.End){SetFrame(Motion.LengthFrames);e.Handled=true;}
        else if(e.Key==Key.Delete){DeleteSelectedKeys();e.Handled=true;}
    }
}
