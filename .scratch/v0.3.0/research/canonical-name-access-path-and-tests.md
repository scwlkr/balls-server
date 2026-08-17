# Canonical host name, access-path fallback, and test seams

**Purpose.** Resolve the v0.3.0 question without authorizing implementation or
system mutation. This note distinguishes sourced platform facts, questions for
isolated prototypes, and product recommendations. It assumes the product
boundary in `CONTEXT.md` and `docs/PRODUCT.md`: authenticated SMB 3.0+ only,
SMB1 disabled, and a trusted LAN or private Tailscale path.

## Decision summary

Use a stable internal host record, but make the user-visible SMB endpoint
**path-specific**:

| Path | Published SMB root | Do not substitute automatically |
| --- | --- | --- |
| Trusted LAN | `\\<current-Windows-computer-name>\\<managed-share>` | MagicDNS name or a LAN IP literal |
| Private Tailscale | `\\<current-machine>.<tailnet>.ts.net\\<managed-share>` | LAN short name or a Tailscale IP literal |

The two roots name the same physical host, but they are not interchangeable
connection identities. Record the selected root when a client is paired or a
drive is mapped. At reconnect, retry that recorded root only. If it is not
usable, report that the owner can transfer a separate endpoint update; do not
discover, guess, or embed an alternate in the initial setup code. The minimal
update is bound to the client's existing product host ID, grant ID, and
credential revision, contains one newly selected endpoint, and requires
explicit import, preview, and re-verification before a remap. It is not a
pairing service and never silently rewrites a mapping or saved credential.

The main open product choice is the LAN primary: the Windows computer name is
the least-invasive v1 option, but it is a single-label name whose resolution
depends on the customer LAN. Do not create DNS aliases, change the computer
name, or alter NetBIOS/LLMNR policy in v0.x. A future managed DNS offering
would be a separately approved product-owned configuration feature, not a
fallback.

## Sourced platform facts

### Windows and SMB naming

