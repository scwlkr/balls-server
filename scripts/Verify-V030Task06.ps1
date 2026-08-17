param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
$prototypeProject = Join-Path $RepositoryRoot '.scratch\v0.3.0\prototypes\access-grant-lifecycle\AccessGrantLifecycle.csproj'
$testProject = Join-Path $RepositoryRoot '.scratch\v0.3.0\prototypes\access-grant-lifecycle.tests\AccessGrantLifecycle.Tests.csproj'
$contract = Join-Path $RepositoryRoot 'docs\security\v0.3.0-access-grant-secret-lifecycle.md'
$errors = [System.Collections.Generic.List[string]]::new()

foreach ($path in @($prototypeProject, $testProject, $contract)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { $errors.Add("Missing Ticket 06 artifact: $path") }
}

$solution = & dotnet sln (Join-Path $RepositoryRoot 'BallsServer.slnx') list 2>&1 | Out-String
if ($LASTEXITCODE -ne 0 -or $solution -match 'access-grant-lifecycle') { $errors.Add('The isolated prototype entered production solution composition.') }

$productionReference = Get-ChildItem -LiteralPath (Join-Path $RepositoryRoot 'src') -Recurse -File |
    Select-String -Pattern 'BallsServer.AccessGrantLifecycle', 'access-grant-lifecycle' -SimpleMatch
if (@($productionReference).Count -gt 0) { $errors.Add('Production source references the isolated lifecycle prototype.') }

$prototypeSource = Get-Content -LiteralPath (Join-Path (Split-Path $prototypeProject) 'Contracts.cs') -Raw
foreach ($forbidden in @('System.Diagnostics.Process', 'System.IO.File', 'DllImport', 'NamedPipeServerStream', 'WNet', 'CredWrite')) {
    if ($prototypeSource.Contains($forbidden, [System.StringComparison]::Ordinal)) { $errors.Add("Prototype contains forbidden adapter evidence: $forbidden") }
}

$testOutput = ''
$prototypeOutput = ''
if ($errors.Count -eq 0) {
    $testOutput = & dotnet test $testProject -c Release --no-restore --nologo --verbosity minimal 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0 -or $testOutput -notmatch 'Failed:\s+0, Passed:\s+24, Skipped:\s+0, Total:\s+24') { $errors.Add('Focused access-grant lifecycle tests did not pass exactly 24/24.') }
}
if ($errors.Count -eq 0) {
    $prototypeOutput = & dotnet run --project $prototypeProject -c Release --no-restore 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0 -or $prototypeOutput.Trim() -ne 'PASS: isolated access-grant lifecycle model loaded; no system, network, account, credential, or filesystem mutation executed.') { $errors.Add('Prototype public output was unexpected.') }
}
foreach ($canary in @('not-a-real-credential', 'hunter2', 'password=')) {
    if ($testOutput.Contains($canary, [System.StringComparison]::OrdinalIgnoreCase) -or $prototypeOutput.Contains($canary, [System.StringComparison]::OrdinalIgnoreCase)) { $errors.Add("Verifier output disclosed secret material: $canary") }
}

if ($errors.Count -gt 0) {
    Write-Host "FAIL: v0.3.0 Task 06 verification found $($errors.Count) problem(s)."
    foreach ($errorMessage in $errors) { Write-Host " - $errorMessage" }
    exit 1
}

Write-Host 'PASS: 24 isolated access-grant lifecycle tests; opaque authorization/revision/atomic-one-read/disclosure/revocation/session/redaction boundaries, production isolation, and clean secret output passed.'
