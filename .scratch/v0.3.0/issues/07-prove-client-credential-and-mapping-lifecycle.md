# 07 — Prove client credential, mapping, and verification lifecycle

**What to build:** Define and prototype exact-target setup-code parsing, separate endpoint-update import, endpoint inspection, one-attempt authentication, access verification, Credential Manager, drive mapping, reconnect, switching, collision, cleanup, and host-revocation behavior in the current user boundary.

**Blocked by:** 05 — Validate explicit endpoint identity and switching; 06 — Specify access-grant and display-once secret lifecycle.

**Status:** ready-for-agent

- [x] Initial setup-code validation accepts exactly one selected endpoint; a separate endpoint-update schema accepts one owner-transferred new endpoint only when product host ID, grant ID, and credential revision exactly match existing client state. Import, preview, and re-verification are explicit, with no discovery, guessing, pairing service, alternate in the setup code, or automatic switch.
- [x] Endpoint observation, mapping/drive-letter inspection, and saved-credential collision checks happen before any SMB authentication attempt.
- [x] Each explicit Check, Connect, Reconnect, or Switch action performs at most one exact-endpoint authentication attempt with no retry, alias, IP workaround, or alternate-path fallback.
- [x] Invalid credential, locked account, path unavailable, observation failure, collision, and cleanup failure remain distinct typed recovery categories without username-oracle behavior.
- [x] A disposable-VM provider prototype defines the exact server credential target consumed for the selected endpoint. Credential saving is initially off, requires current-user consent, records that exact provider target separately from the full mapping UNC, and refuses wildcard, guessed, unmanaged, or overwritten targets.
- [x] The host supplies a qualified local account identity using its observed SAM authority and grant account name; authentication never derives the account authority from the LAN name, MagicDNS alias, mapping UNC, or credential target.
- [x] Reconnect-at-sign-in becomes visibly selected by default only after Save is selected and can be cleared independently.
- [x] Mapping requires an unused owner-selected letter and exact UNC; different credentials, existing mapping, used letter, open file, and related Windows connection collisions preserve unrelated work. Credential use and mapping are reserved for a future in-process Windows API seam, and no password appears in shell or child-process arguments.
- [x] Unmap and credential deletion affect only product-recorded exact targets, treat defined not-found cases as success, and never force disconnect or guess a wildcard target.
- [x] Access verification defines creates, writes, reads/compares, renames, and deletes for one unique product temporary file without enumerating or changing existing files; failed cleanup reports the exact owned leftover path through a deliberately private recovery view, not logs/diagnostics.
- [x] A guarded disposable-VM provider/credential harness is defined; development-host/default execution proves guard refusal, while pure planner/state tests cover endpoint-update binding, provider target versus mapping root, qualified local authority, target ownership, switch ordering, rollback limits, collision, and stale client state after host revocation.
- [x] Test-first prototype/guard/redaction checks pass, and no current-user credential or mapping is written on the development host.
- [x] Both review axes pass with every Critical and Important finding resolved.
- [x] The ticket and authoritative roadmap contain exact verification evidence, and the coherent checkpoint is committed and pushed with unrelated work preserved.

## Comments

Implementation checkpoint, 2026-08-14: published the isolated client credential/mapping lifecycle contract, platform-neutral planner/state prototype, focused 10-test suite, denial-only disposable-VM harness, and verifier. The retained RED test failed because the initial planner refused the required inspection-before-one-attempt Connect plan; GREEN passes 10/10. The prototype proves exact one-endpoint setup/update binding, inspection before one attempt, typed non-oracular recovery, exact server provider-target/UNC separation, host-qualified SAM identity, Save/reconnect selection, exact owned cleanup/not-found, private verification leftovers, imported-only switch preservation, host revocation, and development-host guard refusal. No native credential, mapping, SMB, filesystem, process, network, or Windows mutation adapter exists or ran. Review, push, and parity closure remain open.

Bounded review-fix checkpoint, 2026-08-14: RED correctly found that `OtherShare` passed the exact-endpoint path and first Save did not select reconnect. GREEN passes 12/12: endpoint, setup code, endpoint update, and mapping planning now require the fixed `Balls` share; the first Save selection defaults reconnect on, and explicit reconnect clear remains independent. No adapter or host mutation was added. Review, push, and parity closure remain open.

Completion gate, 2026-08-14: the bounded combined reviewer passed both standards/evidence and specification/security axes for `bac4c8bfebae8af601d3e55a23afe1bad4046148`. The focused verifier passes 12/12, Release build has 0 warnings/errors, default tests pass 210/210, and the reviewed sequence is committed for synchronized push.

Pushed-checkpoint closure, 2026-08-14: the complete Ticket 07 sequence was pushed through `574886cc9c986304d7a74cfab51ee899e78a11f2`; fresh fetch proved exact local/upstream equality and divergence `0 0`. Ticket 07 is closed with Ticket 08 next.
