# v0.2.0 — Host Dashboard Specification

Status: ready-for-agent

**Roadmap authority:** [`docs/roadmap/v0.2.0.md`](../../docs/roadmap/v0.2.0.md)

## Problem Statement

The completed v0.1.0 diagnostic proves that Balls Server can inspect eight Windows prerequisites without elevation or mutation, but it presents those checks as one flat report with one combined readiness result. That model does not clearly answer the owner's practical questions: whether the computer is suitable, whether the selected managed-folder candidate is suitable, whether local access could work, whether Tailscale access could work, and whether Balls Server hosting has actually been configured.

The combined result also carries two policies that are no longer correct for the product. It treats the dashboard process itself being elevated as a readiness prerequisite, even though the dashboard must remain unelevated, and it requires both trusted local-network and Tailscale observations to be ready at the same time. An owner needs independent access-path readiness because either supported private access path can be useful by itself.

v0.2.0 must turn the existing diagnostic into a simple Host Dashboard while preserving the security properties already established by v0.1.0. It must remain read-only, fail closed when observations are unreliable, and avoid suggesting that setup or a working client connection already exists.

## Solution

Build an unelevated Host Dashboard that runs Host Files preflight automatically on launch and again when the owner selects Refresh. The dashboard presents five distinct views of state:

1. computer prerequisites;
2. the selected managed-folder candidate;
3. local access-path readiness;
4. Tailscale access-path readiness; and
5. Balls Server hosting state.

The computer and folder views explain shared prerequisites. The two access-path views independently reduce the prerequisites needed for their path, so one can be Ready while the other is Action required or Unknown. The hosting-state view says Not configured throughout v0.2.0 because this version neither configures hosting nor adopts existing Windows shares.

Plain-language summaries remain visible at a glance. Technical evidence, reason codes, observation timestamps, and check-level results are available through expandable details. Administrator authorization is displayed only as information about a future setup flow and never affects prerequisite or access-path readiness.

The WPF shell becomes a thin adapter over one deep Host Dashboard presentation module. That module owns launch, Refresh, cancellation, folder-selection state, progress, and the last completed dashboard snapshot. Existing Core policy/orchestration and Windows probe seams remain responsible for deterministic policy and read-only operating-system observations.

## User Stories

### Opening and understanding the dashboard

1. As an owner, I want the Host Dashboard to start checking automatically when it opens so that I do not have to discover a separate Run command before seeing useful information.

2. As an owner, I want the initial managed-folder candidate to be my current Windows profile's Documents folder so that the first check has a useful, existing target without configuring anything.

3. As an owner, I want to see that a check is running immediately after launch so that a temporarily empty dashboard is not mistaken for a finished result.

4. As an owner, I want each major area to have a visible text status and short explanation so that I can understand the computer without opening technical details.

5. As an owner, I want status to use both text and a non-color cue so that Ready, Warning, Action required, Unknown, and hosting state remain understandable without relying on color perception.

6. As an owner, I want the dashboard to avoid a single result that combines LAN and Tailscale so that one unavailable access path does not hide the readiness of the other.

7. As an owner, I want the dashboard to distinguish prerequisite readiness from hosting state so that Ready never implies that a share exists or that another computer has connected successfully.

8. As an owner, I want the evaluated folder path and observation time shown with the results so that I know exactly what the current snapshot describes.

### Computer prerequisites

9. As an owner, I want a Computer section that summarizes supported Windows, firewall posture, and SMB policy so that shared host requirements are visible in one place.

10. As an owner, I want unsupported Windows edition or build information to produce Action required when it is known and Unknown when it cannot be observed reliably.

11. As an owner, I want unsafe or disabled Windows Firewall posture to remain visible and to affect both access paths because it is a shared prerequisite.

12. As an owner, I want the SMB result to require a running Server service, SMB 3.0-or-newer negotiation policy, and disabled SMB1 so that Ready represents the approved protocol boundary.

13. As an owner, I want enabled SMB1 to produce Action required, not a warning, because v0.2.0 must not describe an insecure legacy configuration as ready.

14. As an owner, I want missing, malformed, contradictory, or unrecognized SMB dialect observations to produce Unknown so that the dashboard fails closed rather than guessing.

15. As an owner, I want administrator information shown as a future setup consideration so that I understand approval may be requested later without being told to elevate this dashboard.

