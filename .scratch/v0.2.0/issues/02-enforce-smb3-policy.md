# 02 — Enforce the SMB 3.0+ policy

**What to build:** Make the Host Files preflight accurately tell the owner whether this computer's Windows SMB server satisfies the approved SMB 3.0-or-newer boundary, while rejecting SMB1 and failing closed whenever the dialect observation cannot be trusted.

**Blocked by:** None — can start immediately.

**Status:** ready-for-agent

**Implementation:** complete — 2026-08-13

- [x] The read-only Windows observation reports Server service state, whether SMB1 is enabled, whether SMB 2/3 service is available, and the minimum and maximum accepted SMB 2/3 dialects.
- [x] Dialects cross the Windows/Core boundary as typed protocol values rather than presentation strings.
- [x] SMB is Ready only when the Server service is running, SMB1 is disabled, SMB 2/3 is enabled, and the accepted range cannot negotiate below SMB 3.0.
- [x] SMB1 enabled, an explicitly disabled prerequisite, a maximum below SMB 3.0, a minimum below SMB 3.0, or a contradictory trusted range produces Action required with a useful reason.
- [x] Missing properties, nulls, malformed output, timeouts, access denial, parser failures, and unrecognized dialect values produce Unknown without assuming Windows defaults.
- [x] Safe evidence explains the observed service and dialect boundary without raw command output, secrets, peer data, or unrelated machine details.
- [x] SMB inspection remains a fixed allow-listed query with bounded execution and has no mutation, elevation, repair, or caller-supplied script path.
- [x] Core policy tests cover the service, SMB1, enablement, minimum, maximum, contradictory-range, missing-data, and precedence matrix.
- [x] Windows adapter tests cover representative valid, reordered, missing, null, malformed, and forward-unknown query output, plus timeout and cancellation behavior.
- [x] The production diagnostic path returns the revised typed SMB result, and the complete non-mutating automated suite remains green.

## Comments

- 2026-08-13: Implemented typed SMB dialect observations and fail-closed SMB 3.0+ policy. Focused policy coverage passed 33 cases; the full Windows adapter suite passed 69 tests, including the read-only production-wiring smoke. The two-axis review found no scope creep, and its coverage, clarity, and documentation-consistency findings were resolved before handoff. Formatting verification and the solution build passed with 0 warnings and 0 errors; all 180 automated tests passed (Core 100, Presentation 11, Windows 69). Full evidence is recorded in `docs/roadmap/v0.2.0.md`.
