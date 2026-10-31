using System.Windows;
using LiveCG.Core;

namespace LiveCG.App;

public partial class MainWindow
{
    public void MoveLayer(EffectRegion region,int delta)
    {
        if(_busy||_current is null||!_current.Regions.Contains(region)||delta==0)return;
        Preview.CancelGesture();TimelineEditor.CancelGesture();var siblings=_current.Regions.Where(r=>r.ParentId==region.ParentId).ToList();var old=siblings.IndexOf(region);var next=Math.Clamp(old+delta,0,siblings.Count-1);if(old==next)return;
        var target=siblings[next];var active=region.Clips.Contains(_clip!)?_clip:null;var ids=active is null?[]:TimelineEditor.SelectedClipIds.ToArray();_current.Regions.Remove(region);var at=_current.Regions.IndexOf(target);_current.Regions.Insert(delta>0?at+1:at,region);RefreshRegionList();SelectRegion(region,active,false);if(ids.Length>0)TimelineEditor.SetSelection(ids,active);RefreshTimeline();MarkEdited();
    }
    public void ToggleLayer(EffectRegion region)
    {
        if(_busy||_current is null||!_current.Regions.Contains(region))return;
        Preview.CancelGesture();TimelineEditor.CancelGesture();region.Enabled=!region.Enabled;RefreshRegionList();RefreshTimeline();MarkEdited();Notify(region.Role==RegionRole.EffectArea?(region.Enabled?"그룹 안 효과 사용":"그룹 안 효과 해제 · 그룹의 원본은 유지합니다."):(region.Enabled?"레이어 표시":"레이어 숨김 · 출력에서도 숨깁니다."));
    }
    private void MoveLayerUp(object sender,RoutedEventArgs e){if(_region is not null)MoveLayer(_region,-1);}
    private void MoveLayerDown(object sender,RoutedEventArgs e){if(_region is not null)MoveLayer(_region,1);}
    private void ToggleSelectedLayer(object sender,RoutedEventArgs e){if(_region is not null)ToggleLayer(_region);}
    public int SplitAtCursor()
    {
        if(_busy||_gestureActive||_current is null||!CommitClip(true))return 0;
        RecordEdit();
        var items=TimelineEditor.SelectedClips.ToList();if(items.Count==0&&_region is not null&&_clip is not null)items.Add((_region,_clip));
        var planned=items.Select(x=>(x.Region,Original:x.Clip,Left:x.Clip.Copy(false))).Select(x=>(x.Region,x.Original,x.Left,Right:TimelineRules.Split(x.Left,_time,_video.Fps))).Where(x=>x.Right is not null).ToList();
        if(planned.Any(x=>x.Region.Clips.Count+planned.Count(p=>p.Region==x.Region)>4000))throw new System.IO.InvalidDataException("영역당 최대4,000클립입니다.");
        foreach(var item in planned){item.Original.Duration=item.Left.Duration;item.Original.SourceOffset=item.Left.SourceOffset;item.Original.SourceDuration=item.Left.SourceDuration;item.Original.SequenceId=item.Left.SequenceId;item.Region.Clips.Add(item.Right!);}
        if(planned.Count>0){var primary=planned[0].Right!;SelectRegion(planned[0].Region,primary);TimelineEditor.SetSelection(planned.Select(x=>x.Right!.Id),primary);RefreshRegionList();RefreshTimeline();MarkEdited();}
        return planned.Count;
    }
    private void SplitClips(object sender,RoutedEventArgs e){try{var count=SplitAtCursor();Notify(count>0?$"{count}개 효과 분할 · 원래 재생 진행 유지":"분할할 클립 안으로 커서를 옮기세요.");}catch(Exception ex){Error(ex);}}
}
