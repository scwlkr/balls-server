# 04 — Package and verify the two-computer pilot

**What to build:** Publish the portable build and one-line GitHub bootstrap, then prove the complete owner-to-client workflow on supported Windows.

**Blocked by:** 02, 03

**Status:** in-progress

**Implementation:** The reproducible self-contained win-x64 publisher and public one-line install/update bootstrap are implemented. A local `0.4.0-pilot.1` package and checksum were produced successfully. GitHub publication and real clean-install/update/two-computer evidence remain.

- [ ] A GitHub release contains only the versioned Windows package and SHA-256 checksum.
- [x] The bootstrap performs clean install/update, preserves state, creates the Start menu shortcut, and launches Balls Server.
- [ ] Format verification, Release build, full automated tests, secret scan, and dependency audit pass.
- [ ] Supported-Windows UI checks cover both roles, previews, consent, progress, errors, keyboard use, and app-close behavior.
- [ ] One host and one client pass LAN and/or Tailscale setup-code pairing, persistent mapping, Explorer round trips, host restart, Stop Sharing, and Disconnect.
- [ ] Ticket and roadmap evidence are complete before owner acceptance, tag, and release publication.

## Comments

- 2026-08-17: Release publication remains gated on complete implementation and verification evidence.
- 2026-08-17: Background packaging checkpoint produced a self-contained win-x64 app/helper package plus SHA-256 file. Automated tests parse both PowerShell scripts and verify that the installer has no state-file deletion path. The package contains neither PDBs nor private runtime state.
- 2026-08-17: Format verification and Release build passed with zero warnings or errors; all 233 automated tests passed. Gitleaks found no secrets in the public tree or staged package, NuGet reported no vulnerable packages, and the 74 MB archive checksum matched its sidecar exactly.
