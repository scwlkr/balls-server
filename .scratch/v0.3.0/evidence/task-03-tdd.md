# Ticket 03 TDD transcript

All commands ran from `C:\Dev\balls-server\.worktrees\v0.3.0` on 2026-08-14. The prototype is isolated and performed no Windows, network, ACL, share, firewall, account, group, policy, managed-folder, or user-file mutation.

## RED — requested contract and behavior absent

The isolated test project and wished-for behavior tests were written before the prototype project or API existed.

Command:

```powershell
dotnet test '.scratch\v0.3.0\prototypes\managed-resource-safety.tests\ManagedResourceSafety.Tests.csproj' -c Release
```

Exact decisive output excerpts and exit:

```text
Exit code: 1
  Skipping project "C:\Dev\balls-server\.worktrees\v0.3.0\.scratch\v0.3.0\prototypes\managed-resource-safety\ManagedResourceSafety.csproj" because it was not found.
C:\Program Files\dotnet\sdk\10.0.400\Microsoft.Common.CurrentVersion.targets(2205,5): warning MSB9008: The referenced project ..\managed-resource-safety\ManagedResourceSafety.csproj does not exist. [C:\Dev\balls-server\.worktrees\v0.3.0\.scratch\v0.3.0\prototypes\managed-resource-safety.tests\ManagedResourceSafety.Tests.csproj]
C:\Dev\balls-server\.worktrees\v0.3.0\.scratch\v0.3.0\prototypes\managed-resource-safety.tests\ManagedFolderSafetyTests.cs(7,30): error CS0246: The type or namespace name 'FolderObservation' could not be found (are you missing a using directive or an assembly reference?) [C:\Dev\balls-server\.worktrees\v0.3.0\.scratch\v0.3.0\prototypes\managed-resource-safety.tests\ManagedResourceSafety.Tests.csproj]
C:\Dev\balls-server\.worktrees\v0.3.0\.scratch\v0.3.0\prototypes\managed-resource-safety.tests\AccessControlSafetyTests.cs(21,44): error CS0246: The type or namespace name 'ProductAce' could not be found (are you missing a using directive or an assembly reference?) [C:\Dev\balls-server\.worktrees\v0.3.0\.scratch\v0.3.0\prototypes\managed-resource-safety.tests\ManagedResourceSafety.Tests.csproj]
C:\Dev\balls-server\.worktrees\v0.3.0\.scratch\v0.3.0\prototypes\managed-resource-safety.tests\ShareAndFirewallSafetyTests.cs(50,30): error CS0246: The type or namespace name 'SmbPrerequisiteObservation' could not be found (are you missing a using directive or an assembly reference?) [C:\Dev\balls-server\.worktrees\v0.3.0\.scratch\v0.3.0\prototypes\managed-resource-safety.tests\ManagedResourceSafety.Tests.csproj]
C:\Dev\balls-server\.worktrees\v0.3.0\.scratch\v0.3.0\prototypes\managed-resource-safety.tests\MutationAndPrivacySafetyTests.cs(7,30): error CS0246: The type or namespace name 'MutationGuardContext' could not be found (are you missing a using directive or an assembly reference?) [C:\Dev\balls-server\.worktrees\v0.3.0\.scratch\v0.3.0\prototypes\managed-resource-safety.tests\ManagedResourceSafety.Tests.csproj]
```

The missing project and representative missing symbols are the exact isolated folder/reparse/identity, ACE, share/SMB/firewall, and mutation-guard contract requested by Ticket 03. The failure was the expected missing behavior, not a passing change detector or a production-composition test.

## GREEN 1 — initial focused suite

Command:

```powershell
dotnet test '.scratch\v0.3.0\prototypes\managed-resource-safety.tests\ManagedResourceSafety.Tests.csproj' -c Release --no-restore
```

Exact output and exit:

```text
Exit code: 0
  ManagedResourceSafety -> C:\Dev\balls-server\.worktrees\v0.3.0\.scratch\v0.3.0\prototypes\managed-resource-safety\bin\Release\net10.0-windows10.0.26100.0\ManagedResourceSafety.dll
  ManagedResourceSafety.Tests -> C:\Dev\balls-server\.worktrees\v0.3.0\.scratch\v0.3.0\prototypes\managed-resource-safety.tests\bin\Release\net10.0-windows10.0.26100.0\ManagedResourceSafety.Tests.dll
Test run for C:\Dev\balls-server\.worktrees\v0.3.0\.scratch\v0.3.0\prototypes\managed-resource-safety.tests\bin\Release\net10.0-windows10.0.26100.0\ManagedResourceSafety.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:    71, Skipped:     0, Total:    71, Duration: 50 ms - ManagedResourceSafety.Tests.dll (net10.0)
```

