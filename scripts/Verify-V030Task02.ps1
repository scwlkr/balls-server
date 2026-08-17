param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'

$testProject = Join-Path $RepositoryRoot '.scratch\v0.3.0\prototypes\privileged-helper-boundary.tests\PrivilegedHelperBoundary.Tests.csproj'
$designPath = Join-Path $RepositoryRoot 'docs\security\v0.3.0-privileged-helper-boundary.md'
$solutionPath = Join-Path $RepositoryRoot 'BallsServer.slnx'
$verifierModulePath = Join-Path $RepositoryRoot 'scripts\V030Task02Verifier.psm1'
$errors = [System.Collections.Generic.List[string]]::new()

Import-Module -Name $verifierModulePath -Force

if (-not (Test-Path -LiteralPath $testProject -PathType Leaf)) {
    $errors.Add("Missing isolated test project: $testProject")
}

if (-not (Test-Path -LiteralPath $designPath -PathType Leaf)) {
    $errors.Add("Missing helper-boundary design: $designPath")
}
else {
    $design = Get-Content -LiteralPath $designPath -Raw
    $requiredDesignContracts = @(
        'FILE_FLAG_FIRST_PIPE_INSTANCE',
        'PIPE_REJECT_REMOTE_CLIENTS',
        'O:<SID>G:SYD:P(A;;GA;;;<SID>)(A;;GA;;;BA)(A;;GA;;;SY)S:(ML;;NW;;;ME)',
        'GetNamedPipeClientProcessId',
        'GetNamedPipeServerProcessId',
        'TokenElevationType',
        'WinVerifyTrust',
        'balls-helper/1',
        '16,384 bytes',
        '120 seconds',
        'UAC answers only whether Windows may start an elevated process',
        'There is no unsigned fallback in production policy',
        'BallsServer.Test.'
    )
    foreach ($contract in $requiredDesignContracts) {
        if (-not $design.Contains($contract, [System.StringComparison]::Ordinal)) {
            $errors.Add("Design is missing required contract: $contract")
        }
    }
}

$solutionProjects = & dotnet sln $solutionPath list 2>&1 | Out-String
if ($LASTEXITCODE -ne 0) {
    $errors.Add('Could not enumerate production solution composition.')
}
elseif ($solutionProjects -match 'privileged-helper-boundary') {
    $errors.Add('The isolated helper prototype must not enter production solution composition.')
}

$productionReferences = @(Find-Task02ProductionReferences -ProductionRoot (Join-Path $RepositoryRoot 'src'))
if ($productionReferences.Count -gt 0) {
    $errors.Add('Production source references the isolated helper prototype.')
}

$isolationSelfTest = Test-Task02ProductionIsolationScanner
if (-not $isolationSelfTest.Passed -or -not $isolationSelfTest.CleanupCompleted -or $isolationSelfTest.MutationsTested -ne 2) {
    $errors.Add('Production-isolation scanner mutation self-test failed or left temporary residue.')
}

$testOutput = ''
if ($errors.Count -eq 0) {
    $testOutput = & dotnet test $testProject -c Release --no-restore 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) {
        $errors.Add('Focused helper-boundary tests failed.')
    }
    elseif (@([regex]::Matches($testOutput, '(?m)^Passed!\s+-\s+Failed:\s+0,\s+Passed:\s+125,\s+Skipped:\s+0,\s+Total:\s+125,')).Count -ne 1) {
        $errors.Add('Focused helper-boundary result was not exactly 0 failed, 125 passed, 0 skipped, 125 total.')
    }

    foreach ($canary in @('not-a-real-credential', 'hunter2')) {
        if ($testOutput.Contains($canary, [System.StringComparison]::Ordinal)) {
            $errors.Add("Focused test output disclosed secret canary: $canary")
        }
    }
}

if ($errors.Count -gt 0) {
    Write-Host "FAIL: v0.3.0 Task 02 verification found $($errors.Count) problem(s)."
    foreach ($errorMessage in $errors) {
        Write-Host " - $errorMessage"
    }
    if ($testOutput.Length -gt 0) {
        Write-Host $testOutput
    }
    exit 1
}

Write-Host 'PASS: 125 isolated helper-boundary tests, including adversarial/concurrency coverage and one ephemeral named-pipe/process-ID feasibility test; scanner self-test and production isolation passed; secret-output scan clean.'
