---
id: procedure.subscription.create
category: subscription
name: Create an event subscription
governs: registering a guard or reaction subscription
status: active
createdBy: "seed"
changeNote: "Re-seeded: the bootstrap file changed."
---

## Description
Register one versioned guard or reaction middleware subscription. This contract creates a registration only. Registering emits nothing and runs nothing; the registration takes effect the next time a matching event is proposed.

## Instructions
1. Read the target event type and event mechanic before registering either mode.
2. The mechanic must be active, declare the same mode and exact event type in its `requirements.event`, and declare no child mechanics.
3. Use a permanent `subscription.*` id, then dry-run `commit(kind: "subscription")` before writing.
4. Bind every required ordinary mechanic role in `fixedRoleEntityIdsJson`. A reaction may instead bind exactly one declared ordinary role through `roleFromEventPayloadJson`, whose one entry maps the role to a field named by the event type schema extension `x-dantes-entity-payload-fields`. That field must be a direct non-null string property, and the accepted event must name its value exactly once in `entityIds`. Alternatively, a scoped reaction may bind one required role through `fanoutSelectorJson`: its closed object is `{ "role", "relationshipKind", "direction", "componentId" }`, selects directed relationship endpoints with component presence, and may not combine with payload binding.
5. Filter tracked entities only with `trackedEntityIdsJson`; use scalar equality only in `payloadEqualsJson`.
6. Choose an order from -1000 through 1000 and a per-chain execution limit from 1 through 8. Read `procedure.event.chain-limits` before raising that limit above 1.

## Constraints
- A guard and a reaction are different identities. Mode is immutable after creation; create a new id to change it.
- Registrations are append-only and revisioned.
- `roleFromEventPayloadJson` is an empty object or one role-to-field mapping. It is unavailable to guards, cannot duplicate a fixed role, cannot combine with child mechanics, and does not fan out.
- `fanoutSelectorJson` is `{}` or exactly `{ "role": string, "relationshipKind": dotted-string, "direction": "scope-to-candidate" | "candidate-to-scope", "componentId": string }`. It is reaction-only, requires a nonempty subscription scope exactly matching the accepted event scope, needs an existing component definition, cannot target an optional/fixed role or child mechanic, and selects at most eight receivers in ordinal order. Relationship and component JSON are never read.
- An active registration routes. A guard runs before its event is accepted and can deny the whole change; a reaction runs after acceptance, and its effects join the same transaction as the change that triggered it.
- Notifications are the one part of this surface that does not exist yet. A mechanic that returns them is rejected as unavailable.
