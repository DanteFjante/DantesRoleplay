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
2. Return `{ effects: [...] }` to change the world, `{ events: [...] }` to say something happened
   that no change describes, and `narration` or `data` to explain what the rule did. Returning
   nothing but an empty effect list is a legitimate outcome: it records that the rule was consulted
   and had nothing to do.
3. Declare an event as `{ type, payload, entityIds, scope }`. The payload may be an object — it is
   stringified for you — and is validated against the type's registered schema at the moment it is
   emitted, against the version active then. Every id in `entityIds` must name a live entity.
   Reach for this when the fact matters and the world does not show it: a ward was spent, a bargain
   struck, an alarm raised. Another rule can then answer the event, which a narration string cannot
   offer.
4. Read the accepted event from `ctx.event`. A reaction's envelope carries `id` and `sequence` in
   addition to the fields a guard sees, because by now the event exists and has a place in the
   ledger.
5. Read affected world state from `ctx.eventEntities`, a map of entity id to that entity carrying
   only the components declared in step 1. Use declared roles for fixed world context that is not
   part of the event.
6. Use `ctx.event.payload.before` and `.after` when the rule answers the CHANGE rather than the
   state. `eventEntities` shows the world as it now stands; only the payload says what it stood at
   a moment ago, and a rule that fires on "took a wound" needs the difference.
7. Use `ctx.randomInt` for anything random. The seed is derived from the chain's root seed and this
   execution's exact position in it, so the whole chain replays from the root seed alone.
8. Register the rule with `procedure.subscription.create`, and read
   `procedure.event.chain-limits` before setting `maxExecutionsPerChain` above 1.
9. Return `notifications: [{ topic, subject, body, entityIds }]` to tell a person something. See
   `procedure.notification.inspect` before writing one — the subject has to be readable in a list
   without opening it, and the topic is what a reader filters by.
10. Dry-run the subscription, then read the chain back with `query(kind: "events")` and confirm the
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
- A rule cannot declare a `world.*` event. Those are the kernel's own record of what it did, and a
  rule able to forge one could claim a component was replaced that never was — in the one place
  whose entire value is that it can be believed. Propose the effect; the event follows from it.
- A declared event that names an unregistered or inactive type, carries a payload its schema
  rejects, or points at an entity that is not there fails the WHOLE root change with
  `SUBSCRIBER_INVALID_EVENT`. A false statement in an audit trail is worse than a missing one.
- A declared event is guarded like any other proposal, at its own depth, and counts against the
  chain's event budget whether or not a guard allows it.
- Each declared event records the execution that asserted it, so a reader can always ask which rule
  made the claim. Causation alone cannot answer that — two rules answering one event both name it.
- A notification's content and links are written once and are never editable. Nothing delivers
  them: they wait until somebody asks. A notice with no topic, no subject, or naming an entity that
  is not there fails the whole root change, exactly as a bad declared event does.

