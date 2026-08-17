function Find-Task03ProductionReferences {
    param(
        [Parameter(Mandatory)]
        [string]$ProductionRoot
    )

    if (-not (Test-Path -LiteralPath $ProductionRoot -PathType Container)) {
        return @()
    }

    return @(
        Get-ChildItem -LiteralPath $ProductionRoot -Recurse -File |
            Where-Object { $_.Extension -in @('.cs', '.csproj', '.props', '.targets', '.slnx') } |
            Select-String -Pattern 'ManagedResourceSafety' -SimpleMatch
    )
}

function Get-Task03AdapterTokens {
    return @(
        'DllImport',
        'LibraryImport',
        'System.Runtime.InteropServices',
        'System.Management.Automation',
        'System.Diagnostics.Process',
        'Process.Start',
        'Microsoft.Win32.Registry',
        'System.ServiceProcess',
        'System.DirectoryServices.AccountManagement',
        'System.Security.AccessControl',
        'WindowsIdentity',
        'New-SmbShare',
        'Set-Smb',
        'New-NetFirewallRule',
        'Set-NetFirewall',
        'Set-Acl',
        'SetAccessControl'
    )
}

function Find-Task03SourceAdapterEvidence {
    param(
        [Parameter(Mandatory)]
        [string]$PrototypeRoot
    )

    if (-not (Test-Path -LiteralPath $PrototypeRoot -PathType Container)) {
        return @()
    }

    $tokens = @(Get-Task03AdapterTokens)
    return @(
        Get-ChildItem -LiteralPath $PrototypeRoot -Recurse -Filter '*.cs' -File |
            Select-String -Pattern $tokens -SimpleMatch
    )
}

function Test-Task03PathWithin {
    param([string]$Path, [string]$Root)

    $resolvedPath = [System.IO.Path]::GetFullPath($Path)
    $resolvedRoot = [System.IO.Path]::GetFullPath($Root).TrimEnd('\') + '\'
    return $resolvedPath.StartsWith($resolvedRoot, [System.StringComparison]::OrdinalIgnoreCase)
}

function Test-Task03ExactProjectXml {
    param(
        [Parameter(Mandatory)] [xml]$Xml,
        [Parameter(Mandatory)] [string]$ProjectPath,
        [Parameter(Mandatory)] [bool]$IsTestProject,
        [string]$ExpectedPrototypeProject
    )

    $errors = [System.Collections.Generic.List[string]]::new()
    $root = $Xml.DocumentElement
    if ($null -eq $root -or $root.LocalName -cne 'Project' -or $root.NamespaceURI.Length -ne 0 -or
        $root.Attributes.Count -ne 1 -or $root.GetAttribute('Sdk') -cne 'Microsoft.NET.Sdk') {
        $errors.Add('Project root, namespace, attributes, or SDK is outside the exact allowlist.')
        return @($errors)
    }

    $expectedProperties = if ($IsTestProject) {
        @{
            TargetFramework = 'net10.0-windows10.0.26100.0'
            IsPackable = 'false'
            IsTestProject = 'true'
            NoWarn = '$(NoWarn);CA1707'
        }
    }
    else {
        @{
            OutputType = 'Exe'
            TargetFramework = 'net10.0-windows10.0.26100.0'
        }
    }
    $actualProperties = @{}
    $packageItems = [System.Collections.Generic.List[System.Xml.XmlElement]]::new()
    $projectItems = [System.Collections.Generic.List[System.Xml.XmlElement]]::new()
    $usingItems = [System.Collections.Generic.List[System.Xml.XmlElement]]::new()

    foreach ($group in @($root.ChildNodes | Where-Object { $_.NodeType -eq 'Element' })) {
        if ($group.NamespaceURI.Length -ne 0 -or $group.LocalName -cnotin @('PropertyGroup', 'ItemGroup') -or
            $group.Attributes.Count -ne 0) {
            $errors.Add("Project contains an unapproved top-level element or attribute: $($group.LocalName).")
            continue
        }

        foreach ($entry in @($group.ChildNodes | Where-Object { $_.NodeType -eq 'Element' })) {
            if ($entry.NamespaceURI.Length -ne 0 -or @($entry.ChildNodes | Where-Object { $_.NodeType -eq 'Element' }).Count -ne 0) {
                $errors.Add("Project contains a namespaced or nested element: $($entry.LocalName).")
                continue
            }

            if ($group.LocalName -eq 'PropertyGroup') {
                if ($entry.Attributes.Count -ne 0 -or
                    -not $expectedProperties.ContainsKey($entry.LocalName) -or
                    $actualProperties.ContainsKey($entry.LocalName) -or
                    $entry.InnerText -cne $expectedProperties[$entry.LocalName]) {
                    $errors.Add("Project property is missing, duplicated, conditional, or outside the exact allowlist: $($entry.LocalName).")
                    continue
                }
                $actualProperties[$entry.LocalName] = $entry.InnerText
                continue
            }

            switch ($entry.LocalName) {
                'PackageReference' { $packageItems.Add($entry) }
                'ProjectReference' { $projectItems.Add($entry) }
                'Using' { $usingItems.Add($entry) }
                default { $errors.Add("Project item is outside the exact allowlist: $($entry.LocalName).") }
            }
        }
    }

    if ($actualProperties.Count -ne $expectedProperties.Count) {
        $errors.Add('Project does not contain the exact required property set.')
    }

    if (-not $IsTestProject) {
        if ($packageItems.Count + $projectItems.Count + $usingItems.Count -ne 0) {
            $errors.Add('Prototype project contains item declarations.')
        }
        return @($errors)
    }

    $expectedPackages = @{
        'Microsoft.NET.Test.Sdk' = @('17.14.1', '')
        'xunit' = @('2.9.3', '')
        'xunit.runner.visualstudio' = @('3.1.4', 'all')
    }
    if ($packageItems.Count -ne $expectedPackages.Count) {
        $errors.Add('Test project does not contain the exact package item count.')
    }
    foreach ($item in $packageItems) {
        $include = $item.GetAttribute('Include')
        $version = $item.GetAttribute('Version')
        $privateAssets = $item.GetAttribute('PrivateAssets')
        $expectedAttributeCount = if ($privateAssets.Length -eq 0) { 2 } else { 3 }
        if (-not $expectedPackages.ContainsKey($include) -or
            $item.Attributes.Count -ne $expectedAttributeCount -or
            $version -cne $expectedPackages[$include][0] -or
            $privateAssets -cne $expectedPackages[$include][1]) {
            $errors.Add("Test package item is outside the exact allowlist: $include.")
        }
    }

    if ($projectItems.Count -ne 1 -or $projectItems[0].Attributes.Count -ne 1 -or
        -not $projectItems[0].HasAttribute('Include') -or
        [System.IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $ProjectPath) $projectItems[0].GetAttribute('Include'))) -ne
        [System.IO.Path]::GetFullPath($ExpectedPrototypeProject)) {
        $errors.Add('Test project reference is not the one exact isolated prototype item.')
    }
    if ($usingItems.Count -ne 1 -or $usingItems[0].Attributes.Count -ne 1 -or
        $usingItems[0].GetAttribute('Include') -cne 'Xunit') {
        $errors.Add('Test using item is not exactly Xunit.')
    }

    return @($errors)
}