One intermediate focused run had 70 passing and one failing test because a deliberately wrong ownership SID reached the unrelated-ACE comparison before the ownership-tuple comparison. The minimal implementation fix validates the exact recorded SID/ACE ownership tuple first. No test expectation or refusal boundary was weakened.

## RED 2 — effective-access preservation and canonical share ownership

Self-review expanded the adversarial tables for non-directory/non-fixed/unknown-filesystem folders, group name/SID/marker conflicts, guest SMB, and outbound firewall exposure. It also wrote behavior tests requiring a before/after effective-access preservation check and a canonical share ownership fingerprint before either API existed.

Command:

```powershell
dotnet test '.scratch\v0.3.0\prototypes\managed-resource-safety.tests\ManagedResourceSafety.Tests.csproj' -c Release --no-restore
```

Exact decisive output and exit:

```text
Exit code: 1
C:\Dev\balls-server\.worktrees\v0.3.0\.scratch\v0.3.0\prototypes\managed-resource-safety.tests\AccessControlSafetyTests.cs(70,57): error CS1501: No overload for method 'EvaluateIntent' takes 3 arguments [C:\Dev\balls-server\.worktrees\v0.3.0\.scratch\v0.3.0\prototypes\managed-resource-safety.tests\ManagedResourceSafety.Tests.csproj]
C:\Dev\balls-server\.worktrees\v0.3.0\.scratch\v0.3.0\prototypes\managed-resource-safety.tests\AccessControlSafetyTests.cs(89,57): error CS1501: No overload for method 'EvaluateIntent' takes 3 arguments [C:\Dev\balls-server\.worktrees\v0.3.0\.scratch\v0.3.0\prototypes\managed-resource-safety.tests\ManagedResourceSafety.Tests.csproj]
C:\Dev\balls-server\.worktrees\v0.3.0\.scratch\v0.3.0\prototypes\managed-resource-safety.tests\ShareAndFirewallSafetyTests.cs(74,41): error CS0117: 'SharePolicy' does not contain a definition for 'Fingerprint' [C:\Dev\balls-server\.worktrees\v0.3.0\.scratch\v0.3.0\prototypes\managed-resource-safety.tests\ManagedResourceSafety.Tests.csproj]
```

## GREEN 2 — final focused suite

```text
Exit code: 0
  ManagedResourceSafety -> C:\Dev\balls-server\.worktrees\v0.3.0\.scratch\v0.3.0\prototypes\managed-resource-safety\bin\Release\net10.0-windows10.0.26100.0\ManagedResourceSafety.dll
  ManagedResourceSafety.Tests -> C:\Dev\balls-server\.worktrees\v0.3.0\.scratch\v0.3.0\prototypes\managed-resource-safety.tests\bin\Release\net10.0-windows10.0.26100.0\ManagedResourceSafety.Tests.dll
Test run for C:\Dev\balls-server\.worktrees\v0.3.0\.scratch\v0.3.0\prototypes\managed-resource-safety.tests\bin\Release\net10.0-windows10.0.26100.0\ManagedResourceSafety.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:    81, Skipped:     0, Total:    81, Duration: 28 ms - ManagedResourceSafety.Tests.dll (net10.0)
```

## Scope truth

The GREEN suite proves a pure/in-memory model plus one uniquely named disposable temporary-directory cleanup case. It does not prove native NTFS binary round-trip, Windows effective-access APIs, a live share, a live firewall rule, GPO behavior, interface binding, elevation, or any mutating VM operation. Those remain later guarded disposable-VM evidence.

## Review-fix round 1 RED — reproduced fail-open behavior

Adversarial tests were added against the implementation checkpoint before changing the model. They covered lexical traversal, exact same-product-SID ACE multiplicity, protected share/firewall ownership, incomplete SMB bounds, and typed firewall handoffs.

Command:

```powershell
dotnet test '.scratch\v0.3.0\prototypes\managed-resource-safety.tests\ManagedResourceSafety.Tests.csproj' -c Release --no-restore
```

Exact result and decisive failures:

