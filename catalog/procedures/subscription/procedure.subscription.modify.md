---
id: procedure.subscription.modify
category: subscription
name: Modify an event subscription
governs: revising, disabling, archiving, and inspecting event subscriptions
status: active
---

## Description
Revise an existing guard or reaction registration without replacing its history.

## Instructions
1. Query the subscription in full and read the contract that governs the change.
2. Dry-run the complete replacement payload before committing it.
3. Include a nonempty `changeNote` when the id already exists.
4. Use `status: "disabled"` to take a registration out of routing and `status: "archived"` to hide it from ordinary lists as well. A disabled registration is skipped from the next proposed event onward.

## Constraints
- A revision appends a version; it never overwrites an old version.
- `mode` is immutable. A guard cannot become a reaction, or the reverse, under the same id.
- Every revision remains subject to the target type, mechanic, entity, component, filter, ordering, and limit checks.
- Revising a registration does not re-run anything and cannot repair a change that already committed. It applies to events proposed after the revision commits.