function Test-Task03StaticProjectSafety {
    param(
        [Parameter(Mandatory)] [string]$ProjectPath,
        [Parameter(Mandatory)] [string]$BoundaryRoot,
        [Parameter(Mandatory)] [bool]$IsTestProject,
        [string]$ExpectedPrototypeProject,
        [string[]]$ApprovedBuildFiles = @(),
        [string[]]$ApprovedEditorConfigFiles = @()
    )

    $errors = [System.Collections.Generic.List[string]]::new()
    try {
        [xml]$xml = Get-Content -LiteralPath $ProjectPath -Raw
    }
    catch {
        return [pscustomobject]@{ Passed = $false; Errors = @('Project file is not valid XML.') }
    }

    foreach ($shapeError in @(Test-Task03ExactProjectXml `
        -Xml $xml `
        -ProjectPath $ProjectPath `
        -IsTestProject $IsTestProject `
        -ExpectedPrototypeProject $ExpectedPrototypeProject)) {
        $errors.Add($shapeError)
    }

    foreach ($nodeName in @('Import', 'Target', 'UsingTask', 'Exec')) {
        if (@($xml.SelectNodes("//*[local-name()='$nodeName']")).Count -ne 0) {
            $errors.Add("Project contains forbidden executable/import build node $nodeName.")
        }
    }

    foreach ($propertyName in @(
        'PreBuildEvent',
        'PostBuildEvent',
        'RunPostBuildEvent',
        'CustomBeforeMicrosoftCommonTargets',
        'CustomAfterMicrosoftCommonTargets'
    )) {
        if (@($xml.SelectNodes("//*[local-name()='$propertyName']")).Count -ne 0) {
            $errors.Add("Project contains forbidden build-event property $propertyName.")
        }
    }

    $approved = @($ApprovedBuildFiles | ForEach-Object { [System.IO.Path]::GetFullPath($_) })
    $approvedEditorConfigs = @(@(
        foreach ($configPath in @($ApprovedEditorConfigFiles)) {
            if (-not [string]::IsNullOrWhiteSpace($configPath)) {
                [System.IO.Path]::GetFullPath($configPath)
            }
        }
    ) | Sort-Object -Unique)
    $approvedEditorContents = [System.Collections.Generic.List[string]]::new()
    foreach ($approvedEditorConfig in $approvedEditorConfigs) {
        if (-not (Test-Path -LiteralPath $approvedEditorConfig -PathType Leaf) -or
            [System.IO.Path]::GetFileName($approvedEditorConfig) -cne '.editorconfig') {
            $errors.Add("Approved analyzer configuration is missing or not an exact .editorconfig file: $approvedEditorConfig")
            continue
        }
        $normalizedEditorConfig = (Get-Content -LiteralPath $approvedEditorConfig -Raw).Replace("`r`n", "`n")
        $approvedEditorContents.Add($normalizedEditorConfig)
    }
    if ($approvedEditorContents.Count -gt 1) {
        $baselineEditorContent = $approvedEditorContents[0]
        foreach ($approvedEditorContent in $approvedEditorContents) {
            if (-not [string]::Equals($approvedEditorContent, $baselineEditorContent, [System.StringComparison]::Ordinal)) {
                $errors.Add('Approved analyzer configuration files do not have one exact normalized content.')
                break
            }
        }
    }
    $approvedProperties = @{
        AnalysisLevel = 'latest-recommended'
        Deterministic = 'true'
        EnforceCodeStyleInBuild = 'true'
        ImplicitUsings = 'enable'
        LangVersion = 'latest'
        Nullable = 'enable'
        TreatWarningsAsErrors = 'true'
    }
    foreach ($approvedFile in $approved) {
        if (-not (Test-Path -LiteralPath $approvedFile -PathType Leaf) -or
            [System.IO.Path]::GetFileName($approvedFile) -ne 'Directory.Build.props') {
            $errors.Add("Approved build file is missing or not the one supported root props contract: $approvedFile")
            continue
        }
        try { [xml]$approvedXml = Get-Content -LiteralPath $approvedFile -Raw }
        catch { $errors.Add("Approved build file is invalid XML: $approvedFile"); continue }
        $approvedRoot = $approvedXml.DocumentElement
        $propertyGroups = @($approvedRoot.ChildNodes | Where-Object { $_.NodeType -eq 'Element' })
        $properties = @($propertyGroups.ChildNodes | Where-Object { $_.NodeType -eq 'Element' })
        $actualApprovedProperties = @{}
        if ($null -eq $approvedRoot -or $approvedRoot.LocalName -cne 'Project' -or
            $approvedRoot.NamespaceURI.Length -ne 0 -or $approvedRoot.Attributes.Count -ne 0 -or
            $propertyGroups.Count -ne 1 -or $propertyGroups[0].LocalName -cne 'PropertyGroup' -or
            $propertyGroups[0].NamespaceURI.Length -ne 0 -or $propertyGroups[0].Attributes.Count -ne 0 -or
            @($approvedXml.SelectNodes("//*[local-name()='Import' or local-name()='Target' or local-name()='UsingTask' or local-name()='Exec']")).Count -ne 0 -or
            $properties.Count -ne $approvedProperties.Count) {
            $errors.Add("Approved root props contains non-property or executable/import build logic: $approvedFile")
            continue
        }
        foreach ($property in $properties) {
            if ($property.NamespaceURI.Length -ne 0 -or $property.Attributes.Count -ne 0 -or
                @($property.ChildNodes | Where-Object { $_.NodeType -eq 'Element' }).Count -ne 0 -or
                -not $approvedProperties.ContainsKey($property.LocalName) -or
                $actualApprovedProperties.ContainsKey($property.LocalName) -or
                $property.InnerText -cne $approvedProperties[$property.LocalName]) {
                $errors.Add("Approved root props is outside the exact property allowlist: $($property.LocalName)")
            }
            else {
                $actualApprovedProperties[$property.LocalName] = $property.InnerText
            }
        }
        if ($actualApprovedProperties.Count -ne $approvedProperties.Count -or
            @($approvedProperties.Keys | Where-Object { -not $actualApprovedProperties.ContainsKey($_) }).Count -ne 0) {
            $errors.Add("Approved root props does not contain every required property exactly once: $approvedFile")
        }
    }
    $projectDirectory = Split-Path -Parent ([System.IO.Path]::GetFullPath($ProjectPath))
    $nearestBuildFiles = @{
        'Directory.Build.props' = $false
        'Directory.Build.targets' = $false
        'Directory.Packages.props' = $false
    }
    $seenEditorConfigs = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $cursor = $projectDirectory
    while ($cursor.Length -gt 0) {
        foreach ($name in @($nearestBuildFiles.Keys)) {
            if ($nearestBuildFiles[$name]) { continue }
            $candidate = Join-Path $cursor $name
            if (Test-Path -LiteralPath $candidate -PathType Leaf) {
                $nearestBuildFiles[$name] = $true
                if ([System.IO.Path]::GetFullPath($candidate) -notin $approved) {
                    $errors.Add("Project inherits unapproved build file $candidate.")
                }
            }
        }
        foreach ($responseName in @('Directory.Build.rsp', 'MSBuild.rsp')) {
            $responsePath = Join-Path $cursor $responseName
            if (Test-Path -LiteralPath $responsePath -PathType Leaf) {
                $errors.Add("Project inherits unapproved response file $responsePath.")
            }
        }
        foreach ($configName in @('.editorconfig', '.globalconfig')) {
            $configPath = Join-Path $cursor $configName
            if (Test-Path -LiteralPath $configPath -PathType Leaf) {
                $fullConfigPath = [System.IO.Path]::GetFullPath($configPath)
                [void]$seenEditorConfigs.Add($fullConfigPath)
                if ($fullConfigPath -notin $approvedEditorConfigs) {
                    $errors.Add("Project inherits unapproved analyzer configuration $configPath.")
                }
            }
        }
        $parent = Split-Path -Parent $cursor
        if ($parent -eq $cursor -or $parent.Length -eq 0) { break }
        $cursor = $parent
    }

    foreach ($file in Get-ChildItem -LiteralPath $projectDirectory -Recurse -File -Include '*.props', '*.targets' |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }) {
        if ([System.IO.Path]::GetFullPath($file.FullName) -notin $approved) {
            $errors.Add("Project tree contains unapproved build import file $($file.FullName).")
        }
    }

    foreach ($file in Get-ChildItem -LiteralPath $projectDirectory -Recurse -File |
        Where-Object {
            $_.FullName -notmatch '[\\/](bin|obj)[\\/]' -and
            ($_.Name.EndsWith('.csproj.user', [System.StringComparison]::OrdinalIgnoreCase) -or
             $_.Extension -eq '.rsp' -or
             $_.Name -in @('.editorconfig', '.globalconfig'))
        }) {
        $fullInputPath = [System.IO.Path]::GetFullPath($file.FullName)
        if ($file.Name -notin @('.editorconfig', '.globalconfig') -or $fullInputPath -notin $approvedEditorConfigs) {
            $errors.Add("Project tree contains an unapproved user, response, or analyzer-config file $($file.FullName).")
        }
    }
    $seenEditorConfigSet = [string]::Join('|', @($seenEditorConfigs | Sort-Object))
    $approvedEditorConfigSet = [string]::Join('|', @($approvedEditorConfigs | Sort-Object))
    if ($seenEditorConfigSet -ne $approvedEditorConfigSet) {
        $errors.Add('Project ancestry does not contain the exact approved analyzer-configuration path set.')
    }

    return [pscustomobject]@{ Passed = $errors.Count -eq 0; Errors = @($errors) }
}

