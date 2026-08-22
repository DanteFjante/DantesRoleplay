# Character Feature 4 dependency plan — class membership and level-one grants

> **D&D implementation reference:** When this work includes D&D 2024 mechanics, inspect [Foundry VTT dnd5e](https://github.com/foundryvtt/dnd5e) before designing or coding. Use it as a licensed engineering reference—not a direct dependency or rules authority—while retaining the exact `source.dnd2024.srd-5.2.1` locator and all applicable MIT, CC-BY, and asset-license notices for any reused material.

Status: **Planned; class declaration work awaits CH0–CH3, while a playable class fixture also awaits dedicated class/HP/AC/equipment and feature-effect owners.**  
Last updated: 2026-08-20

## Execution rule

This repository planning artifact follows AGENTS.md, `procedure.system.create-feature`, `procedure.system.modify`, `procedure.world.change`, the [Character Creation Plan](../../CHARACTER_CREATION_PLAN.md), CH0–CH3, Items Slice 6, and the existing D&D component contracts. It creates no runtime artifact.

CH4 owns a first-level, single-class membership and source-declared class grants. It consumes the generic CH3 grant declarations and receipts. It never makes `dnd2024.character-level` a class record, copies hit-die/AC/HP formulas to an actor, creates item instances, or permits a spellcasting class before CH10.

## Target capability

A proposed creation can resolve exactly one approved non-spellcasting class definition at class level 1, prove that it agrees with total character level 1, resolve all its closed grants once, and dispatch the resulting values to their established owners inside the future CH5 atomic creation operation.

The initial class is fixture content, not a class-specific schema. A second non-spellcasting level-one class with the same supported declaration forms is representable after source review; subclass, multiclass, later-level, and spellcasting structures remain explicitly separate.

### Included

- An immutable class-definition component attached to a CH1 `class` content entity.
- One single-class actor membership record containing a versioned class-definition ID and initial class level 1 only.
- Reuse of CH3 `dnd2024.character.grant-declarations`, choice sets, and `dnd2024.character.grant-receipts` for class grants and features.
- Closed resolution of validated skill/save/weapon proficiency, item-definition, feature-definition, and class-vital-stat grant targets only when their true owner exists.
- Cross-owner composition requirements for the existing level, proficiency, HP, AC, and item writers.

### Excluded

- Spellcasting classes, spells, slots, prepared/known lists, casting ability, spell DC/attack bonus, and spell features (CH10 plus ruleset spellcasting plans).
- Subclasses, class level 2+, XP, level-up, multiclassing, class replacement, respec, Hit Dice/rest recovery, feats, and ASIs (CH9–CH12 and ruleset class/rest owners).
- An actor-side hit die, HP formula, AC formula, weapon/item state, second total-level field, proficiency bonus, or copied class rules prose.
- Public command transport, item-instance/containment creation, partial class drafts, unbound definition IDs, source-text parsing, and direct raw effects.

## Existing-owner boundaries and discovered blockers

| Concern | Authoritative owner and CH4 rule |
| --- | --- |
| Total level | Existing `mechanic.dnd2024.character-level.record`; CH4 requires total level 1 to agree with class level 1 but never replaces it with class state. |
| Per-class identity/level | CH4's single membership component only. CH12 owns a future plural class-level model and migration; CH4 must reject any second class. |
| Hit die and level-one HP | The current HP writer stores validated final pairs but explicitly does not calculate class advancement. A dedicated source-cited class/HP resolver is a required external leaf; CH4 must not submit caller-calculated HP. |
| Final AC | The current AC writer stores a final value but does not build it from equipment/Dexterity/class rules. A dedicated AC/equipment derivation owner is required; CH4 must not choose a number. |
| Skills, saves, and weapon categories | Existing closed recorders own membership only. CH4 resolves declared grants and invokes them through CH5; it stores no source acquisition list beside the shared receipt. |
| Starting equipment | Items owns definitions, instances, possession, and containment. CH4 may resolve selected definition keys; Items Slice 6/CH5 creates and contains instances. |
| Class features | A feature-definition receipt records only immutable identity. A feature counts as playable only if its separate mechanical owner is implemented; a reference does not simulate a trait or class rule. |

The ruleset roadmap currently lists classes/levels and armor/equipment derivation as planned, not verified. Therefore a complete playable CH4 fixture is blocked even if the administrative HP/AC writers exist. This is deliberate: recording an arbitrary final number would hide missing rules rather than implement them.

## Proposed permanent vocabulary — confirmation required

| Role | Proposed ID and boundary |
| --- | --- |
| Class declaration | `dnd2024.character.class-definition`, attached only to a CH1 class content entity. It contains no sourceRef/title/key, which remain CH1-owned. |
| Actor membership | `dnd2024.character.class-membership`, attached once to the actor with `classDefinitionId` and a `classLevel` whose schema range is 1–20. CH4 may create only the initial value `1`; CH9 owns every supported increase and CH12 owns a second membership. |
| Class contract and record/resolve mechanisms | `procedure.mechanic.dnd2024.class`; `mechanic.dnd2024.class.record`; `mechanic.dnd2024.class.resolve`. |

Confirm these IDs, component meanings, parent-procedure scope, and any compatible class owner under `procedure.system.modify` before authoring. `mechanic.dnd2024.class.resolve` is an internal zero-effect resolver for CH5, not a public action. It reuses CH3 receipt vocabulary and must not create a parallel class-grant receipt.

## Class declaration and resolver boundary

The closed class-definition component contains only: `hitDie` as a canonical source input, `spellcasting` fixed false for this feature, a `levelOneGrantSet` reference to the generic grant declaration, and a level-one feature-definition reference list when the features have identified owners. It has no name, sourceRef, ability scores, calculated HP/AC, item definition, subclass, or arbitrary future-level table.

The membership recorder accepts a pre-bound approved class definition after the CH5 coordinator has verified campaign/profile scope and CH2 total level. It rejects absent/archived/wrong-kind/unknown definition, `spellcasting: true`, a second membership, an initial class level other than 1, or total-level disagreement. It writes only the membership component. The component range is not an authorization to write a later level: CH9 supplies the dedicated guarded advancement transition.

The resolver reads the immutable definition, reuses CH3 choice-set checks, and returns canonical class grant targets plus shared receipt entries with no effects. It rejects duplicate grant keys across the selected origins and class, overlapping target values unless a target owner's source rules explicitly allow them, unsupported feature effect, unresolved equipment choice, stale version, or any attempt to supply HP, AC, a derived bonus, or effects. CH5 later performs all normal owner calls, membership, receipt, and item creation in one transaction.

## Dependency graph and slices

~~~text
CH0 ratified non-spellcasting class, locators, choices, and expected owners    [missing]
└─ CH1 class/feature definitions + CH2 ability/level + CH3 grants/receipts    [blocked parents]
   ├─ existing skill/save/weapon state recorders                               [implemented]
   ├─ class/level-one HP resolver                                               [missing ruleset leaf]
   ├─ AC/equipment derivation resolver                                          [missing ruleset/item leaf]
   ├─ feature-effect owners and Items Slice 6 as the selected class needs them [external leaves]
   └─ confirmed CH4 vocabulary
      ├─ Slice 1: class declaration and zero-effect resolution
      └─ Slice 2: cross-owner composition harness
         └─ CH5 atomic character creation
~~~

### Slice 1 — immutable class content and closed resolution

**Prerequisites:** CH0 selects one non-spellcasting class and maps every level-one grant/choice/feature to a real owner; CH1–CH3 are accepted; class IDs are confirmed.

1. Add the class contract, closed class-definition and membership schemas, and internal resolver.
2. Record exactly one source-cited class entity through CH1 content provenance and attach its level-one declaration plus generic grant declarations/choice sets.
3. Test class kind/version/spellcasting rejection, exactly-level-one membership, duplicate membership, total-level mismatch, duplicate cross-origin/class grant key, and no-effect resolver failure.
4. Run `roleplay validate catalog`.

**Exit:** the class declaration yields a complete, source-traceable level-one grant resolution with every target owner named; no resolver result invents mechanics or persists state.

### Slice 2 — cross-owner composition proof

**Prerequisites:** Slice 1 accepted; the class/HP and AC/equipment derivation leaves required by CH0's fixture are accepted; Items Slice 6 and feature-effect owners are accepted where needed; CH5 transaction boundary is ready for integration.

1. Feed validated class results to existing skill/save/weapon writers, the dedicated HP/AC derivation owners, feature owner(s), and Items Slice 6 only through CH5's transaction coordinator.
2. Record membership and the shared receipt exactly once after all grant prerequisites validate; no child writer can leave a character, item, or receipt on failure.
3. Prove a level-one character has consistent total/class level and can use the existing relevant check/save/weapon path; prove HP/AC came from their derivation owner rather than caller input.
4. Test every owner failure injection and rollback. Run focused tests and `roleplay validate catalog`; full acceptance occurs with CH5.

**Exit:** one fixture class produces all and only its declared level-one state through existing owners, while an invalid or failed grant leaves no partial actor, item, receipt, or success audit.

## Acceptance matrix

| Case | Exact expected result |
| --- | --- |
| Single-class model | One actor has one non-spellcasting class definition at class level 1 and matching total level 1. A second or level-2 membership fails unchanged. |
| Definition provenance | Class and feature references resolve to immutable CH1 versioned definitions; no actor stores class name, rules prose, sourceRef, hit die, or feature rule copy. |
| Grant integrity | Class and origin `grantKey` values are globally unique in the creation resolution; shared receipts prevent a duplicate/replayed grant. |
| Vital-stat correctness | HP and AC inputs come only from their dedicated source-cited derivation owners. Caller-supplied final values/formulas are rejected. |
| Equipment/proficiency ownership | Existing recorders own proficiency state; Items owns selected-item instantiation and containment. CH4 keeps only closed source grant results and receipts. |
| Feature support | A feature reference without an active mechanical owner blocks the fixture; it is not counted as playable merely because a receipt exists. |
| Spell boundary | Any spellcasting declaration, spell grant, slot, spell input, or casting statistic fails before effects and is deferred to CH10. |
| Failure atomicity | Bad class, stale definition, duplicate grant, missing owner, child failure, or scope mismatch yields no partial character state or loose items. |

## Evidence and change control

The later implementation receipt records confirmed IDs, CH0 class locators, feature/owner map, grant fixtures, negative cases, cross-owner proof, item integration proof, and catalog validation. Do not duplicate formulas or source rules into this roadmap.

Amend CH4 before adding a spellcasting class, subclass, another class level, a second class, HP/AC calculation, new grant target/form, class feature behavior, or public command. Those boundaries belong respectively to CH10, CH9, CH12, the ruleset class/armor features, CH3/CH4 amendment, a feature owner, or CH5/CH6.
