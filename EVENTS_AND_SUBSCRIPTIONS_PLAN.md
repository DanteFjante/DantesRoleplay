# Events and subscriptions

## Goal

Enable reliable reactive play: a committed world change can emit a named event, eligible
subscriptions can run a stored reactive mechanic, and any resulting effects can emit further
events. The entire chain remains deterministic, auditable, bounded, and transactional.

Examples include a condition expiring, a location trigger firing when an entity enters it, or a
quest state advancing after a required relationship is created.

## Core model

- **Event type:** a registered, versioned identifier with a description and JSON payload schema.
  Event names are lowercase dot paths, such as `world.component.changed` or
  `ruleset.dnd2024.condition.expired`. There are no ad-hoc event strings.
- **Event:** immutable ledger record with ID, type, payload, timestamp, root correlation ID,
  causation ID, depth, root operation ID, and affected entity IDs.
- **Subscription:** immutable, versioned declaration with ID, event-type ID, optional scope and
  metadata filters, target reactive-mechanic ID, status, and change note. Its mechanic must already
  be active and declare the event context it needs.
- **Reactive mechanic:** an ordinary stored mechanic with explicit event requirements; it receives
  a frozen event projection and returns normal effects plus narration. It cannot emit arbitrary
  events directly.

## Processing and failure semantics

```text
Root action/effects transaction
  → validated effect applies
  → host emits registered event(s)
  → matching active subscriptions are ordered by subscription ID
  → each reactive mechanic executes against its frozen event projection
  → returned effects are validated and applied
  → resulting registered events continue the chain
  → ledger and root audit record commit together
```

- The initial release emits events only from a closed mapping of typed effect results and action
  lifecycle outcomes. It does not expose a free-form `event.emit` effect.
- Every event chain shares one correlation ID. Each child records its direct causation ID.
- A reactive failure, invalid proposed effect, unknown event type, or guard breach aborts the whole
  root transaction: no partial world effects, subscriptions, or ledger records remain committed.
- Emit the same event type and apply subscriptions in stable lexical ID order, so a seeded replay
  has the same outcome.
- Guard each root action with defaults of maximum depth **8**, maximum emitted events **100**, and
  maximum executions of one subscription **1**. Exceeding any limit aborts the root transaction
  with a named, auditable error.

## MCP surface and contracts

Keep the three MCP tools. Add closed semantic kinds only after following
`procedure.mcp.add-tool`:

- `query(kind: "event-types")`, `query(kind: "events")`, and `query(kind: "subscriptions")`
  for discovery, chain inspection, and history.
- `commit(kind: "event-type")` to define or revise a registered event type.
- `commit(kind: "subscription")` to create, revise, enable, disable, or archive a subscription.

Create these governing contracts before code work:

- `procedure.event.define`
- `procedure.subscription.create`
- `procedure.subscription.modify`
- `procedure.event.inspect`
- `procedure.event.chain-limits`

The contracts must state source, scope, payload schema, required mechanic projection, ordering,
failure behavior, test cases, and the exact recovery call for every rejected write.

## Implementation sequence

1. Write and verify the five contracts; update `orient` only after a capability exists.
2. Add persistence and migrations for event types, events, subscriptions, and subscription versions.
3. Add registry validation and read/write MCP kinds, with dry runs where applicable.
4. Implement transactional event production from the closed effect/action mapping.
5. Resolve matching subscriptions, execute their mechanics, and apply resulting effects within the
   root transaction.
6. Enforce chain limits, stable ordering, audit linkage, and replay data.
7. Add protocol, integration, rollback, and replay tests; then expose the new capability through
   `query(kind: "capabilities")` and relevant D&D contracts.

## Acceptance tests

- Unknown event types and subscriptions targeting missing/inactive mechanics fail before writing.
- A valid effect emits the expected ledger event and runs exactly the matching subscriptions in
  stable order.
- Scope and metadata filters exclude nonmatching subscriptions.
- A failing subscriber or invalid child effect leaves both world state and event ledger unchanged.
- Loop depth, event-count, and repeated-subscriber limits abort safely and identify the offending
  event/subscription in the returned error and audit record.
- A recorded chain can be queried by correlation ID and replayed with the same seed to reproduce
  its event order, mechanic versions, effects, and narration.
- Existing actions with no subscriptions retain their current behavior and performance.

## Scope boundaries

This is a post-MVP reactive subsystem. It does not add timers, background jobs, external webhooks,
or server-held sessions. Time-based rules become explicit actions until a separate scheduled-events
design is approved.