```text
Exit code: 1
Failed!  - Failed:     8, Passed:    82, Skipped:     0, Total:    90
ReviewFixAdversarialTests.Null_minimum_dialect_is_unknown_not_accepted
  Expected: False
  Actual:   True
ReviewFixAdversarialTests.Firewall_observation_cannot_self_attest_product_ownership
  Expected: False
  Actual:   True
ReviewFixAdversarialTests.Lexical_traversal_target_is_not_contained
  Expected: False
  Actual:   True
ReviewFixAdversarialTests.Share_observation_cannot_self_attest_product_ownership
  Expected: False
  Actual:   True
ReviewFixAdversarialTests.Same_product_sid_extra_entry_is_rejected_by_applied_verification(entry: ProductAce { Type = Deny, ... })
  Expected: False
  Actual:   True
ReviewFixAdversarialTests.Same_product_sid_extra_entry_is_rejected_by_applied_verification(entry: ProductAce { Type = Allow, Rights = Read, ... })
  Expected: False
  Actual:   True
ReviewFixAdversarialTests.Same_product_sid_extra_entry_is_rejected_by_applied_verification(entry: ProductAce { ... IsInherited = True })
  Expected: False
  Actual:   True
ReviewFixAdversarialTests.Firewall_refusals_have_distinct_typed_administrator_guidance
  Expected: 7
  Actual:   1
```

The duplicate-exact-ACE row already refused and therefore passed at RED; the other same-SID tuples demonstrated the exclusion bug. The retained GREEN tests cover deny, rights, inheritance, propagation, inherited, and duplicate variants across applied verification, owned idempotency, and removal.

## Review-fix round 1 RED — missing typed contracts

The next test-first compile required typed canonical identities, fresh effective-access observations, separate protected ownership records, complete share-authorization intersection, complete firewall expressions, and nullable/complete dialect bounds before those APIs existed.

Command:

```powershell
dotnet test '.scratch\v0.3.0\prototypes\managed-resource-safety.tests\ManagedResourceSafety.Tests.csproj' -c Release --no-restore
```

Exact decisive output and exit:

```text
Exit code: 1
error CS0246: The type or namespace name 'EffectiveAccessSnapshot' could not be found
error CS0246: The type or namespace name 'EffectiveAccessRefusal' could not be found
error CS0246: The type or namespace name 'ShareDesiredState' could not be found
error CS0246: The type or namespace name 'EffectiveAccessVerification' could not be found
error CS0246: The type or namespace name 'LimitedGrantAccessObservation' could not be found
```

The verifier mutation self-test was also changed first to demand the five recursive guard classes.

Command and exact result:

```powershell
& '.\scripts\Test-V030Task03Verifier.ps1'

Exit code: 1
Test-V030Task03Verifier.ps1: The term 'Test-Task03IsolationGuards' is not recognized as a name of a cmdlet, function, script file, or executable program.
```

## Review-fix round 1 GREEN

The minimal implementation now uses canonical path/volume/stable identities, exact complete same-SID ACE multisets, fresh four-principal effective-access results, independent protected share/firewall ownership, a complete share authorization intersection, complete immutable firewall expressions, fail-closed dialect bounds, and exact typed guidance. The verifier checks project composition/dependencies plus recursive source and built-IL adapter evidence and mutation-tests all five guard classes with deterministic cleanup.

Focused command and exact result:

```powershell
dotnet test '.scratch\v0.3.0\prototypes\managed-resource-safety.tests\ManagedResourceSafety.Tests.csproj' -c Release --no-restore

Exit code: 0
Passed!  - Failed:     0, Passed:   162, Skipped:     0, Total:   162, Duration: 37 ms - ManagedResourceSafety.Tests.dll (net10.0)
```

Verifier self-test and focused verifier:

```text
PASS: isolation verifier rejected nested production references, source adapters, project dependencies, linked compile items, and IL adapter evidence; unique temporary fixtures were removed.
PASS: 162 isolated managed-resource tests; canonical folder identity, exact ACE/effective access, protected share/firewall ownership, authorization intersection, typed refusal guidance, five-class recursive source/project/IL isolation guards, denial guard, production isolation, and secret/private-path output scans passed.
```

The scope truth above is unchanged: these results prove isolated contracts and refusal behavior, not native Windows ACL/share/firewall/group/network behavior or a mutating VM run.

## Review-fix round 2 RED — forged decisions, detached evidence, and semantic address gaps

