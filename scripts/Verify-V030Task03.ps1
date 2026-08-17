param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'

$testProject = Join-Path $RepositoryRoot '.scratch\v0.3.0\prototypes\managed-resource-safety.tests\ManagedResourceSafety.Tests.csproj'
$prototypeProject = Join-Path $RepositoryRoot '.scratch\v0.3.0\prototypes\managed-resource-safety\ManagedResourceSafety.csproj'
$designPath = Join-Path $RepositoryRoot 'docs\security\v0.3.0-managed-resource-safety.md'
$solutionPath = Join-Path $RepositoryRoot 'BallsServer.slnx'
$modulePath = Join-Path $RepositoryRoot 'scripts\V030Task03Verifier.psm1'
$errors = [System.Collections.Generic.List[string]]::new()

Import-Module -Name $modulePath -Force

foreach ($artifact in @($testProject, $prototypeProject, $designPath)) {
    if (-not (Test-Path -LiteralPath $artifact -PathType Leaf)) {
        $errors.Add("Missing Ticket 03 artifact: $artifact")
    }
}

$solutionProjects = & dotnet sln $solutionPath list 2>&1 | Out-String
if ($LASTEXITCODE -ne 0) {
    $errors.Add('Could not enumerate production solution composition.')
}
elseif ($solutionProjects -match 'managed-resource-safety') {
    $errors.Add('The isolated managed-resource prototype must not enter production solution composition.')
}

$productionReferences = @(Find-Task03ProductionReferences -ProductionRoot (Join-Path $RepositoryRoot 'src'))
if ($productionReferences.Count -gt 0) {
    $errors.Add('Production source references the isolated managed-resource prototype.')
}

$approvedEditorConfigs = [System.Collections.Generic.List[string]]::new()
$worktreeEditorConfig = Join-Path $RepositoryRoot '.editorconfig'
if (Test-Path -LiteralPath $worktreeEditorConfig -PathType Leaf) {
    $approvedEditorConfigs.Add([System.IO.Path]::GetFullPath($worktreeEditorConfig))
}
else {
    $errors.Add('The worktree .editorconfig required by the exact compiler-input boundary is missing.')
}
$gitCommonDirectory = (& git -C $RepositoryRoot rev-parse --path-format=absolute --git-common-dir 2>&1 | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or $gitCommonDirectory.Length -eq 0) {
    $errors.Add('Could not resolve the Git common directory for the approved analyzer-configuration boundary.')
}
else {
    $commonEditorConfig = Join-Path (Split-Path -Parent $gitCommonDirectory) '.editorconfig'
    if ((Test-Path -LiteralPath $commonEditorConfig -PathType Leaf) -and
        [System.IO.Path]::GetFullPath($commonEditorConfig) -notin $approvedEditorConfigs) {
        $approvedEditorConfigs.Add([System.IO.Path]::GetFullPath($commonEditorConfig))
    }
}

$isolationSelfTest = Test-Task03IsolationGuards
if (-not $isolationSelfTest.Passed -or
    -not $isolationSelfTest.CleanupCompleted -or
    $isolationSelfTest.GuardClassesTested -ne 11 -or
    $isolationSelfTest.RealAssembliesCompiled -lt 7 -or
    -not $isolationSelfTest.MetadataPInvokeDetected -or
    -not $isolationSelfTest.StructuralBuildLogicDetected -or
    -not $isolationSelfTest.StructuralRejectedBeforeBuild -or
    -not $isolationSelfTest.AnalyzerInputDetected -or
    -not $isolationSelfTest.AnalyzerRejectedBeforeBuild -or
    -not $isolationSelfTest.AnalyzerSentinelAbsent -or
    -not $isolationSelfTest.NamespacedBuildLogicDetected -or
    -not $isolationSelfTest.NamespacedRejectedBeforeBuild -or
    -not $isolationSelfTest.NamespacedSentinelAbsent -or
    -not $isolationSelfTest.RootPropsCompletenessDetected -or
    -not $isolationSelfTest.RootPropsRejectedBeforeBuild -or
    -not $isolationSelfTest.EditorConfigInputDetected -or
    -not $isolationSelfTest.EditorConfigRejectedBeforeBuild -or
    -not $isolationSelfTest.EditorConfigCaseDriftDetected -or
    -not $isolationSelfTest.EditorConfigCaseDriftRejectedBeforeBuild -or
    -not $isolationSelfTest.DependencyMetadataDetected -or
    $isolationSelfTest.DependencyMetadataFinding -notlike '*DependencyMutation.dll*pinvoke:Mutate*') {
    $errors.Add('Eleven-class structural/analyzer/configuration/evaluated/dependency-metadata isolation guard self-test failed or left a sentinel or temporary residue.')
}

