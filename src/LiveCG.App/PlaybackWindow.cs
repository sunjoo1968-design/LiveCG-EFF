using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LiveCG.Core;

namespace LiveCG.App;

public sealed class PlaybackWindow:Window
{
    public PlaybackSections Sections { get; }
    public Dictionary<string,MouseValueControl> Values { get; }=[];
    public PlaybackWindow(ImageJob job,VideoOptions video,double cursor)
    {
        Sections=job.Playback with{};
        Title="등장 · 반복 · 퇴장 구간 · "+ProductInfo.Credit;Width=560;Height=660;MinWidth=470;MinHeight=500;WindowStartupLocation=WindowStartupLocation.CenterOwner;Background=new SolidColorBrush(Color.FromRgb(20,32,45));Foreground=Brushes.White;
        var dock=new DockPanel{Margin=new Thickness(16)};Content=dock;var bottom=new StackPanel();DockPanel.SetDock(bottom,Dock.Bottom);dock.Children.Add(bottom);
        var error=new TextBlock{Foreground=Brushes.Salmon,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(3,7,3,5)};bottom.Children.Add(error);
        var buttons=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right};bottom.Children.Add(buttons);
        var ok=new Button{Content="구간 적용",Padding=new Thickness(20,8,20,8),IsDefault=true};var cancel=new Button{Content="취소",Padding=new Thickness(20,8,20,8),IsCancel=true};buttons.Children.Add(ok);buttons.Children.Add(cancel);
        var panel=new StackPanel();dock.Children.Add(new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        panel.Children.Add(new TextBlock{Text="등장 한 번 → 강조 반복 → 조작하면 퇴장",FontSize=17,Margin=new Thickness(3,4,3,12)});
        CheckBox Flag(string label,bool initial,Action<bool> set){var c=new CheckBox{Content=label,IsChecked=initial,Margin=new Thickness(4,7,4,7)};c.Checked+=(_,_)=>set(true);c.Unchecked+=(_,_)=>set(false);panel.Children.Add(c);return c;}
        var enabled=Flag("등장 · 반복 · 퇴장 사용",Sections.Enabled,v=>Sections.Enabled=v);
        void Value(string name,string label,double value,double min,double max,Action<double> set)
        {
            panel.Children.Add(new TextBlock{Text=label,Margin=new Thickness(3,12,3,0)});var mouse=new MouseValueControl();mouse.Configure(min,max,"초",1d/video.Fps);mouse.SetSilently(value);mouse.ValueEdited+=v=>set(TimelineRules.Snap(v,video.Fps));panel.Children.Add(mouse);Values[name]=mouse;
            if(name is "IntroEnd" or "LoopEnd"){var button=new Button{Content="현재 커서로 지정 ("+cursor.ToString("0.##")+"초)",HorizontalAlignment=HorizontalAlignment.Left};button.Click+=(_,_)=>mouse.EditValue(cursor);panel.Children.Add(button);}
        }
        var max=Math.Max(10,Math.Max(TimelineRules.LastEnd(job.Regions)+video.EndHold,Sections.LoopEnd)*1.5);
        Value("IntroEnd","등장 끝 · 파란 구간",Sections.IntroEnd,0,Math.Min(3599,max),v=>Sections.IntroEnd=v);
        Value("LoopEnd","반복 끝 · 초록 구간",Sections.LoopEnd,1d/video.Fps,Math.Min(3600,max),v=>Sections.LoopEnd=v);
        Value("ExitDuration","퇴장 길이 · 분홍 구간 (뒤 클립이 있으면 자동 연장)",Sections.ExitDuration,0,30,v=>Sections.ExitDuration=v);
        panel.Children.Add(new TextBlock{Text="WebM에 넣을 반복 횟수 · 브라우저는 무한 반복",Margin=new Thickness(3,12,3,2)});
        var repeat=new ComboBox{IsEditable=true,Text=Sections.WebmRepeats.ToString(),Margin=new Thickness(3)};foreach(var n in new[]{1,2,3,5,10,20,50,100})repeat.Items.Add(n.ToString());panel.Children.Add(repeat);
        repeat.LostKeyboardFocus+=(_,_)=>{if(int.TryParse(repeat.Text,out var n)&&n is >=1 and <=100){Sections.WebmRepeats=n;error.Text="";}else error.Text="반복은 1~100회 정수입니다.";};
        ok.Click+=(_,_)=>{try{if(!int.TryParse(repeat.Text,out var n))throw new InvalidDataException("반복은 1~100회 정수입니다.");Sections.WebmRepeats=n;Sections.Validate();DialogResult=true;}catch(Exception ex){error.Text=ex.Message;}};
        Flag("퇴장 구간에서 전체를 투명하게 페이드",Sections.ExitFade,v=>Sections.ExitFade=v);
        Flag("OBS 프로그램 전환 시 처음부터 재생",Sections.ReplayOnShow,v=>Sections.ReplayOnShow=v);
        Flag("퇴장 요청 시 현재 반복을 끝내고 퇴장",Sections.ExitAtLoopEnd,v=>Sections.ExitAtLoopEnd=v);
        var reset=new Button{Content="현재 타임라인에 맞춰 구간 추천",HorizontalAlignment=HorizontalAlignment.Left};panel.Children.Add(reset);reset.Click+=(_,_)=>{var p=PlaybackSections.FromTimeline(job,video);Values["IntroEnd"].EditValue(p.IntroEnd);Values["LoopEnd"].EditValue(p.LoopEnd);Values["ExitDuration"].EditValue(1);enabled.IsChecked=true;};
        panel.Children.Add(new TextBlock{Text="적용 후 타임라인 위 색상 바의 경계 3개를 드래그하세요. 경계만 지정하며 효과를 자동 분류하거나 이동하지 않습니다. 반복의 양끝 모습이 같도록 효과를 배치하면 이음새가 자연스럽습니다.\nWebM 자체의 OBS 루프는 영상 전체를 다시 재생합니다. 등장 한 번·강조 무한은 브라우저 패키지를 사용하세요.",TextWrapping=TextWrapping.Wrap,Foreground=new SolidColorBrush(Color.FromRgb(156,183,199)),Margin=new Thickness(4,12,4,8)});
    }
}
