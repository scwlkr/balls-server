param([string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot))

$ErrorActionPreference = 'Stop'
$prototype = Join-Path $RepositoryRoot '.scratch\v0.3.0\prototypes\client-lifecycle\ClientLifecycle.csproj'
$tests = Join-Path $RepositoryRoot '.scratch\v0.3.0\prototypes\client-lifecycle.tests\ClientLifecycle.Tests.csproj'
$contract = Join-Path $RepositoryRoot 'docs\security\v0.3.0-client-credential-mapping-lifecycle.md'
$errors = [System.Collections.Generic.List[string]]::new()
foreach ($path in @($prototype, $tests, $contract)) { if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { $errors.Add("Missing Ticket 07 artifact: $path") } }

$solution = & dotnet sln (Join-Path $RepositoryRoot 'BallsServer.slnx') list 2>&1 | Out-String
if ($LASTEXITCODE -ne 0 -or $solution -match 'client-lifecycle') { $errors.Add('The isolated prototype entered production solution composition.') }
$productionReference = Get-ChildItem -LiteralPath (Join-Path $RepositoryRoot 'src') -Recurse -File | Select-String -Pattern 'BallsServer.ClientLifecycle', 'client-lifecycle' -SimpleMatch
if (@($productionReference).Count -gt 0) { $errors.Add('Production source references the isolated client lifecycle prototype.') }
$source = Get-Content -LiteralPath (Join-Path (Split-Path $prototype) 'Program.cs') -Raw
foreach ($forbidden in @('DllImport', 'System.Diagnostics.Process', 'CredWrite', 'WNet', 'System.IO.File', 'System.IO.Directory')) { if ($source.Contains($forbidden, [System.StringComparison]::Ordinal)) { $errors.Add("Prototype contains forbidden adapter evidence: $forbidden") } }

$testOutput = ''
$runOutput = ''
if ($errors.Count -eq 0) {
    $testOutput = & dotnet test $tests -c Release --no-restore --nologo --verbosity minimal 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0 -or $testOutput -notmatch 'Failed:\s+0, Passed:\s+12, Skipped:\s+0, Total:\s+12') { $errors.Add('Focused client lifecycle tests did not pass exactly 12/12.') }
}
if ($errors.Count -eq 0) {
    $runOutput = & dotnet run --project $prototype -c Release --no-restore 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0 -or $runOutput.Trim() -ne 'PASS: isolated client lifecycle model loaded; no credential, mapping, SMB, filesystem, or Windows mutation executed.') { $errors.Add('Prototype public output was unexpected.') }
}
foreach ($canary in @('not-a-real-credential', 'password=', 'host.lan')) { if ($testOutput.Contains($canary, [System.StringComparison]::OrdinalIgnoreCase) -or $runOutput.Contains($canary, [System.StringComparison]::OrdinalIgnoreCase)) { $errors.Add("Verifier output disclosed private material: $canary") } }
if ($errors.Count -gt 0) { Write-Host "FAIL: v0.3.0 Task 07 verification found $($errors.Count) problem(s)."; $errors | ForEach-Object { Write-Host " - $_" }; exit 1 }
Write-Host 'PASS: 12 isolated client lifecycle tests; fixed Balls share, exact endpoint/update, one-attempt ordering, target separation, Save/reconnect defaults, current-user cleanup/verification, VM guard, production isolation, and clean output passed.'