$projectBoundary = Test-Task03ProjectBoundary `
    -PrototypeProject $prototypeProject `
    -TestProject $testProject `
    -BoundaryRoot $RepositoryRoot `
    -ApprovedBuildFiles @((Join-Path $RepositoryRoot 'Directory.Build.props')) `
    -ApprovedEditorConfigFiles @($approvedEditorConfigs)
if (-not $projectBoundary.Passed) {
    foreach ($boundaryError in $projectBoundary.Errors) {
        $errors.Add("Project boundary: $boundaryError")
    }
}

$prototypeRoot = Split-Path -Parent $prototypeProject
$testRoot = Split-Path -Parent $testProject
$adapterMatches = @(
    @(Find-Task03SourceAdapterEvidence -PrototypeRoot $prototypeRoot)
    @(Find-Task03SourceAdapterEvidence -PrototypeRoot $testRoot)
)
if ($adapterMatches.Count -gt 0) {
    $errors.Add('The isolated prototype or executing test project recursively contains forbidden Windows mutation or shell adapter source evidence.')
}

$testOutput = ''
if ($errors.Count -eq 0) {
    $testOutput = & dotnet test $testProject -c Release --no-restore 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) {
        $errors.Add('Focused managed-resource safety tests failed.')
    }
    elseif (@([regex]::Matches($testOutput, '(?m)^Passed!\s+-\s+Failed:\s+0,\s+Passed:\s+302,\s+Skipped:\s+0,\s+Total:\s+302,')).Count -ne 1) {
        $errors.Add('Focused managed-resource safety result was not exactly 0 failed, 302 passed, 0 skipped, 302 total.')
    }
}

if ($errors.Count -eq 0) {
    $prototypeAssembly = Join-Path $prototypeRoot 'bin\Release\net10.0-windows10.0.26100.0\ManagedResourceSafety.dll'
    $testAssembly = Join-Path $testRoot 'bin\Release\net10.0-windows10.0.26100.0\ManagedResourceSafety.Tests.dll'
    foreach ($assembly in @($prototypeAssembly, $testAssembly)) {
        if (-not (Test-Path -LiteralPath $assembly -PathType Leaf)) {
            $errors.Add("The focused build did not produce a managed assembly for dependency-closure inspection: $assembly")
        }
    }
    if ($errors.Count -eq 0) {
        $approvedTestAssemblies = @(Get-Task03ApprovedPackageAssemblyNames -AssetsPath (Join-Path $testRoot 'obj\project.assets.json'))
        $prototypeEvidence = @(Find-Task03DependencyIlAdapterEvidence -RootAssemblies @($prototypeAssembly) -SearchRoots @((Split-Path -Parent $prototypeAssembly)))
        $testEvidence = @(Find-Task03DependencyIlAdapterEvidence -RootAssemblies @($testAssembly) -SearchRoots @((Split-Path -Parent $testAssembly)) -ApprovedAssemblyNames $approvedTestAssemblies)
        foreach ($finding in @($prototypeEvidence) + @($testEvidence)) {
            $errors.Add("The isolated prototype/test non-framework dependency closure contains forbidden adapter metadata: $finding")
        }
    }
}

$prototypeOutput = ''
if ($errors.Count -eq 0) {
    $prototypeOutput = & dotnet run --project $prototypeProject -c Release --no-restore 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) {
        $errors.Add('Denial-only managed-resource prototype failed.')
    }
    elseif ($prototypeOutput.Trim() -ne 'PASS: isolated managed-resource-safety model loaded; denial guard active; no mutation executed.') {
        $errors.Add('Denial-only managed-resource prototype returned an unexpected result.')
    }

    foreach ($forbiddenOutput in @(
        'not-a-real-credential',
        'hunter2',
        'S-1-5-21',
        '\\',
        $RepositoryRoot
    )) {
        if ($prototypeOutput.Contains($forbiddenOutput, [System.StringComparison]::OrdinalIgnoreCase)) {
            $errors.Add("Prototype output disclosed forbidden secret/private-path material: $forbiddenOutput")
        }
    }
}

if ($errors.Count -gt 0) {
    Write-Host "FAIL: v0.3.0 Task 03 verification found $($errors.Count) problem(s)."
    foreach ($errorMessage in $errors) {
        Write-Host " - $errorMessage"
    }
    if ($testOutput.Length -gt 0) {
        Write-Host $testOutput
    }
    if ($prototypeOutput.Length -gt 0) {
        Write-Host $prototypeOutput
    }
    exit 1
}

Write-Host 'PASS: 302 isolated managed-resource tests; canonical principal and folder/link identities, closed desired/retained/public-result tuples, one-context authorization, semantic private CIDR/interface binding, undefined-enum refusal, namespace-agnostic positive project/root-property XML and exact analyzer-config/compiler-input boundaries, full non-framework dependency-closure metadata checks, eleven-class real compiled no-sentinel cleanup, denial guard, production isolation, and secret/private-path output scans passed.'
