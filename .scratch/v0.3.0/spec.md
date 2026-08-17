# v0.3.0 — Setup security and architecture design specification

Status: ready-for-agent

**Roadmap authority:** [docs/roadmap/v0.3.0.md](../../docs/roadmap/v0.3.0.md)

## Problem Statement

The accepted Host Dashboard can explain whether one Windows computer and managed-folder candidate are ready for private SMB access, but it intentionally cannot configure anything. The next milestones will create a share and narrowly scoped product-owned firewall rules, add limited Windows identities, transfer secrets, persist a client credential, map a drive, repair drift, revoke access, and remove only product-owned configuration. Machine-wide SMB protocol/signing and Server-service policy remain observe-only, administrator-managed prerequisites; Balls Server does not adjust them. Those operations and refusal boundaries cross administrator, local-user, network, and secret-handling trust boundaries. Implementing them without one approved design would risk turning the unelevated dashboard into a confused deputy, taking over unmanaged configuration, leaking a credential, locking out an access grant, or damaging user files during rollback.

v0.3.0 must resolve those risks before mutating production code begins. It must define every proposed operation, who is allowed to perform it, what the owner sees and approves, how the operation proves ownership and idempotency, how partial failure is represented, how recovery avoids unrelated state, and which tests are permitted on which machines. It must also settle the relationship between a stable product host identity and the separate LAN and Tailscale SMB endpoints that Windows treats as connection and credential targets.

## Solution

Publish a complete, design-only security contract for the v0.4.0 through v0.7.0 file-sharing path.

The dashboard remains unelevated and observational. It can prepare a provisional change preview and launch one on-demand privileged helper, but it cannot perform a host mutation. The helper accepts only a versioned, typed, local request; authenticates the initiating user and process; re-observes current state; rejects stale or unmanaged state; presents the authoritative plan; and applies only an approved operation inventory. Every applied step is journaled in a protected product-owned change ledger, verified after execution, and either completed, rolled back narrowly, or left in a named recoverable state.

Each access grant uses one separate, non-administrative host-local Windows identity and one cryptographically random password. The password is generated inside the helper and transferred once through restricted local IPC into a transient setup-code view. The setup code is a bearer secret, not a device-trust assertion or a truly expiring one-time password. On the client, Credential Manager storage and persistent drive mapping occur only in the signed-in user's context and only after explicit consent.

One opaque product host identity ties together the managed share, access grants, ledger, and audit records. It is not an SMB address. The trusted-LAN Windows computer name and the full Tailscale MagicDNS name are separate access endpoints. A client selects and verifies one exact endpoint; Balls Server never silently substitutes another name, IP address, saved credential target, or persistent mapping. A later endpoint change requires a separate, minimal owner-transferred endpoint-update bundle bound to the client's existing host ID, grant ID, and credential revision, followed by explicit import and re-verification.

v0.3.0 ships research, threat-model, contract, recovery, prototype, and test-topology evidence only. It creates no production helper, share, account, group, ACL, firewall rule, mapping, credential, installer, or Tailscale state.

## User Stories

### Owner understanding and consent

1. As an owner, I want setup to show the exact managed folder, share, account, group, permission, firewall, SMB, and service changes before elevation so that I can understand the proposed effect.
2. As an owner, I want the privileged helper to recheck and authoritatively confirm that same plan after elevation so that a stale dashboard preview cannot authorize changed state.
3. As an owner, I want any difference between the previewed and authoritative plan to stop the operation so that consent is never stretched to new work.
4. As an owner, I want consent to name irreversible consequences, temporary interruption, restart requirements, and recovery choices so that approval is informed.
5. As an owner, I want cancellation to be available before the first mutation and between safe steps so that I remain in control without leaving a half-applied primitive.
6. As an owner, I want one clear result—Completed, Canceled, Repair needed, Refused, or Unknown—so that a partial operation is never presented as success.

### Privileged-helper boundary

7. As an owner, I want the Host Dashboard to remain as-invoker and unelevated so that ordinary viewing and diagnostics never inherit administrator rights.
8. As an owner, I want the helper to run on demand and exit after one bounded request so that Balls Server does not create a standing privileged service.
9. As an owner, I want helper requests to use fixed operation types and bounded data instead of command strings or scripts so that the dashboard cannot become a general administration channel.
10. As an owner, I want the helper to reject remote clients, wrong users, wrong sessions, untrusted callers, stale revisions, replays, oversized messages, and unknown protocol fields so that local IPC is not an open mutation surface.
11. As an owner, I want a mutating helper request to require an authoritative helper-controlled confirmation so that another unelevated process cannot silently borrow the helper.
12. As an owner, I want every helper timeout or crash to leave a durable operation state and exact recovery action so that uncertainty fails closed.
13. As an owner, I want the helper to return typed, redacted results rather than raw command output so that secrets and private system detail do not leak through diagnostics.

### Managed folder and share safety

