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
    private readonly CheckBox _softEnabled=new(){Content="소프트",VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(4)};
    private readonly CheckBox _softSeparate=new(){Content="위·오른쪽·아래·왼쪽 개별 조절"};
    private readonly ComboBox _contentMode=new(){Width=140,VerticalAlignment=VerticalAlignment.Center};
    private readonly TextBlock _separationState=new(){VerticalAlignment=VerticalAlignment.Center,Foreground=Brushes.Aquamarine,Margin=new Thickness(5),FontSize=10};
    private readonly ComboBox _softDirection=new(){Width=80,VerticalAlignment=VerticalAlignment.Center};
    public readonly TextBlock SelectedRegionText=new(){Width=78,TextTrimming=TextTrimming.CharacterEllipsis,Foreground=Brushes.Aquamarine,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(3)};
    public readonly Border ColorSwatch=new(){Width=22,Height=22,BorderThickness=new Thickness(1),BorderBrush=Brushes.LightSteelBlue,Background=Brushes.White,Margin=new Thickness(4)};
    private readonly Dictionary<string,MouseValueControl> _softValues=[];
    private readonly Dictionary<string,TextBox> _softInputs=[];
    private readonly StackPanel _softSides=new();
    private bool _softSync;
    public IReadOnlyDictionary<string,MouseValueControl> SoftValues=>_softValues;
    private void InitializeSoftness()
    {
        RegisterName("SelectedRegionText",SelectedRegionText);RegisterName("ColorSwatch",ColorSwatch);RegisterName("AreaContentMode",_contentMode);RegisterName("AreaSoftEnabled",_softEnabled);
        Panel row=SoftnessPanel;row.Children.Add(SelectedRegionText);row.Children.Add(_softEnabled);
        Choices(_softDirection,["안쪽","바깥쪽","양쪽"]);
        void Change(Action<EffectRegion> edit){if(_loading||_softSync||_busy||_region is null)return;edit(_region);MarkEdited();LoadContextLabels();Preview.InvalidateVisual();}
        _softEnabled.Checked+=(_,_)=>Change(r=>r.Softness=r.Softness with{Enabled=true});_softEnabled.Unchecked+=(_,_)=>Change(r=>r.Softness=r.Softness with{Enabled=false});
        _softDirection.SelectionChanged+=(_,_)=>Change(r=>r.Softness=r.Softness with{Direction=(SoftDirection)Math.Max(0,_softDirection.SelectedIndex)});
        void Number(Panel host,string key,string title,double min,double max,string unit,Action<EffectRegion,double> set,bool compact=false)
        {
            Panel target=host;
            target.Children.Add(new TextBlock{Text=compact?"폭":title,FontSize=compact?10:12,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(3,3,3,0)});var input=new TextBox();target.Children.Add(input);EditorState? before=null;
            var mouse=MouseValueControl.Attach(input,min,max,unit,v=>Change(r=>set(r,v)),()=>{before=CaptureState();BeginGesture();},commit=>{EndGesture(commit);if(!commit&&before is not null)RestoreEdit(before);before=null;});
            if(compact){mouse.Orientation=Orientation.Horizontal;mouse.Width=140;mouse.Height=30;mouse.VerticalAlignment=VerticalAlignment.Center;mouse.Slider.Width=75;foreach(FrameworkElement part in mouse.Children)part.VerticalAlignment=VerticalAlignment.Center;if(input.Parent is Expander exact&&exact.Parent is Panel parent){parent.Children.Remove(exact);exact.Header="경계 폭 정밀 입력";SoftDetailsPanel.Children.Add(exact);}}
            input.LostKeyboardFocus+=(_,_)=>{if(double.TryParse(input.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out var n)&&double.IsFinite(n))mouse.EditValue(Math.Clamp(n,min,max));};_softValues[key]=mouse;_softInputs[key]=input;
        }
        Number(row,"Width","경계 폭",0,256,"px",(r,v)=>r.Softness=r.Softness with{Width=v},true);row.Children.Add(_softDirection);
        var details=new Button{Content="세부 설정",VerticalAlignment=VerticalAlignment.Center};details.Click+=(_,_)=>AreaDetailsPopup.IsOpen=!AreaDetailsPopup.IsOpen;row.Children.Add(details);
        Panel content=row;Choices(_contentMode,["영역 전체","배경 고정·내용만"]);content.Children.Add(_contentMode);content.Children.Add(ColorSwatch);
        void Button(string text,RoutedEventHandler action){var b=new Button{Content=text,Padding=new Thickness(7,4,7,4),VerticalAlignment=VerticalAlignment.Center};b.Click+=action;content.Children.Add(b);}
        Button("스포이드",PickBackgroundColor);Button("색상표",ChooseBackgroundColor);Button("정밀 보정",FitObject);row.Children.Remove(details);row.Children.Add(details);
        _contentMode.SelectionChanged+=(_,_)=>Change(r=>{var on=_contentMode.SelectedIndex==1;r.ContentOnly=on;r.CutoutEnabled=on;});
        Number(SoftDetailsPanel,"Strength","영역 경계 부드러움 강도",10,300,"%",(r,v)=>r.Softness=r.Softness with{Strength=v/100});
        SoftDetailsPanel.Children.Add(_softSeparate);SoftDetailsPanel.Children.Add(_softSides);
        _softSeparate.Checked+=(_,_)=>{Change(r=>r.Softness=r.Softness with{SeparateSides=true});_softSides.Visibility=Visibility.Visible;};_softSeparate.Unchecked+=(_,_)=>{Change(r=>r.Softness=r.Softness with{SeparateSides=false});_softSides.Visibility=Visibility.Collapsed;};
        Number(_softSides,"Top","위",0,256,"px",(r,v)=>r.Softness=r.Softness with{Top=v});Number(_softSides,"Right","오른쪽",0,256,"px",(r,v)=>r.Softness=r.Softness with{Right=v});Number(_softSides,"Bottom","아래",0,256,"px",(r,v)=>r.Softness=r.Softness with{Bottom=v});Number(_softSides,"Left","왼쪽",0,256,"px",(r,v)=>r.Softness=r.Softness with{Left=v});
        var guides=new CheckBox{Content="영역 경계 범위선·폭 손잡이 표시",IsChecked=true};SoftDetailsPanel.Children.Add(guides);guides.Checked+=(_,_)=>{Preview.ShowSoftGuides=true;Preview.InvalidateVisual();};guides.Unchecked+=(_,_)=>{Preview.ShowSoftGuides=false;Preview.InvalidateVisual();};
        Number(SoftDetailsPanel,"Tolerance","자동 분리 허용오차",0,150,"",(r,v)=>r.ContentTolerance=v);
        SoftDetailsPanel.Children.Add(new TextBlock{Text="영역 소프트는 선택틀의 경계, 정밀 보정은 실제 오브젝트 윤곽을 조절합니다. 수동 마스크에는 자동 분리를 다시 적용하지 않습니다.",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.LightSteelBlue,Margin=new Thickness(4)});
        Choices(CompareBackgroundBox,["체크무늬","검정 배경","흰색 배경","검정/흰색","사용자 이미지"]);CompareBackgroundBox.SelectedIndex=0;
        Choices(ClipSectionChoice,["자동 분류","등장","반복","퇴장"]);
    }
    private void LoadSoftnessFields()
    {
        if(_softValues.Count==0)return;_softSync=true;
        var s=_region?.Softness??new();_softEnabled.IsChecked=s.Enabled;_softDirection.SelectedIndex=(int)s.Direction;_softSeparate.IsChecked=s.SeparateSides;_softSides.Visibility=s.SeparateSides?Visibility.Visible:Visibility.Collapsed;
        _contentMode.SelectedIndex=_region is not null&&(_region.ContentOnly||RegionRaster.ActiveCutout(_region) is not null)?1:0;
        foreach(var (k,v) in new Dictionary<string,double>{{"Width",s.Width},{"Strength",s.Strength*100},{"Top",s.Top},{"Right",s.Right},{"Bottom",s.Bottom},{"Left",s.Left},{"Tolerance",_region?.ContentTolerance??24}}){_softValues[k].SetSilently(v);_softInputs[k].Text=v.ToString("0.###",CultureInfo.InvariantCulture);}
        SoftnessPanel.IsEnabled=_region is not null&&!_busy;_softSync=false;LoadContextLabels();
    }
    private void CompareBackgroundChanged(object sender,SelectionChangedEventArgs e)
    {if(Preview is null)return;Preview.ComparisonBackground=Math.Max(0,CompareBackgroundBox.SelectedIndex);Preview.InvalidateVisual();}
    private void ShowSoftnessProperties(object sender,RoutedEventArgs e)
    {if(_region is null){Notify("먼저 효과를 적용할 영역을 선택하세요.");return;}LoadSoftnessFields();AreaToolbarScroll.ScrollToLeftEnd();}
    private void AssignTimelineSection(ClipSection section)
    {
        if(_busy||_current is null)return;var clips=TimelineEditor.SelectedClips.Select(x=>x.Clip).ToList();if(clips.Count==0&&_clip is not null)clips.Add(_clip);
        if(clips.Count==0)return;foreach(var clip in clips)clip.Section=section;_current.Playback.ByEffects=true;_current.Playback.Enabled=true;if(section==ClipSection.Exit)_current.Playback.ExitEnabled=true;
        MarkEdited();RefreshClipList();RefreshTimeline();Notify($"{clips.Count}개 효과를 {SectionRules.Name(section)} 구간에 배정했습니다.");
    }
    public Window? CreateLoopComparison()
    {
        if(_current is null||!_current.Playback.Enabled)return null;var plan=PlaybackPlan.Create(_current,_video);
        using var source=SourceImage.Read(_current.SourcePath);using var scene=new EffectScene(source);var panel=new StackPanel{Margin=new Thickness(12)};
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
