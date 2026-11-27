using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using LiveCG.Core;

namespace LiveCG.App;
public partial class MainWindow
{
    private double _layerPanelHeight=240,_rightPanelWidth=335,_leftPanelWidth=280;
    private bool _panelLayoutBusy,_panelDragging;
    private Point _layerDragStart;private EffectRegion? _layerDragSource,_layerMenuTarget;
    private ListBoxItem? _layerDropItem;
    public double PreferredLayerHeight=>_layerPanelHeight;
    public double PreferredRightWidth=>_rightPanelWidth;
    public double PreferredLeftWidth=>_leftPanelWidth;
    public double PreferredTimelineHeight=>_timelineHeight;
    private static T? Ancestor<T>(DependencyObject? item) where T:DependencyObject
    {while(item is not null){if(item is T result)return result;item=item is Visual?VisualTreeHelper.GetParent(item):LogicalTreeHelper.GetParent(item);}return null;}
    private static T? Child<T>(DependencyObject item,string name) where T:FrameworkElement
    {for(var i=0;i<VisualTreeHelper.GetChildrenCount(item);i++){var c=VisualTreeHelper.GetChild(item,i);if(c is T t&&t.Name==name)return t;if(Child<T>(c,name) is { } nested)return nested;}return null;}
    private void InitializeLayerPanel()
    {
        EditorGrid.SizeChanged+=(_,_)=>{ApplyPanelSizes();OutputSettingsScroll.MaxHeight=Math.Clamp(EditorGrid.ActualHeight*.52,280,430);};
        var menu=new ContextMenu();
        void Item(string text,Action<EffectRegion> action){var m=new MenuItem{Header=text};m.Click+=(_,_)=>{if(_layerMenuTarget is { } r&&!_busy)action(r);};menu.Items.Add(m);}
        Item("이름 변경  F2",BeginLayerRename);Item("표시 / 숨김",ToggleLayer);Item("위치 잠금 / 해제",ToggleLayerLock);
        Item("위로 이동",r=>MoveLayer(r,-1));Item("아래로 이동",r=>MoveLayer(r,1));menu.Items.Add(new Separator());
        Item("복제",r=>{SelectRegion(r);DuplicateRegion(this,new RoutedEventArgs());});Item("삭제",r=>{SelectRegion(r);DeleteRegion(this,new RoutedEventArgs());});
        RegionList.ContextMenu=menu;RegionList.PreviewMouseRightButtonDown+=(_,e)=>{var item=Ancestor<ListBoxItem>(e.OriginalSource as DependencyObject);_layerMenuTarget=item?.DataContext as EffectRegion??_region;if(_layerMenuTarget is not null)SelectRegion(_layerMenuTarget);};
        RegionList.DragLeave+=(_,_)=>ClearLayerDropGuide();
    }
    public void SetLayerPanelSize(double height,double width,bool save=false)
    {
        if(!double.IsFinite(height+width))return;_layerPanelHeight=Math.Clamp(height,110,480);_rightPanelWidth=Math.Clamp(width,280,700);ApplyPanelSizes();if(save)SaveWorkspace();
    }
    public void SetEditorPaneSizes(double leftWidth,double rightWidth,double timelineHeight,bool save=false)
    {
        if(!double.IsFinite(leftWidth+rightWidth+timelineHeight))return;
        _leftPanelWidth=Math.Clamp(leftWidth,220,520);
        _rightPanelWidth=Math.Clamp(rightWidth,280,700);
        _timelineHeight=Math.Clamp(timelineHeight,180,700);
        ApplyPanelSizes();
        if(save)SaveWorkspace();
    }
    private void ApplyPanelSizes()
    {
        if(_panelLayoutBusy||_panelDragging)return;
        _panelLayoutBusy=true;
        try
        {
            if(PropertiesLayout is not null&&PropertiesLayout.ActualHeight>0)
            {
                var head=PropertiesLayout.RowDefinitions[0].ActualHeight;
                var maxHeight=Math.Max(110,Math.Min(480,PropertiesLayout.ActualHeight-head-206));
                LayerPanelRow.Height=new GridLength(Math.Min(_layerPanelHeight,maxHeight));
            }
            if(EditorGrid is not null&&EditorGrid.ActualWidth>0)
            {
                const double leftMin=220,leftCap=520,centerMin=300,rightMin=280,rightCap=700;
                if(_effectsWindow is not null)
                {
                    var maxLeft=Math.Clamp(EditorGrid.ActualWidth-10-centerMin,leftMin,leftCap);
                    var left=Math.Clamp(_leftPanelWidth,leftMin,maxLeft);
                    EditorGrid.ColumnDefinitions[0].MinWidth=leftMin;
                    EditorGrid.ColumnDefinitions[0].MaxWidth=maxLeft;
                    EditorGrid.ColumnDefinitions[0].Width=new GridLength(left);
                }
                else
                {
                    var budget=Math.Max(leftMin+rightMin,EditorGrid.ActualWidth-20-centerMin);
                    var left=Math.Clamp(_leftPanelWidth,leftMin,leftCap);
                    var right=Math.Clamp(_rightPanelWidth,rightMin,rightCap);
                    if(left+right>budget)
                    {
                        var extraLeft=left-leftMin;
                        var extraRight=right-rightMin;
                        var extraTotal=extraLeft+extraRight;
                        var allowedExtra=Math.Max(0,budget-leftMin-rightMin);
                        if(extraTotal>0)
                        {
                            left=leftMin+extraLeft*(allowedExtra/extraTotal);
                            right=rightMin+extraRight*(allowedExtra/extraTotal);
                        }
                        else
                        {
                            left=leftMin;
                            right=rightMin;
                        }
                    }
                    var maxLeft=Math.Clamp(EditorGrid.ActualWidth-20-centerMin-right,leftMin,leftCap);
                    var maxRight=Math.Clamp(EditorGrid.ActualWidth-20-centerMin-left,rightMin,rightCap);
                    EditorGrid.ColumnDefinitions[0].MinWidth=leftMin;
                    EditorGrid.ColumnDefinitions[0].MaxWidth=maxLeft;
                    EditorGrid.ColumnDefinitions[0].Width=new GridLength(left);
                    EditorGrid.ColumnDefinitions[4].MinWidth=rightMin;
                    EditorGrid.ColumnDefinitions[4].MaxWidth=maxRight;
                    EditorGrid.ColumnDefinitions[4].Width=new GridLength(right);
                }
            }
            if(EditorGrid is not null&&EditorGrid.ActualHeight>0)
            {
                if(_previewWindow is not null)
                {
                    EditorGrid.RowDefinitions[0].MinHeight=0;
                    EditorGrid.RowDefinitions[0].Height=new GridLength(0);
                    EditorGrid.RowDefinitions[1].Height=new GridLength(0);
                    EditorGrid.RowDefinitions[2].MinHeight=_focusMode?0:180;
                    EditorGrid.RowDefinitions[2].MaxHeight=double.PositiveInfinity;
                    EditorGrid.RowDefinitions[2].Height=_focusMode?new GridLength(0):new GridLength(1,GridUnitType.Star);
                }
                else if(_focusMode)
                {
                    EditorGrid.RowDefinitions[0].MinHeight=180;
                    EditorGrid.RowDefinitions[0].Height=new GridLength(1,GridUnitType.Star);
                    EditorGrid.RowDefinitions[1].Height=new GridLength(0);
                    EditorGrid.RowDefinitions[2].MinHeight=0;
                    EditorGrid.RowDefinitions[2].MaxHeight=double.PositiveInfinity;
                    EditorGrid.RowDefinitions[2].Height=new GridLength(0);
                }
                else
                {
                    var previewMin=Math.Min(200,Math.Max(120,EditorGrid.ActualHeight-7-180));
                    var maxTimeline=Math.Max(180,Math.Min(700,EditorGrid.ActualHeight-7-previewMin));
                    EditorGrid.RowDefinitions[0].MinHeight=previewMin;
                    EditorGrid.RowDefinitions[0].Height=new GridLength(1,GridUnitType.Star);
                    EditorGrid.RowDefinitions[1].Height=new GridLength(7);
                    EditorGrid.RowDefinitions[2].MinHeight=180;
                    EditorGrid.RowDefinitions[2].MaxHeight=maxTimeline;
                    EditorGrid.RowDefinitions[2].Height=new GridLength(Math.Clamp(_timelineHeight,180,maxTimeline));
                }
            }
        }finally{_panelLayoutBusy=false;}
    }
    private void PropertiesLayoutChanged(object sender,SizeChangedEventArgs e)=>ApplyPanelSizes();
    private void PanelResizeStarted(object sender,DragStartedEventArgs e){ApplyPanelSizes();_panelDragging=true;}
    private void LayerHeightChanged(object sender,DragCompletedEventArgs e)
    {_panelDragging=false;if(!e.Canceled&&LayerPanelRow.ActualHeight>0)_layerPanelHeight=Math.Clamp(LayerPanelRow.ActualHeight,110,480);ApplyPanelSizes();SaveWorkspace();}
    private void LeftWidthChanged(object sender,DragCompletedEventArgs e)
    {_panelDragging=false;if(!e.Canceled&&EditorGrid.ColumnDefinitions[0].ActualWidth>0)_leftPanelWidth=Math.Clamp(EditorGrid.ColumnDefinitions[0].ActualWidth,220,520);ApplyPanelSizes();SaveWorkspace();}
    private void EffectsWidthChanged(object sender,DragCompletedEventArgs e)
    {_panelDragging=false;if(!e.Canceled&&EditorGrid.ColumnDefinitions[4].ActualWidth>0)_rightPanelWidth=Math.Clamp(EditorGrid.ColumnDefinitions[4].ActualWidth,280,700);ApplyPanelSizes();SaveWorkspace();}
    private void LeftSplitterDoubleClick(object sender,MouseButtonEventArgs e)
    {if(sender is Thumb { IsDragging:true } t)t.CancelDrag();_panelDragging=false;_leftPanelWidth=280;ApplyPanelSizes();SaveWorkspace();e.Handled=true;}
    private void EffectsSplitterDoubleClick(object sender,MouseButtonEventArgs e)
    {if(sender is Thumb { IsDragging:true } t)t.CancelDrag();_panelDragging=false;_rightPanelWidth=335;ApplyPanelSizes();SaveWorkspace();e.Handled=true;}
    private void TimelineSplitterDoubleClick(object sender,MouseButtonEventArgs e)
    {if(_focusMode||_previewWindow is not null)return;if(sender is Thumb { IsDragging:true } t)t.CancelDrag();_panelDragging=false;_timelineHeight=245;ApplyPanelSizes();SaveWorkspace();e.Handled=true;}
    private void TimelineScrollChanged(object sender,ScrollChangedEventArgs e){if(Math.Abs(e.ViewportWidthChange)>.1)TimelineEditor?.Refresh();}
    private void CloseAreaDetails(object sender,RoutedEventArgs e)=>AreaDetailsPopup.IsOpen=false;
    private void ToolbarMouseWheel(object sender,MouseWheelEventArgs e)
    {if(sender is ScrollViewer scroll){scroll.ScrollToHorizontalOffset(scroll.HorizontalOffset-e.Delta);e.Handled=true;}}
    private void ScrollToolbarLeft(object sender,RoutedEventArgs e){if(sender is FrameworkElement {Tag:string name}&&FindName(name) is ScrollViewer scroll)scroll.ScrollToHorizontalOffset(scroll.HorizontalOffset-220);}
    private void ScrollToolbarRight(object sender,RoutedEventArgs e){if(sender is FrameworkElement {Tag:string name}&&FindName(name) is ScrollViewer scroll)scroll.ScrollToHorizontalOffset(scroll.HorizontalOffset+220);}
    private void LayerEyeClick(object sender,RoutedEventArgs e){if(sender is FrameworkElement{DataContext:EffectRegion r})ToggleLayer(r);e.Handled=true;}
    private void LayerLockClick(object sender,RoutedEventArgs e){if(sender is FrameworkElement{DataContext:EffectRegion r})ToggleLayerLock(r);e.Handled=true;}
    private void LayerFoldClick(object sender,RoutedEventArgs e){if(!_busy&&sender is FrameworkElement{DataContext:EffectRegion{Role:RegionRole.Group} r})ToggleCollapse(r);e.Handled=true;}
    public void ToggleLayerLock(EffectRegion r)
    {
        if(_busy||_current is null||!_current.Regions.Contains(r))return;Preview.CancelGesture();r.Locked=!r.Locked;
        if(_region?.Id==r.Id){var old=_loading;_loading=true;RegionLockCheck.IsChecked=r.Locked;_loading=old;}
        RefreshRegionList();Preview.InvalidateVisual();TimelineEditor.InvalidateVisual();MarkEdited();
    }
    public void RenameLayer(EffectRegion r,string name)
    {
        if(_busy||_current is null||!_current.Regions.Contains(r))return;name=name.Trim();if(name.Length==0||name==r.Name)return;
        r.Name=name.Length>100?name[..100]:name;if(_region?.Id==r.Id){var old=_loading;_loading=true;RegionNameBox.Text=r.Name;_loading=old;}
        RefreshRegionList();LoadContextLabels();UpdateSelectionLabel();Preview.InvalidateVisual();TimelineEditor.InvalidateVisual();MarkEdited();
    }
    public void BeginLayerRename(EffectRegion r)
    {
        if(_busy)return;_layerDragSource=null;RegionList.ScrollIntoView(r);RegionList.UpdateLayout();
        if(RegionList.ItemContainerGenerator.ContainerFromItem(r) is ListBoxItem item&&Child<TextBox>(item,"LayerNameEditor") is { } editor)
        {if(Child<StackPanel>(item,"LayerNameDisplay") is { } display)display.Visibility=Visibility.Collapsed;editor.Text=r.Name;editor.Visibility=Visibility.Visible;editor.Focus();editor.SelectAll();}
    }
    private void LayerNameDoubleClick(object sender,MouseButtonEventArgs e){if(e.ClickCount>=2&&sender is FrameworkElement{DataContext:EffectRegion r}){BeginLayerRename(r);e.Handled=true;}}
    private void FinishLayerRename(TextBox editor,bool commit)
    {
        if(editor.Visibility!=Visibility.Visible)return;var region=editor.DataContext as EffectRegion;var name=editor.Text;editor.Visibility=Visibility.Collapsed;
        if(Ancestor<ListBoxItem>(editor) is { } item&&Child<StackPanel>(item,"LayerNameDisplay") is { } display)display.Visibility=Visibility.Visible;
        if(commit&&region is not null)RenameLayer(region,name);
    }
    private void LayerNameLostFocus(object sender,KeyboardFocusChangedEventArgs e){if(sender is TextBox editor)FinishLayerRename(editor,true);}
    private void LayerNameKeyDown(object sender,KeyEventArgs e){if(sender is TextBox editor&&e.Key is Key.Enter or Key.Escape){FinishLayerRename(editor,e.Key==Key.Enter);RegionList.Focus();e.Handled=true;}}
    private void LayerListKeyDown(object sender,KeyEventArgs e)
    {if(e.Key==Key.F2&&RegionList.SelectedItem is EffectRegion r&&!IsTextEditing(e.OriginalSource as DependencyObject)){BeginLayerRename(r);e.Handled=true;}}
    private void LayerPointerDown(object sender,MouseButtonEventArgs e)
    {if(Ancestor<ButtonBase>(e.OriginalSource as DependencyObject) is not null||Ancestor<TextBox>(e.OriginalSource as DependencyObject) is not null){_layerDragSource=null;return;}_layerDragStart=e.GetPosition(RegionList);_layerDragSource=Ancestor<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext as EffectRegion;}
    private void LayerPointerMove(object sender,MouseEventArgs e)
    {
        if(_busy||e.LeftButton!=MouseButtonState.Pressed||_layerDragSource is null)return;var p=e.GetPosition(RegionList);
        if(Math.Abs(p.X-_layerDragStart.X)<SystemParameters.MinimumHorizontalDragDistance&&Math.Abs(p.Y-_layerDragStart.Y)<SystemParameters.MinimumVerticalDragDistance)return;
        var source=_layerDragSource;_layerDragSource=null;var data=new DataObject("LiveCG.Layer",source.Id.ToString());try{DragDrop.DoDragDrop(RegionList,data,DragDropEffects.Move);}finally{ClearLayerDropGuide();}
    }
    private bool TryLayerDrop(DragEventArgs e,out EffectRegion? source,out EffectRegion? target,out ListBoxItem? item)
    {
        source=null;item=Ancestor<ListBoxItem>(e.OriginalSource as DependencyObject);target=item?.DataContext as EffectRegion;
        if(_busy||_current is null||!e.Data.GetDataPresent("LiveCG.Layer")||!Guid.TryParse(e.Data.GetData("LiveCG.Layer")?.ToString(),out var id))return false;
        source=_current.Regions.FirstOrDefault(r=>r.Id==id);return source is not null&&target is not null&&source.Id!=target.Id&&source.ParentId==target.ParentId;
    }
    private void LayerDragOver(object sender,DragEventArgs e)
    {
        if(!e.Data.GetDataPresent("LiveCG.Layer"))return;ClearLayerDropGuide();e.Effects=DragDropEffects.None;
        if(TryLayerDrop(e,out _,out _,out var item)&&item is not null)
        {_layerDropItem=item;item.BorderBrush=Brushes.Aquamarine;item.BorderThickness=e.GetPosition(item).Y<item.ActualHeight/2?new Thickness(0,2,0,0):new Thickness(0,0,0,2);e.Effects=DragDropEffects.Move;
            if(Ancestor<ScrollViewer>(item) is { } scroll){var y=e.GetPosition(scroll).Y;if(y<24)scroll.LineUp();else if(y>scroll.ActualHeight-24)scroll.LineDown();}}
        e.Handled=true;
    }
    private void LayerDrop(object sender,DragEventArgs e)
    {if(!e.Data.GetDataPresent("LiveCG.Layer"))return;if(TryLayerDrop(e,out var source,out var target,out var item)&&item is not null)DropLayerAt(source!,target!,e.GetPosition(item).Y>=item.ActualHeight/2);ClearLayerDropGuide();e.Handled=true;}
    public bool DropLayerAt(EffectRegion source,EffectRegion target,bool after)
    {
        if(_busy||_current is null||source.Id==target.Id||source.ParentId!=target.ParentId)return false;
        var siblings=_current.Regions.Where(r=>r.ParentId==source.ParentId).ToList();var from=siblings.IndexOf(source);var to=siblings.IndexOf(target);if(from<0||to<0)return false;
        var next=to+(after?1:0)-(from<to?1:0);if(next==from)return false;MoveLayer(source,next-from);return true;
    }
    private void ClearLayerDropGuide(){if(_layerDropItem is not null){_layerDropItem.ClearValue(Control.BorderBrushProperty);_layerDropItem.ClearValue(Control.BorderThicknessProperty);_layerDropItem=null;}}
}
