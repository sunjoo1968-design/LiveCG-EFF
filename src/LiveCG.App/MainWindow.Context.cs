using System.Windows;
using System.Windows.Controls;
using LiveCG.Core;

namespace LiveCG.App;
public partial class MainWindow
{
    private Window? _areaWindow;
    public Window? DetachedAreaWindow=>_areaWindow;
    private void LoadContextLabels()
    {
        if(ContextPathText is null)return;
        var parent=_region?.ParentId is { } id?_current?.Regions.FirstOrDefault(r=>r.Id==id)?.Name:null;
        ContextPathText.Text=_region is null?"영역이나 클립을 선택하세요":(parent is null?"":parent+" → ")+_region.Name+(_selectedMask is not null?" → 마스크: "+_selectedMask.Name:_clip is null?"":" → "+_clip.DisplayName);
        var manual=_region is not null&&RegionRaster.ActiveCutout(_region) is not null;
        _separationState.Text=_region is null?"":manual?"수동 보정 사용":_region.ContentOnly?"자동 분리":"원본 영역";
        _separationState.ToolTip=manual?"저장한 수동 마스크를 사용합니다. 색상 허용오차를 다시 적용하지 않습니다.":"단색 배경 분리는 정밀 보정에서 이어서 편집할 수 있습니다.";
        if(AreaVisibilityButton is not null){AreaVisibilityButton.IsEnabled=_region is not null;AreaVisibilityButton.Content=_region?.Enabled==false?"○ 숨김":"◉ 표시 중";}
        var kind=_clip?.Options.Kind??_defaultEffect.Kind;
        if(ClipAppearanceSection is not null)ClipAppearanceSection.Visibility=kind is EffectKind.Sweep or EffectKind.Glow or EffectKind.Sparkle or EffectKind.Border or EffectKind.Highlight or EffectKind.Ripple or EffectKind.Rays or EffectKind.OrbitBurst or EffectKind.Underline?Visibility.Visible:Visibility.Collapsed;
        if(ClipSectionChoice is not null){var old=_loading;_loading=true;ClipSectionChoice.SelectedIndex=(int)(_clip?.Section??ClipSection.Auto);_loading=old;}
    }
    private void ClipSectionChanged(object sender,SelectionChangedEventArgs e)
    {if(!_loading&&!_busy&&_clip is not null&&ClipSectionChoice.SelectedIndex>=0)AssignTimelineSection((ClipSection)ClipSectionChoice.SelectedIndex);}
    private void PropertyTabChanged(object sender,SelectionChangedEventArgs e)
    {if(e.Source!=EffectsTabs||ContextPathText is null)return;LoadContextLabels();}
    private void ToggleAreaTools(object sender,RoutedEventArgs e)=>AreaToolsExpander.IsExpanded=!AreaToolsExpander.IsExpanded;
    private void DetachAreaTools(object sender,RoutedEventArgs e)
    {
        if(_busy)return;if(_areaWindow is not null){_areaWindow.Activate();return;}
        _areaWindow=FloatPanel(AreaToolsHost,AreaToolsPanel,"영역 빠른 조절",_workspace.AreaTools,saved=>{_workspace.AreaTools=saved;_areaWindow=null;});
        _areaWindow.MinWidth=480;_areaWindow.MinHeight=180;
        _areaWindow.MaxHeight=760;
        if(_workspace.AreaTools is null){_areaWindow.Width=850;_areaWindow.SizeToContent=SizeToContent.Height;}
        AreaToolsExpander.IsExpanded=true;
    }
}
