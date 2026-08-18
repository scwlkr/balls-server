param([switch]$PlanOnly)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$payload = [Console]::In.ReadToEnd() | ConvertFrom-Json

function Resolve-HostSetupPlan($Request) {
  $resourceStates = @($Request.resources)
  if ($resourceStates.Count -ne 5) {
    return [ordered]@{ status = 'Unknown'; primitives = @() }
  }
  if (@($resourceStates | Where-Object { $_ -eq 'Unknown' }).Count -gt 0) {
    return [ordered]@{ status = 'Unknown'; primitives = @() }
  }
  if (@($resourceStates | Where-Object { $_ -in @('Ambiguous', 'UnmanagedConflict') }).Count -gt 0) {
    return [ordered]@{ status = 'Refused'; primitives = @() }
  }
  if (@($resourceStates | Where-Object { $_ -eq 'OwnedDrifted' }).Count -gt 0) {
    return [ordered]@{ status = 'RepairNeeded'; primitives = @() }
  }
  if ($Request.operation -eq 'Apply' -and $Request.ledgerStatus -eq 'Absent' -and
      @($resourceStates | Where-Object { $_ -ne 'Missing' }).Count -eq 0) {
    return [ordered]@{
      status = 'Ready'
      primitives = @(
        'InitializeOwnershipJournal',
        'CreateGroup',
        'CreateAccount',
        'AddMembership',
        'AddFolderAce',
        'CreateShare',
        'CreateFirewallRule',
        'VerifyEffectiveAccess',
        'CommitOwnership')
    }
  }
  if ($Request.operation -eq 'Apply' -and $Request.ledgerStatus -eq 'Committed' -and
      @($resourceStates | Where-Object { $_ -ne 'OwnedConformant' }).Count -eq 0) {
    return [ordered]@{ status = 'NoChanges'; primitives = @('VerifyEffectiveAccess') }
  }
  if ($Request.operation -eq 'Apply' -and $Request.ledgerStatus -eq 'Applying') {
    $applied = @($Request.appliedPrimitives)
    $allowed = @('CreateGroup', 'CreateAccount', 'AddMembership', 'AddFolderAce', 'CreateShare', 'CreateFirewallRule')
    $resourceIndexes = @(0, 1, 1, 2, 3, 4)
    for ($index = 0; $index -lt $applied.Count; $index++) {
      if ($index -ge $allowed.Count -or $applied[$index] -ne $allowed[$index] -or
          $resourceStates[$resourceIndexes[$index]] -ne 'OwnedConformant') {
        return [ordered]@{ status = 'RepairNeeded'; primitives = @() }
      }
    }
    $nextResourceIndex = if ($applied.Count -eq 0) { 0 } else { $resourceIndexes[$applied.Count - 1] + 1 }
    if ($nextResourceIndex -le 4) {
      if (@($resourceStates[$nextResourceIndex..4] | Where-Object { $_ -ne 'Missing' }).Count -gt 0) {
        return [ordered]@{ status = 'RepairNeeded'; primitives = @() }
      }
    }
    $inverse = @{
      CreateGroup = 'RemoveGroup'
      CreateAccount = 'RemoveAccount'
      AddMembership = 'RemoveMembership'
      AddFolderAce = 'RemoveFolderAce'
      CreateShare = 'RemoveShare'
      CreateFirewallRule = 'RemoveFirewallRule'
    }
    [array]::Reverse($applied)
    $rollback = @($applied | ForEach-Object { $inverse[$_] }) + @('MarkCanceled')
    return [ordered]@{ status = 'Recovering'; primitives = $rollback }
  }
  if ($Request.operation -eq 'StopSharing' -and $Request.ledgerStatus -eq 'Committed' -and
      @($resourceStates | Where-Object { $_ -notin @('OwnedConformant', 'Missing') }).Count -eq 0) {
    $stopPrimitives = @()
    if ($resourceStates[1] -eq 'OwnedConformant') { $stopPrimitives += @('DisableAccount', 'RemoveMembership') }
    if ($resourceStates[3] -eq 'OwnedConformant') { $stopPrimitives += 'RemoveShare' }
    if ($resourceStates[4] -eq 'OwnedConformant') { $stopPrimitives += 'RemoveFirewallRule' }
    if ($resourceStates[2] -eq 'OwnedConformant') { $stopPrimitives += 'RemoveFolderAce' }
    if ($resourceStates[1] -eq 'OwnedConformant') { $stopPrimitives += 'RemoveAccount' }
    if ($resourceStates[0] -eq 'OwnedConformant') { $stopPrimitives += 'RemoveGroup' }
    $stopPrimitives += 'MarkHostRemoved'
    return [ordered]@{ status = 'Ready'; primitives = $stopPrimitives }
  }
  return [ordered]@{ status = 'Refused'; primitives = @() }
}

