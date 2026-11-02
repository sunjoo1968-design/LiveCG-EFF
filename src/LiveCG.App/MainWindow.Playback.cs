using System.Windows;
using System.Windows.Controls;
using LiveCG.Core;

namespace LiveCG.App;

public partial class MainWindow
{
    private PlaybackSections _defaultPlayback=PlaybackSections.NewDefault();
    private PlaybackClock? _broadcastClock;
    private double _broadcastElapsed;
    private void InitializePlayback()
    {
        Choices(OutputKindBox,["OBS 브라우저 패키지","WebM 영상","WebM + OBS 둘 다"]);OutputKindBox.SelectedIndex=0;
        Choices(WebmRepeatsBox,["1","2","3","5","10","20","50","100"]);Choices(LoopRestBox,["0","0.25","0.5","1","2","3","5","10","30"]);Choices(RestModeBox,["원본 유지 · 효과만 쉬기","마지막 모습 유지","전체 투명"]);
        Choices(SectionModeBox,["구간: 효과에 맞춤","구간: 직접 조절"]);Choices(PlaybackViewBox,["OBS 방송 미리보기","편집 미리보기"]);BroadcastPreviewCheck.IsChecked=true;PlaybackViewBox.SelectedIndex=0;
    }
    private void ConfigurePlayback(object sender,RoutedEventArgs e)
    {
        if(_busy||_current is null){Notify("먼저 이미지를 선택하세요.");return;}
        StopPlayback();var candidate=_current.Snapshot();if(!candidate.Playback.Enabled)candidate.Playback=PlaybackSections.FromTimeline(candidate,_video);
        var dialog=new PlaybackWindow(candidate,_video,_time){Owner=this};
        if(dialog.ShowDialog()==true){_current.Playback=dialog.Sections with{};_current.Regions=LayerGraph.Copy(candidate.Regions,false);SelectRegion(_current.Regions.FirstOrDefault(r=>r.Id==_region?.Id));MarkEdited();RefreshTimeline();}
    }
    private void OutputKindChanged(object sender,SelectionChangedEventArgs e)
    {
        if(_loading||_busy||OutputKindBox.SelectedIndex<0)return;
        _video.Output=OutputKindBox.SelectedIndex switch{0=>OutputKind.Browser,1=>OutputKind.Webm,_=>OutputKind.Both};ExportButton.Content="출력 시작";MarkEdited(true);RefreshTimeline();
    }
    private bool _playbackViewSync;
    private void BroadcastModeChanged(object sender,RoutedEventArgs e){if(!_loading)StopPlayback();if(_playbackViewSync)return;_playbackViewSync=true;PlaybackViewBox.SelectedIndex=BroadcastPreviewCheck.IsChecked==true?0:1;LoopPreviewCheck.Visibility=BroadcastPreviewCheck.IsChecked==true?Visibility.Collapsed:Visibility.Visible;_playbackViewSync=false;}
    private void PlaybackViewChanged(object sender,SelectionChangedEventArgs e){if(_playbackViewSync||_loading)return;BroadcastPreviewCheck.IsChecked=PlaybackViewBox.SelectedIndex==0;}
    private void SectionModeChanged(object sender,SelectionChangedEventArgs e)
    {
        if(_loading||_busy||_current is null||SectionModeBox.SelectedIndex<0)return;var auto=SectionModeBox.SelectedIndex==0;if(auto==_current.Playback.ByEffects)return;
        StopPlayback();var p=SectionRules.Resolve(_current,_video);_current.Playback=p with{ByEffects=auto,Enabled=true};MarkEdited();RefreshTimeline();
    }
    private void TickPlayback()
    {
        if(!_playing)return;var elapsed=_playStart+_playClock.Elapsed.TotalSeconds;
        if(_singleCycle&&_current is not null)
        {
            var plan=PlaybackPlan.Create(_current,_video);var loop=(double)plan.LoopFrames/plan.Fps;var rest=elapsed>=loop;Seek(rest?(double)(plan.LoopEndFrame-1)/plan.Fps:(double)plan.IntroFrames/plan.Fps+elapsed,false);Preview.RestDisplay=rest?plan.RestMode:null;
            if(elapsed>=loop+(double)plan.RestFrames/plan.Fps){StopPlayback(true);_singleCycle=false;}Preview.InvalidateVisual();return;
        }
        if(_broadcastClock is not null)
        {
            var sample=_broadcastClock.Sample(elapsed);Seek(sample.Time,false);Preview.RestDisplay=sample.IsRest?_current!.Playback.RestMode:null;if(sample.Finished)StopPlayback();return;
        }
        var duration=ResolvedVideo.ActualDuration;var time=elapsed;
        if(time>=duration){if(LoopPreviewCheck.IsChecked==true)time%=duration;else{time=duration;StopPlayback();}}
        Seek(time,false);
    }
    private void ExitPlayback(object sender,RoutedEventArgs e)
    {
        if(_current?.Playback.ExitEnabled==false){Notify("퇴장 사용을 켜면 퇴장할 수 있습니다.");return;}
        if(_current?.Playback.Enabled!=true){Notify("구간 설정에서 등장·반복·퇴장을 먼저 켜세요.");return;}
        BroadcastPreviewCheck.IsChecked=true;
        if(!_playing)TogglePlayback(this,new RoutedEventArgs());
        _broadcastClock?.RequestExit(_playStart+_playClock.Elapsed.TotalSeconds);
        Notify(_current.Playback.ExitAtLoopEnd?"현재 반복이 끝나면 퇴장합니다.":"퇴장 구간을 재생합니다.");
    }
    private void RestartPlayback(object sender,RoutedEventArgs e){StopPlayback();Seek(0,true);TogglePlayback(this,new RoutedEventArgs());}
}
