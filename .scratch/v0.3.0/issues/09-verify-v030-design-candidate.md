# 09 — Verify the v0.3.0 design candidate

**What to build:** Reconcile every v0.3.0 design artifact and produce a trustworthy Verification candidate whose security, privacy, architecture, recovery, and test contracts are complete enough for one explicit owner-acceptance decision.

**Blocked by:** 01 — Publish the threat model and operation matrix; 02 — Prove the privileged-helper authorization boundary; 03 — Prove managed-folder, share, and private-network resource safety; 04 — Specify ledger, reconciliation, rollback, and recovery; 05 — Validate explicit endpoint identity and switching; 06 — Specify access-grant and display-once secret lifecycle; 07 — Prove client credential, mapping, and verification lifecycle; 08 — Publish the disposable VM verification topology.

**Status:** needs-triage

- [x] Glossary, ADRs, Product, Architecture, Preflight, testing strategy, threat model, research, specification, tickets, active version roadmap, and compact roadmap all describe one consistent design and scope.
- [x] All 86 stories and every roadmap remaining-work item/exit check are proven by durable traceability and evidence rather than merely restated.
- [x] Cross-document checks find no conflicting helper/service, elevation/consent, guest/public SMB, endpoint/fallback, secret/expiry, retry/lockout, ownership, recovery, mutation, signing, or test-isolation claim.
- [x] The production tree still contains no v0.3 system mutator, helper/service, account/share/ACL/firewall/mapping/credential write path, guest access, public TCP 445 path, telemetry, or data deletion behavior.
- [x] Secret/artifact scans, Markdown/link/traceability/matrix verifiers, prototype verifiers, guard-denial tests, and any applicable UI evidence pass with limitations stated exactly.
- [x] Restore, formatting verification, Release build, complete automated suite, and production-wiring smoke pass with exact counts and zero-warning/error evidence.
- [x] Both final code-review axes pass with every Critical and Important finding resolved and no scope creep.
- [x] Every ticket and the authoritative roadmap record current evidence; immediately before this local candidate, `codex/v0.3.0` exactly matched `origin/codex/v0.3.0` and local `main` plus `origin/main` still equaled `c7b2957`. The controller must push this candidate and reconfirm parity.
- [x] The milestone state is `Verification`, not Complete, and the completion/tag check remains open pending one explicit owner acceptance.
- [x] Merge to `main`, post-merge local/upstream `main` parity, the `Complete` state, and creation/push of tag `v0.3.0` are explicitly reserved for the orchestrator after owner acceptance and are not claimed by this ticket.
- [x] The owner-facing acceptance prompt states exactly what approval permits and does not do; the exact text is in the Task 09 report.

## Comments

Verification candidate, 2026-08-14: the complete reconciled non-mutating gate passed. Task 01 verified 39 operation rows, 13 completion columns, 86 stories, and 6 exit checks; Tasks 02–04 passed 125, 302, and 140 isolated tests; retained Task 05 evidence passed seven scenarios, 13 owner-interactive controls, and 11 endpoint-update binding/refusal cases; Tasks 06–08 passed 24, 12, and 15 isolated tests. The full solution and all isolated prototype/test projects passed `dotnet format --verify-no-changes`; the only direct release inconsistency was required CRLF line endings in five isolated Ticket 06/07 source files, mechanically normalized. All v0.3 verifier PowerShell files parse cleanly. Release build: 0 warnings, 0 errors. Default non-mutating suite: Core 126, Presentation 15, Windows 69, total 210. Production mutation/helper/telemetry/deletion, secret-artifact, composition, diff, and temporary-residue scans passed. Fresh pre-candidate fetch established feature parity at `832acde9b8f063733a74f9bc10aaa6b31a7be097` with divergence `0 0`; both `main` refs remain `c7b29571facd259a4b453dfd11b9e156f0ceab3d`. Private-file URL policy still denies visual walkthrough, so no visual pass is claimed. This local candidate now awaits the controller's two review axes and push; owner acceptance, merge, `Complete`, and tag remain reserved.

Final review gate, 2026-08-14: independent standards/evidence and specification/security reviewers passed the complete candidate after the docs-only state correction `763f59507fa1838bfe259808ddce92c93096216c`. The sole reported blocker was stale `In progress` wording in Product; Product, both roadmaps, Ticket 09, and the report now consistently say `Verification`. No other Critical or Important finding remains. The reviewed candidate is ready for synchronized push and one owner-acceptance decision.

Synchronized Verification closure, 2026-08-14: the reviewed candidate and final-review evidence were pushed through `6eadc72f2961e885fa1174b42ea3f3e034772657`. Fresh fetch proved feature `HEAD == origin/codex/v0.3.0` with divergence `0 0`; local `main == origin/main == c7b29571facd259a4b453dfd11b9e156f0ceab3d`. Ticket 09 is closed at `Verification`; only explicit owner acceptance may authorize merge, Complete, and tag.

Owner acceptance, 2026-08-15: the owner explicitly accepted v0.3.0. The orchestrator is authorized to merge the reviewed candidate, mark the milestone Complete, create and push tag `v0.3.0`, and prove final parity. This acceptance authorizes no current Windows mutation, repository publication, or official packaged release.

Final-candidate correction, 2026-08-14: `docs/PRODUCT.md` now states the same `Verification` milestone state as both authoritative roadmap files. No behavior or scope changed; targeted link, formatter, and diff checks were rerun.
