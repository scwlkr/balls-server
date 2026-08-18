# 04 — Package and verify the two-computer pilot

**What to build:** Publish the portable build and one-line GitHub bootstrap, then prove the complete owner-to-client workflow on supported Windows.

**Blocked by:** 02, 03

**Status:** in-progress

**Implementation:** The reproducible self-contained win-x64 publisher and public one-line install/update bootstrap are implemented. Public prereleases through `v0.4.0-pilot.3` are published with verified package/checksum assets. Real two-computer evidence remains.

- [x] A GitHub release contains only the versioned Windows package and SHA-256 checksum.
- [x] The bootstrap performs clean install/update, preserves state, creates the Start menu shortcut, and launches Balls Server.
- [x] Format verification, Release build, full automated tests, secret scan, and dependency audit pass.
- [ ] Supported-Windows UI checks cover both roles, previews, consent, progress, errors, keyboard use, and app-close behavior.
- [ ] One host and one client pass LAN and/or Tailscale setup-code pairing, persistent mapping, Explorer round trips, host restart, Stop Sharing, and Disconnect.
- [ ] Ticket and roadmap evidence are complete before owner acceptance, tag, and release publication.

## Comments

- 2026-08-17: Release publication remains gated on complete implementation and verification evidence.
- 2026-08-17: Background packaging checkpoint produced a self-contained win-x64 app/helper package plus SHA-256 file. Automated tests parse both PowerShell scripts and verify that the installer has no state-file deletion path. The package contains neither PDBs nor private runtime state.
- 2026-08-17: Format verification and Release build passed with zero warnings or errors; all 233 automated tests passed. Gitleaks found no secrets in the public tree or staged package, NuGet reported no vulnerable packages, and the 74 MB archive checksum matched its sidecar exactly.
- 2026-08-17: Published public prerelease `v0.4.0-pilot.1` with exactly the versioned win-x64 zip and checksum assets. The exact public bootstrap then passed a clean install and two successive in-place updates with launch disabled: app, helper, fixed script, and Start Menu shortcut were present; the application hash stayed stable; backup cleanup completed; and a synthetic state file outside the App directory survived the update. No application UI or Windows sharing mutation was invoked.
- 2026-08-18: Published `v0.4.0-pilot.2` from synchronized commit `f89c795` with exactly the 74,186,361-byte win-x64 zip and matching SHA-256 sidecar. The staged package has no PDB, state-like file, vulnerable package, or Gitleaks finding. The public bootstrap updated the local installation with launch disabled; app, Windows adapter, helper, and fixed setup script hashes match the published stage, the Start Menu shortcut remains, no backup remains, and the installed production preflight reports Computer, Managed folder, Local access, and SMB Ready twice. No Computer Use, application UI, share, account, managed-folder content, ACL, firewall, or client-mapping mutation occurred during release verification.
- 2026-08-18: Published `v0.4.0-pilot.3` from synchronized commit `da967e9` with exactly the 74,186,371-byte win-x64 zip and matching SHA-256 sidecar. The release has exactly those two assets; the staged package contains no PDB, state-like file, vulnerable dependency, or Gitleaks finding. The public bootstrap updated the local installation with launch disabled; the app, Core, Presentation, Windows adapter, helper, and fixed setup script hash-match the published stage, the Start Menu shortcut remains, no backup remains, and the installed production preflight reports Computer, Managed folder, Local access, and SMB Ready twice. No Computer Use or Windows sharing mutation ran during release verification.