1. Modern direct-hosted SMB uses TCP 445, and Microsoft recommends DNS for
   file-and-printer-sharing name resolution. NetBIOS over TCP is associated
   with older SMB/CIFS transport, not a requirement for SMB 2.0.2 and later.
   [Microsoft: Direct host SMB over TCP/IP](https://learn.microsoft.com/en-us/troubleshoot/windows-server/networking/direct-hosting-of-smb-over-tcpip)

2. A single-label Windows name is not a deterministic DNS-only contract. On
   non-domain networks, Windows can issue parallel or reordered DNS, LLMNR,
   and NetBIOS-over-TCP queries; the policy documentation states that binding
   order can decide between multiple positive responses. Windows also applies
   configured DNS suffixes to a single-label name. A product cannot infer from
   `\\host\\share` alone which mechanism answered it.
   [Microsoft: Smart multi-homed name resolution](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-admx-dnsclient),
   [Microsoft: DNS queries and lookups](https://learn.microsoft.com/en-us/windows-server/networking/dns/queries-lookups)

3. The Windows DNS host name and NetBIOS name are related but not equivalent:
   Microsoft documents that a NetBIOS name may be truncated from the DNS host
   name. Therefore a product must query/display the actual Windows computer
   name rather than deriving a NetBIOS name from its own display label.
   [Microsoft: `COMPUTER_NAME_FORMAT`](https://learn.microsoft.com/en-us/windows/win32/api/sysinfoapi/ne-sysinfoapi-computer_name_format)

4. An arbitrary DNS CNAME is not a harmless SMB alias. Microsoft documents SMB
   failures through a CNAME when the server configuration is hardened or the
   alias SPN is absent, and recommends a Windows computer-name alias rather
   than configuring the file server with a DNS CNAME. This rules out using a
   Balls Server-branded CNAME as an easy canonical name.
   [Microsoft: SMB file-server CNAME failure](https://learn.microsoft.com/en-us/troubleshoot/windows-server/networking/dns-cname-alias-cannot-access-smb-file-server-share)

### Tailscale naming and reachability

5. Tailscale gives each device a unique Tailscale IP and MagicDNS name;
   MagicDNS creates a per-device FQDN and adds a search domain so a machine
   name can normally be used as a short name. The FQDN is the unambiguous
   publishable value: `<machine>.<tailnet>.ts.net`.
   [Tailscale: MagicDNS](https://tailscale.com/docs/features/magicdns),
   [Tailscale: connect to devices](https://tailscale.com/kb/1452/connect-to-devices)

6. A Tailscale machine name is unique within its tailnet, but it commonly
   starts from the OS hostname. A collision receives a suffix (for example,
   `host-1`), and a renamed machine changes its MagicDNS name. With automatic
   name generation, an OS hostname update can change the machine name on the
   next Tailscale start. It is therefore unsafe to store a guessed name before
   asking Tailscale for the current published identity.
   [Tailscale: machine names](https://tailscale.com/kb/1098/machine-names)

7. A shared Tailscale machine must be addressed by its full MagicDNS name from
   the recipient tailnet. That is a useful negative test even if the product
   initially supports only a private owner tailnet: a short name must not be
   presented as universally valid.
   [Tailscale: sharing and MagicDNS](https://tailscale.com/kb/1084/sharing)

8. Tailscale supplies private network reachability; it does not start or
   authorize the destination service. A reachable Tailscale address therefore
   does not prove that Windows Server accepts the SMB login or that the access
   grant has authorization.
   [Tailscale: connect to devices](https://tailscale.com/kb/1452/connect-to-devices)

### Mapping and credential persistence

9. Windows SMB mappings have distinct `Persistent` and `SaveCredentials`
   controls. Persistent mappings can survive restart; saved credentials are
   reused for mappings against the same server. `New-SmbMapping` therefore
   makes the chosen server/root an enduring user-state decision, not a safe
   per-attempt routing hint.
   [Microsoft: `New-SmbMapping`](https://learn.microsoft.com/en-us/powershell/module/smbshare/new-smbmapping?view=windowsserver2025-ps),
   [Microsoft: MSFT_SmbMapping Create](https://learn.microsoft.com/en-us/previous-versions/windows/desktop/smb/msft-smbmapping-create)

10. `net use` similarly supports `/persistent` and `/savecred`, and says
   deviceless connections are not persistent. Any connection-verification
   mechanism that creates a drive or saves a credential is a mutation and
   belongs only in the later explicit, disposable integration suite.
   [Microsoft: `net use`](https://learn.microsoft.com/en-gb/previous-versions/windows/it-pro/windows-server-2012-r2-and-2012/gg651155%28v%3Dws.11%29)

11. For a domain-password credential, Windows Credential Manager's
    `TargetName` identifies the server or servers and its `UserName` is a
    qualified `DomainName\\UserName` or UPN. The server credential target is
    therefore a distinct provider input from the share-qualified mapping UNC;
    its exact supported form must be proved rather than assumed.
    [Microsoft: `CREDENTIAL`](https://learn.microsoft.com/en-us/windows/win32/api/wincred/ns-wincred-credentialw)

12. Every Windows computer is the security authority for its local SAM.
    `LookupAccountName` returns the computer name as the referenced domain for
    a non-domain-joined computer and recommends qualified
    `domain_name\\user_name` input. A host-local access account must carry its
    observed SAM authority independently of any LAN or MagicDNS endpoint alias.
    [Microsoft: local accounts](https://learn.microsoft.com/en-us/windows/security/identity-protection/access-control/local-accounts),
    [Microsoft: `LookupAccountName`](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-lookupaccountnamew)

## Product recommendations

### Canonical model

- **Internal identity:** generate an opaque product host ID during Host Files
  setup. It binds the ownership ledger, share, access grants, and audit record;
  it is never an SMB address and never depends on DNS, IP, or a display name.
- **Endpoint snapshot:** at setup and every successful connection verification,
  store the observed Windows computer name, actual Tailscale MagicDNS FQDN,
  share name, observation time, and selected path. Treat endpoint data as
  drift-prone, non-secret diagnostics; redact it from default exports if the
  privacy design classifies host names as sensitive.
- **Endpoint-update transfer:** the initial credential-transfer setup code
  contains exactly one selected endpoint. A later change uses a separate,
  minimal non-secret bundle containing schema version, product host ID, grant
  ID, current credential revision, share name, one new endpoint kind/value,
  and generation time. The client accepts it only against its existing exact
  host/grant/revision, then requires explicit import and re-verification. It
  includes no alternate or password, performs no discovery/guessing/pairing,
  and cannot switch state automatically.
- **LAN endpoint:** publish the current Windows computer name only, shown as a
  convenience in `\\name\\share`. Label it “Trusted local network”; do not
  claim DNS, LLMNR, or NetBIOS was used. A failed resolution is an actionable
  local-path failure, not permission to alter resolver policy.
- **Tailscale endpoint:** publish only the actual full MagicDNS FQDN for setup
  codes, QR/clipboard copy, and durable client records. A short MagicDNS name
  may be displayed as a convenience only after the full name is present.
- **No additional aliases:** no CNAME, hosts-file entry, SMB OptionalName,
  computer rename, NetBIOS change, LLMNR change, or Tailscale hostname change
  is created by Balls Server. Each has separate ownership, collision, SPN,
  policy, and rollback implications.

### Fallback and IP policy

- **Never automatic between paths.** A connection/mapping selected for LAN is
  not silently retried on Tailscale, and vice versa. The app can diagnose both
  paths independently on the host, but the client receives a new path only
  through the owner-transferred endpoint-update bundle. After explicit import,
  the action must show the new UNC root, the separately proven provider
  credential-target change, confirm whether it will replace a persistent
  mapping, and run the normal SMB authentication/authorization verification.
- **No automatic IP UNC fallback.** Do not create, persist, copy, or silently
  test `\\<IPv4-or-IPv6-literal>\\share` as the alternative access root. It
  conceals which network is being used, can go stale on LAN DHCP changes, and
  bypasses the name-specific behavior whose SMB alias/SPN constraints Microsoft
  documents. This is a product security/reliability recommendation inferred
  from the sourced name and mapping behavior, not a claim that Windows cannot
  accept an IP UNC.
- **Manual diagnostic exception:** after an explicit “advanced diagnostic” user
  action, a temporary IP test may be useful for distinguishing name resolution
  from TCP reachability. It must not store credentials, create a mapping, claim
  that access is verified, or be offered as a recovery path. The result should
  say “transport observation only.”
- **Authentication remains separate:** a successful DNS/MagicDNS lookup or
  TCP 445 reachability is only a route observation. Ready connection
  verification requires an SMB session to the selected published UNC using the
  limited access grant, plus the expected write/read authorization test in the
  future mutation suite.

### Collision, rename, and drift behavior

- Refuse setup if the selected managed-share name already exists and is not
  proven product-owned. Do not take over an existing share, mapping, CNAME, or
  host name.
- The product may choose a share-name collision strategy only within a
  documented product-owned namespace. It must not resolve a collision by
  renaming the Windows computer or Tailscale machine.
- On Windows-name or MagicDNS-name drift, leave existing mappings untouched,
  mark the recorded endpoint stale, and require an explicit re-verification
  before changing product records or client mappings. Explain that a Tailscale
  collision may have added a suffix.
- A device replacement with the same friendly name is a new host until its
  product-owned identity and access grants are re-established. Name equality is
  never sufficient to inherit a share or credential.

## Prototype questions and isolated prototypes

These are experiments, not product code and not a mandate to change a customer
machine. Use two disposable Windows 11 Pro 24H2+ VMs and revert the snapshot
after each case.

| Question | Minimal isolated prototype | Evidence to capture | Accept/reject rule |
| --- | --- | --- | --- |
| Does the current Windows computer name resolve from the client on the private LAN, and by what mechanism? | Same workgroup, private internal vSwitch, host Server service and a temporary test SMB share. From client, resolve/attempt `\\host\\share`; repeat with DNS disabled only if the VM image permits it. | Name lookup results, UNC result, negotiated dialect/signing, no secrets. | Product may display the short LAN name only as convenience; do not infer a resolver or add a fallback. |
| Does the same share work through MagicDNS FQDN with an approved local limited account? | Add both VMs to a non-production tailnet; use `\\machine.tailnet.ts.net\\share`. | Exact FQDN, Tailscale ACL allow/deny result, SMB auth result, negotiated dialect/signing. | FQDN is usable only when Tailscale and SMB tests both succeed. |
| What happens when OS and Tailscale names diverge or collide? | Rename the Tailscale machine once, then create/cause a duplicate name in the isolated tailnet; separately rename the Windows host. | `tailscale status --json` relevant self identity, observed old/new UNC results, persistence behavior. | Detect drift; never guess, alias, or auto-remap. |
| Does a persistent mapping preserve its original root and credential behavior across restart? | Map a drive with explicit consent in the VM, once per LAN and FQDN root; restart client; remove the map and saved test credential in teardown. | Mapping root, reconnect result, Credential Manager presence (name only), user-visible drive letter. | Switching endpoint requires explicit replacement; teardown must remove only the test-owned mapping/credential. |
| Can an IP UNC provide a useful transport-only discriminator without becoming an authentication workaround? | Attempt IP UNC only after equivalent named tests, with no `/savecred` or persistent mapping. | Route, SMB auth outcome, and clear separation of lookup vs login. | If it changes auth/session behavior or risks credential reuse, retain it only as a lab diagnostic, not product UI. |
| How are mappings and sessions affected by name variants? | In a disposable client, create a controlled deviceless named session then try the alternate LAN/Tailscale endpoint with the same limited grant. | Exact Windows error/result and `Get-SmbMapping`/connection state, no secret values. | Do not encode assumed “same server” behavior; model endpoints as distinct until verified. |

## Existing seams and the highest-value new seams

### Existing seams to preserve

| Existing seam | Current evidence | Why it matters |
| --- | --- | --- |
| Core probe contracts | `src/BallsServer.Core/Preflight/ProbeContracts.cs` injects all Windows observations as small interfaces. | New endpoint observations can be deterministic `ProbeResult<T>` values and preserve `Unknown` on environmental failure. |
| Policy/orchestration | `PreflightService` turns exceptions into typed `Unknown` and independently reduces Local/Tailscale aggregates. | Path selection must be a new application concern; it must not collapse two independently ready paths into an auto-fallback. |
| Tailscale adapter/parser | `TailscaleStatusSource` is behind `ITailscaleStatusSource`; parser tests already exclude peer/user data. | Extend a narrowly scoped self-identity result, not the preflight's raw status or sensitive peer/user data. |
| Windows process boundary | `BoundedReadOnlyProcessRunner` bounds no-shell read-only queries. | A future *observation* command can use the same pattern; mapping/setup commands must not be added to it. |
| Test doubles and smoke test | Core stubs and `WindowsPreflightSmokeTests` already protect deterministic default tests and a real non-mutating shape test. | New default tests can be deterministic; no second device, Tailscale account, share, or stored credential belongs in `dotnet test`. |

### Proposed new seams (design only)

1. `IHostEndpointProbe` — read-only observation returning an explicit
   `HostEndpointObservation` with `WindowsComputerName` and a validation state.
   Keep it independent of the existing eight-check catalog until the approved
   UI/report contract changes. A fake supplies valid, malformed, unavailable,
   renamed, and collision-looking values.

2. `ITailscaleIdentitySource` — separate from `ITailscaleStatusSource`, returns
   only this node's current machine name and MagicDNS FQDN (or unavailable).
   The parser must reject missing/non-string/oversized values and never expose
   peers, users, node keys, or Tailscale IPs by default. Prototype first that
   the supported `tailscale status --json` surface has the required self field;
   if it does not, stop rather than scrape UI output.

3. Pure `AccessEndpointPlanner` — input: path readiness, endpoint observations,
   share name, and an explicit user choice; output: `Ready`, `Action required`,
   or `Unknown` plus one immutable UNC root. It cannot run a command, initiate
   a network connection, or choose a second path. Unit-test the complete table
   of missing, malformed, drifted, collision, and alternate-ready combinations.

4. Pure setup-code and endpoint-update parsers — keep the credential-bearing
   initial schema separate from the non-secret update schema. The latter
   accepts one endpoint only when host ID, grant ID, and credential revision
   match existing client state and cannot initiate discovery or switching.

5. `IClientMappingInspector` (read-only) and `IConnectionVerifier` (future
   mutation) — the former inventories only product-owned candidate mappings;
   the latter belongs behind the privileged/client setup workflow and has a
   test implementation only in disposable VMs. Neither should be attached to
   the dashboard preflight factory.

6. `IProductChangeLedger` — a pure contract for the host/access-grant/mapping
   ownership records, including recorded endpoint root and prior root. It makes
   rename/reconnect/rollback behavior testable without Windows mutation and
   lets repair refuse anything not proven owned.

## Disposable-VM topology and mutation matrix

### Topology

```text
Hyper-V private/internal vSwitch (no external switch, no port forwarding)

  CLIENT-VM  ---- private LAN ----  HOST-VM
      |                                  |
      +----- non-production tailnet -----+
                  (optional test leg)

Snapshots: clean OS -> Tailscale signed in -> test-host configured
```

- `HOST-VM`: Windows 11 Pro 24H2+, a dedicated temporary NTFS folder and a
  unique test share/access-grant identity. No production files or accounts.
- `CLIENT-VM`: Windows 11 Pro 24H2+, a disposable non-administrator test
  profile. It is the only VM that creates persistent mappings or stores the
  test credential.
- Tailnet: private, non-production identities and an explicit policy allowing
  only `CLIENT-VM -> HOST-VM:445`; never an external vSwitch, public address,
  router rule, cloud firewall rule, or guest SMB.
- Restore/discard both VMs after every mutating case. Capture only redacted
  command outcome, negotiated security facts, and product-owned ledger IDs.

| Suite / VM state | Operation under test | Required assertions | Mutation / cleanup |
| --- | --- | --- | --- |
| Default unit tests (developer machine) | Planner, separate setup/update parsers, ledger, drift/fallback decisions | Deterministic explicit path selection and exact host/grant/revision binding; no process/mapping/credential/network dependency | None |
| Read-only production smoke (developer machine) | Existing eight-check report; optional endpoint observation shape after approval | Stable report/check shape; unavailable data is `Unknown`; no fixed endpoint assertion | None |
| Sandbox UI smoke | Presentation of independent paths and alternate-path action availability | No setup/mapping action runs; focus/error copy is clear | None |
| Two VMs, private LAN | Create product-owned share/grant, verify named LAN UNC, map/unmap | SMB 3.0+, SMB1 disabled, signing preserved; correct grant can read/write; wrong/revoked grant fails; no takeover | Restore snapshots; assert test folder contents and unrelated shares/ACLs unchanged |
| Two VMs, private tailnet | Same operation through full MagicDNS FQDN | Tailnet policy and SMB auth are independently evidenced; no public 445; short/FQDN behavior recorded | Restore snapshots; remove only test mapping/credential if not restoring |
| Two VMs, failover/drift | LAN unavailable while Tailscale ready, then inverse; Windows/Tailscale rename/collision | Existing mapping stays on recorded root; no client discovery occurs; explicit owner-transferred update import/reverify changes exactly one owned mapping and separately recorded provider credential target | Restore snapshots; verify no alias/DNS/hosts/NetBIOS changes |
| Two VMs, partial failure/rollback | Interrupt after share, ACL, grant, mapping, and credential steps | Ledger records ownership; rollback/recovery affects only owned changes; managed folder/user files survive | Restore snapshots after evidence; compare before/after unrelated state |

## Guardrails for the implementation milestone

- Do not add endpoint probing, mapping, or verification to the v0.1/v0.2
  dashboard's eight-check contract without an approved report/version design.
- A map drive, connection verification with credentials, Credential Manager
  write, share/ACL change, host rename, DNS/hosts edit, or Tailscale rename is
  a mutation. Keep it out of default tests and all unelevated read-only paths.
- Use full MagicDNS FQDN as the durable Tailscale value. Keep the full value in
  logs only if the privacy/audit design explicitly permits it; never log the
  grant secret or credential material.
- Treat success at TCP 445 or name resolution as insufficient for connection
  verified. Verification needs the limited identity's real SMB authorization on
  the exact selected root.
- Prove the Windows provider's exact server credential target separately from
  the full mapping UNC. Authenticate with the helper-observed qualified local
  SAM identity, not an authority inferred from the LAN or MagicDNS alias, and
  never place a password in shell or child-process arguments.

## Evidence reviewed

- `AGENTS.md`, `CONTEXT.md`, `docs/PRODUCT.md`, `docs/ARCHITECTURE.md`,
  `docs/testing/README.md`, `docs/ROADMAP.md`, and `docs/roadmap/v0.3.0.md`.
- Current Core, Windows, Presentation, and test-project seams, especially the
  preflight probe contracts, `PreflightService`, Tailscale status adapter,
  bounded read-only runner, test doubles, and production smoke test.
