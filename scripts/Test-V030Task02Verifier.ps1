param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'

$modulePath = Join-Path $RepositoryRoot 'scripts\V030Task02Verifier.psm1'
Import-Module -Name $modulePath -Force

$result = Test-Task02ProductionIsolationScanner
if (-not $result.Passed -or -not $result.CleanupCompleted -or $result.MutationsTested -ne 2) {
    throw "Task 02 verifier self-test failed: $($result | ConvertTo-Json -Compress)"
}

Write-Host 'PASS: production-isolation scanner rejected both forbidden identifiers and removed its temporary fixture.'