16. As an owner, I want being a standard user or running unelevated to have no negative effect on computer or access-path readiness because v0.2.0 is intentionally an unelevated diagnostic.

### Managed-folder candidate

17. As an owner, I want a Managed folder section that summarizes the selected folder's storage and current-token access observations so that folder-specific blockers are separate from computer and network blockers.

18. As an owner, I want to choose a different existing folder with the standard Windows folder picker so that I can evaluate the folder I may eventually share.

19. As an owner, I want typing or selecting a different folder to mark the displayed snapshot as needing Refresh so that results for the previous folder are never presented as results for the new one.

20. As an owner, I want a missing, blank, or no-longer-existing folder to be rejected in plain language before a refresh begins so that I can correct the selection without receiving misleading probe results.

21. As an owner, I want folder inspection to remain observation-only so that the dashboard never creates the folder, writes a probe file, changes permissions, or takes ownership.

22. As an owner, I want storage and permission observation failures to become Unknown while independent computer and network observations continue so that one inaccessible folder does not erase all diagnostic value.

### Independent access paths

23. As an owner, I want Local access and Tailscale access shown as separate cards so that the state of each private route is immediately clear.

24. As an owner, I want Local access readiness to include shared computer prerequisites, the managed-folder candidate, and trusted local-network posture so that its result covers the prerequisites needed for that path.

25. As an owner, I want Tailscale access readiness to include shared computer prerequisites, the managed-folder candidate, and Tailscale state so that its result covers the prerequisites needed for that path.

26. As an owner, I want Local access to be able to report Ready when Tailscale is absent, offline, or Unknown so that remote-access state does not block a valid private-LAN path.

27. As an owner, I want Tailscale access to be able to report Ready when the local network is public, unavailable, or Unknown so that local-network state does not block a valid Tailscale path.

28. As an owner, I want each access path to fail closed on any Unknown prerequisite used by that path so that the dashboard never calls an incompletely observed path ready.

29. As an owner, I want warnings to be preserved in each affected access-path aggregate so that an advisory condition is not hidden by otherwise-ready prerequisites.

30. As an owner, I want the dashboard to describe LAN and Tailscale only as private SMB access paths so that it never recommends a public endpoint, TCP 445 exposure, guest access, or router port forwarding.

### Hosting state

31. As an owner, I want Hosting state to say Not configured so that I understand v0.2.0 has evaluated prerequisites but has not created a managed share.

32. As an owner, I want Not configured to use hosting-state language rather than Action required so that an intentionally unavailable future capability is not confused with a failed prerequisite.

33. As an owner, I want the dashboard to avoid claiming that an existing Windows share belongs to Balls Server so that unrelated or manually configured shares remain untouched and outside product ownership.

34. As an owner, I want the dashboard to avoid a connection-verified claim so that a ready prerequisite report is not mistaken for proof that a client has authenticated and accessed files.

### Refresh, progress, and cancellation

35. As an owner, I want one clearly named Refresh action so that I can re-observe the computer after changing Windows state outside Balls Server.

36. As an owner, I want only one diagnostic run active at a time so that repeated clicks cannot create overlapping observations or race the visible snapshot.

37. As an owner, I want folder editing and Refresh disabled while a run is active so that the running request continues to describe one stable target.

38. As an owner, I want visible progress that identifies the current check and its position in the run so that a slow operating-system query does not look like a frozen application.

39. As an owner, I want to cancel an active run and receive an explicit Canceled message so that I remain in control of a slow or unnecessary diagnostic.

40. As an owner, I want cancellation to preserve the last completed snapshot, when one exists, and keep it clearly labeled with its original folder and timestamp so that cancellation does not destroy previously useful information.

41. As an owner, I want an initial launch run canceled before completion to show Not checked rather than fabricated readiness results so that an incomplete run cannot be mistaken for evidence.

42. As an owner, I want late results from a canceled or superseded run ignored so that they cannot overwrite a newer completed snapshot.

43. As an owner, I want an unexpected check failure to affect only its applicable result while remaining checks continue so that environmental failures are treated as diagnostic data.

### Details, clarity, and accessibility

44. As an owner, I want expandable details for every summary area so that the default dashboard stays simple while technical evidence remains available when needed.

45. As an owner, I want expanding or collapsing details to be a presentation-only action so that it never re-runs a probe or changes Windows state.

