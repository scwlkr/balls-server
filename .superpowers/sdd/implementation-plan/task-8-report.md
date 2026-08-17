# Task 08 report — disposable VM verification topology

## Outcome

Ticket 08 publishes the later safe-adapter boundary in `docs/testing/v0.3.0-disposable-vm-topology.md`. The deliverable is an executable, platform-neutral topology/schema/guard model and validation; it is not a mutating VM suite or VM-run claim.

## TDD evidence

RED: `Published_contract_is_complete` failed at 0/1 because the initial topology contract returned incomplete.

GREEN: the isolated topology project passes 15/15. It proves the exact default-suite boundary; denial for each missing mutation guard; test-only scope proof; eligible-plan-only behavior; private/internal host-client and optional private-tailnet client-to-host TCP 445 legs; exact snapshot set; a complete 39 by 11 nonblank matrix; rejection of missing/unknown cells; and complete end-to-end/assertion sets.

Bounded review-fix RED: a valid guarded scope with the fixed `Balls` share failed at 14 passed/1 failed because the general namespace rule rejected that product-fixed name. GREEN allows exact `Balls` only when the disposable marker, elevation, configured snapshot, unique namespace, and every other namespaced test-only scope value are valid; another unnamespaced share remains denied.

## Safety boundary

`Get-VM` is unavailable in the current session. No Hyper-V command, VM run, native adapter, SMB, account, credential, filesystem, network, or Windows mutation was attempted. The isolated source contains no VM/process/native/filesystem/credential adapter and is not included in the production solution.

## Verification

`Verify-V030Task01` through `04`, `06`, `07`, and `08` passed at 39 rows/13 columns/86 stories/6 exits, 125/125, 302/302, 140/140, 24/24, 12/12, and 15/15 respectively. Formatting verification, PowerShell parser validation, Release build with 0 warnings/errors, default 210/210 tests, diff check, and production-isolation scan passed.

## Remaining closure

The two independent review axes and the controller's committed/pushed/fetched parity closure remain open. No review, push, VM execution, owner acceptance, milestone completion, merge, or tag is claimed here.
