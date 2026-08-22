# Character Feature 5 dependency plan — atomic character creation coordinator

> **D&D implementation reference:** When this work includes D&D 2024 mechanics, inspect [Foundry VTT dnd5e](https://github.com/foundryvtt/dnd5e) before designing or coding. Use it as a licensed engineering reference—not a direct dependency or rules authority—while retaining the exact `source.dnd2024.srd-5.2.1` locator and all applicable MIT, CC-BY, and asset-license notices for any reused material.

Status: **Slice 0 implemented and accepted. The closed CH5 root remains blocked on CH3–CH4, Items Slice 6, and the remaining fixture owners.**
Last updated: 2026-08-21

## Execution rule

This is a planning-only repository artifact. It follows AGENTS.md, `procedure.system.create-feature`, `procedure.system.modify`, `procedure.world.change`, the [Character Creation Plan](../../CHARACTER_CREATION_PLAN.md), CH0–CH4, the Item and Inventory Plan, `ActionRunner`, and the generic MCP surface contract. It writes no runtime artifact.

CH5 owns one root creation transaction. It resolves a complete request through the lower feature contracts and creates one character only if every proposed effect, guard, reaction, item grant, and receipt is valid. It does not replace any child state owner, make raw database writes, or expose a new MCP tool/kind.

## Target capability

The existing `commit(kind: "action")` path can run one explicit governed character-creation mechanic against an existing active campaign. Its `validate` operation returns the same named corrections and no character-world effects; its `create` operation applies the exact resolved effect bundle in ActionRunner's one transaction, produces one complete campaign-attached character and starting items, and records the normal root audit/event history.

The completed actor has a CH5 creation receipt containing creation-protocol version and immutable source-set identity. It does not copy a root operation ID: audit and event history already own that correlation and are queried by the created actor/root history when needed.

### Included

- One closed complete-build request with `validate` and `create` operations and a caller-supplied, validated permanent character ID.
- A parent creation mechanic/coordinator that invokes CH1–CH4/Items resolution paths and aggregates only their validated effect fragments.
- Entity creation, campaign attachment, profile, ability/level, origins, class, grant receipts, applicable HP/AC/proficiency/feature state, item instances/containment, and creation receipt in one effect batch.
- Existing ActionRunner transaction, dry-run validation, guard/reaction processing, root audit, event correlation, rollback, and query-back evidence.
- Failure codes that identify the invalid build field or missing dependency without exposing internal raw effects as an API.

### Excluded

- A new MCP tool/kind, browser wizard, unauthenticated player ownership, partial drafts, asynchronous jobs, retrying a failed create, or an arbitrary effects endpoint for character data.
- New source content, policy/class/origin rules, language/tool/trait/HP/AC derivation, item definition/instance/equipment semantics, or a public discovery experience; those remain with CH0–CH4, Items, ruleset owners, and CH6.
- Class advancement, spellcasting, feats, multiclassing, correction/respec, retirement, and player-control authorization.

## Verified transaction foundation and architectural gap

| Existing foundation | CH5 use and restriction |
| --- | --- |
| `ActionRunner` | Selects an active mechanic, resolves its projection, runs it, dry-runs output effects, allocates one root operation ID, applies the exact effects in one database transaction, then writes the successful audit. Failure rolls the transaction back and writes a failure audit separately. |
| `procedure.world.change` / `IEffectApplier` | Validates ordered structural effects as all-or-nothing, including guards/reactions and event correlation. CH5 submits one resolved bundle, never a sequence of independent commits. |
| Existing recorder mechanics | They validate their own component state but many require an existing `subject` actor. A new character does not exist while ActionRunner materialises child projections. |
| `IMechanicComposer` | Exposes declared JavaScript child results before a parent runs, but role projection still requires existing entities. CH5 Slice 0 therefore uses a separate, generic typed staged-world composer rather than extending the MCP/mechanic child protocol. |
| Item Slice 6 | Defines the intended character-root starting-equipment integration, but it is still planning-only. |

The last two rows were the critical gap. Slice 0 now supplies a generic staged-world composer: a
root declares a reserved target and the complete set of entity IDs its children may touch. It
starts with the target's `entity.create` effect, dry-run validates the accumulated ordered bundle
on every append, and exposes a read-only overlay of that bundle over persistent state. Existing
typed planners can validate that overlay without a partial write; mutation methods fail, and the
root remains the only caller that can apply the final effects. C15 now exposes the corresponding
effect-free attachment planner. The root/receipt public contract remains intentionally absent.

## Proposed permanent vocabulary — confirmation required

| Role | Proposed ID and boundary |
| --- | --- |
| Root contract | `procedure.character.create`, governing closed validation/create requests, staged composition, transaction/result semantics, and recovery calls. |
| Creation receipt component | `dnd2024.character.creation-receipt`, attached once at successful creation only. |
| Root mechanic | `mechanic.dnd2024.character.create`, an internal semantic creation planner run through the existing action kind. |
| Generic staged-composition capability | Exact interface/contract ID is undecided and needs `procedure.system.modify` confirmation after owner search. It is not a character-specific MCP surface. |

The receipt has exactly `protocolVersion` and a sorted unique `sourceDefinitionIds` array. It contains no status, actor name, campaign ID, source text, raw choices, derived values, item instances, or root operation ID. CH1–CH4 selections and receipts remain the detailed source-of-truth; this is an immutable completion boundary, not a second character sheet.

## Closed request and resolution boundary

The root request is a complete JSON object. Before implementation, confirm exact field names and schema, but its semantic fields are limited to: `operation` (`validate` or `create`), canonical new `characterId`, display name, optional CH1 profile values, raw CH2 ability assignment, bound CH0-supported origin/class/equipment choices, and no caller-provided derived value, component data, sourceRef, effects, item-instance ID, audit ID, campaign ID, or arbitrary definition ID.

The existing campaign appears as an explicit action role and is verified by the campaign-owner attachment contract; it is not trusted from request data. The root coordinator resolves the one supported definition set pre-bound by its content declarations, validates all choices, reserves the requested permanent ID as absent, and returns a canonical plan. The caller may choose the ID only in its strict confirmed syntax; collision, malformed ID, or a nonempty pre-existing entity fails before effects. CH6 may later generate/present this ID but must submit the same closed request.

`validate` uses the same resolution and staged planners as `create` but returns zero character-world effects. Its normal action audit is allowed evidence; it creates no actor, component, containment, relationship, item, creation receipt, success event, or success creation audit. `create` must receive the same canonical resolved bundle from the same request; a changed input or dependency version requires revalidation rather than carrying a stale plan between calls.

## Required effect order

The final bundle's order is fixed by dependency, not by narrative:

1. Create the character entity and establish the confirmed campaign-owned attachment.
2. Add profile, abilities, total level, background/species selections, class membership, and all immutable source/grant receipts.
3. Add only the HP, AC, proficiency, and feature state whose dedicated resolvers have succeeded.
4. Create approved item instances and contain them on the character through Items Slice 6; add equipped state only if its separate rules are ready.
5. Add the creation receipt last.

Every component uses its normal recorder/planner semantics and `component.add` where duplicate application is a bug. The receipt's final position gives the query layer a simple completion invariant: missing receipt means no completed character, but a failed root transaction leaves no partial actor at all.

## Dependency graph and slices

~~~text
CH0 complete, ratified path and all owner map                            [missing]
└─ CH1–CH4 accepted content/state/grant/class contracts                  [blocked parents]
   ├─ campaign character-attachment verifier                             [missing campaign leaf]
   ├─ Items 1–6, class/HP, AC/equipment, language/tool/feature owners    [external leaves]
   ├─ generic staged composition for a not-yet-persisted actor           [implemented Slice 0]
   └─ confirmed CH5 vocabulary and request schema
      ├─ Slice 0: staged-composition proof and transaction decision
      ├─ Slice 1: root contract, receipt, validate/create planner
      └─ Slice 2: full fixture transaction and rollback matrix
         └─ CH6 discovery and play handoff
~~~

### Slice 0 — staged-composition proof

**Prerequisites:** owner search of ActionRunner, composer, projection, effects, and child recorders; semantic confirmation for the generic core interface. **Confirmed 2026-08-21.**

1. Implement or select one generic way to project a reserved new entity through declared child planners without persisting it.
2. Prove children cannot read undeclared state, invent IDs, write directly, observe a different virtual order, or emit effects outside the parent bundle.
3. Prove ActionRunner still dry-runs then applies exactly one bundle inside one transaction and records failure only after rollback.
4. Add focused composition/rollback tests and stop for the semantic acceptance gate.

**Exit:** a parent can deterministically assemble a new-entity effect bundle from child contracts without duplicating their validation or writing a partial entity. **Implemented; see `CHARACTER-FEATURE-05-SLICE-0-RECEIPT.md`.**

### Slice 1 — closed creation planner and receipt

**Prerequisites:** Slice 0 accepted; all CH0–CH4/Items/campaign dependencies required by the first fixture are accepted; permanent IDs/request schema are confirmed.

1. Add `procedure.character.create`, the receipt schema, and root planner under the existing action kind.
2. Implement identical complete-build resolution for `validate` and `create`; bind the supported source definitions internally and reject forbidden/derived/extra inputs.
3. Assemble the ordered bundle through staged child planners; use no MCP handlers, direct database writes, or alternate child validation.
4. Test every input, source, scope, duplicate-ID, stale-definition, and unresolved-owner failure with no character-world effects. Run `roleplay validate catalog`.

**Exit:** valid preflight returns one canonical resolvable bundle, while every invalid request names its correction and leaves no creation artifact.

### Slice 2 — one complete fixture transaction

**Prerequisites:** Slice 1 accepted; starting equipment, all selected feature/vital-stat owners, and campaign attachment are live; test fixture is CH0-ratified.

1. Execute the complete bundle through ActionRunner once and query actor, containment, relationships, receipts, events, and history back.
2. Inject failure at entity creation, each child grant, item/containment, effect validation, guard, reaction, event, receipt, and audit boundary.
3. Prove every failed create has no actor/item/receipt/success event/success audit, while its separate failure audit remains explainable; prove success has exactly one root correlation and one completion receipt.
4. Run focused tests, `roleplay validate catalog`, then the full suite at CH5 acceptance. Run protocol walk only if the existing action dispatch/dependency registration changed.

**Exit:** one legal request creates a coherent, queryable, campaign-attached character with exactly its selected starting state; every injected failure rolls back all character-world effects.

## Acceptance matrix

| Case | Exact expected result |
| --- | --- |
| Complete-only input | Omitted, extra, free-form, derived, arbitrary-definition, raw-effect, item-instance, campaign-ID, or audit-ID input is rejected before a bundle exists. |
| ID and scope | Canonical absent ID plus verified active campaign attachment are required. Reused/malformed ID or missing/wrong/inactive campaign fails with no actor. |
| Same validation | `validate` and `create` resolve the same request through the same child contracts; validation returns no character-world effects and create cannot use a stale plan. |
| Child ownership | Every persisted field is proposed by its authoritative CH1–CH4/Items/derivation planner. The root only orders, composes, and commits. |
| Atomicity | Entity, attachment, components, receipts, item instances, containment, events, guards, reactions, and root audit succeed together or character-world state is absent. |
| Completion invariant | Receipt is added once and last. No completed actor lacks it; no receipt exists without all prerequisite selections and resolved grants. |
| Audit history | Success has one ActionRunner root operation and correlated events. Failure has no success creation audit or event, but retains the normal failure audit after rollback. |
| Public surface | CH5 uses existing `commit(kind: "action")`; CH6 alone decides whether discovery/transport changes need `procedure.mcp.add-tool` confirmation. |

## Evidence and change control

The implementation receipt records the staged-composition decision, confirmed IDs/schema, fixture source set, focused and failure-injection tests, catalog validation/full-suite results, and queried transaction/audit evidence. Do not paste raw effects, source rules, or operation IDs into the permanent character receipt.

Amend this plan before allowing a new source path, an ID-generation policy change, partial/draft state, replay/retry, a different transaction owner, async creation, public kind/tool change, correction/respec, or player authorization. Those boundaries belong to CH0–CH4/CH7, a confirmed CH5 amendment, CH6, CH13, or CH14.
