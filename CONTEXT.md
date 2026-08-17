# Balls Server Context

Balls Server helps an owner provide safe file access between Windows computers and, in later versions, explicitly share node resources for distributed work.

## File sharing

**Host computer**:
The Windows computer that stores the managed folder and serves it through Windows file sharing. Its files persist while it is powered off or asleep, but clients cannot access them until it is available again; closing Balls Server does not stop an established share.
_Avoid_: Cloud server, always-on service

**Host Files**:
The role representing the owner's goal of making selected files available from this computer.
_Avoid_: Server mode, sharing mode

**Host Dashboard**:
The unelevated view of this computer's file-sharing prerequisites, access-path readiness, hosting state, and access grants.
_Avoid_: Setup app, server console

**Host Files preflight**:
A read-only evaluation of whether this computer meets prerequisites for Host Files. It reports observations and never configures the computer.
_Avoid_: Setup, repair

**Host Files setup**:
The consent-driven configuration that makes a managed folder available to approved clients.
_Avoid_: Preflight, installer

**Privileged helper**:
The separate, on-demand elevated boundary that applies an approved Host Files system change after a complete preview and explicit consent.
_Avoid_: Elevated dashboard, setup app, background administration service

**Product-owned change ledger**:
The protected record that identifies exactly which system objects and settings Balls Server created or changed, so verification, repair, and removal affect only those owned changes.
_Avoid_: Machine snapshot, ownership claim based only on a name

**Connect to Files**:
The role representing a person's goal of accessing a managed folder from another Windows computer.
_Avoid_: Client mode, remote administration

**Managed folder**:
The one owner-selected folder whose contents Balls Server makes available. Balls Server never owns or deletes the files in it.
_Avoid_: Data directory, server folder

**Managed share**:
The SMB share created and tracked by Balls Server for the managed folder.
_Avoid_: Existing share, administrative share

## Access and readiness

**Access path**:
A private network route used for an SMB connection. The supported paths are a trusted local network and Tailscale.
_Avoid_: Public endpoint, internet share

**Access endpoint**:
The exact path-specific SMB root used through one access path. Local and Tailscale endpoints may reach the same host, but they are separate connection identities and are never silent fallbacks for one another.
_Avoid_: Host identity, automatic failover target, IP workaround

**Access grant**:
Revocable permission for one client Windows profile to access a managed share. It is a credential identity, not proof that a physical device is trustworthy.
_Avoid_: Trusted device, guest access

**Setup code**:
A display-once bearer bundle that transfers one access grant's endpoint information and SMB credential to its intended client Windows profile. Hiding the display does not expire or revoke a copied credential.
_Avoid_: One-time password, device-trust proof, Tailscale credential

**Prerequisite result**:
One check outcome: Ready, Warning, Action required, or Unknown.
_Avoid_: Pass/fail, Unknown/error

**Access-path readiness**:
The aggregate prerequisite result for local access or Tailscale access. A host may be ready for either path or both.
_Avoid_: Full readiness

**Hosting state**:
Whether Balls Server hosting is not configured, configured, degraded, or connection-verified. It is distinct from prerequisite readiness.
_Avoid_: Readiness

## Planning and delivery

**Milestone**:
A bounded body of work with a user outcome, dependencies, scope, completion checks, and evidence.
_Avoid_: Slice, phase, stage

**Version**:
A semantic identifier assigned to a milestone. Completion attaches that identifier to the verified commit as a Git tag.
_Avoid_: Milestone state

**Official release**:
A completed version intentionally distributed with a signed package, release notes, verification evidence, and owner approval.
_Avoid_: Tag, checkpoint

## Long-term direction

**Balls Node**:
A known computer that explicitly advertises selected resource information to the owner's Balls Server environment.
_Avoid_: Worker, server

**Share Compute**:
Explicitly opt-in execution of approved distributed workloads across Balls Nodes. It is not unrestricted remote command execution or transparent memory pooling.
_Avoid_: Remote administration, pooled RAM
