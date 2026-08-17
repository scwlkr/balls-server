# 01 — Build the first-share workflow and contracts

**What to build:** Add the same-app Host Files and Connect to Files workflow, setup-code contract, preview/result models, and testable presentation state without performing Windows mutations yet.

**Blocked by:** None — can start immediately.

**Status:** done

**Implementation:** `BallsServer.Core.Sharing`, `FirstSharePresentation`, the branded WPF host/connect views, and background WPF smoke coverage.

- [x] The dashboard offers Host Files and Connect to Files as obvious actions while retaining read-only readiness.
- [x] Host state selects one folder and explicit LAN or Tailscale path, then renders a precise mutation preview.
- [x] A versioned setup code round-trips one endpoint and one limited credential while rejecting malformed, unsupported, public, or ambiguous input.
- [x] Connect state previews credential storage, drive mapping, and verification before applying anything.
- [x] Presentation logic is independent of WPF and Windows adapters and has deterministic lifecycle tests.
- [x] The WPF flow shows the Balls Server logo and clear connecting state.
- [x] Default execution remains unelevated and non-mutating.

## Comments

- 2026-08-17: Claimed for immediate implementation after owner acceptance of the accelerated vertical scope.
- 2026-08-17: Implemented and verified with deterministic Core/Presentation tests plus an STA WPF smoke test that renders the compiled Host and Connect views entirely off-screen. The smoke test checks the logo, accessible control names, preview interactions, exact UNC endpoint, and absence of the limited password from rendered text. Full Release verification passed 215 tests with zero build warnings or errors.
