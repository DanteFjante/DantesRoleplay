# Feature 30 dependency plan — guided character creation and playable-sheet acceptance

> **D&D implementation reference:** When this work includes D&D 2024 mechanics, inspect [Foundry VTT dnd5e](https://github.com/foundryvtt/dnd5e) before designing or coding. Use it as a licensed engineering reference—not a direct dependency or rules authority—while retaining the exact `source.dnd2024.srd-5.2.1` locator and all applicable MIT, CC-BY, and asset-license notices for any reused material.

Status: **CH5 Slice 0's generic staged-composition proof is accepted. Feature 30 creates no parallel creation contract or actor state.**
Last updated: 2026-08-21

## Execution rule

This plan is an integration boundary only. It creates no runtime procedure, component, entity,
mechanic, fixture, action, event, subscription, migration, campaign state, or UI. The character
roadmap remains authoritative for CH0–CH8: Feature 30 consumes their verified contracts and records
the D&D ruleset acceptance sequence. An implementation pass must select the named owning Character
slice, not add a Feature-30 substitute.

## Target capability

A host can discover one supported source-cited level-one build, validate it without state change,
create it atomically in an active campaign, read the completed sheet, and immediately use its
existing ruleset mechanics.

### Included

- One stateless, MCP-guided complete-build flow using CH5’s future `validate`/`create` operation.
- The first supported Human Soldier Fighter fixture, its source definitions, closed choices,
  campaign attachment, complete receipt, and readback/play acceptance.
- Cross-owner dependency and verification order from immutable content through creation to one
  ability check, saving throw, inventory inspection, and supported basic combat use.
- A later browser/UI consumer only as parity work after the semantic command is stable.

### Excluded

- A second create command, a Feature-30 component, custom actor schema, inventory list, direct
  database write, raw effect API, server-side drafts, or a new MCP kind/tool.
- Visual character-builder UX, authentication/player-control, correction/respec, advancement,
  multiclassing, spellcasting, every SRD option, and homebrew rules.
- Inferring that a content definition, profile, campaign participation, or level alone proves a
  playable completed character. The CH5 receipt is the completion boundary.

## Official source basis

The governed rules are the registered `source.dnd2024.srd-5.2.1`, *System Reference Document
5.2.1* (Wizards of the Coast LLC, 2025-05-01, CC-BY-4.0), [Character Creation, PDF pp. 19–23](https://media.dndbeyond.com/compendium-images/srd/5.2/SRD_CC_v5.2.pdf), together with the source
locators fixed by the approved CH0 Human Soldier Fighter path. This feature adds no independent
rules interpretation: source selection, origin grants, class facts, equipment, and mechanical
outcomes stay with their named character/ruleset owners.

## Planning inventory and ownership result

| Concern | Current owner and Feature 30 rule |
| --- | --- |
| First supported path | Character CH0 is ratified for Human Soldier Fighter and defines source facts, choices, owner map, and expected state. Feature 30 does not select a second path. |
| Actor/profile/provenance | CH1 is accepted; its profile fragment is internal to the future CH5 root. A profile neither creates an actor nor proves campaign scope. |
| Abilities/level | CH2 owns the policy/validation and existing-state composition. Feature 30 never accepts a modifier, proficiency bonus, HP, or final AC from the caller. |
| Origins and class | CH3 and CH4 own source declarations, selections/grant receipts, and level-one membership. Feature 30 reads their future accepted results only. |
| Atomic creation | CH5 alone owns the action-root, complete request, creation receipt, transaction ordering, rollback, and staged virtual-target composition. |
| Discovery/play handoff | CH6 owns content/character inspection and current MCP-surface decision. Feature 30 must not expose an alternate player command. |
| Campaign scope | Campaign C15 owns active character participation and CH5 consumes its internal planned-target attachment planner. Feature 30 stores no campaign id on the actor. |
| Items and ruleset state | Feature 23 owns item instances/containment; Features 1–9 and later feature owners retain every mechanical component/formula. |
| Completion/expansion | CH7 owns post-play regression/correction/expansion; CH8 owns builder parity. Feature 30 has no independent expansion rule. |

## Recursive dependency analysis

```text
Feature 30: guided creation producing one playable sheet
├─ CH0 ratified Human Soldier Fighter source path                [implemented planning decision]
├─ C15 active campaign-participation / planned attachment seam   [implemented consumer seam]
├─ CH1 actor provenance/profile fragment                          [accepted]
├─ CH2 ability policy and existing-state composition              [accepted]
├─ CH3 origin declarations, choices, and receipts                 [planned parent]
├─ CH4 class membership and level-one grants                      [planned parent]
├─ Feature 23 starting item/containment integration               [accepted foundation]
├─ exact class/HP/AC/feature and species-grant owners             [blocked ruleset parents]
├─ generic not-yet-persisted actor staged composition             [implemented Slice 0]
├─ CH5 atomic validate/create root and completion receipt         [blocked: prior nodes]
├─ CH6 inspection/current MCP handoff                             [blocked: CH5]
├─ existing check/save/inventory/basic-combat consumers           [implemented when required state exists]
└─ one end-to-end playable-sheet acceptance                       [blocked parent]
```

The lowest independently useful change is not a Feature-30 artifact. It is CH5 Slice 0’s generic
staged-composition proof: child planners must validate a reserved, not-yet-persisted actor and
return only ordered effect fragments for the one root transaction.

## Dependency and ownership decisions

1. Feature 30 is a consumer acceptance plan. It cannot define a competing creation receipt,
   selection component, item grant, or player-facing action because CH3–CH6 already own them.
2. Creation is complete-only and stateless. A guide may show supported choices or report failures,
   but it submits CH5’s one closed build; abandoning guidance persists no draft or partial actor.
3. The campaign is an explicit role to the CH5 root. C15’s internal planner supplies attachment
   effects for the reserved actor; neither the payload nor an actor profile can carry a campaign
   assertion.
4. Validation and creation share one resolution route. `validate` has zero character-world
   effects; `create` re-resolves the complete request and atomically applies exactly the accepted
   child effects. A validation response is not a reusable mutable server-side plan.
5. The first playable sheet is an acceptance fixture, not an authorization to hard-code Human,
   Soldier, Fighter, a display name, or final numeric statistics into a generic schema. Each later
   source path needs CH7/source-owner review.
6. “Playable” is operational: a newly created actor must have its completion receipt and pass the
   exact existing consumers named in the fixture. A superficial profile or a manually edited set
   of components cannot satisfy this gate.

## Confirmation boundary

| Decision | Required confirmation before the owning implementation |
| --- | --- |
| Staged composition | Generic virtual-target context, declared child dependencies, virtual effect ordering, deterministic projection, rollback, and no direct child write. |
| First fixture readiness | CH0 source-definition set; all CH2–CH4 choices/grants; Feature 23 item grants; C15 attachment; exact HP/AC/species/feature owners. |
| Root request | CH5’s complete closed input, canonical character-id policy, error/result shape, and validate/create equivalence. |
| Completion | Exact creation-receipt schema/order, root audit/event correlation, and query condition that distinguishes complete from absent/failed creation. |
| Public handoff | CH6’s discovery/inspection mechanisms and whether the current commit surface needs a separately approved extension. |
| Acceptance play | Exact first safe ability check, saving throw, inventory query, and combat action after creation; no consumer is counted until its required state is truly supplied. |

## Slice order and stop gates

| Order | Owning slice | Starts only when | Feature 30 acceptance result |
| --- | --- | --- | --- |
| 0 | CH5 Slice 0 — staged-composition proof | Generic owner search and semantic confirmation. | A parent can assemble virtual new-actor effects deterministically in one transaction; no character content is created. |
| 1 | CH2/CH3/CH4 lower contracts | CH0 path and each owner/ID confirmation. | Ability/origin/class planners provide closed, effect-fragment-ready results; unsupported grants remain named blockers. |
| 2 | Ruleset source owners | Their own dependency plans and first static/runtime slices. | The fixture has real HP/AC/equipment/species/class effects only where dedicated owners are verified. |
| 3 | CH5 Slice 1 — preflight/root contract | Slices 0–2 for the fixture and permanent request IDs. | `validate` returns canonical diagnostics/bundle evidence with zero character-world effects. |
| 4 | CH5 Slice 2 — complete fixture transaction | Slice 3 and all fixture components/items/attachment. | One active-campaign actor, items, receipts, and history are created atomically or absent on every failure. |
| 5 | CH6 — discovery and play handoff | CH5 accepted and current public-surface review. | A fresh context discovers, validates, creates, reads, and uses the fixture without manual component editing. |
| 6 | CH7/CH8 expansion and builder parity | Played CH6 fixture and stable command. | Each new path/UI route preserves identical source/state/receipt semantics. |

## First implementation handoff — CH5 Slice 0

### Boundary

The only current Feature-30 implementation candidate is the Character CH5 owned staged-composition
proof. It must be generic infrastructure: a root receives an immutable planned-target context
(reserved absent id, name, active campaign role/attachment intent, and canonical prior virtual
effects); each declared child validates only that context and returns ordinary effects for the root
bundle. It must not create a persistent actor, call an MCP handler, execute nested commits, or
implement Human Soldier Fighter data.

### Required evidence and behaviour

- A child cannot observe undeclared state, invent an entity id, reorder/rewrite a sibling fragment,
  or return effects outside the root’s declared target/context.
- Root dry-run and commit validate the same frozen ordered bundle and use one ActionRunner
  transaction. Any effect/guard/reaction/event/audit failure rolls back all character-world effects.
- Equivalent context/children produce byte-identical fragments and diagnostics. A missing,
  malformed, stale, colliding, or unconfirmed target fails before a durable effect.
- This proof creates no public command, actor, campaign participation, profile, source definition,
  ability/class/origin selection, item, receipt, or creation action.

### Exit gate

CH5 Slice 0 is accepted only with a confirmed generic contract, focused deterministic and
failure-injection evidence, query-backed zero-persistence proof, catalog/repository checks, and a
receipt. At that point return to CH2–CH4 and the actual ruleset owner map; do not proceed directly
to a Feature-30 create action.

## End-to-end acceptance matrix

| Case | Required evidence once its owning slice is accepted |
| --- | --- |
| Discovery | A fresh host reads only active supported definitions/choice shapes through CH6, never arbitrary ids or copied rules text. |
| Complete request | Missing/extra/free-text/derived/effect/campaign-id/item-instance/audit input rejects before any planned bundle or actor. |
| Validation | Valid and invalid CH0 fixture requests use the same owner planners; validation has zero actor, item, containment, participation, receipt, event, and success-audit effects. |
| Creation | The exact legal request creates one actor, C15 attachment, authoritative component set, contained starting items, grant receipts, and creation receipt in dependency order. |
| Ownership | Every persisted field can be traced to exactly one lower owner; Feature 30 itself contributes no component data or formula. |
| Atomicity | Failure at entity, attachment, profile, choice/grant, vital-stat, item/containment, guard, reaction, event, receipt, or audit leaves no partial character-world evidence. |
| Completion/readback | The actor has the last-added creation receipt and source set; query-back resolves source definitions, participation, components, containment, and root audit correlation. |
| Play | The actor completes one existing ability check, one saving throw once its grant exists, inventory inspection, and the supported basic combat path without manual editing. |
| Parity | A future UI and MCP submit the same complete request and produce byte-equivalent actor/receipt/item state; abandoned guidance creates nothing. |

## Plan-quality audit

- One outcome, official source, explicit non-goals, ownership inventory, and recursive graph: yes.
- The plan resolves Feature 30’s overlap by delegating every permanent fact and command to CH/Ruleset
  owners rather than creating a sibling model: yes.
- One actual next assignment is named, with a clear no-content/no-persistence exit gate: yes — CH5
  Slice 0 generic staged composition.
- Validation, creation, owner traceability, atomicity, readback, play, and UI parity have objective
  acceptance evidence: yes.
- No runtime game artifact was created by this planning pass: yes.

## Plan-change rule

Revise before implementation if CH5 chooses a different root/composition model, C15 changes
planned attachment semantics, a chosen source grant lacks an owner, or CH6 needs a confirmed new
public surface. Do not address a dependency by creating a Feature-30 component, a partial actor,
a duplicate create endpoint, manual final stats, an inventory array, a server draft, or a UI-only
rules path.
