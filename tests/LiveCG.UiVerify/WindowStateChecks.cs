using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using LiveCG.App;
using LiveCG.Core;

internal static class WindowStateChecks
{
    public static int Reproduce(string root,bool minimal=false)
    {
        var folder=Path.Combine(root,"artifacts","v081-window");Directory.CreateDirectory(folder);Environment.SetEnvironmentVariable("LIVECG_DATA_DIR",Path.Combine(folder,"user-data"));
        void Pump()=>Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
        object? Call(MainWindow w,string method)=>typeof(MainWindow).GetMethod(method,BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(w,[w,new RoutedEventArgs()]);
        var errors=0;var w=new MainWindow{WindowStartupLocation=WindowStartupLocation.Manual,Left=-12000,Top=-12000,ShowActivated=false,ShowInTaskbar=false};w.Show();Pump();
        try
        {
            foreach(var kind in minimal?new[]{"Preview"}:new[]{"Preview","Effects"})foreach(var mode in minimal?new[]{"maximized"}:new[]{"normal","maximized","restored"})
            {
                Call(w,"Detach"+kind);Pump();var detached=kind=="Preview"?w.DetachedPreviewWindow!:w.DetachedEffectsWindow!;if(mode!="normal"){detached.WindowState=WindowState.Maximized;Pump();}if(mode=="restored"){detached.WindowState=WindowState.Normal;Pump();}
                detached.Close();Pump();
                try
                {
                    typeof(MainWindow).GetMethod("SaveWorkspace",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(w,null);
                    Console.WriteLine("PASS return "+kind+" / "+mode+" serializes finite workspace");
                }
                catch(TargetInvocationException e){errors++;Console.WriteLine("FAIL return "+kind+" / "+mode+": "+e.InnerException);}
            }
        }
        finally{typeof(MainWindow).GetField("_dirty",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(w,false);typeof(MainWindow).GetField("_workspace",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(w,new WorkspaceState());w.Close();}
        Console.WriteLine("WINDOW_REPRO_ERRORS "+errors);return errors==0?0:1;
    }
    public static void Run(MainWindow w,Action<bool,string> check)
    {
        void Pump()=>Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
        object? Call(string method,params object?[] args)=>typeof(MainWindow).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(w,args);
        var jobs=typeof(MainWindow).GetField("_jobs",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(w);var before=JsonSerializer.Serialize(jobs);
        foreach(var kind in new[]{"Preview","Effects"})foreach(var mode in new[]{"normal","maximized","restored","minimized"})
        {
            Call("Detach"+kind,w,new RoutedEventArgs());Pump();var detached=kind=="Preview"?w.DetachedPreviewWindow!:w.DetachedEffectsWindow!;
            if(mode!="normal"){detached.WindowState=WindowState.Maximized;Pump();}if(mode=="restored"){detached.WindowState=WindowState.Normal;Pump();}if(mode=="minimized"){detached.WindowState=WindowState.Minimized;Pump();}
            var expected=WindowPlacement.Capture(detached);detached.Close();Pump();Call("SaveWorkspace");var workspace=WorkspaceState.Load();var saved=kind=="Preview"?workspace.Preview!:workspace.Effects!;
            check(saved.Width==expected.Width&&saved.Height==expected.Height&&saved.Left==expected.Left&&saved.Top==expected.Top&&saved.Maximized==expected.Maximized,"return "+kind+" / "+mode+" preserves finite pre-close placement");
        }
        check(before==JsonSerializer.Serialize(jobs),"returning detached windows preserves all existing image region and effect data");
        var corrupted=new WorkspaceState{Main=new(){Left=double.NaN,Top=double.PositiveInfinity,Width=double.NegativeInfinity,Height=0},LeftWidth=double.NaN,TimelineHeight=double.PositiveInfinity};corrupted.Save();var clean=WorkspaceState.Load();
        check(double.IsFinite(clean.Main!.Left+clean.Main.Top+clean.Main.Width+clean.Main.Height+clean.LeftWidth+clean.TimelineHeight)&&clean.Main.Width>0&&clean.Main.Height>0,"workspace save normalizes invalid layout values without permitting nonstandard JSON numbers");
        using(var doc=JsonDocument.Parse(File.ReadAllText(WorkspaceState.Path)))check(doc.RootElement.GetProperty("Main").GetProperty("Width").ValueKind==JsonValueKind.Number,"repaired workspace remains strict numeric JSON");
        var unshown=new Window();var placement=WindowPlacement.Capture(unshown);unshown.Close();check(double.IsFinite(placement.Left+placement.Top+placement.Width+placement.Height)&&placement.Width>0&&placement.Height>0,"unshown Auto-size window capture uses finite fallbacks");
        Call("SaveWorkspace");
        var exit=new MainWindow{WindowStartupLocation=WindowStartupLocation.Manual,Left=-12000,Top=-12000,ShowActivated=false,ShowInTaskbar=false};exit.Show();Pump();
        var sample=Path.Combine(DocumentStore.DataDirectory,"..","타임라인 예제.png");var edited=LiveCG.Testing.TestFixtures.Demo(Path.GetFullPath(sample));edited.Regions[0].Clips[0].Duration+=.2;exit.ApplyDocument(new(){Jobs=[edited]});typeof(MainWindow).GetField("_dirty",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(exit,false);
        typeof(MainWindow).GetMethod("DetachPreview",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(exit,[exit,new RoutedEventArgs()]);exit.DetachedPreviewWindow!.WindowState=WindowState.Maximized;Pump();exit.DetachedPreviewWindow.Close();exit.WindowState=WindowState.Maximized;Pump();exit.Close();Pump();check(!exit.IsVisible&&WorkspaceState.Load().Main?.Maximized==true,"editing plus maximized return and application exit completes without numeric serialization errors");
    }
}
