using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
[assembly: AssemblyTitle("LiveCG-EFF")]
[assembly: AssemblyDescription("LiveCG-EFF 실행 · SunjooAn")]
[assembly: AssemblyCompany("SunjooAn")]
[assembly: AssemblyProduct("LiveCG-EFF")]
[assembly: AssemblyCopyright("SunjooAn")]
internal static class Launcher
{
    [STAThread]
    private static void Main()
    {
        try
        {
            string folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"app");
            string program=Path.Combine(folder,"LiveCG-EFF.exe");
            if(!File.Exists(program))throw new FileNotFoundException("app 폴더가 없습니다. 배포 ZIP의 전체 폴더를 압축 해제하세요.",program);
            Process.Start(new ProcessStartInfo(program){WorkingDirectory=folder,UseShellExecute=true});
        }
        catch(Exception ex){MessageBox.Show(ex.Message,"LiveCG-EFF · SunjooAn · "+Assembly.GetExecutingAssembly().GetName().Version,MessageBoxButtons.OK,MessageBoxIcon.Error);}
    }
}
