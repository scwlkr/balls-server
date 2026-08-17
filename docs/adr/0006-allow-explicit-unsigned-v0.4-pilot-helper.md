# ADR 0006: Allow an explicit unsigned v0.4 pilot helper

- **Status:** Accepted for the v0.4 pilot only
- **Date:** 2026-08-17

## Context

The owner accepted an unsigned portable prototype so one host and one approved client can reach a working File Explorer share before the signed installer milestone. The v0.3 production design required an administrator-protected, signed helper and allowed unsigned execution only in disposable test machines. Applying that production gate unchanged would make the accepted v0.4 pilot impossible.

## Decision

The public v0.4 pilot may launch one separate unsigned elevated helper from the exact application directory. Windows will show an Unknown Publisher UAC warning, and the helper must show its own authoritative change preview before applying anything.

The exception remains narrow:

- the unelevated app creates one random current-user local named pipe before launch;
- the helper command line contains only that pipe address;
- the app binds the connected pipe client to the exact PID returned by the UAC launch;
- the request is typed, versioned, nonce-bound, SID-bound, and expires within three minutes;
- the helper requires the elevated identity to match the initiating identity, reruns fail-closed preflight, and accepts one Apply decision;
- folder paths and credentials stay out of process arguments, public results, and diagnostics;
- the mutation runner is a packaged fixed script with a closed operation inventory and redirected standard input;
- the public bootstrap must verify the published package SHA-256 before installing or updating it; and
- the pilot is not an official production release and cannot claim the signed-helper guarantees defined for v0.8.

This exception does not permit guest access, public TCP 445, SMB policy changes, arbitrary commands, services, scheduled tasks, or mutation outside the selected folder and product-owned account, group, share, firewall rule, and ledger.

## Consequences

The pilot can execute the first real share flow quickly, but Windows cannot identify a trusted publisher and the installation remains user-writable. Users must approve the Unknown Publisher prompt only when they intentionally clicked **Apply setup** in Balls Server. Code signing, administrator-protected installation, manifest-pinned binary identity, and the full production peer-evidence contract remain required before the official installer milestone.
