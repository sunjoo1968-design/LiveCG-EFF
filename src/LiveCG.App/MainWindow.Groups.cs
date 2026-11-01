using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using LiveCG.Core;

namespace LiveCG.App;
public partial class MainWindow
{
    private MaskTrack? _selectedMask,_maskClipboard;
    private sealed record GroupChoice(Guid Id,string Name){public override string ToString()=>Name;}
    private void InitializeGroups()
    {
        Choices(CreationRoleBox,["자동 연결","새 전체 묶음","묶음 안 부분 효과","독립 객체"]);CreationRoleBox.SelectedIndex=0;Choices(MaskOperationBox,["보일 곳 더하기","이 부분 가리기 (빼기)","겹친 곳만 보이기"]);Choices(MaskEndBox,["마지막 모양 유지","마스크 적용 해제","대상 영역 숨김 (범위 비움)"]);
        Choices(MaskStartBox,["0","0.5","1","2"]);Choices(MaskDurationBox,["1","2","3","5","10"]);Choices(MaskOpacityBox,["0","25","50","75","100"]);Choices(MaskFeatherBox,["0","0.5","1","2","4","8"]);Choices(MaskExpandBox,["-4","-2","-1","0","1","2","4","8"]);
        Preview.MaskCreated+=MaskCreated;Preview.MaskEdited+=()=>{MarkEdited();LoadMaskFields();RefreshTimeline();};Preview.MaskDeleteRequested+=()=>DeleteMask(this,new RoutedEventArgs());
        Preview.RegionCreated+=ConfigureNewRegion;
        TimelineEditor.MaskSelected+=SelectMask;TimelineEditor.MaskToggleRequested+=(r,m)=>{SelectMask(r,m);m.Enabled=!m.Enabled;LoadMaskFields();MarkEdited();RefreshTimeline();};
        TimelineEditor.MaskMoveRequested+=(r,m,delta)=>{var index=r.Masks.IndexOf(m);var next=Math.Clamp(index+delta,0,r.Masks.Count-1);if(index!=next){r.Masks.RemoveAt(index);r.Masks.Insert(next,m);SelectMask(r,m);MarkEdited();RefreshTimeline();}};
        TimelineEditor.CollapseRequested+=ToggleCollapse;
    }
    private void ConfigureNewRegion(EffectRegion region)
    {
        if(_current is null)return;
        var parent=_region?.Role==RegionRole.Group?_region:_current.Regions.FirstOrDefault(g=>g.Id==_region?.ParentId);
        var b=region.Bounds;var p=parent?.Bounds;
        if(CreationRoleBox.SelectedIndex==3){region.Role=RegionRole.Object;return;}if(CreationRoleBox.SelectedIndex==1){region.Role=RegionRole.Group;return;}
        if(CreationRoleBox.SelectedIndex==2&&parent is not null){region.Role=RegionRole.EffectArea;region.ParentId=parent.Id;return;}
        if(parent is not null&&p is not null&&b.X>=p.X-1e-6&&b.Y>=p.Y-1e-6&&b.X+b.Width<=p.X+p.Width+1e-6&&b.Y+b.Height<=p.Y+p.Height+1e-6){region.Role=RegionRole.EffectArea;region.ParentId=parent.Id;}
        else region.Role=RegionRole.Group;
    }
    private void ClearMaskSelection(){_selectedMask=null;Preview.SelectedMask=null;Preview.CreatingMask=false;TimelineEditor.SelectedMask=null;}
    private void LoadGroupFields()
    {
        if(ParentGroupBox is null)return;var prior=_loading;_loading=true;
        ParentGroupBox.ItemsSource=_current?.Regions.Where(g=>g.Role==RegionRole.Group&&g.Id!=_region?.Id).Select(g=>new GroupChoice(g.Id,g.Name)).ToList();ParentGroupBox.SelectedItem=ParentGroupBox.Items.Cast<GroupChoice>().FirstOrDefault(g=>g.Id==_region?.ParentId);
        MaskList.ItemsSource=_region?.Masks.ToList();MaskList.SelectedItem=_selectedMask;GroupRelationText.Text=_region is null?"목록이나 타임라인에서 영역을 고르세요.":_region.Role==RegionRole.Group?"▣ "+_region.Name+" · 전체 묶음":_region.ParentId is { } id?"▣ "+(_current?.Regions.FirstOrDefault(r=>r.Id==id)?.Name??"")+"  ›  ◇ "+_region.Name:"◇ "+_region.Name+" · 독립 객체";_loading=prior;LoadMaskFields();LoadContextLabels();
    }
    public bool SetRegionRelation(EffectRegion region,RegionRole role,Guid? parentId,out string error)
    {
        error="";if(_current is null||!_current.Regions.Contains(region))return false;
        if(role!=RegionRole.Group&&_current.Regions.Any(r=>r.ParentId==region.Id)){error="하위 효과 영역을 먼저 다른 그룹에 연결하거나 독립 객체로 바꾸세요.";return false;}
        var before=region.Copy(false);region.Role=role;region.ParentId=role==RegionRole.EffectArea?parentId:null;
        try{LayerGraph.Validate(_current.Regions);}catch(Exception ex){region.Role=before.Role;region.ParentId=before.ParentId;error=ex.Message;return false;}
        ClearMaskSelection();SelectRegion(region);RefreshRegionList();RefreshTimeline();MarkEdited();return true;
    }
    private void ToggleCollapse(EffectRegion region){CommitMask(false);region.Collapsed=!region.Collapsed;ClearMaskSelection();SelectRegion(region,null,false);RefreshRegionList();RefreshTimeline();MarkEdited();}
    private void CollapseSelectedGroup(object sender,RoutedEventArgs e){if(_region is not null)ToggleCollapse(_region);}
    public void SelectMask(EffectRegion owner,MaskTrack mask)
    {
        if(_current is null||!owner.Masks.Contains(mask))return;SelectRegion(owner,null,false);owner.Collapsed=false;var parent=_current.Regions.FirstOrDefault(g=>g.Id==owner.ParentId);if(parent is not null)parent.Collapsed=false;
        _selectedMask=mask;Preview.SelectedMask=mask;Preview.SelectedRegion=owner;TimelineEditor.SelectedMask=mask;TimelineEditor.SelectedClipIds.Clear();EffectsTabs.SelectedIndex=3;GroupMaskTabs.SelectedIndex=1;RefreshRegionList();LoadGroupFields();RefreshTimeline();
    }
    private void MaskListChanged(object sender,SelectionChangedEventArgs e){if(!_loading&&_region is not null&&MaskList.SelectedItem is MaskTrack mask)SelectMask(_region,mask);}
    public (double Start,double Duration) NewMaskSpan(EffectRegion owner)
    {
        var clips=owner.Clips.Where(c=>c.Enabled).ToList();if(clips.Count==0&&owner.ParentId is { } id)clips=_current?.Regions.FirstOrDefault(r=>r.Id==id)?.Clips.Where(c=>c.Enabled).ToList()??[];
        if(clips.Count==0&&owner.Role==RegionRole.Group)clips=_current?.Regions.Where(r=>r.ParentId==owner.Id&&r.Enabled).SelectMany(r=>r.Clips.Where(c=>c.Enabled)).ToList()??[];
        var start=clips.Count==0?0:clips.Min(c=>c.Start);var end=clips.Count==0?ResolvedVideo.ActualDuration:clips.Max(c=>c.End);return(start,Math.Clamp(end-start,1d/_video.Fps,3600-start));
    }
    private void MakeSelectedGroup(object sender,RoutedEventArgs e){if(_region is not null&&!SetRegionRelation(_region,RegionRole.Group,null,out var error))Notify(error);}
    private void UnlinkSelectedGroup(object sender,RoutedEventArgs e){if(_region is not null&&!SetRegionRelation(_region,RegionRole.Object,null,out var error))Notify(error);}
    private void AttachSelectedGroup(object sender,RoutedEventArgs e){if(_region is not null&&!SetRegionRelation(_region,RegionRole.EffectArea,(ParentGroupBox.SelectedItem as GroupChoice)?.Id,out var error))Notify(error);}
    private void DrawNewMask(object sender,RoutedEventArgs e)
    {
        if(_busy||_region is null)return;if(_region.Masks.Count>=100){Notify("영역당 마스크는 최대100개입니다.");return;}SetDrawMode(true);_selectedMask=null;Preview.SelectedMask=null;Preview.CreatingMask=true;Notify("마스크 그리기 · 도형을 고른 뒤 원본 화면에서 지정하세요.");
    }
    private void MaskCreated(EffectRegion draft)
    {
        if(_region is null||_region.Masks.Count>=100)return;var span=NewMaskSpan(_region);var mask=new MaskTrack{Name=$"마스크 {_region.Masks.Count+1}",Bounds=draft.Bounds,Shape=draft.Shape,Points=draft.Points.Select(p=>p with{}).ToList(),Start=span.Start,Duration=span.Duration,StartAtInPoint=true,EndMode=MaskEndMode.Release};
        _region.Masks.Add(mask);SelectMask(_region,mask);MarkEdited();
    }
    private void DuplicateMask(object sender,RoutedEventArgs e){if(_region is null||_selectedMask is null||_region.Masks.Count>=100)return;var copy=_selectedMask.Copy();copy.Name+=" 복사";_region.Masks.Add(copy);SelectMask(_region,copy);MarkEdited();}
    private void DeleteMask(object sender,RoutedEventArgs e){if(_busy||_region is null||_selectedMask is null)return;Preview.CancelGesture();TimelineEditor.CancelGesture();_region.Masks.Remove(_selectedMask);ClearMaskSelection();RefreshRegionList();LoadGroupFields();MarkEdited();RefreshTimeline();}
    private void LoadMaskFields()
    {
        if(MaskEditorPanel is not null){MaskEditorPanel.Visibility=_selectedMask is null?Visibility.Collapsed:Visibility.Visible;MaskEmptyHint.Visibility=_selectedMask is null?Visibility.Visible:Visibility.Collapsed;MaskList.Visibility=_region?.Masks.Count>0?Visibility.Visible:Visibility.Collapsed;}
        if(MaskNameBox is null)return;var prior=_loading;_loading=true;var m=_selectedMask;MaskNameBox.Text=m?.Name??"";MaskOperationBox.SelectedIndex=(int)(m?.Operation??MaskOperation.Add);MaskEnabledCheck.IsChecked=m?.Enabled??true;MaskInvertCheck.IsChecked=m?.Invert??false;
        MaskStartBox.Text=F(m?.Start??0);MaskDurationBox.Text=F(m?.Duration??3);MaskOpacityBox.Text=F((m?.Opacity??1)*100);MaskFeatherBox.Text=F(m?.Feather??0);MaskExpandBox.Text=F(m?.Expand??0);if(MaskEndBox.Items.Count==3)MaskEndBox.Items[2]=m?.Operation==MaskOperation.Subtract?"가림 해제 (범위 비움)":"대상 영역 숨김 (범위 비움)";MaskEndBox.SelectedIndex=(int)(m?.EndMode??MaskEndMode.Release);MaskStartCheck.IsChecked=m?.StartAtInPoint??true;
        MaskEndHint.Text=m is null?"보라색 마스크 막대의 양끝을 드래그해 적용 구간을 정합니다.":m.EndMode switch{MaskEndMode.Release=>"막대가 끝나면 이 마스크를 적용하지 않습니다. 부모 원본/다른 마스크는 유지합니다.",MaskEndMode.Hold=>"막대가 끝나도 마지막 모양으로 계속 가립니다/보입니다.",_=>m.Operation==MaskOperation.Subtract?"끝난 뒤 빼기 마스크가 비어 가림을 해제합니다.":"끝난 뒤 보일 범위가 비어 소유 영역/부분 효과를 가립니다."};_loading=prior;
    }
    private static double MaskNumber(ComboBox box){if(double.TryParse(box.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out var n)&&double.IsFinite(n))return n;throw new System.IO.InvalidDataException("마스크 숫자를 확인하세요.");}
    private bool CommitMask(bool report)
    {
        if(_loading||_selectedMask is null)return true;
        try
        {
            var m=_selectedMask.Copy(false);m.Name=MaskNameBox.Text.Trim();m.Operation=(MaskOperation)Math.Max(0,MaskOperationBox.SelectedIndex);m.Enabled=MaskEnabledCheck.IsChecked==true;m.Invert=MaskInvertCheck.IsChecked==true;m.StartAtInPoint=MaskStartCheck.IsChecked==true;m.Start=TimelineRules.Snap(MaskNumber(MaskStartBox),_video.Fps);m.Duration=Math.Max(1d/_video.Fps,TimelineRules.Snap(MaskNumber(MaskDurationBox),_video.Fps));m.Opacity=MaskNumber(MaskOpacityBox)/100;m.Feather=MaskNumber(MaskFeatherBox);m.Expand=MaskNumber(MaskExpandBox);m.EndMode=(MaskEndMode)Math.Max(0,MaskEndBox.SelectedIndex);m.Validate();
            _selectedMask.Name=m.Name;_selectedMask.Operation=m.Operation;_selectedMask.Enabled=m.Enabled;_selectedMask.Invert=m.Invert;_selectedMask.StartAtInPoint=m.StartAtInPoint;_selectedMask.Start=m.Start;_selectedMask.Duration=m.Duration;_selectedMask.Opacity=m.Opacity;_selectedMask.Feather=m.Feather;_selectedMask.Expand=m.Expand;_selectedMask.EndMode=m.EndMode;return true;
        }catch(Exception ex){if(report)Notify(ex.Message);return false;}
    }
    private void MaskPropertiesChanged(object sender,RoutedEventArgs e){if(!_loading&&!_busy&&CommitMask(true)){MarkEdited();UpdateMaskEndHint();var selected=_selectedMask;var prior=_loading;_loading=true;MaskList.Items.Refresh();MaskList.SelectedItem=selected;_loading=prior;RefreshTimeline();}}
    private void UpdateMaskEndHint(){var m=_selectedMask;MaskEndHint.Text=m is null?"마스크 막대의 양끝을 드래그해 구간을 정합니다.":m.EndMode switch{MaskEndMode.Release=>"막대가 끝나면 이 마스크를 적용하지 않습니다. 부모 원본/다른 마스크는 유지합니다.",MaskEndMode.Hold=>"막대가 끝나도 마지막 모양으로 계속 가립니다/보입니다.",_=>m.Operation==MaskOperation.Subtract?"끝난 뒤 빼기 마스크가 비어 가림을 해제합니다.":"끝난 뒤 보일 범위가 비어 소유 영역/부분 효과를 가립니다."};}
    private void MaskComboChanged(object sender,SelectionChangedEventArgs e){if(!_loading)QueueEdit(()=>MaskPropertiesChanged(sender,e));}
    private void EditMaskAnimation(object sender,RoutedEventArgs e)
    {
        if(_busy||_selectedMask is null||_current is null||!CommitMask(true))return;
        StopPlayback();var dialog=new MaskAnimationWindow(_current.SourcePath,_selectedMask){Owner=this};if(dialog.ShowDialog()==true){_selectedMask.Motion=dialog.Result.Copy();MarkEdited();RefreshTimeline();}
    }
}
