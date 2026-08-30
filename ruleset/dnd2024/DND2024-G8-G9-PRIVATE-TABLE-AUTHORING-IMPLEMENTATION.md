# D&D 2024 G8/G9 private-table authoring acceptance

Status: accepted

## Boundary

This slice closes G8 and accepts the already-implemented G9 authoring transaction. It stabilizes
the private-table host selection across configuration refresh, binds it to one exact active source
profile and activation fingerprint, and then re-runs the public authorized authoring acceptance.

Included:

- local private-table seat configuration and policy;
- activated application document/binding evidence;
- exact state-space activation and source-profile matching;
- existing `system.world-state.sync` transaction and protocol acceptance;
- focused lifecycle, audience, activation-document, authoring, and protocol tests;
- graph and completion receipts.

Excluded:

- remote/multi-user authentication;
- changing the selected live campaign, source registrations, activation, or state space;
- live World/campaign authoring or migration;
- backup/restore acceptance;
- ruleset-specific validation inside the generic authoring transaction.

## G8 contract

The host snapshots one configured seat at process construction. Configuration reload cannot switch
principal, role, application, source profile, campaign, or actor underneath a running process. A
process restart reconstructs the same snapshot from the same configuration.

An enabled seat declares a nonempty, unique, bounded source-id profile. The activated binding is
valid only when:

- the requested campaign equals the snapshot campaign;
- the connection peer is loopback;
- the role/actor shape is valid;
- the active application metadata document matches its retained source registration and bytes;
- the active manifest's source IDs exactly equal the snapshot source profile;
- exactly one active campaign root matches across the application's state spaces;
- that state space's manifest fingerprint equals the metadata document's current activation
  fingerprint.

The binding revision incorporates activation, document, state-space, campaign, and component
evidence. Any mismatch fails closed and exposes no selected identity.

The accepted development profile is `dnd2024-core`; optional extension profiles must be selected
explicitly as their full exact source-id list.

## G9 acceptance boundary

`system.world-state.sync` remains the sole reviewed administrative manifest transaction for
additive/update-only live application ECS World authoring. It requires private-operator
authorization at the protocol boundary, exact application/state-space/root selection, registered
component owners, expected entity/component/edge revisions, bounded root containment, typed
effects, one transaction, audit evidence, and replay/conflict handling.

G9 acceptance does not authorize any live write in this slice. It confirms that the existing
implementation is usable after G4, G7N, and G8 are verified.

## Acceptance

- a running host retains its original seat after configuration mutation;
- restart from the same values reconstructs an equal seat;
- loopback Game Master context succeeds without an actor;
- remote, mismatched campaign/application, malformed role, and changed source profile fail closed;
- stale activation/state-space fingerprint fails closed;
- world-authoring dry run/commit/replay/conflict/stale/scope/rollback tests pass;
- protocol authorization and closed input tests pass;
- no live database or activation is changed.
