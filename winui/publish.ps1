param([string]$Dotnet = 'dotnet', [switch]$Offline)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$env:DOTNET_CLI_HOME=Join-Path $root '.dotnet-cli'
$env:APPDATA=Join-Path $root 'tools/appdata'
$env:LOCALAPPDATA=Join-Path $root 'tools/localappdata'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$argsList=@('publish', (Join-Path $PSScriptRoot 'CodexWatch.csproj'), '-c','Release','--self-contained','true','-p:UseSharedCompilation=false','-p:NuGetAudit=false',('-p:RestorePackagesPath='+ (Join-Path $root 'tools/packages')),'-o',(Join-Path $root 'dist/winui3-win-x64'))
if ($Offline) { $argsList+=@('--configfile',(Join-Path $PSScriptRoot 'NuGet.Config')) }
else { $argsList+=@('--configfile',(Join-Path $PSScriptRoot 'NuGet.Online.Config')) }
& $Dotnet @argsList
if ($LASTEXITCODE -ne 0) { throw "Publish failed: $LASTEXITCODE" }
Write-Output (Join-Path $root 'dist/winui3-win-x64/CodexWatch.exe')
