# 01 — Build the Host Dashboard state and run lifecycle

**What to build:** Give the owner a responsive Host Dashboard that checks automatically on launch, can be refreshed or canceled, and always makes clear which selected folder and completed observation its visible state describes. Put this behavior behind one testable presentation interface so the WPF window only renders state and forwards owner actions.

**Blocked by:** None — can start immediately.

**Status:** ready-for-agent

**Implementation:** complete — 2026-08-13

- [x] The dashboard presentation module owns selected-folder state, run lifecycle, ordered progress, the last completed snapshot, stale-result state, and launch, Refresh, Cancel, and folder-selection actions through a small interface.
- [x] The WPF window delegates behavior to that interface and contains no Windows query, prerequisite threshold, readiness reduction, or repair behavior.
- [x] The first diagnostic starts after the window loads and evaluates the current Windows profile's Documents folder without requiring an initial button press.
- [x] Exactly one run can be active; Refresh and folder editing are unavailable while it runs, and Cancel is available only while it runs.
- [x] Progress identifies the current check and its position without publishing partial observations as a completed snapshot.
- [x] A completed snapshot is published atomically with its evaluated folder path and start/completion timestamps.
- [x] Choosing or typing a different folder marks the previous snapshot as needing Refresh; blank or nonexistent folders are rejected in plain language before orchestration starts.
- [x] Cancel preserves the prior completed snapshot when one exists, while a canceled initial run shows Not checked; results arriving from canceled or superseded runs cannot replace current state.
- [x] Closing the window cancels active work, and no background timer or window-activation event starts another diagnostic.
- [x] Automated tests exercise externally visible presentation state for launch, Refresh, progress, invalid folders, folder changes, completion, cancellation, exceptions, and late-result suppression.
- [x] The application builds and the existing non-mutating automated suite remains green.

## Comments

- 2026-08-13: Implemented in commits `61ac923`, `28ece68`, and `e9ff2d5`, followed by a two-axis review and correction pass. Formatting verification and the solution build passed with 0 warnings and 0 errors; all 141 automated tests passed (Core 75, Presentation 11, Windows 55). Full evidence is recorded in `docs/roadmap/v0.2.0.md`.