46. As an owner, I want details to identify the individual prerequisite result, plain-language reason, reason code, and safe evidence so that I can understand or report a blocker precisely.

47. As an owner, I want raw exceptions, stack traces, secrets, Tailscale peer details, usernames, and unrelated file metadata excluded from the dashboard so that diagnostics do not expose unnecessary private information.

48. As a keyboard user, I want the folder picker, Refresh, Cancel, and detail toggles reachable in a predictable tab order with visible focus so that the dashboard is usable without a mouse.

49. As a screen-reader user, I want controls and changing run status to have meaningful accessible names and announcements so that launch, progress, cancellation, and completion are perceivable.

50. As an owner, I want user-facing copy to use the canonical terms Host Dashboard, Host Files preflight, managed folder, access path, prerequisite result, access-path readiness, and hosting state so that product language remains consistent across the application and documentation.

### Safety boundaries

51. As an owner, I want every launch, Refresh, cancellation, error, and close path to remain unelevated so that viewing state never opens a UAC prompt or launches a privileged helper.

52. As an owner, I want the dashboard to make no Windows, network, account, credential, permission, share, service, registry, policy, firewall, Tailscale, or dependency changes so that v0.2.0 remains a read-only milestone.

53. As an owner, I want no background timer or hidden monitoring after the visible run completes so that operating-system inspection happens only on launch or explicit Refresh.

54. As an owner, I want no diagnostic upload, telemetry, or automatic export so that folder paths and machine observations remain local unless I deliberately share them outside the application.

55. As an owner, I want setup, repair, pairing, mapping, installation, and elevation actions absent from the dashboard so that the interface does not promise capabilities scheduled for later versions.

## Implementation Decisions

### Module and interface boundaries

- Introduce one deep presentation module for Host Dashboard state. Its small interface exposes the current selected folder, lifecycle state, progress, last completed snapshot, detail-expansion state, and the launch, Refresh, Cancel, and folder-selection operations needed by WPF.
- Make that presentation interface the primary behavior test surface. The WPF window is an adapter that binds visible controls to the interface and contains no Windows queries, prerequisite thresholds, readiness reduction, or hosting-state inference.
- Preserve the existing Core preflight interface as the application/policy seam, evolving its report from one flat overall result into a structured Host Dashboard snapshot. Do not introduce separate orchestration interfaces for every dashboard card.
- Continue accepting read-only probe interfaces as dependencies and returning typed results. Windows adapters report observations only; they do not interpret UI state or attempt recovery.
- Add progress reporting to the existing orchestration flow without making progress delivery a second diagnostic engine. Progress describes the ordered check being observed and never changes result policy.

### Dashboard snapshot

- A completed snapshot records the evaluated folder path, start and completion timestamps, check-level prerequisite results, the Computer aggregate, Managed folder aggregate, Local access-path readiness, Tailscale access-path readiness, administrator information, and hosting state.
- Publish completed snapshots atomically. Intermediate observations drive progress but are not presented as the current completed result.
- Preserve the previous completed snapshot during Refresh and cancellation. Clearly distinguish its evaluated path from the currently selected path, and mark it as needing Refresh when those paths differ.
- Ignore completion and progress events whose run identity is no longer current. A canceled or superseded run cannot replace the active snapshot.
- Do not persist the snapshot or selected path between application sessions in v0.2.0. The default candidate on each launch is the current Windows profile's Documents folder.

### Prerequisite and aggregate policy

- Keep the canonical prerequisite results: Ready, Warning, Action required, and Unknown.
- Keep the canonical aggregate outcomes: Ready, Ready with warnings, Not ready, and Indeterminate.
- Reduce each aggregate independently with the established fail-closed precedence: Action required produces Not ready; otherwise Unknown produces Indeterminate; otherwise Warning produces Ready with warnings; otherwise the aggregate is Ready.
- The Computer aggregate contains the supported Windows, Windows Firewall, and SMB prerequisite results.
- The Managed folder aggregate contains storage-location and current-token folder-permission results for the selected candidate.
- Local access-path readiness reduces the Computer aggregate's underlying prerequisites, the Managed folder aggregate's underlying prerequisites, and trusted local-network posture.
- Tailscale access-path readiness reduces the Computer aggregate's underlying prerequisites, the Managed folder aggregate's underlying prerequisites, and Tailscale state.
- Do not calculate or display one combined access-path aggregate. Local and Tailscale results are peers, and either can be Ready independently.
- Administrator observation is a separate informational fact. It is excluded from the Computer, Managed folder, Local access, and Tailscale reductions even when unavailable, non-administrator, or unelevated.
- Hosting state is Not configured in v0.2.0. It is a domain state, not a prerequisite result, and is excluded from every readiness reduction.

