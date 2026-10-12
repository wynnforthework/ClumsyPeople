param(
    [string]$DalamudPath = "$env:APPDATA\XIVLauncherCN\addon\Hooks\26-10-08-02",
    [string]$Dotnet = "dotnet",
    [string]$Source = "",
    [string]$Models = ""
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    if (-not (Test-Path (Join-Path $DalamudPath 'Dalamud.dll'))) { throw 'Specify -DalamudPath pointing to Dalamud API 15.' }
    $version = (Get-Item (Join-Path $DalamudPath 'Dalamud.dll')).VersionInfo.FileVersion
    if (-not $version.StartsWith('15.')) { throw "API 15 required, found $version" }
    $env:DALAMUD_HOME = $DalamudPath
    $restoreArgs = @('restore','ClumsyPeople.sln','--locked-mode')
    if ($Source) { $restoreArgs += @('--source',$Source,'-p:NuGetAudit=false') }
    & $Dotnet @restoreArgs
    if ($LASTEXITCODE) { throw 'Restore failed' }
    & $Dotnet build ClumsyPeople.sln -c Release --no-restore -warnaserror
    if ($LASTEXITCODE) { throw 'Build failed' }
    $testArgs = @('run','--project','ClumsyPeople.Tests','-c','Release','--no-build')
    if ($Models) { $testArgs += @('--',$Models) }
    & $Dotnet @testArgs
    if ($LASTEXITCODE) { throw 'Tests failed' }
    & "$PSScriptRoot/package.ps1"
} finally { Pop-Location }