if ($PlanOnly) {
  Resolve-HostSetupPlan $payload | ConvertTo-Json -Compress
  exit 0
}

$shareName = 'Balls'
$groupName = 'Balls Server Access'
$marker = 'Balls Server managed object v1'
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
    schemaVersion = 1
    transactionId = $Ledger.transactionId
    status = $Ledger.status
    appliedPrimitives = @($Ledger.appliedPrimitives)
  } | ConvertTo-Json -Compress
  [IO.File]::AppendAllText($journalPath, $journalRecord + [Environment]::NewLine, (New-Object Text.UTF8Encoding($false)))
  Set-ProtectedLedgerAcl $stateDirectory $journalPath
}

function Read-ProtectedLedger {
  if (-not (Test-Path -LiteralPath $ledgerPath -PathType Leaf)) { return $null }
  $ledger = [IO.File]::ReadAllText($ledgerPath) | ConvertFrom-Json
  $required = @('schemaVersion', 'marker', 'transactionId', 'ownerSid', 'status', 'managedFolder', 'accessPath', 'endpoint',
    'shareName', 'groupName', 'groupSid', 'userName', 'userSid', 'firewallRuleName', 'interfaceAlias', 'appliedPrimitives')
  $actual = @($ledger.PSObject.Properties.Name | Sort-Object)
  if (@(Compare-Object ($required | Sort-Object) $actual).Count -ne 0 -or
      $ledger.schemaVersion -ne 1 -or $ledger.marker -ne $marker -or
      $ledger.ownerSid -ne [Security.Principal.WindowsIdentity]::GetCurrent().User.Value -or
      $ledger.shareName -ne $shareName -or $ledger.groupName -ne $groupName) {
    throw 'The protected ownership record is invalid.'
  }
  return $ledger
}

function Test-ExactMarker($Object) {
  return $null -ne $Object -and $Object.Description -eq $marker
}

