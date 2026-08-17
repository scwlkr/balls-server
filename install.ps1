[CmdletBinding()]
param(
  [string]$Repository = 'scwlkr/balls-server',
  [string]$InstallRoot = (Join-Path (Join-Path $env:LOCALAPPDATA 'Balls Server') 'App'),
  [switch]$NoLaunch
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Get-SafeFullPath([string]$Path, [string]$Parent) {
  $fullPath = [IO.Path]::GetFullPath($Path).TrimEnd('\')
  $fullParent = [IO.Path]::GetFullPath($Parent).TrimEnd('\')
  if (-not $fullPath.StartsWith($fullParent + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The requested installation path is outside the Balls Server application directory.'
  }
  return $fullPath
}

function Remove-SafeTree([string]$Path, [string]$Parent) {
  $safePath = Get-SafeFullPath $Path $Parent
  if (Test-Path -LiteralPath $safePath) {
    Remove-Item -LiteralPath $safePath -Recurse -Force
  }
}

$productRoot = Join-Path $env:LOCALAPPDATA 'Balls Server'
$InstallRoot = Get-SafeFullPath $InstallRoot $productRoot
$backupRoot = Get-SafeFullPath ($InstallRoot + '.previous') $productRoot
$temporaryParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
$temporaryRoot = Join-Path $temporaryParent ("BallsServer.Install." + [Guid]::NewGuid().ToString('N'))
$downloadedZip = Join-Path $temporaryRoot 'BallsServer.zip'
$downloadedChecksum = Join-Path $temporaryRoot 'BallsServer.sha256'
$extractedRoot = Join-Path $temporaryRoot 'extracted'
$installed = $false
$movedCurrent = $false

try {
  New-Item -ItemType Directory -Path $temporaryRoot -Force | Out-Null
  $releaseHeaders = @{ 'User-Agent' = 'Balls-Server-Installer' }
  $releases = Invoke-RestMethod -Uri "https://api.github.com/repos/$Repository/releases?per_page=20" -Headers $releaseHeaders
  $release = @($releases | Where-Object { -not $_.draft } | Select-Object -First 1)
  if ($release.Count -ne 1) { throw 'No Balls Server release is available.' }

  $zipAssets = @($release[0].assets | Where-Object { $_.name -match '^BallsServer-.+-win-x64\.zip$' })
  $checksumAssets = @($release[0].assets | Where-Object { $_.name -match '^BallsServer-.+-win-x64\.zip\.sha256$' })
  if ($zipAssets.Count -ne 1 -or $checksumAssets.Count -ne 1) {
    throw 'The Balls Server release assets are incomplete or ambiguous.'
  }
  if ($checksumAssets[0].name -ne ($zipAssets[0].name + '.sha256')) {
    throw 'The Balls Server checksum does not match the selected package.'
  }

  Invoke-WebRequest -Uri $zipAssets[0].browser_download_url -OutFile $downloadedZip -UseBasicParsing
  Invoke-WebRequest -Uri $checksumAssets[0].browser_download_url -OutFile $downloadedChecksum -UseBasicParsing
  Import-Module Microsoft.PowerShell.Utility -Force
  $checksumText = [IO.File]::ReadAllText($downloadedChecksum).Trim()
  $expectedHash = ($checksumText -split '\s+')[0].ToUpperInvariant()
  if ($expectedHash -notmatch '^[0-9A-F]{64}$') { throw 'The published checksum is malformed.' }
  $actualHash = (Microsoft.PowerShell.Utility\Get-FileHash -LiteralPath $downloadedZip -Algorithm SHA256).Hash.ToUpperInvariant()
  if ($actualHash -ne $expectedHash) { throw 'The downloaded Balls Server package failed SHA-256 verification.' }

  Expand-Archive -LiteralPath $downloadedZip -DestinationPath $extractedRoot
  foreach ($requiredFile in @('BallsServer.exe', 'BallsServer.Helper.exe', 'HostSetup.ps1')) {
    if (-not (Test-Path -LiteralPath (Join-Path $extractedRoot $requiredFile) -PathType Leaf)) {
      throw "The verified package is missing $requiredFile."
    }
  }

  $runningApps = @(Get-Process -Name 'BallsServer' -ErrorAction SilentlyContinue | Where-Object {
    try { $_.Path -and ([IO.Path]::GetFullPath($_.Path)).StartsWith($InstallRoot + '\', [StringComparison]::OrdinalIgnoreCase) }
    catch { $false }
  })
  foreach ($runningApp in $runningApps) {
    [void]$runningApp.CloseMainWindow()
    if (-not $runningApp.WaitForExit(5000)) { Stop-Process -Id $runningApp.Id -Force }
  }

  New-Item -ItemType Directory -Path $productRoot -Force | Out-Null
  Remove-SafeTree $backupRoot $productRoot
  if (Test-Path -LiteralPath $InstallRoot) {
    Move-Item -LiteralPath $InstallRoot -Destination $backupRoot
    $movedCurrent = $true
  }
  Move-Item -LiteralPath $extractedRoot -Destination $InstallRoot
  $installed = $true

  $programs = [Environment]::GetFolderPath('Programs')
  $shortcutPath = Join-Path $programs 'Balls Server.lnk'
  $shell = New-Object -ComObject WScript.Shell
  $shortcut = $shell.CreateShortcut($shortcutPath)
  $shortcut.TargetPath = Join-Path $InstallRoot 'BallsServer.exe'
  $shortcut.WorkingDirectory = $InstallRoot
  $shortcut.IconLocation = (Join-Path $InstallRoot 'BallsServer.exe') + ',0'
  $shortcut.Description = 'Balls Server private Windows file sharing'
  $shortcut.Save()

  Remove-SafeTree $backupRoot $productRoot
  if (-not $NoLaunch) { Start-Process -FilePath (Join-Path $InstallRoot 'BallsServer.exe') }
  Write-Host "Balls Server is installed and up to date. Open it from the Start Menu."
} catch {
  if (-not $installed -and $movedCurrent -and -not (Test-Path -LiteralPath $InstallRoot) -and (Test-Path -LiteralPath $backupRoot)) {
    Move-Item -LiteralPath $backupRoot -Destination $InstallRoot
  }
  throw
} finally {
  Remove-SafeTree $temporaryRoot $temporaryParent
}
