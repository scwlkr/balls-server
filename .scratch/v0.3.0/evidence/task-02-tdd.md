# Ticket 02 TDD transcript

All commands ran from `C:\Dev\balls-server\.worktrees\v0.3.0` on 2026-08-14. The prototype is isolated and performed no Windows mutation.

## RED 1 — requested prototype absent

Command:

```powershell
dotnet test '.scratch\v0.3.0\prototypes\privileged-helper-boundary.tests\PrivilegedHelperBoundary.Tests.csproj' -c Release
```

Exact output and exit:

```text
Exit code: 1
  Determining projects to restore...
  Restored C:\Dev\balls-server\.worktrees\v0.3.0\.scratch\v0.3.0\prototypes\privileged-helper-boundary\PrivilegedHelperBoundary.csproj (in 745 ms).
  Restored C:\Dev\balls-server\.worktrees\v0.3.0\.scratch\v0.3.0\prototypes\privileged-helper-boundary.tests\PrivilegedHelperBoundary.Tests.csproj (in 779 ms).
CSC : error CS5001: Program does not contain a static 'Main' method suitable for an entry point [C:\Dev\balls-server\.worktrees\v0.3.0\.scratch\v0.3.0\prototypes\privileged-helper-boundary\PrivilegedHelperBoundary.csproj]
```

The prototype implementation that makes this pass is the requested isolated executable and security API. No implementation existed when this RED was captured.

## RED 2 — strict terminal contract absent

After the request/state-machine cycle reached 34 passing tests, four terminal-codec tests were added before their implementation.

Command:

```powershell
dotnet test '.scratch\v0.3.0\prototypes\privileged-helper-boundary.tests\PrivilegedHelperBoundary.Tests.csproj' -c Release --no-restore
```

Exact outcome and representative compiler findings:

```text
Exit code: 1
  PrivilegedHelperBoundary -> C:\Dev\balls-server\.worktrees\v0.3.0\.scratch\v0.3.0\prototypes\privileged-helper-boundary\bin\Release\net10.0-windows10.0.26100.0\PrivilegedHelperBoundary.dll
AuthorizationBoundaryTests.cs(48,46): error CS0117: 'StrictProtocolCodec' does not contain a definition for 'EncodeTerminal'
AuthorizationBoundaryTests.cs(50,9): error CS0246: The type or namespace name 'TerminalDecodeResult' could not be found
AuthorizationBoundaryTests.cs(50,60): error CS0117: 'StrictProtocolCodec' does not contain a definition for 'DecodeTerminal'
```

The same missing-symbol findings repeated at the other three prewritten terminal-codec test call sites. The prototype implementation that makes these tests pass is the strict bounded result codec with operation/nonce binding, unknown-field rejection, and private-content refusal.

## GREEN — complete focused suite

Command:

```powershell
dotnet test '.scratch\v0.3.0\prototypes\privileged-helper-boundary.tests\PrivilegedHelperBoundary.Tests.csproj' -c Release --no-restore
```

Exact output and exit:

```text
Exit code: 0
  PrivilegedHelperBoundary -> C:\Dev\balls-server\.worktrees\v0.3.0\.scratch\v0.3.0\prototypes\privileged-helper-boundary\bin\Release\net10.0-windows10.0.26100.0\PrivilegedHelperBoundary.dll
  PrivilegedHelperBoundary.Tests -> C:\Dev\balls-server\.worktrees\v0.3.0\.scratch\v0.3.0\prototypes\privileged-helper-boundary.tests\bin\Release\net10.0-windows10.0.26100.0\PrivilegedHelperBoundary.Tests.dll
Test run for C:\Dev\balls-server\.worktrees\v0.3.0\.scratch\v0.3.0\prototypes\privileged-helper-boundary.tests\bin\Release\net10.0-windows10.0.26100.0\PrivilegedHelperBoundary.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:    39, Skipped:     0, Total:    39, Duration: 92 ms - PrivilegedHelperBoundary.Tests.dll (net10.0)
```

One intermediate run reached 33 of 34 passing tests and exposed a fixture replacement that changed both revision and operation ID. The fixture was narrowed to mutate only the named field; no product behavior was weakened.

## Review-fix round 1 RED 1 — adversarial codecs, diagnostics, replay, and concurrency

Command:

```powershell
dotnet test '.scratch\v0.3.0\prototypes\privileged-helper-boundary.tests\PrivilegedHelperBoundary.Tests.csproj' -c Release --no-restore
```

Decisive exact output:

```text
System.NullReferenceException: Object reference not set to an instance of an object.
   at BallsServer.SecurityPrototype.StrictProtocolCodec.DecodeRequest(ReadOnlySpan`1 encoded) ... AuthorizationBoundary.cs:line 184
