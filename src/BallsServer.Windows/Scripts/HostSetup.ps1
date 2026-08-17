$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$payload = [Console]::In.ReadToEnd() | ConvertFrom-Json
$shareName = 'Balls'
$groupName = 'Balls Server Access'
$marker = 'Balls Server managed object v1'
$firewallName = if ($payload.AccessPath -eq 'Tailscale') { 'BallsServer-SMB-Tailscale-v1' } else { 'BallsServer-SMB-Local-v1' }
$stateDirectory = Join-Path ([Environment]::GetFolderPath('CommonApplicationData')) 'Balls Server'
$ledgerPath = Join-Path $stateDirectory 'host-state.json'
$temporaryLedgerPath = Join-Path $stateDirectory 'host-state.pending.json'
$createdGroup = $false
$createdUser = $false
$addedAce = $false
$createdShare = $false
$createdFirewall = $false
$createdLedger = $false
$folderRule = $null

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
  $inheritance = [Security.AccessControl.InheritanceFlags]'ContainerInherit, ObjectInherit'
  $allow = [Security.AccessControl.AccessControlType]::Allow
  $directorySecurity = New-Object Security.AccessControl.DirectorySecurity
  $directorySecurity.SetAccessRuleProtection($true, $false)
  $directorySecurity.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($administrators, 'FullControl', $inheritance, 'None', $allow)))
  $directorySecurity.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($system, 'FullControl', $inheritance, 'None', $allow)))
  (Get-Item -LiteralPath $DirectoryPath).SetAccessControl($directorySecurity)
  $fileSecurity = New-Object Security.AccessControl.FileSecurity
  $fileSecurity.SetAccessRuleProtection($true, $false)
  $fileSecurity.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($administrators, 'FullControl', $allow)))
  $fileSecurity.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($system, 'FullControl', $allow)))
  (Get-Item -LiteralPath $FilePath).SetAccessControl($fileSecurity)
}

try {
  $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
  if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Elevation is required.' }
  if ($payload.AccessPath -notin @('Local', 'Tailscale')) { throw 'Unsupported access path.' }
  if ($payload.UserName -notmatch '^BallsClient-[A-Z0-9]{6}$') { throw 'Invalid limited account name.' }
  if ([string]::IsNullOrWhiteSpace($payload.Password) -or $payload.Password.Length -lt 20) { throw 'Invalid limited credential.' }
  $folder = [IO.Path]::GetFullPath([string]$payload.ManagedFolder)
  if (-not [IO.Directory]::Exists($folder)) { throw 'Managed folder is unavailable.' }
  $root = [IO.Path]::GetPathRoot($folder).TrimEnd('\')
  if ($folder.TrimEnd('\') -eq $root) { throw 'A drive root cannot be shared.' }
  if ((Get-Item -LiteralPath $folder -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'A reparse-point root cannot be shared.' }
  $volume = Get-Volume -DriveLetter ([IO.Path]::GetPathRoot($folder).Substring(0, 1))
  if ($volume.FileSystem -ne 'NTFS' -or $volume.DriveType -ne 'Fixed') { throw 'A fixed NTFS folder is required.' }
  if (Test-Path -LiteralPath $ledgerPath) { throw 'Balls Server hosting is already configured.' }
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
  $endpoint = if ($payload.AccessPath -eq 'Tailscale') { $addresses[0] } else { $env:COMPUTERNAME }
  $group = New-LocalGroup -Name $groupName -Description $marker
  $createdGroup = $true
  $securePassword = ConvertTo-SecureString ([string]$payload.Password) -AsPlainText -Force
  $user = New-LocalUser -Name $payload.UserName -Password $securePassword -Description $marker -AccountNeverExpires -PasswordNeverExpires -UserMayNotChangePassword
  $createdUser = $true
  Add-LocalGroupMember -Group $groupName -Member $payload.UserName
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
  $administrators = ([Security.Principal.SecurityIdentifier]'S-1-5-32-544').Translate([Security.Principal.NTAccount]).Value
  New-SmbShare -Name $shareName -Path $folder -Description $marker -ChangeAccess $groupName -FullAccess $administrators | Out-Null
  $createdShare = $true
  New-NetFirewallRule -Name $firewallName -DisplayName "Balls Server SMB ($($payload.AccessPath))" -Group 'Balls Server' -Description $marker -Enabled True -Direction Inbound -Protocol TCP -LocalPort 445 -LocalAddress $addresses[0] -RemoteAddress $remoteScope -InterfaceAlias $adapter.Name -Profile Private,Domain -Action Allow | Out-Null
  $createdFirewall = $true
  $verifiedShare = Get-SmbShare -Name $shareName -ErrorAction Stop
  if ([IO.Path]::GetFullPath($verifiedShare.Path) -ne $folder) { throw 'Share verification failed.' }
  $verifiedRule = Get-NetFirewallRule -Name $firewallName -ErrorAction Stop
  if (-not $verifiedRule.Enabled -or $verifiedRule.Direction -ne 'Inbound' -or $verifiedRule.Action -ne 'Allow') { throw 'Firewall verification failed.' }
  New-Item -ItemType Directory -Path $stateDirectory -Force | Out-Null
  $ledger = [ordered]@{
    schemaVersion = 1; marker = $marker; managedFolder = $folder; accessPath = [string]$payload.AccessPath
    endpoint = $endpoint; shareName = $shareName; groupName = $groupName; groupSid = $group.SID.Value
    userName = [string]$payload.UserName; userSid = $user.SID.Value; firewallRuleName = $firewallName
    interfaceAlias = $adapter.Name
  }
  [IO.File]::WriteAllText($temporaryLedgerPath, ($ledger | ConvertTo-Json -Compress), (New-Object Text.UTF8Encoding($false)))
  Move-Item -LiteralPath $temporaryLedgerPath -Destination $ledgerPath -Force
  Set-ProtectedLedgerAcl $stateDirectory $ledgerPath
  $createdLedger = $true
  [ordered]@{ hostName = $endpoint; shareName = $shareName; userName = [string]$payload.UserName } | ConvertTo-Json -Compress
} catch {
  if ($createdLedger -and (Test-Path -LiteralPath $ledgerPath)) { Remove-Item -LiteralPath $ledgerPath -Force -ErrorAction SilentlyContinue }
  if (Test-Path -LiteralPath $temporaryLedgerPath) { Remove-Item -LiteralPath $temporaryLedgerPath -Force -ErrorAction SilentlyContinue }
  if ($createdFirewall) { Remove-NetFirewallRule -Name $firewallName -ErrorAction SilentlyContinue }
  if ($createdShare) { Remove-SmbShare -Name $shareName -Force -Confirm:$false -ErrorAction SilentlyContinue }
  if ($addedAce -and $null -ne $folderRule) {
    $rollbackAcl = Get-Acl -LiteralPath $folder -ErrorAction SilentlyContinue
    if ($null -ne $rollbackAcl) {
      $rollbackAcl.RemoveAccessRuleSpecific($folderRule)
      Set-Acl -LiteralPath $folder -AclObject $rollbackAcl -ErrorAction SilentlyContinue
    }
  }
  if ($createdUser) { Remove-LocalUser -Name $payload.UserName -ErrorAction SilentlyContinue }
  if ($createdGroup) { Remove-LocalGroup -Name $groupName -ErrorAction SilentlyContinue }
  exit 1
}