The round-2 adversarial suite was added against checkpoint `a0f2d6fb3211622c6df3bdac00a4339f92a7cc2c` before changing the model. The first run reproduced caller-forgeable containment decisions, contradictory share success tuples, fail-open CIDR strings, and undefined security enums.

```powershell
dotnet test '.scratch\v0.3.0\prototypes\managed-resource-safety.tests\ManagedResourceSafety.Tests.csproj' -c Release --no-restore

Exit code: 1
Failed!  - Failed:    14, Passed:   162, Skipped:     0, Total:   176
```

The fourteen failures were three forged/noncanonical folder cases, four contradictory accepted share-component tuples, five `/0`/invalid-octet/whitespace/interface-mismatch CIDR cases, and two undefined-enum cases. Desired one-context binding then failed compilation on missing `ShareAuthorizationContext`, `ProductGrantIdentity`, `BoundFolderUseValidation`, `BoundAceVerification`, `BoundEffectiveAccessVerification`, `BoundPrerequisiteResult`, and `BoundLimitedGrantAccessObservation`.

Self-review deliberately extended RED before implementation was considered complete:

```text
Exit code: 1 — Failed: 8, Passed: 212, Total: 220
```

Those cases proved blank folder volume/file/descriptor evidence, invalid product group name/object/marker, LAN local network-address misuse, and a `/32` remote host accepted as a subnet. The typed limited-grant status contract next failed compilation on missing `Accepted`, `Status`, and `LimitedGrantAccessStatus`. A further run failed 6 of 227 for blank/noncanonical planned folder fields, helper token prefixes without an owned suffix, and an empty live share stable ID that threw rather than returning a typed refusal. The final decision-boundary RED failed 1 of 228 because a caller-forged broad share plan with missing steps passed verification.

## Review-fix round 2 RED/GREEN — metadata-aware verifier

The verifier self-test was strengthened first to require real compiled adversarial assemblies and metadata P/Invoke detection. Its RED exited 1 because the old result could not provide `RealAssembliesCompiled` or `MetadataPInvokeDetected`. The replacement self-test compiles the nested fixtures and passes exactly:

```text
PASS: isolation verifier compiled real nested fixtures, rejected production references, source adapters, project dependencies, linked compile items, and metadata P/Invoke evidence in managed assemblies; unique temporary fixtures were removed.
```

## Review-fix round 2 GREEN

The final model recomputes folder safety from raw canonical path/volume/link/target evidence, binds the complete share authorization intersection to one helper-owned observation context and exact success tuples, validates canonical private CIDRs with address/prefix math and interface evidence, and explicitly refuses undefined security enums. The verifier recursively checks both projects' sources and boundaries and metadata-inspects both built assemblies.

```powershell
dotnet test '.scratch\v0.3.0\prototypes\managed-resource-safety.tests\ManagedResourceSafety.Tests.csproj' -c Release --no-restore

Exit code: 0
Passed!  - Failed:     0, Passed:   236, Skipped:     0, Total:   236, Duration: 77 ms - ManagedResourceSafety.Tests.dll (net10.0)
```

The intentional scope remains pure/disposable: no native ACL, group, share, firewall, SMB, network, production-composition, or VM mutation was added or executed.

## Review-fix round 2 final CIDR audit RED/GREEN

The final decision-API audit added explicit whole-range private-containment cases. Before the prefix-boundary fix, a `10.0.0.1/32` host paired with `10.0.0.0/7` and an `fd12:.../128` host paired with `fc00::/6` were accepted because only their network addresses, not their entire supernets, appeared private.

```text
Exit code: 1
Failed!  - Failed:     2, Passed:    13, Skipped:     0, Total:    15, Duration: 27 ms
```

After requiring an IPv4 subnet prefix no broader than its exact RFC 1918 block and an IPv6 ULA subnet prefix no broader than `fc00::/7`, the same targeted command passed 15/15. The final focused suite and strengthened verifier pass 239/239.

The same final audit also challenged absolute link-path canonicalization. Rooted `/C:/...`, duplicate-separator, and trailing-separator aliases were behaviorally RED while a drive-relative path already refused:

```text
Exit code: 1
Failed!  - Failed:     3, Passed:     1, Skipped:     0, Total:     4, Duration: 14 ms
```

Requiring one drive-root separator, no duplicate separators, and no non-root trailing separator made the targeted cases pass 4/4. With these retained cases, the final focused suite and strengthened verifier pass 243/243.

## Review-fix round 3 RED — closed result tuples and executable build graph

