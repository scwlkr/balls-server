function Find-Task02ProductionReferences {
    param(
        [Parameter(Mandatory)]
        [string]$ProductionRoot
    )

    if (-not (Test-Path -LiteralPath $ProductionRoot -PathType Container)) {
        return @()
    }

    $forbiddenTokens = @('SecurityPrototype', 'PrivilegedHelperBoundary')
    return @(
        Get-ChildItem -LiteralPath $ProductionRoot -Recurse -File |
            Select-String -Pattern $forbiddenTokens -SimpleMatch
    )
}

function Test-Task02ProductionIsolationScanner {
    $fixtureRoot = Join-Path ([System.IO.Path]::GetTempPath()) "BallsServer.Task02Verifier.$([guid]::NewGuid().ToString('N'))"
    $sourceRoot = Join-Path $fixtureRoot 'src'
    $fixturePath = Join-Path $sourceRoot 'VerifierMutation.cs'
    $passed = $true
    $mutationsTested = 0
    $cleanupCompleted = $false

    try {
        [void][System.IO.Directory]::CreateDirectory($sourceRoot)
        foreach ($forbiddenToken in @('SecurityPrototype', 'PrivilegedHelperBoundary')) {
            [System.IO.File]::WriteAllText($fixturePath, "namespace $forbiddenToken;", [System.Text.UTF8Encoding]::new($false))
            $matches = @(Find-Task02ProductionReferences -ProductionRoot $sourceRoot)
            $mutationsTested++
            if ($matches.Count -ne 1 -or -not $matches[0].Line.Contains($forbiddenToken, [System.StringComparison]::Ordinal)) {
                $passed = $false
            }

            [System.IO.File]::Delete($fixturePath)
            if (@(Find-Task02ProductionReferences -ProductionRoot $sourceRoot).Count -ne 0) {
                $passed = $false
            }
        }
    }
    finally {
        if ([System.IO.Directory]::Exists($fixtureRoot)) {
            [System.IO.Directory]::Delete($fixtureRoot, $true)
        }
        $cleanupCompleted = -not [System.IO.Directory]::Exists($fixtureRoot)
    }

    return [pscustomobject]@{
        Passed = $passed
        MutationsTested = $mutationsTested
        CleanupCompleted = $cleanupCompleted
    }
}

Export-ModuleMember -Function Find-Task02ProductionReferences, Test-Task02ProductionIsolationScanner
