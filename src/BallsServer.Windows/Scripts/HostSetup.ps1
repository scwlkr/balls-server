$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$payload = [Console]::In.ReadToEnd() | ConvertFrom-Json

$shareName = 'Balls'
$groupName = 'Balls Server Access'
$marker = 'Balls Server managed object v2'
$firewallName = if ($payload.AccessPath -eq 'Tailscale') { 'BallsServer-SMB-Tailscale-v1' } else { 'BallsServer-SMB-Local-v1' }
$stateDirectory = Join-Path ([Environment]::GetFolderPath('CommonApplicationData')) 'Balls Server'
$ledgerPath = Join-Path $stateDirectory 'host-state.json'
$temporaryLedgerPath = Join-Path $stateDirectory 'host-state.pending.json'
$journalPath = Join-Path $stateDirectory 'host-state.journal'
$createdGroup = $false
$createdUser = $false
$addedAce = $false
$createdShare = $false
$createdFirewall = $false
$createdLedger = $false
$folderRule = $null
$ledger = $null

function Test-PrivateIpv4([string]$Address) {
  $octets = $Address.Split('.')
  if ($octets.Count -ne 4) { return $false }
  $values = @($octets | ForEach-Object { [int]$_ })
  return $values[0] -eq 10 -or
    ($values[0] -eq 172 -and $values[1] -ge 16 -and $values[1] -le 31) -or
    ($values[0] -eq 192 -and $values[1] -eq 168)
}

function Test-TailscaleIpv4([string]$Address) {
  $octets = $Address.Split('.')
  if ($octets.Count -ne 4) { return $false }
  $first = [int]$octets[0]
  $second = [int]$octets[1]
  return $first -eq 100 -and $second -ge 64 -and $second -le 127
}

function Set-ProtectedLedgerAcl([string]$DirectoryPath, [string]$FilePath) {
  $administrators = ([Security.Principal.SecurityIdentifier]'S-1-5-32-544').Translate([Security.Principal.NTAccount])
  $system = ([Security.Principal.SecurityIdentifier]'S-1-5-18').Translate([Security.Principal.NTAccount])
  $owner = [Security.Principal.WindowsIdentity]::GetCurrent().User.Translate([Security.Principal.NTAccount])
  $inheritance = [Security.AccessControl.InheritanceFlags]'ContainerInherit, ObjectInherit'
  $allow = [Security.AccessControl.AccessControlType]::Allow
  $directorySecurity = New-Object Security.AccessControl.DirectorySecurity
  $directorySecurity.SetOwner($system)
  $directorySecurity.SetAccessRuleProtection($true, $false)
  $directorySecurity.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($administrators, 'FullControl', $inheritance, 'None', $allow)))
  $directorySecurity.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($system, 'FullControl', $inheritance, 'None', $allow)))
  $directorySecurity.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($owner, 'ReadAndExecute', $inheritance, 'None', $allow)))
  (Get-Item -LiteralPath $DirectoryPath).SetAccessControl($directorySecurity)
  $fileSecurity = New-Object Security.AccessControl.FileSecurity
  $fileSecurity.SetOwner($system)
  $fileSecurity.SetAccessRuleProtection($true, $false)
  $fileSecurity.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($administrators, 'FullControl', $allow)))
  $fileSecurity.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($system, 'FullControl', $allow)))
  $fileSecurity.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($owner, 'Read', $allow)))
  (Get-Item -LiteralPath $FilePath).SetAccessControl($fileSecurity)
}

function Write-ProtectedLedger($Ledger) {
  New-Item -ItemType Directory -Path $stateDirectory -Force | Out-Null
  [IO.File]::WriteAllText($temporaryLedgerPath, ($Ledger | ConvertTo-Json -Depth 5 -Compress), (New-Object Text.UTF8Encoding($false)))
  Set-ProtectedLedgerAcl $stateDirectory $temporaryLedgerPath
  Move-Item -LiteralPath $temporaryLedgerPath -Destination $ledgerPath -Force
  Set-ProtectedLedgerAcl $stateDirectory $ledgerPath
  $journalRecord = [ordered]@{
    schemaVersion = 2
    transactionId = $Ledger.transactionId
    productHostId = $Ledger.productHostId
    revision = $Ledger.revision
    status = $Ledger.status
    startedPrimitive = $Ledger.startedPrimitive
    appliedPrimitives = @($Ledger.appliedPrimitives)
  } | ConvertTo-Json -Compress
  [IO.File]::AppendAllText($journalPath, $journalRecord + [Environment]::NewLine, (New-Object Text.UTF8Encoding($false)))
  Set-ProtectedLedgerAcl $stateDirectory $journalPath
}

