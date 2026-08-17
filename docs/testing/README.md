# Testing on one Windows laptop

Balls Server must be practical to develop without a permanent multi-PC lab. The normal loop uses deterministic unit tests; Windows Sandbox and isolated Hyper-V virtual machines provide manual operating-system coverage.

## Automated tests

Run from the repository root:

```powershell
dotnet test
```

Automated tests should use fake Windows probes and cover at least:

- supported and unsupported Windows observations;
- the Windows 11 Pro 24H2+ support threshold;
- Host Files prerequisite selection and evaluation;
- Ready, Warning, Action required, and Unknown mapping;
- partial probe failures and access-denied behavior;
- aggregation when independent checks have mixed outcomes;
- cancellation or repeated execution where applicable.

One production-wiring smoke test runs the actual read-only probes on the supported Windows development machine. It asserts the versioned report shape and exact check identities/order; it never asserts that the developer machine is ready. This test is safe without administrator rights, Tailscale, an SMB share, or network access because unavailable observations are expected diagnostic data and remain valid smoke outcomes.

The default test run must not require administrator rights, installed Tailscale, an active SMB share, a second computer, or network access. It must not mutate the machine.

## Windows Sandbox smoke test

Use Windows Sandbox for a clean-machine UI smoke test when the required runtime is available. Validate application startup, target-folder selection, Host Files diagnostic completion, and safe reporting of unavailable prerequisites. Beginning with v0.2.0, record this smoke evidence in the active version file before completion. v0.4.0 adds verification for its portable GitHub bootstrap; signed installer and full packaging tests begin in v0.8.0.

The v0.2.0 supported-Windows smoke covers launch and automatic diagnosis, the profile Documents default, another existing local folder, stale-result indication, Refresh, ordered visible progress, Cancel, independent Local/Tailscale rendering, bounded expandable details, visible keyboard focus order, and close during a run. It also confirms the dashboard remains unelevated and offers no setup, repair, share, credential, permission, firewall, mapping, installation, or public-access action. Fast-machine races may pair the rendered control check with deterministic presentation lifecycle coverage; record that split explicitly instead of claiming an unobserved canceled-state frame.

Treat the Sandbox as disposable and do not use it to justify mutation in v0.1.0 or v0.2.0. The dashboard remains read-only even in an isolated environment.

## Hyper-V integration test

Use one or more disposable Windows 11 Pro virtual machines when a stable OS image, snapshotting, or a two-device topology is needed. Prefer an internal or private virtual switch. A private-LAN scenario can be modeled between isolated VMs; a Tailscale scenario may be tested only within a private tailnet using non-production test identities.

Never expose TCP 445 through an external virtual switch, public address, router port forward, or cloud firewall rule.

## Suggested manual matrix

| Environment | Primary purpose | Expected mutation |
| --- | --- | --- |
| Developer machine | Build and unit tests with fake probes | None |
| Windows Sandbox | Clean-start and unavailable-state smoke tests | None from Balls Server |
| Hyper-V VM snapshot | Read-only Windows integration checks | None from Balls Server v0.1.0/v0.2.0 |
| Two isolated Hyper-V VMs | Later private connectivity tests | Future milestone only |

Any future mutating test suite must be separate from `dotnet test`, opt-in, clearly labeled, and run only in disposable isolated machines after v0.3.0 setup design is approved. Each mutation test begins from a known snapshot, checks the product-owned change record, exercises partial failure and rollback, verifies that user files remain intact, and restores or discards the machine.

The v0.3.0 design fixes the executable boundary for those later suites in [the disposable-VM topology](v0.3.0-disposable-vm-topology.md): default tests stay unelevated/offline/non-mutating; the future mutation runner requires elevation, a disposable marker, the exact configured snapshot, a unique product-test namespace, and a proof that no production object is in scope. It defines private/internal host-client and optional private-tailnet TCP 445-only legs, exact snapshots, full operation coverage, redacted evidence, and mandatory restore/discard. It records no mutating VM execution.

## Release verification

The v0.9.0 candidate matrix must cover clean install, upgrade, application repair, application removal, Host Files setup/repair/removal, Connect pairing/forget/reconnect, access-grant revocation, and temporary-file round trips over isolated LAN and private test-tailnet paths. v1.0.0 cannot become official while a critical or high-severity defect remains.

Connect-to-Files tests begin in its versioned milestones. Balls Node inventory and Share Compute require separate future test strategies for identity, consent, isolation, scheduling, revocation, and multi-node failures; none of those compute tests or implementations belong in v0.x or v1.x. Future capacity tests must preserve the distinction between aggregate cluster resources and the memory available to one ordinary process.