Round-3 adversarial tests were added against checkpoint `d9d8615539d1f31ba3a974a99ab28ffcd7c843fd` before changing the implementation.

```powershell
dotnet test '.scratch\v0.3.0\prototypes\managed-resource-safety.tests\ManagedResourceSafety.Tests.csproj' -c Release --no-restore --filter 'FullyQualifiedName~ReviewFixRound3Tests'

Exit code: 1
Failed!  - Failed:    31, Passed:    22, Skipped:     0, Total:    53, Duration: 48 ms
```

The failures reproduced ten independently forged desired-state fields, four contradictory retained-folder success tuples, duplicate retained link evidence, reuse of a malformed helper token by both planning and verification, and all fifteen defined/undefined `Accepted=true` non-success public-result combinations. Folder fields already rejected through later live-state checks and remain in the table so every desired field is pinned at the shared validator itself.

The verifier self-test was then strengthened first. Its exact RED result showed that the old verifier still reported only five guard classes and four real assemblies and exposed no structural-before-build or dependency-closure metadata evidence:

```text
Exit code: 1
{"Passed":true,"CleanupCompleted":true,"GuardClassesTested":5,"RealAssembliesCompiled":4,"MetadataPInvokeDetected":true,"Detected":["production-reference","source-adapter","il-adapter","project-dependency","linked-compile"],"CleanAfterRemoval":["production-reference","source-adapter","il-adapter","project-dependency","linked-compile"]}
```

## Review-fix round 3 GREEN

The minimal implementation shares one closed desired-state validator between share planning and accepted-plan verification, requires a canonical retained-folder success tuple before TOCTOU comparison, and formats Verified only for `(Accepted=true, ResourceRefusal.None)`. The verifier rejects unapproved executable/import build logic before project evaluation, checks both evaluated project item/reference graphs, and follows local non-framework managed dependency references while applying the exact approved-package asset allowlist.

```text
Passed!  - Failed:     0, Passed:   296, Skipped:     0, Total:   296, Duration: 78 ms - ManagedResourceSafety.Tests.dll (net10.0)
PASS: isolation verifier rejected structural build logic before execution, evaluated both project graphs, compiled real nested fixtures, rejected production references, source adapters, project/import dependencies, linked compile items, and exact transitive metadata P/Invoke evidence; unique temporary fixtures were removed.
PASS: 296 isolated managed-resource tests; closed desired/retained/public-result tuples, final folder/link/target identity recomputation, one-context authorization, semantic private CIDR/interface binding, undefined-enum refusal, pre-execution structural and exact evaluated project boundaries, full non-framework dependency-closure metadata checks against the approved package allowlist, seven-class real compiled mutation cleanup, denial guard, production isolation, and secret/private-path output scans passed.
```

The structural `<Target><Exec>` canary was never evaluated or executed. The imported dependency fixture contained only a declaration-only `DllImport`; no native method was called. All fixtures used a unique temporary root and were removed. No native, VM, system, network, production, managed-folder, or user-file mutation occurred.

## Controller closure RED — principal/path aliases and compiler-executed inputs

Both round-3 review axes were reproduced against `d23494d2e9606d80252954ddd98e31edc0ede270`. The focused controller cases were added before the model changes:

```text
Exit code: 1
Failed!  - Failed:     6, Passed:    53, Skipped:     0, Total:    59
```

The six failures covered a leading-zero SID alias/reused principal identity, four duplicate/mixed/trailing relative-separator encodings, and a noncanonical retained-link order. The public-result cross-product was tightened at the same time to assert every exact documented string rather than accepting either refusal disposition.

The verifier self-test expectation was strengthened before implementation. Its RED exited 1 because the prior result still reported seven classes and six real assemblies and had no Analyzer/namespaced-build/no-sentinel fields. Independent review had demonstrated that those omitted inputs could run during the later build.

## Controller closure GREEN

The model now round-trips every product SID component through canonical invariant decimal form, requires one machine prefix with distinct group/grant RIDs and stable object IDs, accepts only forward-slash relative paths without alias separators, and requires discovery's ordinal link order. The verifier positively allowlists every project/root-props XML element, attribute, property, and item without namespaces; rejects ambient build, response, user, Analyzer, and compiler-input additions before evaluation; then verifies the exact implicit SDK analyzer and remaining evaluated input sets.

