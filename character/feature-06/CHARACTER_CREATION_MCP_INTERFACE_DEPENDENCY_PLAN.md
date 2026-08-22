# Character-creation MCP interface dependency plan

> **D&D implementation reference:** When this work includes D&D 2024 mechanics, inspect [Foundry VTT dnd5e](https://github.com/foundryvtt/dnd5e) before designing or coding. Use it as a licensed engineering reference—not a direct dependency or rules authority—while retaining the exact `source.dnd2024.srd-5.2.1` locator and all applicable MIT, CC-BY, and asset-license notices for any reused material.

Status: **Planning-only implementation order — does not authorize a runtime slice**  
Last reviewed: 2026-08-21

## Outcome

A fresh trusted MCP session can use the existing three verbs to discover the one supported
source-cited character build, validate a closed creation request, create the character atomically
inside an existing campaign, inspect the completed character and receipt, and take one mechanic
action that the resulting state actually supports.

The target is Character **CH6**, not a browser builder, player login, or a broader character
generator. The first supported path remains the CH0-ratified level-one **Human Soldier Fighter**.
No feature may call a source option playable merely because its identity can be stored.

## Current baseline

| Capability | Status | Evidence / boundary |
| --- | --- | --- |
| First supported build | Ratified | CH0 fixes the Human Soldier Fighter's choices and source locators. |
| Campaign-scoped character participation | Implemented | Campaign C15 supplies active-scope verification and an attachment planner. |
| Character profile and immutable content provenance | Implemented | CH1. It is an internal CH5 dependency, not a public creation action. |
| Standard Array validation and raw abilities fragment | Implemented | CH2. It records raw scores only; origin increases remain with their owner. |
| Species selection, Soldier ability increases, languages, and tools | Implemented as separate owners | Features 26 and 28 supply narrow composition leaves; CH3 must compose them rather than copy their state. |
| Physical item/inventory primitives | Implemented | Feature 23 Slices 1–11; exact starting-package content and grants still need their own fixture proof. |
| New-actor staged composition | Implemented | CH5 Slice 0 composes a virtual new actor but persists nothing. |
| Atomic character creation and MCP discovery | Missing | CH5 root and CH6 are the final two character-owned capabilities. |

Row 0 was completed on 2026-08-21. Its exact owner evidence, unresolved leaves, and next-pass
stop gate are recorded in
[`CHARACTER-FEATURE-00-OWNER-MAP-RECONCILIATION.md`](../feature-00/CHARACTER-FEATURE-00-OWNER-MAP-RECONCILIATION.md).

## Delivery rules

1. Implement one row below at a time. A prerequisite owner remains authoritative; do not combine
   its work with the consuming CH3, CH4, CH5, or CH6 pass.
2. A row marked **confirmation gate** stops after the semantic decision and populated handoff.
   It does not author permanent IDs, schemas, or runtime code in the same pass.
3. Before any runtime work, use a clean catalog/database baseline. Do not import the persistent
   game database during normal repository implementation.
4. CH5 is the only root that creates a character. Child features return validated fragments or
   values; they do not create a partial actor, item, or receipt.
5. CH6 consumes the existing `orient`, `query`, and `commit(kind: "action")` surface unless a
   protocol walk proves a confirmed public-surface addition is unavoidable.

## Mandatory common reads for every Terra pass

Read these in order before the row-specific set:

1. `AGENTS.md`
2. `STATUS.md` and `KNOWN_ISSUES.md`
3. `CHARACTER_CREATION_PLAN.md`
4. `ARCHITECTURE.md` — only the ownership/integration sections consumed by the row
5. `SUBSYSTEM_IMPLEMENTATION_HANDOFF.md`
6. `ruleset/dnd2024/TERRA-FEATURE-PLANNING-GUIDE.md` for a planning pass, then
   `ruleset/dnd2024/TERRA-IMPLEMENTATION-HANDOFF.md` for the implementation pass
7. Live `procedure.system.create-feature`; also live `procedure.system.modify` before proposing
   permanent vocabulary, and every row-specific governing procedure listed below.

At implementation start, Terra must search `catalog/`, runtime code, and tests for each proposed
ID, its source term, and its likely synonyms. It must record catalog/database drift rather than
forcing an import or creating a parallel owner.

## Ordered implementation ledger

### 0. Reconfirm the supported first-build owner map

**Type:** confirmation and planning gate. No runtime change.

The Human Soldier Fighter is deliberately demanding: its final state includes origin feats and
traits, Fighter grants, HP, AC/equipment, weapon mastery, and a complete starting package. Before
assigning a consumer slice, recheck that every selected result has one active owner and that the
current owner can participate in CH5's single transaction.

Terra reads in addition to the common set:

- `character/feature-00/CHARACTER-FEATURE-00-DEPENDENCY-PLAN.md`
- `character/feature-03/CHARACTER-FEATURE-03-DEPENDENCY-PLAN.md`
- `character/feature-04/CHARACTER-FEATURE-04-DEPENDENCY-PLAN.md`
- `character/feature-05/CHARACTER-FEATURE-05-DEPENDENCY-PLAN.md`
- `campaign/feature-15/CAMPAIGN-FEATURE-15-CHARACTER-PARTICIPATION-PLAN.md`
- `ruleset/dnd2024/feature-23/IMPLEMENTATION-STATUS.md`
- Feature 25, 26, 27, and 28 dependency plans and their latest receipts

**Exit:** one table maps every CH0 selection to exactly one owner, identifies each missing leaf,
and states the first implementable leaf. If any selected choice has no owner, amend CH0 or plan
that owner; do not write CH3 declarations that hide the gap.

**Completed:** see the CH0 owner-map reconciliation above. It found that CH3 remains blocked by
missing behavior/content owners; the next pass is owner planning and semantic confirmation, not
CH3 implementation.

### 1. Close the remaining first-build ruleset/content leaves

**Type:** one or more independently planned owner slices. Do not merge them into character code.

These are the prerequisite families that must be proven for the ratified fixture before CH3/CH4
can describe it as source-complete:

1. Feature 28: immutable Origin-feat identities are accepted; plan the approved active behavior
   for Alert and Savage Attacker, plus the selected Human trait/Heroic-Inspiration owner if it
   remains part of CH0.
2. Feature 25: confirmed learned weapon-mastery grant semantics for the Fighter's selected weapons.
3. Feature 27: the level-one Fighter membership/HP policy and each selected feature's real owner;
   an entitlement with `behaviorStatus: "unimplemented"` is not sufficient.
4. Feature 24 plus the applicable equipment owner: **satisfied by Feature 24 Slice 4**. CH5 must
   compose final AC from legal equipment rather than accepting a caller-supplied number.
5. Feature 23/catalog content: source-cited definitions for every item in the chosen Fighter and
   Soldier package, then an owner-approved way for CH5 to instantiate and place them atomically.

Terra reads, as applicable:

- `ruleset/dnd2024/feature-23/IMPLEMENTATION-STATUS.md` and Feature 23 receipts, plus
  `ruleset/dnd2024/feature-23/IMPLEMENTATION-STATUS.md`
- `ruleset/dnd2024/feature-24/FEATURE-24-DEPENDENCY-PLAN.md`
- `ruleset/dnd2024/feature-25/FEATURE-25-DEPENDENCY-PLAN.md`
- `ruleset/dnd2024/feature-26/FEATURE-26-DEPENDENCY-PLAN.md`
- `ruleset/dnd2024/feature-27/FEATURE-27-DEPENDENCY-PLAN.md`
- `ruleset/dnd2024/feature-28/FEATURE-28-DEPENDENCY-PLAN.md`
- `ruleset/dnd2024/feature-30/FEATURE-30-DEPENDENCY-PLAN.md`
- The exact existing source, item, HP, AC, weapon, feature, and equipment contracts/mechanics
  selected by the ownership search.

**Exit:** every CH0 result has a source-cited, compatible owner that can return a closed fragment
to CH5. The output is not a character, a profile, or a public creation command.

### 2. CH3 Slice 1 — origin declarations and choice content

**Type:** confirmation gate, then one character-origin declaration slice.

Confirm the proposed shared grant-declaration, choice-set, background-selection, and frozen
grant-receipt vocabulary under `procedure.system.modify`. Then add only the immutable background
and choice declarations needed by the ratified path. Species profile and selected-species state
remain Feature 26-owned.

Terra reads in addition to the common set:

- `character/feature-03/CHARACTER-FEATURE-03-DEPENDENCY-PLAN.md`
- `character/feature-01/CHARACTER-FEATURE-01-DEPENDENCY-PLAN.md` and both receipts
- `character/feature-02/CHARACTER-FEATURE-02-DEPENDENCY-PLAN.md` and both receipts
- `ruleset/dnd2024/feature-26/FEATURE-26-SLICE-2-RECEIPT.md`
- `ruleset/dnd2024/feature-28/FEATURE-28-SLICE-2-RECEIPT.md` and
  `ruleset/dnd2024/feature-28/FEATURE-28-SLICE-3-RECEIPT.md`
- The current procedures for character content definitions, species selection, origin languages,
  proficiency recording, and item definitions.

**Exit:** the approved background and every permitted choice can be traversed to a real owner;
unsupported domains, source-version mismatch, duplicate grant keys, and hidden defaults fail.

### 3. CH3 Slice 2 — origin selection resolver and receipts

**Type:** one closed, zero-effect resolver slice.

Implement the background selection and origin resolver only after Slice 1 and all fixture owners
are accepted. It validates pre-bound selections and returns normalized owner-targeted values plus
immutable receipt data. It must not create items, attach a campaign actor, or write a component;
CH5 will be the sole transaction root.

Terra reads in addition to the common set:

- The accepted CH3 Slice 1 handoff and receipt
- `character/feature-03/CHARACTER-FEATURE-03-DEPENDENCY-PLAN.md`, Slice 2 and acceptance matrix
- `character/feature-05/CHARACTER-FEATURE-05-DEPENDENCY-PLAN.md`, staged-composition boundary
- The exact Feature 26/28 and proficiency/item owner contracts selected in the Slice 1 map

**Exit:** one valid origin selection resolves deterministically with no effects; invalid, repeated,
or unsupported selections leave all actor/item/campaign state unchanged.

### 4. CH4 Slice 1 — Fighter declaration, membership, and class resolver

**Type:** confirmation gate, then one class declaration/resolution slice.

Confirm the class-definition, initial single-class membership, and reuse of CH3 receipt semantics.
Implement only the immutable Fighter declaration and zero-effect class resolver. It must reject
spellcasting, a second class, a class level other than one, caller-computed HP/AC, and unsupported
feature grants.

Terra reads in addition to the common set:

- `character/feature-04/CHARACTER-FEATURE-04-DEPENDENCY-PLAN.md`
- accepted CH3 evidence and `character/feature-05/CHARACTER-FEATURE-05-DEPENDENCY-PLAN.md`
- `ruleset/dnd2024/feature-27/FEATURE-27-DEPENDENCY-PLAN.md` and latest receipt
- Feature 23–25 plans/receipts for the selected items, equipment, and mastery grants
- The current character-level, HP, AC, weapon-proficiency, and feature-owner contracts

**Exit:** the selected Fighter path resolves to closed, source-traceable level-one grants without
persisting actor state or pretending unimplemented feature identities are playable.

### 5. CH4 Slice 2 — cross-owner composition proof

**Type:** integration proof, not public creation.

Use the accepted CH3/CH4 resolvers to prove the complete origin/class output can be handed to its
actual HP, AC, proficiency, feature, item, and equipment owners. This proof must preserve the
single future CH5 root boundary and must include negative/rollback tests for every child owner.

Terra reads in addition to the common set:

- accepted CH3 and CH4 Slice 1 receipts
- `character/feature-04/CHARACTER-FEATURE-04-DEPENDENCY-PLAN.md`, Slice 2
- `character/feature-05/CHARACTER-FEATURE-05-SLICE-0-RECEIPT.md`
- exact contracts for every owner admitted by the first-build owner map

**Exit:** every first-build result can be supplied by a real owner and failures cannot leave an
actor, loose item, receipt, or success audit behind.

### 6. CH5 Slice 1 — closed validate/create planner and receipt

**Type:** confirmation gate, then root-planner implementation.

Confirm `procedure.character.create`, the creation receipt schema, the closed request format, and
the identifier policy. Implement identical full-build resolution for `validate` and `create`,
using the staged composer and only pre-bound approved definitions. `validate` returns no world
effects; it is not a server-side draft.

Terra reads in addition to the common set:

- `character/feature-05/CHARACTER-FEATURE-05-DEPENDENCY-PLAN.md`
- `character/feature-05/CHARACTER-FEATURE-05-SLICE-0-RECEIPT.md`
- accepted CH1–CH4 receipts and the C15 participation plan/receipts
- `ruleset/dnd2024/feature-30/FEATURE-30-DEPENDENCY-PLAN.md`
- `procedure.world.change`, the action-runner contract, and every selected child-owner contract

**Exit:** valid preflight returns one canonical bundle; any malformed, stale, cross-campaign, or
unresolved-owner request produces no character-world effect.

### 7. CH5 Slice 2 — atomic first-character creation

**Type:** one transaction and rollback acceptance slice.

Create the CH0 fixture once through the existing ActionRunner path. The fixed bundle creates the
actor, composes the C15 attachment, records profile/abilities/origin/class/owner state, creates and
places starting items, and adds the completion receipt last. Inject failures across every child,
effect, event, guard, reaction, receipt, and audit boundary.

Terra reads in addition to the common set:

- accepted CH5 Slice 1 handoff
- `character/feature-05/CHARACTER-FEATURE-05-DEPENDENCY-PLAN.md`, Slice 2 and acceptance matrix
- C15, Feature 23, and every CH3/CH4 child receipt/contract in the accepted bundle
- `procedure.world.change`, `procedure.action.run`, and event/subscription contracts reached by
  the bundle

**Exit:** exactly one legal request creates a campaign-attached, queryable character with its
receipt and selected starting state. Every failure rolls back all character-world effects.

### 8. CH6 Slice 1 — discovery and inspection using existing MCP kinds

**Type:** MCP contract confirmation, then discovery-only implementation.

Confirm `procedure.character.inspect`. Reinspect `VerbSurface` and `procedure.mcp.add-tool` before
writing anything. The normal outcome is no new tool or kind: a fresh client reads advertised
capabilities, the character procedures, approved content definitions, and the completed receipt
through existing `query` kinds.

Terra reads in addition to the common set:

- `character/feature-06/CHARACTER-FEATURE-06-DEPENDENCY-PLAN.md`
- accepted CH5 receipts and `DantesRoleplay.MCPServer/Tools/VerbSurface.cs`
- `DantesRoleplay.MCPServer/Tools/QueryTool.cs` and `CommitTool.cs`
- `catalog/procedures/system/procedure.mcp.add-tool.md` and
  `catalog/procedures/system/procedure.system.use.md`
- the accepted `procedure.character.create` and creation-mechanic contracts

**Exit:** a context-free client can discover the exact one supported build, its source/version,
campaign requirement, closed request, exclusions, and recovery calls without raw component edits.

### 9. CH6 Slice 2 — creation handoff and cold protocol walk

**Type:** final MCP acceptance slice.

Make successful creation return only the character ID, selected immutable source identities,
receipt presence, truly satisfied playable capabilities, and a literal next safe action. Prove
intent-routing uniqueness, then run a new-session walk: orient; discover; validate an invalid then
valid request; create; inspect actor and history; read one mechanic; and perform the supported
first action.

Terra reads in addition to the common set:

- `character/feature-06/CHARACTER-FEATURE-06-DEPENDENCY-PLAN.md`, Slice 2 and acceptance matrix
- accepted CH5 evidence and the live creation/first-action mechanics
- `COLDWALK.md`, `VERB_MIGRATION.md`, and the protocol-walk tests
- current `VerbSurface`, `QueryTool`, `CommitTool`, and action-routing tests

**Exit:** a fresh session creates exactly one actor with no human-supplied component IDs/effects and
reaches one safe action through only advertised, governed MCP calls.

## Explicitly deferred after CH6

| Feature | Why it is not needed for the MCP creation interface |
| --- | --- |
| CH7 | Corrections and controlled source expansion require real CH6 play evidence. |
| CH8 | Guided questions and browser parity are consumers of the stable CH5/CH6 command. |
| CH9 | Level advancement requires campaign policy and separate transaction semantics. |
| CH10–CH12 | Spellcasting, ASIs/feats at advancement, and multiclassing expand the supported path. |
| CH13 | Retirement/archive is lifecycle work after a created-character surface exists. |
| CH14 | Authenticated player control is a security/identity boundary, not trusted-host creation. |

## Verification bar for every implementation row

- Run focused tests while iterating.
- Run `roleplay validate catalog` after catalog changes.
- Run the full suite at the row's acceptance gate and `git diff --check` before its receipt.
- Run a real protocol walk whenever MCP registration, action routing, or public result shape changes.
- Query/read back all new catalog and runtime artifacts; prove negative, replay, and rollback paths.
- Update only the owning feature plan, its receipt, `STATUS.md`, and this ledger when the evidence
  genuinely changes. Stop before beginning the next row.

## Stop conditions

Stop and return to planning if an owner map is incomplete, a required source choice cannot be
closed, a permanent ID/schema/public surface needs confirmation, a current owner conflicts with a
proposed one, catalog and runtime state drift, or a child cannot participate in the CH5 transaction.
Never bypass an absent owner by accepting caller-supplied derived values, raw effects, a manual
inventory array, an arbitrary content ID, or a partial character draft.
