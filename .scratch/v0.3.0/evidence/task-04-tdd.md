# Ticket 04 TDD evidence

Date: 2026-08-14

Scope: isolated, platform-neutral ledger, reconciliation, rollback, crash, recovery, and retention model only. No production or Windows mutation code was present during any cycle.

## Cycle 1 — missing ledger/recovery model

The tests were written first against the wished-for schema, reconciliation, rollback, operation-state, replay, mirror/corruption, scenario, manifest, and retention APIs. The first invocation exposed a test-string quoting error; that was corrected before accepting RED evidence.

Decisive RED command:

```powershell
dotnet test .scratch\v0.3.0\prototypes\ledger-recovery.tests\LedgerRecovery.Tests.csproj -c Release --no-restore
```

Result: exit `1`. MSBuild reported the referenced `ledger-recovery\LedgerRecovery.csproj` did not exist, followed by `CS0246`/`CS0103` errors for the wished-for production types. This was the expected feature-missing failure, not a test syntax failure.

Minimal implementation added the isolated project, complete schema validator, protected-state policy, reconciliation and rollback policies, state machine, crash catalog, replay policy, primary/mirror recovery, in-memory store, named scenario outcomes, manifest, and retention policy.

GREEN command: the same command after restore. Result: exit `0`, `91` passed, `0` failed, `0` skipped.

## Cycle 2 — authorization evidence and convergence

RED command:

```powershell
dotnet test .scratch\v0.3.0\prototypes\ledger-recovery.tests\LedgerRecovery.Tests.csproj -c Release --no-restore
```

Result: exit `1`. Compilation failed because `DesiredResourceState`, `ConvergenceDisposition`, and `ConvergencePolicy` did not exist. The same test set also required journal fields for user/session/pipe/helper/consent binding and a permanent non-authority rule.

Minimal implementation added the exact non-secret journal binding fields, `JournalEntry.CanAuthorizeMutationOrReplay = false`, generalized secret-canary scanning, and explicit setup/removal convergence with at most one creation and no unmanaged adoption.

GREEN result: exit `0`, `101` passed, `0` failed, `0` skipped.

## Cycle 3 — executable crash injection

RED command:

```powershell
dotnet test .scratch\v0.3.0\prototypes\ledger-recovery.tests\LedgerRecovery.Tests.csproj -c Release --no-restore
```

Result: exit `1`; `CS0246` proved `InMemoryTransactionPrototype` and `CrashExecutionSnapshot` were missing. A catalog-only assertion was therefore insufficient to satisfy the crash-injection requirement.

Minimal implementation added a fresh in-memory transaction run for each selected point. It persists modeled Planned/Started/Verified journal records, applies unique in-memory primitive identities, replaces primary and mirror revisions separately, and advances the committed revision only at the final boundary. The catalog covers before/after planned journal, every primitive-start journal, every primitive, every primitive-verification journal, both copy replacements, and revision advancement.

GREEN result: exit `0`, `103` passed, `0` failed, `0` skipped. For a three-primitive plan, every one of `26` injection points produced the documented fail-closed recovery disposition.

## Cycle 4 — restrictive owner-readable ACL

Targeted RED command:

```powershell
dotnet test .scratch\v0.3.0\prototypes\ledger-recovery.tests\LedgerRecovery.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~Host_state_acl_contract
```

Result: exit `1`, `1` failed of `1`. Expected `O:SYG:SYD:P(A;;FA;;;SY)(A;;FA;;;BA)(A;;FR;;;<OWNER-SID>)`; actual used `OW`, which referred to the file owner rather than the recorded product owner SID.

Minimal implementation replaced `OW` with the exact substituted recorded-owner SID placeholder. Later focused GREEN included this case.

## Cycle 5 — generalized password/secret-field refusal

Targeted RED command:

```powershell
dotnet test .scratch\v0.3.0\prototypes\ledger-recovery.tests\LedgerRecovery.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~Forbidden_secret_fields
```

