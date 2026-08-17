# 06 — Specify access-grant and display-once secret lifecycle

**What to build:** Define and prove the per-client Windows identity, password, pending activation, display-once transfer, rotation, revocation, attributable-session, clipboard/QR, redaction, and lost-transfer behavior.

**Blocked by:** 02 — Prove the privileged-helper authorization boundary; 03 — Prove managed-folder, share, and private-network resource safety; 04 — Specify ledger, reconciliation, rollback, and recovery; 05 — Validate explicit endpoint identity and switching.

**Status:** ready-for-agent

- [x] One opaque non-administrator account per intended client profile belongs only to the product group, cannot change its managed password, and remains subject to observed local expiry/lockout/network-logon policy.
- [x] Password creation uses at least 32 cryptographically random bytes, one value per explicit owner action, no automatic policy-guessing retry, and a fresh value after explicit Retry.
- [x] Create and rotate end in Disabled pending transfer; a separate authoritative Activate operation bound to the displayed credential revision is the only way to enable access.
- [x] Lost, hidden, timed-out, crashed, unread, or failed transfers leave no enabled undisclosed credential and recover only by reissue/rotation or revocation, never retrieval.
- [x] The setup code contains the minimal approved fields and exactly one selected endpoint, is correctly described as live bearer material until host rotation/revocation, and never claims physical-device trust or true display expiry.
- [x] Secret IPC is one local initiating user/pipe/nonce-bound response/read; passwords never enter arguments, environment, stdout/stderr, logs, audit, diagnostics, ordinary configuration, ledger, crash attachments, or artifacts.
- [x] Clipboard copy and QR rendering require explicit warned actions; QR is memory-only, and clipboard cleanup clears only the unchanged value Balls Server wrote.
- [x] Rotation, revocation, optional deletion, and attributable-session closure have distinct consent, verification, partial-failure, and manual-recovery behavior and never close every session or restore obsolete access automatically.
- [x] A test-first lifecycle/state-machine prototype, setup-code vectors, one-read IPC cases, buffer/redaction checks, and secret-flow scan all pass without production mutation.
- [x] Both review axes pass with every Critical and Important finding resolved.
- [x] The ticket and authoritative roadmap contain exact verification evidence, and the coherent checkpoint is committed and pushed with unrelated work preserved.

## Comments

Completion review gate, 2026-08-14: bounded standards and specification/security review PASS final checkpoint `4c39ef4de91ebebdf9647a2b46f6255b7d0742c9`. The closure proves opaque helper-owned one-use activation authorization, exact transfer grant/revision binding, atomic one-read concurrency, and internally generated fresh CSPRNG secrets for create/rotate. Focused verification passes 24/24; push/fetch/parity remain open.

Pushed-checkpoint closure, 2026-08-14: the reviewed Ticket 06 sequence and review evidence were pushed through `de2b39ae4f6585909165f66fe220a12893e725cc`; fresh fetch proved exact local/upstream equality and divergence `0 0`. Ticket 06 is closed with Ticket 07 next.

Implementation checkpoint, 2026-08-14: published `docs/security/v0.3.0-access-grant-secret-lifecycle.md`, isolated `.scratch/v0.3.0/prototypes/access-grant-lifecycle*` projects, and `scripts/Verify-V030Task06.ps1`. TDD RED correctly failed against the missing project/API; GREEN passes `19/19`. The model proves the single non-admin/product-group and observed-policy shape; 32-byte cryptographic per-action generation; Disabled create/rotate and exact revision-bound activation; fail-closed lost/hidden/timed-out/crashed/unread/failed transfer; one-endpoint minimal bearer bundle; one initiating-user/session/pipe/nonce/operation/grant/revision-bound read; buffer destruction/redacted public output; warned clipboard/QR behavior; and exact-grant session selection/revocation with no automatic restore. Ticket 01 passes 39 rows/13 columns/86 stories/6 exits; Tickets 02-04 pass 125/125, 302/302, and 140/140; Ticket 05 retained reference remains `012a424ba2d8ce23ada4f2b527a2404bbe28d5c0`. Isolated/solution formats, all verifier PowerShell parses, Release build (0 warnings/errors), default 210 tests, `git diff --check`, production-reference/forbidden-adapter isolation, secret-output, and residue checks pass. No production composition, native pipe/account/clipboard/QR/process/filesystem/network/credential operation, elevation, system mutation, or VM run occurred. Independent review axes and committed-and-pushed parity closure remain open.

Bounded closure fix, 2026-08-14: activation now consumes a fresh authoritative one-use proof bound to operation/grant/current revision; absent, mismatched, and reused proofs refuse. Secret transfer atomically exchanges its one response, so 32 concurrent reads have one winner, and construction rejects setup-code grant/revision mismatch. Create and rotate now generate internally through the CSPRNG seam, accept no caller secret bytes, and record fresh 32-byte generation. Targeted RED was the former API mismatch; GREEN passes `23/23`. Review/push closure remains open.
