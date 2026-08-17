# 02 — Apply Host Files setup and record ownership

**What to build:** Implement the separate elevated helper for one approved host setup, limited access grant, protected ownership record, setup-code result, and Stop Sharing recovery.

**Blocked by:** 01

**Status:** ready-for-agent

**Implementation:** pending

- [ ] The dashboard sends only a typed, versioned, expiring authorized request to the helper after preview and consent.
- [ ] The helper refuses unsupported folders, unmanaged conflicts, unsafe SMB state, unsafe firewall scope, stale requests, caller substitution, and partial ownership ambiguity.
- [ ] Successful setup creates the approved group/account, NTFS/share permissions, share, and selected private firewall rule with minimum privilege.
- [ ] The protected ledger and setup result contain no plaintext secret outside the approved one-time handoff.
- [ ] Repeated setup is idempotent; failure and cancellation reconcile to a known recoverable state.
- [ ] Stop Sharing removes only product-owned host configuration and preserves the folder and files.
- [ ] Default tests stay unelevated/non-mutating; isolated mutation tests cover real Windows behavior.

## Comments

- 2026-08-17: Ready after Ticket 01 establishes the production contracts.
