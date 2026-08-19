# Balls Server

> [!WARNING]
> **Archived, unsupported work in progress.** This branch was preserved without merge or release
> when Balls Server was retired on August 19, 2026. It is not completion evidence. Active
> development moved to [Balls](https://github.com/scwlkr/balls), currently private while
> licensing and public-release decisions are finalized.

Balls Server is a Windows desktop application for safely sharing files between approved PCs over a private LAN or Tailscale without exposing SMB to the public internet.

This is a public repository. Credentials, private host or network identifiers, real file metadata, and unsanitized diagnostics do not belong in source, history, issues, releases, or artifacts.

## Status

- **v0.1.0 — Complete:** unelevated, read-only Host Files preflight; 129 automated tests passed.
- **v0.2.0 — Complete:** the accepted simple Host Dashboard has separate Local/Tailscale access-path readiness, corrected SMB/administrator policy, 210 passing automated tests, and complete supported-Windows UI/safety evidence.
- **v0.3.0 — Complete:** the owner accepted the reviewed setup security and architecture design; the product remains read-only.
- **v0.4.0 — In progress:** the public pilot implements consent-driven hosting, a setup code, and a persistent File Explorer drive over LAN or Tailscale. Disposable-machine mutation and two-computer pilot verification remain before milestone completion.

[The roadmap](docs/ROADMAP.md) is the status source of truth. Its compact index links to each version's accomplished work, remaining work, exit checks, and evidence.

## Product direction

- **Host Files** makes one selected read/write folder available to approved clients.
- **Connect to Files** maps that managed share as a Windows drive.
- **v1.0.0** is the first official file-sharing release.
- **v2.x** introduces Balls Nodes and resource inventory.
- **v3.x** introduces explicitly opt-in Share Compute for approved distributed workloads.

The v0.4 pilot includes a separate UAC helper for previewed Host Files changes and an unelevated native Windows client connector. It is intentionally unsigned and is not the official production release.

## Installation disabled

Do not install this branch or the unsigned pilot releases for new use. The branch-local
`install.ps1` now refuses to download or change files. Tags and release assets remain only as
historical provenance.

## Safety boundary

- Windows 11 Pro 24H2+ using C#, .NET 10, and WPF.
- Authenticated SMB 3.0+ with SMB1 disabled and SMB signing preserved.
- Private LAN or private Tailscale access paths, reported independently.
- No guest access, public TCP 445, router port forwarding, or general remote administration.
- No required Balls Server cloud account, advertising, or telemetry.
- Repair, removal, upgrade, and uninstall never delete the managed folder or user files.

## Build and test

From a Windows development environment with the .NET 10 SDK:

```powershell
dotnet restore
dotnet build
dotnet test
```

Default tests are unelevated and non-mutating. Windows Sandbox and isolated Hyper-V machines provide clean-machine and future topology coverage.

## Documentation

- [Roadmap and current position](docs/ROADMAP.md)
- [Product definition](docs/PRODUCT.md)
- [Domain glossary](CONTEXT.md)
- [Architecture and trust boundaries](docs/ARCHITECTURE.md)
- [Host Files preflight contract](docs/PREFLIGHT.md)
- [Testing strategy](docs/testing/README.md)
- [Architecture decisions](docs/adr/)
