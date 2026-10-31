using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using LiveCG.App;
using LiveCG.Core;

internal static class LayerPanelUiChecks
{
    public static void Run(MainWindow window,string folder,Action<bool,string> check)
    {
        object? Call(string name,params object?[] args){var method=typeof(MainWindow).GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public)!;return method.Invoke(window,method.GetParameters().Select((p,i)=>i<args.Length?args[i]:p.DefaultValue).ToArray());}
        T C<T>(string name)=>(T)window.FindName(name);
        T F<T>(string name)=>(T)typeof(MainWindow).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
        IEnumerable<T> Desc<T>(DependencyObject root)where T:DependencyObject{for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);if(child is T match)yield return match;foreach(var next in Desc<T>(child))yield return next;}}
        void Pump()=>Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
        var path=Path.Combine(folder,"타임라인 예제.png");
        var group=new EffectRegion{Name="쿠폰 그룹",Role=RegionRole.Group,Bounds=new(.1,.2,.65,.6),Clips=[new(){Duration=1,Options=new(){Kind=EffectKind.Zoom}}]};
        var child=new EffectRegion{Name="배지",Role=RegionRole.EffectArea,ParentId=group.Id,Bounds=new(.6,.2,.15,.3),Shape=RegionShape.Ellipse,Clips=[new(){Start=1,Options=new(){Kind=EffectKind.Ripple}}],Masks=[new(){Name="배지 마스크"}]};
        var price=new EffectRegion{Name="가격",Bounds=new(.1,.8,.7,.1),Clips=[new(){Options=new(){Kind=EffectKind.Bounce}}]};
        window.ApplyDocument(new(){Jobs=[new(){SourcePath=path,Width=960,Height=540,Regions=[group,child,price]}]});Call("SelectRegion",child,child.Clips[0],false);Call("Notify","한 줄 도구 · 레이어 선택 · 높이와 너비 조절");
        StudioUiChecks.Capture(window,Path.Combine(folder,"layers-v013.png"),1600,1000);Pump();
        var layers=C<ListBox>("RegionList");var preview=C<PreviewControl>("Preview");var row=C<StackPanel>("SoftnessPanel");var tools=C<Border>("AreaToolsPanel");
        check(layers.IsVisible&&layers.Items.Count==3&&C<Border>("LayerListPanel").IsVisible,"layer list is expanded by default with group children");
        check(ReferenceEquals(layers.SelectedItem,child)&&preview.SelectedRegion==child,"layer selection matches preview and timeline");
        var childItem=(ListBoxItem)layers.ItemContainerGenerator.ContainerFromItem(child);var texts=Desc<TextBlock>(childItem).Select(t=>t.Text).ToList();
        check(texts.Any(t=>t.Contains("마스크 1"))&&texts.Any(t=>t==child.Name),"layer row identifies name and effect/mask counts");
        check(Desc<Image>(childItem).Any(image=>image.Source is not null),"layer thumbnail displays the original selected content");
        window.SelectMask(child,child.Masks[0]);Call("DeleteMask",window,new RoutedEventArgs());Pump();childItem=(ListBoxItem)layers.ItemContainerGenerator.ContainerFromItem(child);
        check(child.Masks.Count==0&&!Desc<TextBlock>(childItem).Any(t=>t.Text.Contains("마스크 1")),"deleting a mask immediately refreshes the layer count badge");
        var converter=new LayerRowConverter();check(((Thickness)converter.Convert(child,typeof(Thickness),"indent",CultureInfo.InvariantCulture)).Left==16&&((Thickness)converter.Convert(group,typeof(Thickness),"indent",CultureInfo.InvariantCulture)).Left==0,"child layer has visible hierarchy indentation");
        check(row.Orientation==Orientation.Horizontal&&C<StackPanel>("DrawingToolbar").Orientation==Orientation.Horizontal&&Math.Abs(tools.ActualHeight-40)<.1,"drawing and area tools stay in fixed single rows");
        check(new[]{"AreaToolsExpander","AreaPinCheck","AreaVisibilityButton","AreaToolsHost"}.All(n=>window.FindName(n) is null),"obsolete collapse pin and detached area controls are absent");
        C<Popup>("AreaDetailsPopup").IsOpen=true;Pump();check(C<Popup>("AreaDetailsPopup").IsOpen&&Math.Abs(tools.ActualHeight-40)<.1,"advanced area popup does not grow the toolbar");
        Call("CloseAreaDetails",window,new RoutedEventArgs());
        var visibleButtons=Desc<Button>(childItem).Where(b=>b.ToolTip?.ToString()=="표시/숨김").ToList();visibleButtons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();check(!child.Enabled,"layer eye button edits visibility in the shared region model");
        Call("UndoEdit",window,new RoutedEventArgs());Pump();var job=F<ImageJob>("_current");child=job.Regions.First(r=>r.Id==child.Id);group=job.Regions.First(r=>r.Id==group.Id);price=job.Regions.First(r=>r.Id==price.Id);check(child.Enabled,"layer visibility changes support undo");
        window.ToggleLayerLock(child);Pump();check(child.Locked&&preview.SelectedRegion?.Locked==true,"layer lock synchronizes with preview immediately");window.ToggleLayerLock(child);
        window.BeginLayerRename(child);Pump();childItem=(ListBoxItem)layers.ItemContainerGenerator.ContainerFromItem(child);var editor=Desc<TextBox>(childItem).First(t=>t.Name=="LayerNameEditor");
        check(editor.IsVisible,"F2/double-click opens in-place layer name editing");editor.Text="LIVE 배지";Call("FinishLayerRename",editor,true);Pump();check(child.Name=="LIVE 배지"&&C<TextBlock>("ContextPathText").Text.Contains("LIVE 배지"),"inline layer rename updates shared context");
        check(!window.DropLayerAt(child,price,false)&&child.ParentId==group.Id,"drag ordering cannot silently remove a region from its group");
        check(window.DropLayerAt(price,group,false)&&job.Regions[0]==price&&child.ParentId==group.Id,"layer drag reorders siblings and preserves group membership");
        Call("ToggleCollapse",group);Pump();check(layers.Items.Count==2,"group fold hides its child rows");Call("ToggleCollapse",group);Pump();check(layers.Items.Count==3,"group unfold restores child rows");
        window.SetLayerPanelSize(310,460,true);Pump();var saved=WorkspaceState.Load();check(Math.Abs(saved.LayerPanelHeight-310)<.1&&Math.Abs(saved.RightWidth-460)<.1,"layer height and right-panel width persist in workspace");
        saved.Main=null;saved.Save();var oldRestore=Environment.GetEnvironmentVariable("LIVECG_DISABLE_WORKSPACE_RESTORE");Environment.SetEnvironmentVariable("LIVECG_DISABLE_WORKSPACE_RESTORE",null);
        var restored=new MainWindow{WindowStartupLocation=WindowStartupLocation.Manual,Left=-15000,Top=-15000,ShowActivated=false,ShowInTaskbar=false};
        try{restored.Show();Pump();check(restored.PreferredLayerHeight==310&&restored.PreferredRightWidth==460,"reopening the application restores saved layer-panel dimensions");}finally{restored.Close();Environment.SetEnvironmentVariable("LIVECG_DISABLE_WORKSPACE_RESTORE",oldRestore);}
        check(C<GridSplitter>("LayerHeightSplitter").ResizeDirection==GridResizeDirection.Rows&&C<GridSplitter>("EffectsWidthSplitter").ResizeDirection==GridResizeDirection.Columns&&C<RowDefinition>("LayerPanelRow").ActualHeight>280,"independent splitters resize layer height and property width");
        StudioUiChecks.Capture(window,Path.Combine(folder,"layers-resized-v013.png"),1600,1000);
        StudioUiChecks.Capture(window,Path.Combine(folder,"layers-wide-v013.png"),1920,1080);check(Math.Abs(tools.ActualHeight-40)<.1&&preview.ActualHeight>300,"wide layout keeps the same single-row controls with a larger preview");
        Call("DetachEffects",window,new RoutedEventArgs());Pump();check(window.DetachedEffectsWindow is not null&&layers.IsVisible,"layer panel travels with detached effect properties");window.DetachedEffectsWindow!.Close();Pump();check(window.DetachedEffectsWindow is null&&Math.Abs(window.PreferredRightWidth-460)<.1,"returning properties keeps the preferred panel size");
        window.SetLayerPanelSize(240,335);StudioUiChecks.Capture(window,Path.Combine(folder,"layers-narrow-v013.png"),1160,780);Pump();
        var drawing=C<ScrollViewer>("DrawingToolbarScroll");var area=C<ScrollViewer>("AreaToolbarScroll");drawing.ScrollToRightEnd();area.ScrollToRightEnd();Pump();
        check(drawing.ScrollableWidth>0&&drawing.HorizontalOffset>0&&area.ScrollableWidth>0&&area.HorizontalOffset>0,"narrow layout provides horizontal access to every single-row tool");
        check(preview.ActualHeight>100&&Math.Abs(tools.ActualHeight-40)<.1,"narrow window retains preview space without toolbar wrapping");
        drawing.ScrollToLeftEnd();area.ScrollToLeftEnd();
        var many=Enumerable.Range(1,100).Select(i=>new EffectRegion{Name=$"영역 {i:000}",Bounds=new(.1,.2,.4,.4)}).ToList();window.ApplyDocument(new(){Jobs=[new(){SourcePath=path,Width=960,Height=540,Regions=many}]});Pump();layers.ScrollIntoView(many[^1]);Pump();layers.SelectedItem=many[^1];Pump();
        var scroll=Desc<ScrollViewer>(layers).First();check(layers.Items.Count==100&&scroll.ScrollableHeight>0&&scroll.VerticalOffset>0&&preview.SelectedRegion==many[^1],"100 layers scroll internally and selecting the last one updates preview");
        check(Desc<ListBoxItem>(layers).Count()<50,"large layer list virtualizes rows instead of growing the window");
        Call("Notify","100개 레이어 · 내부 스크롤");StudioUiChecks.Capture(window,Path.Combine(folder,"layers-many-v013.png"),1600,1000);
        var bad=new WorkspaceState{RightWidth=double.PositiveInfinity,LayerPanelHeight=double.NaN};bad.Save();saved=WorkspaceState.Load();check(saved.RightWidth==335&&saved.LayerPanelHeight==240,"invalid panel measurements normalize to safe finite defaults");
        File.WriteAllText(WorkspaceState.Path,"{\"AreaToolsDetached\":true,\"AreaToolsExpanded\":false,\"AreaToolsPinned\":false,\"RightWidth\":380,\"LayerPanelHeight\":220}");saved=WorkspaceState.Load();check(saved.RightWidth==380&&saved.LayerPanelHeight==220,"old area-window preferences are ignored while valid panel dimensions migrate");
        window.SetLayerPanelSize(240,335,true);Call("Notify","한 줄 도구 · 레이어 패널");
    }
}
