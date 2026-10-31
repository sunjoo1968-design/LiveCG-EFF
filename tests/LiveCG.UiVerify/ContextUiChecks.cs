using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using LiveCG.App;
using LiveCG.Core;
using SkiaSharp;

internal static class ContextUiChecks
{
    public static void Run(MainWindow window,string folder,Action<bool,string> check)
    {
        object? Call(string n,params object?[] values){var m=typeof(MainWindow).GetMethod(n,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public)!;return m.Invoke(window,m.GetParameters().Select((p,i)=>i<values.Length?values[i]:p.DefaultValue).ToArray());}
        T C<T>(string n)=>(T)window.FindName(n);T F<T>(string n)=>(T)typeof(MainWindow).GetField(n,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
        void Pump()=>Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
        bool Inside(DependencyObject item,DependencyObject parent){for(var n=item;n is not null;n=VisualTreeHelper.GetParent(n))if(n==parent)return true;return false;}
        var path=Path.Combine(folder,"타임라인 예제.png");var first=new EffectRegion{Name="가격",Bounds=new(.12,.25,.4,.4),Clips=[new(){Options=new(){Kind=EffectKind.Zoom}}]};var second=new EffectRegion{Name="배지",Bounds=new(.55,.25,.25,.35),Clips=[new(){Options=new(){Kind=EffectKind.Ripple}}]};
        window.ApplyDocument(new(){Jobs=[new(){SourcePath=path,Width=960,Height=540,Regions=[first,second]}]});Call("SelectRegion",first,first.Clips[0],false);Pump();
        var area=C<Border>("AreaToolsPanel");var tabs=C<TabControl>("EffectsTabs");
        check(!Inside(area,tabs)&&Inside(area,C<Border>("PreviewPanel")),"quick area controls live outside clip properties and outside the pixel viewport");
        check(!C<Expander>("ClipTimingSection").IsExpanded&&!C<System.Windows.Controls.Primitives.Popup>("AreaDetailsPopup").IsOpen,"advanced timing and area details start collapsed");
        Call("ToggleSelectedLayer",window,new RoutedEventArgs());Pump();check(!first.Enabled&&ReferenceEquals(C<ListBox>("RegionList").SelectedItem,first),"quick visibility label follows hidden state immediately");Call("ToggleSelectedLayer",window,new RoutedEventArgs());Pump();check(first.Enabled&&ReferenceEquals(C<ListBox>("RegionList").SelectedItem,first),"quick visibility label follows restored state immediately");
        var assetCount=C<ListBox>("AssetList").Items.Count;C<TextBox>("EffectSearchBox").Text="unrelated-no-effect";Pump();check(C<ListBox>("AssetList").Items.Count==assetCount,"library and personal-asset searches have independent state");C<TextBox>("EffectSearchBox").Text="";
        check(C<Expander>("ClipAppearanceSection").Visibility==Visibility.Collapsed,"non-color zoom effect hides irrelevant appearance controls");
        foreach(var i in new[]{0,1,2,3}){tabs.SelectedIndex=i;Pump();check(C<ComboBox>("AreaContentMode").IsVisible&&C<CheckBox>("AreaSoftEnabled").IsVisible,"quick controls remain accessible on property tab "+i);}
        var mode=C<ComboBox>("AreaContentMode");mode.SelectedIndex=1;Pump();check(first.ContentOnly&&first.CutoutEnabled,"quick content mode enables single-pass background separation");
        window.SoftValues["Width"].EditValue(19);check(first.Softness.Width==19,"quick width changes without opening the clip tab");
        using(var source=SKBitmap.Decode(path))
        {
            var dialog=new ObjectSelectionWindow(source,first){Owner=window,ShowActivated=false,ShowInTaskbar=false,WindowStartupLocation=WindowStartupLocation.Manual,Left=-15000,Top=-15000};
            try
            {
                dialog.Show();Pump();check(dialog.Canvas.Buffer.Alpha.SequenceEqual(RegionSeparation.InitialMask(source,first).Alpha),"precision editor continues the exact quick separation result");
                check(dialog.Title.Contains("정밀 분리")&&dialog.FitBoundsCheck.IsChecked==false,"precision editor defaults to preserving selection bounds");
                dialog.Canvas.Buffer.Brush(20,20,5,true);check(dialog.PrepareResult()&&dialog.SelectedBounds==first.Bounds,"brush refinement keeps the original selected geometry");
                Call("ResetHistory");window.ApplyObjectSelection(dialog.SelectedBounds,dialog.SelectedCutout,SKColors.White,17);Pump();
                var current=F<ImageJob>("_current").Regions[0];check(current.ContentOnly&&current.CutoutEnabled&&current.Background==BackgroundMode.Solid&&current.ContentTolerance==17,"precision apply shares mode, background color and tolerance with quick controls");
                var saved=current.Cutout!.MaskPng;mode.SelectedIndex=0;Pump();check(!current.ContentOnly&&!current.CutoutEnabled&&current.Cutout.MaskPng==saved,"whole-region mode retains manual corrections for later");
                mode.SelectedIndex=1;Pump();check(current.CutoutEnabled&&current.Cutout.MaskPng==saved,"re-enabling content mode restores manual mask without re-extraction");
                var reopen=new ObjectSelectionWindow(source,current);check(reopen.Canvas.Buffer.Alpha.SequenceEqual(MaskBuffer.Decode(current.Cutout).Alpha),"reopening precision editor preserves hand-painted mask");reopen.Close();
                Call("UndoEdit",window,new RoutedEventArgs());Pump();check(!F<ImageJob>("_current").Regions[0].CutoutEnabled,"quick separation toggle participates in editor undo");
                StudioUiChecks.Capture(dialog,Path.Combine(folder,"precision-v012.png"),1120,800);
            }finally{dialog.Close();}
        }
        var job=F<ImageJob>("_current");var selected=job.Regions[1];Call("SelectRegion",selected,selected.Clips[0],false);tabs.SelectedIndex=1;Pump();
        check(C<Expander>("ClipAppearanceSection").Visibility==Visibility.Visible,"ripple reveals its relevant color controls");
        check(C<TextBlock>("ContextPathText").Text.Contains("배지")&&C<TextBlock>("ContextPathText").Text.Contains("원형"),"selected region and effect remain identified in the context header");
        Call("Notify","클립 기본 · 시간·반복 · 색상 분류");StudioUiChecks.Capture(window,Path.Combine(folder,"context-clips-v012.png"),1580,1040);
        tabs.SelectedIndex=3;C<TabControl>("GroupMaskTabs").SelectedIndex=1;Pump();check(C<StackPanel>("MaskEditorPanel").Visibility==Visibility.Collapsed,"empty mask selection hides unrelated mask property fields");
        var mask=new MaskTrack{Name="배지 마스크",Bounds=selected.Bounds,Duration=2};selected.Masks.Add(mask);Call("SelectMask",selected,mask);Pump();
        check(C<TabControl>("GroupMaskTabs").SelectedIndex==1&&C<StackPanel>("MaskEditorPanel").Visibility==Visibility.Visible,"timeline mask selection opens dedicated mask editor with grouped controls");
        check(C<TextBlock>("ContextPathText").Text.Contains("마스크"),"context header identifies selected mask instead of a stale clip");
        Call("Notify","마스크 모양 · 시간 · 움직임 · 가장자리 · 끝난 뒤");StudioUiChecks.Capture(window,Path.Combine(folder,"context-masks-v012.png"),1580,1040);
        tabs.SelectedIndex=0;Call("Notify","영역 빠른 조절 · 클립과 독립 · 정밀 분리 통합");Pump();StudioUiChecks.Capture(window,Path.Combine(folder,"context-overview-v012.png"),1580,1040);
    }
}
