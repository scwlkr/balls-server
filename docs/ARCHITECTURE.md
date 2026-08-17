# Architecture

## Context

Balls Server is a Windows 11 Pro desktop application built with C#, .NET 10, and WPF. Authenticated SMB 3.0+ is the file-transfer mechanism, constrained to a private LAN or Tailscale with SMB1 disabled. The Complete v0.1.0 system is a read-only Host Files preflight diagnostic; the accepted, Complete v0.2.0 milestone presents that diagnostic as a simple, still read-only Host Dashboard. The accepted, Complete v0.3.0 milestone defines the security and architecture envelope for v0.4.0 First Working Share without itself adding mutation.

```text
User
  |
  v
WPF shell and Host Files readiness view
  |
  v
Preflight orchestration and policy
  |
  v
Read-only Windows probe interfaces
  |
  v
Windows observations (no writes)
```

## Logical layers

### Presentation

The WPF layer owns command state, progress, and rendering the Host Files diagnostic results. It should not contain Windows commands or encode prerequisite thresholds in UI strings. Later role selection must not be implied to exist in v0.1.0.

### Application and policy

The application layer runs the checks for **Host Files** and converts raw observations into typed prerequisite results. The v0.2.0 report publishes Computer and Managed folder aggregates plus independent Local and Tailscale access-path readiness; it has no combined both-path result. Future administrator authorization is separate information and cannot affect readiness. Later application flows add **Connect to Files** without changing completed version scope retrospectively.

Useful outcome categories are:

- **Ready**: the observed state satisfies the prerequisite.
- **Warning**: the prerequisite is satisfied, but an advisory condition deserves attention.
- **Action required**: the state is known and does not satisfy the prerequisite, but the dashboard does not change it.
- **Unknown**: the state could not be observed reliably; an unavailable probe or error is a cause, not a separate outcome.

### Windows integration

Small interfaces isolate operating-system queries from policy. Production adapters may call supported Windows or .NET query APIs, while tests use fakes with deterministic observations and failures.

A probe reports observations only. It must not contain a fallback that changes the system, requests elevation, or repairs a failed prerequisite.

## Read-only dashboard invariant

Every v0.1.0 and v0.2.0 dashboard path, including error handling, is non-mutating and unelevated. The application must not:

- change Windows optional features, services, registry, policy, accounts, groups, or credentials;
- create or modify SMB shares, file permissions, firewall rules, network profiles, adapters, routes, or stored connections;
- install, uninstall, or configure Tailscale or another dependency;
- invoke a setup helper or elevated process.

Read-only inspection can still fail because of permissions, platform differences, or unavailable services. Such failures are data: preserve a safe diagnostic result and continue independent checks where possible.

## Network and trust boundary

SMB 3.0+ traffic is allowed only between authenticated endpoints over one of these paths:

1. a private local network; or
2. a private Tailscale network.

The dashboard reports the paths independently; one host may be ready locally, remotely, or both. Tailscale provides private reachability, while Windows SMB still authenticates and authorizes file access. No design may require public inbound TCP 445, a public SMB endpoint, guest access, or router port forwarding.

## Privileged setup boundary

The dashboard remains unelevated. A separate, narrowly scoped helper may perform Host Files or Connect to Files changes only after its design milestone is complete and the user approves a precise preview. v0.4.0 may use an unsigned portable build and a public GitHub bootstrap for rapid install/update; v0.8.0 remains responsible for the signed official installer and complete distribution lifecycle. See [ADR 0001](adr/0001-separate-dashboard-and-privileged-setup.md).

The unsigned v0.4 helper is an explicit pilot exception, not the production trust model. Its narrowed pipe, PID, identity, request-expiry, fixed-operation, and Unknown Publisher consent requirements are recorded in [ADR 0006](adr/0006-allow-explicit-unsigned-v0.4-pilot-helper.md).

Before mutating work begins, its architecture must define:

- which operations require elevation and why;
- explicit consent and a preview of changes;
- least-privilege operation boundaries;
- idempotency and partial-failure behavior;
- rollback or precise manual recovery guidance;
- auditable logs that do not contain secrets;
- isolated integration tests.

The helper must also record which share, permission, firewall, identity, and mapping changes Balls Server owns. Repair and removal operate only on those owned changes, and no path deletes the managed folder or its contents.

## Client access identity

Each access grant uses a separate, limited identity rather than guest access, a shared credential, or the owner's personal Windows/Microsoft credentials. Authorization combines product-owned permission groups, share permissions, and NTFS permissions. A client may save its secret in Windows Credential Manager only after consent. The identity represents revocable access, not proof of physical device trust. See [ADR 0003](adr/0003-use-a-limited-identity-per-access-grant.md).

## Testability

Policy and orchestration run deterministically through fake probe implementations. The default suite includes one narrow production-wiring smoke test on the supported Windows development machine: it runs the same read-only probes as the app and asserts the versioned report shape plus stable check identity and order, never that the machine has a particular readiness state. Missing Tailscale, disabled prerequisites, access denial, and other environmental differences become diagnostic results rather than test dependencies; an unavailable observation remains a valid production-smoke outcome. Clean-machine UI checks remain manual in Windows Sandbox or isolated Hyper-V environments. Future mutating suites are separate, explicit, and disposable.

See [testing/README.md](testing/README.md) for the environment matrix.
The exact eight-observation contract, seven prerequisite results, and structured readiness reductions are documented in [PREFLIGHT.md](PREFLIGHT.md).

## Long-term compute boundary

Balls Node inventory and an explicitly opt-in Share Compute capability are v2.x and v3.x horizons, not v0.x or v1.x architecture. The current file-sharing solution must not grow compute discovery, resource advertisement, worker enrollment, command execution, scheduling, remote jobs, or workload APIs.

If that direction is approved later, it needs a separate trust boundary and architecture for node identity, owner consent, advertised resources, workload isolation, scheduling, revocation, and auditing. File sharing must not silently enroll a machine for compute work.

Resource totals across Balls Nodes describe aggregate cluster capacity for distributable workloads. They do not pool physical memory into a single address space, and they cannot make one ordinary process use the combined RAM of several machines.
