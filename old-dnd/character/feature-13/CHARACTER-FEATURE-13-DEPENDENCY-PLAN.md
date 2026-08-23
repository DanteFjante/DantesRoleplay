# Character Feature 13 dependency plan — voluntary retirement and archive lifecycle

> **D&D implementation reference:** When this work includes D&D 2024 mechanics, inspect [Foundry VTT dnd5e](https://github.com/foundryvtt/dnd5e) before designing or coding. Use it as a licensed engineering reference—not a direct dependency or rules authority—while retaining the exact `source.dnd2024.srd-5.2.1` locator and all applicable MIT, CC-BY, and asset-license notices for any reused material.

Status: **Planned; implementation awaits a verified CH6 character surface and a campaign-owned character-participation lifecycle decision.**  
Last updated: 2026-08-20

## Execution rule

This is a planning-only repository artifact. It follows AGENTS.md, `procedure.system.create-feature`, `procedure.system.modify`, the [Character Creation Plan](../../CHARACTER_CREATION_PLAN.md), CH1, CH5–CH7, and the accepted campaign/session lifecycle contracts. It writes no runtime artifact.

CH13 is voluntary character lifecycle only. It preserves the actor, immutable sources, inventory containment, campaign history, operations, and receipts. It must never use delete, act as a correction/respec, infer a D&D mechanical death, or move/transfer a character or their possessions as a side effect.

## Target capability

A trusted host can validate, retire, then archive one campaign-attached character through one governed action. Retirement marks the character no longer available for character-owned progression and ordinary player-character handoff; archiving subsequently removes it from ordinary active-character discovery while retaining read-only historical inspection. Both changes coordinate the campaign-owned participation relationship in the same root transaction and are fully auditable. They are irreversible in this feature.

The first fixture is one active created character in one active campaign. It proves a reusable lifecycle contract, not retirement of every actor kind, NPC conversion, party replacement, campaign closure, death, resurrection, or account/player management.

### Included

- One closed actor lifecycle component with the sequence `active → retired → archived` only.
- One source-free, schema-bound character lifecycle action with `validate`, `retire`, and `archive` operations; existing action transport is presumed until CH6/protocol inspection proves otherwise.
- Campaign attachment/participation validation and an atomic campaign-owner transition that stops the character being offered as an active player-character participant.
- Revision of character-owned create/advance/correct/guide/inspect behavior to respect lifecycle state: retired characters do not receive ordinary advancement/correction/player-character handoff; archived characters are hidden from ordinary lists and shown only in explicit historical inspection.
- Readback, audit/event, rollback, stale/replay/corrupt-state, and fresh-host continuity evidence.

### Excluded

- Entity/component/relationship/containment deletion, item transfer, currency settlement, equipment changes, world-location moves, campaign/quest/session closure, automatic replacement-character creation, or notifications beyond existing root behavior.
- D&D unconsciousness, dying, death, resurrection, injury, condition changes, or any mechanical effect of retirement. These remain ruleset owners.
- Unretire/reactivate/unarchive, respec/rename/profile correction, class/level/feat/spell changes, NPC conversion, player authentication/authorization, public UI, collaboration, or remote account control.
- A generic global action ban. CH13 gates character-owned flows and campaign player-character participation; whether a trusted GM later uses the preserved entity as an NPC is a separate campaign/authorization decision.

## Ownership and lifecycle result

| Concern | Authoritative owner and CH13 rule |
| --- | --- |
| Character lifecycle state | CH13 `dnd2024.character.lifecycle`, attached only to an actor carrying the accepted CH1 character marker/profile. It contains status only. |
| Campaign scope and active participant/party role | Campaign owner. It must expose the existing character attachment and a lifecycle-safe participation transition. CH13 neither invents a campaign ID field nor removes raw relationships. |
| Character identity, sources, grants, classes, abilities, resources, inventory | Their existing character/ruleset/Items owners. Retirement/archive preserves them byte-for-byte. |
| Player control and authorization | CH14/identity owner. Before that, CH13 is trusted-host lifecycle administration; descriptive status grants no authority. |
| Character create/correct/advance/guide/inspect | CH5–CH9 and CH6/CH8. CH13 adds explicit lifecycle preconditions/visibility behavior, not duplicate command paths. |
| Generic gameplay action eligibility | The owning ruleset/campaign action. CH13 does not globally block arbitrary actions it does not own; it supplies a safe lifecycle projection for later consumers. |
| Audit/event/history | Existing root ActionRunner/audit/event model. Root operation ID remains in history, not copied into the lifecycle component. |

The campaign participation transition is a hard external leaf. It must state what “not an active player-character participant” means for the actual campaign model and how it composes atomically with lifecycle state. CH13 may not approximate it by removing a containment or relationship, setting an actor name, or adding a copied campaign/status field.

## Proposed permanent vocabulary — confirmation required

| Role | Proposed ID and boundary |
| --- | --- |
| Actor state | `dnd2024.character.lifecycle`, complete closed data `{ status: "active" | "retired" | "archived" }`. No reason/note, campaign ID, date, world location, player/account ID, inventory, source, mechanic, or root-operation ID. |
| Governing contract | `procedure.character.lifecycle`, governing transitions, campaign composition, character-flow preconditions, historical readback, and recovery. |
| Root mechanic | `mechanic.dnd2024.character.lifecycle`, selected through the existing action path and returning one atomic typed effect bundle. |
| Campaign participation transition | Exact component/relationship/procedure/mechanic ID is intentionally undecided and owned by Campaign. It must be confirmed before CH13 implementation. |

These IDs, component meaning, campaign counterpart, historical query/filter convention, event/audit shape, and revisions to CH5–CH9 require confirmation under `procedure.system.modify`. Do not reuse an unrelated quest/world `archived` status or widen an existing profile component: lifecycle meaning is distinct and permanent. A new MCP kind/tool or public route needs its own CH6/protocol and `procedure.mcp.add-tool` confirmation.

## Closed action contract

`procedure.character.lifecycle` accepts exactly:

~~~text
{
  operation: "validate" | "retire" | "archive",
  characterId: canonical existing character entity ID,
  expectedStatus: "active" | "retired"
}
~~~

`characterId` is resolved through its existing campaign attachment; campaign scope is never caller input. `expectedStatus` is a stale-intent guard: `retire` accepts only `active`, `archive` accepts only `retired`, and `validate` accepts the state appropriate to the requested non-writing transition. Null, missing, extra, malformed/non-object, unknown operation, unavailable actor, wrong profile/marker, no/duplicate/cross-campaign attachment, malformed lifecycle, or expected/current mismatch fails before effects.

The initial created actor receives lifecycle `active` as part of CH5 only after CH13's component/creation integration is accepted; legacy previously created actors require an explicit scoped migration/defaulting plan with readback, not a runtime assumption. `validate` returns the current status, requested next status, campaign participation readiness, and literal recovery call with zero durable changes. `retire` and `archive` revalidate under the root transaction and never trust a previous preview.

Canonical success output contains only `characterId`, `previousStatus`, `currentStatus`, `campaignParticipationUpdated`, `historicalReadAvailable`, and literal `nextAction`. It excludes a campaign ID, user identity, reason text, item/source/class details, raw effects, root operation/audit/event ID, or an assertion about D&D death. Ordinary inspect can return `active`/`retired` state to an authorized/trusted host; archived detail requires explicit historical mode and never bypasses CH14 audience rules when they later exist.

## Transition and transaction rules

1. Resolve one existing character actor, its exact lifecycle component, campaign-owned attachment/participation record, and campaign lifecycle. Require an active campaign/participant only where the Campaign owner defines it; do not infer party membership from world containment.
2. Validate the requested monotonic transition: no lifecycle component/default legacy actor is blocked pending migration; `active→retired` is valid only once; `retired→archived` is valid only once; `active→archived`, archive replay, retire replay, and every reverse transition fail unchanged.
3. Ask Campaign for a dry-run typed participation transition. It must leave campaign history intact and remove only the owner-defined active player-character availability. If it requires quest, session, encounter, replacement, possession, world-movement, or auth changes outside its accepted contract, block with that named dependency.
4. `validate` returns zero effects. On `retire`/`archive`, repeat checks under one ActionRunner root and apply the Campaign participation transition plus one full lifecycle component replacement in confirmed order. No child may commit independently.
5. On retirement, character-specific progression/correction and ordinary player-character handoff reject the new status. On archive, ordinary active-character discovery omits it; explicit historical inspection remains read-only. Existing source, class, profile, grants, receipts, items/containment, world position, and audit history are untouched.
6. Root event/audit is written only after the complete bundle commits. Campaign transition, lifecycle state, guard/reaction, event, audit, cancellation, or timeout failure rolls back everything. Separate failure audit follows existing system policy only.

The plan does not prescribe an active encounter/session rule. If a campaign feature later owns such a state, it must either veto the lifecycle request with a named recovery or provide its own typed child transition before CH13 is enabled for that context. CH13 will not silently withdraw a character mid-state.

## Dependency graph and slices

~~~text
Verified CH6 created-character surface + campaign/session lifecycle evidence
├─ CH1 campaign character attachment and active-participant owner          [external campaign leaf]
├─ confirmed campaign participation retirement/archive transition           [missing primary leaf]
├─ CH5 creation lifecycle initialization or scoped legacy migration         [character integration leaf]
├─ CH6/CH7/CH8 lifecycle-aware read/write behavior                          [character consumers]
└─ ActionRunner atomic cross-owner transaction/audit boundary               [shared gate]
   └─ Slice 1: lifecycle state, read projection, and creation/migration proof
      └─ Slice 2: atomic retirement and archive fixture
         └─ CH14 authenticated control and separate reactivation/NPC policy
~~~

### Slice 1 — state and lifecycle-aware consumption

**Prerequisites:** CH1 attachment, CH5/CH6 creation/read model, campaign participation semantics, permanent IDs, and legacy/new-character rollout policy are confirmed.

1. Add the closed lifecycle component, reader/precondition helpers, and confirmed CH5 initial `active` write or a scoped, idempotent migration for existing created actors.
2. Revise character-owned correction/advancement/guide/inspection projections to distinguish active, retired, and archived behavior without removing historical data.
3. Test active initialization, exactly one component, legacy migration/default refusal, malformed status, character-marker absence, active/retired/archive visibility, readback, and no D&D/item/world/campaign-copy mutation.
4. Run focused tests and `roleplay validate catalog` after catalog work.

**Exit:** every supported character has one valid lifecycle status and each character consumer handles it deterministically; no retirement action is enabled yet.

### Slice 2 — atomic voluntary lifecycle fixture

**Prerequisites:** Slice 1 accepted; Campaign exposes one root-composable participation transition; event/audit and active-context behavior are confirmed.

1. Add the lifecycle contract/root mechanic with validate, retire, and archive; compose campaign participation and lifecycle state in one existing action transaction.
2. Demonstrate active→retired→archived for one campaign-attached actor; inspect current and explicit historical results across a fresh host/session.
3. Inject failures at campaign transition, lifecycle write, guard/reaction, event, audit, cancellation, and timeout boundaries. Test no attachment/cross-campaign/duplicate attachment, stale status, direct archive, replay, reverse transition, corrupt state, campaign inactive/blocked context, rollback, restore, and no item/location/source/history mutation.
4. Run focused tests, `roleplay validate catalog` where applicable, full suite at acceptance, and a protocol walk only if existing action/query registration changes.

**Exit:** retirement and archive are durable, auditable, monotonic lifecycle changes with one campaign participation result and intact character history; every invalid or failed attempt leaves both owners unchanged.

## Acceptance matrix

| Case | Exact expected result |
| --- | --- |
| State machine | Exactly `active→retired→archived` is possible. Direct archive, replay, reverse/reactivate/unarchive, and malformed state fail unchanged. |
| Scope/participation | One valid campaign attachment and Campaign-owned participation transition are required. Missing, duplicate, cross-scope, inactive, or unsupported active-context records block with a named recovery. |
| Preservation | Character entity, profile, sources, abilities, classes, spell/feat state, grants/receipts, item containment, world location, campaign history, and operation history remain unchanged except confirmed lifecycle/participation state. |
| Character flows | Retired actors cannot use character correction/advancement/ordinary player-character handoff; archived actors are absent from ordinary active discovery but available to explicit historical inspection. |
| D&D boundary | Retirement/archive does not create dying/death/condition mechanics, move the actor, change HP, spend resources, transfer possessions, or alter quest/session/world truth. |
| Atomicity | Campaign participation and lifecycle status commit together or roll back together with event/audit behavior; no partial retirement/archival status survives failure. |
| Legacy/replay | Uninitialized legacy state requires the confirmed migration path; repeated/stale requests cannot double-transition or erase history. |
| Authorization boundary | Before CH14 the action is trusted-host only. Lifecycle status/visibility is not identity, ownership, or permission. |

## Evidence and change control

The implementation receipt records confirmed IDs, campaign participation owner and composition proof, initial/migration rollout decision, canonical requests/results, fresh-host historical readback, all lifecycle/rollback/replay/corrupt/context cases, preservation comparisons, catalog validation, and full-suite result. It does not store a retirement narrative, private player data, copied campaign state, raw effects, D&D status, source rules, or audit IDs.

Amend CH13 before adding reactivation/unarchive, NPC conversion, deletion/purge, item settlement/transfer, replacement characters, death/resurrection linkage, campaign/session/encounter transition, player authorization, public UI, notification policy, or a new active-action restriction. Those belong to a dedicated lifecycle/migration plan, Campaign/Items/World/ruleset owners, CH14, CH8 plus public-surface confirmation, or the action owner that needs the restriction.
