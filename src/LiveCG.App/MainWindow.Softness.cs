using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LiveCG.Core;
using SkiaSharp;

namespace LiveCG.App;
public partial class MainWindow
{
    private readonly CheckBox _softEnabled=new(){Content="선택 영역 경계 부드럽게"};
    private readonly CheckBox _softSeparate=new(){Content="위·오른쪽·아래·왼쪽 개별 조절"};
    private readonly CheckBox _contentOnly=new(){Content="배경 고정 · 내용만 효과 적용 (단색 배경)"};
    private readonly ComboBox _softDirection=new();
    private readonly Dictionary<string,MouseValueControl> _softValues=[];
    private readonly Dictionary<string,TextBox> _softInputs=[];
    private readonly StackPanel _softSides=new();
    private bool _softSync;
    public IReadOnlyDictionary<string,MouseValueControl> SoftValues=>_softValues;
    private void InitializeSoftness()
    {
        var panel=new StackPanel();SoftnessPanel.Children.Add(new Expander{Header="영역 소프트 경계 · 배경 고정",Foreground=Brushes.LightSteelBlue,Content=panel,IsExpanded=true});
        panel.Children.Add(_softEnabled);Choices(_softDirection,["안쪽 · 선택 범위 안에서","바깥쪽 · 주변까지 확장","양쪽 · 선택선을 중심으로"]);panel.Children.Add(_softDirection);
        void Change(Action<EffectRegion> edit){if(_loading||_softSync||_busy||_region is null)return;edit(_region);MarkEdited();Preview.InvalidateVisual();}
        _softEnabled.Checked+=(_,_)=>Change(r=>r.Softness=r.Softness with{Enabled=true});_softEnabled.Unchecked+=(_,_)=>Change(r=>r.Softness=r.Softness with{Enabled=false});
        _softDirection.SelectionChanged+=(_,_)=>Change(r=>r.Softness=r.Softness with{Direction=(SoftDirection)Math.Max(0,_softDirection.SelectedIndex)});
        void Number(Panel host,string key,string title,double min,double max,string unit,Action<EffectRegion,double> set)
        {
            host.Children.Add(new TextBlock{Text=title,Margin=new Thickness(3,8,3,2)});var input=new TextBox();host.Children.Add(input);EditorState? before=null;
            var mouse=MouseValueControl.Attach(input,min,max,unit,v=>{Change(r=>set(r,v));},()=>{before=CaptureState();BeginGesture();},commit=>{EndGesture(commit);if(!commit&&before is not null)RestoreEdit(before);before=null;});
            input.LostKeyboardFocus+=(_,_)=>{if(double.TryParse(input.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out var n)&&double.IsFinite(n))mouse.EditValue(Math.Clamp(n,min,max));};
            _softValues[key]=mouse;_softInputs[key]=input;
        }
        Number(panel,"Width","소프트 폭 · 원본 픽셀",0,256,"px",(r,v)=>r.Softness=r.Softness with{Width=v});
        Number(panel,"Strength","부드러움 강도",10,300,"%",(r,v)=>r.Softness=r.Softness with{Strength=v/100});
        panel.Children.Add(_softSeparate);panel.Children.Add(_softSides);
        _softSeparate.Checked+=(_,_)=>{Change(r=>r.Softness=r.Softness with{SeparateSides=true});_softSides.Visibility=Visibility.Visible;};_softSeparate.Unchecked+=(_,_)=>{Change(r=>r.Softness=r.Softness with{SeparateSides=false});_softSides.Visibility=Visibility.Collapsed;};
        Number(_softSides,"Top","위",0,128,"px",(r,v)=>r.Softness=r.Softness with{Top=v});
        Number(_softSides,"Right","오른쪽",0,128,"px",(r,v)=>r.Softness=r.Softness with{Right=v});
        Number(_softSides,"Bottom","아래",0,128,"px",(r,v)=>r.Softness=r.Softness with{Bottom=v});
        Number(_softSides,"Left","왼쪽",0,128,"px",(r,v)=>r.Softness=r.Softness with{Left=v});
        var guides=new CheckBox{Content="안·밖 범위선과 폭 손잡이 표시",IsChecked=true};panel.Children.Add(guides);guides.Checked+=(_,_)=>{Preview.ShowSoftGuides=true;Preview.InvalidateVisual();};guides.Unchecked+=(_,_)=>{Preview.ShowSoftGuides=false;Preview.InvalidateVisual();};
        panel.Children.Add(_contentOnly);_contentOnly.Checked+=(_,_)=>Change(r=>r.ContentOnly=true);_contentOnly.Unchecked+=(_,_)=>Change(r=>r.ContentOnly=false);
        Number(panel,"Tolerance","배경 분리 허용오차",0,150,"",(r,v)=>r.ContentTolerance=v);
        panel.Children.Add(new TextBlock{Text="배경색은 아래 ‘영역 배경 처리’에서 추정하거나 스포이드로 지정합니다. 글자는 원본 픽셀을 사용합니다. 사진·무늬 배경 복원용은 아닙니다.",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.LightSteelBlue,Margin=new Thickness(4)});
        Choices(CompareBackgroundBox,["체크무늬","검정 배경","흰색 배경","검정 / 흰색 비교","사용자 이미지"]);CompareBackgroundBox.SelectedIndex=0;
    }
    private void LoadSoftnessFields()
    {
        if(_softValues.Count==0)return;_softSync=true;
        var s=_region?.Softness??new();_softEnabled.IsChecked=s.Enabled;_softDirection.SelectedIndex=(int)s.Direction;_softSeparate.IsChecked=s.SeparateSides;_softSides.Visibility=s.SeparateSides?Visibility.Visible:Visibility.Collapsed;_contentOnly.IsChecked=_region?.ContentOnly??false;
        foreach(var (k,v) in new Dictionary<string,double>{{"Width",s.Width},{"Strength",s.Strength*100},{"Top",s.Top},{"Right",s.Right},{"Bottom",s.Bottom},{"Left",s.Left},{"Tolerance",_region?.ContentTolerance??24}}){_softValues[k].SetSilently(v);_softInputs[k].Text=v.ToString("0.###",CultureInfo.InvariantCulture);}
        SoftnessPanel.IsEnabled=_region is not null&&!_busy;_softSync=false;
    }
    private void CompareBackgroundChanged(object sender,SelectionChangedEventArgs e)
    {if(Preview is null)return;Preview.ComparisonBackground=Math.Max(0,CompareBackgroundBox.SelectedIndex);Preview.InvalidateVisual();}
    private void ShowSoftnessProperties(object sender,RoutedEventArgs e)
    {if(_region is null){Notify("먼저 효과를 적용할 영역을 선택하세요.");return;}EffectsTabs.SelectedIndex=1;LoadSoftnessFields();SoftnessPanel.BringIntoView();}
    private void AssignTimelineSection(ClipSection section)
    {
        if(_busy||_current is null)return;var clips=TimelineEditor.SelectedClips.Select(x=>x.Clip).ToList();if(clips.Count==0&&_clip is not null)clips.Add(_clip);
        if(clips.Count==0)return;foreach(var clip in clips)clip.Section=section;_current.Playback.ByEffects=true;_current.Playback.Enabled=true;if(section==ClipSection.Exit)_current.Playback.ExitEnabled=true;
        MarkEdited();RefreshClipList();RefreshTimeline();Notify($"{clips.Count}개 효과를 {SectionRules.Name(section)} 구간에 배정했습니다.");
    }
    public Window? CreateLoopComparison()
    {
        if(_current is null||!_current.Playback.Enabled)return null;var plan=PlaybackPlan.Create(_current,_video);
        using var source=SKBitmap.Decode(_current.SourcePath);using var scene=new EffectScene(source);var panel=new StackPanel{Margin=new Thickness(12)};
        panel.Children.Add(new TextBlock{Text="반복 마지막 → 반복 첫 프레임 · 크기·위치·경계가 이어지는지 비교하세요.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(4,4,4,12)});
        var grid=new System.Windows.Controls.Primitives.UniformGrid{Columns=2};panel.Children.Add(grid);
        foreach(var (time,label) in new[]{((plan.LoopEndFrame-1d)/plan.Fps,"반복 마지막 프레임"),((double)plan.IntroFrames/plan.Fps,"반복 첫 프레임")})
        {
            var cell=new StackPanel();grid.Children.Add(cell);cell.Children.Add(new TextBlock{Text=label,Margin=new Thickness(5)});
            using var bitmap=scene.Render(SectionRules.RenderRegions(_current),_video,time,520);using var image=SKImage.FromBitmap(bitmap);using var data=image.Encode(SKEncodedImageFormat.Png,100);using var stream=new MemoryStream(data.ToArray());var display=new BitmapImage();display.BeginInit();display.CacheOption=BitmapCacheOption.OnLoad;display.StreamSource=stream;display.EndInit();display.Freeze();cell.Children.Add(new Border{Background=Brushes.DimGray,Margin=new Thickness(4),Child=new System.Windows.Controls.Image{Source=display,Stretch=Stretch.Uniform,MaxHeight=500}});
        }
        return new Window{Title="반복 연결 확인 · "+ProductInfo.Credit,Owner=this,Width=1080,Height=650,MinWidth=650,MinHeight=360,Background=Background,Foreground=Foreground,Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto},WindowStartupLocation=WindowStartupLocation.CenterOwner};
    }
    private void CompareLoopEdges(object sender,RoutedEventArgs e)
    {try{var window=CreateLoopComparison();if(window is null){Notify("PNG와 등장·반복 구간을 먼저 설정하세요.");return;}window.ShowDialog();}catch(Exception ex){Error(ex);}}
}
