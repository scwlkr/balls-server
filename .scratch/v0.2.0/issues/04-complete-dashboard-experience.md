# 04 — Complete the dashboard experience

**What to build:** Finish the simple Host Dashboard so the owner can distinguish readiness from actual Balls Server hosting, understand blockers without technical overload, inspect safe evidence when needed, and operate the interface with a mouse, keyboard, or screen reader.

**Blocked by:** 03 — Present structured Host Files readiness.

**Status:** ready-for-agent

**Implementation:** complete — 2026-08-14

- [x] The summary-first layout gives Computer, Managed folder, Local access, Tailscale access, and Hosting state clear, balanced placement without recreating a combined access-path result.
- [x] Hosting state displays Not configured as a neutral domain state, never as a prerequisite failure, and does not claim a managed share or verified client connection exists.
- [x] No existing Windows share is detected, adopted, modified, or described as Balls Server-owned.
- [x] Each summary area offers expandable details containing its individual prerequisite results, plain-language reasons, reason codes, safe evidence, and observation timestamps.
- [x] Expanding or collapsing details changes presentation only and never starts a probe or changes Windows state.
- [x] Raw exceptions, stack traces, secrets, usernames, Tailscale peer details, full command output, and unrelated file metadata are not displayed.
- [x] Visible copy uses the canonical Host Dashboard, Host Files preflight, managed folder, access path, prerequisite result, access-path readiness, and hosting state vocabulary.
- [x] Every status uses text plus an icon or shape rather than color alone, and Warning, Action required, Unknown, aggregate outcomes, and Not configured remain visually distinct.
- [x] Folder selection, Refresh, Cancel, and detail toggles have predictable keyboard navigation, visible focus, meaningful accessible names, and perceivable run-state updates.
- [x] The dashboard contains no setup, repair, share creation, access-grant, mapping, installation, elevation, public-SMB, guest-access, telemetry, export, or background-monitoring action.
- [x] Presentation tests assert accessible state and owner-visible behavior without coupling to brushes, pixel layouts, dispatcher internals, or notification counts.
- [x] The application builds and the complete non-mutating automated suite remains green.

## Comments

- 2026-08-14 implementation checkpoint: Added the typed neutral Not configured hosting state, five presentation-owned summary areas, per-area presentation-only details with individual prerequisite results, safe evidence, reason codes and timestamps, canonical text-plus-shape status cues, and WPF keyboard/screen-reader affordances. Formatting verification and the solution build passed with 0 warnings and 0 errors; all 210 automated tests passed (Core 126, Presentation 15, Windows 69). Final two-axis review is pending; checkpoint evidence is recorded in `docs/roadmap/v0.2.0.md`.
- 2026-08-14 review checkpoint: The Standards review found no hard violations and the Spec review found no scope creep. Addressed its findings by preserving card instances and keyboard focus during detail toggles, spanning Hosting state across the final summary row, and consolidating duplicated hosting/accessibility copy. Formatting verification, the presentation suite, focused Core tests, and the solution build passed; re-review remains pending.
- 2026-08-14 review follow-up: Spec re-review passed without findings or scope creep. Standards re-review found no violations, confirmed the material duplication findings resolved, and prompted one final consolidation of the shared accessible-status sentence; presentation tests and formatting verification passed. Final re-review remains pending.
- 2026-08-14 final verification: Both review axes passed without findings. Formatting verification and the Debug solution build passed with 0 warnings and 0 errors; the complete Release suite passed 210 tests (Core 126, Presentation 15, Windows 69), and static inspection found no forbidden dashboard action hook. An intervening Debug Core-test load was blocked by enterprise Application Control while the unchanged Core suite passed in Release; this environmental policy event did not affect the verified product behavior. Full evidence is recorded in `docs/roadmap/v0.2.0.md`.
