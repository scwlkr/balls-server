[CmdletBinding()]
param(
  [Parameter(Mandatory)]
  [string]$Version
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($Version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') {
  throw 'Version must be a semantic version such as 0.4.0-pilot.1.'
}

$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactsRoot = Join-Path $repository 'artifacts'
$packageName = "BallsServer-$Version-win-x64"
$stage = Join-Path $artifactsRoot $packageName
$zipPath = Join-Path $artifactsRoot ($packageName + '.zip')
$checksumPath = $zipPath + '.sha256'

function Assert-ArtifactPath([string]$Path) {
  $fullPath = [IO.Path]::GetFullPath($Path)
  $fullArtifacts = [IO.Path]::GetFullPath($artifactsRoot).TrimEnd('\')
  if (-not $fullPath.StartsWith($fullArtifacts + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'A packaging path escaped the artifacts directory.'
  }
}

foreach ($path in @($stage, $zipPath, $checksumPath)) { Assert-ArtifactPath $path }
New-Item -ItemType Directory -Path $artifactsRoot -Force | Out-Null
foreach ($path in @($stage, $zipPath, $checksumPath)) {
  if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
}

$appProject = Join-Path (Join-Path $repository 'src') 'BallsServer.App\BallsServer.App.csproj'
$helperProject = Join-Path (Join-Path $repository 'src') 'BallsServer.Helper\BallsServer.Helper.csproj'
& dotnet publish $appProject -c Release -r win-x64 --self-contained true -p:DebugType=None -p:DebugSymbols=false -o $stage --nologo
if ($LASTEXITCODE -ne 0) { throw 'Balls Server application publication failed.' }
& dotnet publish $helperProject -c Release -r win-x64 --self-contained true -p:DebugType=None -p:DebugSymbols=false -o $stage --nologo
if ($LASTEXITCODE -ne 0) { throw 'Balls Server helper publication failed.' }

Get-ChildItem -LiteralPath $stage -Filter '*.pdb' -File -Recurse | Remove-Item -Force
foreach ($requiredFile in @('BallsServer.exe', 'BallsServer.Helper.exe', 'HostSetup.ps1')) {
  if (-not (Test-Path -LiteralPath (Join-Path $stage $requiredFile) -PathType Leaf)) {
    throw "Portable output is missing $requiredFile."
  }
}

Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zipPath -CompressionLevel Optimal
Import-Module Microsoft.PowerShell.Utility -Force
$hash = (Microsoft.PowerShell.Utility\Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToUpperInvariant()
[IO.File]::WriteAllText(
  $checksumPath,
  "$hash  $([IO.Path]::GetFileName($zipPath))`n",
  (New-Object Text.UTF8Encoding($false)))

Write-Output $zipPath
Write-Output $checksumPath
