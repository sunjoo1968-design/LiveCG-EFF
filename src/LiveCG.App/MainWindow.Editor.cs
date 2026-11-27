using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using LiveCG.Core;

namespace LiveCG.App;

public partial class MainWindow
{
    private EditorPreferences _editor=new();
    private readonly EditHistory _history=new();
    private sealed record CopiedClip(EffectClip Clip,int Row,double Offset);
    private sealed record CopiedRegionBundle(Guid SourceJobId,int PrimaryIndex,List<EffectRegion> Regions,List<(Guid SourceId,double OriginY)> Origins);
    private List<CopiedClip>? _clipboard;
    private CopiedRegionBundle? _regionClipboard;
    private bool _gestureActive,_gestureDirtyBefore,_restoring,_defaultLoop;
    private double _defaultLoopPeriod=1;
    private MotionDefinition? _defaultMotion;
    private string? _gestureJson;
    private sealed record QualityChoice(string Name,int Crf){public override string ToString()=>Name;}
    private static readonly QualityChoice[] Qualities=[new("최고 화질",10),new("고화질 (권장)",18),new("균형",24),new("작은 파일",30)];

    private EditorState CaptureState()=>new(JsonSerializer.Serialize(new ProjectDocument{Jobs=_jobs.Select(j=>j.Snapshot()).ToList(),Video=_video with{}}),_current?.SourcePath,_region?.Id,_clip?.Id,_time,TimelineEditor.SelectedClipIds.ToArray(),_selectedMask?.Id);
    private void ResetHistory(){_history.Reset(CaptureState());UpdateHistoryButtons();}
    private void RecordEdit(){if(!_restoring&&!_loading&&!_gestureActive){_history.Record(CaptureState());UpdateHistoryButtons();}}
    private void UpdateHistoryButtons(){UndoButton.IsEnabled=!_busy&&_history.CanUndo;RedoButton.IsEnabled=!_busy&&_history.CanRedo;}
    private void BeginGesture(){if(_restoring)return;RecordEdit();_gestureJson=CaptureState().Json;_gestureActive=true;_gestureDirtyBefore=_dirty;}
    private void EndGesture(bool commit)
    {
        _gestureActive=false;
        if(commit&&_gestureJson!=CaptureState().Json)MarkEdited();else _dirty=_gestureDirtyBefore;
        _gestureJson=null;
        RefreshRegionList();LoadClipFields();LoadGroupFields();RefreshTimeline();UpdateSelectionLabel();UpdateHistoryButtons();
    }
    private void RestoreEdit(EditorState? state)
    {
        if(state is null||_busy)return;
        _restoring=true;
        try
        {
            var document=JsonSerializer.Deserialize<ProjectDocument>(state.Json)??throw new InvalidDataException("편집 기록을 읽지 못했습니다.");
            foreach(var restored in document.Jobs)
            {
                var live=_jobs.FirstOrDefault(item=>item.BroadcastId==restored.BroadcastId);
                if(live is not null&&!string.IsNullOrEmpty(live.OutputPath))
                {
                    restored.OutputPath=live.OutputPath;
                    restored.OutputAliases=[..live.OutputAliases];
                }
            }
            document.OutputDirectory=OutputFolderBox.Text;ApplyDocument(document,false);var job=_jobs.FirstOrDefault(j=>j.SourcePath==state.File);
            if(job is not null&&job!=_current)DisplayJob(job);
            if(job is not null){_loading=true;FileList.SelectedItem=job;_loading=false;}
            var region=_current?.Regions.FirstOrDefault(r=>r.Id==state.Region);
            SelectRegion(region,region?.Clips.FirstOrDefault(c=>c.Id==state.Clip));if(state.SelectedClips is {Length:>0})TimelineEditor.SetSelection(state.SelectedClips,_clip);if(region?.Masks.FirstOrDefault(m=>m.Id==state.Mask) is { } mask)SelectMask(region,mask);Seek(state.Time,false);
            _dirty=true;foreach(var item in _jobs){item.Progress=0;item.Status="편집 복원";}
        }
        catch(Exception ex){Error(ex);}
        finally{_restoring=false;UpdateHistoryButtons();}
    }
    private void UndoEdit(object sender,RoutedEventArgs e){if(_busy||_gestureActive)return;StopPlayback();RestoreEdit(_history.Undo());}
    private void RedoEdit(object sender,RoutedEventArgs e){if(_busy||_gestureActive)return;StopPlayback();RestoreEdit(_history.Redo());}
    private void CopyClip(object sender,RoutedEventArgs e)
    {
        if(_busy)return;
        if(_selectedMask is not null){_maskClipboard=_selectedMask.Copy();_clipboard=null;Notify("마스크 복사 완료");return;}
        _maskClipboard=null;
        if(_clip is null)
        {
            if(JobEffectBundle.CanCopy(_current)){CopyJobEffectsFrom(_current);}
            return;
        }
        if(!CommitClip(true))return;
        var selected=TimelineEditor.SelectedClips.ToList();if(selected.Count==0)selected.Add((_region!,_clip));
        var rows=LayerGraph.Rows(_current!.Regions).Where(r=>r.Mask is null).Select(r=>r.Region).ToList();var firstRow=selected.Min(x=>rows.IndexOf(x.Region));var firstTime=selected.Min(x=>x.Clip.Start);
        _clipboard=selected.Select(x=>new CopiedClip(x.Clip.Copy(),rows.IndexOf(x.Region)-firstRow,x.Clip.Start-firstTime)).ToList();
        _regionClipboard=null;
        if(JobEffectBundle.FromJob(_current) is { } bundle){_jobEffectsClipboard=bundle;UpdateJobEffectButtons();}
        Notify($"{selected.Count}개 효과 복사 · 타임라인 커서 또는 01 자료 목록에 붙여넣을 수 있습니다.");
    }
    private void PasteClip(object sender,RoutedEventArgs e)
    {
        if(_busy)return;
        if(_maskClipboard is not null&&_region is not null){if(_region.Masks.Count>=100)return;var mask=_maskClipboard.Copy();mask.Start=_time;mask.Validate();_region.Masks.Add(mask);SelectMask(_region,mask);MarkEdited();return;}
        if(_busy||_region is null||_clipboard is null)return;
        try
        {
            var pending=PlanClipboardPaste();
            foreach(var item in pending)item.Region.Clips.Add(item.Clip);
            SelectRegion(pending[0].Region,pending[0].Clip);TimelineEditor.SetSelection(pending.Select(x=>x.Clip.Id),pending[0].Clip);
            RefreshRegionList();UpdateSelectionLabel();RefreshTimeline();MarkEdited();
        }catch(Exception ex){Error(ex);}
    }
    public List<(EffectRegion Region,EffectClip Clip)> PlanClipboardPaste()
    {
        if(_current is null||_region is null||_clipboard is null)throw new InvalidDataException("복사한 효과와 대상 영역이 필요합니다.");
        var rows=LayerGraph.Rows(_current.Regions).Where(r=>r.Mask is null).Select(r=>r.Region).ToList();var baseRow=rows.IndexOf(_region);var pending=new List<(EffectRegion Region,EffectClip Clip)>();var instances=new Dictionary<Guid,Guid>();var sequences=new Dictionary<Guid,Guid>();
        foreach(var item in _clipboard)
        {
            var row=baseRow+item.Row;if(row<0||row>=rows.Count)throw new InvalidDataException("붙여넣을 영역 행이 부족합니다. 그룹을 펼치거나 위쪽 영역을 선택하세요.");
            var copy=item.Clip.Copy();TimelineRules.SetTiming(copy,_time+item.Offset,copy.Duration,_video.Fps);
            if(copy.AssetInstanceId is { } id){if(!instances.TryGetValue(id,out var next))instances[id]=next=Guid.NewGuid();copy.AssetInstanceId=next;}
            if(copy.SequenceId is { } sequence){if(!sequences.TryGetValue(sequence,out var next))sequences[sequence]=next=Guid.NewGuid();copy.SequenceId=next;}
            pending.Add((rows[row],copy));
        }
        if(pending.GroupBy(x=>x.Region).Any(g=>g.Key.Clips.Count+g.Count()>4000))throw new InvalidDataException("영역당 클립은 최대4,000개입니다.");
        return pending;
    }
    private void AddTimelineMenu()
    {
        var menu=new ContextMenu();
        void Item(string text,RoutedEventHandler action){var item=new MenuItem{Header=text};item.Click+=action;menu.Items.Add(item);}
        Item("효과 복사  Ctrl+C",CopyClip);Item("현재 커서에 붙여넣기  Ctrl+V",PasteClip);
        Item("선택 효과 삭제  Delete",DeleteClip);menu.Items.Add(new Separator());Item("되돌리기  Ctrl+Z",UndoEdit);Item("다시 실행  Ctrl+Shift+Z",RedoEdit);
        Item("✂ 커서에서 분할  Ctrl+K",SplitClips);
        foreach(var section in new[]{ClipSection.Intro,ClipSection.Loop,ClipSection.Exit,ClipSection.Auto}){var s=section;Item(SectionRules.Name(s)+" 구간에 배정",(_,_)=>AssignTimelineSection(s));}
        TimelineEditor.ContextMenu=menu;
    }
    public static bool IsTextEditing(DependencyObject? element)
    {
        while(element is not null)
        {
            if(element is TextBoxBase or PasswordBox||element is ComboBox{IsEditable:true})return true;
            element=element is Visual?VisualTreeHelper.GetParent(element):LogicalTreeHelper.GetParent(element);
        }
        return false;
    }
    private bool CopySelectedRegion()
    {
        if(_busy||_region is null||_current is null||!CommitClip(true)||!CommitRegion(true))return false;
        var source=_current.Regions.Where(r=>r.Id==_region.Id||r.ParentId==_region.Id).ToList();
        var snapshots=source.Select(r=>r.Copy(false)).ToList();
        var origins=source.Select(r=>(r.Id,r.Bounds.Y)).ToList();
        _regionClipboard=new(_current.BroadcastId,source.IndexOf(_region),snapshots,origins);
        _clipboard=null;
        if(JobEffectBundle.FromJob(_current) is { } bundle){_jobEffectsClipboard=bundle;UpdateJobEffectButtons();}
        Notify("영역 복사 완료 · Ctrl+V로 붙여넣은 뒤 Shift+드래그로 수평 이동할 수 있습니다.");
        return true;
    }
    private bool PasteCopiedRegion()
    {
        if(_busy||_current is null||_regionClipboard is not { } bundle||!CommitClip(true)||!CommitRegion(true))return false;
        if(_current.Regions.Count+bundle.Regions.Count>100){Notify("영역은 파일당 최대 100개입니다.");return false;}
        var copies=LayerGraph.Copy(bundle.Regions);
        var copy=copies[Math.Clamp(bundle.PrimaryIndex,0,copies.Count-1)];
        var originalPrimary=bundle.Regions[Math.Clamp(bundle.PrimaryIndex,0,bundle.Regions.Count-1)];
        if(originalPrimary.Role==RegionRole.EffectArea&&originalPrimary.ParentId is { } parentId&&_current.Regions.Any(r=>r.Id==parentId&&r.Role==RegionRole.Group))
        {
            copy.Role=RegionRole.EffectArea;
            copy.ParentId=parentId;
        }
        copy.Name+=" 복사";
        copy.Locked=false;
        for(var i=0;i<copies.Count;i++)
            Preview.RegisterCopiedRegion(copies[i].Id,bundle.Origins[i].SourceId,bundle.Origins[i].OriginY);
        _current.Regions.InsertRange(0,copies);
        RefreshRegionList();
        SelectRegion(copy);
        MarkEdited();
        RefreshTimeline();
        Notify("영역 붙여넣기 완료 · Shift를 누른 채 드래그하면 원본 영역과 같은 Y(수평선)로 이동합니다.");
        return true;
    }
    private static Key ActualKey(KeyEventArgs e)=>e.Key==Key.ImeProcessed?e.ImeProcessedKey:e.Key==Key.System?e.SystemKey:e.Key;
    private bool ShouldRouteSpaceToPreview(DependencyObject? focus)
    {
        if(_busy||IsTextEditing(focus))return false;
        if(focus is ComboBox { IsDropDownOpen:true } or MenuItem)return false;
        return Preview.IsVisible&&(Preview.IsSpaceHeld||Preview.IsPanning||ReferenceEquals(focus,Preview)||Preview.IsMouseOver||PreviewPanel.IsMouseOver);
    }
    private void EditorKeyDown(object sender,KeyEventArgs e)
    {
        var actualKey=ActualKey(e);
        if(actualKey is Key.LeftShift or Key.RightShift)Preview.UpdateDragConstraint(true);
        if(actualKey==Key.Space&&ShouldRouteSpaceToPreview(Keyboard.FocusedElement as DependencyObject))
        {
            if(!ReferenceEquals(Keyboard.FocusedElement,Preview))Preview.Focus();
            Preview.SetSpaceHeld(true);
            e.Handled=true;
            return;
        }
        if(!e.Handled)e.Handled=HandleEditorKey(actualKey,Keyboard.Modifiers,Keyboard.FocusedElement as DependencyObject);
    }
    private void EditorKeyUp(object sender,KeyEventArgs e)
    {
        var actualKey=ActualKey(e);
        if(actualKey is Key.LeftShift or Key.RightShift)
            Preview.UpdateDragConstraint(Keyboard.IsKeyDown(Key.LeftShift)||Keyboard.IsKeyDown(Key.RightShift));
        if(actualKey==Key.Space&&(Preview.IsSpaceHeld||ShouldRouteSpaceToPreview(Keyboard.FocusedElement as DependencyObject)))
        {
            Preview.SetSpaceHeld(false);
            e.Handled=true;
        }
    }
    private void WindowDeactivated(object? sender,EventArgs e)=>Preview.SetSpaceHeld(false);
    private bool IsInMaterialPanel(DependencyObject? item)
    {
        while(item is not null)
        {
            if(ReferenceEquals(item,MaterialPanel))return true;
            item=item is Visual?VisualTreeHelper.GetParent(item):LogicalTreeHelper.GetParent(item);
        }
        return false;
    }
    private bool IsInRegionSurface(DependencyObject? focus) =>
        ReferenceEquals(focus,Preview)||Ancestor<PreviewControl>(focus)==Preview||Ancestor<ListBox>(focus)==RegionList;
    public bool HandleEditorKey(Key key,ModifierKeys modifiers,DependencyObject? focus)
    {
        if(_busy||IsTextEditing(focus))return false;
        var e=new RoutedEventArgs();
        var inMaterialPanel=IsInMaterialPanel(focus);
        var inRegionSurface=!inMaterialPanel&&_selectedMask is null&&IsInRegionSurface(focus);
        if(modifiers==ModifierKeys.Control||modifiers==(ModifierKeys.Control|ModifierKeys.Shift))
        {
            if(key==Key.C&&modifiers==ModifierKeys.Control)
            {
                if(inMaterialPanel)CopyJobEffects(this,e);
                else if(inRegionSurface&&_region is not null)CopySelectedRegion();
                else CopyClip(this,e);
            }
            else if(key==Key.V&&modifiers==ModifierKeys.Control)
            {
                if(inMaterialPanel||(_jobEffectsClipboard is not null&&SelectedJobs().Count>1))PasteJobEffects(this,e);
                else if((inRegionSurface||_clipboard is null)&&_regionClipboard is not null)PasteCopiedRegion();
                else PasteClip(this,e);
            }
            else if(key==Key.D&&modifiers==ModifierKeys.Control&&_region is not null)DuplicateRegion(this,e);
            else if(key==Key.K)SplitClips(this,e);
            else if(key==Key.Z){if(modifiers.HasFlag(ModifierKeys.Shift))RedoEdit(this,e);else UndoEdit(this,e);}
            else if(key==Key.Y)RedoEdit(this,e);else return false;
            return true;
        }
        if(!_editor.ShortcutsEnabled||(modifiers!=ModifierKeys.None&&modifiers!=ModifierKeys.Shift)||_current is null)return false;
        if(focus is TimelineControl && key is Key.Left or Key.Right)
        {
            var clips=TimelineEditor.SelectedClips.Select(item=>item.Clip).ToList();
            if(modifiers==ModifierKeys.Shift&&clips.Count==0)return false;
            var frames=modifiers==ModifierKeys.Shift?10:1;
            var delta=(key==Key.Left?-1d:1d)*frames/_video.Fps;
            if(clips.Count>0)
            {
                if(!CommitClip(true))return false;
                if(clips.Min(clip=>clip.Start)+delta < -1e-9 || clips.Max(clip=>clip.Start+clip.Duration)+delta > 3600+1e-9)return true;
                foreach(var clip in clips)clip.Start=Math.Max(0,clip.Start+delta);
                MarkEdited();RefreshTimeline();LoadClipFields();
            }
            StopPlayback();
            var time=TimelineRules.Snap(_time+delta,_video.Fps);
            Seek(Math.Clamp(time,0,ResolvedVideo.ActualDuration),true);
            return true;
        }
        if(key.ToString()==_editor.DrawKey){SetDrawMode(true);return true;}
        else if(key.ToString()==_editor.SelectKey){SetDrawMode(false);return true;}
        else if(key==Key.Tab&&focus is PreviewControl or TimelineControl)
        {
            var count=_current.Regions.Count;if(count>0){var index=_region is null?-1:_current.Regions.IndexOf(_region);SelectRegion(_current.Regions[(index+(modifiers.HasFlag(ModifierKeys.Shift)?-1:1)+count)%count]);}
            return true;
        }
        else if(key>=Key.D1&&key<=Key.D9){var index=key-Key.D1;if(index<_current.Regions.Count)SelectRegion(_current.Regions[index]);return true;}
        return false;
    }
    private void SetDrawMode(bool draw)
    {
        if(_busy||_current is null){Notify("먼저 PNG를 추가하세요.");return;}
        Preview.CancelGesture();
        if(!draw)Preview.CreatingMask=false;
        if(draw){Preview.PointEditMode=false;PathEditButton.Content="점 편집";HideOverlaysCheck.IsChecked=false;Preview.NewShape=(RegionShape)Math.Max(0,ShapeBox.SelectedIndex);}
        if(draw&&AutoFocusCheck.IsChecked==true)SetFocusMode(true);
        Preview.AddMode=draw;Preview.ShowEffects=false;EffectsPreviewCheck.IsChecked=false;Preview.Focus();Preview.InvalidateVisual();UpdateViewLabel();
        Notify(draw?"영역을 드래그하세요. Shift는 정원/정사각형, ESC는 취소입니다.":"영역 선택 모드");
    }
    private void ShapeChanged(object sender,SelectionChangedEventArgs e)
    {
        if(_loading||_busy||ShapeBox.SelectedIndex<0)return;
        _editor.NewShape=(RegionShape)ShapeBox.SelectedIndex;Preview.NewShape=_editor.NewShape;
        SetDrawMode(true);Notify("다음에 그릴 새 도형입니다. 기존 영역은 바뀌지 않습니다.");
    }
    private void ChangeSelectedShape(object sender,RoutedEventArgs e)
    {
        if(_busy||_region is null)return;var shape=(RegionShape)Math.Max(0,ShapeBox.SelectedIndex);
        if(shape is RegionShape.Freehand or RegionShape.Polygon&&(_selectedMask?.Points.Count??_region.Points.Count)<3){Notify("이 도형은 새로 그리거나 기존 경로의 점을 편집하세요.");return;}
        Preview.CancelGesture();if(_selectedMask is not null)_selectedMask.Shape=shape;else _region.Shape=shape;Preview.PointEditMode=false;PathEditButton.Content="점 편집";MarkEdited();Preview.InvalidateVisual();Notify("선택한 영역/마스크의 도형을 변경했습니다.");
    }
    private void TogglePointEditing(object sender,RoutedEventArgs e)
    {SetDrawMode(false);var shape=Preview.SelectedMask?.Shape??_region?.Shape;if(shape is not (RegionShape.Polygon or RegionShape.Freehand)){Notify("먼저 포인트 펜/자유손 영역이나 마스크를 선택하세요.");return;}Preview.PointEditMode=!Preview.PointEditMode;PathEditButton.Content=Preview.PointEditMode?"점 편집 중":"점 편집";Preview.InvalidateVisual();Notify(Preview.PointEditMode?"점 편집: 점 드래그 · 선분 클릭 추가 · Delete 삭제 · ESC 취소":"영역 선택 모드");}
    private void ShowEditorOptions()
    {
        var prior=_loading;_loading=true;SnapCheck.IsChecked=_editor.Snap;RippleCheck.IsChecked=_editor.Ripple;FollowMagnetCheck.IsChecked=_editor.FollowMagnet;
        HideOverlaysCheck.IsChecked=_editor.HideOverlays;ShowAllRegionsCheck.IsChecked=_editor.ShowAllRegions;ShapeBox.SelectedIndex=(int)_editor.NewShape;
        _loading=prior;ApplyEditorOptions();
    }
    private void EditorOptionsChanged(object sender,RoutedEventArgs e){if(!_loading&&!_busy)ApplyEditorOptions();}
    private void ApplyEditorOptions()
    {
        _editor.Snap=SnapCheck.IsChecked==true;_editor.Ripple=RippleCheck.IsChecked==true;_editor.FollowMagnet=FollowMagnetCheck.IsChecked==true;
        _editor.HideOverlays=HideOverlaysCheck.IsChecked==true;_editor.ShowAllRegions=ShowAllRegionsCheck.IsChecked==true;
        TimelineEditor.SnapEnabled=_editor.Snap;TimelineEditor.RippleEnabled=_editor.Ripple;TimelineEditor.FollowMagnet=_editor.FollowMagnet;
        Preview.ShowOverlays=!_editor.HideOverlays;Preview.ShowAllRegions=_editor.ShowAllRegions;Preview.NewShape=_editor.NewShape;
        if(_editor.HideOverlays){Preview.CancelGesture();Preview.AddMode=false;}
        Preview.InvalidateVisual();UpdateViewLabel();
    }
    private void ConfigureShortcuts(object sender,RoutedEventArgs e)
    {
        if(_busy)return;var dialog=new ShortcutWindow(_editor){Owner=this};
        if(dialog.ShowDialog()==true){_editor=dialog.Preferences;ShowEditorOptions();Notify("단축키 변경 완료 · 다음 실행에도 사용하려면 현재 옵션을 기본값으로 저장하세요.");}
    }
    private void ShowQuality()
    {
        while(QualityBox.Items.Count>Qualities.Length)QualityBox.Items.RemoveAt(QualityBox.Items.Count-1);
        var choice=Qualities.FirstOrDefault(q=>q.Crf==_video.Crf);
        if(choice is null){choice=new("이전 사용자 품질",_video.Crf);QualityBox.Items.Add(choice);}
        QualityBox.SelectedItem=choice;
    }
    private List<ImageJob> ExportTargets()=>ExportScopeBox.SelectedIndex switch{1=>SelectedJobs(),2=>_current is null?[]:[_current],_=>_jobs.ToList()};
}
