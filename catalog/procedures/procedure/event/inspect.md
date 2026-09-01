---
id: procedure.event.inspect
category: event
name: Inspect the event ledger
governs: query(kind: "events"), reading what a committed world change recorded
status: active
createdBy: "seed"
changeNote: "Re-seeded: the bootstrap file changed."
---

## Description
Read the append-only record of structural events. Every row says that one world change was
proposed, survived every registered guard, and committed in the same transaction as the change
itself.

## Instructions
1. Start from what you already know. With an operation id from `query(kind: "history")`, use
   `rootOperationId`; with an event in hand, use its `correlationId` to see everything that
   committed alongside it.
2. Use `entityId` to ask what has ever happened to one thing. That filter reads an index of
   affected entities, not the payloads, so it stays cheap as the ledger grows.
3. Pass `id` to read one event in full. A listing omits payloads on purpose — a chain is read for
   its shape, and a hundred payloads is not a shape.
4. Page a long chain with `afterSequence`, taking the last `sequence` you saw. Ordering is total
   and stable, so a page boundary never loses a row.
5. Bound a search in time with `from` and `to` as ISO-8601 UTC instants. `from` is inclusive and
   `to` is exclusive, so adjacent windows neither overlap nor skip.
6. Read a payload in two halves. Its top level says WHAT changed — the entity, the definition, the
   kind, the effect's index in its batch — and holds only scalars, which is exactly what a
   subscription's `payloadEquals` can filter on. `before` and `after` carry the state itself and
   are never filterable.
7. Compare the two halves to answer "what did this rule do?". `after` alone cannot: four vigour is
   a scratch or nearly fatal depending on what it replaced.

## Constraints
- Read only. There is no commit kind for events and there will not be one: an event that could be
  written directly would be a claim about a change that never happened.
- Rows are never revised or removed. A correction is a new world change, which records its own
  event.
- An event exists only because `commit(kind: "effects")` or `commit(kind: "action")` succeeded.
  Catalog import, seeding, migrations and administrative writes deliberately emit nothing, so an
  absent event is not evidence that the world did not change by those routes.
- A denied guard leaves no event. The refusal is recorded as a failed operation in
  `query(kind: "history")` instead — look there when a change you expected produced nothing.
- `before` and `after` are null when there was nothing there — a created entity has no before, a
  deleted one has no after. Null is a fact about the world, not a gap in the record.
- Each half's SHAPE is fixed per event type: a whole entity snapshot for entity events, the
  component's data for component events, container and slot for a move, the edge's data for a
  relationship. Read the type's schema rather than probing for keys.
- A `component.merged` event carries `patch` as well, because what a rule asked for and what the
  world made of it are different facts, and a shallow merge is where they diverge.
- Events recorded before these types reached their current version carry the older, narrower
  payload and have no `before` or `after` at all. Each event states its `typeVersion`; read it
  before concluding that an old row means nothing was replaced.
- Most rows are structural: the kernel's record of a world change. A row with a
  `producerExecutionId` is different in kind — a rule DECLARED it, asserting something the world
  does not show. Read the execution it names to see which rule, at which version, and on what seed.
- `correlationId` and `rootOperationId` currently hold the same value. Filter by whichever names
  what you actually have; do not infer that one is derived from the other.
