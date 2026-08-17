# Windows threat model and privileged-operation inventory

**Status:** v0.3.0 design research only. This document authorizes no product
implementation or Windows mutation. It assumes authenticated SMB 3.0+ on a
trusted private LAN or private Tailscale path, SMB1 disabled, signing
preserved, and one limited host-local identity per client Windows profile.

## Decision summary

1. Keep all observation, planning, and ordinary client-user actions in the
   unelevated dashboard. Give a privileged helper only fixed, typed Host
   operations against objects proven product-owned.
2. The dashboard's approval screen is necessary UX evidence but is not a
   sufficient security authorization. Before every privileged transaction, a
   helper-owned confirmation path must show the normalized effects and bind
   the initiating SID, operation ID, nonce, plan digest, and expiry to the
   helper request. A compromised dashboard must not be able to assert that the
   owner approved arbitrary administrative work.
3. Never automate machine-wide SMB protocol/signing settings, built-in
   firewall-rule groups, user-right assignments, resolver policy, computer or
   Tailscale naming, or takeover of unmanaged shares and ACLs. Report the
   conflict and give exact administrator recovery guidance.
4. Mutate only ledger-recorded objects in a product namespace. Roll back a
   step only when its current identity and state still match the helper's
   recorded postcondition; otherwise stop rather than overwriting later human
   or policy changes.
5. Reject UNC/device paths, alternate data streams, volume roots, and any
   managed-folder root or ancestor containing a reparse point. Descendant
   reparse points remain a release blocker until a disposable-VM prototype
   proves a containment and ongoing-drift policy.

## Evidence labels

- **Sourced fact** means the statement is directly supported by the linked
  Microsoft primary documentation.
- **Recommendation** means a Balls Server design decision or security
  inference. It is not represented as a Windows requirement.
- **Open blocker** means mutating implementation must not begin for that area
  until the stated design or prototype evidence exists.

## System, assets, and trust boundaries

Security assets are the managed folder and user files; host and client
credentials; product-owned account, group, share, ACL, firewall, mapping, and
Credential Manager objects; the ownership ledger; consent and audit evidence;
and the integrity of the fixed helper protocol.

| Boundary | Less-trusted side | More-privileged or sensitive side | Required control |
| --- | --- | --- | --- |
| Person -> dashboard | Local interactive input and imported transfer bundle | Planned operation and transient secret view | Exact preview, explicit choices, no implicit persistence, bounded fields |
| Dashboard -> helper IPC | Mutable unelevated process | Administrative Windows operations and one-time secret response | Local-only endpoint, explicit DACL, caller-token validation, nonce, plan digest, expiry, replay rejection |
| Helper -> Windows/ledger | Parsed request and observed state | SAM/local groups, share/NTFS ACLs, firewall, filesystem and durable ownership | Fixed API allow-list, product namespace, handle-based validation, write-ahead step journal |
| Host -> SMB client | Network and bearer credential | Managed read/write data | Exact path-specific endpoint, limited account, share/NTFS intersection, SMB 3.0+, no guest/public 445 |
| Helper -> dashboard secret response | Privileged generated credential | User-session memory and display surfaces | One response/reader, explicit pipe DACL, short display, no logs/clipboard automation/persistence |
| Connect flow -> Credential Manager/mapping | Imported credential and endpoint | Current Windows user's durable state | Separate consents, provider-supported server credential target recorded separately from the mapping UNC, qualified host-local SAM identity, current-user APIs, product-owned target/letter only |
| Local/Tailscale path | Name resolution and transport | SMB authentication and authorization | Report independently; never infer SMB success from name lookup or TCP 445; never auto-fallback |

