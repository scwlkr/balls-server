# Access-grant secret lifecycle research

**Status:** research for v0.3.0 design only. This file authorizes no system
change. It applies the product boundary of authenticated SMB 3.0+ on a private
LAN or Tailscale, no guest access, and one limited identity for each client
Windows profile.

## Decision summary

Use one host-local, non-administrative Windows account per access grant and a
product-owned local security group for authorization. The privileged helper
creates, changes, disables, and removes those host objects; the unelevated
dashboard requests and observes those operations. On the client, the
unelevated Connect flow may save the credential in the current user's Windows
Credential Manager and create a reconnecting mapping only after separate,
explicit consent.

The password must be generated in the helper, sent once over a locally
authenticated IPC response, displayed once by the dashboard, and promptly
cleared from managed and native buffers. It must never enter a command line,
log, diagnostic export, ordinary configuration, product ledger, crash report,
or release artifact.

The product must **not** call a password-bearing bundle a time-expiring setup
code. A copied password remains usable until its Windows account is disabled,
expires, or its password changes. A genuinely expiring setup code needs a
separate, authenticated, one-time pairing protocol; that protocol is not yet
defined and must not be implied by the SMB setup flow.

## Evidence labels

- **Sourced fact** — directly supported by the Microsoft primary sources in
  [Source register](#source-register).
- **Recommendation** — a product design decision or implementation constraint
  derived for Balls Server. It is not represented as a Microsoft requirement.

## Sourced Windows facts relevant to the design

| Topic | Sourced fact |
| --- | --- |
| Local identity | `New-LocalUser` can create a local account with an account-expiry value, password-expiry control, and a `UserMayNotChangePassword` option. [Microsoft Learn](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.localaccounts/new-localuser?view=powershell-5.1) |
| Account creation privilege | `NetUserAdd` creates an account and assigns a password; on a member server or workstation, Administrators and Power Users may call it. It reports password-policy rejection, including `NERR_PasswordTooShort`. [Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/api/lmaccess/nf-lmaccess-netuseradd) |
| Group authorization | Local security-group membership gives each member the group's assigned rights and permissions; members of `Administrators` have full control, so membership must be limited. [Microsoft Learn](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.localaccounts/add-localgroupmember?view=powershell-5.1) |
| SMB network logon | `Access this computer from the network` is required for SMB-based protocols. A policy delivered by a higher-precedence GPO can replace the local setting. `Deny access to this computer from the network` overrides the allow right. [Allow right](https://learn.microsoft.com/en-us/previous-versions/windows/it-pro/windows-10/security/threat-protection/security-policy-settings/access-this-computer-from-the-network), [deny right](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-userrights) |
| Secret generation | `BCryptGenRandom` supplies random bytes; the default provider implements an SP 800-90 CTR_DRBG. Microsoft specifically recommends approved random generators rather than `System.Random`. [API](https://learn.microsoft.com/en-us/windows/win32/api/bcrypt/nf-bcrypt-bcryptgenrandom), [SDL guidance](https://learn.microsoft.com/en-us/azure/security/develop/threat-modeling-tool-cryptography) |
| Password handling | Microsoft recommends Credential Manager for persisted credentials and says its vault encrypts them with the user's logon-session key. It also says to collect secrets late, discard them early, and use `SecureZeroMemory` after use. [Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/secbp/handling-passwords) |
| Credential scope | A `CRED_PERSIST_LOCAL_MACHINE` credential persists across later logons for the same user on the same computer. For `CRED_TYPE_DOMAIN_PASSWORD`, `TargetName` identifies the server or servers and `UserName` is a qualified `DomainName\\UserName` or UPN; a user can have only one stored credential per target. That server target is a separate provider input from the share-qualified mapping UNC and must be proven rather than inferred. [CREDENTIAL](https://learn.microsoft.com/en-us/windows/win32/api/wincred/ns-wincred-credentialw), [CredUI target behavior](https://learn.microsoft.com/en-us/windows/win32/api/wincred/nf-wincred-creduipromptforcredentialsw) |
| Local account authority | Each Windows computer is the security authority for accounts in its local SAM. `LookupAccountName` returns the referenced domain name, which is the computer name for a non-domain-joined computer, and recommends qualified `domain_name\\user_name` input to avoid ambiguity. The host must therefore publish its observed SAM authority for the grant account independently of any LAN or MagicDNS access alias. [Local accounts](https://learn.microsoft.com/en-us/windows/security/identity-protection/access-control/local-accounts), [LookupAccountName](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-lookupaccountnamew) |
| Credential deletion | `CredDelete` removes a credential from the credential set associated with the caller's current-token logon session; `ERROR_NOT_FOUND` is a defined outcome. [Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/api/wincred/nf-wincred-creddeletew) |
| Reconnecting mapping | `WNetAddConnection2` can map a local drive to a network resource. `CONNECT_UPDATE_PROFILE` makes Windows restore that mapping at future logons. [Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/api/winnetwk/nf-winnetwk-wnetaddconnection2w) |
| Mapping removal | `WNetCancelConnection2` with `CONNECT_UPDATE_PROFILE` removes the remembered mapping; with `fForce = FALSE` it fails rather than disconnecting open files or jobs. [Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/api/winnetwk/nf-winnetwk-wnetcancelconnection2w) |
| Credential collision | Windows deliberately disallows multiple connections to a server/shared resource by the same user under more than one user name. [Microsoft Learn](https://learn.microsoft.com/en-us/troubleshoot/windows-server/networking/cannot-connect-to-network-share) |
| Lockout behavior | The account-lockout threshold determines the failed attempts that lock an account; a locked account needs reset or the configured duration to expire. Microsoft warns that retrying applications and protocol negotiation can count toward the threshold, and that lockout is also a denial-of-service vector. [Microsoft Learn](https://learn.microsoft.com/en-us/previous-versions/windows/it-pro/windows-10/security/threat-protection/security-policy-settings/account-lockout-threshold) |
| Auditing | Windows can audit account creation/change/delete/password/lockout events. File-share auditing covers every shared folder and may be high volume; detailed file-share events occur for each network share object access. [Account management](https://learn.microsoft.com/en-us/previous-versions/windows/it-pro/windows-10/security/threat-protection/auditing/basic-audit-account-management), [file share policy](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-audit), [event 5145](https://learn.microsoft.com/en-us/previous-versions/windows/it-pro/windows-10/security/threat-protection/auditing/event-5145) |
| IPC default risk | Named pipes accept a security descriptor. The default descriptor grants broad access, including read access to Everyone and anonymous, so a secret-bearing helper pipe needs an explicit restrictive DACL. [Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/ipc/named-pipe-security-and-access-rights) |

## Recommended host model

### Identity, group, and permissions

1. **Recommendation — identity:** create one local account with a product
   generated opaque name, for example `BSG-<short-grant-id>`. Its friendly
   label is kept only in the owner-visible ledger, not in the Windows account
   name, description, share name, or diagnostics. Do not reuse a name or a
   password between grants.
2. **Recommendation — privilege:** create it as an ordinary local user; do not
   add it to `Administrators`, Remote Desktop Users, backup-operator groups,
   or any user-selected pre-existing group. Its only intended capability is
   SMB network logon and access to the managed share.
3. **Recommendation — product group:** create one clearly named,
   product-owned local security group for managed-share read/write access and
   grant that group the product-owned share ACL and managed-folder NTFS ACL.
   Add each active access account to that group. Record group and account SIDs,
   not only names. This gives a single grant revocation operation (remove the
   member and disable the account) while preserving the required separate
   identity per profile.
4. **Recommendation — ACL intersection:** both the SMB-share ACL and NTFS ACL
   must name only the product group (and necessary owner/system principals).
   Verify the effective objects and refuse an unmanaged-share or unmanaged-ACL
   conflict; never widen an existing share. `Grant-SmbShareAccess` adds an
   allow ACE to an SMB-share security descriptor, but this is future helper
   work, not a dashboard action. [Microsoft Learn](https://learn.microsoft.com/en-us/powershell/module/smbshare/grant-smbshareaccess?view=windowsserver2025-ps)
5. **Recommendation — user rights:** do not edit machine-wide user-right
   assignments merely to make a grant work. Preflight the effective
   `SeNetworkLogonRight` and `SeDenyNetworkLogonRight`; report a GPO-owned
   denial as **Action required** with manual owner/administrator recovery.
   Changing those rights affects more than Balls Server and deny overrides
   allow.
6. **Recommendation — account flags:** set `UserMayNotChangePassword` so the
   product can rotate and revoke predictably. Do not set
   `PasswordNeverExpires` as a workaround for local policy; discover effective
   password and expiry policy, and schedule a deliberate rotate-before-expiry
   flow if it applies. The account itself does not need a routine expiry:
   active access ends through explicit revocation. A *pending* pairing is not
   represented by a live, unclaimed account (see setup-code decision below).
7. **Recommendation — password:** generate at least 32 random bytes with the
   platform cryptographic RNG, encode with a compatibility-tested alphabet,
   and attempt account creation once. If Windows rejects it for policy, discard
   it, record only the non-secret policy/error category, and generate a fresh
   value. Do not repeatedly submit the same rejected password or expose it in
   an error dialog.

### Product-owned ledger (no secret)

**Recommendation:** persist the minimum operational record below, protected
as local application data and visible only to the current owner. This is an
ownership/recovery record, not a credential store.

| Field | Purpose | Prohibited content |
| --- | --- | --- |
| Grant ID and state | Opaque immutable ID; `Active`, `Rotating`, `Revoked`, `Repair needed`, or `Unknown` | Password, setup code, QR payload |
| Host objects | Account SID/name, group SID/name, creation and last-verified timestamps | Owner password or profile PII |
| Authorization | Managed-share identity and hashes/revisions of the product-owned share/NTFS ACEs | File names, file paths beyond the selected managed-folder reference |
| Client intent | User-approved mapping letter, full mapping UNC, separately proven provider credential-target name, qualified host-local account identity, share name, and whether persistence was consented | Client Windows password or credential blob |
| Lifecycle | Rotation/revocation version, reason category, audit correlation ID, manual-recovery status | Raw exception payloads containing account credentials |

The ledger must have an explicit schema version, atomic update/recovery marker,
and a read-only export that redacts account names by default. It must not use a
password-derived identifier; an account SID plus product grant ID is enough for
reconciliation.

## Secret and setup-code lifecycle

### Chosen design for this milestone: display-once credential transfer

**Recommendation:** model the first client handoff as a *display-once
credential transfer*, not an independently redeemable pairing service:

1. The owner consents after a preview naming the client profile label,
   limited read/write grant, managed share, whether the credential will be
   stored on the client, and recovery/revocation consequences.
2. The host helper generates the password and creates the account, group
   membership, and approved ACLs. It verifies all objects before returning
   success. Account creation must use a direct API/helper request, never a
   command line containing a password.
3. The helper sends the dashboard exactly one response containing the
   username and password. Use a one-request, one-response local named pipe
   with an explicit DACL for the initiating user's SID and the helper service
   identity; bind a random request nonce to the user-approved request; reject
   a second reader, wrong SID, stale nonce, or repeated response. The helper
   logs only operation/grant IDs and result codes.
4. The dashboard renders the secret only in a dedicated transient view. It
   never writes the value to the clipboard automatically, activity history,
   telemetry, accessibility diagnostic, crash dump attachment, or normal log.
   Manual copy and QR display, if approved later, need a clear warning that
   either is another secret disclosure channel.
5. The owner transfers the values directly to the intended client profile.
   The client flow uses them immediately to validate SMB and, only after a
   separate consent, saves them in Credential Manager and creates the mapping.
6. The dashboard closes or times out its display (recommended: 60 seconds),
   clears its view model, and requests native/managed buffer clearing where
   possible. This is a display-window expiry, **not** an access expiry.

**Recommendation — setup-transfer contents:** if product language requires a
"setup code," label the displayed data honestly as a **credential transfer
bundle**. It contains only the minimum the client needs: schema version,
product host and grant IDs, credential revision, exactly one selected endpoint,
managed-share name, the helper-observed local SAM authority and account name as
one qualified host-local identity, password, and generated-at timestamp. The
SAM authority is independent of the LAN or MagicDNS endpoint alias. Do not
include an alternate endpoint, folder listing, owner identity, client personal
data, diagnostics, account SID, or Tailscale credential. The password makes the
bundle a bearer secret; do not persist it or promise it expires merely because
the UI did.

**Recommendation — later endpoint update:** transfer a changed endpoint in a
separate minimal, non-secret endpoint-update bundle. It contains schema version,
product host ID, grant ID, current credential revision, share name, exactly one
new selected endpoint kind/value, and generated-at timestamp. The client accepts
it only when the host/grant/revision exactly match its existing record, then
requires explicit Import, preview, and re-verification. It contains no password
or alternate endpoint, performs no discovery or guessing, is not a remote
pairing service, and never switches a saved credential or mapping automatically.
If no usable existing credential is available through the proven provider flow,
the owner reissues the credential transfer instead.

### What a genuinely expiring setup code requires

**Recommendation:** if a future release needs a code whose redemption truly
expires, it needs a separately approved pairing protocol, not just SMB:

- Generate an opaque 256-bit random one-time token. Persist only a keyed
  verifier, grant ID, issue time, attempt count, and short expiry (recommended
  10 minutes); never persist the plaintext token or SMB password.
- Bind it to a confirmed host identity and an authenticated, encrypted local
  transport. The protocol must perform mutual endpoint validation, one-time
  consumption, timeout, replay handling, rate limits, and precise recovery.
- Issue the SMB password only inside that secured exchange, then mark the
  token consumed atomically before returning success. Account creation occurs
  only after the pairing is accepted, avoiding a live unclaimed account.
- This is a new remote protocol and attack surface. It cannot be silently
  folded into SMB setup or the privileged helper; it needs a threat model,
  protocol specification, disposable-VM tests, and explicit milestone
  approval.

Until that exists, the product should use owner-assisted display-once transfer
and explain that revocation/rotation, rather than the screen timer, ends the
credential's validity.

## Client Credential Manager and drive mapping

1. **Recommendation — consent and identity:** the Connect dashboard, running
   as the signed-in client user, offers two separate boxes: “Save this
   credential in Windows Credential Manager” and “Reconnect this drive when I
   sign in.” Neither is preselected. The privileged helper is not involved.
2. **Recommendation — target canonicalization:** distinguish three exact
   values before storing anything: the selected endpoint name, the full
   share-qualified mapping UNC, and the server credential target supported by
   the Windows provider. Prove the provider target in a disposable VM, display
   it in the preview, record it separately from the mapping UNC, and never use
   a wildcard or assume the full UNC is the credential target. LAN and
   Tailscale names remain different targets. Removal deletes only each exact
   product-created provider target. This must be prototyped because target
   selection and credential-provider behavior are Windows-provider dependent.
3. **Recommendation — storage:** use the Windows credential APIs in the
   current user's context, with local-machine persistence only after consent.
   Keep its exact provider-supported target and qualified host-local account
   identity in the non-secret ledger so the target can be deleted; never retain
   or re-read the credential blob merely to display it. The qualified account
   uses the helper-observed host SAM authority and must not be derived from the
   LAN name, MagicDNS alias, mapping UNC, or credential target. Treat
   `ERROR_NOT_FOUND` during removal as an idempotent completed state.
4. **Recommendation — mapping:** create only the selected unused drive letter
   and exact canonical UNC path. Use a non-interactive, bounded connection
   attempt and `CONNECT_UPDATE_PROFILE` only when reconnect consent was given.
   Call in-process Windows APIs; never pass a password through a shell or child
   process argument. A prototype must verify whether
   the selected provider consumes the saved target credential when the API
   password argument is null; if it does not, use the Windows credential UI or
   another supported in-process route without logging the secret.
5. **Recommendation — collision recovery:** before connecting, enumerate the
   chosen letter and existing connections to the server. On an existing
   different-credential connection, show the affected connection and offer
   explicit choices: cancel, disconnect only that named product-owned mapping
   after warning about open files, or have the owner resolve the foreign
   connection. Never delete all network connections as a repair shortcut.
6. **Recommendation — removal:** first cancel the mapped letter with
   `CONNECT_UPDATE_PROFILE` and `fForce = FALSE`; if it has open files, stop
   and provide the exact manual close-and-retry instruction. Then delete only
   the recorded Credential Manager targets in the current client user's
   context. A host-side revoke cannot delete a remote client's local stored
   credential, so its client cleanup remains a best-effort local action.

## Retry, rotation, revocation, and recovery

### Authentication and lockout safety

- **Recommendation:** never automatically retry an invalid credential. On a
  user-entered correction, allow one explicit attempt, then stop and report a
  neutral `Action required` result. Do not distinguish a nonexistent username
  from a bad password in UI or logs.
- **Recommendation:** before the first attempt and after a failure, query or
  observe the host lockout state when the allowed design can do so. Show only
  “the access account is locked” and a wait/unlock recovery choice; do not
  encourage repeated password entry.
- **Recommendation:** map Windows failure categories to typed outcomes:
  connection/path unavailable → `Warning` or `Action required`; access denied
  or invalid credential → `Action required`; lockout → `Action required`;
  policy/observation error → `Unknown`. Preserve a native numeric error only
  in local redacted technical detail.
- **Recommendation:** no background reconnect loop while a stored credential
  is known bad. The OS may attempt an approved reconnect at sign-in; Balls
  Server itself must not add retries around it.

### Rotation

**Recommendation:** rotation is a transaction, not a background password
change:

1. Owner selects one active grant and consents to invalidate the old secret.
2. Helper changes the account password, verifies account/group/share/NTFS
   ownership remains intact, and increments the grant credential revision in
   the ledger. It never logs either secret.
3. The old client mapping is expected to fail until the new display-once
   transfer is installed. The client removes its prior product-owned mapping
   and Credential Manager entry, then saves/maps the new credential only with
   consent.
4. If transfer fails after the password change, report `Repair needed`: the
   grant is still valid with the new password, but client access is not yet
   configured. The owner may retry a fresh transfer or revoke. Do not attempt
   to recover an erased old password.

Set a product rotation reminder based on observed Windows password policy and
owner choice. The host may rotate early for suspected disclosure; it must not
silently rotate an actively used credential.

### Revocation

**Recommendation:** revocation should take effect host-side in this order:

1. Confirm the specific grant identity, client profile label, affected share,
   and consequence that mapped clients lose new access.
2. Remove the account from the product group, disable the local account, and
   verify both state changes. The ACL stays group-based; do not remove shared
   group ACEs while other grants are active.
3. End only sessions attributable to that grant if the future helper can do so
   safely and explicitly; otherwise say that already-open files/connections
   may need normal Windows session closure and record the incomplete state.
4. Retain a tombstone with opaque grant ID, account SID, time, actor category,
   and completion status. Delete the account only after successful verification
   and according to an owner-approved retention rule. Never delete the managed
   folder, its contents, or a user-owned client mapping.
5. Tell the owner that client-side Credential Manager deletion requires access
   to that client Windows profile. The host-side disable protects the share
   even if the client still retains an unusable old secret.

### Exact manual recovery boundaries

| Condition | Recommended recovery | Do not do |
| --- | --- | --- |
| Group policy denies SMB network logon | Show the effective policy/right and ask the PC/domain administrator to adjust it outside Balls Server; rerun preflight. | Overwrite GPO or broad local user-right assignments. |
| Password rejected by local policy | Discard it, surface a redacted policy failure, and create a new random password only after the owner retries. | Relax password policy or reuse/display the rejected value. |
| Existing different-server credential connection | Disconnect the named conflicting connection only after an open-file warning, or have the owner resolve it manually. | Disconnect all connections or substitute an IP/alias to evade the collision. |
| Drive in use/open files | Do not force disconnect; owner closes files, disconnects the recorded drive, then retries cleanup. | Force close user work. |
| Client lost credential | Owner rotates/reissues or revokes the specific grant. | Recover the old password from product data. |
| Grant account locked | Wait for policy duration or use a clearly authorized local administrator recovery; inspect stale client credentials before retrying. | Repeated automatic authentication attempts. |
| Partial host creation | Reconcile only recorded product-owned SIDs/ACLs; either finish verified setup or remove those objects after consent. | Touch unmanaged accounts, shares, ACLs, firewall policy, folder data, or user files. |

## Audit, privacy, and helper boundary

### Auditable records

**Recommendation:** create a local, append-oriented product audit event for
each request, preview acceptance, helper authorization, account/group/ACL
mutation, credential-display acknowledgement, mapping action, test result,
rotation, revoke, cleanup, and recovery. Retain: timestamp, opaque operation
and grant IDs, initiator SID/category, before/after non-secret state,
correlation ID, result category, and native error code. Do not retain
passwords, setup tokens, credential blobs, QR images, full UNC paths in
exports, file names, IP addresses unless the owner explicitly previews them,
or raw exception text without redaction.

**Recommendation:** use Windows Security auditing only as an optional
corroborating system record, not as a reason to enable broad file-access
auditing. Account-management auditing gives useful creation/password/disable/
delete/lockout evidence. File-share and detailed-file-share auditing may cover
all shares and be high volume, so it needs a separate preview, retention,
privacy, and test decision.

### Dashboard and privileged-helper split

| Unelevated dashboard | Narrow privileged helper |
| --- | --- |
| Collects consent; renders preview/status; initiates a bounded request; displays the one-time secret; manages the current client's Credential Manager/mapping; never performs host mutation. | Validates protocol version, caller SID, nonce, requested grant scope, and ownership; generates secret; creates/changes/disables/removes recorded host account/group/share/NTFS objects; returns redacted status plus the one-time secret only when requested. |
| Has no standing elevation, no direct account/ACL/firewall/group write capability, and no general administrative RPC surface. | Has no UI, no arbitrary command/script field, no arbitrary path/ACL/principal mutation, no client Credential Manager access, and no Tailscale credential handling. |

**Recommendation:** use a fixed allow-list message schema rather than command
strings or PowerShell fragments. Every helper request contains opaque IDs and
validated enum operations; it excludes password, arbitrary UNC path, policy
script, and token fields. For creation, the helper generates the password
internally. For rotation, it does the same. A helper process must never emit
the secret on stdout/stderr, Windows Event Log, or an audit payload. The pipe
DACL must be explicit; the Windows default is unsuitable for secret transport.

## Required prototype and test evidence before implementation

1. Disposable workgroup Windows 11 Pro VMs: host plus two client profiles;
   prove one account per profile, non-admin membership, SMB 3.0+, SMB1 off,
   and that share/NTFS intersection admits only the active grant.
2. Helper IPC tests: wrong SID, replayed nonce, second pipe reader, stale
   request, helper crash before/after account creation, dashboard crash before
   display, and no secret in captured stdout/stderr/Event Log/product logs.
3. Credential tests in the same and a different client profile: no persistence
   without consent; local-only persistence with consent; the exact provider
   target proven and recorded separately from the mapping UNC; helper-observed
   SAM authority accepted independently from the endpoint alias; exact-target
   deletion; alternate local/Tailscale target behavior; sign-in reconnect; no
   password-bearing shell/process arguments; and clean removal of mapping plus
   credential.
4. Connection tests: known-bad credential, password-policy rejection, account
   lockout, unavailable path, name fallback, and pre-existing different
   credential to the same server. Confirm that the product has no retry loop
   and never tears down unrelated mappings.
5. Lifecycle tests: rotate after successful use, revoke with an open client
   file, helper interruption at every mutation point, ledger corruption,
   reboot/reconnect, owner removal, and manual recovery. Verify no path deletes
   the managed folder or user files.

## Source register

All external sources below are Microsoft primary documentation.

1. [New-LocalUser](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.localaccounts/new-localuser?view=powershell-5.1)
2. [NetUserAdd](https://learn.microsoft.com/en-us/windows/win32/api/lmaccess/nf-lmaccess-netuseradd)
3. [Add-LocalGroupMember](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.localaccounts/add-localgroupmember?view=powershell-5.1)
4. [Access this computer from the network](https://learn.microsoft.com/en-us/previous-versions/windows/it-pro/windows-10/security/threat-protection/security-policy-settings/access-this-computer-from-the-network)
5. [BCryptGenRandom](https://learn.microsoft.com/en-us/windows/win32/api/bcrypt/nf-bcrypt-bcryptgenrandom)
6. [Handling passwords](https://learn.microsoft.com/en-us/windows/win32/secbp/handling-passwords)
7. [CREDENTIAL structure](https://learn.microsoft.com/en-us/windows/win32/api/wincred/ns-wincred-credentialw)
8. [CredDelete](https://learn.microsoft.com/en-us/windows/win32/api/wincred/nf-wincred-creddeletew)
9. [WNetAddConnection2](https://learn.microsoft.com/en-us/windows/win32/api/winnetwk/nf-winnetwk-wnetaddconnection2w)
10. [WNetCancelConnection2](https://learn.microsoft.com/en-us/windows/win32/api/winnetwk/nf-winnetwk-wnetcancelconnection2w)
11. [Windows multiple-credential connection behavior](https://learn.microsoft.com/en-us/troubleshoot/windows-server/networking/cannot-connect-to-network-share)
12. [Account lockout threshold](https://learn.microsoft.com/en-us/previous-versions/windows/it-pro/windows-10/security/threat-protection/security-policy-settings/account-lockout-threshold)
13. [Named-pipe security and access rights](https://learn.microsoft.com/en-us/windows/win32/ipc/named-pipe-security-and-access-rights)
14. [Audit policy CSP: file share](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-audit)
15. [Audit account management](https://learn.microsoft.com/en-us/previous-versions/windows/it-pro/windows-10/security/threat-protection/auditing/basic-audit-account-management)
16. [Local accounts](https://learn.microsoft.com/en-us/windows/security/identity-protection/access-control/local-accounts)
17. [LookupAccountName](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-lookupaccountnamew)
