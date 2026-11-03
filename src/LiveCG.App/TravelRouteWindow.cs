using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using LiveCG.Core;
using SkiaSharp;
namespace LiveCG.App;
public sealed class TravelRouteWindow:Window
{
    public TravelRoute Route { get; private set; }
    public string AssetName=>string.IsNullOrWhiteSpace(_assetName.Text)?"내 여행 경로":_assetName.Text.Trim();
    public event Action<TravelRoute>? SaveAssetRequested;
    public RouteCanvas Canvas { get; }
    public ListBox PointsList { get; }=new(){Height=145};
    public Slider TimeSlider { get; }=new(){Minimum=0};
    public MouseValueControl TotalTime { get; }=new();
    public TextBox? StopExactBox { get; private set; }
    private readonly int _fps;
    private readonly StackPanel _pointSettings=new(),_styleSettings=new();
    private readonly TextBlock _info=new(){TextWrapping=TextWrapping.Wrap},_clockLabel=new();
    private readonly TextBox _assetName=new(){Text="내 여행 경로",Width=160};
    private readonly List<TravelRoute> _undo=[],_redo=[];
    private TravelRoute _accepted;private bool _sync,_playing,_numberGesture;private TravelRoute? _numberBefore;
    private readonly DispatcherTimer _timer=new(){Interval=TimeSpan.FromMilliseconds(33)};
    private readonly Stopwatch _clock=new();private double _playStart;private string? _error;
    private MouseValueControl? _stopValue;private Button _apply=new(),_save=new();
    public TravelRouteWindow(SKBitmap map,TravelRoute route,int fps=30)
    {
        Route=route.Copy();_fps=fps;RouteEditing.AllocateDuration(Route,Route.DurationBudget??(Route.Points.Count>=2?Route.Duration:10),map.Width,map.Height,fps);_accepted=Route.Copy();
        Title="여행 경로 편집 · "+ProductInfo.Credit;Width=1280;Height=900;MinWidth=930;MinHeight=650;WindowStartupLocation=WindowStartupLocation.CenterOwner;Background=System.Windows.Media.Brushes.Black;Foreground=System.Windows.Media.Brushes.White;
        Canvas=new(map,()=>Route){Selected=Route.Points.Count>0?0:-1};
        var root=new DockPanel{Margin=new Thickness(10)};Content=root;var top=new WrapPanel();DockPanel.SetDock(top,Dock.Top);root.Children.Add(top);
        Button Add(Panel host,string label,Action action){var button=new Button{Content=label};button.Click+=(_,_)=>action();host.Children.Add(button);return button;}
        Add(top,"새 경로",()=>Edit(()=>{Route.Points.Clear();Canvas.Selected=-1;}));Add(top,"−",()=>Canvas.Zoom(.8));Add(top,"＋",()=>Canvas.Zoom(1.25));Add(top,"100%",()=>{Canvas.Viewport.Native();Canvas.InvalidateVisual();});Add(top,"화면 맞춤",Canvas.Fit);Add(top,"되돌리기",Undo);Add(top,"다시 실행",Redo);
        top.Children.Add(new TextBlock{Text="우클릭: 점 추가·메뉴 / 왼쪽 드래그: 점·곡선 / 휠: 확대 / Space·중간버튼: 이동",Margin=new Thickness(5),TextWrapping=TextWrapping.Wrap});
        var bottom=new StackPanel();DockPanel.SetDock(bottom,Dock.Bottom);root.Children.Add(bottom);var playback=new WrapPanel();bottom.Children.Add(playback);
        Add(playback,"▶ 재생 / 정지",TogglePlayback);Add(playback,"처음",()=>{Stop();TimeSlider.Value=0;});playback.Children.Add(_clockLabel);
        var actual=new CheckBox{Content="실제 효과만 보기 · 편집 표시 숨김",Margin=new Thickness(8,5,4,5)};actual.Checked+=(_,_)=>{Canvas.ShowGuides=false;Canvas.InvalidateVisual();};actual.Unchecked+=(_,_)=>{Canvas.ShowGuides=true;Canvas.InvalidateVisual();};playback.Children.Add(actual);
        TimeSlider.ValueChanged+=(_,_)=>{Canvas.Time=TimeSlider.Value;_clockLabel.Text=$"{TimeSlider.Value:0.00} / {Route.Duration:0.00}초";Canvas.InvalidateVisual();};bottom.Children.Add(TimeSlider);
        var actions=new WrapPanel();bottom.Children.Add(actions);actions.Children.Add(_assetName);
        _save=Add(actions,"내 여행 경로로 저장",()=>{try{Reallocate();Route.Validate();SaveAssetRequested?.Invoke(Route.Copy());_info.Text="내 여행 경로에 저장했습니다.";}catch(Exception ex){_info.Text=ex.Message;}});
        _apply=Add(actions,"타임라인에 적용",()=>{try{Reallocate();Route.Validate();DialogResult=true;}catch(Exception ex){_info.Text=ex.Message;}});Add(actions,"취소",Close);bottom.Children.Add(_info);
        var tabs=new TabControl{Width=310};DockPanel.SetDock(tabs,Dock.Right);root.Children.Add(tabs);var points=new StackPanel{Margin=new Thickness(6)};
        points.Children.Add(new TextBlock{Text="전체 시간 · 이동 + 정차",Margin=new Thickness(3,5,3,3)});TotalTime.Configure(.1,300,"초",.5);TotalTime.Slider.Ticks=new System.Windows.Media.DoubleCollection(Enumerable.Range(1,600).Select(i=>i*.5));TotalTime.Slider.IsSnapToTickEnabled=true;TotalTime.SetSilently(Route.DurationBudget??(Route.Duration>.1?Route.Duration:10));points.Children.Add(TotalTime);
        TotalTime.ValueEdited+=v=>{if(!_sync)SetTotalDuration(v);};points.Children.Add(new TextBlock{Text="이동 시간은 경로 길이에 맞춰 자동 계산",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(3,5,3,5)});
        var exactTime=new TextBox{Text=TotalTime.Value.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture)};points.Children.Add(new Expander{Header="전체 시간 정밀 입력 · 초",Foreground=System.Windows.Media.Brushes.White,Content=exactTime});
        void Exact(){if(double.TryParse(exactTime.Text,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var seconds)&&double.IsFinite(seconds)&&seconds is >=.1 and <=300){SetTotalDuration(seconds);exactTime.Text=TotalTime.Value.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture);}else _info.Text="전체 시간은 0.1~300초입니다.";}
        exactTime.LostKeyboardFocus+=(_,_)=>Exact();exactTime.KeyDown+=(_,e)=>{if(e.Key==Key.Enter){Exact();e.Handled=true;}};TotalTime.Slider.ValueChanged+=(_,_)=>exactTime.Text=TotalTime.Value.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture);
        points.Children.Add(PointsList);var order=new WrapPanel();points.Children.Add(order);Add(order,"위로",()=>MovePoint(-1));Add(order,"아래로",()=>MovePoint(1));Add(order,"삭제",DeletePoint);Add(order,"중간 삽입",InsertPoint);points.Children.Add(_pointSettings);
        tabs.Items.Add(new TabItem{Header="경유점 · 시간",Content=new ScrollViewer{Content=points,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}});tabs.Items.Add(new TabItem{Header="아이콘 · 궤적",Content=new ScrollViewer{Content=_styleSettings,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}});root.Children.Add(Canvas);
        PointsList.SelectionChanged+=(_,_)=>{if(_sync)return;Canvas.Selected=PointsList.SelectedIndex;LoadPoint();Canvas.InvalidateVisual();};
        Canvas.GestureStarted+=()=>{Stop();_accepted=Route.Copy();};Canvas.Changed+=()=>{Record();Refresh();};Canvas.GestureEnded+=()=>{Record();Refresh();};Canvas.SelectionChanged+=()=>{_sync=true;PointsList.SelectedIndex=Canvas.Selected;_sync=false;LoadPoint();};Canvas.Message+=message=>_info.Text=message;
        LoadStyle();Refresh();_timer.Tick+=(_,_)=>{if(!_playing)return;var time=_playStart+_clock.Elapsed.TotalSeconds;if(time>=Route.Duration){if(Route.Repeat){time%=Route.Duration;_playStart=time;_clock.Restart();}else{time=Route.Duration;Stop();}}TimeSlider.Value=time;};_timer.Start();Closed+=(_,_)=>{_timer.Stop();Canvas.Dispose();};
        PreviewKeyDown+=(_,e)=>{if(MainWindow.IsTextEditing(Keyboard.FocusedElement as DependencyObject))return;if(Keyboard.Modifiers==ModifierKeys.Control&&e.Key==Key.Z){Undo();e.Handled=true;}else if(Keyboard.Modifiers==ModifierKeys.Control&&e.Key==Key.Y){Redo();e.Handled=true;}else if(e.Key==Key.Delete){DeletePoint();e.Handled=true;}else if(e.Key==Key.Escape){Canvas.CancelGesture();Refresh();}};
    }
    private void Reallocate(){if(Route.DurationBudget is { } budget)RouteEditing.AllocateDuration(Route,budget,Canvas.SourceWidth,Canvas.SourceHeight,_fps);}
    private void Edit(Action change,bool reloadPoint=true){if(_sync)return;Stop();change();Record();Refresh(reloadPoint);}
    private MouseValueControl Number(Panel panel,string label,double value,double min,double max,string unit,Action<double> set,double step=.1)
    {
        panel.Children.Add(new TextBlock{Text=label,Margin=new Thickness(3,7,3,2)});var control=new MouseValueControl();control.Configure(min,max,unit,step);control.Slider.IsSnapToTickEnabled=step==.5;control.SetSilently(value);
        control.GestureStarted+=()=>{_numberGesture=true;_numberBefore=Route.Copy();};control.ValueEdited+=v=>{if(_sync)return;set(v);if(!_numberGesture){Record();Refresh(false);}else{try{Reallocate();_error=null;}catch(Exception ex){_error=ex.Message;}Refresh(false);}};
        control.GestureEnded+=commit=>{if(!_numberGesture)return;_numberGesture=false;if(!commit&&_numberBefore is not null)Route=_numberBefore.Copy();Record();_numberBefore=null;Refresh();};panel.Children.Add(control);return control;
    }
    private void Flag(Panel panel,string label,bool value,Action<bool> set){var box=new CheckBox{Content=label,IsChecked=value,Margin=new Thickness(3,6,3,6)};box.Checked+=(_,_)=>Edit(()=>set(true));box.Unchecked+=(_,_)=>Edit(()=>set(false));panel.Children.Add(box);}
    private void Choices(Panel panel,string label,string[] names,int index,Action<int> set){panel.Children.Add(new TextBlock{Text=label,Margin=new Thickness(3,7,3,2)});var combo=new ComboBox{ItemsSource=names,SelectedIndex=index};combo.SelectionChanged+=(_,_)=>{if(!_sync&&combo.SelectedIndex>=0)Edit(()=>set(combo.SelectedIndex));};panel.Children.Add(combo);}
    private static string[] IconNames=>["비행기","자동차","캠핑카","자전거","사용자 이미지","배"];
    private void LoadStyle()
    {
        _sync=true;_styleSettings.Children.Clear();Choices(_styleSettings,"기본 이동 아이콘",IconNames,(int)Route.Icon,i=>Route.Icon=(TravelIcon)i);
        var import=new Button{Content="사용자 PNG / WebP 아이콘"};import.Click+=(_,_)=>{var file=new OpenFileDialog{Filter="투명 아이콘|*.png;*.webp"};if(file.ShowDialog(this)!=true)return;try{using var original=SourceImage.Read(file.FileName)??throw new InvalidDataException("아이콘을 읽지 못했습니다.");var scale=Math.Min(1,512d/Math.Max(original.Width,original.Height));using var resized=original.Resize(new SKImageInfo(Math.Max(1,(int)(original.Width*scale)),Math.Max(1,(int)(original.Height*scale))),new SKSamplingOptions(SKCubicResampler.CatmullRom));Edit(()=>{Route.CustomPng=ObjectExtractor.EncodeBitmap(resized);Route.Icon=TravelIcon.Custom;});LoadStyle();}catch(Exception ex){_info.Text=ex.Message;}};_styleSettings.Children.Add(import);
        Choices(_styleSettings,"아이콘 방향",["경로 따라 · 수평 이동 지붕 위 유지","좌우 방향만 전환","방향 고정"],(int)Route.Direction,i=>Route.Direction=(RouteDirection)i);Number(_styleSettings,"아이콘 크기",Route.IconSize,8,200,"px",v=>Route.IconSize=v);Flag(_styleSettings,"약한 들썩임 · 기울임",Route.Animate,v=>Route.Animate=v);Flag(_styleSettings,"부드러운 곡선 · 선을 잡고 드래그",Route.Curved,v=>Route.Curved=v);
        _styleSettings.Children.Add(new TextBlock{Text="곡선을 켜도 처음에는 직선입니다. 선 위를 왼쪽으로 누르고 끌면 굽힘점이 생깁니다. 주황 곡선점은 정차하지 않습니다.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(4)});
        Choices(_styleSettings,"지나온 궤적 모양",["실선","파선","점선","화살표"],(int)Route.LineStyle,i=>Route.LineStyle=(RouteLineStyle)i);
        void Color(string title,Func<string> get,Action<string> set){var button=new Button{Content=title+" · "+get()};button.Click+=(_,_)=>{var picker=new ColorPickerWindow(SKColor.Parse(get())){Owner=this};if(picker.ShowDialog()==true){Edit(()=>set(ColorPickerWindow.Hex(picker.SelectedColor)));button.Content=title+" · "+get();}};_styleSettings.Children.Add(button);}Color("궤적 색",()=>Route.Color,v=>Route.Color=v);Color("외곽선 색",()=>Route.OutlineColor,v=>Route.OutlineColor=v);
        Number(_styleSettings,"선 굵기",Route.LineWidth,1,30,"px",v=>Route.LineWidth=v);Number(_styleSettings,"선 불투명도",Route.Opacity*100,0,100,"%",v=>Route.Opacity=v/100);Number(_styleSettings,"외곽선",Route.Outline,0,10,"px",v=>Route.Outline=v);Flag(_styleSettings,"예정 경로 옅게 표시",Route.FuturePath,v=>Route.FuturePath=v);Flag(_styleSettings,"모든 지점 정차 중 강조 · 이름 없음",Route.Arrival,v=>Route.Arrival=v);Flag(_styleSettings,"전체 반복 · OBS 반복과 연동",Route.Repeat,v=>Route.Repeat=v);_sync=false;
    }
    // Retained for old editor automation; collinear handles never introduce a bend.
    private void InitializeCurveHandles(){for(var i=0;i<Route.Points.Count;i++){var p=Route.Points[i];if(i>0&&p.InX==0&&p.InY==0){var a=Route.Points[i-1];p.InX=(a.X-p.X)/3;p.InY=(a.Y-p.Y)/3;}if(i+1<Route.Points.Count&&p.OutX==0&&p.OutY==0){var b=Route.Points[i+1];p.OutX=(b.X-p.X)/3;p.OutY=(b.Y-p.Y)/3;}}}
    private void LoadPoint()
    {
        _sync=true;_pointSettings.Children.Clear();_stopValue=null;StopExactBox=null;var i=Canvas.Selected;if(i<0||i>=Route.Points.Count){_pointSettings.Children.Add(new TextBlock{Text="지도 빈 곳을 우클릭해 출발점부터 찍으세요.",TextWrapping=TextWrapping.Wrap});_sync=false;return;}
        var point=Route.Points[i];_pointSettings.Children.Add(new TextBlock{Text=RouteEditing.Role(Route,i),FontWeight=FontWeights.Bold});var name=new TextBox{Text=point.Name,ToolTip="편집용 이름 · 방송에 표시하지 않습니다."};name.LostKeyboardFocus+=(_,_)=>{if(point.Name!=name.Text)Edit(()=>point.Name=name.Text,false);};_pointSettings.Children.Add(new TextBlock{Text="편집용 이름"});_pointSettings.Children.Add(name);
        Choices(_pointSettings,"역할 변경 · 순서와 함께 이동",["출발","경유","도착"],i==0?0:i==Route.Points.Count-1?2:1,role=>SetPointRole(role));
        _stopValue=Number(_pointSettings,"이 지점 정차 · 0초=통과",point.Stop,0,30,"초",v=>point.Stop=v,.5);
        var exact=new TextBox{Text=point.Stop.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture),Width=80,ToolTip="0~30초 · 출력 프레임 단위로 맞춥니다."};StopExactBox=exact;var exactRow=new WrapPanel();exactRow.Children.Add(new TextBlock{Text="정밀 입력",VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(3)});exactRow.Children.Add(exact);exactRow.Children.Add(new TextBlock{Text="초",VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(3)});var apply=new Button{Content="적용",Name="StopExactApply"};exactRow.Children.Add(apply);_pointSettings.Children.Add(exactRow);
        void CommitExact(){if(_sync||!Route.Points.Any(p=>ReferenceEquals(p,point)))return;if(double.TryParse(exact.Text,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var value)&&double.IsFinite(value)&&value is >=0 and <=30){if(Math.Abs(value-point.Stop)>1e-9)Edit(()=>point.Stop=value);exact.Text=point.Stop.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture);}else{_info.Text="정차 시간은 0~30초로 입력하세요.";exact.Text=point.Stop.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture);}}
        apply.Click+=(_,_)=>CommitExact();exact.PreviewLostKeyboardFocus+=(_,_)=>CommitExact();exact.KeyDown+=(_,e)=>{if(e.Key==Key.Enter){CommitExact();e.Handled=true;}};
        Choices(_pointSettings,"이 지점 이후 교통수단",new[]{"이전 교통수단 유지"}.Concat(IconNames).ToArray(),point.Icon is { } icon?(int)icon+1:0,n=>point.Icon=n==0?null:(TravelIcon)(n-1));
        var all=new Button{Content="이 정차 시간을 모든 지점에 적용"};all.Click+=(_,_)=>Edit(()=>{foreach(var p in Route.Points)p.Stop=point.Stop;});_pointSettings.Children.Add(all);
        var reset=new Button{Content="연결된 곡선 직선으로"};reset.Click+=(_,_)=>Edit(()=>{point.InX=point.InY=point.OutX=point.OutY=0;point.CurvePoints.Clear();if(i+1<Route.Points.Count){Route.Points[i+1].CurvePoints.Clear();Route.Points[i+1].InX=Route.Points[i+1].InY=0;}});_pointSettings.Children.Add(reset);_sync=false;
    }
    public void SetTotalDuration(double seconds)=>Edit(()=>Route.DurationBudget=seconds,false);
    public void SetPointRole(int role)
    {
        var i=Canvas.Selected;if(i<0)return;var target=role==0?0:role==2?Route.Points.Count-1:Route.Points.Count>2?Math.Clamp(i,1,Route.Points.Count-2):i;Edit(()=>{RouteEditing.MoveWaypoint(Route,i,target);Canvas.Selected=target;});
    }
    public void MovePoint(int delta){var i=Canvas.Selected;var target=i+delta;if(i<0||target<0||target>=Route.Points.Count)return;Edit(()=>{RouteEditing.MoveWaypoint(Route,i,target);Canvas.Selected=target;});}
    public void DeletePoint(){var i=Canvas.Selected;if(i<0)return;Edit(()=>{RouteEditing.DeleteWaypoint(Route,i);Canvas.Selected=Math.Min(i,Route.Points.Count-1);});}
    public void InsertPoint(){if(Route.Points.Count<2){_info.Text="지도 빈 곳을 우클릭해 출발·도착점을 찍으세요.";return;}Edit(()=>Canvas.Selected=RouteEditing.InsertWaypoint(Route,Math.Clamp(Canvas.Selected,0,Route.Points.Count-2),0,.5));}
    private void Refresh(bool point=true)
    {
        _sync=true;Canvas.Selected=Math.Clamp(Canvas.Selected,-1,Route.Points.Count-1);PointsList.ItemsSource=Route.Points.Select((p,i)=>$"{RouteEditing.Role(Route,i)}{(p.Name.Length>0?" · "+p.Name:"")} · 정차{p.Stop:0.##}초").ToList();PointsList.SelectedIndex=Canvas.Selected;TimeSlider.Maximum=Math.Max(.1,Route.Duration);_clockLabel.Text=$"{TimeSlider.Value:0.00} / {Route.Duration:0.00}초";
        if(Route.DurationBudget is { } budget)TotalTime.SetSilently(budget);else if(Route.Duration>=.1)TotalTime.SetSilently(Route.Duration);
        if(Canvas.Selected>=0){var selected=Route.Points[Canvas.Selected];_stopValue?.SetSilently(selected.Stop);if(StopExactBox is not null&&!StopExactBox.IsKeyboardFocusWithin)StopExactBox.Text=selected.Stop.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture);}
        _apply.IsEnabled=_save.IsEnabled=Route.Points.Count>=2;_info.Text=_error??(Route.Points.Count==0?"빈 지도에서 우클릭해 출발점을 찍으세요.":Route.Points.Count==1?"다음 점을 우클릭해 도착점을 찍으세요.":$"{Route.Points.Count}개 지점 · 이동+정차 {Route.Duration:0.##}초 · 선과 점은 편집 표시이며 방송에 포함되지 않습니다.");_sync=false;if(point)LoadPoint();Canvas.InvalidateVisual();
    }
    private void Record()
    {
        try{Reallocate();if(Route.Duration>300)throw new InvalidDataException("전체 길이는 최대300초입니다.");_undo.Add(_accepted);while(_undo.Count>40)_undo.RemoveAt(0);_redo.Clear();_accepted=Route.Copy();_error=null;}
        catch(Exception ex){Route=_accepted.Copy();_error=ex.Message;LoadStyle();LoadPoint();}
    }
    public void Undo(){if(_undo.Count==0)return;Stop();_redo.Add(Route.Copy());Route=_undo[^1].Copy();_undo.RemoveAt(_undo.Count-1);_accepted=Route.Copy();_error=null;LoadStyle();Refresh();}
    public void Redo(){if(_redo.Count==0)return;Stop();_undo.Add(Route.Copy());Route=_redo[^1].Copy();_redo.RemoveAt(_redo.Count-1);_accepted=Route.Copy();_error=null;LoadStyle();Refresh();}
    private void Stop(){_playing=false;_clock.Stop();}
    private void TogglePlayback(){if(_playing){Stop();return;}if(Route.Points.Count<2){_info.Text="출발·도착점을 먼저 찍으세요.";return;}if(TimeSlider.Value>=Route.Duration)TimeSlider.Value=0;_playStart=TimeSlider.Value;_clock.Restart();_playing=true;}
}
