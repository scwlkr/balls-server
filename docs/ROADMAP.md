# Roadmap

This is the small index for project position and sequencing. Open only the linked version file needed for scope, completed work, remaining work, checks, and evidence.

## Current position

| Item | Position |
| --- | --- |
| Completed version | [v0.3.0 — Setup security and architecture design](roadmap/v0.3.0.md) |
| Current milestone | [v0.4.0 — First Working Share](roadmap/v0.4.0.md), **In progress** |
| Next action | Run Ticket 02 disposable-host mutation verification, then complete the two-computer Ticket 04 pilot. |
| File-sharing destination | v1.0.0 official release |
| Full product direction | v3.x Share Compute, after v2.x Balls Nodes |

The roadmap has no promised dates. Work proceeds in dependency order as quickly as its safety and completion checks allow.

## Milestone states

`Proposed → Defined → In progress → Verification → Complete`

Use **Blocked** only while a stated condition prevents progress. **Complete** requires every exit check, recorded evidence, and a semantic-version Git tag. An **official release** additionally requires a signed package, release notes, release verification, and owner approval.

## File-sharing path

| Version | State | Outcome |
| --- | --- | --- |
| [v0.1.0](roadmap/v0.1.0.md) | Complete | Unelevated, read-only Host Files preflight |
| [v0.2.0](roadmap/v0.2.0.md) | Complete | Simple Host Dashboard with accurate per-path readiness |
| [v0.3.0](roadmap/v0.3.0.md) | Complete | Approved setup, identity, trust, recovery, and test design |
| [v0.4.0](roadmap/v0.4.0.md) | In progress | First working two-computer share in File Explorer |
| [v0.5.0](roadmap/v0.5.0.md) | Proposed | Pilot reliability and connection diagnostics |
| [v0.6.0](roadmap/v0.6.0.md) | Proposed | Multiple access grants and path changes |
| [v0.7.0](roadmap/v0.7.0.md) | Proposed | End-to-end verification, repair, access removal, and cleanup |
| [v0.8.0](roadmap/v0.8.0.md) | Proposed | Installer, signing, upgrade, and update system |
| [v0.9.0](roadmap/v0.9.0.md) | Proposed | Scope-frozen release candidate |
| [v1.0.0](roadmap/v1.0.0.md) | Proposed | First official file-sharing release |

## Distant horizons

[v2.x Balls Nodes and v3.x Share Compute](roadmap/horizons.md) are part of the full product direction but do not expand current milestone scope.

## Update rule

Every meaningful checkpoint updates the active version file in the same commit. Account for completed work, remaining work, changed scope, and new evidence. Work may move to another version or be rejected explicitly; it is never silently dropped to declare completion.

## Permanent boundaries

- Public repository and local-first product; repository content and release artifacts must contain no secrets, private identifiers, real file metadata, or unsanitized diagnostics.
- SMB 3.0 or newer only, with SMB1 disabled, over a private LAN or Tailscale.
- No public TCP 445, SMB port forwarding, guest access, or general remote administration.
- The dashboard remains unelevated; future system changes require preview, consent, least privilege, ownership tracking, recovery, and isolated tests.
- Balls Server never deletes the managed folder or its contents.

## Public history

Public Git history begins with the sanitized post-v0.3.0 snapshot. The original v0.1.0-v0.3.0 development history and tags remain in a private archive because historical test fixtures contained private host metadata.
