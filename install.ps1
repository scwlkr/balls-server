[CmdletBinding()]
param(
  [string]$Repository = 'scwlkr/balls-server',
  [string]$InstallRoot = (Join-Path (Join-Path $env:LOCALAPPDATA 'Balls Server') 'App'),
  [switch]$NoLaunch
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

throw 'Balls Server is retired and unsupported. The archived installer will not download or change files. Active development moved to https://github.com/scwlkr/balls.'