14. As an owner, I want setup to accept only one existing folder on a fixed local NTFS volume so that Balls Server never creates or shares an ambiguous remote/device path.
15. As an owner, I want drive roots, Windows/system locations, UNC paths, device paths, and a selected tree containing an unresolved reparse point refused so that setup cannot unexpectedly expose a broader namespace.
16. As an owner, I want Balls Server never to take ownership of the managed folder or replace its complete ACL so that existing owner and system access remains intact.
17. As an owner, I want setup to add only the exact product-group ACE it needs and to stop on a conflicting deny or ambiguous equivalent ACE so that unmanaged permissions are not silently reclassified as product-owned.
18. As an owner, I want an existing unmanaged share that uses the proposed name or folder to stop setup so that Balls Server never adopts, edits, renames, or removes another share.
19. As an owner, I want the managed share to expose only the selected folder and grant change access only through the product-owned access group so that unrelated authenticated users do not inherit access.
20. As an owner, I want share and NTFS authorization verified together so that a successful share creation is not mistaken for effective file access.
21. As an owner, I want repair and removal to preserve the managed folder and every file under it even when other cleanup steps fail.

### SMB, service, firewall, and policy

22. As an owner, I want setup to leave a compliant SMB 3.0-or-newer server unchanged so that global settings are not churned.
23. As an owner, I want SMB1 disabled, SMB 2/3 enabled, minimum negotiation fixed at SMB 3.0, and signing protections preserved before hosting can be configured.
24. As an owner, I want a noncompliant global SMB or Server-service state to stop setup with exact administrator guidance rather than let Balls Server rewrite machine-wide policy.
25. As an owner, I want Balls Server to create its own narrowly scoped firewall rules instead of enabling or editing a broad built-in rule group so that ownership and removal are precise.
26. As an owner, I want the LAN rule limited to TCP 445 on approved private/domain local interfaces and local-subnet traffic so that public-network SMB is not opened.
27. As an owner, I want any Tailscale rule bound to the observed Tailscale interface and private Tailscale address scope so that it does not become a general Public-profile SMB exception.
28. As an owner, I want an unavailable or policy-managed firewall/SMB state reported as refused or unknown with manual recovery rather than bypassed.
29. As an owner, I want Balls Server never to weaken SMB signing, encryption, firewall defaults, network category, router policy, or public-edge policy to make setup pass.
30. As an owner, I want Tailscale install and sign-in handled as a separate user-controlled handoff so that Balls Server never collects or stores Tailscale credentials.

### Product ownership and reconciliation

31. As an owner, I want every product-created share, group, account, ACE, firewall rule, mapping, and saved-credential target tied to an opaque product identifier and stable Windows identifier so that a friendly name alone never proves ownership.
32. As an owner, I want the product-owned change ledger protected from ordinary writes and free of access secrets so that it can guide repair without becoming a credential vault.
33. As an owner, I want ledger updates to be versioned, atomic, and journaled before mutation so that a crash does not erase which step was attempted.
34. As an owner, I want reconciliation to classify each resource as owned-and-conformant, owned-and-drifted, missing, unmanaged conflict, ambiguous, or unknown so that repair decisions are deterministic.
35. As an owner, I want setup repeated with the same desired state to verify and converge without duplicate objects so that operations are idempotent.
36. As an owner, I want a request carrying an old ledger revision to stop and refresh the preview so that concurrent or external changes are not overwritten.
37. As an owner, I want rollback to reverse only a product-owned change created by the current transaction so that broad snapshots cannot erase changes made by Windows or another administrator.
38. As an owner, I want a changed or ambiguous owned object left in Repair needed with exact manual guidance rather than force-restored.
39. As an owner, I want removal to keep a non-secret tombstone for completed access-grant revocation and operation evidence while deleting no user file.

### Access grants and limited identities

40. As an owner, I want one opaque, non-administrative local Windows account per intended client profile so that one grant can be rotated or revoked without affecting another.
41. As an owner, I want every active grant account to belong only to the product-owned share-access group and no administrative, Remote Desktop, backup, or unrelated group.
42. As an owner, I want Windows network-logon policy observed but not rewritten so that domain or machine security policy is not broadened for convenience.
43. As an owner, I want the helper to generate at least 32 cryptographically random bytes for each password and satisfy effective local password policy without reusing a rejected value.
44. As an owner, I want the access user unable to change its product-managed password so that rotation and revocation remain predictable.
45. As an owner, I want password expiry and lockout policy observed and reflected in rotation/recovery guidance rather than disabled.
46. As an owner, I want revocation to remove group membership and disable the one account before optional deletion so that access stops without changing other grants.
47. As an owner, I want rotation to invalidate the old password and produce a new display-once transfer so that a suspected disclosure has a precise response.
48. As an owner, I want already-open sessions attributable to the selected grant handled explicitly and never confused with revoking every client.

### Setup-code and secret lifecycle

49. As an owner, I want a setup code to contain only its schema version, product host identity and label, exactly one selected endpoint, share name, qualified host-local account identity, grant password, credential revision, and generation time so that the client receives only what it needs.
50. As an owner, I want the setup code described as a bearer credential bundle so that a copied code is never represented as expired merely because its display closed.
51. As an owner, I want the setup code shown only once in a dedicated transient view and removed after five minutes or an explicit Hide action so that casual exposure is bounded.
52. As an owner, I want a lost or prematurely hidden code recovered through rotation/reissue rather than by reading the old password from product storage.
53. As an owner, I want secret IPC restricted to one initiating user, one local pipe instance, one nonce-bound response, and one read so that another process cannot replay or steal the response.
54. As an owner, I want credentials excluded from arguments, environment variables, stdout, stderr, logs, audit events, diagnostics, ordinary configuration, the ledger, crash attachments, and release artifacts.
55. As an owner, I want manual clipboard copy or QR display to require an explicit action and warning so that another disclosure channel is never automatic.
56. As an owner, I want any product-written clipboard value cleared only when it is still the exact value Balls Server placed there so that cleanup does not erase newer user clipboard content.
57. As an owner, I want secret buffers created late, retained briefly, and cleared where the platform permits so that lifetime is minimized without claiming managed memory is perfectly erasable.

