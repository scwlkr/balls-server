# 08 — Publish the disposable VM verification topology

**What to build:** Produce the executable boundary, topology, guard, fixture, matrix, evidence, and reset design for future default, opt-in host-mutation, and two-VM private-network verification suites.

**Blocked by:** 02 — Prove the privileged-helper authorization boundary; 03 — Prove managed-folder, share, and private-network resource safety; 04 — Specify ledger, reconciliation, rollback, and recovery; 05 — Validate explicit endpoint identity and switching; 06 — Specify access-grant and display-once secret lifecycle; 07 — Prove client credential, mapping, and verification lifecycle.

**Status:** ready-for-agent

- [x] Default tests remain unelevated, offline, deterministic, non-mutating, and independent of Tailscale, Hyper-V, shares, accounts, mappings, credentials, and a second computer.
- [x] Opt-in mutation requires elevation, a disposable-VM marker, exact known snapshot ID, unique product-test namespace, and proof that no production folder, account, share, credential, mapping, or tailnet identity is in scope.
- [x] The host/client topology uses supported disposable Windows VMs on private/internal networking and an optional non-production tailnet leg that allows only client-to-host TCP 445; no public/external SMB route or router forwarding exists.
- [x] Clean, Tailscale-ready, and configured snapshots, fixtures, before/after inventories, primitive interruption points, evidence format, cleanup expectations, and mandatory restore/discard rules are exact.
- [x] The matrix covers every earlier operation with positive, refusal, partial-failure, crash, rollback, repair, recovery, idempotent, drift, privacy, and unrelated-state-preservation cases; no cell is blank.
- [x] End-to-end rows cover LAN, full MagicDNS, selected-path failure, explicit switch, rename/collision drift, reconnect, credential collision, one-attempt failure, rotate, revoke, open-file removal, leftover verification file, ledger loss/corruption, reboot, and complete host/client cleanup.
- [x] Assertions include SMB 3.0+, SMB1 disabled, signing preserved, private route, share/NTFS intersection, per-grant isolation, exact mapping/credential targets, ownership reconciliation, unrelated-state preservation, and managed-folder/file survival.
- [x] Schema/matrix validation and guard-denial tests are written test-first where applicable and pass; the unavailable local Hyper-V command is recorded without claiming a VM run.
- [x] v0.3.0 explicitly does not claim a mutating VM execution; the deliverable is the reviewed executable topology for later safe adapters.
- [x] Both review axes pass with every Critical and Important finding resolved.
- [x] The ticket and authoritative roadmap contain exact verification evidence, and the coherent checkpoint is committed and pushed with unrelated work preserved.

## Comments

Implementation checkpoint, 2026-08-14: published `docs/testing/v0.3.0-disposable-vm-topology.md`, an isolated platform-neutral topology/schema/guard prototype, 14 focused tests, and `scripts/Verify-V030Task08.ps1`. TDD RED was `Published_contract_is_complete` at 0/1 against the deliberately incomplete contract; GREEN is 14/14. The guard denies missing elevation, disposable marker, exact configured snapshot, unique `BallsServer.Test.` namespace, or test-only scope proof before any adapter. The executable 39 by 11 nonblank operation matrix, exact clean/Tailscale-ready/configured snapshots, private/internal two-VM and optional non-production client-to-host TCP 445 tailnet legs, interruption/reset/evidence contract, all required end-to-end rows, and safety assertions are validated. `Get-VM` is unavailable in this session; no Hyper-V, VM, native adapter, SMB, account, credential, filesystem, network, or Windows mutation was attempted or claimed. Review and push/parity closure remain open.

Bounded review fix, 2026-08-14: RED reproduced the scope-proof blocker at 14 passed/1 failed because the fixed `Balls` share was rejected despite valid elevation, disposable marker, exact configured snapshot, unique namespace, and every other namespaced test-only scope object. GREEN passes 15/15: `Balls` alone is permitted as the fixed share while every other unnamespaced share remains refused. No VM or host mutation was added. Review and push/parity closure remain open.

Completion gate, 2026-08-14: the bounded combined reviewer passed both standards/evidence and specification/security axes for `7209960`. The focused verifier passes 15/15, Release build has 0 warnings/errors, default tests pass 210/210, and the reviewed sequence is committed for synchronized push.

Pushed-checkpoint closure, 2026-08-14: the complete Ticket 08 sequence was pushed through `6eb00771cb16fa394ffbc10587d97210c399be80`; fresh fetch proved exact local/upstream equality and divergence `0 0`. Ticket 08 is closed with Ticket 09 next.
