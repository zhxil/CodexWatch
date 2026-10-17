$ErrorActionPreference = 'Stop'

$project = Join-Path $PSScriptRoot 'CodexQuotaTray.csproj'
$output = Join-Path $PSScriptRoot 'dist\win-x64'

dotnet publish $project `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  --output $output `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true

Write-Host "Published to $output"