Expected: Replay
Actual:   None
Expected: MalformedMessage
Actual:   None
Assert.Throws() Failure: No exception was thrown
Expected: typeof(System.ArgumentException)
Failed!  - Failed:    19, Passed:    43, Skipped:     0, Total:    62, Duration: 98 ms - PrivilegedHelperBoundary.Tests.dll (net10.0)
```

The failures covered duplicate-plus-omission, null required values, Unicode operation IDs, numeric/undefined terminal enums, invalid counts and status/code pairs, oversized encoding, caller-controlled diagnostics, and stale-nonce reuse. Coordinated consent/terminal/secret races also executed in this RED set.

## Review-fix round 1 RED 2 — complete consent and helper-owned timing bindings

After RED 1 became GREEN at 66 tests, the complete binding/timing tests were written against the wished-for API.

Decisive exact output:

```text
Exit code: 1
AuthorizationBoundaryTests.cs(390,9): error CS0117: 'AuthorizationScenario' does not contain a definition for 'PipeInstanceId'
AuthorizationBoundaryTests.cs(391,9): error CS0117: 'AuthorizationScenario' does not contain a definition for 'LaunchMonotonic'
AuthorizationBoundaryTests.cs(392,9): error CS0117: 'AuthorizationScenario' does not contain a definition for 'MutualAuthenticationCompletedAt'
AuthorizationBoundaryTests.cs(393,9): error CS0117: 'AuthorizationScenario' does not contain a definition for 'RequestReceivedAt'
AuthorizationBoundaryTests.cs(394,9): error CS0117: 'AuthorizationScenario' does not contain a definition for 'PlanDisplayedAt'
AuthorizationBoundaryTests.cs(395,9): error CS0117: 'AuthorizationScenario' does not contain a definition for 'ApplyAt'
AuthorizationBoundaryTests.cs(398,54): error CS1729: 'ConsentBinding' does not contain a constructor that takes 9 arguments
ReviewAdversarialTests.cs(156,44): error CS0117: 'ConsentBinding' does not contain a definition for 'Operation'
ReviewAdversarialTests.cs(159,43): error CS0117: 'ConsentBinding' does not contain a definition for 'ExpectedRevision'
ReviewAdversarialTests.cs(161,39): error CS0117: 'ConsentBinding' does not contain a definition for 'PipeInstanceId'
```

## Review-fix round 1 RED 3 — verifier mutation self-test

Command `& '.\scripts\Test-V030Task02Verifier.ps1'` exited 1 with this decisive exact message before its module existed:

```text
The specified module 'C:\Dev\balls-server\.worktrees\v0.3.0\scripts\V030Task02Verifier.psm1' was not loaded because no valid module file was found in any module directory.
```

## Review-fix round 1 GREEN

```text
Passed!  - Failed:     0, Passed:    81, Skipped:     0, Total:    81, Duration: 73 ms - PrivilegedHelperBoundary.Tests.dll (net10.0)
PASS: production-isolation scanner rejected both forbidden identifiers and removed its temporary fixture.
PASS: 81 isolated helper-boundary tests, including adversarial/concurrency coverage and one ephemeral named-pipe/process-ID feasibility test; scanner self-test and production isolation passed; secret-output scan clean.
```

## Review-fix round 2 RED 1 — exhaustive terminal mapping

The encode and decode adversarial tables were expanded before changing the prototype. They included cross-status codes for every external status and explicitly `Refused + ConsentExpired`.

Command:

```powershell
dotnet test '.scratch\v0.3.0\prototypes\privileged-helper-boundary.tests\PrivilegedHelperBoundary.Tests.csproj' -c Release --no-restore
```

Decisive exact output:

```text
Assert.Throws() Failure: No exception was thrown
Expected: MalformedMessage
Actual:   None
Failed!  - Failed:     2, Passed:    89, Skipped:     0, Total:    91, Duration: 80 ms - PrivilegedHelperBoundary.Tests.dll (net10.0)
```

Both failures were the forbidden `Refused + ConsentExpired` tuple, once on encode and once on decode.

## Review-fix round 2 RED 2 — independent Apply binding and full phase timeline

Integrated tests next changed each of the nine display/Apply binding components independently, rejected invalid pipe identities, and breached every documented phase separately. They were compiled before the canonical timeline API existed.

Decisive exact output and exit:

```text
Exit code: 1
AuthorizationBoundaryTests.cs(400,19): error CS0246: The type or namespace name 'HelperPhaseTimeline' could not be found (are you missing a using directive or an assembly reference?)
```

## Review-fix round 2 GREEN

```text
Passed!  - Failed:     0, Passed:   108, Skipped:     0, Total:   108, Duration: 75 ms - PrivilegedHelperBoundary.Tests.dll (net10.0)
```

## Review-fix round 3 RED 1 — post-auth terminal-emission bypasses

Integrated tests first gave early cancellation, stale revision, malformed request, terminal-write overrun, and absolute-lifetime overrun an unconfirmable terminal timeline.

Decisive exact output:

```text
Expected: Unknown
Actual:   Canceled
Expected: Unknown
Actual:   Refused
Failed!  - Failed:     5, Passed:   108, Skipped:     0, Total:   113, Duration: 88 ms - PrivilegedHelperBoundary.Tests.dll (net10.0)
```

All five failures reproduced the bypass: the model exposed the candidate result without proving the terminal write.

## Review-fix round 3 RED 2 — explicit delivered versus unconfirmed outcome

The tests then required a delivery marker, an unconfirmed factory that cannot be encoded, and an integrated one-use emission gate.

Decisive exact output and exit:

```text
Exit code: 1
ReviewAdversarialTests.cs(105,48): error CS1061: 'TerminalResult' does not contain a definition for 'WasDelivered'
ReviewAdversarialTests.cs(112,85): error CS0117: 'TerminalResult' does not contain a definition for 'Unconfirmed'
ReviewAdversarialTests.cs(165,9): error CS0246: The type or namespace name 'TerminalEmissionGate' could not be found
```

## Review-fix round 3 intermediate GREEN

```text
Passed!  - Failed:     0, Passed:   117, Skipped:     0, Total:   117, Duration: 85 ms - PrivilegedHelperBoundary.Tests.dll (net10.0)
```

## Review-fix round 3 RED 3 — pre-auth delivery distinction

Return-path self-review identified that pre-auth peer refusal correctly bypassed authenticated emission but still carried the default delivered marker. A focused integration test required the typed local refusal without a helper-delivery claim.

```text
Assert.False() Failure
Expected: False
Actual:   True
Failed!  - Failed:     1, Passed:   117, Skipped:     0, Total:   118, Duration: 83 ms - PrivilegedHelperBoundary.Tests.dll (net10.0)
```

## Review-fix round 3 intermediate GREEN 2

```text
Passed!  - Failed:     0, Passed:   118, Skipped:     0, Total:   118, Duration: 81 ms - PrivilegedHelperBoundary.Tests.dll (net10.0)
```

## Review-fix round 3 RED 4 — confirmation outcome readiness

Return-path self-review then mutated an expired confirmation so its terminal appeared before the Apply timeout was observable. This exposed that the gate used re-observation completion rather than Apply as the readiness timestamp for `ConsentExpired`.

```text
Expected: Unknown
Actual:   Canceled
Failed!  - Failed:     1, Passed:   118, Skipped:     0, Total:   119, Duration: 89 ms - PrivilegedHelperBoundary.Tests.dll (net10.0)
```

## Review-fix round 3 final GREEN

```text
Passed!  - Failed:     0, Passed:   119, Skipped:     0, Total:   119, Duration: 83 ms - PrivilegedHelperBoundary.Tests.dll (net10.0)
```

## Controller closure RED — failed first emission reopened the gate

After both final re-review axes reproduced the same remaining cardinality defect, controller-owned tests attempted an unconfirmed, premature, or invalid-contract terminal first and then attempted a valid terminal through the same gate. A mixed valid/invalid concurrent test was added at the same boundary.

Decisive exact output and exit:

```text
Exit code: 1
Assert.False() Failure
Expected: False
Actual:   True
Failed!  - Failed:     3, Passed:     1, Skipped:     0, Total:     4, Duration: 15 s - PrivilegedHelperBoundary.Tests.dll (net10.0)
```

The three failures proved that unconfirmed-first, premature-first, and invalid-contract-first attempts did not close the emission gate.

## Controller closure GREEN

The first attempt now atomically claims the emission gate before any validation. Complete external-contract and timing validation precede the separate delivered-response count, so a failed first attempt leaves zero delivered responses while permanently refusing every later attempt.

```text
Passed!  - Failed:     0, Passed:     4, Skipped:     0, Total:     4, Duration: 15 s - PrivilegedHelperBoundary.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:   123, Skipped:     0, Total:   123, Duration: 14 s - PrivilegedHelperBoundary.Tests.dll (net10.0)
```

## Controller closure null-first RED and GREEN

Closure re-review found one remaining ordering edge: null validation ran before the atomic claim. A null-first-then-valid theory case and mixed null/valid concurrency case were added before changing the API.

Decisive RED:

```text
Exit code: 1
ReviewAdversarialTests.cs(263,36): error CS8604: Possible null reference argument for parameter 'candidate' in 'TerminalResult TerminalEmissionGate.Expose(TerminalResult candidate, long outcomeReadyAt)'.
```

The gate now accepts a nullable candidate only to fail it closed after atomically claiming the attempt. Targeted and full focused GREEN:

```text
Passed!  - Failed:     0, Passed:     5, Skipped:     0, Total:     5, Duration: 12 s - PrivilegedHelperBoundary.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:   125, Skipped:     0, Total:   125, Duration: 12 s - PrivilegedHelperBoundary.Tests.dll (net10.0)
```
