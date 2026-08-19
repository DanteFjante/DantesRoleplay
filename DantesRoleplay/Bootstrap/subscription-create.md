---
id: procedure.subscription.create
category: subscription
name: Create an event subscription
governs: registering a guard or reaction subscription
status: active
---

## Description
Register one versioned guard or reaction middleware subscription. This contract creates a registration only; it does not emit events, route middleware, block an action, or notify anyone.

## Instructions
1. Read the target event type and event mechanic before registering either mode.
2. The mechanic must be active, declare the same mode and exact event type in its `requirements.event`, and declare no child mechanics.
3. Use a permanent `subscription.*` id, then dry-run `commit(kind: "subscription")` before writing.
4. Bind every required ordinary mechanic role in `fixedRoleEntityIdsJson`; filter tracked entities only with `trackedEntityIdsJson`; use scalar equality only in `payloadEqualsJson`.
5. Choose an order from -1000 through 1000 and a per-chain execution limit from 1 through 8.

## Constraints
- A guard and a reaction are different identities. Mode is immutable after creation; create a new id to change it.
- Registrations are append-only and revisioned.
- This slice stores and validates registrations only. Guard execution, blocked outcomes, reaction dispatch, event chains, ledger entries, and notifications are not implemented yet.
