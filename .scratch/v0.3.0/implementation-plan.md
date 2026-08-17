# v0.3.0 implementation plan

**Spec:** [v0.3.0 setup security and architecture design](spec.md)

## Global constraints

- The active roadmap and specification are binding. A ticket may narrow unsafe automation to refusal but may not widen privilege, mutate unmanaged state, expose a secret, weaken isolation, or add production behavior.
- v0.3.0 is design and prototype evidence only. Default verification stays unelevated, non-mutating, offline, local, and independent of Hyper-V or Tailscale.
- Execute one implementation agent at a time in dependency order. Each task gets applicable test-first work, both independent review axes, exact ticket/roadmap evidence, and a committed and pushed checkpoint.
- Preserve unrelated files. Never enable guest/public SMB, weaken SMB/signing/firewall policy, take folder ownership, force disconnect, delete the managed folder/files, or persist a real credential/mapping on the development host.

## Task 1: Publish the threat model and operation matrix

Execute [Ticket 01](issues/01-publish-threat-model-and-operation-matrix.md).

## Task 2: Prove the privileged-helper authorization boundary

Execute [Ticket 02](issues/02-prove-privileged-helper-boundary.md) after Task 1.

## Task 3: Prove managed-resource safety

Execute [Ticket 03](issues/03-prove-managed-resource-safety.md) after Task 1.

## Task 4: Specify ledger, reconciliation, rollback, and recovery

Execute [Ticket 04](issues/04-specify-ledger-reconciliation-and-recovery.md) after Tasks 2 and 3.

## Task 5: Validate explicit endpoint identity and switching

Execute [Ticket 05](issues/05-validate-explicit-endpoint-switching.md) after Task 1.

## Task 6: Specify the access-grant and display-once secret lifecycle

Execute [Ticket 06](issues/06-specify-access-grant-and-secret-lifecycle.md) after Tasks 2 through 5.

## Task 7: Prove the client credential, mapping, and verification lifecycle

Execute [Ticket 07](issues/07-prove-client-credential-and-mapping-lifecycle.md) after Tasks 5 and 6.

## Task 8: Publish the disposable VM verification topology

Execute [Ticket 08](issues/08-publish-disposable-vm-verification-topology.md) after Tasks 2 through 7.

## Task 9: Verify the v0.3.0 design candidate

Execute [Ticket 09](issues/09-verify-v030-design-candidate.md) after Tasks 1 through 8. This task advances the milestone only to Verification.

Task 9 proves only feature/upstream parity and that local `main` plus `origin/main` remain at the unchanged planning baseline `c7b2957`. Owner acceptance, merge to the default branch, post-merge main parity, the Complete state, and the `v0.3.0` tag remain orchestrator steps after Task 9 and are not delegated.
