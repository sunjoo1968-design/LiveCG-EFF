$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.Drawing
$taskRoot=Split-Path $PSScriptRoot -Parent
$taskVersion=([xml](Get-Content -LiteralPath (Join-Path $taskRoot 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version
$taskParts=$taskVersion.Split('.');$taskTag='v'+$taskParts[0]+$taskParts[1]+$(if([int]$taskParts[2] -gt 0){$taskParts[2]}else{''})
$taskBundle=Join-Path $taskRoot "dist/LiveCG-EFF-$taskVersion"
$taskZip=Join-Path $taskRoot "dist/LiveCG-EFF-$taskVersion-win-x64.zip"
$taskReport=[ordered]@{Version=$taskVersion}
$taskArchive=[IO.Compression.ZipFile]::OpenRead($taskZip)
try {
    $taskNames=@($taskArchive.Entries | ForEach-Object {$_.FullName.Replace('\','/')})
    $taskReport.ZipEntries=$taskNames.Count
    foreach($item in @(@('RootLauncher','LiveCG-EFF.exe'),@('AppExecutable','app/LiveCG-EFF.exe'),@('Runtime','app/coreclr.dll'),@('Skia','app/libSkiaSharp.dll'),@('FFmpeg','app/tools/ffmpeg/ffmpeg.exe'),@('FFprobe','app/tools/ffmpeg/ffprobe.exe'),@('DotnetLicense','licenses/dotnet/LICENSE.txt'),@('Guide','사용안내/README.md'),@('FfmpegLicense','app/tools/ffmpeg/LICENSE'))) {$taskReport[$item[0]]=$taskNames -contains ("LiveCG-EFF-$taskVersion/"+$item[1])}
    $taskReport.NoRuntimeDllsInRoot=@(Get-ChildItem -LiteralPath $taskBundle -Filter *.dll -File).Count -eq 0
    $taskReport.NoOcr=@($taskNames | Where-Object {$_ -match 'tesseract|leptonica|tessdata'}).Count -eq 0
    $taskReport.CurrentPackagePresent=(Test-Path -LiteralPath $taskBundle) -and (Test-Path -LiteralPath $taskZip)
    $taskReport.GuideVersion=(Get-Content -LiteralPath (Join-Path $taskBundle '사용안내/README.md') -Raw) -match ('현재 버전\s*:?\s*'+[regex]::Escape($taskVersion)+'\b')
    foreach($relative in @('LiveCG-EFF.exe','app/LiveCG-EFF.dll','app/LiveCG.Core.dll')) {
        $taskEntry=$taskArchive.Entries | Where-Object {$_.FullName.Replace('\','/') -eq ("LiveCG-EFF-$taskVersion/"+$relative)}
        $taskStream=$taskEntry.Open();$taskHasher=[Security.Cryptography.SHA256]::Create()
        try {$taskHash=[Convert]::ToHexString($taskHasher.ComputeHash($taskStream))} finally {$taskStream.Dispose();$taskHasher.Dispose()}
        $taskReport['ZipMatches-'+$relative]=$taskHash -eq (Get-FileHash -LiteralPath (Join-Path $taskBundle $relative) -Algorithm SHA256).Hash
    }
} finally {$taskArchive.Dispose()}
$taskIconFile=[IO.File]::ReadAllBytes((Join-Path $taskRoot 'src/LiveCG.App/Assets/LiveCG.ico'))
$taskReport.IconResolutionCount=[BitConverter]::ToUInt16($taskIconFile,4)
$taskIcon=[Drawing.Icon]::ExtractAssociatedIcon((Join-Path $taskBundle 'LiveCG-EFF.exe'));$taskAppIcon=[Drawing.Icon]::ExtractAssociatedIcon((Join-Path $taskBundle 'app/LiveCG-EFF.exe'))
$taskBitmap=$taskIcon.ToBitmap();$taskAppBitmap=$taskAppIcon.ToBitmap();$taskMatch=$true
try {for($y=0;$y -lt $taskBitmap.Height;$y++){for($x=0;$x -lt $taskBitmap.Width;$x++){if($taskBitmap.GetPixel($x,$y) -ne $taskAppBitmap.GetPixel($x,$y)){$taskMatch=$false}}}} finally {$taskBitmap.Dispose();$taskAppBitmap.Dispose();$taskIcon.Dispose();$taskAppIcon.Dispose()}
$taskReport.LauncherAppIconsMatch=$taskMatch
$taskReport.LauncherVersion=([Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $taskBundle 'LiveCG-EFF.exe'))).FileVersion
Add-Type @'
using System;using System.Runtime.InteropServices;using System.Text;
public static class LiveCGPackageProbe {
 public delegate bool Visitor(IntPtr window,IntPtr state);
 [DllImport("user32.dll")]static extern bool EnumWindows(Visitor callback,IntPtr state);
 [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetWindowText(IntPtr window,StringBuilder text,int count);
 public static string Title(int pid){string result="";EnumWindows((window,state)=>{uint owner;GetWindowThreadProcessId(window,out owner);if(owner==(uint)pid){var text=new StringBuilder(1024);GetWindowText(window,text,text.Capacity);if(text.ToString().Contains("LiveCG-EFF"))result=text.ToString();}return true;},IntPtr.Zero);return result;}
}
'@
$taskOldData=$env:LIVECG_DATA_DIR;$taskOldRestore=$env:LIVECG_DISABLE_WORKSPACE_RESTORE;$taskChild=$null
try {
    $env:LIVECG_DATA_DIR=Join-Path $taskRoot "artifacts/$taskTag-package-user-data";$env:LIVECG_DISABLE_WORKSPACE_RESTORE='1'
    $taskExisting=@(Get-Process -Name LiveCG-EFF -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id)
    $taskLauncher=Start-Process -FilePath (Join-Path $taskBundle 'LiveCG-EFF.exe') -WorkingDirectory $taskRoot -WindowStyle Hidden -PassThru
    [void]$taskLauncher.WaitForExit(5000)
    $taskClock=[Diagnostics.Stopwatch]::StartNew();$taskTitle=''
    do {$taskChild=Get-Process -Name LiveCG-EFF -ErrorAction SilentlyContinue | Where-Object {$_.Id -notin $taskExisting -and $_.Path -eq (Join-Path $taskBundle 'app/LiveCG-EFF.exe')} | Select-Object -First 1;if($taskChild){$taskTitle=[LiveCGPackageProbe]::Title($taskChild.Id)};if(!$taskTitle){Start-Sleep -Milliseconds 100}} while(!$taskTitle -and $taskClock.ElapsedMilliseconds -lt 15000)
    $taskReport.StartupTitle=$taskTitle;$taskReport.RootLauncherStartsApp=$taskTitle.Contains('SunjooAn') -and $taskTitle.Contains($taskVersion)
} finally {if($taskChild -and !$taskChild.HasExited){Stop-Process -Id $taskChild.Id};$env:LIVECG_DATA_DIR=$taskOldData;$env:LIVECG_DISABLE_WORKSPACE_RESTORE=$taskOldRestore}
$taskReport.ZipMiB=[Math]::Round((Get-Item -LiteralPath $taskZip).Length/1MB,1)
$taskReport.ZipSHA256=(Get-FileHash -LiteralPath $taskZip -Algorithm SHA256).Hash
New-Item -ItemType Directory -Path (Join-Path $taskRoot 'artifacts') -Force | Out-Null
$taskReport | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskRoot "artifacts/$taskTag-package-report.json") -Encoding utf8
$taskReport | ConvertTo-Json
if(@($taskReport.Values | Where-Object {$_ -is [bool] -and !$_}).Count){throw 'Package verification failed'}
