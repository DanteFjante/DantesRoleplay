# Character Feature 2 dependency plan — ability assignment and existing-state composition

> **D&D implementation reference:** When this work includes D&D 2024 mechanics, inspect [Foundry VTT dnd5e](https://github.com/foundryvtt/dnd5e) before designing or coding. Use it as a licensed engineering reference—not a direct dependency or rules authority—while retaining the exact `source.dnd2024.srd-5.2.1` locator and all applicable MIT, CC-BY, and asset-license notices for any reused material.

Status: **Implemented and accepted; full-suite verification passed.**
Last updated: 2026-08-20

## Execution rule

This repository planning artifact follows AGENTS.md, `procedure.system.create-feature`, `procedure.system.modify`, `procedure.world.change`, the [Character Creation Plan](../../CHARACTER_CREATION_PLAN.md), and the existing D&D contracts below. It creates no runtime artifact.

CH2 composes established D&D state owners. It may add a narrowly governed ability-assignment policy and normal ability-score recorder only after confirmation; it must not introduce a second ability component, modifier, total level, proficiency bonus, HP, AC, weapon, or grant system.

## Target capability

The future character-creation coordinator can validate six submitted raw ability scores against one immutable, source-cited assignment policy selected by CH0, record them through the existing `dnd2024.abilities` owner, and record total level 1 through the existing total-level recorder. Existing check and saving-throw mechanics then read their authoritative components and derive their own results.

### Included

- One versioned ability-assignment-policy definition for the CH0-approved method.
- Closed validation of all six raw scores and either a fixed multiset or point-budget policy.
- A normal, add-only six-score recorder governed by the existing ability-score contract.
- Composition rules for `dnd2024.character-level` at level 1 and for later CH3/CH4 calls to the existing proficiency, HP, and final-AC recorders.
- Focused consumer tests showing existing ability checks and saving throws use existing state.

### Excluded

- Choosing method/scores (CH0), actor creation/attachment (CH1/CH5), origin/class grants (CH3/CH4), starting items (Items/CH5), and root transaction/public command (CH5/CH6).
- Modifiers, proficiency bonus, saving-throw totals, passive values, DCs, roll results, XP, class levels, hit dice, HP formulas, armor formulas, equipment, and source-rule prose.
- Random/rolled generation, free-form point costs, post-creation score increases, and score reassignment. A random method needs a confirmed CH2 amendment with deterministic auditable rolls.

## Existing-owner composition

| Existing owner | CH2 rule |
| --- | --- |
| `procedure.mechanic.dnd2024.abilities` / `dnd2024.abilities` | The only six-score state: exact lowercase `str`, `dex`, `con`, `int`, `wis`, `cha`; integer 1–30. Modifiers remain derived. Initial creation uses `component.add`; later one-score change follows its governed merge path. |
| `procedure.mechanic.dnd2024.character-level` / `mechanic.dnd2024.character-level.record` | CH5 records level `1` through this existing mechanism. Its fixed source reference and derived proficiency bonus remain its owner's concern. CH2 supplies neither. |
| Skill/save proficiency recorders | CH3/CH4 resolve grants and call their existing closed-list recorders. CH2 neither selects proficiencies nor stores acquisition source. |
| Weapon-proficiency, HP, and final-AC writers | CH4 later supplies validated class/equipment results to their owners. CH2 must not infer or record arbitrary final values just because the components exist. |
| `mechanic.dnd2024.check.ability` and `mechanic.dnd2024.saving-throw` | Consumer proofs only. Both require abilities and total level; saving throws also require saving-throw proficiency state. They own d20 resolution and derived totals. |

## Confirmed CH2 vocabulary

| Role | Proposed ID and boundary |
| --- | --- |
| Immutable assignment-policy component | `dnd2024.character.ability-assignment-policy`, attached only to a versioned policy entity, never an actor. |
| Governing policy procedure and validator | `procedure.mechanic.dnd2024.character-ability-assignment-policy`; `mechanic.dnd2024.character-ability-assignment-policy.validate`. The validator produces normalized scores and no effects. |
| Existing-ability normal recorder | `mechanic.dnd2024.abilities.record`, governed by existing `procedure.mechanic.dnd2024.abilities`; it writes only one absent `dnd2024.abilities` component. |
| Initial CH0 policy entity | `content.dnd2024.ability-assignment.standard-array.v1`, the source-cited Standard Array fixture. |

The policy family and initial entity ID are confirmed for Slice 1. The ability recorder remains a
separate Slice 2 confirmation boundary. The policy entity ID carries stable key/version; no actor
stores a duplicate policy ID. CH5's later creation receipt records that policy version with its
other creation sources. If CH1's content-definition design is formally expanded to own policies,
stop and reconcile that single ownership decision first.

## Policy data and validation boundary

The policy component is a closed source-cited declaration containing policy version, `sourceRef`, score bounds, and exactly one allocation family:

| Family | Declaration | Submitted-score test |
| --- | --- | --- |
| `fixed-multiset` | Exactly six integer values; duplicates are permitted. | The six submitted scores, compared without labels, equal the declared multiset exactly once each. |
| `point-budget` | Nonnegative budget plus a closed ascending score-to-cost table and score bounds. | Every submitted score is listed and its six costs sum exactly to budget. |

The first CH0 record uses one family only; the schema supports both so a fixture choice is not a hard-coded capability. A new family is a semantic schema expansion, not an arbitrary string or script.

The validator receives its bound policy by role, not a caller-selected entity ID. Its exact input is the six-key score object. It rejects missing, extra, wrong-case, noninteger, nonfinite, out-of-policy-range, incorrect multiset/budget, corrupt policy, and derived fields such as `modifier`, `proficiencyBonus`, `sourceRef`, `roll`, or `effects`. It returns canonical scores and no effects. The recorder receives only those scores, requires absent ability state, validates the existing component schema, and emits one `component.add`. It has no policy, source, correction, merge, grant, or effect input.

## Dependency graph and slices

~~~text
CH0 ratified Standard Array method, locator, bounds, and assignment        [verified]
└─ CH1 accepted provenance + campaign actor-scope contracts                [verified parent]
   ├─ confirmed CH2 Slice 1 vocabulary                                      [accepted semantic gate]
│  └─ Slice 1: policy declaration + zero-effect validator
   └─ existing ability/level recorders re-read and compatible               [verified]
      └─ Slice 2: add-only ability recorder + integration harness
         └─ CH3 origins → CH4 class grants → CH5 atomic creation
~~~

### Slice 1 — immutable policy and validation

**Prerequisites:** CH0 names a `fixed-multiset` or `point-budget` method, locator, bounds, and one complete legal assignment; CH1 Slice 1 is accepted; permanent IDs/schema meanings are confirmed.

1. Add the policy contract, closed schema, and zero-effect validator.
2. Record exactly one versioned source-cited policy entity from CH0. No actor is created.
3. Test valid assignments, each rejected input class, corrupt policy data, and replay-stable validation. Rejections have no effects and leave state unchanged.
4. Run `roleplay validate catalog`.

**Exit:** a reviewer can trace the policy to CH0/SRD 5.2.1, recompute its allowed scores, and prove validation neither writes state nor accepts arbitrary or derived values.

**Status: Accepted.** Receipt: [CH2 Slice 1 receipt](CHARACTER-FEATURE-02-SLICE-1-RECEIPT.md).

### Slice 2 — existing-state recording and consumer proof

**Prerequisites:** Slice 1 accepted; current ability/level contracts remain compatible; CH1 actor scope/profile contract is accepted for any character-level fixture; new recorder ID confirmed.

1. Add the ability-score recorder beneath its existing procedure with absent-only add semantics.
2. In an integration harness, bind the CH0 policy, validate scores, record the returned scores, and invoke the existing total-level recorder with `level: 1`.
3. Use existing recorders only when prerequisites exist: a raw check needs abilities; saving-throw proof also needs valid saving-throw proficiency state later supplied by CH4. Do not create HP, AC, weapon, or proficiency data merely to claim integration.
4. Prove consumer mechanics derive modifier/proficiency bonus from existing state; no CH2 state contains their outputs. Run focused tests and `roleplay validate catalog`.

**Exit:** valid inputs yield one canonical ability component and level-one record through their owners; duplicate/failed paths leave no state; consumers resolve from existing state only.

**Status: Accepted.** Receipt: [CH2 Slice 2 receipt](CHARACTER-FEATURE-02-SLICE-2-RECEIPT.md).

## Acceptance matrix

| Case | Exact expected result |
| --- | --- |
| Source/policy fidelity | One immutable policy matches CH0's method and SRD locator; unknown source, blank locator, or altered policy fails. |
| Assignment completeness | Exactly six canonical integer scores are required. A fixed array preserves its multiset; a point buy totals its declared budget. |
| Breadth boundary | Both deterministic families are representable; dice/random methods are explicitly unsupported until a CH2 amendment. |
| Ability ownership | `dnd2024.abilities` remains the sole score state and has exactly its existing fields. No modifier/policy lives on the actor. |
| Level/proficiency boundary | Only the existing level recorder sees `1`; proficiency bonus remains derived. CH2 writes no proficiency state. |
| Vital-stat boundary | CH2 writes no HP or AC and derives neither. Those writers are reserved for CH4. |
| Failure atomicity | Invalid policy/assignment, duplicate recording, corrupt prerequisite, or wrong scope produces no partial component/effect/audit success. |
| Consumer proof | Existing ability-check/save mechanics calculate from ability, level, and where required existing proficiency state, without CH2 calculation. |

## Evidence and change control

The implementation receipt records confirmed IDs, CH0 policy reference/locator, policy fixture ID, accepted/rejected tests, consumer proofs, and catalog validation. Do not copy ratified scores or source rules into the roadmap.

Return here before adding dice rolling, a third allocation family, score increases/correction, a public creation command, caller-selectable policy, actor policy reference, or HP/AC/proficiency grant. Those boundaries belong to a confirmed CH2 amendment, CH7, CH5/CH6, CH3, or CH4.