function Undo-InterruptedSetup($Ledger) {
  $recoveryGroup = Get-LocalGroup -Name $Ledger.groupName -ErrorAction SilentlyContinue
  $recoveryUser = Get-LocalUser -Name $Ledger.userName -ErrorAction SilentlyContinue
  $recoveryShare = Get-SmbShare -Name $Ledger.shareName -ErrorAction SilentlyContinue
  $recoveryFirewall = Get-NetFirewallRule -Name $Ledger.firewallRuleName -ErrorAction SilentlyContinue
  if (($null -ne $recoveryGroup -and (-not (Test-ExactMarker $recoveryGroup) -or
       ($Ledger.groupSid -and $recoveryGroup.SID.Value -ne $Ledger.groupSid))) -or
      ($null -ne $recoveryUser -and (-not (Test-ExactMarker $recoveryUser) -or
       ($Ledger.userSid -and $recoveryUser.SID.Value -ne $Ledger.userSid))) -or
      ($null -ne $recoveryShare -and ($recoveryShare.Description -ne $marker -or
       [IO.Path]::GetFullPath($recoveryShare.Path) -ne $Ledger.managedFolder)) -or
      ($null -ne $recoveryFirewall -and $recoveryFirewall.Description -ne $marker)) {
    throw 'Interrupted setup contains changed or ambiguous ownership; repair is required.'
  }
  if ($null -ne $recoveryFirewall) { Remove-NetFirewallRule -Name $Ledger.firewallRuleName }
  if ($null -ne $recoveryShare) { Remove-SmbShare -Name $Ledger.shareName -Confirm:$false }
  if ($null -ne $recoveryGroup -and (Test-Path -LiteralPath $Ledger.managedFolder -PathType Container)) {
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
  if ($null -ne $recoveryUser) { Remove-LocalUser -Name $Ledger.userName }
  if ($null -ne $recoveryGroup) { Remove-LocalGroup -Name $Ledger.groupName }
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
  $expectedPayloadFields = @('AccessPath', 'ManagedFolder', 'Operation', 'Password', 'UserName')
  if (@(Compare-Object $expectedPayloadFields $payloadFields).Count -ne 0 -or
      $payload.Operation -notin @('Apply', 'StopSharing')) { throw 'Unsupported host operation.' }
  if ($payload.Operation -eq 'StopSharing') {
    if ([string]$payload.ManagedFolder -ne '' -or [string]$payload.UserName -ne '' -or
        [string]$payload.Password -ne '') { throw 'Stop Sharing accepts no caller-selected resource.' }
    $ledger = Read-ProtectedLedger
    if ($null -eq $ledger -or $ledger.status -eq 'Removed') {
      [ordered]@{ status = 'Stopped'; shareName = $shareName } | ConvertTo-Json -Compress
      exit 0
    }
    if ($ledger.status -notin @('Committed', 'Stopping')) { throw 'Protected ownership is incomplete; repair is required.' }
    $ownedGroup = Get-LocalGroup -Name $ledger.groupName -ErrorAction SilentlyContinue
    $ownedUser = Get-LocalUser -Name $ledger.userName -ErrorAction SilentlyContinue
    $ownedShare = Get-SmbShare -Name $ledger.shareName -ErrorAction SilentlyContinue
    $ownedFirewall = Get-NetFirewallRule -Name $ledger.firewallRuleName -ErrorAction SilentlyContinue
    if (($null -ne $ownedGroup -and (-not (Test-ExactMarker $ownedGroup) -or $ownedGroup.SID.Value -ne $ledger.groupSid)) -or
        ($null -ne $ownedUser -and (-not (Test-ExactMarker $ownedUser) -or $ownedUser.SID.Value -ne $ledger.userSid)) -or
        ($null -ne $ownedShare -and ($ownedShare.Description -ne $marker -or [IO.Path]::GetFullPath($ownedShare.Path) -ne $ledger.managedFolder)) -or
        ($null -ne $ownedFirewall -and $ownedFirewall.Description -ne $marker)) {
      throw 'A recorded object no longer matches protected ownership.'
    }
    $ledger.status = 'Stopping'
    Write-ProtectedLedger $ledger
    if ($null -ne $ownedUser) {
      Disable-LocalUser -Name $ledger.userName
      $ledger.appliedPrimitives += 'DisableAccount'
      Write-ProtectedLedger $ledger
    }
    if ($null -ne $ownedShare) {
      Remove-SmbShare -Name $ledger.shareName -Confirm:$false
      if (Get-SmbShare -Name $ledger.shareName -ErrorAction SilentlyContinue) { throw 'Share removal verification failed.' }
      $ledger.appliedPrimitives += 'RemoveShare'
      Write-ProtectedLedger $ledger
    }
    if ($null -ne $ownedFirewall) {
      Remove-NetFirewallRule -Name $ledger.firewallRuleName
      if (Get-NetFirewallRule -Name $ledger.firewallRuleName -ErrorAction SilentlyContinue) { throw 'Firewall removal verification failed.' }
      $ledger.appliedPrimitives += 'RemoveFirewallRule'
      Write-ProtectedLedger $ledger
    }
    if ($null -ne $ownedGroup) {
      $ownedAcl = Get-Acl -LiteralPath $ledger.managedFolder
      $ownedRules = @($ownedAcl.Access | Where-Object {
        $_.IdentityReference.Value -eq $ledger.groupSid -and
        $_.AccessControlType -eq [Security.AccessControl.AccessControlType]::Allow -and
        ($_.FileSystemRights -band [Security.AccessControl.FileSystemRights]::Modify) -ne 0
      })
      if ($ownedRules.Count -gt 1) { throw 'The recorded folder permission is ambiguous.' }
      if ($ownedRules.Count -eq 1) {
        $ownedAcl.RemoveAccessRuleSpecific($ownedRules[0])
        Set-Acl -LiteralPath $ledger.managedFolder -AclObject $ownedAcl
      }
      $ledger.appliedPrimitives += 'RemoveFolderAce'
      Write-ProtectedLedger $ledger
    }
    if ($null -ne $ownedUser) {
      Remove-LocalUser -Name $ledger.userName
      if (Get-LocalUser -Name $ledger.userName -ErrorAction SilentlyContinue) { throw 'Account removal verification failed.' }
      $ledger.appliedPrimitives += 'RemoveAccount'
      Write-ProtectedLedger $ledger
    }
    if ($null -ne $ownedGroup) {
      Remove-LocalGroup -Name $ledger.groupName
      if (Get-LocalGroup -Name $ledger.groupName -ErrorAction SilentlyContinue) { throw 'Group removal verification failed.' }
      $ledger.appliedPrimitives += 'RemoveGroup'
      Write-ProtectedLedger $ledger
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
  $volume = Get-Volume -DriveLetter ([IO.Path]::GetPathRoot($folder).Substring(0, 1))
  if ($volume.FileSystem -ne 'NTFS' -or $volume.DriveType -ne 'Fixed') { throw 'A fixed NTFS folder is required.' }
  $serverService = Get-Service -Name LanmanServer -ErrorAction Stop
  $smbConfiguration = Get-SmbServerConfiguration -ErrorAction Stop
  $minimumDialect = 0
  if (-not [int]::TryParse([string]$smbConfiguration.Smb2DialectMin, [ref]$minimumDialect)) {
    $minimumDialect = switch ([string]$smbConfiguration.Smb2DialectMin) {
      'SMB300' { 768 }
      'SMB302' { 770 }
      'SMB311' { 785 }
      default { 0 }
    }
  }
  if ($serverService.Status -ne 'Running' -or $smbConfiguration.EnableSMB1Protocol -ne $false -or
      $smbConfiguration.EnableSMB2Protocol -ne $true -or $minimumDialect -lt 768 -or
      $smbConfiguration.EnableSecuritySignature -ne $true) {
    throw 'SMB 3.0 and signing prerequisites are not in the required safe state.'
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
    if (-not (Test-ExactMarker $verifiedGroup) -or $verifiedGroup.SID.Value -ne $existingLedger.groupSid -or
        -not (Test-ExactMarker $verifiedUser) -or $verifiedUser.SID.Value -ne $existingLedger.userSid -or
        $null -eq $verifiedShare -or $verifiedShare.Description -ne $marker -or
        [IO.Path]::GetFullPath($verifiedShare.Path) -ne $folder -or
        $null -eq $verifiedFirewall -or $verifiedFirewall.Description -ne $marker) {
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
  $ledger = [ordered]@{
    schemaVersion = 1
    marker = $marker
    transactionId = [Guid]::NewGuid().ToString('N')
    ownerSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    status = 'Applying'
    managedFolder = $folder
    accessPath = [string]$payload.AccessPath
    endpoint = $endpoint
    shareName = $shareName
    groupName = $groupName
    groupSid = ''
    userName = [string]$payload.UserName
    userSid = ''
    firewallRuleName = $firewallName
    interfaceAlias = $adapter.Name
    appliedPrimitives = @()
  }
  Write-ProtectedLedger $ledger
  $createdLedger = $true
  $group = New-LocalGroup -Name $groupName -Description $marker
  $createdGroup = $true
  $ledger.groupSid = $group.SID.Value
  $ledger.appliedPrimitives += 'CreateGroup'
  Write-ProtectedLedger $ledger
  $securePassword = ConvertTo-SecureString ([string]$payload.Password) -AsPlainText -Force
  $user = New-LocalUser -Name $payload.UserName -Password $securePassword -Description $marker -AccountNeverExpires -PasswordNeverExpires -UserMayNotChangePassword
  $createdUser = $true
  $ledger.userSid = $user.SID.Value
  $ledger.appliedPrimitives += 'CreateAccount'
  Write-ProtectedLedger $ledger
  Add-LocalGroupMember -Group $groupName -Member $payload.UserName
  $ledger.appliedPrimitives += 'AddMembership'
  Write-ProtectedLedger $ledger
  $folderAcl = Get-Acl -LiteralPath $folder
  $folderRule = New-Object Security.AccessControl.FileSystemAccessRule(
    $group.SID,
    ([Security.AccessControl.FileSystemRights]::Modify -bor [Security.AccessControl.FileSystemRights]::Synchronize),
    ([Security.AccessControl.InheritanceFlags]::ContainerInherit -bor [Security.AccessControl.InheritanceFlags]::ObjectInherit),
    [Security.AccessControl.PropagationFlags]::None,
    [Security.AccessControl.AccessControlType]::Allow)
  $folderAcl.AddAccessRule($folderRule)
  Set-Acl -LiteralPath $folder -AclObject $folderAcl
  $addedAce = $true
  $ledger.appliedPrimitives += 'AddFolderAce'
  Write-ProtectedLedger $ledger
  $administrators = ([Security.Principal.SecurityIdentifier]'S-1-5-32-544').Translate([Security.Principal.NTAccount]).Value
  New-SmbShare -Name $shareName -Path $folder -Description $marker -ChangeAccess $groupName -FullAccess $administrators | Out-Null
  $createdShare = $true
  $ledger.appliedPrimitives += 'CreateShare'
  Write-ProtectedLedger $ledger
  New-NetFirewallRule -Name $firewallName -DisplayName "Balls Server SMB ($($payload.AccessPath))" -Group 'Balls Server' -Description $marker -Enabled True -Direction Inbound -Protocol TCP -LocalPort 445 -LocalAddress $addresses[0] -RemoteAddress $remoteScope -InterfaceAlias $adapter.Name -Profile Private,Domain -Action Allow | Out-Null
  $createdFirewall = $true
  $ledger.appliedPrimitives += 'CreateFirewallRule'
  Write-ProtectedLedger $ledger
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
  $ledger.appliedPrimitives += 'VerifyEffectiveAccess'
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
