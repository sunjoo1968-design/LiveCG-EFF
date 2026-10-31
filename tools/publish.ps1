$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'bootstrap.ps1')
$taskVersion = ([xml](Get-Content -LiteralPath (Join-Path $taskRoot 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version
$taskPublish = Join-Path $taskRoot "dist\LiveCG-EFF-$taskVersion"
$taskAppFolder = Join-Path $taskPublish 'app'
& (Join-Path $PSScriptRoot 'build-icon.ps1')
& $taskSdk publish (Join-Path $taskRoot 'src\LiveCG.App\LiveCG.App.csproj') -c Release -r win-x64 --self-contained true -o $taskAppFolder --disable-build-servers -m:1 -v minimal
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
$taskFfmpeg = Get-ChildItem -LiteralPath (Join-Path $taskRoot '.tools\ffmpeg') -Filter ffmpeg.exe -Recurse | Select-Object -First 1
$taskVendor = Split-Path (Split-Path $taskFfmpeg.FullName -Parent) -Parent
$taskDestination = Join-Path $taskAppFolder 'tools\ffmpeg'
New-Item -ItemType Directory -Path $taskDestination -Force | Out-Null
Copy-Item -LiteralPath $taskFfmpeg.FullName -Destination $taskDestination -Force
Copy-Item -LiteralPath (Join-Path $taskFfmpeg.DirectoryName 'ffprobe.exe') -Destination $taskDestination -Force
Copy-Item -LiteralPath (Join-Path $taskVendor 'LICENSE'),(Join-Path $taskVendor 'README.txt') -Destination $taskDestination -Force
$taskGuide = Join-Path $taskPublish '사용안내'
New-Item -ItemType Directory -Path $taskGuide -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $taskRoot 'README.md'),(Join-Path $taskRoot 'THIRD_PARTY_NOTICES.md') -Destination $taskGuide -Force
$taskGuideDocs=Join-Path $taskGuide 'docs'
New-Item -ItemType Directory -Path $taskGuideDocs -Force | Out-Null
foreach($taskDocFolder in @('01-plan','02-design','03-analysis','04-report','images')) {
    Copy-Item -LiteralPath (Join-Path $taskRoot "docs/$taskDocFolder") -Destination $taskGuideDocs -Recurse -Force
}
foreach($taskVerification in Get-ChildItem -LiteralPath (Join-Path $taskRoot 'docs/verification') -Directory){
    $taskEvidence=Join-Path $taskGuideDocs ("verification/"+$taskVerification.Name)
    New-Item -ItemType Directory -Path $taskEvidence -Force | Out-Null
    Get-ChildItem -LiteralPath $taskVerification.FullName -File | Where-Object {$_.Name -in @('summary.json','render-comparison.json')} | Copy-Item -Destination $taskEvidence -Force
}
Copy-Item -LiteralPath (Join-Path $taskRoot 'docs/ROADMAP.md'),(Join-Path $taskRoot 'docs/PROJECT_LAYOUT.md') -Destination $taskGuideDocs -Force
New-Item -ItemType Directory -Path (Join-Path $taskPublish 'licenses') -Force | Out-Null
Get-ChildItem -LiteralPath (Join-Path $taskRoot 'licenses') -File | Where-Object { $_.Name -match '^(SkiaSharp|OpenTK|GLWpfControl)' } | Copy-Item -Destination (Join-Path $taskPublish 'licenses') -Force
$taskDotnetLicense = Join-Path $taskPublish 'licenses\dotnet'
New-Item -ItemType Directory -Path $taskDotnetLicense -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $taskRoot '.tools\dotnet\LICENSE.txt'),(Join-Path $taskRoot '.tools\dotnet\ThirdPartyNotices.txt') -Destination $taskDotnetLicense -Force
$taskLauncherVersion = Join-Path $taskRoot "artifacts\launcher-$taskVersion-version.cs"
New-Item -ItemType Directory -Path (Split-Path $taskLauncherVersion -Parent) -Force | Out-Null
"[assembly:System.Reflection.AssemblyVersion(`"$taskVersion.0`")]`n[assembly:System.Reflection.AssemblyFileVersion(`"$taskVersion.0`")]" | Set-Content -LiteralPath $taskLauncherVersion -Encoding utf8
$taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $taskCompiler /nologo /target:winexe /platform:x64 /utf8output /codepage:65001 "/win32icon:$(Join-Path $taskRoot 'src\LiveCG.App\Assets\LiveCG.ico')" "/out:$(Join-Path $taskPublish 'LiveCG-EFF.exe')" /reference:System.Windows.Forms.dll (Join-Path $PSScriptRoot 'Launcher.cs') $taskLauncherVersion
if ($LASTEXITCODE -ne 0) { throw 'Launcher build failed.' }
@"
LiveCG-EFF $taskVersion · 제작자 SunjooAn

이 폴더의 LiveCG-EFF.exe를 실행하세요.
app 폴더: 실행에 필요한 프로그램과 런타임입니다. 이동하거나 삭제하지 마세요.
사용안내 폴더: README.md와 외부 구성요소 안내입니다.
licenses 폴더: 외부 라이선스 원문입니다.
다른 PC로 옮길 때는 이 폴더 전체를 복사하세요.
"@ | Set-Content -LiteralPath (Join-Path $taskPublish '먼저 읽어주세요.txt') -Encoding utf8
Compress-Archive -LiteralPath $taskPublish -DestinationPath (Join-Path $taskRoot "dist\LiveCG-EFF-$taskVersion-win-x64.zip") -Force
Write-Output "Ready: $(Join-Path $taskPublish 'LiveCG-EFF.exe')"
