# Host Files preflight contract

This document preserves the implemented v0.1.0 contract separately from the accepted, Complete v0.2.0 Host Dashboard policy. The accepted, Complete v0.3.0 design authorizes only later implementation inside its documented mutation envelope; the current product still performs no setup mutation.

## v0.1.0 implemented contract

v0.1.0 runs exactly eight independent, read-only checks. It gathers observations, evaluates them in the platform-neutral Core project, and returns every result even when one Windows query fails.

“Ready” covers the intended complete host, not just one possible connection path. It therefore requires both a trusted local-network posture and connected Tailscale: each SMB connection uses the private LAN or Tailscale, while the product is expected to support safe local and remote access.

| Check | Ready when | Read-only observation |
| --- | --- | --- |
| Administrator access | The current process token is elevated and belongs to the local Administrators group. | Current Windows access token. |
| Windows edition and version | Windows 11 Pro 24H2+ (build 26100+) is installed. | Native OS version query and read-only Windows version registry values. |
| Storage location | The selected path resolves to a fixed local NTFS volume with at least 10 GiB free. | Path/volume metadata; the nearest existing ancestor may identify the volume. |
| Network profile | At least one connected profile is Private or domain-authenticated. | `Get-NetConnectionProfile` through a fixed query that returns only selected JSON fields. |
| Windows Firewall | All Domain, Private, and Public profiles are enabled and block inbound traffic by default. | `Get-NetFirewallProfile` through a fixed query that returns only selected JSON fields. |
| Tailscale | Tailscale is installed, its service is running, and the local node reports an online Running state with an address. | Service status plus the allow-listed `tailscale status --json` command. Peer and user details are discarded. |
| SMB file sharing | The Windows Server service is running and SMB 2/3 is enabled. | Service status plus `Get-SmbServerConfiguration` through a fixed JSON query. SMB 1, if enabled, produces a warning. |
| Folder permissions | The folder exists and the current token has read, traverse, and modify rights. | The folder's security descriptor and a Windows access check. No probe file is created. |

The storage floor is an initial product policy, not an installer allocation. It can change centrally in `PreflightPolicy` as real deployments establish a better capacity requirement.

## Result semantics

Each check has one of four outcomes:

- **Ready**: the observed state meets the v0.1.0 policy.
- **Warning**: the required state is present, but an advisory condition deserves attention.
- **Action required**: the state is known and does not meet the policy.
- **Unknown**: Windows did not provide enough trustworthy information.

The overall result fails closed:

1. Any action-required result produces **Not ready**.
2. Otherwise, any unknown result produces **Could not determine readiness**.
3. Otherwise, any warning produces **Ready with warnings**.
4. Only all-ready results produce **Ready**.

An unknown required check can never produce a ready report.

## Mutation prohibition

The diagnostic does not request elevation, install or authenticate Tailscale, start services, enable SMB, change a network category, change firewall policy, create a folder, write a probe file, change an ACL, create a share, map a drive, or persist credentials.

PowerShell is used only behind an enum-backed allow-list for Windows query cmdlets. No caller supplies script text. External process execution is limited to Tailscale's machine-readable status command. Timeouts and access-denied responses become unknown results; they do not trigger repairs.

## v0.2.0 approved policy changes

v0.2.0 preserves read-only, fail-closed execution while changing how the dashboard explains readiness:

- Administrator authorization is information about future setup, not a prerequisite result and not a reason to elevate the dashboard.
- Trusted-LAN and Tailscale access-path readiness are reduced and shown independently; a host may be ready for either path or both.
- Prerequisite results remain Ready, Warning, Action required, and Unknown. Overall results remain Ready, Ready with warnings, Not ready, and Indeterminate.
- SMB readiness requires SMB 3.0 or newer, and enabled SMB1 is Action required rather than a warning.
- Prerequisite readiness never implies that a managed share is configured or that a client connection has been verified.

The exact v0.2.0 implementation and completion gates live in [its version file](roadmap/v0.2.0.md).

## v0.2.0 implemented structured readiness

The Host Files preflight still executes the eight stable, ordered observations. Its completed snapshot now separates seven prerequisite results from administrator information and publishes four fail-closed readiness aggregates:

| Area | Prerequisite results |
| --- | --- |
| Computer | Windows edition and version, Windows Firewall, SMB file sharing |
| Managed folder | Storage location, current-token folder permissions |
| Local access | Computer and Managed folder prerequisites, plus trusted local-network posture |
| Tailscale access | Computer and Managed folder prerequisites, plus Tailscale state |

Each area independently applies Action required → Not ready, otherwise Unknown → Indeterminate, otherwise Warning → Ready with warnings, otherwise Ready. There is no combined Local-and-Tailscale result. Either access path can be ready while the other is not ready or indeterminate.

Administrator membership and process elevation are retained only as future Host Files setup information. Available, standard-user, unelevated, elevated, and unavailable administrator observations cannot change Computer, Managed folder, Local access, or Tailscale access readiness.
