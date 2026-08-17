param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
$modulePath = Join-Path $RepositoryRoot 'scripts\V030Task03Verifier.psm1'
Import-Module -Name $modulePath -Force

$result = Test-Task03IsolationGuards
if (-not $result.Passed -or -not $result.CleanupCompleted -or $result.GuardClassesTested -ne 11 -or
    $result.RealAssembliesCompiled -lt 7 -or -not $result.StructuralRejectedBeforeBuild -or
    -not $result.AnalyzerRejectedBeforeBuild -or -not $result.AnalyzerSentinelAbsent -or
    -not $result.NamespacedRejectedBeforeBuild -or -not $result.NamespacedSentinelAbsent -or
    -not $result.RootPropsRejectedBeforeBuild -or -not $result.EditorConfigRejectedBeforeBuild -or
    -not $result.EditorConfigCaseDriftRejectedBeforeBuild -or -not $result.MetadataPInvokeDetected -or
    -not $result.DependencyMetadataDetected -or $result.DependencyMetadataFinding -notlike '*DependencyMutation.dll*pinvoke:Mutate*') {
    throw "Task 04 isolation verifier self-test failed: $($result | ConvertTo-Json -Compress)"
}

Write-Host 'PASS: Task 04 verifier self-test rejected all eleven executable-build, analyzer, input-graph, linked-source, and compiled dependency/PInvoke mutations before unsafe execution and cleaned up deterministically.'