```text
Passed!  - Failed:     0, Passed:    59, Skipped:     0, Total:    59
PASS: isolation verifier rejected namespaced build logic and compiler analyzers before evaluation, evaluated both exact project graphs, compiled real nested fixtures, rejected production references, source adapters, project/import dependencies, linked compile items, and exact transitive metadata P/Invoke evidence; no sentinel or temporary fixture remained.
PASS: 302 isolated managed-resource tests; canonical principal and folder/link identities, closed desired/retained/public-result tuples, one-context authorization, semantic private CIDR/interface binding, undefined-enum refusal, namespace-agnostic positive project XML and exact evaluated compiler-input boundaries, full non-framework dependency-closure metadata checks, nine-class real compiled no-sentinel cleanup, denial guard, production isolation, and secret/private-path output scans passed.
```

The two executable canaries were compiled or expressed only inside a unique temporary fixture, rejected before candidate evaluation, never produced their sentinel, and were removed. No native method, Windows resource operation, production composition, user-data access, or VM mutation occurred.

## Final exact compiler-policy closure RED/GREEN

Closure re-review then reproduced two remaining exact-input gaps: a seven-entry root props file could omit one required property by duplicating another allowed property, and a nested `root = true` `.editorconfig` could silently change analyzer policy. The self-test expectation was raised before implementation and exited 1 because the prior result still reported nine classes and had no root-completeness or analyzer-config fields.

The root-props self-test now omits each of the seven required properties in turn while duplicating another, proving every variant is rejected before evaluation. A nested analyzer-policy fixture is likewise rejected before evaluation, removed, and followed by a clean exact-boundary pass. Static ancestry and subtree checks refuse unapproved `.editorconfig` and `.globalconfig`; evaluated editor-config paths equal the approved worktree/Git-common set with identical normalized content; implicit nonexistent global-config candidates are SDK-owned and cannot become live files without static refusal.

```text
PASS: isolation verifier rejected namespaced build logic, compiler analyzers, incomplete root properties, and unapproved analyzer configuration before evaluation; evaluated both exact project graphs, compiled real nested fixtures, and left no sentinel or temporary fixture.
PASS: 302 isolated managed-resource tests; canonical principal and folder/link identities, closed desired/retained/public-result tuples, one-context authorization, semantic private CIDR/interface binding, undefined-enum refusal, namespace-agnostic positive project/root-property XML and exact analyzer-config/compiler-input boundaries, full non-framework dependency-closure metadata checks, eleven-class real compiled no-sentinel cleanup, denial guard, production isolation, and secret/private-path output scans passed.
```

No analyzer/configuration canary was executed, no sentinel survived, and the unique temporary root was removed.

## Ordinal analyzer-policy content closure RED/GREEN

Final specification/security review reproduced that `Sort-Object -Unique` collapsed newline-normalized `.editorconfig` contents case-insensitively. Before changing the comparison, the eleven-class self-test added two exact approved ancestor configs differing only by `none` versus `NONE` and required refusal before MSBuild evaluation. The genuine RED exited 1 with `Passed=false`, 10 detected classes, `EditorConfigCaseDriftDetected=false`, `EditorConfigCaseDriftRejectedBeforeBuild=false`, and `CleanupCompleted=true`.

The content comparison now uses `StringComparison.Ordinal`. The strengthened test is GREEN: both the unapproved nested policy and the approved-path case-only drift refuse before evaluation, both fixtures are deleted, the clean boundary passes, all eleven classes are detected and clean, and the unique temporary root is absent.

```text
PASS: isolation verifier rejected namespaced build logic, compiler analyzers, incomplete root properties, and unapproved analyzer configuration before evaluation; evaluated both exact project graphs, compiled real nested fixtures, and left no sentinel or temporary fixture.
PASS: 39 operation rows, 13 completion columns, 86 stories, 6 exit checks, and the threat-model contract verified.
PASS: 125 isolated helper-boundary tests, including adversarial/concurrency coverage and one ephemeral named-pipe/process-ID feasibility test; scanner self-test and production isolation passed; secret-output scan clean.
PASS: 302 isolated managed-resource tests; exact analyzer-config/compiler-input boundaries and eleven-class cleanup passed.
PASS: 3 Task 03 PowerShell files parse cleanly.
PASS: prototype, prototype-test, and solution formatting.
Build succeeded. 0 Warning(s), 0 Error(s).
Passed: Core 126, Presentation 15, Windows 69; total 210, failed 0, skipped 0.
PASS: diff, sentinel, and temporary-fixture residue checks clean.
```
