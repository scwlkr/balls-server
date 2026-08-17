# v0.4.0 — First Working Share specification

## Goal

Ship one Windows application that lets an owner host one folder and one approved client connect to it as a persistent File Explorer drive over either a private LAN or Tailscale.

## Primary flow

1. The application offers **Host Files** and **Connect to Files**.
2. Host Files selects an existing local folder, previews changes, and requests UAC only when applying them.
3. The privileged helper creates one product-owned share, least-privilege access identity, required permissions, selected private firewall scope, and a protected ownership record.
4. The host displays one copyable setup code for either the LAN or Tailscale endpoint.
5. Connect to Files accepts the code, previews credential storage and mapping, creates a persistent available drive, and verifies a temporary create/read/rename/delete round trip.
6. Both users work through File Explorer after closing Balls Server. Files remain on the host and are unavailable while it is powered off or asleep.

## Rapid distribution

- Publish one unsigned portable Windows build in a GitHub release.
- A public PowerShell bootstrap downloads the latest package and SHA-256 checksum, verifies it, installs or updates application files under the current user's local application directory, preserves product state, creates a Start menu shortcut, and launches the app.
- Formal signing, MSI/MSIX packaging, silent updates, and multi-user machine installation remain later work.

## Safety boundary

- Windows 11 Pro 24H2+, SMB 3.0+, SMB1 disabled, signing preserved.
- Authenticated access only; no guest, owner's personal password, public TCP 445, or router forwarding.
- LAN and Tailscale endpoints are explicit choices, never silent fallbacks.
- Secrets never enter command lines, logs, normal configuration, setup-code diagnostics, or release artifacts.
- Stop Sharing and Disconnect affect only recorded product-owned objects and never delete the managed folder or user files.

## Acceptance

- The same build completes the flow on one clean host and one clean client.
- A setup code maps a persistent drive over one selected private path.
- Both profiles pass create/read/rename/delete through File Explorer while both app windows are closed.
- Host shutdown makes the drive unavailable without losing files; access resumes after restart.
- Failure, cancellation, Stop Sharing, and Disconnect leave a known recoverable state and preserve user data.
- The public bootstrap performs a clean install and an in-place update without replacing product state.
- Automated tests, isolated mutation tests, supported-Windows UI checks, and a two-computer pilot are recorded before completion.