### SMB policy

- Extend the SMB observation to represent the Server service state, whether SMB1 is enabled, and the minimum and maximum SMB 2/3 dialects accepted by the Windows SMB server.
- Model dialect observations as typed protocol values rather than comparing display strings in presentation code.
- SMB is Ready only when the Server service is running, SMB1 is disabled, and the observed server dialect range cannot negotiate below SMB 3.0.
- SMB1 enabled is Action required. A disabled Server service, an explicitly disabled SMB 2/3 server, a maximum dialect below SMB 3.0, a minimum dialect below SMB 3.0, or a contradictory dialect range is Action required when the observation is trustworthy.
- Missing properties, unsupported values, parser failures, permission failures, timeouts, and otherwise unreliable dialect observations are Unknown. Do not substitute assumed Windows defaults.
- Server encryption configuration may remain safe evidence but is not a v0.2.0 readiness requirement. The dashboard must not claim that it verified a public-exposure boundary or a client negotiation.

### Run lifecycle

- Start the launch diagnostic only after the WPF window is loaded so that the shell can render before Windows queries begin.
- Permit exactly one active run. Refresh, folder editing, and folder browsing are unavailable during that run; Cancel is available only during that run.
- Validate that the requested candidate is a nonblank existing folder before starting orchestration. Validation is read-only and does not create or normalize the user's files.
- A caller cancellation ends the active lifecycle as Canceled rather than manufacturing Unknown prerequisite results. Probe timeouts, access denials, malformed data, and non-cancellation exceptions remain typed Unknown results so independent checks can continue.
- Closing the window cancels any active run and performs no cleanup mutation.
- Run only on window launch and explicit Refresh. Detail expansion, focus changes, and ordinary window activation do not start a diagnostic.

### Presentation

- Use a compact summary-first layout with visible Computer, Managed folder, Local access, Tailscale access, and Hosting state areas. Local and Tailscale must have equal visual weight.
- Show result text and an icon or shape in addition to color. Reserve prerequisite-result styling for prerequisite and aggregate results; render Not configured as hosting-state language.
- Present short, owner-oriented explanations at the summary level. Put reason codes, per-check evidence, timestamps, and more technical descriptions in expandable details.
- Use canonical domain language in visible text. UI may render the domain value Action required as “Action needed” only if the mapping remains explicit and consistent; stored and tested domain outcomes remain Action required.
- Keep raw operating-system data bounded to the evidence already required to explain the result. Do not display full command output or identifiers discarded by the probe boundary.
- Preserve keyboard navigation, visible focus, semantic control names, and perceivable run-state updates. Accessibility must not depend on color or animation.

### Security and ownership

- Retain the read-only dashboard invariant across production code and tests. No error-handling or fallback path may mutate the system, request elevation, or invoke future setup.
- Continue using fixed allow-listed Windows queries and bounded external-process execution. User-controlled values never become command text.
- Do not inspect, adopt, rename, alter, or claim ownership of existing Windows shares. Balls Server owns no hosting configuration in v0.2.0.
- Keep all observations local. No telemetry, automatic diagnostics upload, credential persistence, or report export is introduced by this milestone.

## Testing Decisions

### Primary behavior seam

- Test most v0.2.0 behavior through the Host Dashboard presentation interface, with the real Core orchestration and policy composed behind it and deterministic fake probe adapters below it.
- These tests cover automatic launch, initial Documents selection, Refresh, one-run-at-a-time behavior, progress, cancellation, stale-folder marking, atomic snapshot publication, late-result suppression, detail expansion, and the enabled state of owner actions.
- Assert externally visible state and typed snapshot results. Do not assert private method calls, property-notification counts, collection implementation, dispatcher details, or WPF brush instances.

### Core policy seam

