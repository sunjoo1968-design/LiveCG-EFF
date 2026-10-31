$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskDependencies = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'dependencies.json') -Raw | ConvertFrom-Json
function Get-VerifiedFile($entry, [string]$destination) {
    New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
    if (-not (Test-Path -LiteralPath $destination) -or (Get-FileHash -LiteralPath $destination -Algorithm $entry.algorithm).Hash -ne $entry.hash) {
        Invoke-WebRequest -Uri $entry.url -OutFile $destination
    }
    if ((Get-FileHash -LiteralPath $destination -Algorithm $entry.algorithm).Hash -ne $entry.hash) { throw "Downloaded file checksum mismatch: $destination" }
}
$taskSdk = Join-Path $taskRoot '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $taskSdk)) {
    $taskZip = Join-Path $taskRoot '.tools\downloads\dotnet-sdk.zip'
    Get-VerifiedFile $taskDependencies.sdk $taskZip
    Expand-Archive -LiteralPath $taskZip -DestinationPath (Join-Path $taskRoot '.tools\dotnet') -Force
}
$taskFfmpegRoot = Join-Path $taskRoot '.tools\ffmpeg'
if (-not (Get-ChildItem -LiteralPath $taskFfmpegRoot -Filter ffmpeg.exe -Recurse -ErrorAction SilentlyContinue)) {
    $taskZip = Join-Path $taskRoot '.tools\downloads\ffmpeg.zip'
    Get-VerifiedFile $taskDependencies.ffmpeg $taskZip
    Expand-Archive -LiteralPath $taskZip -DestinationPath $taskFfmpegRoot -Force
}
$env:DOTNET_ROOT = Join-Path $taskRoot '.tools\dotnet'
$env:DOTNET_CLI_HOME = Join-Path $taskRoot '.tools\cli'
$env:NUGET_PACKAGES = Join-Path $taskRoot '.tools\packages'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $taskRoot '.tools\http-cache'
$env:NUGET_SCRATCH = Join-Path $taskRoot '.tools\scratch'
$env:NUGET_PLUGINS_CACHE_PATH = Join-Path $taskRoot '.tools\plugins'
$env:MSBuildEnableWorkloadResolver = 'false'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
Write-Output "Development environment ready: $(& $taskSdk --version)"