### Host identity, endpoints, and fallback

58. As an owner, I want one opaque product host identity independent of Windows, DNS, Tailscale, IP, or display names so that renaming a computer does not transfer ownership to another machine.
59. As an owner, I want the LAN endpoint to use the currently observed Windows computer name and the Tailscale endpoint to use the currently observed full MagicDNS name so that each path has an explicit published SMB root.
60. As an owner, I want endpoint observations timestamped and treated as drift-prone so that a renamed or collision-suffixed host is reverified rather than guessed.
61. As a client user, I want to select and verify one endpoint before saving a credential or mapping so that my persistent state names the path I approved.
62. As a client user, I want failure of my selected endpoint to preserve my current state and require a separately owner-transferred endpoint update plus an explicit Switch action so that Balls Server never discovers or silently moves to another SMB path.
63. As a client user, I want an endpoint switch to require explicit import of a separate owner-transferred bundle containing one newly selected endpoint and matching my existing host ID, grant ID, and credential revision, then show the new UNC root, mapping replacement, provider credential-target change, and verification step before mutation.
64. As a client user, I want IP UNC paths allowed only as an advanced transport diagnostic with no credential, mapping, or verified-access claim so that IP is not an authentication workaround.
65. As an owner, I want Balls Server never to create a DNS alias, hosts entry, SMB alias, computer rename, NetBIOS policy change, LLMNR policy change, or Tailscale rename to manufacture a canonical name.

### Client credential, mapping, and lockout safety

66. As a client user, I want parsing and read-only endpoint checks to happen before an SMB authentication attempt so that obvious route failures do not consume password attempts.
67. As a client user, I want at most one SMB authentication attempt per explicit action and no automatic retry or fallback so that Balls Server does not drive account lockout.
68. As a client user, I want invalid credential, locked account, path unavailable, and observation failure represented as distinct typed recovery categories without revealing whether a guessed username exists.
69. As a client user, I want Windows Credential Manager storage to require explicit consent and apply only to the exact provider-supported server credential target in my current Windows profile, recorded separately from the full selected mapping UNC.
70. As a client user, I want reconnect-at-sign-in consent separate and visible, with reconnect selected by default only after I choose to save the dedicated credential.
71. As a client user, I want an existing mapping, drive-letter use, open file, or different credential to the same server detected before change so that Balls Server does not disconnect unrelated work.
72. As a client user, I want mapping removal to avoid forced disconnect and delete only the product-recorded target so that open files and unrelated credentials are preserved.
73. As a client user, I want connection verification to create, read, rename, and delete one uniquely named temporary file and to report an exact leftover path if cleanup fails.
74. As an owner, I want host revocation to remain effective even if a client still holds an obsolete saved credential, while client cleanup remains a separate current-user action.

### Audit, privacy, and recovery

75. As an owner, I want a local audit event for preview, consent, authorization, mutation, verification, rollback, repair, credential display acknowledgement, mapping, rotation, revocation, and removal so that consequential actions are explainable.
76. As an owner, I want audit events to contain timestamps, opaque IDs, actor category, before/after non-secret state, result category, and native error code without passwords, code payloads, file listings, raw exceptions, or peer/user data.
77. As an owner, I want default diagnostics to redact account names, host names, UNC roots, IP addresses, and the managed-folder path unless I explicitly preview their inclusion.
78. As an owner, I want broad Windows detailed-file-share auditing left unchanged so that Balls Server does not create high-volume privacy-sensitive logs.
79. As an owner, I want every partial-failure point to have a specific automated rollback rule or a precise manual recovery instruction so that “try setup again” is never the only guidance.
80. As an owner, I want recovery instructions to identify what Balls Server will not touch so that unmanaged shares, policies, accounts, mappings, credentials, ACLs, and user files remain protected.

### Verification and future implementation gate

81. As a developer, I want default tests to stay unelevated, deterministic, non-mutating, offline, and independent of Tailscale, shares, accounts, mappings, or a second computer.
82. As a developer, I want helper protocol, planner, ledger, reconciliation, redaction, endpoint, and lifecycle behavior testable through platform-neutral seams before any Windows mutation adapter exists.
83. As a developer, I want mutating integration tests opt-in and guarded by an administrator check, disposable-VM marker, known snapshot, and unique product-test namespace so that they cannot run accidentally on the development machine.
84. As a developer, I want two-VM LAN and private-tailnet tests isolated from public networks and production identities so that no verification exposes TCP 445 or real credentials.
85. As a developer, I want every mutation test to inventory before/after unrelated state and preserve the managed folder and test files required by the scenario so that cleanup safety is evidence, not assumption.
86. As an owner, I want v0.3.0 to remain documentation and prototype only so that accepting this design is a gate for later mutation, not permission for this version to change Windows.

## Implementation Decisions

### Trust boundaries and threat model

