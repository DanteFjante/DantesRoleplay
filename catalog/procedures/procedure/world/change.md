---
id: procedure.world.change
category: world
name: Synchronize reviewed application world state
governs: commit(kind: "system.world-state.sync"), additive and update-only application world synchronization
status: active
createdBy: "seed"
changeNote: "Re-seeded: the bootstrap file changed."
---

## Description
The governed authoring route for synchronizing already-reviewed entities, typed components,
containment, and relationships beneath one application World root. It is not a raw effect-list
escape hatch.

## Matches

## Instructions
1. Read the current application, state-space binding, root, affected entities, component-type
   registrations, and relationship ownership before preparing a change.
2. Build one closed root-scoped manifest with exact expected revisions. Include only reviewed
   additions and updates; use an application mechanic for rule-decided outcomes.
3. Call `system.world-state.sync` in preview mode with a unique request token. Read every schema,
   ownership, containment, relationship, authorization, and revision check.
4. Commit the identical manifest. Any changed source revision or fingerprint rejects the request
   without partial application.
5. Read back the affected entities through the structured next actions and retain the operation
   receipt as synchronization evidence.

## Constraints
- The manifest is additive/update-only. It does not delete entities, remove components, or provide
  arbitrary raw effects.
- Every component uses a registered application-qualified type and schema version.
- Every entity remains beneath the declared World root; the request cannot move across state
  spaces or select its own audience.
- The whole manifest is atomic and idempotent. A failed item applies nothing.
- Do not encode an uncertain outcome directly. Resolve it through interaction planning or exact
  `application.action.execute`.
