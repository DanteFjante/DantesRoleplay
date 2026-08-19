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
1. Declare `requirements.event` with `mode: "guard"` and the exact structural event types.
2. Return exactly `{ decision: "allow" }` to permit the change, or `{ decision: "deny", code: "...", reason: "..." }` to block it.
3. Use `ctx.event` for the immutable proposal and `ctx.eventEntities` for affected ids. Use declared roles only for fixed world context.
4. Dry-run the subscription before enabling it.

## Constraints
- A guard cannot return narration, data, effects, events, notifications, child results, or a rewritten proposal.
- Any invalid, unavailable, or failing guard fails closed and rolls back the root transaction.
- The first deterministic deny (subscription order, then id) short-circuits later guards.
- This slice has no accepted-event ledger and runs no reactions.
