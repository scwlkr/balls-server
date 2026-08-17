# Product definition

## Purpose

Windows file sharing is capable but difficult to configure safely. Balls Server gives an owner a simple view of computer state, guides approved setup, and connects trusted Windows profiles to files without exposing SMB to the public internet.

The full product direction includes file sharing, Balls Node inventory, and explicitly opt-in Share Compute. Semantic versions keep current work inside its approved boundary: v0.x builds file sharing, v1.0.0 is its first official release, v2.x introduces Balls Nodes, and v3.x introduces Share Compute.

## File-sharing roles

| Role | User intent |
| --- | --- |
| **Host Files** | Make one selected folder on this computer available to approved clients. |
| **Connect to Files** | Access a managed folder from another approved Windows profile. |

Host Files preflight is read-only. Host Files setup is a later, consent-driven capability. Connect readiness and Connect setup are similarly separate. See [the glossary](../CONTEXT.md) for the canonical terms.

## Supported file-sharing environment

- Host and client operating system: Windows 11 Pro 24H2 or later.
- Desktop stack: C#, .NET 10, and WPF.
- File-sharing protocol: authenticated SMB 3.0 or newer, with SMB1 disabled and SMB signing preserved.
- Access paths: a trusted private LAN or a private Tailscale network, reported independently.

Public TCP 445 exposure, router port forwarding for SMB, blank-password access, and insecure guest access are prohibited. Tailscale supplies a private access path; Windows SMB still controls authentication and file authorization.

## v1.0 file-sharing boundary

The first official release targets one Balls Server-managed read/write folder per host. The owner can:

- see Computer, Managed folder, Local access, Tailscale access, and Hosting state in a simple unelevated dashboard;
- preview and approve narrowly scoped Host Files setup;
- create a separate, limited access grant for each client Windows profile;
- map the managed share as a reconnecting Windows drive after explicit credential-storage consent;
- verify access, repair only product-owned drift, revoke one grant, and remove product-owned configuration;
- keep the managed folder and every file when hosting or the application is removed.

Balls Server does not take over an existing unmanaged share or silently overwrite permissions it does not own. Conflicts stop with a plain explanation and recovery choices.

## Trust, consent, and privacy

The dashboard runs without elevation. A separate privileged helper may perform only operations approved by a completed setup-design milestone, after showing a change preview and receiving consent. Every operation must define least privilege, idempotency, ownership, partial-failure behavior, verification, rollback, and manual recovery.

Client access uses dedicated non-administrative credentials rather than guest access or the owner's personal Windows/Microsoft password. With consent, a client may store its credential in Windows Credential Manager. Secrets never belong in process arguments, logs, diagnostics, normal configuration, or release artifacts.

File-sharing functionality is local-first and requires no Balls Server cloud account, advertising, or telemetry. Diagnostics remain local, omit credentials and sensitive file details, and are exported only after an explicit action and preview.

## Current program position

v0.1.0, the unelevated read-only Host Files preflight; v0.2.0, the accepted Host Dashboard; and v0.3.0, the accepted setup security and architecture design, are Complete. v0.4.0 First Working Share is the next Proposed milestone: it pulls one complete host-to-client File Explorer path forward while remaining inside the accepted mutation envelope. No setup mutation is implemented yet. [The roadmap](ROADMAP.md) is the source of truth for current state, accomplished work, remaining work, and completion evidence.

## Permanent non-goals

- Public SMB hosting or inbound internet TCP 445.
- Router port-forwarding guidance for SMB.
- Guest or anonymous file access.
- General remote administration or unrestricted remote command execution.
- Deleting the managed folder or user files during repair, removal, upgrade, or uninstall.

## Distant direction

v2.x may inventory owner-approved computers as Balls Nodes and show resources each node explicitly advertises. v3.x may let owners opt nodes into narrowly approved distributed workloads through Share Compute.

Neither direction expands current file-sharing scope. Each requires a separate product definition, identity and consent model, privacy rules, security architecture, isolation strategy, revocation design, and test plan before implementation. Aggregate node resources do not turn several machines' memory into one larger address space for an ordinary process.
