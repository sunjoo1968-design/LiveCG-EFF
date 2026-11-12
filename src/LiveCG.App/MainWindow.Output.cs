using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using LiveCG.Core;

namespace LiveCG.App;
public partial class MainWindow
{
    private bool _outputSync,_singleCycle;
    private static string RestName(LoopRestMode mode)=>mode switch{LoopRestMode.Hold=>"마지막 모습 유지",LoopRestMode.Transparent=>"전체 투명",_=>"원본 유지 · 효과만 쉬기"};
    private void RefreshOutputSettings()
    {
        if(WebmRepeatsBox is null)return;_outputSync=true;
        var p=_current?.Playback??_defaultPlayback;WebmRepeatsBox.Text=p.WebmRepeats.ToString();LoopRestBox.Text=F(p.LoopRest);RestModeBox.SelectedIndex=(int)p.RestMode;
        var plan=_current is null?null:PlaybackPlan.Create(_current,_video);
        AutoDurationText.Text=plan is null?"이미지를 선택하세요":$"WebM {plan.WebmDuration:0.00}초 · {plan.WebmFrameCount:N0}프레임"+(plan.Enabled?$"\n{plan.Repeats}회 · 사이 쉬기 {(double)plan.RestFrames/plan.Fps:0.##}초\nOBS 무한 반복":"\n구간 미사용 · 전체 1회");
        if(_video.Output==OutputKind.Browser)AutoDurationText.Text=plan is null?"OBS 브라우저 패키지":$"OBS · 등장 {(double)plan.IntroFrames/plan.Fps:0.##}초 → 반복 무한\n사이 쉬기 {p.LoopRest:0.##}초";WebmRepeatsPanel.Visibility=WebmQualityPanel.Visibility=_video.Output==OutputKind.Browser?Visibility.Collapsed:Visibility.Visible;OutputTimingGrid.Columns=_video.Output==OutputKind.Browser?1:2;
        AutoDurationText.ToolTip=plan is null?"":$"편집 타임라인 {plan.Duration:0.##}초 · 최종 WebM은 반복 사이 쉬기를 포함하고 마지막 쉬기는 생략합니다.";
        WebmRepeatsBox.IsEnabled=LoopRestBox.IsEnabled=RestModeBox.IsEnabled=!_busy;_outputSync=false;
    }
    private void OutputTimingComboChanged(object sender,SelectionChangedEventArgs e){if(!_outputSync&&!_loading)QueueEdit(()=>OutputTimingChanged(sender,e));}
    private void OutputTimingChanged(object sender,RoutedEventArgs e)=>CommitOutputTiming();
    private bool CommitOutputTiming()
    {
        if(_outputSync||_loading||_busy)return true;
        try
        {
            var repeats=Number(WebmRepeatsBox.Text,"반복 횟수");if(repeats!=Math.Truncate(repeats)||repeats is <1 or >100)throw new InvalidDataException("반복 횟수는 1~100 정수입니다.");
            var rest=Number(LoopRestBox.Text,"사이 쉬기");if(rest is <0 or >30)throw new InvalidDataException("사이 쉬기는 0~30초입니다.");
            SetOutputTiming((int)repeats,rest,(LoopRestMode)Math.Max(0,RestModeBox.SelectedIndex));
            return true;
        }catch(Exception ex){Notify(ex.Message);return false;}
    }
    public void SetOutputTiming(int repeats,double rest,LoopRestMode mode)
    {
        if(_busy)return;var p=(_current?.Playback??_defaultPlayback) with{WebmRepeats=repeats,LoopRest=TimelineRules.Snap(rest,_video.Fps),RestMode=mode};p.Validate();
        if(p==(_current?.Playback??_defaultPlayback))return;StopPlayback();
        if(_current is null)_defaultPlayback=p;else{if(!p.Enabled){var span=PlaybackSections.FromTimeline(_current,_video);p=p with{Enabled=true,ByEffects=true,IntroEnd=span.IntroEnd,LoopEnd=span.LoopEnd};}_current.Playback=p;}
        MarkEdited();RefreshTimeline();
    }
    public int ApplyTimingToSelected()
    {
        if(_busy)return 0;var from=_current?.Playback??_defaultPlayback;var targets=SelectedJobs().Where(j=>j!=_current).ToList();
        foreach(var job in targets){var p=job.Playback.Enabled?job.Playback:PlaybackSections.FromTimeline(job,_video);job.Playback=p with{WebmRepeats=from.WebmRepeats,LoopRest=from.LoopRest,RestMode=from.RestMode};job.Status="반복 설정 적용";}
        if(targets.Count>0)MarkEdited(true);RefreshTimeline();return targets.Count;
    }
    private void ApplyOutputTiming(object sender,RoutedEventArgs e){if(CommitOutputTiming())Notify($"다른 선택 파일 {ApplyTimingToSelected()}개에 반복·쉬기를 적용했습니다.");}
    private void PreviewSingleCycle(object sender,RoutedEventArgs e)
    {
        if(_current is null||_busy||!CommitClip(true)||!CommitRegion(true)||!CommitVideo(true))return;
        if(!CommitOutputTiming())return;StopPlayback();_singleCycle=true;_playing=true;_playStart=0;_playClock.Restart();PlayButton.Content="Ⅱ 일시정지";EffectsPreviewCheck.IsChecked=true;Preview.ShowEffects=true;
        var plan=PlaybackPlan.Create(_current,_video);Seek((double)plan.IntroFrames/plan.Fps,true);Notify("반복 효과 1회 + 사이 쉬기 확인");
    }
    public IReadOnlyList<string> OutputPlanLines()
    {
        var jobs=ExportTargets();var folder=OutputFolderBox.Text.Trim();var lines=new List<string>();
        var duplicate=jobs.GroupBy(j=>j.OutputName,StringComparer.OrdinalIgnoreCase).Where(g=>g.Count()>1).Select(g=>g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach(var job in jobs)
        {
            var p=PlaybackPlan.Create(job,_video);var suffix=duplicate.Contains(job.OutputName)?" ⚠ 출력 이름 중복":"";
            if(_video.Output!=OutputKind.Browser)lines.Add($"{Path.Combine(folder,job.OutputName)} · {p.WebmDuration:0.##}초 / {p.WebmFrameCount:N0}프레임{suffix}");
            if(_video.Output!=OutputKind.Webm)lines.Add($"{LinkedOutput.Entry(LinkedOutput.Target(job,folder))} · "+(p.Enabled?$"등장 {((double)p.IntroFrames/p.Fps):0.##}초 / 반복 {((double)p.LoopFrames/p.Fps):0.##}초 + 쉬기 {((double)p.RestFrames/p.Fps):0.##}초 · 무한":$"전체 {p.Duration:0.##}초 · 1회")+suffix);
        }return lines;
    }
    private void ShowOutputPlan(object sender,RoutedEventArgs e)
    {
        if(_busy||!CommitVideo(true)||!CommitOutputTiming())return;var panel=new DockPanel{Margin=new Thickness(12)};
        var close=new Button{Content="닫기",HorizontalAlignment=HorizontalAlignment.Right};DockPanel.SetDock(close,Dock.Bottom);panel.Children.Add(close);
        panel.Children.Add(new ListBox{ItemsSource=OutputPlanLines(),HorizontalContentAlignment=HorizontalAlignment.Stretch});
        var dialog=new Window{Title="출력 예정 목록 · "+ProductInfo.Credit,Owner=this,Width=900,Height=480,Content=panel,Background=Background,Foreground=Foreground,WindowStartupLocation=WindowStartupLocation.CenterOwner};close.Click+=(_,_)=>dialog.Close();dialog.ShowDialog();
    }
    private void OpenBrowserEntry(object sender,RoutedEventArgs e)
    {
        if(_current is null)return;var entry=Path.GetFullPath(LinkedOutput.Entry(LinkedOutput.Target(_current,OutputFolderBox.Text.Trim())));
        if(!File.Exists(entry)){Notify("OBS 패키지를 먼저 출력하세요.");return;}Process.Start(new ProcessStartInfo("explorer.exe"){Arguments="/select,\""+entry+"\"",UseShellExecute=true});
    }
}
