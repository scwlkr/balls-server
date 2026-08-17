# 03 — Connect the client and map a persistent drive

**What to build:** Consume one setup code without elevation, store its limited credential after consent, map a persistent drive, verify a temporary file round trip, and support Disconnect.

**Blocked by:** 01

**Status:** in-progress

**Implementation:** The WPF flow now requires explicit credential-save consent and an available selected drive letter. The Windows service uses Credential Manager and MPR APIs directly, performs one exact-endpoint mapping attempt, verifies one owned temporary file, persists a secret-free client record, and disconnects only the recorded mapping and credential. Disposable-VM and two-computer execution evidence remain.

- [x] Connection validates the exact setup-code endpoint and never substitutes another path.
- [x] The client saves the credential only after consent and never places the secret in process arguments, logs, or normal configuration.
- [x] Mapping uses an available user-selected drive letter and reconnects at sign-in.
- [x] Authentication is attempted at most once per explicit action.
- [x] Verification isolates and removes one temporary file without touching existing content.
- [x] Disconnect removes only the recorded mapping and saved credential.
- [ ] Automated and isolated client tests cover malformed, unreachable, rejected, collision, restart, and cleanup behavior.

## Comments

- 2026-08-17: Ready after Ticket 01 establishes the production contracts.
- 2026-08-17: Background implementation checkpoint passed 232 automated tests. Fakes prove exact endpoint/target use, one mapping attempt, secret-free state, and narrow rollback. The compiled Connect view renders off-screen and exercises drive selection, consent, Connect, status, and Disconnect controls. No Credential Manager, drive, or remote-file mutation was run on the development computer.
