# 03 — Connect the client and map a persistent drive

**What to build:** Consume one setup code without elevation, store its limited credential after consent, map a persistent drive, verify a temporary file round trip, and support Disconnect.

**Blocked by:** 01

**Status:** ready-for-agent

**Implementation:** pending

- [ ] Connection validates the exact setup-code endpoint and never substitutes another path.
- [ ] The client saves the credential only after consent and never places the secret in process arguments, logs, or normal configuration.
- [ ] Mapping uses an available user-selected drive letter and reconnects at sign-in.
- [ ] Authentication is attempted at most once per explicit action.
- [ ] Verification isolates and removes one temporary file without touching existing content.
- [ ] Disconnect removes only the recorded mapping and saved credential.
- [ ] Automated and isolated client tests cover malformed, unreachable, rejected, collision, restart, and cleanup behavior.

## Comments

- 2026-08-17: Ready after Ticket 01 establishes the production contracts.