Result: exit `1`, `2` failed and `6` passed. `passwordHash` and `secretHint` were caught only by the canary value, not identified as forbidden field names.

Minimal implementation made password/secret property-name matching case-insensitive and general while retaining the exact forbidden credential/setup-code names. Targeted GREEN passed `8/8`.

## Cycle 6 — total loss with corroborating-journal loss

Targeted RED command:

```powershell
dotnet test .scratch\v0.3.0\prototypes\ledger-recovery.tests\LedgerRecovery.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~Primary_mirror_and_journal_recovery
```

Result: exit `1`, `2` failed and `10` passed. When primary and mirror were both corrupt or both missing and the journal was also invalid, the policy returned generic `UnknownReadOnly` instead of the required `TotalLossReadOnly` manifest state.

Minimal implementation prioritized loss of both ledger copies over journal validity. The invalid journal remains unusable, but it cannot suppress the total-loss read-only manifest boundary.

## Final focused GREEN

```powershell
& .\scripts\Verify-V030Task04.ps1
```

Result: exit `0`:

```text
PASS: 107 isolated ledger/recovery tests; schema, authorization non-reuse, transition, reconciliation cross-product, convergence, rollback, mirror/corruption/total-loss, crash matrix, retention, redaction, production isolation, output, and residue checks passed.
```

The verifier also proved the projects remain outside production composition, rejected adapter evidence, found no secret/private value in prototype output, and found no `BallsServer.Test.LedgerRecovery.*` residue. No filesystem fixture was used.

## Review-fix round 1 — consolidated adversarial contract

The new adversarial test file was added before changing production code. It named the missing closed retention collections alongside the closed graph, append-chain, ownership, rollback/replay, durable crash-state, revision-exhaustion, host/client, manifest, and retention behavior.

Decisive RED command:

```powershell
dotnet test .scratch/v0.3.0/prototypes/ledger-recovery.tests/LedgerRecovery.Tests.csproj --no-restore --nologo --verbosity minimal
```

Exact decisive output retained from the run:

```text
LedgerRecovery -> C:\Dev\balls-server\.worktrees\v0.3.0\.scratch\v0.3.0\prototypes\ledger-recovery\bin\Debug\net10.0-windows10.0.26100.0\LedgerRecovery.dll
C:\Dev\balls-server\.worktrees\v0.3.0\.scratch\v0.3.0\prototypes\ledger-recovery.tests\ReviewFixAdversarialTests.cs(237,21): error CS0246: The type or namespace name 'RetentionCollections' could not be found (are you missing a using directive or an assembly reference?) [C:\Dev\balls-server\.worktrees\v0.3.0\.scratch\v0.3.0\prototypes\ledger-recovery.tests\LedgerRecovery.Tests.csproj]
```

Result: exit `1`. This was the expected feature-missing compile failure against the first absent hardened contract, not a syntax, restore, or environment failure.

The minimal complete implementation then added the closed canonical schema graph and scanner, cryptographically recomputed journal and ownership chains, exact rollback/replay/consent bindings, durable-state recovery, typed revision exhaustion, host/client type separation, bounded recovery-manifest instructions, collection retention, and the exact-project/dependency verifier. An intermediate behavioral run compiled all wished-for APIs and failed `2/106`: one idempotent retention-result identity mismatch and one incorrect journal binding assertion. The retention implementation was made identity-preserving on repeated no-op purge, and the assertion was corrected to the fixture's exact nonce/authorization fields.

Focused GREEN command:

```powershell
dotnet test .scratch/v0.3.0/prototypes/ledger-recovery.tests/LedgerRecovery.Tests.csproj --no-restore --nologo --verbosity minimal
```

Exact pre-self-review focused result:

```text
Passed!  - Failed:     0, Passed:   118, Skipped:     0, Total:   118, Duration: 70 ms - LedgerRecovery.Tests.dll (net10.0)
```

Verifier self-test GREEN:

