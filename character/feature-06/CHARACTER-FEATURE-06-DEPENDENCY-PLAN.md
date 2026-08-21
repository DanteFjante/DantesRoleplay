# Character Feature 6 dependency plan — MCP discovery and play handoff

Status: **Planned; blocked on accepted CH5 creation plus reinspection of the then-current three-verb surface.**  
Last updated: 2026-08-20

## Execution rule

This is a planning-only repository artifact. It follows AGENTS.md, `procedure.system.create-feature`, `procedure.mcp.add-tool`, `procedure.system.use`, the [Character Creation Plan](../../CHARACTER_CREATION_PLAN.md), and accepted CH0–CH5 contracts. It creates no runtime artifact.

CH6 makes the completed creation capability discoverable and usable by a fresh trusted MCP session. It consumes CH5's canonical validation/create result and existing query/action paths; it does not add a character-specific transport path, duplicate a creation validator, or claim authentication/authorization that CH14 has not implemented.

## Target capability

A fresh MCP session can orient, discover the one supported source-cited build and exact closed request shape, validate it, create it through the existing action kind, query the resulting actor and receipt, and perform one already-supported ability check. Every step names an existing contract or capability and a next safe call; none requires manual component/effect editing.

### Included

- Read-only discovery using current `query(kind: "capabilities")`, `procedures`, `mechanics`, `entities`, `world`, `history`, and when useful `graph` kinds.
- One `procedure.character.inspect` contract that explains how to read approved content, a completed character, creation receipt, and operation history through existing generic queries.
- A CH5 action result/handoff projection with entity ID, immutable selected source IDs, receipt presence, playable capabilities actually satisfied, and one literal next safe action.
- Intent-match uniqueness/routing checks for `mechanic.dnd2024.character.create` on the existing `commit(kind: "action")` kind.
- A fresh-session protocol walk covering orient/discover/validate/create/query/ability-check and named failure recovery.

### Excluded

- A fourth tool, a pre-approved query/commit kind, a generic “character” command, browser wizard, player login, audience enforcement, correction, advancement, source expansion, or UI presentation.
- Direct `commit(kind: "effects")` character creation, caller-selected mechanic ID, manual component assembly, raw effect display as a normal API, and cached/stale validate output reused for create.
- Promise of every class feature, spell, attack, item action, language, or tool capability. The handoff lists only mechanics whose declared prerequisites are present on this completed actor.

## Current-surface decision

The existing surface already exposes the needed primitive shapes: capability catalog; full procedures/mechanics; component-filtered entity reads; history; and `commit(kind: "action")`, whose root mechanic is selected by intent. CH6 begins by re-reading `VerbSurface`, dispatcher registrations, and `procedure.mcp.add-tool`. It must use this surface if it can provide the target capability.

No new kind is presumed. Only if a focused protocol walk proves that the existing kinds cannot discover a bounded supported build or invoke the closed CH5 operation without unsafe ambiguity may implementation propose a new kind. That proposal must stop for separate `procedure.mcp.add-tool` confirmation, include the exact payload/parameters/example/dispatcher/guard tests in the same slice, and remain a thin delegate to the CH5 kernel service.

## Discovery and inspection contract

`procedure.character.inspect` is the proposed permanent contract ID and needs confirmation before authoring. It owns instructions, not a new state component or mechanic. It must direct a fresh session through this sequence:

1. `orient`, then `query(kind: "capabilities")` to obtain actual current kinds and payload shapes.
2. `query(kind: "procedures", id: "procedure.character.create")`, `procedure.character.choose`, and `procedure.character.inspect`; read the current versions rather than relying on this plan.
3. `query(kind: "mechanics", id: "mechanic.dnd2024.character.create")` to learn exact roles, scope, input, match phrases, and current source version.
4. Use `query(kind: "entities", withDefinitionId: "dnd2024.character.content-definition")` plus definition-specific reads to discover only active CH0-approved content and its source references. Query current component definitions first; never infer support from an entity name.
5. Submit the CH5 closed request through `commit(kind: "action")`, with campaign in the exact declared role map. `validate` first; revise source choices from returned named corrections; then submit an unchanged valid request as `create`.
6. `query(kind: "entities", id: "<created-character-id>")` and `query(kind: "history", subject: "<created-character-id>")` to inspect result and root audit. Query events by the returned root operation only when event evidence is needed.
7. Read the returned first-action mechanic in full, then use existing `commit(kind: "action")` with its declared role/input shape. For the first fixture this is an ability check only if its requirements are satisfied.

Discovery returns source identity, stable keys/versioned entity IDs, available choices, cardinalities, stated exclusions, and source locators. It never returns rules prose as a substitute for a mechanic, an arbitrary definition ID as permitted input, hidden campaign/world knowledge, other campaigns’ context, or an authorization claim.

## Result and failure handoff

CH5's root output data is the one creation handoff shape; CH6 must not create a second result component. After successful `create`, it contains only:

| Field | Meaning |
| --- | --- |
| `characterId` | Created actor's permanent entity ID. |
| `sourceDefinitionIds` | Sorted immutable identities selected by the completed build, matching the creation receipt. |
| `receiptPresent` | True only after the CH5 completion receipt is committed. |
| `playableCapabilities` | Stable mechanic IDs/version plus declared input/role summary for only currently satisfied safe actions. |
| `nextAction` | One literal existing query or action call, built from the live surface rather than prose. |

The returned ID/capabilities are a handoff convenience, not new authoritative actor fields. On validation failure, return named field/path/code and the literal next correction/discovery call; no actor ID, success receipt, source-set assertion, or “created” narration is returned. On transaction failure, expose the existing failure audit ID and recovery query while never claiming an entity or item was created.

## Dependency graph and slices

~~~text
Accepted CH5 root action, receipt, rollback, and queried fixture evidence      [blocked parent]
├─ current VerbSurface/dispatch and procedure.mcp.add-tool reinspection        [required gate]
├─ CH0 source content and CH1–CH4 discovery metadata                           [blocked parents]
├─ active first playable mechanic with satisfied prerequisites                  [external readiness]
└─ confirmed procedure.character.inspect and result-handoff schema
   ├─ Slice 1: discovery/inspection contract using existing surface
   └─ Slice 2: creation handoff, intent routing, and fresh-session protocol walk
      └─ CH7 correction/evidence gate and CH8 UI parity
~~~

### Slice 1 — bounded discovery using existing kinds

**Prerequisites:** CH5 is accepted with one fixture; current query/action capabilities and permanent contract ID are reconfirmed; all discoverable content is active and source-cited.

1. Add `procedure.character.inspect` with exact generic-query recipes, limits, source/version interpretation, and failure recovery.
2. Add only the read projections/configuration needed for active CH0 content to be found through existing entity/world queries. Do not add a kind if component-filtered entity reads suffice.
3. Test a fresh, context-free session’s ability to identify one supported build, source locator, required campaign role, complete request fields, exclusions, and next call.
4. Test archived/unknown/missing content, ambiguous component/entity result, missing campaign, and absent receipt handoffs. Run `roleplay validate catalog`.

**Exit:** a reader can discover the exact supported build and request from contracts/catalog queries alone, with no free-text source interpretation or manual state assembly.

### Slice 2 — action handoff and protocol proof

**Prerequisites:** Slice 1 accepted; CH5's root mechanic has one unique creation route for its match phrases; an ability-check mechanic is active and its fixture prerequisites are satisfied.

1. Make CH5 return the one structured success/failure handoff shape and literal live next calls; do not add a second action or result store.
2. Verify intent-ranking has no conflicting active mechanic for the published create phrases. If it does, correct matches/routing under existing mechanics before considering surface expansion.
3. Perform protocol walk from a new session: orient; discover; validate invalid then valid input; create; query actor/receipt/history; read ability-check mechanic; make the safe check; and inspect named failure recovery.
4. Run focused tests and catalog validation. Run the protocol walk because MCP discovery/dependency registration or result routing changed; run the full suite at CH6 acceptance.

**Exit:** the walk uses only advertised current verbs/kinds and governed contracts, creates exactly one actor, and yields a safe first action without a human supplying component IDs/effects or hidden context.

## Acceptance matrix

| Case | Exact expected result |
| --- | --- |
| Surface fidelity | Every published call exists in live `VerbSurface` and its dispatcher; no fictional command/kind/tool appears in contracts or handoff. |
| Fresh discovery | A blank session learns the complete supported build, source locator, exclusions, campaign role, and request shape through orient/query only. |
| Closed invocation | Creation uses existing `commit(kind: "action")`; it cannot name a mechanic ID, send effects, manually attach components, or bypass CH5 validation. |
| Intent safety | The published creation phrases select exactly the intended active root mechanic; conflicting mechanics fail the routing gate. |
| Result truthfulness | Success names only committed actor/receipt/source/capability facts. Failure names no created state and points to history/discovery recovery. |
| First play | Handoff lists only a mechanic whose requirements are met and gives its actual role/input shape after the session reads it. |
| Scope/auth boundary | Discovery is trusted-host only until CH14. It does not expose a player-control claim or use profile visibility as permission. |
| Expansion discipline | A missing discovery capability triggers `procedure.mcp.add-tool` confirmation rather than a stealth query/commit kind. |

## Evidence and change control

The implementation receipt records surface reinspection, confirmed contract/result IDs, discovery fixture IDs/source locators, route-uniqueness proof, complete protocol-walk transcript/results, catalog validation, full-suite result, and any `procedure.mcp.add-tool` decision. Do not copy protocol data or action history into the actor.

Amend CH6 before adding a new surface kind/tool, a web/UI workflow, player authorization, inspection that changes state, correction, advancement, additional content, or a different first action. Those boundaries belong to `procedure.mcp.add-tool`, CH8, CH14, CH7, CH9, CH7 expansion, or the owning ruleset feature.
