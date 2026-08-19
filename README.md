# Balls Server

> [!WARNING]
> **Retired and unsupported as of August 19, 2026.** This was the original Windows
> file-sharing prototype that led to the broader [Balls](https://github.com/scwlkr/balls)
> platform. The successor repository is currently private while licensing and public-release
> decisions are finalized.

Do not install the unsigned pilot releases for new use. The `main` branch installer now refuses
to run. Existing tags, prereleases, assets, documentation, and source remain available only as
historical provenance and implementation prior art.

## Historical status

- **v0.1.0 — Complete:** unelevated, read-only Host Files preflight.
- **v0.2.0 — Complete:** accepted read-only Host Dashboard and readiness policy.
- **v0.3.0 — Complete:** accepted setup security and architecture design.
- **v0.4.0 — Retired incomplete:** the pilot was never accepted as a completed milestone;
  disposable-machine and two-computer verification remained outstanding.

Two later local commits are preserved without merge or release under
[`archive/v0.4.0-completion-wip`](https://github.com/scwlkr/balls-server/tree/archive/v0.4.0-completion-wip).
They remain unaccepted work in progress despite the original local branch name.

## Existing pilot installations

Do not rerun the remote bootstrap. The archived project provides no supported version or ongoing
security fixes. If a pilot changed Windows sharing state, preserve the managed folder and user
files and consult the documentation matching that exact pilot before attempting cleanup. The
unfinished project must not be treated as verified uninstall or recovery guidance.

## Historical product boundary

Balls Server explored one Windows-managed folder shared over authenticated SMB 3.0+ on a private
LAN or Tailscale path. It prohibited guest access, public TCP 445, router port forwarding, and
general remote administration. Those findings are prior research; SMB, Tailscale, WPF, and one
Windows host do not define the successor Balls architecture.

## Historical build

From a Windows environment with the pinned .NET 10 SDK:

```powershell
dotnet restore
dotnet build
dotnet test
```

Building historical source does not make the pilot supported or production-ready.

## Documentation

- [Retired roadmap and recorded evidence](docs/ROADMAP.md)
- [Historical product definition](docs/PRODUCT.md)
- [Historical domain glossary](CONTEXT.md)
- [Historical architecture and trust boundaries](docs/ARCHITECTURE.md)
- [Security policy](SECURITY.md)
