using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LiveCG.Core;

namespace LiveCG.App;
public sealed class PlaybackWindow:Window
{
    public PlaybackSections Sections { get; }
    public Dictionary<string,MouseValueControl> Values { get; }=[];
    public ListBox Clips { get; }=new(){SelectionMode=SelectionMode.Extended,Height=170};
    private readonly ImageJob _job;private readonly VideoOptions _video;
    private readonly TextBlock _summary=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(4),Foreground=Brushes.Aquamarine};
    private readonly CheckBox _auto=new(){Content="효과 기준으로 구간 길이 자동 결정",Margin=new Thickness(4,8,4,8)};
    private readonly CheckBox _exit=new(){Content="퇴장 사용",Margin=new Thickness(4,8,4,8)};
    private bool _sync;
    private sealed record Choice(EffectRegion Region,EffectClip Clip)
    {public override string ToString()=>$"[{SectionRules.Name(SectionRules.Effective(Clip))}] {Region.Name} · {Clip.DisplayName}";}
    public PlaybackWindow(ImageJob job,VideoOptions video,double cursor)
    {
        _job=job;_video=video;Sections=job.Playback with{};_job.Playback=Sections;
        Title="효과별 등장 · 반복 · 선택형 퇴장 · "+ProductInfo.Credit;Width=650;Height=790;MinWidth=510;MinHeight=500;WindowStartupLocation=WindowStartupLocation.CenterOwner;Background=new SolidColorBrush(Color.FromRgb(20,32,45));Foreground=Brushes.White;
        var dock=new DockPanel{Margin=new Thickness(16)};Content=dock;var bottom=new StackPanel();DockPanel.SetDock(bottom,Dock.Bottom);dock.Children.Add(bottom);
        var error=new TextBlock{Foreground=Brushes.Salmon,TextWrapping=TextWrapping.Wrap};bottom.Children.Add(error);
        var buttons=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right};bottom.Children.Add(buttons);
        var ok=new Button{Content="구간 적용",Padding=new Thickness(20,8,20,8),IsDefault=true};var cancel=new Button{Content="취소",Padding=new Thickness(20,8,20,8),IsCancel=true};buttons.Children.Add(ok);buttons.Children.Add(cancel);
        var panel=new StackPanel();dock.Children.Add(new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        panel.Children.Add(new TextBlock{Text="등장 한 번 → 강조 반복 → 선택하면 퇴장",FontSize=17,Margin=new Thickness(3,4,3,12)});
        CheckBox Flag(Panel host,string label,bool initial,Action<bool> set){var c=new CheckBox{Content=label,IsChecked=initial,Margin=new Thickness(4,7,4,7)};c.Checked+=(_,_)=>set(true);c.Unchecked+=(_,_)=>set(false);host.Children.Add(c);return c;}
        Flag(panel,"등장 · 반복 사용",Sections.Enabled,v=>Sections.Enabled=v);
        _auto.IsChecked=Sections.ByEffects;panel.Children.Add(_auto);_auto.Checked+=(_,_)=>{Sections.ByEffects=true;RefreshSummary();};_auto.Unchecked+=(_,_)=>{Sections.ByEffects=false;RefreshSummary();};
        panel.Children.Add(new TextBlock{Text="효과 선택 · Ctrl/Shift로 여러 개 선택 후 구간 배정",Margin=new Thickness(4)});
        panel.Children.Add(Clips);var assignments=new WrapPanel();panel.Children.Add(assignments);
        foreach(var section in new[]{ClipSection.Intro,ClipSection.Loop,ClipSection.Exit,ClipSection.Auto}){var s=section;var b=new Button{Content=SectionRules.Name(s)+"에 배정",Padding=new Thickness(9,6,9,6)};b.Click+=(_,_)=>AssignSelected(s);assignments.Children.Add(b);}
        _exit.IsChecked=Sections.ExitEnabled!=false;panel.Children.Add(_exit);
        var exitOptions=new StackPanel();var exitPrecise=new StackPanel();panel.Children.Add(exitOptions);
        Flag(exitOptions,"퇴장 구간에서 전체를 투명하게 페이드",Sections.ExitFade,v=>Sections.ExitFade=v);
        Flag(exitOptions,"퇴장 요청 시 현재 반복을 끝내고 퇴장",Sections.ExitAtLoopEnd,v=>Sections.ExitAtLoopEnd=v);
        void ExitChanged(){Sections.ExitEnabled=_exit.IsChecked==true;exitOptions.Visibility=exitPrecise.Visibility=Sections.ExitEnabled==true?Visibility.Visible:Visibility.Collapsed;RefreshSummary();}
        _exit.Checked+=(_,_)=>ExitChanged();_exit.Unchecked+=(_,_)=>ExitChanged();exitOptions.Visibility=exitPrecise.Visibility=_exit.IsChecked==true?Visibility.Visible:Visibility.Collapsed;
        var actions=new WrapPanel();panel.Children.Add(actions);
        var arrange=new Button{Content="배정 구간 순서로 효과 정렬",ToolTip="각 구간의 내부 간격을 유지해 등장→반복→퇴장 순서로 이동합니다. 마스크는 이동하지 않습니다."};arrange.Click+=(_,_)=>{SectionRules.Arrange(_job,_video.Fps);RefreshClips();};actions.Children.Add(arrange);
        var split=new Button{Content="구간 경계에서 효과 분할"};split.Click+=(_,_)=>{SectionRules.SplitBoundaries(_job,_video);RefreshClips();};actions.Children.Add(split);
        panel.Children.Add(_summary);
        var precise=new StackPanel();panel.Children.Add(new Expander{Header="정밀 설정 · 초 / 프레임으로 경계 직접 지정",Foreground=Brushes.LightSteelBlue,Content=precise,Margin=new Thickness(3,8,3,8)});
        void Value(string name,string label,double value,double min,double max,Action<double> set)
        {
            var host=name=="ExitDuration"?exitPrecise:precise;
            host.Children.Add(new TextBlock{Text=label,Margin=new Thickness(3,10,3,0)});var mouse=new MouseValueControl();mouse.Configure(min,max,"초",1d/video.Fps);mouse.SetSilently(value);mouse.ValueEdited+=v=>{if(_sync)return;if(Sections.ByEffects){var p=SectionRules.Resolve(job,video);Sections.IntroEnd=p.IntroEnd;Sections.LoopEnd=p.LoopEnd;Sections.ExitDuration=p.ExitDuration;}set(TimelineRules.Snap(v,video.Fps));_auto.IsChecked=false;RefreshSummary();};host.Children.Add(mouse);Values[name]=mouse;
            var row=new WrapPanel();host.Children.Add(row);var frame=new TextBox{Width=90,Text=Math.Round(value*video.Fps).ToString()};row.Children.Add(new TextBlock{Text="프레임",VerticalAlignment=VerticalAlignment.Center});row.Children.Add(frame);
            var apply=new Button{Content="프레임 적용"};apply.Click+=(_,_)=>{if(int.TryParse(frame.Text,out var n)&&n>=min*video.Fps&&n<=max*video.Fps)mouse.EditValue((double)n/video.Fps);};row.Children.Add(apply);
            if(name is "IntroEnd" or "LoopEnd"){var b=new Button{Content="현재 커서로 지정"};b.Click+=(_,_)=>mouse.EditValue(cursor);row.Children.Add(b);}
        }
        Value("IntroEnd","등장 끝",Sections.IntroEnd,0,3599,v=>Sections.IntroEnd=v);
        Value("LoopEnd","반복 끝",Sections.LoopEnd,1d/video.Fps,3600,v=>Sections.LoopEnd=v);
        precise.Children.Add(exitPrecise);
        Value("ExitDuration","퇴장 길이",Sections.ExitDuration,0,30,v=>Sections.ExitDuration=v);
        panel.Children.Add(new TextBlock{Text="WebM 반복 횟수 · OBS 브라우저는 계속 반복",Margin=new Thickness(4,8,4,3)});
        var repeat=new ComboBox{IsEditable=false};foreach(var n in new[]{1,2,3,5,10,20,50,100})repeat.Items.Add(n);if(!repeat.Items.Contains(Sections.WebmRepeats))repeat.Items.Add(Sections.WebmRepeats);repeat.SelectedItem=Sections.WebmRepeats;repeat.SelectionChanged+=(_,_)=>Sections.WebmRepeats=(int)repeat.SelectedItem;panel.Children.Add(repeat);
        Flag(panel,"OBS 프로그램 전환 시 처음부터 재생",Sections.ReplayOnShow,v=>Sections.ReplayOnShow=v);
        ok.Click+=(_,_)=>{try{var p=SectionRules.Resolve(job,video);p.Validate();Sections.IntroEnd=p.IntroEnd;Sections.LoopEnd=p.LoopEnd;Sections.ExitDuration=p.ExitDuration;DialogResult=true;}catch(Exception ex){error.Text=ex.Message;}};
        RefreshClips();
    }
    public void SelectClips(IEnumerable<Guid> ids){var set=ids.ToHashSet();Clips.SelectedItems.Clear();foreach(var c in Clips.Items.Cast<Choice>())if(set.Contains(c.Clip.Id))Clips.SelectedItems.Add(c);}
    public void AssignSelected(ClipSection section)
    {if(Clips.SelectedItems.Count==0)return;foreach(var c in Clips.SelectedItems.Cast<Choice>())c.Clip.Section=section;_auto.IsChecked=true;if(section==ClipSection.Exit)_exit.IsChecked=true;RefreshClips();}
    private void RefreshClips(){var selected=Clips.SelectedItems.Cast<Choice>().Select(c=>c.Clip.Id).ToArray();Clips.ItemsSource=_job.Regions.SelectMany(r=>r.Clips.OrderBy(c=>c.Start).Select(c=>new Choice(r,c))).ToList();SelectClips(selected);RefreshSummary();}
    private void RefreshSummary()
    {
        var p=SectionRules.Resolve(_job,_video);var count=SectionRules.Crossing(_job,_video).Count;
        _summary.Text=$"등장 {p.IntroEnd:0.##}초 → 반복 {p.LoopEnd-p.IntroEnd:0.##}초"+(Sections.ExitEnabled!=false?$" → 퇴장 {p.ExitDuration:0.##}초":" · 퇴장 없음")+(count>0?$"\n⚠ 경계를 가로지르는 효과 {count}개 · 정렬하거나 분할해 확인하세요.":"");
        if(Values.Count==3&&Sections.ByEffects){_sync=true;Values["IntroEnd"].SetSilently(p.IntroEnd);Values["LoopEnd"].SetSilently(p.LoopEnd);Values["ExitDuration"].SetSilently(p.ExitDuration);_sync=false;}
    }
}
