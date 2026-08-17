# Issue tracker: Local Markdown

Specs and tickets live as Markdown files under `.scratch/`. `docs/ROADMAP.md` remains authoritative for milestone scope, state, progress, and evidence; local tickets execute that roadmap and cannot silently redefine it.

## Conventions

- One feature directory: `.scratch/<feature-slug>/`
- Spec: `.scratch/<feature-slug>/spec.md`
- Tickets: `.scratch/<feature-slug>/issues/<NN>-<slug>.md`
- One file per ticket, numbered from `01`
- `Status:` records the current triage role
- `Blocked by:` records ticket-number dependencies
- Comments append under `## Comments`

When publishing work, create the appropriate file under the feature directory. When fetching work, read the exact referenced ticket. Approved scope or completion changes must also update the active roadmap version file.

## Wayfinding

- Map: `.scratch/<effort>/map.md`
- Child: `.scratch/<effort>/issues/<NN>-<slug>.md`
- `Type:` is `research`, `prototype`, `grilling`, or `task`
- Wayfinding `Status:` is `claimed` or `resolved`
- A ticket is available when open, unblocked, and unclaimed
- Claim before work; resolve with an `## Answer` and add its result to the map