- Treat the owner, the signed-in client user, the unelevated dashboard, the elevated helper, Windows local security authorities, Credential Manager, SMB, Tailscale, and protected local state as distinct trust zones.
- Threat-model spoofing, tampering, repudiation, information disclosure, denial of service, elevation of privilege, replay, time-of-check/time-of-use races, account lockout, unmanaged-state takeover, credential-target collision, endpoint drift, and rollback damage.
- Do not claim to defend against a malicious local administrator, kernel compromise, or a compromised Windows/Tailscale platform. An administrator can bypass product ACLs and inspect process memory; the design minimizes and audits product exposure rather than inventing an impossible boundary.
- Treat the setup code and saved SMB password as high-value bearer secrets. Treat endpoint names, account names, selected folder paths, ledger content, and audit metadata as private local data even though they are not authentication secrets.

### Unelevated dashboard and privileged helper

- Keep all dashboard diagnostics and presentation as-invoker. The helper is a separate on-demand process with no standing service, scheduler, background monitor, general shell, arbitrary PowerShell, remote listener, or installer responsibility.
- Use a versioned, strict, bounded local protocol with an enum operation, operation ID, initiating user/session identity, request nonce, expected ledger revision, desired resource identifiers, and validated typed values. Unknown fields, operations, sizes, revisions, users, sessions, or replays are refused.
- Use one local named-pipe instance with an explicit DACL for the initiating user, Administrators, and SYSTEM; reject remote clients; bind the request and response to the operation ID and nonce; and close after one terminal response. Create it with first-instance protection and abort on a pre-existing endpoint.
- Authenticate both peers with Windows process and token evidence: each side obtains the connected peer process ID, requires the expected user/session and integrity/elevation, validates the exact product image path in an administrator-protected location, product version/hash, and trusted Authenticode signer, then rechecks that the process is still alive. A mismatch, unavailable signer, pipe squatter, or unverifiable peer is a refusal. Unsigned development helpers run only behind an explicit disposable-VM test guard and are never accepted as production authorization.
- The dashboard creates the first-instance, remote-client-rejecting pipe before launching the one-shot helper with ShellExecute runas. The helper connects as the pipe client. Each side validates the peer process ID, token, image, and signature before the dashboard sends the request.
- The helper re-observes state and displays an elevated TaskDialog-style authoritative confirmation in the initiating interactive session. Its authorization record is bound to that helper process instance, user/session, operation ID, nonce, plan digest, and a two-minute monotonic deadline; one Apply click consumes it. UAC elevation alone is not operation consent.
- The dashboard may render a provisional preview from read-only observations. After elevation, the helper re-observes every precondition and computes the authoritative plan. If the plan differs, it returns a redacted changed-preview result and applies nothing.
- A matching plan still requires a minimal helper-controlled confirmation that names the authoritative changes and consequences. Dashboard-only consent is not sufficient authorization for a privileged mutation because another same-user process could attempt to invoke the helper.
- The helper validates and canonicalizes all paths, SIDs, endpoints, and resource identities itself. It never trusts display strings, caller-computed ACLs, arbitrary principal names, or a caller-supplied secret.
- Bound each request and primitive step with a documented timeout. Cancellation is honored before mutation and between primitives; an in-flight primitive completes or fails into the journal before cancellation is acknowledged.
- Return typed operation results and bounded native error codes. Raw stdout, stderr, exception text, secrets, ACL dumps, and command output never cross into ordinary dashboard state.

### Approved operation inventory

| Operation | Execution boundary | Mutation and privilege | Safe completion rule |
| --- | --- | --- | --- |
| Observe and draft preview | Unelevated dashboard through read-only probes | None | Unknown observations block affected mutations. |
| Authoritative plan and consent | Elevated helper | Read protected machine state; no mutation before confirmation | Plan hash/revision matches the preview and current state. |
| Initialize protected ledger and audit | Elevated helper | Create product-owned machine state with restrictive ACLs | Schema, ACL, owner SID, and empty journal verify. |
| Verify required SMB server state | Unelevated observation, authoritatively rechecked by helper | None; machine-wide SMB protocol, signing, and Server-service configuration remain administrator-managed prerequisites | Re-observation proves Server running, SMB1 disabled, SMB 3.0+ only, and signing not weakened; otherwise setup refuses with exact guidance. |
| Create product access group | Elevated helper | Create one local security group | Stable SID and product marker match the ledger. |
| Authorize managed folder | Elevated helper | Add one inheritable Modify ACE for the product group without replacing the DACL or taking ownership | Effective NTFS access verifies and all unrelated ACEs remain. |
| Create managed share | Elevated helper | Create the fixed Balls managed share with Administrators Full and product group Change; no Everyone/guest access | Path, share ACL, protocol state, and product marker verify. |
| Create private firewall rules | Elevated helper | Create separate product-owned TCP 445 rules for approved LAN and/or Tailscale scope | Rule identity, interface/profile, address, port, direction, and enabled state match exactly. |
| Create/rotate/revoke access grant | Elevated helper | Create or change one local non-admin account, group membership, password, disabled/pending state, and attributable sessions | Account/group SIDs and effective share/NTFS access match the selected grant state; undisclosed credentials remain disabled. |
| Activate transferred access grant | Elevated helper | Enable one disabled pending grant only after a separate preview and owner confirmation | Credential revision matches the displayed transfer, group/ACL ownership is conformant, and the account becomes enabled once. |
| Verify, repair, or remove host configuration | Elevated helper | Reconcile and mutate only proven product-owned resources | Every changed resource is conformant or a named recoverable state; folder/files remain. |
| Parse setup code | Unelevated client dashboard | Transient secret import only | Payload contains exactly one selected endpoint and qualified host-local account identity; no alternate, discovery, persistence, or credential attempt. |
| Import endpoint update | Unelevated client dashboard | Transient non-secret import only | Bundle contains one new selected endpoint and matches the existing host ID, grant ID, and credential revision; mismatch refuses, and import alone changes nothing. |
| Inspect selected endpoint | Unelevated client dashboard | None | Exact imported endpoint observation is valid; no discovery, guessing, alternate selection, or credential attempt. |
| Authenticate once and verify access | Unelevated client user | One explicit SMB session and one isolated temporary file lifecycle | Exact endpoint and grant authenticate; temporary file is removed or precisely reported. |
| Save/delete credential | Current client user | Credential Manager write/delete after consent | The exact provider-supported server credential target, recorded separately from the mapping UNC, exists or is idempotently absent. |
| Map/unmap drive | Current client user | WNet mapping/profile change after consent | Exact drive/root/persistence matches; removal refuses open-file force. |
| Switch endpoint | Current client user | Explicitly replace one product-owned credential target and mapping only after endpoint-update import and re-verification | Existing host/grant/revision match, one new endpoint is selected, old and new objects are exact, and no discovery, fallback, or automatic switch occurs. |
| Tailscale install/sign-in handoff | User-controlled external flow | Outside the Balls Server helper and without Tailscale credentials | User returns and read-only observation confirms current state. |

