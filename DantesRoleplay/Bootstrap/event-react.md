---
id: procedure.event.react
category: event
name: Author a reaction to an accepted event
governs: reaction mechanics registered through event subscriptions
status: active
---

## Description
A reaction runs after a structural event has survived every guard and been written to the ledger,
inside the same uncommitted transaction as the change that caused it. What it returns becomes part
of that change, so a reaction either commits with the event it answered or nothing commits at all.

## Instructions
1. Declare `requirements.event` with `mode: "reaction"`, the exact event types the rule answers,
   and the `components` it needs projected onto the entities those events touch.
2. Return `{ effects: [...] }` to change the world, and `narration` or `data` to explain what the
   rule did. Returning nothing but an empty effect list is a legitimate outcome: it records that
   the rule was consulted and had nothing to do.
3. Read the accepted event from `ctx.event`. A reaction's envelope carries `id` and `sequence` in
   addition to the fields a guard sees, because by now the event exists and has a place in the
   ledger.
4. Read affected world state from `ctx.eventEntities`, a map of entity id to that entity carrying
   only the components declared in step 1. Use declared roles for fixed world context that is not
   part of the event.
5. Use `ctx.randomInt` for anything random. The seed is derived from the chain's root seed and this
   execution's exact position in it, so the whole chain replays from the root seed alone.
6. Register the rule with `procedure.subscription.create`, and read
   `procedure.event.chain-limits` before setting `maxExecutionsPerChain` above 1.
7. Dry-run the subscription, then read the chain back with `query(kind: "events")` and confirm the
   causation ids say what you expected.

## Constraints
- A reaction cannot return a guard decision. Deciding whether a change is permitted is a guard's
  job, and a mechanic that does both has been registered in the wrong mode.
- A reaction's effects are not applied by the router. They are proposed like any other change, so
  they propose their own events, face the same guards, and count against the same chain budget one
  level deeper.
- Any failure aborts the entire root change: an unavailable or inactive mechanic, a mechanic that
  no longer declares the type, corrupt bindings, a projection failure, a throw, a timeout, or an
  invalid effect. There is no partial reaction.
- Reactions run in ascending declared order, then by subscription id, over events in ledger
  sequence order. The tiebreak is what makes a chain reproducible rather than dependent on what
  the database happened to return first.
- Only committed successful executions are kept. A chain that rolls back leaves no execution rows
  behind, because an execution is evidence that a rule ran and its work stood.
- Derived events and notifications are not available yet. A reaction changes the world and the
  events its changes propose follow from that; it cannot yet emit an event directly.
