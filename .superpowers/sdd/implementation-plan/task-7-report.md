# Task 07 report — client credential, mapping, and verification lifecycle

## Outcome

An isolated, platform-neutral client lifecycle prototype and contract now define the future current-user credential, mapping, verification, cleanup, switching, and disposable-VM guard behavior. No production project references the prototype.

## TDD evidence

RED: `ClientLifecycleTests.Connect_inspects_the_exact_endpoint_mapping_letter_and_credential_before_its_one_attempt` failed because the planner returned `Allowed = false`.

GREEN: the focused project passes 10/10. It covers exact one-endpoint setup/update binding, observation/collision before a maximum one authentication attempt, typed non-oracular failures, provider target/UNC separation, save/reconnect selection, owned-only cleanup/not-found, private verification leftovers, explicit switch ordering/state preservation, host revocation, and development-host VM guard refusal.

## Safety boundary

The prototype has no native credential, mapping, SMB, filesystem, shell, child-process, or Windows mutation adapter. The guarded future VM harness is denial-only on the development host. Review, push, owner approval, and later disposable-VM mutation remain open.

## Bounded review fix

RED: two targeted regressions failed as expected: `OtherShare` was accepted as an exact endpoint, and the first `SelectSave(true)` left reconnect unselected.

GREEN: the focused project now passes 12/12. An exact endpoint, setup code, endpoint update, and mapping plan require the fixed `Balls` share; first Save selects reconnect by default, while explicit reconnect clear remains independent.
