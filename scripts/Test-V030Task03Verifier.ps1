param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'

$modulePath = Join-Path $RepositoryRoot 'scripts\V030Task03Verifier.psm1'
Import-Module -Name $modulePath -Force

$result = Test-Task03IsolationGuards
if (-not $result.Passed -or
    -not $result.CleanupCompleted -or
    $result.GuardClassesTested -ne 11 -or
    $result.RealAssembliesCompiled -lt 7 -or
    -not $result.MetadataPInvokeDetected -or
    -not $result.StructuralBuildLogicDetected -or
    -not $result.StructuralRejectedBeforeBuild -or
    -not $result.AnalyzerInputDetected -or
    -not $result.AnalyzerRejectedBeforeBuild -or
    -not $result.AnalyzerSentinelAbsent -or
    -not $result.NamespacedBuildLogicDetected -or
    -not $result.NamespacedRejectedBeforeBuild -or
    -not $result.NamespacedSentinelAbsent -or
    -not $result.RootPropsCompletenessDetected -or
    -not $result.RootPropsRejectedBeforeBuild -or
    -not $result.EditorConfigInputDetected -or
    -not $result.EditorConfigRejectedBeforeBuild -or
    -not $result.EditorConfigCaseDriftDetected -or
    -not $result.EditorConfigCaseDriftRejectedBeforeBuild -or
    -not $result.DependencyMetadataDetected -or
    $result.DependencyMetadataFinding -notlike '*DependencyMutation.dll*pinvoke:Mutate*') {
    throw "Task 03 verifier self-test failed: $($result | ConvertTo-Json -Compress)"
}

Write-Host 'PASS: isolation verifier rejected namespaced build logic, compiler analyzers, incomplete root properties, and unapproved analyzer configuration before evaluation; evaluated both exact project graphs, compiled real nested fixtures, and left no sentinel or temporary fixture.'
