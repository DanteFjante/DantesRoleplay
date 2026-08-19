---
id: procedure.event.guard
category: event
name: Author a pre-commit event guard
governs: guard mechanics registered through event subscriptions
status: active
---

## Description
A guard reads one immutable proposed structural event after its enclosing effect batch has been
applied inside an uncommitted transaction. It must explicitly allow the proposal or deny the whole
root world change.

## Instructions
1. Declare `requirements.event` with `mode: "guard"`, the exact structural event types, and the
   `components` the guard needs projected onto the entities the event touches.
2. Return exactly `{ decision: "allow" }` to permit the change, or
   `{ decision: "deny", code: "...", reason: "..." }` to block it.
3. Read the proposal from `ctx.event`. It always carries `mode`, `type`, `typeVersion`, `scope`,
   `payload` as an object, `entityIds`, `correlationId`, `causationId` and `depth`. A guard also
   gets `proposalOrdinal`, and deliberately gets no `id` and no `sequence`: it is being asked about
   something that does not exist yet and may never.
4. Read affected world state from `ctx.eventEntities`, a map of entity id to that entity carrying
   only the components declared in step 1. An entity the batch deleted is absent from the map; what
   it was is already frozen in `ctx.event.payload`.
5. Use declared roles for fixed world context that is not part of the event.
6. Return `narration` and `data` alongside an allow when the reason a change was permitted is worth
   recording. Both are optional and neither changes the outcome.
7. Dry-run the subscription before enabling it.

## Constraints
- A guard cannot return effects, events, notifications, child results, or a rewritten proposal. It
  decides; it does not change the world.
- A denial needs both a code and a reason. The code is 3 to 64 characters of A-Z, 0-9 and
  underscore starting with a letter, and the reason is at most 500 characters. Supplying a code
  while allowing is itself a failure, because a reader cannot tell which the guard meant.
- Any invalid, unavailable, or failing guard fails closed and rolls back the root transaction.
- The first deterministic deny (subscription order, then id) short-circuits later guards.
- Guards run on every proposal in a chain, not only the first. A reaction's own world changes
  propose their own events, and those face the same guards at their own depth.
- A guard's seed is derived from its registration and the proposal it is judging, so two guards
  looking at one proposal do not share a seed and one guard judging one proposal twice draws the
  same numbers.
