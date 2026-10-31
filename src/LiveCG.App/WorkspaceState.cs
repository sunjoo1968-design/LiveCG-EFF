using System.IO;
using System.Text.Json;
using System.Windows;

namespace LiveCG.App;

public record WindowPlacement
{
    public double Left { get; set; }=100;
    public double Top { get; set; }=80;
    public double Width { get; set; }=1000;
    public double Height { get; set; }=760;
    public bool Maximized { get; set; }
    public static WindowPlacement Capture(Window window)
    {
        var b=window.RestoreBounds;var restore=window.WindowState!=WindowState.Normal&&!b.IsEmpty&&double.IsFinite(b.Left)&&double.IsFinite(b.Top)&&double.IsFinite(b.Width)&&double.IsFinite(b.Height)&&b.Width>0&&b.Height>0;
        return new WindowPlacement{Left=restore?b.Left:window.Left,Top=restore?b.Top:window.Top,Width=restore?b.Width:double.IsFinite(window.Width)&&window.Width>0?window.Width:window.ActualWidth,Height=restore?b.Height:double.IsFinite(window.Height)&&window.Height>0?window.Height:window.ActualHeight,Maximized=window.WindowState==WindowState.Maximized}.Normalized();
    }
    public WindowPlacement Normalized()=>this with{Left=double.IsFinite(Left)?Left:100,Top=double.IsFinite(Top)?Top:80,Width=double.IsFinite(Width)&&Width>0?Width:1000,Height=double.IsFinite(Height)&&Height>0?Height:760};
    public void Apply(Window window)
    {
        if(!double.IsFinite(Left+Top+Width+Height))return;
        var vw=SystemParameters.VirtualScreenWidth;var vh=SystemParameters.VirtualScreenHeight;
        window.Width=Math.Clamp(Width,Math.Min(window.MinWidth,vw),Math.Max(window.MinWidth,vw));window.Height=Math.Clamp(Height,Math.Min(window.MinHeight,vh),Math.Max(window.MinHeight,vh));
        window.Left=Math.Clamp(Left,SystemParameters.VirtualScreenLeft,SystemParameters.VirtualScreenLeft+Math.Max(0,vw-window.Width));
        window.Top=Math.Clamp(Top,SystemParameters.VirtualScreenTop,SystemParameters.VirtualScreenTop+Math.Max(0,vh-window.Height));
        if(Maximized)window.WindowState=WindowState.Maximized;
    }
}
public class WorkspaceState
{
    public WindowPlacement? Main { get; set; }
    public WindowPlacement? Preview { get; set; }
    public WindowPlacement? Effects { get; set; }
    public bool PreviewDetached { get; set; }
    public bool EffectsDetached { get; set; }
    public bool FocusMode { get; set; }
    public bool AutoFocus { get; set; }=true;
    public double RightWidth { get; set; }=335;
    public double LayerPanelHeight { get; set; }=240;
    public double LeftWidth { get; set; }=280;
    public double TimelineHeight { get; set; }=245;
    public bool OutputExpanded { get; set; }=true;
    public static string Path=>System.IO.Path.Combine(LiveCG.Core.DocumentStore.DataDirectory,"workspace.json");
    private WorkspaceState Normalize(){Main=Main?.Normalized();Preview=Preview?.Normalized();Effects=Effects?.Normalized();RightWidth=double.IsFinite(RightWidth)?Math.Clamp(RightWidth,300,700):335;LayerPanelHeight=double.IsFinite(LayerPanelHeight)?Math.Clamp(LayerPanelHeight,110,480):240;LeftWidth=double.IsFinite(LeftWidth)?Math.Clamp(LeftWidth,240,450):280;TimelineHeight=double.IsFinite(TimelineHeight)?Math.Clamp(TimelineHeight,170,700):245;return this;}
    public static WorkspaceState Load(){try{return (JsonSerializer.Deserialize<WorkspaceState>(File.ReadAllText(Path))??new()).Normalize();}catch(IOException){return new();}catch(JsonException){return new();}}
    public void Save()
    {
        var json=JsonSerializer.Serialize(Normalize());Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);var temporary=Path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.WriteAllText(temporary,json);File.Move(temporary,Path,true);}finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
}