- Keep focused Core tests for every individual prerequisite policy and for the fail-closed reducer.
- Add table-driven coverage for independent Local and Tailscale combinations, including Ready/Ready, Ready/Not ready, Not ready/Ready, Indeterminate/Ready, Ready with warnings, and shared-prerequisite failures that affect both paths.
- Prove that administrator observations of elevated, unelevated, administrator, standard-user, and unavailable never alter Computer, Managed folder, Local, or Tailscale results.
- Prove that hosting state remains Not configured and cannot affect readiness.
- Cover folder-specific Unknown results, independent-check exception isolation, ordered progress, caller cancellation, and a completed snapshot containing the requested path and timestamps.
- Cover the complete SMB policy matrix: service states, SMB1 enabled/disabled/unknown, dialect minimum and maximum below/equal to/above SMB 3.0, contradictory ranges, missing values, unrecognized values, and precedence when more than one unsafe condition exists.

### Windows adapter seam

- Extend existing Windows parser and probe tests for the dialect fields using representative valid, missing, null, malformed, reordered, and forward-unknown query output.
- Verify that SMB inspection remains behind the fixed query allow-list and that no caller can supply script text.
- Keep tests for timeouts, access denial, unavailable services, malformed output, and cancellation. These tests assert observations or Unknown results, never changes to the development machine.
- Update the narrow production-wiring smoke test to assert the v0.2.0 report shape and stable check identity/order without asserting that the current computer is Ready.

### WPF and manual validation seam

- Use presentation-interface tests for behavior that does not require a rendered WPF window. Do not add pixel snapshots or tests coupled to exact spacing, colors, or typography.
- Run a recorded manual smoke test on supported Windows for window launch, automatic diagnosis, initial folder, selecting a different folder, stale-result indication, Refresh, visible progress, Cancel, independent path rendering, expandable details, keyboard navigation, and window close during a run.
- Confirm during the smoke test that no UAC prompt appears and that the dashboard offers no setup, repair, share, credential, mapping, installation, or public-access action.
- Run the full automated suite and formatting/build gates before owner acceptance. Preserve the supported-Windows production smoke as read-only and unelevated.

### What not to test

- Do not assert a particular readiness state for the development machine, Windows Sandbox image, Hyper-V image, or owner-selected folder.
- Do not create shares, accounts, ACL entries, firewall rules, registry values, files, services, mappings, credentials, or Tailscale state as test setup.
- Do not test Windows internals through WPF. Windows adapters have their own seam, and presentation tests consume typed observations and results.
- Do not test future setup, repair, pairing, connection verification, installation, background monitoring, or compute behavior in the v0.2.0 suite.

## Out of Scope

- Creating, changing, repairing, or removing a managed share.
- Detecting, adopting, reporting in detail on, or taking ownership of existing Windows shares.
- Creating folders, writing probe files, changing NTFS permissions, or taking ownership of files.
- Creating accounts, access grants, groups, credentials, mappings, or saved connections.
- Changing Windows features, SMB configuration, services, firewall rules, network profiles, registry, policy, adapters, routes, or Tailscale state.
- Starting an elevated process, privileged helper, setup flow, repair flow, installer, or UAC prompt.
- Verifying a real client connection or reporting hosting as Configured, Degraded, or Connection-verified.
- Recommending or enabling public SMB, inbound public TCP 445, port forwarding, guest access, blank passwords, or disabled SMB protections.
- Background monitoring, scheduled refresh, notifications, telemetry, diagnostic upload, automatic export, or cross-session dashboard persistence.
- Connect to Files, drive mapping, client pairing, access-grant management, release packaging, code signing, Balls Nodes, or Share Compute.
- Retrospective changes to the completed v0.1.0 contract or tag.

## Further Notes

- This specification executes the approved v0.2.0 roadmap and does not replace it. Scope, milestone state, completion checks, and evidence remain authoritative in the versioned roadmap.
- `ready-for-agent` means the behavior and testing seams are defined well enough to decompose into implementation tickets. It does not mean implementation has started or v0.2.0 is complete.
- v0.2.0 remains a development milestone tag boundary, not an official release. Official signed releases remain later roadmap work.
- The existing v0.1.0 eight-check contract remains historical evidence. v0.2.0 may reuse its observations while changing report structure, administrator treatment, access-path reduction, and SMB policy as explicitly approved here and in the roadmap.
- Any implementation discovery that conflicts with the glossary or an accepted ADR must be surfaced and resolved rather than silently changing this specification.