### Refuse rather than automate

- Refuse a selected folder that is missing, remote, a device path, a drive root, a protected system location, non-NTFS, or has a reparse point at its root or ancestor. Descendant reparse points block setup until the isolated prototype proves a containment and drift policy. Never take ownership to overcome refusal.
- Refuse an unmanaged share-name or shared-folder collision, an unmanaged equivalent ACL, a conflicting deny ACE, an ambiguous product marker, or ledger loss/corruption that prevents ownership proof.
- Refuse global SMB changes when non-administrative unmanaged shares, incompatible active sessions, policy-managed values, or unrecognized configuration could be affected. Give exact manual recovery and re-run observation.
- Refuse to change machine-wide user-right assignments, account-lockout policy, password policy, firewall defaults, network category, DNS/hosts/NetBIOS/LLMNR policy, Windows/Tailscale host names, Tailscale ACLs, router policy, or public edge policy.
- Refuse to edit built-in/unmanaged firewall rules or create Any-profile/Any-remote TCP 445 exposure. A safely scoped rule that cannot be expressed or re-observed is not created.
- Refuse force-disconnect of open files, deletion of unrelated SMB sessions, deletion of unmanaged mappings/credentials, broad ACL restore, or managed-folder deletion.

### Managed resource design

- Use one fixed visible share name, Balls, for the one managed folder. Any unmanaged collision stops setup; v1.0 does not invent a suffix, rename the host, or adopt the share.
- Use one product-owned local security group for share and NTFS authorization. Store its SID as authority; its name and description are human-readable markers, not proof.
- Use one opaque local account name per access grant, constrained to Windows local-name limits, with the friendly client-profile label held only in protected product state.
- Add exactly one Allow ACE for the product-group SID with Modify and Synchronize rights, ContainerInherit and ObjectInherit, and no propagation restriction. Preserve DACL control/inheritance flags, owner, SYSTEM, Administrators, and every unrelated ACE; never take ownership, replace the descriptor, recursively rewrite children, or grant Full Control to access grants.
- Record the binary product ACE plus a canonical multiset fingerprint of all unrelated ACEs and DACL control flags before and after. Automatic removal requires exactly one matching product ACE and an unchanged unrelated fingerprint. Effective-access verification must prove create/read/write/rename/delete for the group and preserve owner/SYSTEM/administrator access; any deny/conflict or round-trip mismatch is refusal.
- The LAN firewall rule permits inbound TCP 445 only on approved Private/Domain local interfaces and local-subnet scope. The Tailscale rule is separately owned, interface-bound, and limited to the documented private Tailscale address space. No rule exposes public inbound SMB.
- A compliant existing global SMB state is observed, not claimed as product-owned. Balls Server does not change global SMB protocol, signing, encryption, Server-service startup, or Group Policy values; noncompliance is an administrator-managed prerequisite with exact recovery guidance.

### Product-owned change ledger and transaction model