```powershell
& .\scripts\Test-V030Task04Verifier.ps1
```

```text
PASS: Task 04 verifier self-test rejected all eleven executable-build, analyzer, input-graph, linked-source, and compiled dependency/PInvoke mutations before unsafe execution and cleaned up deterministically.
```

Public verifier GREEN:

```powershell
& .\scripts\Verify-V030Task04.ps1
```

```text
PASS: 118 isolated ledger/recovery tests plus eleven-class real compiled isolation mutations; closed schema/journal/ownership/replay/rollback/crash/manifest/retention contracts, exact project and compiler inputs, dependency metadata, output, and residue checks passed.
```

Self-review then tightened three fail-closed edges: a self-consistently hashed but noncanonical ownership proof, an advanced copy pair with an incomplete journal, and a retention record placed in the wrong protected collection. Regression tests for the ownership and retention cases increased the final focused result to `120/120`; the incomplete-journal assertion was added to the existing durable-copy contradiction test. The complete gate below uses that final count.

## Review-fix final gate

- `Verify-V030Task01.ps1`: PASS — 39 rows, 13 columns, 86 stories, 6 exits.
- `Verify-V030Task02.ps1`: PASS — 125/125 plus ephemeral IPC, scanner, isolation, and secret-output checks.
- `Verify-V030Task03.ps1`: PASS — 302/302 plus exact-input/dependency isolation and eleven-class cleanup.
- `ReviewFixAdversarialTests`: PASS — 30/30.
- `Test-V030Task04Verifier.ps1`: PASS — eleven mutation classes, no sentinel or fixture residue.
- `Verify-V030Task04.ps1`: PASS — 120/120 and the required exact-input/dependency/output/residue boundary.
- Prototype, test, and solution `dotnet format --verify-no-changes`: exit `0`.
- All nine Ticket 01–04 `.ps1`/`.psm1` files: PowerShell parser returned zero errors.
- `dotnet build BallsServer.slnx -c Release --no-restore --nologo`: exit `0`, 0 warnings, 0 errors.
- `dotnet test BallsServer.slnx -c Release --no-build --no-restore`: exit `0`; Core 126, Presentation 15, Windows 69, 210 total.
- Final `git diff --check`, composition/source scans, secret/private-output scans, and `BallsServer.Test.*` residue scan: clean.

## Review-fix round 2 — bounded closure

Early Ticket 04 cycles above are summarized chronology with selected exact output. This round retains each decisive targeted RED category and the exact final command output after the last implementation/evidence edit.

1. Resource-operation map RED: the round-2 filter exited `1` with `CS0117` for missing `LanFirewallRule`, `TailscaleFirewallRule`, `VerificationFileCleanup`, and OP-11/12/16/32/38. GREEN: `10/10`.
2. Reconciliation provenance RED: the filter exited `1` with `CS0246`/`CS0117` for missing `ReconciliationResult`, `OwnershipProvenance`, and `Reconcile`; the first implementation then produced a behavioral RED because proven live absence was incorrectly `Invalid`. GREEN: `12/12`.
3. Journal semantics RED: the filter exited `1` with `CS0246` for missing `JournalState`. GREEN: `17/17`, covering request revision `N`, committed ledger `N+1`, failed/unknown results, rollback/recovery progression, and one terminal.
4. Typed crash RED: the filter exited `1` with `CS0246` for missing `CrashExecutionResult`, `CrashExecutionCode`, and `CrashJournalRecordKind`. The independently expected 26-point test then exposed premature commit recovery before the complete planned primitive set. A separate malformed-order regression failed with expected `Undefined`, actual `ResumeReadOnlyReconciliation`. GREEN: all typed crash cases pass with exact plan order and typed revision exhaustion.
5. Complete public proof RED: the filter exited `1` with `CS0246` for missing `ProtectedLedgerRecoveryEvidence`. The initial GREEN closed durable crash, replay, protected-copy, and retention bypasses, but its reflection assertion incorrectly allowed convergence from a caller-supplied derived `ReconciliationResult`; the urgent wrap below records the later RED and raw-proof-only correction.
6. Structured retention RED: the filter exited `1` with `CS1739`/`CS0117` because `EvidenceFingerprint` did not exist and arbitrary exact bytes remained. GREEN: canonical non-secret fingerprints retain identically, secret-shaped values refuse Cleanup needed, and no public byte-payload property remains.
7. Terminal repair RED: the exact documented `Planned -> Started -> RepairNeeded` progression failed with expected `Valid`, actual `InvalidJournal`. GREEN: `5/5` journal progression rows pass, including a terminal repair handoff and a repair handoff that continues through recovery to one later terminal.

