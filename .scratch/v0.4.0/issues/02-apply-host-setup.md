# 02 — Apply Host Files setup and record ownership

**What to build:** Implement the separate elevated helper for one approved host setup, limited access grant, protected ownership record, setup-code result, and Stop Sharing recovery.

**Blocked by:** 01

**Status:** in-progress

**Implementation:** Typed expiring request/response protocol, current-user named-pipe coordinator, separate UAC helper and approval window, generated limited credential, fixed stdin-only PowerShell mutation plan, rollback path, protected ownership ledger, and setup-code response are implemented. Stop Sharing, crash reconciliation, and disposable-VM execution evidence remain.

- [x] The dashboard sends only a typed, versioned, expiring authorized request to the helper after preview and consent.
- [ ] The helper refuses unsupported folders, unmanaged conflicts, unsafe SMB state, unsafe firewall scope, stale requests, caller substitution, and partial ownership ambiguity.
- [ ] Successful setup creates the approved group/account, NTFS/share permissions, share, and selected private firewall rule with minimum privilege.
- [x] The protected ledger and setup result contain no plaintext secret outside the approved one-time handoff.
- [ ] Repeated setup is idempotent; failure and cancellation reconcile to a known recoverable state.
- [ ] Stop Sharing removes only product-owned host configuration and preserves the folder and files.
- [ ] Default tests stay unelevated/non-mutating; isolated mutation tests cover real Windows behavior.

## Comments

- 2026-08-17: Ready after Ticket 01 establishes the production contracts.
- 2026-08-17: Background-only implementation checkpoint: the real WPF helper view renders off-screen, request/response frames round-trip with strict bounds, setup-code and command diagnostics redact secrets, the fixed PowerShell plan passes the PowerShell parser without execution, and process arguments contain neither the managed-folder path nor credential. No Windows mutation was run on the development computer.
- 2026-08-18: Fixed the supported-Windows SMB dialect adapter after the live CIM provider returned decimal strings instead of symbolic names. TDD RED failed all eight numeric dialect cases; GREEN passes all 27 focused SMB parser/probe tests. On the owner-authorized development host, the repaired probe first identified the unrestricted minimum as `smb_minimum_below_3`; an explicit elevated `Set-SmbServerConfiguration -Smb2DialectMin SMB300` hardening left SMB1 disabled and made Computer, Managed folder, and Local access Ready on two consecutive production-composition runs. Format verification, Release build with zero warnings/errors, all 241 automated tests, Gitleaks, and the NuGet vulnerability audit pass. No share, account, folder, ACL, firewall, or file mutation occurred.
- 2026-08-18: Live setup diagnosis found two sequential Windows PowerShell process-boundary failures: redirected JSON carried a UTF-8 BOM that Windows PowerShell 5.1 rejected, then its inherited PowerShell 7 `PSModulePath` prevented loading the signed built-in security module. Separate real-process TDD tests failed before each fix and now pass with BOM-less UTF-8 plus removal of the inherited module path. An elevated forced-rollback run then traversed the complete real group/account/ACL/share/firewall/ledger plan to the intentional full-success checkpoint and removed every diagnostic object. Temporary instrumentation and results were deleted; the managed folder remained. Format verification, Release build with zero warnings/errors, and all 243 automated tests pass.
