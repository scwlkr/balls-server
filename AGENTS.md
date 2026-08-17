# Balls Server

## Guardrails

- Public repo: assume every tracked file, commit, branch, issue, release, artifact, diagnostic, and file property can become public.
- Never store credentials, tokens, private keys, personal or host identifiers, private network details, real file metadata, or unsanitized diagnostics in the repository. Use synthetic fixtures and redacted evidence.
- Read `docs/ROADMAP.md` and the active version file before product work; update its progress and evidence with each checkpoint.
- Use authenticated SMB 3.0+ with SMB1 disabled, over private LAN or Tailscale. Never expose TCP 445 publicly, request port forwarding, or enable guest access.
- Keep dashboards/preflight unelevated and observational. Future system changes require an approved milestone and the separate privileged-helper boundary.
- Change only product-owned configuration; preserve the managed folder and user files through repair, removal, upgrade, and uninstall.

## Engineering

- Separate WPF, policy, and Windows probes; isolate Windows access behind testable interfaces.
- Use typed Ready/Warning/Action required/Unknown results; treat environmental failures as data.
- Default tests are non-mutating and unelevated; mutation tests use disposable isolated machines.
- Before changing product scope, preflight policy, architecture, or manual tests, read `docs/PRODUCT.md`, `docs/PREFLIGHT.md`, `docs/ARCHITECTURE.md`, or `docs/testing/README.md`, respectively.

## Agent skills

### Issue tracker

Local Markdown specs and tickets live under `.scratch/<feature>/`; the versioned roadmap remains the project-status authority. See `docs/agents/issue-tracker.md`.

### Triage labels

Local issue `Status:` values use the five canonical triage roles. See `docs/agents/triage-labels.md`.

### Domain docs

This is a single-context repo with `CONTEXT.md` and system-wide decisions under `docs/adr/`. See `docs/agents/domain.md`.

## Git

- Commit coherent, verified checkpoints separately.
- Fetch/reconcile without data loss before work and handoff; push checkpoints; finish with local/upstream equal and every worktree change accounted for. Preserve shared history; report blockers.