- Store a schema version; product host ID; owner SID; revision; desired-state fingerprint; managed-folder reference; endpoint snapshots; operation journal; and resource records for share, group, accounts, exact ACE tuples, firewall rules, full mapping UNC roots, and separately proven provider credential-target names. Store SIDs and stable platform identifiers wherever available.
- Never store a password, setup-code payload, credential blob, Tailscale credential/key, file listing, or password-derived identifier.
- Protect host state in a machine-wide product-data location readable by the owner and Administrators and writable only by the helper/Administrators/SYSTEM. Client mapping records remain current-user state.
- Before the first primitive, atomically persist the request, authoritative plan fingerprint, ledger revision, and Planned journal state. Append Started and terminal verification evidence for every primitive; atomically advance the ledger revision at a safe checkpoint.
- Reconciliation classifies each resource as OwnedConformant, OwnedDrifted, Missing, UnmanagedConflict, Ambiguous, or Unknown. Only the first three may produce an automatic plan, and a missing resource is recreated only when its absence and identity are unambiguous.
- Rollback runs in reverse order over primitives created by the current transaction. It removes an ACE/rule/share/account only if the current object still matches the exact product-created fingerprint. Otherwise it stops in Repair needed and names manual action.
- Idempotent not-found outcomes are success for removal; access-denied, policy-managed, malformed, changed, or unverifiable outcomes are never coerced to success.
- Maintain an atomic protected mirror plus append journal so one damaged copy can be reconstructed. After each completed transaction, offer an explicitly previewed non-secret recovery manifest containing stable resource IDs and exact manual-removal order; it is not ownership authority for automatic repair.
- If every ledger copy is lost or corrupt, product automation becomes read-only. It inventories candidate product markers and produces object-by-object administrator instructions, but the administrator confirms each SID/object in Windows before manual disable or removal. The order removes grants before share, rule, group, and ACE records and never deletes the managed folder or files.
- Retain non-secret revoked-grant tombstones and audit evidence for 90 days after the relevant host configuration is removed, then purge them automatically; secrets are never retained for audit.

### Access-grant and secret contract

- Generate one password per grant or rotation inside the helper from at least 32 cryptographically random bytes using a compatibility-tested encoding. Attempt account creation once per explicit owner action; if local policy rejects the value, destroy it and return a redacted policy result. A later explicit Retry action generates a fresh value. Never resubmit a rejected value or run an automatic policy-guessing loop.
- The account is non-administrative, may not change its password, and belongs only to the product access group. Do not disable password expiry or lockout policy globally. Observe effective policy and surface rotation/lockout recovery.
- Create and rotate into Disabled pending transfer. The secret response and setup-code display do not enable the account. After direct transfer, the owner invokes a separate Activate grant operation whose authoritative preview names the grant and credential revision. A crash, timeout, lost display, or failed client handoff therefore leaves an unusable credential; reissue rotates it while still disabled, and revoke removes it.
- The setup code is a display-once bearer bundle. It contains schema version, product host ID and label, credential revision, share name, exactly one selected endpoint, the helper-observed local SAM authority plus account name as one qualified host-local account identity, password, and generation time. The SAM authority identifies the host account independently of the LAN or MagicDNS alias. The setup code contains no alternate endpoint, Tailscale credential, owner credential, SID, file data, diagnostics, or claim of cryptographic device trust.
- A five-minute display timeout limits screen exposure only. Hide, close, crash, or timeout destroys the available display copy; recovery rotates/reissues the grant. None of those events make a copied password expire.
- Transfer the secret only in the nonce-bound one-response pipe. The helper, dashboard, and client collect the secret late, avoid immutable copies where practical, and clear owned native/managed buffers as early as the platform permits.
- Clipboard copy and QR rendering are explicit, warned actions. A clipboard cleanup clears only the unchanged value written by Balls Server. The QR image is generated in memory and never persisted.
- Rotation disables the grant and closes only separately confirmed attributable sessions, changes the host password, advances the credential revision, and produces a new transfer. It remains disabled until the separate Activate grant operation. Failure after rotation is Repair needed but cannot expose a live undisclosed credential; the erased old password is never recovered.
- Revocation removes group membership and disables the account before optional deletion. It affects one grant. Any attributable open SMB session is listed for explicit closure; the helper never closes all sessions.

### Host identity and endpoint contract

- Generate one opaque product host ID during first Host Files setup. It binds the ledger, share, grants, and audit but is never used as a DNS/SMB name.
- Publish the trusted-LAN endpoint from the current observed Windows computer name and the Tailscale endpoint from the current observed full MagicDNS FQDN. Record share name, path kind, value, and observation time.
- Treat Local and Tailscale as separate connection identities. The client selects one immutable UNC root for authentication and mapping, while the exact provider-supported server credential target is proven and recorded separately. Another host path is never client discovery or retry input.
- Transfer a later endpoint choice only through a separate minimal, non-secret endpoint-update bundle containing schema version, product host ID, grant ID, current credential revision, share name, one newly selected endpoint kind/value, and generation time. The client accepts it only against an existing exact host/grant/revision record and requires explicit Import, preview, and re-verification. It contains no password or alternate endpoint, does not discover or guess a path, is not a remote pairing service, and cannot switch a mapping automatically. If the client has no usable existing grant credential through the proven provider flow, the owner must reissue the credential transfer instead.
- Do not persist IP UNC roots. An explicitly requested advanced IP diagnostic can distinguish name resolution from transport but supplies no credential, creates no SMB session/mapping, and never reports connection verified.
- On name drift, collision suffix, host replacement, or endpoint ambiguity, preserve existing mapping/user state, mark the endpoint stale, re-observe, and require explicit re-verification.

### Client credential, mapping, and verification