function Get-Task03EvaluatedProject {
    param([Parameter(Mandatory)] [string]$ProjectPath)

    $output = & dotnet msbuild $ProjectPath -nologo '-getItem:Compile,ProjectReference,PackageReference,Reference,FrameworkReference,Using,Analyzer,AdditionalFiles,EditorConfigFiles,GlobalAnalyzerConfigFiles,Content,EmbeddedResource' '-getProperty:TargetFramework,EnableDefaultCompileItems' 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) {
        return [pscustomobject]@{ Passed = $false; Error = 'MSBuild project evaluation failed.'; Data = $null }
    }
    try {
        $data = $output | ConvertFrom-Json -Depth 20
        return [pscustomobject]@{ Passed = $true; Error = $null; Data = $data }
    }
    catch {
        return [pscustomobject]@{ Passed = $false; Error = 'MSBuild project evaluation did not return valid JSON.'; Data = $null }
    }
}

function Test-Task03EvaluatedItems {
    param(
        [Parameter(Mandatory)] $Evaluation,
        [Parameter(Mandatory)] [string]$ProjectPath,
        [Parameter(Mandatory)] [bool]$IsTestProject,
        [string]$ExpectedPrototypeProject,
        [string[]]$ApprovedEditorConfigFiles = @()
    )

    $errors = [System.Collections.Generic.List[string]]::new()
    $data = $Evaluation.Data
    $projectRoot = Split-Path -Parent ([System.IO.Path]::GetFullPath($ProjectPath))
    if ($data.Properties.TargetFramework -ne 'net10.0-windows10.0.26100.0' -or
        $data.Properties.EnableDefaultCompileItems -ne 'true') {
        $errors.Add('Evaluated target framework or default compile policy is not exact.')
    }

    $evaluatedCompile = @($data.Items.Compile | ForEach-Object { [System.IO.Path]::GetFullPath($_.FullPath) } | Sort-Object -Unique)
    $sourceCompile = @(Get-ChildItem -LiteralPath $projectRoot -File -Filter '*.cs' |
        ForEach-Object { [System.IO.Path]::GetFullPath($_.FullName) } | Sort-Object -Unique)
    if (@($evaluatedCompile | Where-Object { -not (Test-Task03PathWithin -Path $_ -Root $projectRoot) }).Count -gt 0 -or
        [string]::Join('|', $evaluatedCompile) -ne [string]::Join('|', $sourceCompile)) {
        $errors.Add('Evaluated compile items are external, linked, missing, or not the exact project source set.')
    }

    if (@($data.Items.Reference).Count -ne 0) {
        $errors.Add('Evaluated project contains explicit assembly references.')
    }

    $expectedAnalyzerNames = @(
        'Microsoft.CodeAnalysis.CodeStyle',
        'Microsoft.CodeAnalysis.CodeStyle.Fixes',
        'Microsoft.CodeAnalysis.CSharp.CodeStyle',
        'Microsoft.CodeAnalysis.CSharp.CodeStyle.Fixes',
        'Microsoft.CodeAnalysis.CSharp.NetAnalyzers',
        'Microsoft.CodeAnalysis.NetAnalyzers'
    )
    $analyzers = @($data.Items.Analyzer)
    $analyzerNames = @($analyzers | ForEach-Object { $_.Filename } | Sort-Object)
    if ([string]::Join('|', $analyzerNames) -ne [string]::Join('|', $expectedAnalyzerNames) -or
        @($analyzers | Where-Object {
            $_.IsImplicitlyDefined -ne 'true' -or
            $_.DefiningProjectName -ne 'Microsoft.NET.Sdk.Analyzers' -or
            [System.IO.Path]::GetFileName($_.DefiningProjectFullPath) -ne 'Microsoft.NET.Sdk.Analyzers.targets' -or
            -not (Test-Task03PathWithin -Path $_.FullPath -Root (Split-Path -Parent (Split-Path -Parent $_.DefiningProjectFullPath)))
        }).Count -ne 0) {
        $errors.Add('Evaluated analyzers are not the exact implicit SDK analyzer set.')
    }
    if (@($data.Items.AdditionalFiles).Count -ne 0 -or @($data.Items.EmbeddedResource).Count -ne 0) {
        $errors.Add('Evaluated project contains unapproved compiler or embedded-resource inputs.')
    }
    $evaluatedEditorConfigs = @($data.Items.EditorConfigFiles |
        ForEach-Object { [System.IO.Path]::GetFullPath($_.FullPath) } |
        Sort-Object -Unique)
    $expectedEditorConfigs = @(@(
        foreach ($configPath in @($ApprovedEditorConfigFiles)) {
            if (-not [string]::IsNullOrWhiteSpace($configPath)) {
                [System.IO.Path]::GetFullPath($configPath)
            }
        }
    ) | Sort-Object -Unique)
    $globalConfigItems = @($data.Items.GlobalAnalyzerConfigFiles)
    if ([string]::Join('|', $evaluatedEditorConfigs) -ne [string]::Join('|', $expectedEditorConfigs) -or
        @($globalConfigItems | Where-Object {
            $_.DefiningProjectName -ne 'Microsoft.Managed.Core' -or
            [System.IO.Path]::GetFileName($_.FullPath) -cne '.globalconfig' -or
            (Test-Path -LiteralPath $_.FullPath -PathType Leaf)
        }).Count -ne 0) {
        $errors.Add("Evaluated analyzer configuration inputs are not the exact approved path set: actual=$([string]::Join('|', $evaluatedEditorConfigs)); expected=$([string]::Join('|', $expectedEditorConfigs)).")
    }
    $frameworks = @($data.Items.FrameworkReference)
    $frameworkNames = @($frameworks | ForEach-Object { $_.Identity } | Sort-Object)
    if (@($frameworks | Where-Object { $_.IsImplicitlyDefined -ne 'true' }).Count -gt 0 -or
        [string]::Join('|', $frameworkNames) -ne 'Microsoft.NETCore.App|Microsoft.Windows.SDK.NET.Ref.Windows') {
        $errors.Add('Evaluated framework references are not the exact implicit framework set.')
    }

    $packages = @($data.Items.PackageReference)
    $projectReferences = @($data.Items.ProjectReference)
    if ($IsTestProject) {
        $expectedPackages = @(
            'Microsoft.NET.Test.Sdk|17.14.1',
            'xunit.runner.visualstudio|3.1.4',
            'xunit|2.9.3'
        )
        $actualPackages = @($packages | ForEach-Object { "$($_.Identity)|$($_.Version)" } | Sort-Object)
        if ([string]::Join('|', $actualPackages) -ne [string]::Join('|', ($expectedPackages | Sort-Object))) {
            $errors.Add('Evaluated test packages are not the exact approved names and versions.')
        }
        if ($projectReferences.Count -ne 1 -or
            [System.IO.Path]::GetFullPath($projectReferences[0].FullPath) -ne [System.IO.Path]::GetFullPath($ExpectedPrototypeProject)) {
            $errors.Add('Evaluated test project reference is not exactly the isolated prototype.')
        }
        $explicitUsings = @($data.Items.Using | Where-Object { $_.DefiningProjectFullPath -eq [System.IO.Path]::GetFullPath($ProjectPath) })
        if ($explicitUsings.Count -ne 1 -or $explicitUsings[0].Identity -ne 'Xunit') {
            $errors.Add('Evaluated explicit using items are not exactly Xunit.')
        }
        $content = @($data.Items.Content)
        if ($content.Count -notin @(0, 2) -or @($content | Where-Object {
            $_.DefiningProjectName -ne 'Microsoft.TestPlatform.TestHost' -or
            $_.Filename -ne 'testhost' -or
            $_.Extension -notin @('.dll', '.exe') -or
            $_.CopyToOutputDirectory -ne 'PreserveNewest'
        }).Count -ne 0) {
            $errors.Add('Evaluated test content is not the exact testhost package payload.')
        }
    }
    elseif ($packages.Count -ne 0 -or $projectReferences.Count -ne 0 -or @($data.Items.Content).Count -ne 0 -or
        @($data.Items.Using | Where-Object { $_.DefiningProjectFullPath -eq [System.IO.Path]::GetFullPath($ProjectPath) }).Count -ne 0) {
        $errors.Add('Evaluated prototype graph contains package, project, or explicit using dependencies.')
    }

    return @($errors)
}

