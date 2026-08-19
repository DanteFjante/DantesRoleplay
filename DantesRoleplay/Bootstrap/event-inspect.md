---
id: procedure.event.inspect
category: event
name: Inspect the event ledger
governs: query(kind: "events"), reading what a committed world change recorded
status: active
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
- `correlationId` and `rootOperationId` currently hold the same value. Filter by whichever names
  what you actually have; do not infer that one is derived from the other.