- Parse and validate the initial setup code without persistence; it supplies exactly one endpoint. Parse a later endpoint-update bundle through a separate schema, authenticate it by exact equality with the client's existing product host ID, grant ID, and credential revision, and require explicit import and re-verification. Neither path discovers, guesses, or automatically selects an alternate. Observe endpoint resolution/reachability and local mapping/credential collisions before the first authentication attempt.
- Permit one authentication attempt per explicit Check, Connect, Reconnect, or Switch action. Never automatically retry an invalid credential or authenticate on an alternate endpoint.
- Derive and prototype the exact server credential target supported by the Windows provider, then store that exact target only with explicit current-user consent. Record it separately from the full mapping UNC; never assume the share-qualified UNC is the Credential Manager target or use a wildcard. Saving is initially off. After the user selects Save, reconnect-at-sign-in becomes visibly selected by default and can be cleared independently; this deliberate two-step consent implements the approved v0.6 reconnect default while keeping persistence opt-in.
- Authenticate with the helper-observed qualified local account identity (`<host-SAM-authority>\\<grant-account>`), never with an authority inferred from a LAN or MagicDNS endpoint alias. Map only an owner-selected unused drive letter and exact selected UNC. Use in-process Windows APIs; the password never appears in a shell or child-process argument. On another connection using different credentials, stop and identify the named conflict; never evade it with an alias/IP or disconnect all connections.
- Unmap with persistence removal and without force. If open files prevent removal, stop with close-and-retry guidance. Delete only recorded product credential targets; not-found is idempotent success.
- End-to-end authorization verification uses one uniquely named temporary file in the managed folder: create, write random non-secret content, read/compare, rename once, and delete. Never enumerate or alter existing files. If delete fails, report the exact owned temporary path and recovery.

### Audit and privacy

- Write local append-oriented product audit events for security-relevant transitions. Include timestamp, opaque operation/grant IDs, actor SID/category, ledger revision, resource type/stable ID, preview/consent state, before/after non-secret fingerprint, result category, correlation ID, and bounded native error code.
- Exclude passwords, setup codes, credential blobs, QR/clipboard content, raw exception/command output, Tailscale peers/users/keys, and file names/content. Default diagnostic export also redacts account names, host names, UNC roots, IP addresses, and the managed-folder path unless explicitly previewed.
- Do not enable broad Windows file-share/file-access auditing. Existing Windows Security events may be cited as corroboration but are not product ownership proof.
- Keep all evidence local. No telemetry, cloud account, automatic upload, or automatic export is introduced.

## Testing Decisions

### Highest seams

- Make a platform-neutral setup planner and reconciliation engine the primary behavior seam. Given observed state, desired state, ledger revision, and explicit owner choices, it returns a complete immutable preview, refusal, or typed unknown result without mutating Windows.
- Make a platform-neutral operation state machine the primary lifecycle seam. It covers preview, authoritative revalidation, consent, journal preparation, each primitive, verification, cancellation, rollback, repair-needed, recovery, and idempotent replay.
- Make a strict helper-protocol codec/validator the security seam for versioning, size limits, unknown fields, user/session binding, nonce/replay, revisions, timeouts, redaction, and secret-response cardinality.
- Include peer-process authentication and pipe-squatting behavior in that seam: expected client/server PID, token, session, image path, version/hash, signature trust, first-instance creation, process-exit races, and fail-closed evidence.
- Make the product-owned ledger interface the ownership seam. Tests use deterministic in-memory stores and simulated crash points; Windows resource adapters remain behind narrow typed interfaces.
- Make endpoint planning, separate setup-code and endpoint-update parsing, credential-target planning, qualified-account planning, mapping planning, and one-attempt authentication policy pure client seams.

### Default automated suite

- Keep the complete default solution suite unelevated, offline, non-mutating, and runnable without Tailscale, Hyper-V, administrator rights, a share, a second computer, a saved credential, or network access.
- Table-test every operation/refusal combination, managed/unmanaged conflict, stale revision, reconciliation class, crash point, rollback precondition, idempotent repeat, redaction boundary, setup-code variant, endpoint drift/fallback choice, credential collision, and lockout-safe attempt transition.
- Verify that no default production composition references a mutation adapter or launches the helper during preflight/dashboard behavior.
- Preserve the existing 210 read-only tests and production-wiring smoke. v0.3.0 changes documentation and prototypes only, so no new production behavior test is required merely to grep documents.

### Prototype evidence

- Keep the logic prototype isolated from the milestone production branch. Preserve durable evidence through a retained remote prototype reference or immutable milestone copies of the artifact, verifier, and result; a local-only or unspecified throwaway branch is insufficient. The current prototype is one self-contained HTML file with visible state, free-play actions, and guided scenarios.
- The prototype asks whether an owner can understand endpoint selection and safe path switching without automatic fallback. Scenarios cover LAN success, Tailscale success, selected-path failure with alternate ready, Windows/Tailscale name drift, IP diagnostic temptation, persistent mapping replacement, and credential-target collision.
- Validate every guided scenario and illegal transition with the durable state-machine verifier. A visual walkthrough through Computer Use or Browser is additional evidence only when the browser can establish the private local page URL under its safety policy; a URL-policy denial is recorded explicitly and is never represented as a visual pass. The prototype changes no Windows network state.
- Add disposable security prototypes for the helper-owned exact-plan confirmation and peer-authentication handshake, descendant reparse containment/drift, reversible NTFS ACE round-trip, narrow LAN/Tailscale firewall-rule expression, and Credential Manager/WNet exact-target behavior. A failed prototype resolves to refusal, never a broader mutation.

### Mutating integration suite design

