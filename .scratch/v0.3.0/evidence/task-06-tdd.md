# Ticket 06 TDD evidence

Date: 2026-08-14

Scope: isolated platform-neutral access-grant and display-once secret lifecycle model only. No production composition or native Windows, IPC, account, clipboard, QR, filesystem, process, network, or credential operation was written or run.

## RED — missing lifecycle prototype

The desired state test was written first against `AccessGrant`, `GrantFacts`, and the activation state machine.

```powershell
dotnet test .scratch/v0.3.0/prototypes/access-grant-lifecycle.tests/AccessGrantLifecycle.Tests.csproj -c Release --nologo --verbosity minimal
```

Result: exit `1`. MSBuild reported the referenced `access-grant-lifecycle\AccessGrantLifecycle.csproj` missing, followed by `CS0246`/`CS0103` errors for the desired lifecycle APIs. This was the expected missing-feature failure.

## GREEN — minimum isolated model

The smallest model added the non-admin/product-group and observed-policy check, explicit-action random buffer generator, pending/active/recovery/revoked state machine, revision-only activation, minimum bearer setup code, bound one-read response, transient display/buffer disposal, warned clipboard and memory-only QR actions, narrow session selection, and public-text sink scanner.

```powershell
dotnet test .scratch/v0.3.0/prototypes/access-grant-lifecycle.tests/AccessGrantLifecycle.Tests.csproj -c Release --no-restore --nologo --verbosity minimal
& .\scripts\Verify-V030Task06.ps1
```

Result: exit `0`, `19` passed, `0` failed, `0` skipped. The verifier also confirmed no production-solution/source reference, no forbidden adapter source evidence, and no secret canary in focused-test or prototype public output.

## Bounded closure fix

Targeted RED changed activation to require a fresh one-use authorization proof and changed transfer/read and create/rotate tests to require atomic one-winner delivery, exact setup-code grant/revision binding, and internal-only CSPRNG generation. The old API failed to compile against those tests. GREEN passes `23/23`: absent, operation/grant/revision-mismatched, and reused authorization refuse; 32 concurrent reads have one delivered response; mismatched setup-code construction refuses; and reflection confirms create/rotate accept no caller byte input while each action records a fresh 32-byte CSPRNG generation.

Final one-line closure RED/GREEN makes `ActivationAuthorization` non-publicly constructible. Only the internal authoritative-helper seam is visible to the friend test assembly; reflection proves public callers cannot mint the proof. Focused GREEN is `24/24`.

## Final gates

- Ticket 01 verifier: PASS — 39 operation rows, 13 completion columns, 86 stories, 6 exits.
- Ticket 02 verifier: PASS — 125 focused tests.
- Ticket 03 verifier: PASS — 302 focused tests and isolation cleanup.
- Ticket 04 verifier: PASS — 140 focused tests and isolation cleanup.
- Ticket 05: retained immutable reference observed at `012a424ba2d8ce23ada4f2b527a2404bbe28d5c0`; its locally recorded durable verifier result remains the Ticket 05 evidence.
- Isolated prototype/test and solution formatting verification: PASS.
- All `scripts/Verify-V030Task*.ps1` PowerShell parsers: PASS.
- Release build: PASS — 0 warnings, 0 errors.
- Default solution tests: PASS — Core 126, Presentation 15, Windows 69; 210 total.
- `git diff --check`, prototype isolation/source scan, secret-output scan, and temporary residue scan: PASS.
