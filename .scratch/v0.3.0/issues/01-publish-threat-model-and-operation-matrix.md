# 01 — Publish the threat model and operation matrix

**What to build:** Turn the approved v0.3.0 research and specification into a complete security model and an operation-by-operation completion matrix that proves every future Host Files and Connect to Files action has a narrow, recoverable design.

**Blocked by:** None — can start immediately.

**Status:** ready-for-agent

- [x] The threat model names trust zones, protected assets, data flows, attacker capabilities, assumptions, residual risks, and the permanent refusal invariants.
- [x] The operation inventory has separate rows for preview, authoritative consent, ledger initialization/recovery, prerequisite and folder validation, group, ACE, share, LAN rule, Tailscale rule, grant create/activate/rotate/revoke, attributable-session closure, setup-code secret response, setup-code display, setup-code Hide/timeout destruction, clipboard write, conditional clipboard clear, QR render, repair, host removal, Tailscale handoff, setup-code parsing, endpoint-update import, endpoint inspection/switch/IP diagnostic, one-shot authentication, access verification, credential save/delete, map/unmap, reconnect-profile persistence, verification-file cleanup, and retention purge.
- [x] Every operation has substantive entries for consent, authorization, least privilege, idempotency, ownership proof, verification, partial failure, rollback, manual recovery, audit/redaction, and refusal behavior; no cell is blank or hidden behind generic transaction prose.
- [x] Global SMB, Server-service, security-policy, network-category, user-right, and Tailscale-credential changes remain observation, administrator handoff, or refusal boundaries.
- [x] Exact object-by-object manual recovery is present wherever automatic ownership proof can fail.
- [x] All 86 specification stories and every v0.3.0 roadmap exit check trace to an operation, explicit non-operation, test boundary, or later owner-acceptance step.
- [x] Conflicting older helper/service/consent terminology in the research is reconciled to the one-shot elevated-helper design.
- [x] A deterministic completeness and traceability verifier is written test-first where applicable and passes.
- [x] No production code or Windows, network, credential, account, share, firewall, mapping, policy, or user-file mutation is introduced.
- [x] Both review axes pass with every Critical and Important finding resolved.
- [x] The ticket and authoritative roadmap contain exact verification evidence, and the coherent checkpoint is committed and pushed with unrelated work preserved.

## Comments

Implementation checkpoint, 2026-08-14: published `docs/security/v0.3.0-threat-model.md`, `docs/security/v0.3.0-operation-matrix.md`, and `docs/security/v0.3.0-traceability.md`. Added the deterministic offline verifier `scripts/Verify-V030Task01.ps1`. Test-first RED exited 1 with exactly three missing-artifact findings before those documents existed. GREEN result: `PASS: 39 operation rows, 13 completion columns, 86 stories, 6 exit checks, and the threat-model contract verified.` `dotnet format BallsServer.slnx --verify-no-changes --no-restore` exited 0. `dotnet build BallsServer.slnx -c Release --no-restore` succeeded with 0 warnings and 0 errors. `dotnet test BallsServer.slnx -c Release --no-build --no-restore` passed all 210 tests: Core 126, Presentation 15, Windows 69. The two independent review axes and final pushed-checkpoint criterion remain open for the controller.

Review-fix round 1, 2026-08-14: OP-12 now requires at least 32 cryptographically random bytes generated inside the helper and exactly one policy attempt per explicit owner action. Guest, anonymous, and blank-password SMB are permanent refusals reflected in prerequisite, share, grant, and authentication operations. Stories 07, 13, 43, 75, and 79 plus Exit checks 01 and 04 now have complete semantic targets. The verifier now pins all 39 canonical ID-to-name pairs, semantic phrases, exact target sets and dispositions for all 86 stories and 6 exits, and exact summaries for the review-sensitive rows. Strengthened-verifier RED found 21 expected semantic defects; a deliberate OP-01 relabel then failed with exactly one canonical-name error. Restored GREEN result: `PASS: 39 operation rows, 13 completion columns, 86 stories, 6 exit checks, and the threat-model contract verified.` Formatting exited 0, Release build succeeded with 0 warnings and 0 errors, and all 210 tests passed: Core 126, Presentation 15, Windows 69. Independent re-review and final push remain controller work.

Completion review gate, 2026-08-14: initial implementation `26a244d` and review fix `0b502c7` passed both independent axes. Standards review: PASS. Specification/security review: PASS. All five Important findings were resolved with no new Critical or Important finding. Focused verification passed 39 operation rows, 13 completion columns, 86 stories, and 6 exit checks; Release build remained 0 warnings and 0 errors; all 210 tests passed: Core 126, Presentation 15, Windows 69. No visual, VM-mutation, production-mutation, owner-acceptance, merge, tag, or push claim is made. The final committed-and-pushed checkpoint item remains open; the controller will push immediately after this evidence commit passes scoped review, then prove SHA parity in a separate closure checkpoint.

Pushed-checkpoint closure, 2026-08-14: the controller pushed reviewed evidence commit `c890a494ab3b8f061da6b684216c614fcef2f6b0`. After a fresh fetch, local `HEAD` and `origin/codex/v0.3.0` both equaled that SHA, divergence was `0 0`, and the working tree was clean. This closure record is the final Ticket 01 evidence update. No owner acceptance, merge, tag, visual pass, VM mutation, or production mutation is claimed.