- Create a separate opt-in suite in v0.4.0 or later. It requires an explicit mutation flag, elevation, a disposable-VM marker, a known snapshot identifier, a product-test namespace, and preflight proof that no production folder/account/share/credential is in scope.
- Run host mutation cases on a disposable Windows 11 Pro 24H2+ VM with a dedicated temporary NTFS folder. Capture before/after inventories for shares, groups, users, ACEs, firewall rules, SMB values, services, ledger/audit, and test files.
- Interrupt after every primitive to prove journal recovery, narrow rollback, Repair needed behavior, and user-file preservation. Cover unmanaged conflicts, policy/GPO refusal, reparse-root refusal, active sessions, lockout/password-policy errors, ledger loss/corruption, and reboot.
- The default dotnet test command never discovers or runs this suite.

### Two-VM end-to-end design

- Use a host VM and non-administrator client VM on a Hyper-V private/internal switch. Add an optional non-production tailnet leg with policy allowing only client-to-host TCP 445. Never use an external/public SMB route, router port forward, production tailnet identity, or real user files.
- Snapshot clean OS, Tailscale-ready, and configured-host checkpoints. Restore/discard after each mutating case; do not treat best-effort cleanup as isolation.
- Cover LAN endpoint, full MagicDNS endpoint, selected-path failure, explicit switch, rename/collision drift, persistent reconnect, different-credential collision, one-attempt failure, rotate, revoke, open-file removal, leftover verification file, and complete host/client cleanup.
- Assert negotiated SMB 3.0+, SMB1 disabled, signing preserved, private route, share/NTFS intersection, per-grant isolation, exact mapping/credential targets, owned-state reconciliation, unrelated-state preservation, and managed-folder/file survival.

### Manual, UI, and document verification

- v0.3.0 requires no mutating VM execution because production mutation is explicitly out of scope. Its VM gate is a reviewed, executable topology and matrix with prerequisites, isolation guards, fixtures, evidence, and cleanup/reset rules.
- Run the logic prototype through its durable verifier and attempt an interactive walkthrough only when the private local page is accepted by the available UI tool's URL policy. No production WPF UI changes are introduced, so a new Host Dashboard UI smoke is not a v0.3.0 exit requirement; re-run the existing Release smoke only if product UI or composition changes unexpectedly.
- Pressure-test every proposed operation against consent, least privilege, idempotency, ownership, verification, partial failure, rollback, and manual recovery. Any blank cell blocks Verification.
- Publish an operation-by-operation completion matrix that records those eight properties for every Host and Connect operation, including ledger initialization, partial grant creation, endpoint switching, Credential Manager persistence, mapping, and removals. Generic transaction prose does not satisfy the gate when an operation has special recovery behavior.
- Reconcile Product, Architecture, Preflight, testing strategy, glossary, ADRs, active roadmap, spec, tickets, threat model, and research conclusions before owner acceptance.

## Out of Scope

- Any production Windows, account, group, ACL, share, firewall, SMB, service, mapping, Credential Manager, registry, policy, installer, update, or Tailscale mutation.
- A production privileged-helper executable, service, scheduled task, installer technology, executable filename, packaging format, signing certificate, or update channel.
- Taking ownership of a folder, replacing its DACL, adopting an existing share, modifying unmanaged firewall rules, changing machine user-right assignments, or force-closing unrelated sessions/files.
- Public inbound TCP 445, router port forwarding, public SMB endpoints, guest/anonymous/blank-password access, disabled SMB signing, or SMB below 3.0.
- A Balls Server remote pairing service, genuinely expiring/redeemable one-time code, background credential broker, cloud account, relay, telemetry service, or Tailscale credential/ACL management.
- Automatic LAN/Tailscale/IP failover, DNS/hosts/SMB aliases, Windows/Tailscale rename, resolver-policy changes, or silent remapping.
- Implementing Host Files setup, Connect Dashboard, credential persistence, mapping, repair, rotation, revocation, removal, installer, signing, official release, Balls Nodes, or Share Compute.
- Running mutating integration or two-VM end-to-end cases before their later implementation milestones have safe code to exercise.

## Further Notes

- This specification executes the approved v0.3.0 roadmap and does not expand it. The roadmap remains authoritative for milestone state, progress, exit checks, and evidence.
- Status ready-for-agent means the security and testing decisions are defined well enough to split into design-delivery tickets. It does not authorize mutation or imply owner acceptance.
- ADR 0004 records the separate explicit endpoint decision. ADR 0005 records the display-once bearer transfer decision and the rejection of an unauthenticated “expiring code” claim.
- The primary design inputs are the [Windows threat model and operation inventory](research/windows-threat-model-and-operations.md), [access-grant secret lifecycle research](research/access-grant-secret-lifecycle.md), and [canonical endpoint and test research](research/canonical-name-access-path-and-tests.md). Recommendations inferred from platform documentation remain Balls Server design choices and are reviewed against this specification.
- The explicit-endpoint prototype and durable verifier are preserved by the retained remote reference `origin/prototype/v0.3-endpoint-switching` at commit `012a424ba2d8ce23ada4f2b527a2404bbe28d5c0`. All seven guided scenarios, fail-closed transition invariants, and 13 visible controls passed executable validation. Computer Use could not establish the current local-file URL with sufficient confidence, and Browser rejected `file:///` navigation under its URL policy; no visual-pass claim is made and no alternate browser workaround is permitted.
- A later implementation discovery that would add an operation, widen privilege, transmit a new secret, touch an unmanaged object, or weaken an isolation guard returns to a new design decision; it is not absorbed as an implementation detail.
- Owner acceptance of v0.3.0 approves this design as the allowed mutation envelope for later milestones. It does not itself cause a mutation.