function Test-PathOverlap([string]$First, [string]$Second) {
  $left = [IO.Path]::GetFullPath($First).TrimEnd('\')
  $right = [IO.Path]::GetFullPath($Second).TrimEnd('\')
  return $left -eq $right -or
    $left.StartsWith($right + '\', [StringComparison]::OrdinalIgnoreCase) -or
    $right.StartsWith($left + '\', [StringComparison]::OrdinalIgnoreCase)
}

function Get-Sha256([string]$Value) {
  $bytes = [Text.Encoding]::UTF8.GetBytes($Value)
  $algorithm = [Security.Cryptography.SHA256]::Create()
  try {
    return ([BitConverter]::ToString($algorithm.ComputeHash($bytes))).Replace('-', '').ToLowerInvariant()
  } finally {
    $algorithm.Dispose()
  }
}

function Get-Fingerprint($Value) {
  return Get-Sha256 ($Value | ConvertTo-Json -Depth 10 -Compress)
}

function Invoke-HelperMode([string]$Mode, [string]$InputValue) {
  $runnerPath = Join-Path $PSScriptRoot 'BallsServer.Helper.exe'
  if (-not (Test-Path -LiteralPath $runnerPath -PathType Leaf)) { throw 'The protected policy runner is missing.' }
  $output = $InputValue | & $runnerPath $Mode
  if ($LASTEXITCODE -ne 0) { throw 'The protected policy runner refused the observation.' }
  return ($output -join [Environment]::NewLine).Trim()
}

function Get-StableFolderIdentity([string]$Path) {
  return Invoke-HelperMode '--folder-identity' $Path
}

function Get-PropertyValue($Object, [string]$Name, $Default) {
  $property = $Object.PSObject.Properties[$Name]
  if ($null -eq $property) { return $Default }
  return $property.Value
}

function Get-DialectNumber($Value) {
  $number = 0
  if ([int]::TryParse([string]$Value, [ref]$number)) { return $number }
  $result = switch ([string]$Value) {
    'SMB202' { 514 }
    'SMB210' { 528 }
    'SMB300' { 768 }
    'SMB302' { 770 }
    'SMB311' { 785 }
    default { 0 }
  }
  return $result
}

function Get-AclRows($Acl) {
  return @($Acl.Access | ForEach-Object {
    '{0}|{1}|{2}|{3}|{4}|{5}' -f $_.IdentityReference.Value,
      [int]$_.AccessControlType, [int]$_.FileSystemRights, [int]$_.InheritanceFlags,
      [int]$_.PropagationFlags, [bool]$_.IsInherited
  } | Sort-Object)
}

function Get-OwnershipObservation($Ledger) {
  $group = Get-LocalGroup -SID $Ledger.groupSid -ErrorAction Stop
  $user = Get-LocalUser -SID $Ledger.userSid -ErrorAction Stop
  $members = @(Get-LocalGroupMember -SID $Ledger.groupSid -ErrorAction Stop |
    ForEach-Object { $_.SID.Value } | Sort-Object)
  $accountGroupSids = @(Get-LocalGroup -ErrorAction Stop | Where-Object {
    @(Get-LocalGroupMember -SID $_.SID -ErrorAction Stop | Where-Object {
      $_.SID.Value -eq $Ledger.userSid
    }).Count -eq 1
  } | ForEach-Object { $_.SID.Value } | Sort-Object)
  $folderItem = Get-Item -LiteralPath $Ledger.managedFolder -Force -ErrorAction Stop
  $folderStableId = Get-StableFolderIdentity $folderItem.FullName
  $acl = Get-Acl -LiteralPath $Ledger.managedFolder -ErrorAction Stop
  $aclRows = @(Get-AclRows $acl)
  $ownedAceRows = @($aclRows | Where-Object {
    $_.StartsWith($Ledger.groupSid + '|0|', [StringComparison]::Ordinal)
  })
  $unrelatedAclRows = @($aclRows | Where-Object { $ownedAceRows -notcontains $_ })
  $conflictingAceCount = @($acl.Access | Where-Object {
    $_.IdentityReference.Value -eq $Ledger.groupSid -and
    ($_.AccessControlType -ne [Security.AccessControl.AccessControlType]::Allow -or
     ($_.FileSystemRights -band [Security.AccessControl.FileSystemRights]::Modify) -eq 0 -or
     ($_.InheritanceFlags -band [Security.AccessControl.InheritanceFlags]::ContainerInherit) -eq 0 -or
     ($_.InheritanceFlags -band [Security.AccessControl.InheritanceFlags]::ObjectInherit) -eq 0)
  }).Count
  if ($ownedAceRows.Count -ne 1) { $conflictingAceCount++ }
  $share = Get-SmbShare -Name $Ledger.shareName -ErrorAction Stop
  $shareStableId = if ($share.Description -eq $Ledger.shareMarker) { $Ledger.shareId } else { 'marker-mismatch' }
  $shareAccess = @(Get-SmbShareAccess -Name $Ledger.shareName -ErrorAction Stop | ForEach-Object {
    '{0}|{1}|{2}' -f $_.AccountName, $_.AccessControlType, $_.AccessRight
  } | Sort-Object)
  $firewall = Get-NetFirewallRule -Name $Ledger.firewallRuleName -ErrorAction Stop
  $firewallStableId = if ($firewall.Description -eq $Ledger.firewallMarker) { $Ledger.firewallRuleId } else { 'marker-mismatch' }
  $port = Get-NetFirewallPortFilter -AssociatedNetFirewallRule $firewall -ErrorAction Stop
  $address = Get-NetFirewallAddressFilter -AssociatedNetFirewallRule $firewall -ErrorAction Stop
  $interface = Get-NetFirewallInterfaceFilter -AssociatedNetFirewallRule $firewall -ErrorAction Stop
  $server = Get-Service -Name LanmanServer -ErrorAction Stop
  $smb = Get-SmbServerConfiguration -ErrorAction Stop
  $minimumDialect = Get-DialectNumber $smb.Smb2DialectMin
  $maximumDialect = Get-DialectNumber $smb.Smb2DialectMax
  $restrictAnonymous = Get-ItemPropertyValue -LiteralPath 'HKLM:\SYSTEM\CurrentControlSet\Control\Lsa' -Name RestrictAnonymous -ErrorAction Stop
  $limitBlankPasswords = Get-ItemPropertyValue -LiteralPath 'HKLM:\SYSTEM\CurrentControlSet\Control\Lsa' -Name LimitBlankPasswordUse -ErrorAction Stop
  $guestEnabled = [bool](Get-PropertyValue $smb 'EnableAuthenticateUserSharing' $false)
  $adapter = Get-NetAdapter -Name $Ledger.interfaceAlias -ErrorAction Stop
  $currentAddresses = @(Get-NetIPAddress -InterfaceIndex $adapter.InterfaceIndex -AddressFamily IPv4 -AddressState Preferred -ErrorAction Stop |
    ForEach-Object { $_.IPAddress } | Sort-Object)
  if ($Ledger.accessPath -eq 'Tailscale') {
    $tailscalePath = Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'Tailscale\tailscale.exe'
    $tailscaleStatusText = (& $tailscalePath status --json) -join [Environment]::NewLine
    if ($LASTEXITCODE -ne 0) { throw 'Tailscale endpoint observation failed.' }
    $currentEndpoint = ([string]($tailscaleStatusText | ConvertFrom-Json).Self.DNSName).TrimEnd('.')
  } else {
    $currentEndpoint = $env:COMPUTERNAME
  }
  $endpointFingerprint = Get-Fingerprint ([ordered]@{
    accessPath = $Ledger.accessPath
    endpoint = $currentEndpoint
    interfaceAlias = $adapter.Name
    localAddresses = $currentAddresses
  })
  $groupFingerprint = Get-Fingerprint ([ordered]@{
    sid = $group.SID.Value
    description = $group.Description
    members = $members
  })
  $accountFingerprint = Get-Fingerprint ([ordered]@{
    sid = $user.SID.Value
    description = $user.Description
    enabled = [bool]$user.Enabled
    accountExpires = [string]$user.AccountExpires
    passwordExpires = [string]$user.PasswordExpires
    userMayChangePassword = [bool]$user.UserMayChangePassword
    groupMemberships = $accountGroupSids
  })
  $membershipFingerprint = Get-Fingerprint ([ordered]@{
    groupSid = $group.SID.Value
    accountSid = $user.SID.Value
    members = $members
  })
  $folderFingerprint = Get-Fingerprint ([ordered]@{ stableId = $folderStableId; acl = $aclRows })
  $aceFingerprint = Get-Fingerprint ([ordered]@{ stableId = $Ledger.folderAceId; rules = $ownedAceRows })
  $shareFingerprint = Get-Fingerprint ([ordered]@{
    stableId = $shareStableId
    path = [IO.Path]::GetFullPath($share.Path)
    description = $share.Description
    access = $shareAccess
  })
  $firewallFingerprint = Get-Fingerprint ([ordered]@{
    stableId = $firewallStableId
    description = $firewall.Description
    enabled = [string]$firewall.Enabled
    direction = [string]$firewall.Direction
    action = [string]$firewall.Action
    profile = [string]$firewall.Profile
    interfaceAlias = @($interface.InterfaceAlias | Sort-Object)
    protocol = [string]$port.Protocol
    localPort = [string]$port.LocalPort
    localAddress = [string]$address.LocalAddress
    remoteAddress = [string]$address.RemoteAddress
  })
  $resources = @(
    [ordered]@{ kind = 'Group'; stableId = $group.SID.Value; fingerprint = $groupFingerprint },
    [ordered]@{ kind = 'Account'; stableId = $user.SID.Value; fingerprint = $accountFingerprint },
    [ordered]@{ kind = 'Membership'; stableId = "$($group.SID.Value):$($user.SID.Value)"; fingerprint = $membershipFingerprint },
    [ordered]@{ kind = 'FolderAce'; stableId = $Ledger.folderAceId; fingerprint = $aceFingerprint },
    [ordered]@{ kind = 'Share'; stableId = $shareStableId; fingerprint = $shareFingerprint },
    [ordered]@{ kind = 'FirewallRule'; stableId = $firewallStableId; fingerprint = $firewallFingerprint })
  $otherShareCount = @(Get-SmbShare -ErrorAction Stop | Where-Object {
    $_.Name -ne $Ledger.shareName -and -not $_.Special -and $null -ne $_.Path -and
    (Test-PathOverlap $_.Path $Ledger.managedFolder)
  }).Count
  $descendantReparseCount = @(Get-ChildItem -LiteralPath $Ledger.managedFolder -Force -Recurse -Attributes ReparsePoint -ErrorAction Stop).Count
  $expectedShareAccess = @(
    "$($Ledger.groupName)|Allow|Change",
    "$($Ledger.administratorsName)|Allow|Full") | Sort-Object
  $firewallSafe = [string]$firewall.Enabled -eq 'True' -and [string]$firewall.Direction -eq 'Inbound' -and
    [string]$firewall.Action -eq 'Allow' -and [string]$port.Protocol -eq 'TCP' -and
    [string]$port.LocalPort -eq '445' -and [string]$address.LocalAddress -eq $Ledger.localAddress -and
    [string]$address.RemoteAddress -eq $Ledger.remoteScope -and
    @($interface.InterfaceAlias).Count -eq 1 -and $interface.InterfaceAlias -eq $Ledger.interfaceAlias -and
    $adapter.Status -eq 'Up' -and $currentAddresses.Count -eq 1 -and $currentAddresses[0] -eq $Ledger.localAddress -and
    $currentEndpoint -eq $Ledger.endpoint
  $effectiveAccess = $user.Enabled -and $members.Count -eq 1 -and $members[0] -eq $user.SID.Value -and
    $accountGroupSids -contains $group.SID.Value -and $accountGroupSids -notcontains 'S-1-5-32-544' -and
    $ownedAceRows.Count -eq 1 -and @(Compare-Object $expectedShareAccess $shareAccess).Count -eq 0 -and $firewallSafe
  return [ordered]@{
    complete = $true
    serverRunning = $server.Status -eq 'Running'
    smb1Disabled = $smb.EnableSMB1Protocol -eq $false
    smb2Enabled = $smb.EnableSMB2Protocol -eq $true
    minimumDialect = $minimumDialect
    maximumDialect = $maximumDialect
    signingEnabled = $smb.EnableSecuritySignature -eq $true
    signingRequired = $smb.RequireSecuritySignature -eq $true
    guestDisabled = -not $guestEnabled
    anonymousDisabled = [int]$restrictAnonymous -ge 1
    blankPasswordsDisabled = [int]$limitBlankPasswords -eq 1
    firewallScopeSafe = $firewallSafe
    authenticatedEffectiveAccess = $effectiveAccess
    managedFolderStableId = $folderStableId
    managedFolderFingerprint = $folderFingerprint
    unrelatedAclFingerprint = Get-Fingerprint $unrelatedAclRows
    endpointFingerprint = $endpointFingerprint
    descendantReparseCount = $descendantReparseCount
    otherShareCount = $otherShareCount
    conflictingAceCount = $conflictingAceCount
    resources = @($resources | ForEach-Object {
      [ordered]@{ kind = $_.kind; state = 'Present'; stableId = $_.stableId; fingerprint = $_.fingerprint }
    })
  }
}

function Invoke-ProductionOwnershipPolicy($Ledger, $Live, [string]$Phase, [string]$ApprovedPlanDigest) {
  $policyRequest = [ordered]@{
    phase = $Phase
    operation = [string]$payload.Operation
    approvedPlanDigest = if ([string]::IsNullOrEmpty($ApprovedPlanDigest)) { $null } else { $ApprovedPlanDigest }
    ledger = $Ledger.ownership
    live = $Live
  } | ConvertTo-Json -Depth 12 -Compress
  return (Invoke-HelperMode '--ownership-policy' $policyRequest) | ConvertFrom-Json
}

function Get-InitialPlanFingerprint($Request) {
  $folder = [IO.Path]::GetFullPath([string]$Request.ManagedFolder)
  $folderItem = Get-Item -LiteralPath $folder -Force -ErrorAction Stop
  $folderAcl = Get-Acl -LiteralPath $folder -ErrorAction Stop
  $namedGroup = Get-LocalGroup -Name $groupName -ErrorAction SilentlyContinue
  $namedUser = Get-LocalUser -Name $Request.UserName -ErrorAction SilentlyContinue
  $namedShare = Get-SmbShare -Name $shareName -ErrorAction SilentlyContinue
  $namedFirewall = Get-NetFirewallRule -Name $firewallName -ErrorAction SilentlyContinue
  $smb = Get-SmbServerConfiguration -ErrorAction Stop
  return Get-Fingerprint ([ordered]@{
    operation = [string]$Request.Operation
    ownershipSeed = [string]$Request.OwnershipSeed
    managedFolder = $folderItem.FullName
    managedFolderStableId = Get-StableFolderIdentity $folderItem.FullName
    folderAttributes = [int]$folderItem.Attributes
    accessPath = [string]$Request.AccessPath
    userName = [string]$Request.UserName
    acl = @(Get-AclRows $folderAcl)
    descendantReparsePoints = @(Get-ChildItem -LiteralPath $folder -Force -Recurse -Attributes ReparsePoint -ErrorAction Stop |
      ForEach-Object { $_.FullName } | Sort-Object)
    sharesExposingFolder = @(Get-SmbShare -ErrorAction Stop | Where-Object {
      -not $_.Special -and $null -ne $_.Path -and (Test-PathOverlap $_.Path $folder)
    } | ForEach-Object { Get-Fingerprint ([ordered]@{ name = $_.Name; path = $_.Path; description = $_.Description }) } | Sort-Object)
    namedConflicts = @([bool]$namedGroup, [bool]$namedUser, [bool]$namedShare, [bool]$namedFirewall)
    smb = Get-Fingerprint ([ordered]@{
      smb1 = $smb.EnableSMB1Protocol
      smb2 = $smb.EnableSMB2Protocol
      min = [string]$smb.Smb2DialectMin
      max = [string]$smb.Smb2DialectMax
      signingEnabled = $smb.EnableSecuritySignature
      signingRequired = $smb.RequireSecuritySignature
    })
  })
}

function Get-PartialStopFingerprint($Ledger) {
  $group = Get-LocalGroup -SID $Ledger.groupSid -ErrorAction SilentlyContinue
  $user = Get-LocalUser -SID $Ledger.userSid -ErrorAction SilentlyContinue
  $share = Get-SmbShare -Name $Ledger.shareName -ErrorAction SilentlyContinue
  $firewall = Get-NetFirewallRule -Name $Ledger.firewallRuleName -ErrorAction SilentlyContinue
  $members = if ($null -eq $group) { @() } else {
    @(Get-LocalGroupMember -SID $Ledger.groupSid -ErrorAction Stop | ForEach-Object { $_.SID.Value } | Sort-Object)
  }
  $acl = if (Test-Path -LiteralPath $Ledger.managedFolder -PathType Container) {
    @(Get-AclRows (Get-Acl -LiteralPath $Ledger.managedFolder -ErrorAction Stop))
  } else { @() }
  return Get-Fingerprint ([ordered]@{
    operation = 'ResumeStopSharing'
    ledger = Get-Fingerprint $Ledger
    group = if ($null -eq $group) { $null } else { Get-Fingerprint $group }
    user = if ($null -eq $user) { $null } else { Get-Fingerprint $user }
    members = $members
    share = if ($null -eq $share) { $null } else { Get-Fingerprint $share }
    firewall = if ($null -eq $firewall) { $null } else { Get-Fingerprint $firewall }
    acl = $acl
  })
}

function Start-OwnedPrimitive([string]$Primitive) {
  if ($null -eq $script:ledger) { throw 'Ownership journal is unavailable.' }
  if ($script:ledger.startedPrimitive -eq $Primitive) { return }
  if ($null -ne $script:ledger.startedPrimitive) { throw 'Another ownership primitive is incomplete.' }
  $script:ledger.startedPrimitive = $Primitive
  if ($script:ledger.status -eq 'Stopping') { $script:ledger.stopStartedPrimitive = $Primitive }
  $script:ledger.revision = [long]$script:ledger.revision + 1
  Write-ProtectedLedger $script:ledger
}

function Complete-OwnedPrimitive([string]$Primitive) {
  if ($script:ledger.startedPrimitive -ne $Primitive) { throw 'Ownership journal intent does not match completion.' }
  $script:ledger.appliedPrimitives = @($script:ledger.appliedPrimitives) + $Primitive
  if ($script:ledger.status -eq 'Stopping') {
    $script:ledger.stopAppliedPrimitives = @($script:ledger.stopAppliedPrimitives) + $Primitive
    $script:ledger.stopStartedPrimitive = $null
  }
  $script:ledger.startedPrimitive = $null
  $script:ledger.revision = [long]$script:ledger.revision + 1
  Write-ProtectedLedger $script:ledger
}

function Read-ProtectedLedger {
  if (-not (Test-Path -LiteralPath $ledgerPath -PathType Leaf)) { return $null }
  $ledger = [IO.File]::ReadAllText($ledgerPath) | ConvertFrom-Json
  $required = @('schemaVersion', 'marker', 'transactionId', 'productHostId', 'ownerSid', 'status', 'managedFolder', 'accessPath', 'endpoint',
    'shareName', 'shareId', 'shareMarker', 'groupName', 'groupSid', 'groupMarker', 'userName', 'userSid', 'userMarker',
    'folderAceId', 'firewallRuleName', 'firewallRuleId', 'firewallMarker', 'interfaceAlias', 'localAddress', 'remoteScope',
    'administratorsName', 'revision', 'startedPrimitive', 'appliedPrimitives', 'stopStartedPrimitive',
    'stopAppliedPrimitives', 'ownership')
  $actual = @($ledger.PSObject.Properties.Name | Sort-Object)
  if (@(Compare-Object ($required | Sort-Object) $actual).Count -ne 0 -or
      $ledger.schemaVersion -ne 2 -or $ledger.marker -ne $marker -or
      $ledger.ownerSid -ne [Security.Principal.WindowsIdentity]::GetCurrent().User.Value -or
      $ledger.shareName -ne $shareName -or $ledger.groupName -ne $groupName) {
    throw 'The protected ownership record is invalid.'
  }
  return $ledger
}

function Test-ExactMarker($Object, [string]$ExpectedMarker) {
  return $null -ne $Object -and $Object.Description -eq $ExpectedMarker
}

function Undo-InterruptedSetup($Ledger) {
  $creationOrder = @('CreateGroup', 'CreateAccount', 'AddMembership', 'AddFolderAce', 'CreateShare', 'CreateFirewallRule', 'VerifyEffectiveAccess')
  $applied = @($Ledger.appliedPrimitives)
  for ($index = 0; $index -lt $applied.Count; $index++) {
    if ($index -ge $creationOrder.Count -or $applied[$index] -ne $creationOrder[$index]) {
      throw 'Interrupted setup journal is not an exact applied prefix.'
    }
  }
  if ($null -ne $Ledger.startedPrimitive -and
      ($applied.Count -ge $creationOrder.Count -or $Ledger.startedPrimitive -ne $creationOrder[$applied.Count])) {
    throw 'Interrupted setup journal has an invalid durable intent.'
  }
  $intent = @($applied)
  if ($null -ne $Ledger.startedPrimitive) { $intent += [string]$Ledger.startedPrimitive }
  $recoveryGroup = Get-LocalGroup -Name $Ledger.groupName -ErrorAction SilentlyContinue
  $recoveryUser = Get-LocalUser -Name $Ledger.userName -ErrorAction SilentlyContinue
  $recoveryShare = Get-SmbShare -Name $Ledger.shareName -ErrorAction SilentlyContinue
  $recoveryFirewall = Get-NetFirewallRule -Name $Ledger.firewallRuleName -ErrorAction SilentlyContinue
  if (($null -ne $recoveryGroup -and (-not (Test-ExactMarker $recoveryGroup $Ledger.groupMarker) -or
       ($Ledger.groupSid -and $recoveryGroup.SID.Value -ne $Ledger.groupSid))) -or
      ($null -ne $recoveryUser -and (-not (Test-ExactMarker $recoveryUser $Ledger.userMarker) -or
       ($Ledger.userSid -and $recoveryUser.SID.Value -ne $Ledger.userSid))) -or
      ($null -ne $recoveryShare -and ($recoveryShare.Description -ne $Ledger.shareMarker -or
       [IO.Path]::GetFullPath($recoveryShare.Path) -ne $Ledger.managedFolder)) -or
      ($null -ne $recoveryFirewall -and $recoveryFirewall.Description -ne $Ledger.firewallMarker)) {
    throw 'Interrupted setup contains changed or ambiguous ownership; repair is required.'
  }
  if ($intent -contains 'CreateFirewallRule' -and $null -ne $recoveryFirewall) { Remove-NetFirewallRule -Name $Ledger.firewallRuleName }
  if ($intent -contains 'CreateShare' -and $null -ne $recoveryShare) { Remove-SmbShare -Name $Ledger.shareName -Confirm:$false }
  if ($intent -contains 'AddFolderAce' -and $null -ne $recoveryGroup -and (Test-Path -LiteralPath $Ledger.managedFolder -PathType Container)) {
    $recoveryAcl = Get-Acl -LiteralPath $Ledger.managedFolder
    $recoveryRules = @($recoveryAcl.Access | Where-Object {
      $_.IdentityReference.Value -eq $recoveryGroup.SID.Value -and
      $_.AccessControlType -eq [Security.AccessControl.AccessControlType]::Allow -and
      ($_.FileSystemRights -band [Security.AccessControl.FileSystemRights]::Modify) -ne 0
    })
    if ($recoveryRules.Count -gt 1) { throw 'Interrupted folder ownership is ambiguous; repair is required.' }
    if ($recoveryRules.Count -eq 1) {
      $recoveryAcl.RemoveAccessRuleSpecific($recoveryRules[0])
      Set-Acl -LiteralPath $Ledger.managedFolder -AclObject $recoveryAcl
    }
  }
  if ($intent -contains 'AddMembership' -and $null -ne $recoveryUser -and $null -ne $recoveryGroup) {
    $membership = @(Get-LocalGroupMember -SID $recoveryGroup.SID -ErrorAction Stop |
      Where-Object { $_.SID.Value -eq $recoveryUser.SID.Value })
    if ($membership.Count -gt 1) { throw 'Interrupted membership ownership is ambiguous.' }
    if ($membership.Count -eq 1) {
      Remove-LocalGroupMember -SID $recoveryGroup.SID -Member $recoveryUser.SID -Confirm:$false
    }
  }
  if ($intent -contains 'CreateAccount' -and $null -ne $recoveryUser) { Remove-LocalUser -SID $recoveryUser.SID }
  if ($intent -contains 'CreateGroup' -and $null -ne $recoveryGroup) { Remove-LocalGroup -SID $recoveryGroup.SID }
  if ((Get-SmbShare -Name $Ledger.shareName -ErrorAction SilentlyContinue) -or
      (Get-NetFirewallRule -Name $Ledger.firewallRuleName -ErrorAction SilentlyContinue) -or
      (Get-LocalUser -Name $Ledger.userName -ErrorAction SilentlyContinue) -or
      (Get-LocalGroup -Name $Ledger.groupName -ErrorAction SilentlyContinue)) {
    throw 'Interrupted setup cleanup could not be verified.'
  }
  $Ledger.status = 'Canceled'
  $Ledger.appliedPrimitives = @($Ledger.appliedPrimitives) + 'ReconciledRollback'
  Write-ProtectedLedger $Ledger
}

try {
  $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
  if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Elevation is required.' }
  $payloadFields = @($payload.PSObject.Properties.Name | Sort-Object)
  $expectedPayloadFields = @('AccessPath', 'ApprovedPlanDigest', 'ManagedFolder', 'Operation', 'OwnershipSeed', 'Password', 'Phase', 'UserName')
  if (@(Compare-Object $expectedPayloadFields $payloadFields).Count -ne 0 -or
      $payload.Operation -notin @('Apply', 'StopSharing') -or
      $payload.Phase -notin @('Preview', 'Execute') -or
      $payload.OwnershipSeed -notmatch '^[0-9a-f]{32}$') { throw 'Unsupported host operation.' }
  $approvalLedger = Read-ProtectedLedger
  $policyResult = $null
  if ($null -ne $approvalLedger -and $approvalLedger.status -eq 'Committed') {
    $approvalLive = Get-OwnershipObservation $approvalLedger
    $policyResult = Invoke-ProductionOwnershipPolicy $approvalLedger $approvalLive ([string]$payload.Phase) ([string]$payload.ApprovedPlanDigest)
    if ($policyResult.status -notin @('PreviewReady', 'Ready', 'NoChanges')) {
      throw 'The authoritative ownership plan refused this operation.'
    }
    $planDigest = [string]$policyResult.planDigest
    $planRevision = [long]$approvalLedger.revision
  } elseif ($null -eq $approvalLedger -and $payload.Operation -eq 'Apply') {
    $planDigest = Get-InitialPlanFingerprint $payload
    $planRevision = 0
    if ($payload.Phase -eq 'Execute' -and $payload.ApprovedPlanDigest -ne $planDigest) {
      throw 'The approved host plan is stale.'
    }
  } elseif ($null -ne $approvalLedger -and $approvalLedger.status -in @('Applying', 'RepairNeeded') -and
      $payload.Operation -eq 'Apply') {
    $planDigest = Get-Fingerprint ([ordered]@{
      operation = 'RecoverThenApply'
      ownershipSeed = [string]$payload.OwnershipSeed
      ledger = Get-Fingerprint $approvalLedger
      requestedState = Get-InitialPlanFingerprint $payload
    })
    $planRevision = [long]$approvalLedger.revision
    if ($payload.Phase -eq 'Execute' -and $payload.ApprovedPlanDigest -ne $planDigest) {
      throw 'The approved recovery plan is stale.'
    }
  } elseif ($null -ne $approvalLedger -and $approvalLedger.status -eq 'Stopping' -and
      $payload.Operation -eq 'StopSharing') {
    $stopOrder = @('DisableAccount', 'RemoveMembership', 'RemoveShare', 'RemoveFirewallRule',
      'RemoveFolderAce', 'RemoveAccount', 'RemoveGroup')
    $stopApplied = @($approvalLedger.stopAppliedPrimitives)
    for ($index = 0; $index -lt $stopApplied.Count; $index++) {
      if ($index -ge $stopOrder.Count -or $stopApplied[$index] -ne $stopOrder[$index]) {
        throw 'Stop Sharing journal is not an exact applied prefix.'
      }
    }
    if ($null -ne $approvalLedger.stopStartedPrimitive -and
        ($stopApplied.Count -ge $stopOrder.Count -or
         $approvalLedger.stopStartedPrimitive -ne $stopOrder[$stopApplied.Count])) {
      throw 'Stop Sharing journal has an invalid durable intent.'
    }
    $planDigest = Get-PartialStopFingerprint $approvalLedger
    $planRevision = [long]$approvalLedger.revision
    if ($payload.Phase -eq 'Execute' -and $payload.ApprovedPlanDigest -ne $planDigest) {
      throw 'The approved Stop Sharing recovery plan is stale.'
    }
  } elseif ($null -eq $approvalLedger -and $payload.Operation -eq 'StopSharing') {
    $planDigest = Get-Fingerprint ([ordered]@{ operation = 'StopSharing'; ownership = 'Absent' })
    $planRevision = 0
    if ($payload.Phase -eq 'Execute' -and $payload.ApprovedPlanDigest -ne $planDigest) {
      throw 'The approved host plan is stale.'
    }
  } else {
    throw 'Protected ownership is incomplete; repair is required.'
  }
  if ($payload.Phase -eq 'Preview') {
    [ordered]@{ status = 'PreviewReady'; planDigest = $planDigest; revision = $planRevision } |
      ConvertTo-Json -Compress
    exit 0
  }
  if ($payload.Operation -eq 'StopSharing') {
    if ([string]$payload.ManagedFolder -ne '' -or [string]$payload.UserName -ne '' -or
        [string]$payload.Password -ne '') { throw 'Stop Sharing accepts no caller-selected resource.' }
    $ledger = Read-ProtectedLedger
    if ($null -eq $ledger -or $ledger.status -eq 'Removed') {
      [ordered]@{ status = 'Stopped'; shareName = $shareName } | ConvertTo-Json -Compress
      exit 0
    }
    if ($ledger.status -notin @('Committed', 'Stopping')) { throw 'Protected ownership is incomplete; repair is required.' }
    if ($ledger.status -eq 'Committed') {
      $authorizedPrimitives = @($policyResult.primitives | ForEach-Object { $_.kind })
      $requiredStopPrimitives = @('DisableAccount', 'RemoveMembership', 'RemoveShare', 'RemoveFirewallRule',
        'RemoveFolderAce', 'RemoveAccount', 'RemoveGroup', 'MarkHostRemoved')
      if (@(Compare-Object $requiredStopPrimitives $authorizedPrimitives).Count -ne 0) {
        throw 'The production ownership policy did not authorize the exact Stop Sharing sequence.'
      }
      $ledger.stopAppliedPrimitives = @()
      $ledger.stopStartedPrimitive = $null
      $ledger.status = 'Stopping'
      Write-ProtectedLedger $ledger
    }
    $ownedGroup = Get-LocalGroup -Name $ledger.groupName -ErrorAction SilentlyContinue
    $ownedUser = Get-LocalUser -Name $ledger.userName -ErrorAction SilentlyContinue
    $ownedShare = Get-SmbShare -Name $ledger.shareName -ErrorAction SilentlyContinue
    $ownedFirewall = Get-NetFirewallRule -Name $ledger.firewallRuleName -ErrorAction SilentlyContinue
    if (($null -ne $ownedGroup -and (-not (Test-ExactMarker $ownedGroup $ledger.groupMarker) -or $ownedGroup.SID.Value -ne $ledger.groupSid)) -or
        ($null -ne $ownedUser -and (-not (Test-ExactMarker $ownedUser $ledger.userMarker) -or $ownedUser.SID.Value -ne $ledger.userSid)) -or
        ($null -ne $ownedShare -and ($ownedShare.Description -ne $ledger.shareMarker -or [IO.Path]::GetFullPath($ownedShare.Path) -ne $ledger.managedFolder)) -or
        ($null -ne $ownedFirewall -and $ownedFirewall.Description -ne $ledger.firewallMarker)) {
      throw 'A recorded object no longer matches protected ownership.'
    }
    if ($ledger.stopAppliedPrimitives -notcontains 'DisableAccount') {
      if ($null -eq $ownedUser) { throw 'The recorded account disappeared before it was disabled.' }
      if (-not $ownedUser.Enabled -and $ledger.startedPrimitive -ne 'DisableAccount') {
        throw 'The recorded account changed without a durable intent.'
      }
      Start-OwnedPrimitive 'DisableAccount'
      if ($ownedUser.Enabled) { Disable-LocalUser -SID $ledger.userSid }
      if ((Get-LocalUser -SID $ledger.userSid -ErrorAction Stop).Enabled) { throw 'Account disable verification failed.' }
      Complete-OwnedPrimitive 'DisableAccount'
    }
    if ($ledger.stopAppliedPrimitives -notcontains 'RemoveMembership') {
      if ($null -eq $ownedUser -or $null -eq $ownedGroup) {
        throw 'The recorded membership cannot be proven before removal.'
      }
      $ownedMembership = @(Get-LocalGroupMember -SID $ledger.groupSid -ErrorAction Stop |
        Where-Object { $_.SID.Value -eq $ledger.userSid })
      if ($ownedMembership.Count -gt 1) { throw 'The recorded group membership is ambiguous.' }
      if ($ownedMembership.Count -eq 0 -and $ledger.startedPrimitive -ne 'RemoveMembership') {
        throw 'The recorded group membership changed without a durable intent.'
      }
      Start-OwnedPrimitive 'RemoveMembership'
      if ($ownedMembership.Count -eq 1) {
        Remove-LocalGroupMember -SID $ledger.groupSid -Member $ledger.userSid -Confirm:$false
      }
      if (@(Get-LocalGroupMember -SID $ledger.groupSid -ErrorAction Stop |
          Where-Object { $_.SID.Value -eq $ledger.userSid }).Count -ne 0) {
        throw 'Membership removal verification failed.'
      }
      Complete-OwnedPrimitive 'RemoveMembership'
    }
    if ($ledger.stopAppliedPrimitives -notcontains 'RemoveShare') {
      if ($null -eq $ownedShare -and $ledger.startedPrimitive -ne 'RemoveShare') {
        throw 'The recorded share changed without a durable intent.'
      }
      Start-OwnedPrimitive 'RemoveShare'
      if ($null -ne $ownedShare) { Remove-SmbShare -Name $ledger.shareName -Confirm:$false }
      if (Get-SmbShare -Name $ledger.shareName -ErrorAction SilentlyContinue) { throw 'Share removal verification failed.' }
      Complete-OwnedPrimitive 'RemoveShare'
    }
    if ($ledger.stopAppliedPrimitives -notcontains 'RemoveFirewallRule') {
      if ($null -eq $ownedFirewall -and $ledger.startedPrimitive -ne 'RemoveFirewallRule') {
        throw 'The recorded firewall rule changed without a durable intent.'
      }
      Start-OwnedPrimitive 'RemoveFirewallRule'
      if ($null -ne $ownedFirewall) { Remove-NetFirewallRule -Name $ledger.firewallRuleName }
      if (Get-NetFirewallRule -Name $ledger.firewallRuleName -ErrorAction SilentlyContinue) { throw 'Firewall removal verification failed.' }
      Complete-OwnedPrimitive 'RemoveFirewallRule'
    }
    if ($ledger.stopAppliedPrimitives -notcontains 'RemoveFolderAce' -and $null -ne $ownedGroup) {
      $ownedAcl = Get-Acl -LiteralPath $ledger.managedFolder
      $unrelatedBefore = @(Get-AclRows $ownedAcl | Where-Object {
        -not $_.StartsWith($ledger.groupSid + '|', [StringComparison]::Ordinal)
      })
      if ((Get-Fingerprint $unrelatedBefore) -ne $ledger.ownership.unrelatedAclFingerprint) {
        throw 'Unrelated folder permissions no longer match protected ownership.'
      }
      $ownedRules = @($ownedAcl.Access | Where-Object {
        $_.IdentityReference.Value -eq $ledger.groupSid -and
        $_.AccessControlType -eq [Security.AccessControl.AccessControlType]::Allow -and
        ($_.FileSystemRights -band [Security.AccessControl.FileSystemRights]::Modify) -ne 0
      })
      if ($ownedRules.Count -gt 1) { throw 'The recorded folder permission is ambiguous.' }
      if ($ownedRules.Count -eq 0 -and $ledger.startedPrimitive -ne 'RemoveFolderAce') {
        throw 'The recorded folder permission changed without a durable intent.'
      }
      Start-OwnedPrimitive 'RemoveFolderAce'
      if ($ownedRules.Count -eq 1) {
        $ownedAcl.RemoveAccessRuleSpecific($ownedRules[0])
        Set-Acl -LiteralPath $ledger.managedFolder -AclObject $ownedAcl
      }
      $remainingRules = @((Get-Acl -LiteralPath $ledger.managedFolder).Access | Where-Object {
        $_.IdentityReference.Value -eq $ledger.groupSid
      })
      if ($remainingRules.Count -ne 0) { throw 'Folder permission removal verification failed.' }
      $unrelatedAfter = @(Get-AclRows (Get-Acl -LiteralPath $ledger.managedFolder) | Where-Object {
        -not $_.StartsWith($ledger.groupSid + '|', [StringComparison]::Ordinal)
      })
      if ((Get-Fingerprint $unrelatedAfter) -ne $ledger.ownership.unrelatedAclFingerprint) {
        throw 'Unrelated folder permissions changed during removal.'
      }
      Complete-OwnedPrimitive 'RemoveFolderAce'
    }
    if ($ledger.stopAppliedPrimitives -notcontains 'RemoveAccount') {
      if ($null -eq $ownedUser -and $ledger.startedPrimitive -ne 'RemoveAccount') {
        throw 'The recorded account changed without a durable intent.'
      }
      Start-OwnedPrimitive 'RemoveAccount'
      if ($null -ne $ownedUser) { Remove-LocalUser -SID $ledger.userSid }
      if (Get-LocalUser -Name $ledger.userName -ErrorAction SilentlyContinue) { throw 'Account removal verification failed.' }
      Complete-OwnedPrimitive 'RemoveAccount'
    }
    if ($ledger.stopAppliedPrimitives -notcontains 'RemoveGroup') {
      if ($null -eq $ownedGroup -and $ledger.startedPrimitive -ne 'RemoveGroup') {
        throw 'The recorded group changed without a durable intent.'
      }
      Start-OwnedPrimitive 'RemoveGroup'
      if ($null -ne $ownedGroup) { Remove-LocalGroup -SID $ledger.groupSid }
      if (Get-LocalGroup -Name $ledger.groupName -ErrorAction SilentlyContinue) { throw 'Group removal verification failed.' }
      Complete-OwnedPrimitive 'RemoveGroup'
    }
    $ledger.status = 'Removed'
    Write-ProtectedLedger $ledger
    [ordered]@{ status = 'Stopped'; shareName = $shareName } | ConvertTo-Json -Compress
    exit 0
  }
  if ($payload.AccessPath -notin @('Local', 'Tailscale')) { throw 'Unsupported access path.' }
  if ($payload.UserName -notmatch '^BallsClient-[A-Z0-9]{6}$') { throw 'Invalid limited account name.' }
  if ([string]::IsNullOrWhiteSpace($payload.Password) -or $payload.Password.Length -lt 20) { throw 'Invalid limited credential.' }
  $folder = [IO.Path]::GetFullPath([string]$payload.ManagedFolder)
  if (-not [IO.Directory]::Exists($folder)) { throw 'Managed folder is unavailable.' }
  if ($folder.StartsWith('\\', [StringComparison]::Ordinal) -or $folder.StartsWith('\\?\', [StringComparison]::Ordinal)) {
    throw 'A remote or device path cannot be shared.'
  }
  $root = [IO.Path]::GetPathRoot($folder).TrimEnd('\')
  if ($folder.TrimEnd('\') -eq $root) { throw 'A drive root cannot be shared.' }
  $systemLocations = @(
    [Environment]::GetFolderPath('Windows'),
    [Environment]::GetFolderPath('ProgramFiles'),
    [Environment]::GetFolderPath('ProgramFilesX86'),
    [Environment]::GetFolderPath('CommonApplicationData')) | Where-Object { $_ }
  if (@($systemLocations | Where-Object {
    $folder -eq $_ -or $folder.StartsWith($_.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)
  }).Count -gt 0) { throw 'A protected system location cannot be shared.' }
  $candidate = Get-Item -LiteralPath $folder -Force
  while ($null -ne $candidate -and $candidate.FullName.TrimEnd('\') -ne $root) {
    if ($candidate.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'A reparse-point path cannot be shared.' }
    $candidate = $candidate.Parent
  }
  if (@(Get-ChildItem -LiteralPath $folder -Force -Recurse -Attributes ReparsePoint -ErrorAction Stop).Count -ne 0) {
    throw 'A folder containing a descendant reparse point cannot be shared.'
  }
  if (@(Get-SmbShare -ErrorAction Stop | Where-Object {
      -not $_.Special -and $null -ne $_.Path -and (Test-PathOverlap $_.Path $folder) -and
      -not ($null -ne $approvalLedger -and $approvalLedger.status -eq 'Committed' -and
        $_.Name -eq $approvalLedger.shareName -and $_.Description -eq $approvalLedger.shareMarker)
    }).Count -ne 0) { throw 'Another share already exposes the selected folder.' }
  $volume = Get-Volume -DriveLetter ([IO.Path]::GetPathRoot($folder).Substring(0, 1))
  if ($volume.FileSystem -ne 'NTFS' -or $volume.DriveType -ne 'Fixed') { throw 'A fixed NTFS folder is required.' }
  $serverService = Get-Service -Name LanmanServer -ErrorAction Stop
  $smbConfiguration = Get-SmbServerConfiguration -ErrorAction Stop
  $minimumDialect = Get-DialectNumber $smbConfiguration.Smb2DialectMin
  $maximumDialect = Get-DialectNumber $smbConfiguration.Smb2DialectMax
  $restrictAnonymous = Get-ItemPropertyValue -LiteralPath 'HKLM:\SYSTEM\CurrentControlSet\Control\Lsa' -Name RestrictAnonymous -ErrorAction Stop
  $limitBlankPasswords = Get-ItemPropertyValue -LiteralPath 'HKLM:\SYSTEM\CurrentControlSet\Control\Lsa' -Name LimitBlankPasswordUse -ErrorAction Stop
  $guestEnabled = [bool](Get-PropertyValue $smbConfiguration 'EnableAuthenticateUserSharing' $false)
  if ($serverService.Status -ne 'Running' -or $smbConfiguration.EnableSMB1Protocol -ne $false -or
      $smbConfiguration.EnableSMB2Protocol -ne $true -or $minimumDialect -lt 768 -or
      $maximumDialect -lt $minimumDialect -or $smbConfiguration.EnableSecuritySignature -ne $true -or
      $smbConfiguration.RequireSecuritySignature -ne $true -or $guestEnabled -or
      [int]$restrictAnonymous -lt 1 -or [int]$limitBlankPasswords -ne 1) {
    throw 'The complete authenticated SMB 3.0 safety intersection is not in the required state.'
  }
  $firewallProfiles = @(Get-NetFirewallProfile -PolicyStore ActiveStore -ErrorAction Stop |
    Where-Object { $_.Name -in @('Private', 'Domain') })
  if ($firewallProfiles.Count -eq 0 -or @($firewallProfiles | Where-Object { -not $_.Enabled }).Count -gt 0) {
    throw 'The private firewall policy is unavailable or disabled.'
  }
  $existingLedger = Read-ProtectedLedger
  if ($null -ne $existingLedger -and $existingLedger.status -in @('Applying', 'RepairNeeded')) {
    Undo-InterruptedSetup $existingLedger
    $existingLedger = Read-ProtectedLedger
  }
  if ($null -ne $existingLedger -and $existingLedger.status -eq 'Committed') {
    if ($existingLedger.managedFolder -ne $folder -or $existingLedger.accessPath -ne [string]$payload.AccessPath -or
        $existingLedger.firewallRuleName -ne $firewallName) { throw 'The approved setup differs from protected ownership.' }
    $verifiedGroup = Get-LocalGroup -Name $existingLedger.groupName -ErrorAction SilentlyContinue
    $verifiedUser = Get-LocalUser -Name $existingLedger.userName -ErrorAction SilentlyContinue
    $verifiedShare = Get-SmbShare -Name $existingLedger.shareName -ErrorAction SilentlyContinue
    $verifiedFirewall = Get-NetFirewallRule -Name $existingLedger.firewallRuleName -ErrorAction SilentlyContinue
    if (-not (Test-ExactMarker $verifiedGroup $existingLedger.groupMarker) -or $verifiedGroup.SID.Value -ne $existingLedger.groupSid -or
        -not (Test-ExactMarker $verifiedUser $existingLedger.userMarker) -or $verifiedUser.SID.Value -ne $existingLedger.userSid -or
        $null -eq $verifiedShare -or $verifiedShare.Description -ne $existingLedger.shareMarker -or
        [IO.Path]::GetFullPath($verifiedShare.Path) -ne $folder -or
        $null -eq $verifiedFirewall -or $verifiedFirewall.Description -ne $existingLedger.firewallMarker) {
      throw 'Protected host setup is missing, changed, or ambiguous.'
    }
    [ordered]@{
      hostName = $existingLedger.endpoint
      shareName = $shareName
      userName = "$env:COMPUTERNAME\$($existingLedger.userName)"
      alreadyConfigured = $true
    } | ConvertTo-Json -Compress
    exit 0
  }
  if ($null -ne $existingLedger -and $existingLedger.status -notin @('Canceled', 'Removed')) {
    throw 'Protected ownership is incomplete or ambiguous.'
  }
  if (Get-SmbShare -Name $shareName -ErrorAction SilentlyContinue) { throw 'The Balls share name is already in use.' }
  if (Get-LocalGroup -Name $groupName -ErrorAction SilentlyContinue) { throw 'The Balls Server access group already exists without an ownership record.' }
  if (Get-LocalUser -Name $payload.UserName -ErrorAction SilentlyContinue) { throw 'The limited account name is already in use.' }
  if (Get-NetFirewallRule -Name $firewallName -ErrorAction SilentlyContinue) { throw 'The Balls Server firewall rule already exists without an ownership record.' }
  if ($payload.AccessPath -eq 'Tailscale') {
    $adapters = @(Get-NetAdapter | Where-Object { $_.Status -eq 'Up' -and ($_.Name -like '*Tailscale*' -or $_.InterfaceDescription -like '*Tailscale*') })
  } else {
    $privateIndexes = @(Get-NetConnectionProfile | Where-Object { $_.NetworkCategory -in @('Private', 'DomainAuthenticated') } | Select-Object -ExpandProperty InterfaceIndex)
    $adapters = @(Get-NetAdapter | Where-Object { $_.Status -eq 'Up' -and $privateIndexes -contains $_.InterfaceIndex } | Sort-Object ifIndex)
  }
  if ($adapters.Count -ne 1) { throw 'Exactly one supported private adapter must be active.' }
  $adapter = $adapters[0]
  $profile = Get-NetConnectionProfile -InterfaceIndex $adapter.InterfaceIndex -ErrorAction Stop
  if ($profile.NetworkCategory -notin @('Private', 'DomainAuthenticated')) { throw 'The selected adapter is not private.' }
  $addresses = @(Get-NetIPAddress -InterfaceIndex $adapter.InterfaceIndex -AddressFamily IPv4 -AddressState Preferred | Select-Object -ExpandProperty IPAddress)
  if ($payload.AccessPath -eq 'Tailscale') {
    $addresses = @($addresses | Where-Object { Test-TailscaleIpv4 $_ })
    $remoteScope = '100.64.0.0/10'
  } else {
    $addresses = @($addresses | Where-Object { Test-PrivateIpv4 $_ })
    $remoteScope = 'LocalSubnet'
  }
  if ($addresses.Count -ne 1) { throw 'Exactly one supported private IPv4 address is required.' }
  if ($payload.AccessPath -eq 'Tailscale') {
    $tailscalePath = Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'Tailscale\tailscale.exe'
    if (-not (Test-Path -LiteralPath $tailscalePath -PathType Leaf)) { throw 'Tailscale is unavailable.' }
    $tailscaleStatusText = (& $tailscalePath status --json) -join [Environment]::NewLine
    if ($LASTEXITCODE -ne 0) { throw 'Tailscale status is unavailable.' }
    $tailscaleStatus = $tailscaleStatusText | ConvertFrom-Json
    $endpoint = ([string]$tailscaleStatus.Self.DNSName).TrimEnd('.')
    if (-not $endpoint.EndsWith('.ts.net', [StringComparison]::OrdinalIgnoreCase)) { throw 'Tailscale MagicDNS is unavailable.' }
  } else {
    $endpoint = $env:COMPUTERNAME
  }
  $administrators = ([Security.Principal.SecurityIdentifier]'S-1-5-32-544').Translate([Security.Principal.NTAccount]).Value
  $productHostId = [string]$payload.OwnershipSeed
  $groupResourceId = (Get-Sha256 ($productHostId + ':group')).Substring(0, 32)
  $accountResourceId = (Get-Sha256 ($productHostId + ':account')).Substring(0, 32)
  $shareResourceId = (Get-Sha256 ($productHostId + ':share')).Substring(0, 32)
  $firewallResourceId = (Get-Sha256 ($productHostId + ':firewall')).Substring(0, 32)
  $ledger = [ordered]@{
    schemaVersion = 2
    marker = $marker
    transactionId = [Guid]::NewGuid().ToString('N')
    productHostId = $productHostId
    ownerSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    status = 'Applying'
    managedFolder = $folder
    accessPath = [string]$payload.AccessPath
    endpoint = $endpoint
    shareName = $shareName
    shareId = $shareResourceId
    shareMarker = "${marker}:${productHostId}:${shareResourceId}"
    groupName = $groupName
    groupSid = ''
    groupMarker = "${marker}:${productHostId}:${groupResourceId}"
    userName = [string]$payload.UserName
    userSid = ''
    userMarker = "${marker}:${productHostId}:${accountResourceId}"
    folderAceId = (Get-Sha256 ($productHostId + ':folder-ace')).Substring(0, 32)
    firewallRuleName = $firewallName
    firewallRuleId = $firewallResourceId
    firewallMarker = "${marker}:${productHostId}:${firewallResourceId}"
    interfaceAlias = $adapter.Name
    localAddress = [string]$addresses[0]
    remoteScope = [string]$remoteScope
    administratorsName = [string]$administrators
    appliedPrimitives = @()
    startedPrimitive = $null
    stopAppliedPrimitives = @()
    stopStartedPrimitive = $null
    revision = 0
    ownership = $null
  }
  Write-ProtectedLedger $ledger
  $createdLedger = $true
  Start-OwnedPrimitive 'CreateGroup'
  $group = New-LocalGroup -Name $groupName -Description $ledger.groupMarker
  $createdGroup = $true
  $ledger.groupSid = $group.SID.Value
  if ((Get-LocalGroup -SID $group.SID -ErrorAction Stop).Description -ne $ledger.groupMarker) { throw 'Group creation verification failed.' }
  Complete-OwnedPrimitive 'CreateGroup'
  $securePassword = ConvertTo-SecureString ([string]$payload.Password) -AsPlainText -Force
  Start-OwnedPrimitive 'CreateAccount'
  $user = New-LocalUser -Name $payload.UserName -Password $securePassword -Description $ledger.userMarker -AccountNeverExpires -PasswordNeverExpires -UserMayNotChangePassword
  $createdUser = $true
  $ledger.userSid = $user.SID.Value
  $verifiedNewUser = Get-LocalUser -SID $user.SID -ErrorAction Stop
  if (-not $verifiedNewUser.Enabled -or $verifiedNewUser.Description -ne $ledger.userMarker -or
      $null -ne $verifiedNewUser.AccountExpires -or $verifiedNewUser.PasswordExpires -ne $null -or
      $verifiedNewUser.UserMayChangePassword) { throw 'Account creation verification failed.' }
  Complete-OwnedPrimitive 'CreateAccount'
  Start-OwnedPrimitive 'AddMembership'
  Add-LocalGroupMember -Group $groupName -Member $payload.UserName
  if (@(Get-LocalGroupMember -SID $group.SID -ErrorAction Stop |
      Where-Object { $_.SID.Value -eq $user.SID.Value }).Count -ne 1) { throw 'Membership creation verification failed.' }
  Complete-OwnedPrimitive 'AddMembership'
  $folderAcl = Get-Acl -LiteralPath $folder
  $folderRule = New-Object Security.AccessControl.FileSystemAccessRule(
    $group.SID,
    ([Security.AccessControl.FileSystemRights]::Modify -bor [Security.AccessControl.FileSystemRights]::Synchronize),
    ([Security.AccessControl.InheritanceFlags]::ContainerInherit -bor [Security.AccessControl.InheritanceFlags]::ObjectInherit),
    [Security.AccessControl.PropagationFlags]::None,
    [Security.AccessControl.AccessControlType]::Allow)
  Start-OwnedPrimitive 'AddFolderAce'
  $folderAcl.AddAccessRule($folderRule)
  Set-Acl -LiteralPath $folder -AclObject $folderAcl
  $addedAce = $true
  Complete-OwnedPrimitive 'AddFolderAce'
  Start-OwnedPrimitive 'CreateShare'
  New-SmbShare -Name $shareName -Path $folder -Description $ledger.shareMarker -ChangeAccess $groupName -FullAccess $administrators | Out-Null
  $createdShare = $true
  Complete-OwnedPrimitive 'CreateShare'
  Start-OwnedPrimitive 'CreateFirewallRule'
  New-NetFirewallRule -Name $firewallName -DisplayName "Balls Server SMB ($($payload.AccessPath))" -Group 'Balls Server' -Description $ledger.firewallMarker -Enabled True -Direction Inbound -Protocol TCP -LocalPort 445 -LocalAddress $addresses[0] -RemoteAddress $remoteScope -InterfaceAlias $adapter.Name -Profile Private,Domain -Action Allow | Out-Null
  $createdFirewall = $true
  Complete-OwnedPrimitive 'CreateFirewallRule'
  $verifiedShare = Get-SmbShare -Name $shareName -ErrorAction Stop
  if ([IO.Path]::GetFullPath($verifiedShare.Path) -ne $folder) { throw 'Share verification failed.' }
  $verifiedShareAccess = @(Get-SmbShareAccess -Name $shareName -ErrorAction Stop)
  if ($verifiedShareAccess.Count -ne 2 -or
      @($verifiedShareAccess | Where-Object {
        $_.AccessControlType -eq 'Allow' -and $_.AccessRight -eq 'Change' -and $_.AccountName -eq $groupName
      }).Count -ne 1 -or
      @($verifiedShareAccess | Where-Object {
        $_.AccessControlType -eq 'Allow' -and $_.AccessRight -eq 'Full' -and $_.AccountName -eq $administrators
      }).Count -ne 1) { throw 'Share permission verification failed.' }
  $verifiedMembership = @(Get-LocalGroupMember -Group $groupName -ErrorAction Stop | Where-Object { $_.SID.Value -eq $user.SID.Value })
  if ($verifiedMembership.Count -ne 1) { throw 'Limited account membership verification failed.' }
  $verifiedAcl = Get-Acl -LiteralPath $folder
  $verifiedFolderRules = @($verifiedAcl.Access | Where-Object {
    $_.IdentityReference.Value -eq $group.SID.Value -and
    $_.AccessControlType -eq [Security.AccessControl.AccessControlType]::Allow -and
    ($_.FileSystemRights -band [Security.AccessControl.FileSystemRights]::Modify) -ne 0 -and
    ($_.InheritanceFlags -band [Security.AccessControl.InheritanceFlags]::ContainerInherit) -ne 0 -and
    ($_.InheritanceFlags -band [Security.AccessControl.InheritanceFlags]::ObjectInherit) -ne 0
  })
  if ($verifiedFolderRules.Count -ne 1) { throw 'Folder permission verification failed.' }
  $verifiedRule = Get-NetFirewallRule -Name $firewallName -ErrorAction Stop
  if (-not $verifiedRule.Enabled -or $verifiedRule.Direction -ne 'Inbound' -or $verifiedRule.Action -ne 'Allow') { throw 'Firewall verification failed.' }
  $verifiedPort = Get-NetFirewallPortFilter -AssociatedNetFirewallRule $verifiedRule -ErrorAction Stop
  $verifiedAddress = Get-NetFirewallAddressFilter -AssociatedNetFirewallRule $verifiedRule -ErrorAction Stop
  if ($verifiedPort.Protocol -ne 'TCP' -or [string]$verifiedPort.LocalPort -ne '445' -or
      [string]$verifiedAddress.LocalAddress -ne [string]$addresses[0] -or
      [string]$verifiedAddress.RemoteAddress -ne [string]$remoteScope -or
      $verifiedRule.InterfaceAlias -ne $adapter.Name -or $verifiedRule.Profile -band 4) {
    throw 'Firewall scope verification failed.'
  }
  Start-OwnedPrimitive 'VerifyEffectiveAccess'
  $observation = Get-OwnershipObservation $ledger
  if (-not $observation.serverRunning -or -not $observation.smb1Disabled -or
      -not $observation.smb2Enabled -or $observation.minimumDialect -lt 768 -or
      $observation.maximumDialect -lt $observation.minimumDialect -or
      -not $observation.signingEnabled -or -not $observation.signingRequired -or
      -not $observation.guestDisabled -or -not $observation.anonymousDisabled -or
      -not $observation.blankPasswordsDisabled -or -not $observation.firewallScopeSafe -or
      -not $observation.authenticatedEffectiveAccess -or $observation.descendantReparseCount -ne 0 -or
      $observation.otherShareCount -ne 0 -or $observation.conflictingAceCount -ne 0) {
    throw 'Effective authenticated SMB access verification failed.'
  }
  Complete-OwnedPrimitive 'VerifyEffectiveAccess'
  $ledger.ownership = [ordered]@{
    schemaVersion = 2
    productHostId = $ledger.productHostId
    revision = [long]$ledger.revision
    status = 'Committed'
    desiredStateFingerprint = Get-Fingerprint ([ordered]@{
      managedFolderStableId = $observation.managedFolderStableId
      accessPath = $ledger.accessPath
      endpointFingerprint = $observation.endpointFingerprint
      resources = $observation.resources
    })
    managedFolderStableId = $observation.managedFolderStableId
    managedFolderFingerprint = $observation.managedFolderFingerprint
    unrelatedAclFingerprint = $observation.unrelatedAclFingerprint
    endpointFingerprint = $observation.endpointFingerprint
    resources = @($observation.resources | ForEach-Object {
      [ordered]@{ kind = $_.kind; stableId = $_.stableId; fingerprint = $_.fingerprint }
    })
    appliedPrimitives = @($ledger.appliedPrimitives)
    startedPrimitive = $null
  }
  $ledger.status = 'Committed'
  Write-ProtectedLedger $ledger
  [ordered]@{
    hostName = $endpoint
    shareName = $shareName
    userName = "$env:COMPUTERNAME\$($payload.UserName)"
    alreadyConfigured = $false
  } | ConvertTo-Json -Compress
} catch {
  if (Test-Path -LiteralPath $temporaryLedgerPath) { Remove-Item -LiteralPath $temporaryLedgerPath -Force -ErrorAction SilentlyContinue }
  if ($createdFirewall) { Remove-NetFirewallRule -Name $firewallName -ErrorAction SilentlyContinue }
  if ($createdShare) { Remove-SmbShare -Name $shareName -Confirm:$false -ErrorAction SilentlyContinue }
  if ($addedAce -and $null -ne $folderRule) {
    $rollbackAcl = Get-Acl -LiteralPath $folder -ErrorAction SilentlyContinue
    if ($null -ne $rollbackAcl) {
      $rollbackAcl.RemoveAccessRuleSpecific($folderRule)
      Set-Acl -LiteralPath $folder -AclObject $rollbackAcl -ErrorAction SilentlyContinue
    }
  }
  if ($createdUser) { Remove-LocalUser -Name $payload.UserName -ErrorAction SilentlyContinue }
  if ($createdGroup) { Remove-LocalGroup -Name $groupName -ErrorAction SilentlyContinue }
  if ($null -ne $ledger -and $createdLedger) {
    $remainingOwnedObject =
      (Get-NetFirewallRule -Name $firewallName -ErrorAction SilentlyContinue) -or
      (Get-SmbShare -Name $shareName -ErrorAction SilentlyContinue) -or
      (Get-LocalUser -Name $payload.UserName -ErrorAction SilentlyContinue) -or
      (Get-LocalGroup -Name $groupName -ErrorAction SilentlyContinue)
    $ledger.status = if ($remainingOwnedObject) { 'RepairNeeded' } else { 'Canceled' }
    if ($remainingOwnedObject) {
      $ledger.appliedPrimitives += 'RollbackIncomplete'
    } else {
      $ledger.appliedPrimitives += 'RollbackVerified'
    }
    try { Write-ProtectedLedger $ledger } catch { }
  }
  exit 1
}
