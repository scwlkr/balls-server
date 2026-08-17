# Distant product horizons

These horizons are part of the full Balls Server direction. They do not add work to a v0.x or v1.x milestone.

## v2.x — Balls Nodes

- **State:** Horizon
**Depends on:** v1.0.0 and a separately approved product definition

Identify owner-approved computers as Balls Nodes and show only the resources each node explicitly chooses to advertise. Before implementation, define node identity, discovery, consent, revocation, privacy, architecture, and multi-node tests. v2.x performs no shared computation or arbitrary command execution.

## v3.x — Share Compute

- **State:** Horizon
**Depends on:** v2.x and a separately approved compute product/security definition

Let owners explicitly opt nodes into narrowly approved distributed workloads with isolation, scheduling, revocation, failure handling, and auditing. Share Compute is not general remote administration, unrestricted command execution, or transparent pooling of several machines' memory into one process.

Individual v2.x and v3.x versions are defined only when their prerequisites are complete. Each completed version receives its own semantic tag; an official release additionally follows the release boundary in `docs/ROADMAP.md`.
