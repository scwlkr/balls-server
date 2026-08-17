param([string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot))

$ErrorActionPreference = 'Stop'
$prototype = Join-Path $RepositoryRoot '.scratch\v0.3.0\prototypes\disposable-vm-topology\DisposableVmTopology.csproj'
$tests = Join-Path $RepositoryRoot '.scratch\v0.3.0\prototypes\disposable-vm-topology.tests\DisposableVmTopology.Tests.csproj'
$contract = Join-Path $RepositoryRoot 'docs\testing\v0.3.0-disposable-vm-topology.md'
$errors = [System.Collections.Generic.List[string]]::new()
foreach ($path in @($prototype, $tests, $contract)) { if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { $errors.Add("Missing Ticket 08 artifact: $path") } }

$solution = & dotnet sln (Join-Path $RepositoryRoot 'BallsServer.slnx') list 2>&1 | Out-String
if ($LASTEXITCODE -ne 0 -or $solution -match 'disposable-vm-topology') { $errors.Add('The isolated topology prototype entered production solution composition.') }
$productionReference = Get-ChildItem -LiteralPath (Join-Path $RepositoryRoot 'src') -Recurse -File | Select-String -Pattern 'BallsServer.DisposableVmTopology', 'disposable-vm-topology' -SimpleMatch
if (@($productionReference).Count -gt 0) { $errors.Add('Production source references the isolated topology prototype.') }
$source = Get-Content -LiteralPath (Join-Path (Split-Path $prototype) 'Program.cs') -Raw
foreach ($forbidden in @('System.Diagnostics.Process', 'System.IO.File', 'System.IO.Directory', 'DllImport', 'WNet', 'CredWrite', 'New-VMSwitch')) { if ($source.Contains($forbidden, [System.StringComparison]::Ordinal)) { $errors.Add("Prototype contains mutation adapter evidence: $forbidden") } }

$testOutput = ''
$runOutput = ''
if ($errors.Count -eq 0) {
    $testOutput = & dotnet test $tests -c Release --no-restore --nologo --verbosity minimal 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0 -or $testOutput -notmatch 'Failed:\s+0, Passed:\s+15, Skipped:\s+0, Total:\s+15') { $errors.Add('Focused disposable-VM topology tests did not pass exactly 15/15.') }
}
if ($errors.Count -eq 0) {
    $runOutput = & dotnet run --project $prototype -c Release --no-restore 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0 -or $runOutput.Trim() -ne 'PASS: isolated disposable-VM topology model loaded; no VM, network, SMB, account, credential, filesystem, or Windows mutation executed.') { $errors.Add('Prototype public output was unexpected.') }
}
foreach ($required in @('v030-clean', 'v030-tailscale-ready', 'v030-configured', '39 operation IDs', '11 required scenario columns', 'LAN', 'Full MagicDNS', 'Host/client cleanup')) { if (-not $source.Contains($required, [System.StringComparison]::Ordinal) -and -not (Get-Content -LiteralPath $contract -Raw).Contains($required, [System.StringComparison]::Ordinal)) { $errors.Add("Topology contract lacks required evidence: $required") } }
foreach ($canary in @('not-a-real-credential', 'hunter2', 'password=')) { if ($testOutput.Contains($canary, [System.StringComparison]::OrdinalIgnoreCase) -or $runOutput.Contains($canary, [System.StringComparison]::OrdinalIgnoreCase)) { $errors.Add("Verifier output disclosed secret material: $canary") } }
if ($errors.Count -gt 0) { Write-Host "FAIL: v0.3.0 Task 08 verification found $($errors.Count) problem(s)."; $errors | ForEach-Object { Write-Host " - $_" }; exit 1 }
Write-Host 'PASS: 15 isolated topology tests; exact default boundary, fixed Balls-share scope guard and denials, 39 by 11 nonblank matrix, private two-VM and optional tailnet legs, snapshots, E2E rows, assertions, production isolation, and clean output passed.'
