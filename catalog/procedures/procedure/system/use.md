---
id: procedure.system.use
category: system
name: Use this system
governs: cold-session orientation, capability discovery, and the common operating protocol
status: active
createdBy: "seed"
changeNote: "Routes cold sessions to bounded current operational contracts."
---

## Description
Start here without loading the whole manual. `orient` identifies the current principal, audience,
applications, state spaces, and registered capabilities. `query` reads through a closed kind; `commit`
changes state through a currently registered typed capability. Generated direct capability tools use
the same registered contracts. No other MCP verb exists.

## Matches
how do I use this system
start a new operator session
discover the right capability
get operational context for a task

## Instructions
### Cold-session sequence
1. Call `orient()` first and again whenever scope changes. Do not reuse an application, state space,
   audience, grant, or capability remembered from another session.
2. If the needed capability is unclear, use
   `query(kind: "system.feature-search", applicationId: "...", query: "describe the intended outcome")`
   for application behavior or read this manual by intent through the current context service.
3. Call `query(kind: "capabilities")` before constructing input. It is generated from the same closed
   query/commit descriptors and supplies current fields, schemas, confirmation, idempotency, owner,
   lifecycle, and recovery references. Never guess a kind or field.
4. Read only the exact linked procedure returned by discovery. Use
   `query(kind: "procedures", id: "...")`; keep its revision/section reference with the selected
   capability. A summary or matching phrase is not the contract.
5. Inspect current owner state and history. For a write, dry-run the identical payload where supported,
   review affected owners and preconditions, then commit once with a stable idempotency identity.
6. Interpret the structured result before continuing. Preserve task handles, operation receipts,
   previous commits, completion evidence, and recovery identity. Query the authoritative owner back
   before reporting completion.

### Intent routing
- Application registration, sources, schemas, activation, state spaces, readiness, dependencies, and
  derived-cache recovery: `procedure.system.application-lifecycle`.
- Runtime query/action selection, JavaScript service calls, Atomic commits, durable job handoff, and
  partial receipts: `procedure.system.runtime-services` and `procedure.action.run`.
- Permissioned runtime candidate write, reuse review, validation, activation, recovery, catalog
  comparison, and intent phrases: the exact `procedure.system.application-candidate.*` contract.
- Schedules, conditional JavaScript observers, observation triggers, workflow fires, and recovery:
  `procedure.system.trigger-scheduling`.
- INNER task submission, dependencies, read/list/wait/cancel, typed results, and reconnect recovery:
  the exact `procedure.system.inner-worker.*` contract.
- Explicitly linked private conversation-journal capture, inspection, recovery, retention, and
  deletion: `procedure.system.conversation-memory`. Source-pinned dreaming follows
  `procedure.system.conversation-dream` through its configured application-owned executable
  procedure; it does not capture unrelated conversations or establish played state.
- Composed pages, query/action bindings, publication, assets, worker result presentation, and current
  theme limitation: `procedure.system.web-composition`.
- Generic entity/component/relationship inspection and audited history: `procedure.system.inspect`.
- Information sources and registered information actions: `procedure.information.manage`,
  `procedure.information.answer`, and `procedure.information.action`.
- Namespace creation and placement of new authored identities: `procedure.system.namespace`.

### Closed surface index
Use this index only to choose what to inspect in `query(kind: "capabilities")`; the linked deep
procedure and live descriptor supply the actual schema.

- Query kinds: `capabilities`, `procedures`, `categories`, `world`, `entities`, `graph`, `mechanics`,
  `event-types`, `events`, `subscriptions`, `notifications`, `feedback`, `information-answer`,
  `information-actions`, `system.audience-context`, `system.applications`, `system.sources`,
  `system.application-preview`, `system.application-readiness`, `system.application-object`,
  `system.dependencies`, `system.catalogs`, `system.catalog.browse`, `system.catalog.search`,
  `system.catalog.record`, `system.feature-search`, `system.interaction-plan`,
  `system.interaction-receipt`, `system.interaction-recipes`, `system.trigger-scheduling`,
  `system.conversation-memory`, `system.blobs`, `namespaces`, and `history`.
- Commit kinds: `application.action.execute`, `system.application-object.submit`, `feedback`,
  `system.application.register`, `system.source.register`, `system.extension.register`,
  `system.component-type.register`, `system.application.activate`, `system.state-space.create`,
  `system.state-space.upgrade`, `system.state-space.adopt-legacy`, `system.world-state.sync`,
  `system.interaction-execute`, `system.interaction-recipe-review`, `system.trigger-scheduling`,
  `system.knowledge-state.sync`, `system.conversation-memory`, `system.namespace.register`,
  `system.blob-upload.begin`, and `system.blob-upload.finalize`.

### Common result protocol
Treat `tag` and `code` as authoritative. `completed` is read/computation success; `committed` requires
an operation receipt; `pending` requires retaining its exact task handle; `failed`, `cancelled`,
`denied`, `unavailable`, and reconciliation outcomes remain distinct. A proposal is inert. Model text,
progress, and dry-run output are not commit evidence.

If a multi-step interaction fails after an earlier owner committed, keep that receipt in
`previousCommits`; there is no cross-owner rollback. Retry an equal request with its original
idempotency identity. If commit status is uncertain, reconcile the original receipt before retrying.
If inputs or activation are stale, read fresh state and create a new planned attempt.

## Constraints
- Authentication, principals, grants, selected revisions, deadlines, budgets, provider/model/tools,
  and execution authority are host supplied unless the current schema explicitly says otherwise.
- `query` never changes state. `commit` coordinates only registered write capabilities. Short-lived
  blob upload URLs accept only bytes declared by their prior commit and do not mutate game state.
- Never commit a payload that differs from its reviewed dry run or preflight.
- Never infer availability from a procedure record. `orient()` and the live capability catalog win.
- Never report an outcome that was not confirmed through its authoritative read owner.