Named-pipe security descriptors control both pipe ends. Microsoft's default
descriptor grants read access to Everyone and anonymous, so it is unsuitable
for this secret-bearing boundary. Named pipes can also be remotely accessible
when the Server service is running; a local-only pipe must deny
`NT AUTHORITY\NETWORK`. [Named-pipe security](https://learn.microsoft.com/en-us/windows/win32/ipc/named-pipe-security-and-access-rights),
[named pipes](https://learn.microsoft.com/en-us/windows/win32/ipc/named-pipes)

## STRIDE analysis

| Category | Representative threat | Required mitigation and refusal behavior |
| --- | --- | --- |
| Spoofing | Another local process opens the helper pipe, claims the dashboard SID, substitutes an operation ID, or reads the credential response. | DACL to the initiating user SID plus helper identity; deny network; impersonate the connected client and derive its SID/session from the token; do not trust a SID field; one instance/reader; random nonce and short expiry. Abort on any identity/impersonation failure. |
| Spoofing | A reused host name, MagicDNS name, share name, or friendly client label is treated as the prior object. | Bind ownership to opaque host/grant IDs and Windows SIDs/object identities. Re-observe exact path-specific endpoints. Name equality never transfers ownership. |
| Tampering | Dashboard or malware changes path, principals, ACL, firewall scope, or operation after approval. | Helper reconstructs a canonical plan from allow-listed IDs/enums, independently validates current state, and requires a helper-owned confirmation over the exact plan digest. No scripts, command strings, arbitrary principals, raw ACLs, or raw firewall clauses in IPC. |
| Tampering | A junction/symlink swaps the managed directory or exposes data outside it between preview and ACL/share creation. | Reject reparse roots/ancestors; open and identify the directory in the helper; retain/revalidate a handle and volume/file identity before and after each path mutation. Refuse on drift. Descendant behavior remains blocked pending prototype. |
| Tampering | Ledger corruption causes repair/removal to seize an unmanaged object. | Authenticated/ACL-protected, schema-versioned, atomic ledger with write-ahead operation journal and recorded SIDs/object IDs plus before/after fingerprints. Corruption yields `Unknown`/manual recovery, never inference from a matching name. |
| Repudiation | A user or compromised process denies approval, or a partial helper transaction has no reconstruction trail. | Append-oriented redacted events for preview, helper confirmation, caller SID/session, plan digest, operation/step IDs, before/after non-secret state, result and native error. Never log secrets or detailed file metadata. |
| Information disclosure | Password appears in arguments, stdout/stderr, logs, dumps, clipboard history, diagnostics, ledger, QR image, or a second pipe reader. | Generate inside helper; direct API only; one response; transient dedicated view; no automatic clipboard/QR; exclude secret fields from serialization/log types; clear buffers best-effort; rotate/revoke after suspected disclosure. |
| Information disclosure | SMB/firewall/path fallback exposes data on an unapproved interface or name. | Product-owned rule scoped to approved private path only; no public 445, wildcard credential target, silent LAN/Tailscale/IP fallback, or existing-rule widening. Verify exact endpoint and effective authorization. |
| Denial of service | Automatic retries lock the grant account; forced mapping/session teardown loses open work; helper crash leaves half-created objects. | One explicit credential attempt after correction, no background retry loop, non-forced mapping removal, bounded helper timeouts/cancellation, durable step journal, idempotent reconciliation and precise recovery. |
| Elevation of privilege | General RPC/PowerShell fields, arbitrary path/ACL/principal requests, or a dashboard-only `approved=true` turn the helper into an admin proxy. | Closed operation enum and schema; helper-owned confirmation; caller/ownership validation per request; no child shell; deny unknown fields/versions; least-privilege service identity and Windows API surface. |
| Elevation of privilege | Limited SMB account receives admin/group/user rights or permissive share/NTFS access. | Ordinary local account, membership only in the product group, group-based allow ACEs, necessary owner/SYSTEM access, no guest/personal/admin credential, effective-access verification and revoke tests. |

Microsoft says `ImpersonateNamedPipeClient` changes the server thread to the
security context of the last message read and warns that a failed
impersonation leaves the privileged server context active; the server must
check failure and execute no client request. The helper must revert promptly
after extracting and validating the caller token.
[Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/api/namedpipeapi/nf-namedpipeapi-impersonatenamedpipeclient)

## Exact operation inventory and least privilege

`Observe`, `Preview`, `Verify`, and `Reconcile` never imply permission to
mutate. Each mutating operation is a separate typed request with its own
confirmation and idempotency key.

### Host Files

| Operation | Executor and least privilege | Allowed effect | Preconditions, verification, and refusal |
| --- | --- | --- | --- |
| `ObserveHostReadiness` | Unelevated dashboard/probes | Read supported OS, services, SMB policy, folder/share/ACL/firewall/user-right observations | No helper and no writes. Failed observation is `Unknown`, not permission to repair. |
| `SelectManagedFolder` | Dashboard plans; helper validates at mutation time | Record an existing local NTFS directory, or create one new product-owned leaf only if a later approved design includes creation | Exact absolute DOS path only; reject UNC/device/root/ADS/reparse/drift and inaccessible ownership. Never delete contents. |
| `CreateManagedAuthorizationGroup` | Helper; local account/group administration | Create one product-namespaced local security group | Refuse same-name unmanaged group; record SID immediately; verify non-admin membership/rights. |
| `CreateManagedShare` | Helper; SMB share administration | Create one product-namespaced share for the exact validated directory | Refuse an existing share unless ledger ID, path identity, and descriptor all match; no hidden takeover or path rewrite. |
| `ApplyManagedShareAcl` | Helper; share security-descriptor write | Add/restore only product group and necessary SYSTEM/owner allow entries | Refuse unknown ACEs or inherited/unmanaged descriptor semantics; never widen an existing share. Verify exact descriptor. |
| `ApplyManagedNtfsAcl` | Helper; directory security write, taking no ownership | Add/restore only ledger-recorded ACEs needed for product group read/write while preserving required owner/SYSTEM access | Refuse if applying requires ownership takeover, inheritance replacement, removing unknown ACEs, or touching descendants outside a separately approved plan. Record exact ACE identities and pre-state. |
| `CreatePrivateSmbFirewallRule` | Helper; Windows Firewall policy write | Create one named, product-owned inbound TCP 445 rule scoped to approved profile/interface/remote-address design | Refuse if equivalent safe reachability already depends on unmanaged/GPO rules, required scope cannot be expressed/proven, or policy store is GPO-owned. Never enable/edit built-in File and Printer Sharing groups. |
| `CreateAccessGrant` | Helper; local account administration plus product-group membership | Generate secret; create one non-admin local account; add only to product group | Fixed generated name, cryptographic random password, no admin or extra groups, no password in request/log. Refuse name/SID collision and user-right denial. Verify account flags/group. |
| `RotateAccessGrant` | Helper; password reset on one ledger SID | Generate and set a new password, increment revision, return it once | Confirm loss of old access; no recovery of old secret; partial display failure becomes `Repair needed`, then reissue or revoke. |
| `RevokeAccessGrant` | Helper; membership/account control for one ledger SID | Remove from product group, disable account, optionally close only attributable sessions after separate confirmation | Verify SID, group, share and consequence. Do not remove shared ACLs. Keep tombstone. Account deletion is a later separately confirmed cleanup. |
| `RemoveHostConfiguration` | Helper; rights limited to recorded product objects | Disable/delete recorded grants per policy, remove product share/firewall rule/group/ACEs, retain folder and files | Reverse dependency order; current-state fingerprint must match. Never delete managed directory/files, unknown ACEs, unmanaged shares/rules/accounts, or broad sessions. |
| `VerifyHostConfiguration` / `ReconcileOwnedDrift` | Read-only verification; helper only for a separately confirmed repair | Compare live SIDs/object IDs/descriptors with ledger; restore only a missing/changed product-owned element | Foreign drift, ambiguous identity, GPO override, ledger corruption, or ownership mismatch stops as `Unknown`/manual recovery. |

`NetUserAdd` creates local accounts and reports password-policy failures;
security-group membership confers the group's rights and permissions.
[NetUserAdd](https://learn.microsoft.com/en-us/windows/win32/api/lmaccess/nf-lmaccess-netuseradd),
[local group membership](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.localaccounts/add-localgroupmember?view=powershell-5.1)

### Connect to Files

| Operation | Executor and least privilege | Allowed effect | Preconditions, verification, and refusal |
| --- | --- | --- | --- |
| `ImportCredentialTransfer` | Unelevated dashboard, current user | Parse bounded schema into transient memory | Treat as bearer secret; reject oversized/unknown/malformed data; no persistence, logs, command line, automatic clipboard, or helper. |
| `ImportEndpointUpdate` | Unelevated dashboard, current user | Parse one minimal non-secret endpoint update | Require exact existing product host ID, grant ID, and credential revision plus one new selected endpoint. No password, alternate, discovery, guessing, pairing service, or state change on import. |
| `VerifySelectedEndpoint` | Unelevated Connect flow, current user's network context | One bounded SMB authentication/authorization check against the exact approved UNC | No automatic retry or LAN/Tailscale/IP/name substitution. TCP 445 is not verification. Detect lockout/collision without revealing username validity. |
| `SaveCredential` | Unelevated current-user Credential Manager API | Save one credential for the exact provider-supported server target, recorded separately from the full mapping UNC | Separate unchecked-by-default consent. No wildcard/alternate target. Use the helper-observed qualified local SAM authority, not an authority inferred from an endpoint alias. Record target/name only, never blob. |
| `MapDrive` | Unelevated current-user network API | Map one selected unused letter to exact UNC; persist only if separately approved | Refuse letter/server credential collision; use in-process APIs with no password-bearing shell or child-process argument; no force disconnect. |
| `SwitchEndpoint` | Unelevated current user | After explicit endpoint-update import and re-verification, remove exactly one product-owned mapping/credential and create the explicitly selected new endpoint | Require matching existing host/grant/revision; preview old/new roots, exact provider-target change, and persistence consequence. Never discover, silently rewrite, or leave stale product credential targets. |
| `RemoveClientConfiguration` | Unelevated current user | Non-force remove recorded mapping and delete recorded Credential Manager target | Open files stop cleanup with close-and-retry guidance. `ERROR_NOT_FOUND` is idempotent success. Host revoke cannot perform this remote client cleanup. |
| `InstallOrSignInTailscale` | External Tailscale-owned flow under its own UX/authorization | Handoff only | Balls Server never receives Tailscale credentials, silently installs, changes host name, tailnet policy, or declares SMB authorization from reachability. |

Windows Credential Manager persists credentials in the calling user's context,
and `WNetAddConnection2`/`WNetCancelConnection2` create and remove mappings;
non-force removal avoids disconnecting open work.
[Credential structure](https://learn.microsoft.com/en-us/windows/win32/api/wincred/ns-wincred-credentialw),
[CredDelete](https://learn.microsoft.com/en-us/windows/win32/api/wincred/nf-wincred-creddeletew),
[WNetAddConnection2](https://learn.microsoft.com/en-us/windows/win32/api/winnetwk/nf-winnetwk-wnetaddconnection2w),
[WNetCancelConnection2](https://learn.microsoft.com/en-us/windows/win32/api/winnetwk/nf-winnetwk-wnetcancelconnection2w)

## Refuse versus automate policy

| Existing or required state | Automate | Refuse and explain |
| --- | --- | --- |
| Unmanaged SMB share | Nothing | Any matching name/path not proven by ledger identity. Offer another product-owned share name or administrator-led removal outside Balls Server. |
| Unmanaged NTFS/share ACL | Add only a precisely approved product ACE when preservation and effective access are proven by the final design | Ownership takeover, unknown ACE removal, inheritance replacement, broad principals (`Everyone`, guest), or any descriptor Balls Server cannot round-trip safely. |
| Global SMB server/client settings | Nothing | SMB1 enabled, SMB2/3 disabled, minimum dialect below product policy, signing weakened, or setting unreadable/GPO-owned. Show exact setting and administrator recovery; rerun preflight. |
| Firewall | Create/update/delete only a uniquely named product rule with a recorded rule identity and narrow approved scope | Enabling/editing built-in groups, `Any` profile/address when narrower scope is required, public profile/public 445, GPO-owned policy, unmanaged conflicting rule, or unverifiable effective scope. |
| User-right assignment | Nothing | `SeNetworkLogonRight` absent or `SeDenyNetworkLogonRight` applies. Deny overrides allow, and higher-precedence policy can replace local configuration; direct the administrator/GPO owner to resolve it. [Allow right](https://learn.microsoft.com/en-us/previous-versions/windows/it-pro/windows-10/security/threat-protection/security-policy-settings/access-this-computer-from-the-network), [user-right policy](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-userrights) |
| Reparse or ambiguous path | Nothing | Root/ancestor reparse point, UNC/device/root/ADS path, changed volume/file identity, traversal, inaccessible component, or descendant reparse until containment is proven. Never delete or retarget a reparse point. |

Windows documents `FILE_FLAG_OPEN_REPARSE_POINT` as opening the reparse point
rather than its target, and `FILE_ATTRIBUTE_REPARSE_POINT` as the indication
that a file or directory has reparse data. Those are necessary observations,
not by themselves a complete race-free subtree policy.
[CreateFile](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-createfilew),
[reparse operations](https://learn.microsoft.com/en-us/windows/win32/fileio/reparse-point-operations)

## Helper authorization, IPC, and TOCTOU contract

### Request and confirmation

1. Dashboard asks the helper for a read-only normalized plan using an opaque
   product host/grant ID and a fixed operation enum. The helper observes live
   state and returns the exact proposed effects and a digest; the dashboard
   does not supply raw ACLs, principals, firewall expressions, scripts, or an
   arbitrary path for direct execution.
2. Dashboard renders that plan for usability. The privileged side then obtains
   confirmation over the same digest. **Recommendation:** use a small,
   one-shot, signed elevated confirmation component (or equivalently secure
   helper-owned local interaction) because a service in session 0 should not
   trust a dashboard-only `approved=true`. The confirmation shows operation,
   folder/share, grant label, firewall/path scope, destructive consequences,
   and objects retained.
3. Confirmation creates a single-use authorization record bound to protocol
   version, initiating user SID and logon session, operation/transaction ID,
   nonce, plan digest, issue/expiry time, and helper instance. It contains no
   secret. Any plan drift invalidates it and requires a new preview/confirmation.
4. Execution accepts the record once. Unknown protocol fields/version,
   duplicate/stale nonce, wrong token/session, digest mismatch, or prior
   completion is a safe refusal. UAC elevation alone does not substitute for
   operation-specific confirmation.

This recommendation intentionally refines the earlier “helper has no UI”
sketch: the standing mutation service still has no general UI or command
surface, but security authorization must be owned by the privileged boundary,
not merely asserted by the unelevated dashboard. The final helper architecture
must choose and prototype the safe Windows interaction mechanism.

### Pipe and process behavior

- Local named pipe only; explicit DACL for the initiating SID, helper/service
  identity, and required SYSTEM/administrator identity; explicitly deny
  network access. Random per-transaction name or unguessable suffix in
  addition to authorization checks.
- Fixed bounded binary/JSON schema with maximum sizes, depth, string formats,
  enum values, request count, and deadline. No PowerShell, shell, executable,
  environment, callback, URL, format string, raw security descriptor, or
  arbitrary filesystem operation fields.
- After reading the request, impersonate the actual pipe client only to obtain
  and validate token SID, logon session, integrity/elevation facts, then
  `RevertToSelf`. If impersonation or token validation fails, perform no work.
- Exactly one request/response and one reader. Never serialize a password into
  generic status/error objects. Disconnect and zero secret buffers after the
  response; timeout/crash after account creation becomes journaled `Repair
  needed`, not an automatic second credential response.
- Bound execution and cancellation per step. Kill/restart must not replay a
  non-idempotent step solely because the dashboard timed out.

### Path and state TOCTOU

1. Helper canonicalizes and opens every path itself; dashboard strings are
   hints only. Reject relative, UNC, device, volume-root, trailing-dot/space,
   alternate-stream, reserved-name, and traversal forms before object lookup.
2. Walk root and ancestors without following reparse points; reject any
   reparse tag. Open the selected directory, record volume serial plus stable
   file identity and final normalized path, and keep a handle where supported.
3. Re-observe share name, directory identity, descriptor, firewall policy and
   ledger generation immediately before each mutation. Compare with the
   confirmed plan. On mismatch, end the transaction without “best effort.”
4. Prefer handle-based filesystem security APIs. When a Windows API requires a
   name (notably share creation), revalidate identity directly before and after
   the call; if post-verification differs, mark `Repair needed` and remove only
   the newly created share if its identity still matches.
5. Do not recursively rewrite child ACLs in the initial design. A recursive
   subtree walk is both destructive and race-prone. Descendant reparse behavior
   must be proven in the isolated test matrix before the folder can be claimed
   confined.

## Ownership ledger, transaction, rollback, and manual recovery

### Non-secret ledger

Store an ACL-protected, schema-versioned record containing opaque host,
transaction, operation and grant IDs; initiator SID/session category; managed
directory volume/file identity and normalized path; account/group SIDs and
names; share/rule/mapping/credential-target identities; exact product ACE/rule
fingerprints; endpoint root; confirmation plan digest/time; step state;
credential revision; tombstones; and redacted audit correlation IDs. Never
store passwords, transfer bundles, QR/clipboard material, credential blobs,
Tailscale credentials, folder listings, or raw exception text.

Before the first Windows change, atomically persist `Preparing` plus the
confirmed plan and observed pre-state. For each step persist `Intent`, perform
once, verify live state, then persist `Applied` and its exact postcondition.
Finish only after end-to-end verification and an atomic `Active` transition.
Recovery reads live state and classifies each step as absent, expected,
conflicted, or unknown; names alone never prove ownership.

### Rollback rule

Rollback runs in reverse dependency order and removes only an `Applied` object
whose stable identity and live fingerprint still equal the recorded
postcondition. Missing means idempotently complete. Changed, ambiguous,
GPO-owned, in-use, or unobservable means stop, retain the journal, report
`Repair needed`/`Unknown`, and give manual recovery. Rollback never restores a
whole ACL/firewall/policy snapshot over later changes and never deletes the
managed directory or any user file.

| Partial state | Safe automated action | Manual recovery when identity/state is not exact |
| --- | --- | --- |
| Group/account created, later step failed | Disable grant first; remove membership/account/group only if SID and empty/product-only dependencies match | Show redacted object names/SIDs and exact verification commands; administrator removes only after confirming no foreign use. |
| Share created, ACL/firewall failed | Remove only the just-created share if share identity/path/descriptor match; retain folder/files | Administrator inspects share path, open sessions and descriptor, then removes the named share only; never delete its directory. |
| Product ACE applied, later step failed | Remove only the exact recorded ACE if descriptor generation/fingerprint permits a safe edit | Present the exact product SID/ACE and current mismatch; administrator uses Windows security UI/API to remove that ACE only. |
| Firewall rule created, verification failed | Delete only recorded product rule identity when unchanged | Show rule store/name/ID and effective-policy conflict; administrator or GPO owner resolves it. Do not toggle built-in groups. |
| Password changed but display/client setup failed | Keep grant disabled until owner chooses fresh transfer, rotation, or revoke; never recover old secret | Owner explicitly reissues/rotates or revokes. Client removes stale saved credential in that profile. |
| Mapping/credential partly created | Non-force remove exact recorded mapping, then exact target credential in current user context | Close open files; remove the named mapping and Credential Manager target from the affected client profile. Host cannot do this remotely. |
| Ledger corrupt/missing | No destructive reconciliation | Export redacted observations; administrator compares stable IDs. Re-adoption of existing objects is a future, separately threat-modeled operation, not repair. |
| User-right/global SMB/GPO conflict | No product rollback or mutation | Administrator/GPO owner changes policy outside Balls Server and reruns read-only preflight. |
| Reparse/path identity drift | No share/ACL mutation and no reparse deletion | Owner restores an ordinary local directory/path or selects a different folder; rerun preview. Preserve all targets and data. |

## Required security tests before mutating implementation

- IPC: wrong/low-integrity SID, different session, network caller, pipe
  squatting, replay/stale nonce, second reader, malformed/oversized request,
  unknown enum/version, impersonation failure, digest/plan drift, service and
  dashboard crash at every boundary, and secret scans of logs/dumps/stdout.
- Consent: compromised dashboard attempts a fabricated approval, changed path,
  expanded ACL/firewall scope, repeated transaction, and approval reuse after
  helper restart. Only the helper-owned exact-plan confirmation may authorize.
- Paths: junction/symlink at root and every ancestor, mount point, subst/device/
  UNC/ADS/traversal forms, rename/swap between plan and each API call, volume
  replacement, and descendant reparse creation before/after setup.
- Ownership: same-name foreign share/group/account/rule, deleted-and-recreated
  SID/object, changed ACE/rule after product creation, missing/corrupt ledger,
  GPO firewall/right override, and repair/removal with unrelated state diffs.
- Lifecycle: interruption after every mutation, reboot reconciliation,
  password-policy rejection, lockout, open SMB file/session, grant rotation and
  revoke, non-force mapping cleanup, and proof that folder/user files survive.
- Network: SMB1 off, negotiated SMB 3.0+, signing preserved, wrong/revoked grant
  denied, separate LAN/MagicDNS exact-root tests, no auto/IP fallback, no
  public-profile or external TCP 445 exposure, and Tailscale reachability kept
  distinct from SMB authorization.

All mutating tests run only in snapshotted disposable Windows 11 Pro 24H2+
host/client VMs on an internal/private switch or non-production tailnet. The
default developer suite remains non-mutating and unelevated.

## Open blockers

1. **Helper-owned confirmation mechanism:** prototype the one-shot elevated
   confirmer/service handshake, session targeting, secure-desktop/UAC behavior,
   signing and failure recovery. Dashboard-only consent is rejected.
2. **Reparse containment:** prove Windows SMB behavior for root, ancestor and
   descendant junctions/symlinks and define how later reparse drift is detected
   without unsafe recursive mutation. No managed share over an unresolved
   reparse-containing tree.
3. **Firewall scope:** prototype whether a product-owned TCP 445 rule can be
   expressed and verified narrowly for the approved trusted-LAN and Tailscale
   paths on supported Windows. If not, firewall automation is refused.
4. **NTFS ACL preservation:** specify the exact owner/SYSTEM/product-group ACE
   template, inheritance behavior, effective-access proof, and reversible
   fingerprinting before any ACL writer is approved.
5. **Helper service identity/installation:** select the minimum Windows service
   account/privileges and installation/update trust model at the installer
   milestone; do not default to a general LocalSystem administrative broker.

## Sources and project evidence reviewed

Project evidence: `AGENTS.md`, `docs/PRODUCT.md`, `docs/ARCHITECTURE.md`,
`docs/roadmap/v0.3.0.md`, ADRs 0001-0005,
`access-grant-secret-lifecycle.md`, and
`canonical-name-access-path-and-tests.md`.

External facts use Microsoft primary documentation linked inline, including
the existing research registers for local accounts/groups, password handling,
Credential Manager, mappings, user rights, auditing, and named-pipe security.
Recommendations, operation boundaries, refusals, ledger design, rollback, and
test requirements are Balls Server inferences from those facts and the product
guardrails.
