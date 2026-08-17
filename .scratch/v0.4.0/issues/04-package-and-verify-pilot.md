# 04 — Package and verify the two-computer pilot

**What to build:** Publish the portable build and one-line GitHub bootstrap, then prove the complete owner-to-client workflow on supported Windows.

**Blocked by:** 02, 03

**Status:** ready-for-agent

**Implementation:** pending

- [ ] A GitHub release contains only the versioned Windows package and SHA-256 checksum.
- [ ] The bootstrap performs clean install/update, preserves state, creates the Start menu shortcut, and launches Balls Server.
- [ ] Format verification, Release build, full automated tests, secret scan, and dependency audit pass.
- [ ] Supported-Windows UI checks cover both roles, previews, consent, progress, errors, keyboard use, and app-close behavior.
- [ ] One host and one client pass LAN and/or Tailscale setup-code pairing, persistent mapping, Explorer round trips, host restart, Stop Sharing, and Disconnect.
- [ ] Ticket and roadmap evidence are complete before owner acceptance, tag, and release publication.

## Comments

- 2026-08-17: Release publication remains gated on complete implementation and verification evidence.