function Test-Task03ProjectBoundary {
    param(
        [Parameter(Mandatory)]
        [string]$PrototypeProject,

        [Parameter(Mandatory)]
        [string]$TestProject,

        [string]$BoundaryRoot = (Split-Path -Parent (Split-Path -Parent $PrototypeProject)),

        [string[]]$ApprovedBuildFiles = @(),

        [string[]]$ApprovedEditorConfigFiles = @()
    )

    $errors = [System.Collections.Generic.List[string]]::new()
    if (-not (Test-Path -LiteralPath $PrototypeProject -PathType Leaf)) {
        $errors.Add('Prototype project is missing.')
    }
    if (-not (Test-Path -LiteralPath $TestProject -PathType Leaf)) {
        $errors.Add('Test project is missing.')
    }
    if ($errors.Count -gt 0) {
        return [pscustomobject]@{ Passed = $false; Errors = @($errors) }
    }

    $prototypeStatic = Test-Task03StaticProjectSafety `
        -ProjectPath $PrototypeProject `
        -BoundaryRoot $BoundaryRoot `
        -IsTestProject $false `
        -ApprovedBuildFiles $ApprovedBuildFiles `
        -ApprovedEditorConfigFiles $ApprovedEditorConfigFiles
    $testStatic = Test-Task03StaticProjectSafety `
        -ProjectPath $TestProject `
        -BoundaryRoot $BoundaryRoot `
        -IsTestProject $true `
        -ExpectedPrototypeProject $PrototypeProject `
        -ApprovedBuildFiles $ApprovedBuildFiles `
        -ApprovedEditorConfigFiles $ApprovedEditorConfigFiles
    foreach ($staticError in @($prototypeStatic.Errors)) { $errors.Add("Prototype static boundary: $staticError") }
    foreach ($staticError in @($testStatic.Errors)) { $errors.Add("Test static boundary: $staticError") }
    if (-not $prototypeStatic.Passed -or -not $testStatic.Passed) {
        return [pscustomobject]@{ Passed = $false; Errors = @($errors); EvaluationAttempted = $false }
    }

    try {
        [xml]$prototypeXml = Get-Content -LiteralPath $PrototypeProject -Raw
        [xml]$testXml = Get-Content -LiteralPath $TestProject -Raw
    }
    catch {
        $errors.Add('A project file is not valid XML.')
        return [pscustomobject]@{ Passed = $false; Errors = @($errors); EvaluationAttempted = $false }
    }

    foreach ($nodeName in @('PackageReference', 'ProjectReference', 'Reference', 'FrameworkReference', 'Compile')) {
        if (@($prototypeXml.SelectNodes("//$nodeName")).Count -ne 0) {
            $errors.Add("Prototype project contains forbidden $nodeName items.")
        }
    }

    $allowedPackages = @('Microsoft.NET.Test.Sdk', 'xunit', 'xunit.runner.visualstudio')
    $testPackages = @($testXml.SelectNodes('//PackageReference'))
    $testPackageNames = @($testPackages | ForEach-Object { $_.Include })
    if ($testPackages.Count -ne $allowedPackages.Count -or
        @($testPackageNames | Where-Object { $_ -notin $allowedPackages }).Count -gt 0 -or
        @($allowedPackages | Where-Object { $_ -notin $testPackageNames }).Count -gt 0) {
        $errors.Add('Test project package references are not the exact approved test-only set.')
    }

    $projectReferences = @($testXml.SelectNodes('//ProjectReference'))
    if ($projectReferences.Count -ne 1 -or
        [System.IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $TestProject) $projectReferences[0].Include)) -ne
        [System.IO.Path]::GetFullPath($PrototypeProject)) {
        $errors.Add('Test project must contain only the expected prototype project reference.')
    }

    foreach ($nodeName in @('Reference', 'FrameworkReference', 'Compile')) {
        if (@($testXml.SelectNodes("//$nodeName")).Count -ne 0) {
            $errors.Add("Test project contains forbidden $nodeName items.")
        }
    }

    $prototypeEvaluation = Get-Task03EvaluatedProject -ProjectPath $PrototypeProject
    $testEvaluation = Get-Task03EvaluatedProject -ProjectPath $TestProject
    if (-not $prototypeEvaluation.Passed) { $errors.Add($prototypeEvaluation.Error) }
    if (-not $testEvaluation.Passed) { $errors.Add($testEvaluation.Error) }
    if ($prototypeEvaluation.Passed -and $testEvaluation.Passed) {
        foreach ($evaluationError in @(Test-Task03EvaluatedItems -Evaluation $prototypeEvaluation -ProjectPath $PrototypeProject -IsTestProject $false -ApprovedEditorConfigFiles $ApprovedEditorConfigFiles)) {
            $errors.Add("Prototype evaluated boundary: $evaluationError")
        }
        foreach ($evaluationError in @(Test-Task03EvaluatedItems -Evaluation $testEvaluation -ProjectPath $TestProject -IsTestProject $true -ExpectedPrototypeProject $PrototypeProject -ApprovedEditorConfigFiles $ApprovedEditorConfigFiles)) {
            $errors.Add("Test evaluated boundary: $evaluationError")
        }
    }

    return [pscustomobject]@{
        Passed = $errors.Count -eq 0
        Errors = @($errors)
        EvaluationAttempted = $true
    }
}

function Find-Task03IlAdapterEvidence {
    param(
        [Parameter(Mandatory)]
        [string]$AssemblyPath
    )

    if (-not (Test-Path -LiteralPath $AssemblyPath -PathType Leaf)) {
        return @()
    }

    $evidence = [System.Collections.Generic.List[string]]::new()
    $forbiddenTypePrefixes = @(
        'System.Runtime.InteropServices.DllImportAttribute',
        'System.Runtime.InteropServices.LibraryImportAttribute',
        'System.Runtime.InteropServices.NativeLibrary',
        'System.Management.Automation',
        'System.Diagnostics.Process',
        'Microsoft.Win32.Registry',
        'System.ServiceProcess',
        'System.DirectoryServices.AccountManagement',
        'System.Security.AccessControl',
        'System.Security.Principal.WindowsIdentity'
    )
    $forbiddenAssemblies = @(
        'System.Management.Automation',
        'System.ServiceProcess.ServiceController',
        'System.DirectoryServices.AccountManagement',
        'System.Security.AccessControl'
    )
    $forbiddenMembers = @('SetAccessControl', 'GetAccessControl', 'CreateSubKey', 'OpenSubKey')

    $stream = [System.IO.File]::OpenRead((Resolve-Path -LiteralPath $AssemblyPath))
    $peReader = $null
    try {
        $peReader = [System.Reflection.PortableExecutable.PEReader]::new($stream)
        if (-not $peReader.HasMetadata) {
            return @('not-managed-metadata')
        }

        $reader = [System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($peReader)
        foreach ($handle in $reader.MethodDefinitions) {
            $method = $reader.GetMethodDefinition($handle)
            if (($method.Attributes -band [System.Reflection.MethodAttributes]::PinvokeImpl) -ne 0) {
                $evidence.Add("pinvoke:$($reader.GetString($method.Name))")
            }
        }

        foreach ($handle in $reader.ModuleReferences) {
            $module = $reader.GetModuleReference($handle)
            $evidence.Add("module:$($reader.GetString($module.Name))")
        }

        foreach ($handle in $reader.TypeReferences) {
            $type = $reader.GetTypeReference($handle)
            $namespace = $reader.GetString($type.Namespace)
            $name = $reader.GetString($type.Name)
            $fullName = if ($namespace.Length -eq 0) { $name } else { "$namespace.$name" }
            if (@($forbiddenTypePrefixes | Where-Object { $fullName.StartsWith($_, [System.StringComparison]::Ordinal) }).Count -gt 0) {
                $evidence.Add("type:$fullName")
            }
        }

        foreach ($handle in $reader.MemberReferences) {
            $member = $reader.GetMemberReference($handle)
            $memberName = $reader.GetString($member.Name)
            if ($memberName -in $forbiddenMembers) {
                $evidence.Add("member:$memberName")
            }
        }

        foreach ($handle in $reader.AssemblyReferences) {
            $assembly = $reader.GetAssemblyReference($handle)
            $assemblyName = $reader.GetString($assembly.Name)
            if ($assemblyName -in $forbiddenAssemblies) {
                $evidence.Add("assembly:$assemblyName")
            }
        }
    }
    catch {
        $evidence.Add('metadata-read-failed')
    }
    finally {
        if ($null -ne $peReader) {
            $peReader.Dispose()
        }
        $stream.Dispose()
    }

    return @($evidence | Sort-Object -Unique)
}

function Get-Task03ManagedAssemblyReferences {
    param([Parameter(Mandatory)] [string]$AssemblyPath)

    $references = [System.Collections.Generic.List[string]]::new()
    $stream = [System.IO.File]::OpenRead((Resolve-Path -LiteralPath $AssemblyPath))
    $peReader = $null
    try {
        $peReader = [System.Reflection.PortableExecutable.PEReader]::new($stream)
        if (-not $peReader.HasMetadata) { return @() }
        $reader = [System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($peReader)
        foreach ($handle in $reader.AssemblyReferences) {
            $reference = $reader.GetAssemblyReference($handle)
            $references.Add($reader.GetString($reference.Name))
        }
    }
    finally {
        if ($null -ne $peReader) { $peReader.Dispose() }
        $stream.Dispose()
    }
    return @($references | Sort-Object -Unique)
}

function Get-Task03ApprovedPackageAssemblyNames {
    param([Parameter(Mandatory)] [string]$AssetsPath)

    if (-not (Test-Path -LiteralPath $AssetsPath -PathType Leaf)) { return @() }
    $assets = Get-Content -LiteralPath $AssetsPath -Raw | ConvertFrom-Json -Depth 100
    $names = [System.Collections.Generic.List[string]]::new()
    foreach ($target in $assets.targets.PSObject.Properties.Value) {
        foreach ($libraryProperty in $target.PSObject.Properties) {
            $libraryMetadata = $assets.libraries.PSObject.Properties[$libraryProperty.Name].Value
            if ($libraryMetadata.type -ne 'package') { continue }
            $library = $libraryProperty.Value
            foreach ($groupName in @('runtime', 'runtimeTargets', 'compile')) {
                $group = $library.$groupName
                if ($null -eq $group) { continue }
                foreach ($asset in $group.PSObject.Properties.Name) {
                    if ([System.IO.Path]::GetExtension($asset) -eq '.dll') {
                        $names.Add([System.IO.Path]::GetFileNameWithoutExtension($asset))
                    }
                }
            }
        }
    }
    return @($names | Sort-Object -Unique)
}

function Find-Task03DependencyIlAdapterEvidence {
    param(
        [Parameter(Mandatory)] [string[]]$RootAssemblies,
        [Parameter(Mandatory)] [string[]]$SearchRoots,
        [string[]]$ApprovedAssemblyNames = @()
    )

    $candidates = @{}
    foreach ($root in $SearchRoots) {
        if (-not (Test-Path -LiteralPath $root -PathType Container)) { continue }
        foreach ($assembly in Get-ChildItem -LiteralPath $root -Recurse -File -Filter '*.dll') {
            $name = [System.IO.Path]::GetFileNameWithoutExtension($assembly.Name)
            if (-not $candidates.ContainsKey($name)) { $candidates[$name] = $assembly.FullName }
        }
    }

    $queue = [System.Collections.Generic.Queue[string]]::new()
    foreach ($assembly in $RootAssemblies) { $queue.Enqueue([System.IO.Path]::GetFullPath($assembly)) }
    $visited = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $evidence = [System.Collections.Generic.List[string]]::new()
    while ($queue.Count -gt 0) {
        $assembly = $queue.Dequeue()
        if (-not $visited.Add($assembly) -or -not (Test-Path -LiteralPath $assembly -PathType Leaf)) { continue }
        $name = [System.IO.Path]::GetFileNameWithoutExtension($assembly)
        if ($name -notin $ApprovedAssemblyNames) {
            foreach ($finding in @(Find-Task03IlAdapterEvidence -AssemblyPath $assembly)) {
                $evidence.Add("$([System.IO.Path]::GetFileName($assembly))|$finding")
            }
        }
        foreach ($referenceName in @(Get-Task03ManagedAssemblyReferences -AssemblyPath $assembly)) {
            if ($candidates.ContainsKey($referenceName)) { $queue.Enqueue($candidates[$referenceName]) }
        }
    }

    return @($evidence | Sort-Object -Unique)
}

function Invoke-Task03FixtureBuild {
    param(
        [Parameter(Mandatory)]
        [string]$ProjectPath
    )

    $output = & dotnet build $ProjectPath -c Release --nologo --verbosity quiet 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) {
        throw "Could not compile verifier mutation fixture: $output"
    }

    $projectName = [System.IO.Path]::GetFileNameWithoutExtension($ProjectPath)
    $assemblies = @(Get-ChildItem -LiteralPath (Join-Path (Split-Path -Parent $ProjectPath) 'bin\Release') -Recurse -File -Filter "$projectName.dll")
    if ($assemblies.Count -ne 1) { throw "Fixture build did not produce one exact root assembly for $projectName." }
    return $assemblies[0].FullName
}

function Test-Task03IsolationGuards {
    $fixtureRoot = Join-Path ([System.IO.Path]::GetTempPath()) "BallsServer.Task03Verifier.$([guid]::NewGuid().ToString('N'))"
    $detected = [System.Collections.Generic.List[string]]::new()
    $clean = [System.Collections.Generic.List[string]]::new()
    $cleanFailures = [System.Collections.Generic.List[string]]::new()
    $cleanupCompleted = $false
    $realAssembliesCompiled = 0
    $metadataPInvokeDetected = $false
    $structuralBuildLogicDetected = $false
    $structuralRejectedBeforeBuild = $false
    $analyzerInputDetected = $false
    $analyzerRejectedBeforeBuild = $false
    $analyzerSentinelAbsent = $false
    $namespacedBuildLogicDetected = $false
    $namespacedRejectedBeforeBuild = $false
    $namespacedSentinelAbsent = $false
    $rootPropsCompletenessDetected = $false
    $rootPropsRejectedBeforeBuild = $false
    $editorConfigInputDetected = $false
    $editorConfigRejectedBeforeBuild = $false
    $editorConfigCaseDriftDetected = $false
    $editorConfigCaseDriftRejectedBeforeBuild = $false
    $dependencyMetadataDetected = $false
    $dependencyMetadataFinding = ''

    try {
        $productionRoot = Join-Path $fixtureRoot 'production\nested\deeper'
        [void][System.IO.Directory]::CreateDirectory($productionRoot)
        $productionMutation = Join-Path $productionRoot 'ProductionMutation.cs'
        $productionProject = Join-Path $productionRoot 'Production.csproj'
        [System.IO.File]::WriteAllText($productionProject, '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>', [System.Text.UTF8Encoding]::new($false))
        [System.IO.File]::WriteAllText($productionMutation, 'namespace ManagedResourceSafety; public static class ProductionMutation { }', [System.Text.UTF8Encoding]::new($false))
        [void](Invoke-Task03FixtureBuild -ProjectPath $productionProject)
        $realAssembliesCompiled++
        if (@(Find-Task03ProductionReferences -ProductionRoot $fixtureRoot).Count -gt 0) {
            $detected.Add('production-reference')
        }
        [System.IO.File]::WriteAllText($productionMutation, 'namespace SafeFixture; public static class ProductionMutation { }', [System.Text.UTF8Encoding]::new($false))
        if (@(Find-Task03ProductionReferences -ProductionRoot $fixtureRoot).Count -eq 0) {
            $clean.Add('production-reference')
        }

        $sourceRoot = Join-Path $fixtureRoot 'prototype\nested\deeper'
        [void][System.IO.Directory]::CreateDirectory($sourceRoot)
        $sourceMutation = Join-Path $sourceRoot 'SourceMutation.cs'
        $sourceProject = Join-Path $sourceRoot 'SourceAdapter.csproj'
        [System.IO.File]::WriteAllText($sourceProject, '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><AllowUnsafeBlocks>true</AllowUnsafeBlocks></PropertyGroup></Project>', [System.Text.UTF8Encoding]::new($false))
        [System.IO.File]::WriteAllText($sourceMutation, 'using System.Runtime.InteropServices; internal static class SourceMutation { [DllImport("kernel32.dll")] internal static extern void Mutate(); }', [System.Text.UTF8Encoding]::new($false))
        $sourceAssembly = Invoke-Task03FixtureBuild -ProjectPath $sourceProject
        $realAssembliesCompiled++
        if (@(Find-Task03SourceAdapterEvidence -PrototypeRoot (Join-Path $fixtureRoot 'prototype')).Count -gt 0) {
            $detected.Add('source-adapter')
        }
        $metadataEvidence = @(Find-Task03IlAdapterEvidence -AssemblyPath $sourceAssembly)
        if (@($metadataEvidence | Where-Object { $_ -like 'pinvoke:*' -or $_ -like 'module:*' }).Count -gt 0) {
            $detected.Add('il-adapter')
            $metadataPInvokeDetected = $true
        }
        [System.IO.File]::WriteAllText($sourceMutation, 'internal static class SourceMutation { internal static void Observe() { } }', [System.Text.UTF8Encoding]::new($false))
        $sourceAssembly = Invoke-Task03FixtureBuild -ProjectPath $sourceProject
        if (@(Find-Task03SourceAdapterEvidence -PrototypeRoot (Join-Path $fixtureRoot 'prototype')).Count -eq 0) {
            $clean.Add('source-adapter')
        }
        if (@(Find-Task03IlAdapterEvidence -AssemblyPath $sourceAssembly).Count -eq 0) {
            $clean.Add('il-adapter')
        }

        $fixtureProps = Join-Path $fixtureRoot 'Directory.Build.props'
        $fixturePropsXml = '<Project><PropertyGroup><AnalysisLevel>latest-recommended</AnalysisLevel><Deterministic>true</Deterministic><EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild><ImplicitUsings>enable</ImplicitUsings><LangVersion>latest</LangVersion><Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup></Project>'
        [System.IO.File]::WriteAllText($fixtureProps, $fixturePropsXml, [System.Text.UTF8Encoding]::new($false))
        $boundaryArguments = @{
            BoundaryRoot = $fixtureRoot
            ApprovedBuildFiles = @($fixtureProps)
        }

        $projectRoot = Join-Path $fixtureRoot 'project-boundary'
        [void][System.IO.Directory]::CreateDirectory($projectRoot)
        $prototypeProject = Join-Path $projectRoot 'Prototype.csproj'
        $testProject = Join-Path $projectRoot 'Tests.csproj'
        $goodPrototype = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0-windows10.0.26100.0</TargetFramework></PropertyGroup></Project>'
        $goodLibrary = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0-windows10.0.26100.0</TargetFramework></PropertyGroup></Project>'
        $goodTest = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0-windows10.0.26100.0</TargetFramework><IsPackable>false</IsPackable><IsTestProject>true</IsTestProject><NoWarn>$(NoWarn);CA1707</NoWarn></PropertyGroup><ItemGroup><PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" /><PackageReference Include="xunit" Version="2.9.3" /><PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" PrivateAssets="all" /></ItemGroup><ItemGroup><ProjectReference Include="Prototype.csproj" /></ItemGroup><ItemGroup><Using Include="Xunit" /></ItemGroup></Project>'
        $dependencyRoot = Join-Path $fixtureRoot 'project-dependency-source'
        [void][System.IO.Directory]::CreateDirectory($dependencyRoot)
        $dependencyProject = Join-Path $dependencyRoot 'Dependency.csproj'
        [System.IO.File]::WriteAllText($dependencyProject, $goodLibrary, [System.Text.UTF8Encoding]::new($false))
        [System.IO.File]::WriteAllText((Join-Path $dependencyRoot 'Dependency.cs'), 'namespace SafeFixture; public static class Dependency { }', [System.Text.UTF8Encoding]::new($false))
        $dependencyMutation = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0-windows10.0.26100.0</TargetFramework></PropertyGroup><ItemGroup><ProjectReference Include="..\project-dependency-source\Dependency.csproj" /></ItemGroup></Project>'
        [System.IO.File]::WriteAllText($prototypeProject, $dependencyMutation, [System.Text.UTF8Encoding]::new($false))
        [System.IO.File]::WriteAllText($testProject, $goodTest, [System.Text.UTF8Encoding]::new($false))
        [void](Invoke-Task03FixtureBuild -ProjectPath $prototypeProject)
        $realAssembliesCompiled++
        if (-not (Test-Task03ProjectBoundary -PrototypeProject $prototypeProject -TestProject $testProject @boundaryArguments).Passed) {
            $detected.Add('project-dependency')
        }
        [System.IO.File]::WriteAllText($prototypeProject, $goodPrototype, [System.Text.UTF8Encoding]::new($false))
        $projectCleanResult = Test-Task03ProjectBoundary -PrototypeProject $prototypeProject -TestProject $testProject @boundaryArguments
        if ($projectCleanResult.Passed) {
            $clean.Add('project-dependency')
        }
        else { $cleanFailures.Add("project-dependency: $([string]::Join('; ', $projectCleanResult.Errors))") }

        $outsideRoot = Join-Path $fixtureRoot 'outside'
        [void][System.IO.Directory]::CreateDirectory($outsideRoot)
        [System.IO.File]::WriteAllText((Join-Path $outsideRoot 'Mutation.cs'), 'namespace SafeFixture; public static class LinkedMutation { }', [System.Text.UTF8Encoding]::new($false))
        $linkedCompileMutation = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0-windows10.0.26100.0</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup><Compile Include="..\outside\Mutation.cs" Link="Mutation.cs" /></ItemGroup></Project>'
        [System.IO.File]::WriteAllText($prototypeProject, $linkedCompileMutation, [System.Text.UTF8Encoding]::new($false))
        [void](Invoke-Task03FixtureBuild -ProjectPath $prototypeProject)
        $realAssembliesCompiled++
        if (-not (Test-Task03ProjectBoundary -PrototypeProject $prototypeProject -TestProject $testProject @boundaryArguments).Passed) {
            $detected.Add('linked-compile')
        }
        [System.IO.File]::WriteAllText($prototypeProject, $goodPrototype, [System.Text.UTF8Encoding]::new($false))
        $linkedCleanResult = Test-Task03ProjectBoundary -PrototypeProject $prototypeProject -TestProject $testProject @boundaryArguments
        if ($linkedCleanResult.Passed) {
            $clean.Add('linked-compile')
        }
        else { $cleanFailures.Add("linked-compile: $([string]::Join('; ', $linkedCleanResult.Errors))") }

        $structuralMutation = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0-windows10.0.26100.0</TargetFramework></PropertyGroup><Target Name="ForbiddenBuildLogic"><Exec Command="dotnet --info" /></Target></Project>'
        [System.IO.File]::WriteAllText($prototypeProject, $structuralMutation, [System.Text.UTF8Encoding]::new($false))
        $structuralResult = Test-Task03ProjectBoundary -PrototypeProject $prototypeProject -TestProject $testProject @boundaryArguments
        if (-not $structuralResult.Passed -and -not $structuralResult.EvaluationAttempted) {
            $detected.Add('structural-build-logic')
            $structuralBuildLogicDetected = $true
            $structuralRejectedBeforeBuild = $true
        }
        [System.IO.File]::WriteAllText($prototypeProject, $goodPrototype, [System.Text.UTF8Encoding]::new($false))
        $structuralCleanResult = Test-Task03ProjectBoundary -PrototypeProject $prototypeProject -TestProject $testProject @boundaryArguments
        if ($structuralCleanResult.Passed) {
            $clean.Add('structural-build-logic')
        }
        else { $cleanFailures.Add("structural-build-logic: $([string]::Join('; ', $structuralCleanResult.Errors))") }

        $analyzerRoot = Join-Path $fixtureRoot 'analyzer-input'
        [void][System.IO.Directory]::CreateDirectory($analyzerRoot)
        $analyzerProject = Join-Path $analyzerRoot 'SentinelGenerator.csproj'
        $analyzerSource = Join-Path $analyzerRoot 'SentinelGenerator.cs'
        $analyzerSentinel = Join-Path $analyzerRoot 'must-not-exist.txt'
        $dotnetRoot = Split-Path -Parent (Get-Command dotnet).Source
        $sdkVersion = (& dotnet --version).Trim()
        $roslynRoot = Join-Path $dotnetRoot "sdk\$sdkVersion\Roslyn\bincore"
        $escapedCodeAnalysis = [System.Security.SecurityElement]::Escape((Join-Path $roslynRoot 'Microsoft.CodeAnalysis.dll'))
        $escapedCodeAnalysisCSharp = [System.Security.SecurityElement]::Escape((Join-Path $roslynRoot 'Microsoft.CodeAnalysis.CSharp.dll'))
        $escapedAnalyzerSentinel = $analyzerSentinel.Replace('\', '\\')
        $analyzerProjectXml = "<Project Sdk=`"Microsoft.NET.Sdk`"><PropertyGroup><TargetFramework>net10.0</TargetFramework><NoWarn>`$(NoWarn);RS1036</NoWarn></PropertyGroup><ItemGroup><Reference Include=`"Microsoft.CodeAnalysis`" HintPath=`"$escapedCodeAnalysis`" Private=`"false`" /><Reference Include=`"Microsoft.CodeAnalysis.CSharp`" HintPath=`"$escapedCodeAnalysisCSharp`" Private=`"false`" /></ItemGroup></Project>"
        $analyzerSourceText = "using System.IO; using Microsoft.CodeAnalysis; namespace SafeFixture; [Generator] public sealed class SentinelGenerator : IIncrementalGenerator { public void Initialize(IncrementalGeneratorInitializationContext context) { File.WriteAllText(`"$escapedAnalyzerSentinel`", `"executed`"); } }"
        [System.IO.File]::WriteAllText($analyzerProject, $analyzerProjectXml, [System.Text.UTF8Encoding]::new($false))
        [System.IO.File]::WriteAllText($analyzerSource, $analyzerSourceText, [System.Text.UTF8Encoding]::new($false))
        $analyzerAssembly = Invoke-Task03FixtureBuild -ProjectPath $analyzerProject
        $realAssembliesCompiled++
        $escapedAnalyzerAssembly = [System.Security.SecurityElement]::Escape($analyzerAssembly)
        $analyzerMutation = $goodPrototype.Replace('</Project>', "<ItemGroup><Analyzer Include=`"$escapedAnalyzerAssembly`" /></ItemGroup></Project>")
        [System.IO.File]::WriteAllText($prototypeProject, $analyzerMutation, [System.Text.UTF8Encoding]::new($false))
        $analyzerResult = Test-Task03ProjectBoundary -PrototypeProject $prototypeProject -TestProject $testProject @boundaryArguments
        if (-not $analyzerResult.Passed -and -not $analyzerResult.EvaluationAttempted -and
            -not [System.IO.File]::Exists($analyzerSentinel)) {
            $detected.Add('analyzer-input')
            $analyzerInputDetected = $true
            $analyzerRejectedBeforeBuild = $true
            $analyzerSentinelAbsent = $true
        }
        [System.IO.File]::WriteAllText($prototypeProject, $goodPrototype, [System.Text.UTF8Encoding]::new($false))
        $analyzerCleanResult = Test-Task03ProjectBoundary -PrototypeProject $prototypeProject -TestProject $testProject @boundaryArguments
        if ($analyzerCleanResult.Passed -and -not [System.IO.File]::Exists($analyzerSentinel)) {
            $clean.Add('analyzer-input')
        }
        else { $cleanFailures.Add("analyzer-input: $([string]::Join('; ', $analyzerCleanResult.Errors))") }

        $namespacedSentinel = Join-Path $projectRoot 'namespaced-must-not-exist.txt'
        $escapedNamespacedSentinel = [System.Security.SecurityElement]::Escape($namespacedSentinel)
        $namespacedMutation = "<Project xmlns=`"http://schemas.microsoft.com/developer/msbuild/2003`" Sdk=`"Microsoft.NET.Sdk`"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0-windows10.0.26100.0</TargetFramework></PropertyGroup><Target Name=`"ForbiddenNamespacedBuildLogic`" BeforeTargets=`"CoreCompile`"><WriteLinesToFile File=`"$escapedNamespacedSentinel`" Lines=`"executed`" /></Target></Project>"
        [System.IO.File]::WriteAllText($prototypeProject, $namespacedMutation, [System.Text.UTF8Encoding]::new($false))
        $namespacedResult = Test-Task03ProjectBoundary -PrototypeProject $prototypeProject -TestProject $testProject @boundaryArguments
        if (-not $namespacedResult.Passed -and -not $namespacedResult.EvaluationAttempted -and
            -not [System.IO.File]::Exists($namespacedSentinel)) {
            $detected.Add('namespaced-build-logic')
            $namespacedBuildLogicDetected = $true
            $namespacedRejectedBeforeBuild = $true
            $namespacedSentinelAbsent = $true
        }
        [System.IO.File]::WriteAllText($prototypeProject, $goodPrototype, [System.Text.UTF8Encoding]::new($false))
        $namespacedCleanResult = Test-Task03ProjectBoundary -PrototypeProject $prototypeProject -TestProject $testProject @boundaryArguments
        if ($namespacedCleanResult.Passed -and -not [System.IO.File]::Exists($namespacedSentinel)) {
            $clean.Add('namespaced-build-logic')
        }
        else { $cleanFailures.Add("namespaced-build-logic: $([string]::Join('; ', $namespacedCleanResult.Errors))") }

        $fixturePropertyPairs = @(
            [pscustomobject]@{ Name = 'AnalysisLevel'; Value = 'latest-recommended' }
            [pscustomobject]@{ Name = 'Deterministic'; Value = 'true' }
            [pscustomobject]@{ Name = 'EnforceCodeStyleInBuild'; Value = 'true' }
            [pscustomobject]@{ Name = 'ImplicitUsings'; Value = 'enable' }
            [pscustomobject]@{ Name = 'LangVersion'; Value = 'latest' }
            [pscustomobject]@{ Name = 'Nullable'; Value = 'enable' }
            [pscustomobject]@{ Name = 'TreatWarningsAsErrors'; Value = 'true' }
        )
        $allMalformedPropsRejected = $true
        for ($missingIndex = 0; $missingIndex -lt $fixturePropertyPairs.Count; $missingIndex++) {
            $replacementIndex = if ($missingIndex -eq 0) { 1 } else { 0 }
            $malformedEntries = [System.Collections.Generic.List[string]]::new()
            for ($propertyIndex = 0; $propertyIndex -lt $fixturePropertyPairs.Count; $propertyIndex++) {
                if ($propertyIndex -eq $missingIndex) { continue }
                $pair = $fixturePropertyPairs[$propertyIndex]
                $malformedEntries.Add("<$($pair.Name)>$($pair.Value)</$($pair.Name)>")
            }
            $replacement = $fixturePropertyPairs[$replacementIndex]
            $malformedEntries.Add("<$($replacement.Name)>$($replacement.Value)</$($replacement.Name)>")
            $malformedProps = "<Project><PropertyGroup>$([string]::Join('', $malformedEntries))</PropertyGroup></Project>"
            [System.IO.File]::WriteAllText($fixtureProps, $malformedProps, [System.Text.UTF8Encoding]::new($false))
            $malformedPropsResult = Test-Task03ProjectBoundary -PrototypeProject $prototypeProject -TestProject $testProject @boundaryArguments
            if ($malformedPropsResult.Passed -or $malformedPropsResult.EvaluationAttempted) {
                $allMalformedPropsRejected = $false
            }
        }
        if ($allMalformedPropsRejected) {
            $detected.Add('root-props-completeness')
            $rootPropsCompletenessDetected = $true
            $rootPropsRejectedBeforeBuild = $true
        }
        [System.IO.File]::WriteAllText($fixtureProps, $fixturePropsXml, [System.Text.UTF8Encoding]::new($false))
        $rootPropsCleanResult = Test-Task03ProjectBoundary -PrototypeProject $prototypeProject -TestProject $testProject @boundaryArguments
        if ($rootPropsCleanResult.Passed) {
            $clean.Add('root-props-completeness')
        }
        else { $cleanFailures.Add("root-props-completeness: $([string]::Join('; ', $rootPropsCleanResult.Errors))") }

        $nestedEditorConfig = Join-Path $projectRoot '.editorconfig'
        [System.IO.File]::WriteAllText($nestedEditorConfig, "root = true`ndotnet_diagnostic.CA2000.severity = none`n", [System.Text.UTF8Encoding]::new($false))
        $editorConfigResult = Test-Task03ProjectBoundary -PrototypeProject $prototypeProject -TestProject $testProject @boundaryArguments
        $unapprovedEditorConfigRejectedBeforeBuild = -not $editorConfigResult.Passed -and -not $editorConfigResult.EvaluationAttempted
        [System.IO.File]::Delete($nestedEditorConfig)

        $rootEditorConfig = Join-Path $fixtureRoot '.editorconfig'
        [System.IO.File]::WriteAllText($rootEditorConfig, "root = true`ndotnet_diagnostic.CA2000.severity = none`n", [System.Text.UTF8Encoding]::new($false))
        [System.IO.File]::WriteAllText($nestedEditorConfig, "root = true`ndotnet_diagnostic.CA2000.severity = NONE`n", [System.Text.UTF8Encoding]::new($false))
        $caseDriftArguments = @{
            BoundaryRoot = $fixtureRoot
            ApprovedBuildFiles = @($fixtureProps)
            ApprovedEditorConfigFiles = @($rootEditorConfig, $nestedEditorConfig)
        }
        $editorConfigCaseDriftResult = Test-Task03ProjectBoundary -PrototypeProject $prototypeProject -TestProject $testProject @caseDriftArguments
        if (-not $editorConfigCaseDriftResult.Passed -and -not $editorConfigCaseDriftResult.EvaluationAttempted) {
            $editorConfigCaseDriftDetected = $true
            $editorConfigCaseDriftRejectedBeforeBuild = $true
        }
        [System.IO.File]::Delete($rootEditorConfig)
        [System.IO.File]::Delete($nestedEditorConfig)
        if ($unapprovedEditorConfigRejectedBeforeBuild -and $editorConfigCaseDriftDetected) {
            $detected.Add('editor-config-input')
            $editorConfigInputDetected = $true
            $editorConfigRejectedBeforeBuild = $true
        }
        $editorConfigCleanResult = Test-Task03ProjectBoundary -PrototypeProject $prototypeProject -TestProject $testProject @boundaryArguments
        if ($editorConfigCleanResult.Passed) {
            $clean.Add('editor-config-input')
        }
        else { $cleanFailures.Add("editor-config-input: $([string]::Join('; ', $editorConfigCleanResult.Errors))") }

        $importRoot = Join-Path $fixtureRoot 'imported-boundary'
        $importDependencyRoot = Join-Path $fixtureRoot 'imported-dependency'
        [void][System.IO.Directory]::CreateDirectory($importRoot)
        [void][System.IO.Directory]::CreateDirectory($importDependencyRoot)
        $importDependencyProject = Join-Path $importDependencyRoot 'DependencyMutation.csproj'
        [System.IO.File]::WriteAllText($importDependencyProject, '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><NoWarn>$(NoWarn);CA1401;SYSLIB1054</NoWarn></PropertyGroup></Project>', [System.Text.UTF8Encoding]::new($false))
        [System.IO.File]::WriteAllText((Join-Path $importDependencyRoot 'DependencyMutation.cs'), 'using System.Runtime.InteropServices; namespace SafeFixture; public static class DependencyMutation { [DllImport("kernel32.dll")] public static extern void Mutate(); public static void Observe() { } }', [System.Text.UTF8Encoding]::new($false))
        $importDependencyAssembly = Invoke-Task03FixtureBuild -ProjectPath $importDependencyProject
        $realAssembliesCompiled++
        $importPrototype = Join-Path $importRoot 'ImportedHost.csproj'
        $importTest = Join-Path $importRoot 'ImportedTests.csproj'
        $importProps = Join-Path $importRoot 'Directory.Build.props'
        [System.IO.File]::WriteAllText($importPrototype, '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>', [System.Text.UTF8Encoding]::new($false))
        [System.IO.File]::WriteAllText((Join-Path $importRoot 'ImportedHost.cs'), 'using SafeFixture; namespace SafeHost; public static class ImportedHost { public static void Observe() => DependencyMutation.Observe(); }', [System.Text.UTF8Encoding]::new($false))
        $escapedDependency = [System.Security.SecurityElement]::Escape($importDependencyAssembly)
        [System.IO.File]::WriteAllText($importProps, "<Project><ItemGroup><Reference Include=`"DependencyMutation`"><HintPath>$escapedDependency</HintPath><Private>true</Private></Reference></ItemGroup></Project>", [System.Text.UTF8Encoding]::new($false))
        $importHostAssembly = Invoke-Task03FixtureBuild -ProjectPath $importPrototype
        $realAssembliesCompiled++
        [System.IO.File]::WriteAllText($importPrototype, $goodPrototype, [System.Text.UTF8Encoding]::new($false))
        $goodImportTest = $goodTest.Replace('Prototype.csproj', 'ImportedHost.csproj')
        [System.IO.File]::WriteAllText($importTest, $goodImportTest, [System.Text.UTF8Encoding]::new($false))
        $importBoundary = Test-Task03ProjectBoundary -PrototypeProject $importPrototype -TestProject $importTest @boundaryArguments
        $dependencyEvidence = @(Find-Task03DependencyIlAdapterEvidence -RootAssemblies @($importHostAssembly) -SearchRoots @((Split-Path -Parent $importHostAssembly), (Split-Path -Parent $importDependencyAssembly)))
        $exactDependencyFinding = @($dependencyEvidence | Where-Object { $_ -eq 'DependencyMutation.dll|pinvoke:Mutate' })
        if (-not $importBoundary.Passed -and -not $importBoundary.EvaluationAttempted -and $exactDependencyFinding.Count -eq 1) {
            $detected.Add('imported-dependency')
            $dependencyMetadataDetected = $true
            $dependencyMetadataFinding = $exactDependencyFinding[0]
        }
        [System.IO.File]::Delete($importProps)
        $importIntermediate = Join-Path $importRoot 'obj'
        if ([System.IO.Directory]::Exists($importIntermediate)) {
            [System.IO.Directory]::Delete($importIntermediate, $true)
        }
        $importCleanResult = Test-Task03ProjectBoundary -PrototypeProject $importPrototype -TestProject $importTest @boundaryArguments
        if ($importCleanResult.Passed) {
            $clean.Add('imported-dependency')
        }
        else { $cleanFailures.Add("imported-dependency: $([string]::Join('; ', $importCleanResult.Errors))") }
    }
    finally {
        if ([System.IO.Directory]::Exists($fixtureRoot)) {
            [System.IO.Directory]::Delete($fixtureRoot, $true)
        }
        $cleanupCompleted = -not [System.IO.Directory]::Exists($fixtureRoot)
    }

    return [pscustomobject]@{
        Passed = $detected.Count -eq 11 -and $clean.Count -eq 11
        CleanupCompleted = $cleanupCompleted
        GuardClassesTested = 11
        RealAssembliesCompiled = $realAssembliesCompiled
        MetadataPInvokeDetected = $metadataPInvokeDetected
        StructuralBuildLogicDetected = $structuralBuildLogicDetected
        StructuralRejectedBeforeBuild = $structuralRejectedBeforeBuild
        AnalyzerInputDetected = $analyzerInputDetected
        AnalyzerRejectedBeforeBuild = $analyzerRejectedBeforeBuild
        AnalyzerSentinelAbsent = $analyzerSentinelAbsent
        NamespacedBuildLogicDetected = $namespacedBuildLogicDetected
        NamespacedRejectedBeforeBuild = $namespacedRejectedBeforeBuild
        NamespacedSentinelAbsent = $namespacedSentinelAbsent
        RootPropsCompletenessDetected = $rootPropsCompletenessDetected
        RootPropsRejectedBeforeBuild = $rootPropsRejectedBeforeBuild
        EditorConfigInputDetected = $editorConfigInputDetected
        EditorConfigRejectedBeforeBuild = $editorConfigRejectedBeforeBuild
        EditorConfigCaseDriftDetected = $editorConfigCaseDriftDetected
        EditorConfigCaseDriftRejectedBeforeBuild = $editorConfigCaseDriftRejectedBeforeBuild
        DependencyMetadataDetected = $dependencyMetadataDetected
        DependencyMetadataFinding = $dependencyMetadataFinding
        Detected = @($detected)
        CleanAfterRemoval = @($clean)
        CleanFailures = @($cleanFailures)
    }
}

Export-ModuleMember -Function Find-Task03ProductionReferences, Find-Task03SourceAdapterEvidence, Test-Task03ProjectBoundary, Find-Task03IlAdapterEvidence, Find-Task03DependencyIlAdapterEvidence, Get-Task03ApprovedPackageAssemblyNames, Test-Task03IsolationGuards