The bounded round-2 candidate at commit `d4e5d27` had targeted `23/23` and focused `139/139`; its exact then-current gate transcript is retained below and superseded by the urgent wrap evidence that follows.

## Round-2 final exact output

Final focused command after implementation and evidence updates:

```powershell
dotnet test .scratch/v0.3.0/prototypes/ledger-recovery.tests/LedgerRecovery.Tests.csproj -c Release --no-restore --nologo --verbosity minimal
```

```text
LedgerRecovery -> C:\Dev\balls-server\.worktrees\v0.3.0\.scratch\v0.3.0\prototypes\ledger-recovery\bin\Release\net10.0-windows10.0.26100.0\LedgerRecovery.dll
LedgerRecovery.Tests -> C:\Dev\balls-server\.worktrees\v0.3.0\.scratch\v0.3.0\prototypes\ledger-recovery.tests\bin\Release\net10.0-windows10.0.26100.0\LedgerRecovery.Tests.dll
Test run for C:\Dev\balls-server\.worktrees\v0.3.0\.scratch\v0.3.0\prototypes\ledger-recovery.tests\bin\Release\net10.0-windows10.0.26100.0\LedgerRecovery.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:   139, Skipped:     0, Total:   139, Duration: 74 ms - LedgerRecovery.Tests.dll (net10.0)
```

Final verifier command:

```powershell
& .\scripts\Verify-V030Task04.ps1
```

```text
PASS: 139 isolated ledger/recovery tests plus eleven-class real compiled isolation mutations; closed schema/journal/ownership/replay/rollback/crash/manifest/retention contracts, exact project and compiler inputs, dependency metadata, output, and residue checks passed.
```

The complete round-2 gate also passed Ticket 01 (`39/13/86/6`), Ticket 02 (`125/125`), Ticket 03 (`302/302`), the eleven-class Task 04 verifier self-test, round-2 targeted `23/23`, consolidated adversarial `29/29`, all three format checks, all nine PowerShell parses, Release build with zero warnings/errors, all 210 default tests, diff/isolation/secret/private-output scans, and residue cleanup.

## Urgent final wrap — exact two regressions

Targeted RED command filtered the partial-plan commit and public convergence surface tests. It exited `1` with `2/2` failures: the exact planned set `[p1,p2,p3]` with only p1 started, verified, applied, and `RevisionCommitted` returned `CompleteIdempotently` instead of `Undefined`; reflection found public convergence parameters `[ReconciliationResult, DesiredResourceState]` instead of `[ReconciliationInput, DesiredResourceState]`.

Minimal GREEN requires a revision commit to have started, verified, and applied the entire exact planned set. The only public convergence call now accepts the complete raw `ReconciliationInput` plus desired state and derives reconciliation internally. The focused suite passes `140/140`; round-2 targeted passes `24/24`; consolidated adversarial remains `29/29`.

```text
Passed!  - Failed:     0, Passed:   140, Skipped:     0, Total:   140, Duration: 84 ms - LedgerRecovery.Tests.dll (net10.0)
PASS: 140 isolated ledger/recovery tests plus eleven-class real compiled isolation mutations; closed schema/journal/ownership/replay/rollback/crash/manifest/retention contracts, exact project and compiler inputs, dependency metadata, output, and residue checks passed.
```
