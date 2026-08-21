# Feature 32 dependency plan — spell resolution, effects, and duration

Status: **Slice 1 verified; immutable spell-resolution profiles are implemented. Active-effect, cast, and consequence work remains blocked on its named lifecycle and composition seams.**
Last updated: 2026-08-21

## Execution rule

Slice 1 created only a static catalog component, definition procedure, and source profiles co-
located with Feature 31 spell identities. It created no actor or gameplay state. A later
implementation pass must re-read Feature 31, action economy, D20/save/damage/healing, tactical,
condition, concentration, and clock contracts; reconcile catalog/database drift; confirm IDs;
validate a disposable catalog import; write a receipt; and stop after one reviewed slice.

## Target capability

The game can resolve one legal source-cited spell through an authoritative resource, action,
targeting, D20/save, consequence, and duration/effect lifecycle without duplicating any underlying
rule owner.

### Included

- Immutable spell-resolution profiles: source-declared casting action, range/target form, duration
  class, concentration requirement, resolution family, and named consequence-family interfaces.
- A future player-facing cast root that checks Feature-31 availability, spends the correct action
  and slot atomically, and routes exactly one source profile to its owned resolver family.
- Spell attack, spell save, self/creature/area targeting, instant consequences, durable active
  effect identity/ending, and duration lifecycle as separate later slices.
- The active effect protocol required by Feature 18 concentration and Feature 37 timed travel
  consequences, but not a duplicate concentration owner.

### Excluded

- Spell lists, known/prepared choices, slots, spellcasting ability, spell save DC, and spell attack
  bonus; Feature 31 owns them.
- Generic scripts, caller-supplied spells/DCs/modifiers/targets/effects, every SRD spell in one
  pass, ritual/rest casting, counterspell/ready timing, and spell points.
- Reimplementation of action economy, weapon attacks, saving throws, damage mitigation, healing,
  temporary HP, conditions, tactical geometry, visibility, concentration, or world time.

## Official source basis

The source is `source.dnd2024.srd-5.2.1`, *System Reference Document 5.2.1* (Wizards of the Coast
LLC, 2025-05-01, CC-BY-4.0): [Spellcasting and casting spells, PDF pp. 103–106](https://media.dndbeyond.com/compendium-images/srd/5.2/SRD_CC_v5.2.pdf), [Spells, PDF pp. 106–175](https://media.dndbeyond.com/compendium-images/srd/5.2/SRD_CC_v5.2.pdf), and [Rules Glossary > Duration, Concentration, Areas of Effect, PDF pp. 176–187](https://media.dndbeyond.com/compendium-images/srd/5.2/SRD_CC_v5.2.pdf).

- Each spell declares casting time, range, components, duration, and effect. A caster follows
  those entries; spell attacks use a spell attack modifier and spell saves use the caster’s spell
  save DC supplied by Feature 31.
- Targets must be legal for the declared spell; a clear path and area rules are spatial concerns.
  Duration and Concentration are lifecycle facts, not a slot or a target-side condition.
- The same spell cast multiple times does not create duplicate overlapping benefits; a durable
  effect owner must make source, target, start, ending, and replacement semantics observable.

## Planning inventory and ownership result

| Inquiry | Evidence and decision |
| --- | --- |
| Spell identities/resources | Feature 31 owns canonical spell identity, availability, slots, derived DC/attack modifier, and resource transitions. Feature 32 consumes typed projections and never accepts those values from the caller. |
| Action cost | Feature 12 owns spending/restoring Action, Bonus Action, Reaction, and interaction. Feature 32 decides an individual spell’s declared cost and composes with the one spender. |
| D20 and saving throws | Feature 3 owns the D20 convention; Feature 4 owns target saving throws. Spell attack arithmetic is a new Feature-32 family over Feature-31 statistics, while spell-save consequences reuse Feature 4 rather than copy its roll. |
| Damage/healing/conditions | Features 9, 15–17 and 13 own their respective state changes. Feature 32 selects a source-declared consequence and calls its owner; it does not write HP, mitigation, or condition components directly. |
| Targeting/geometry | Feature 20 owns placement/distance/reach, Feature 21 cover/range geometry, and Feature 34 visibility. A spell profile’s range is static; legal targets and areas must consume those future tactical owners. |
| Persistent effects | No durable spell-effect identity/ending protocol exists. Feature 18 and Feature 37 identify this exact missing owner as Feature 32; an effect cannot be a free-form component on caster or target. |
| Duration/time | No approved clock/expiry lifecycle is available. Feature 33 owns rests and Feature 37 confirms timed effect expiry is blocked until Feature 32 defines effect identity plus a shared time owner is confirmed. |
| Existing spell owner | Searches find no spell resolution component, effect instance, spell target, spell attack, area, duration, or cast mechanic. Feature 31’s static identity is intentionally not a resolver. |

## Recursive dependency analysis

```text
Feature 32: spell resolution, effects, and duration
├─ official spell source and Feature-31 identity/resource seam   [identity implemented; resource later]
├─ canonical spell identity                                      [implemented: Feature 31 Slice 1]
├─ immutable spell-resolution profile                            [implemented: Slice 1]
├─ active spell-effect instance/explicit end protocol            [blocked: resolution profile + lifecycle confirmation]
├─ resource/action cast admission parent                         [blocked: Feature 31 + Feature 12 composition]
├─ spell attack and spell-save resolver families                 [blocked: admission + Features 3–4]
├─ target/area/range/cover/sight validation                      [blocked: Features 20–21, 34]
├─ instant damage/healing/condition consequences                 [blocked: Features 9, 13, 15–17]
├─ timed effect duration/expiry                                  [blocked: effect protocol + clock owner]
├─ concentration integration                                     [blocked: Feature 18 + effect protocol]
└─ source spell vertical slices                                  [blocked parent]
```

The static profile is a leaf only after Feature 31 has established a single canonical spell
identity reference. It describes how a spell will be resolved; it does not make that spell
available or castable.

## Dependency and ownership decisions

1. Spell identity and spell-resolution profile are separate immutable components on the same
   versioned spell-content entity. Feature 31 owns the former; Feature 32 owns the latter. A
   profile references neither a character nor a mutable slot/effect instance.
2. The profile contains closed structural facts: action family, range/target/area family, duration
   family, concentration-required flag, resolution family, and named consequence-family keys. It
   never contains executable JavaScript, a caller-target, dice result, final damage, DC, slot, or
   ad-hoc condition/effect payload.
3. A future active spell effect is a durable entity/component identity, not copied data on caster
   or target. It records exact spell-profile version, creator, authorised affected subject(s),
   active/ended lifecycle, and source-defined duration basis. It must expose one normal end path
   and observable end reason so Feature 18 can reference/end it without becoming an effect owner.
4. The cast root derives every input from profile and authoritative projections. It requests the
   Feature-31 slot transition and Feature-12 action spend within one root transaction. No failed
   precondition may consume either resource or create an effect instance.
5. Spell attacks are Feature-32 D20 resolution using Feature-31’s derived attack modifier; weapon
   attack rules remain Feature 8. Spell saves invoke Feature 4 with a derived, trusted DC and then
   apply only the source-declared consequence through its owner.
6. Static range is not a successful target check. Feature 20/21/34 determine placement, path,
   cover, and sight when the spell requires them. Self and explicitly non-spatial spells must not
   fabricate map or target state just to use a shared API.
7. Concentration remains Feature 18’s creature-state/loss rule. Feature 32 marks a profile as
   concentration-requiring and creates/ends its effect instance; it does not add a second
   concentration list, damage save, or Incapacitated/death reaction.

## Confirmation boundary

| Decision | Required confirmation before implementation |
| --- | --- |
| Profile schema | Exact Feature-32 component/procedure IDs, profile fields, source/identity reference, closed family vocabularies, and immutable revision policy. |
| First static spell set | Exact source entries/locators and which resolution families they exercise; no spell becomes playable from a profile alone. |
| Effect protocol | Effect entity/component IDs, creator/subject relationship shape, active/end/replacement semantics, end-reason vocabulary, and composition/event ordering with Feature 18. |
| Cast admission | Feature-31 availability/slot child result, Feature-12 action cost route, action timing, failure order, and atomic resource rollback. |
| Targeting | Shared target/area/range input representation and exact Feature 20/21/34 data projections, including self/object/creature policy. |
| Consequences | Each source family’s real owner and typed child/result/effect seam for spell attacks/saves, damage, healing, conditions, movement, and summons. |
| Duration | Clock/turn/rest authority, expiry/replacement/cancellation semantics, and next-dawn/long-duration policy; turns cannot be assumed to be a universal clock. |

## Slice order and stop gates

| Order | Slice | Starts only when | Exit gate |
| --- | --- | --- | --- |
| 0 | Canonical spell identity | Feature 31 Slice 1 verified. | **Verified:** every resolution profile references one immutable, source-cited spell identity. |
| 1 | Immutable spell-resolution profiles | Slice 0 and permanent profile vocabulary confirmed. | **Verified 2026-08-21:** source profiles read back with zero actor/cast/effect behaviour. |
| 2 | Active effect identity and explicit ending protocol | Slice 1 and lifecycle/event confirmation. | A typed active effect has one creator/source/end path and no duration or concentration workaround. |
| 3 | Cast admission and resource/action atomicity | Slices 1–2, Feature 31 resources, Feature 12 composition. | A legal declared cast can reserve/spend exactly its action/slot or leave both unchanged. |
| 4 | Effect-free spell attack/save evidence | Slice 3, Features 3–4, and trusted target context. | One profile resolves exact attack/save evidence without applying HP/conditions/effects. |
| 5 | Instant consequence families | Slice 4 and each HP/damage/healing/condition owner. | One source spell applies only its declared immediate consequence atomically. |
| 6 | Spatial and area families | Slice 3, Features 20–21/34, and area model. | A ranged/area spell validates exact legal targets and geometric preconditions. |
| 7 | Duration and concentration integration | Slice 2, duration clock, Feature 18, and source spell fixture. | Active effects start/end/expire/replaced consistently; concentration has one effect reference. |
| 8 | Source expansion | Prior family gates and source review. | Each additional spell reuses an accepted profile/action/effect family, never scripts a new rule. |

## Slice 1 — immutable spell-resolution profiles

### Runtime artifacts

- A confirmed `dnd2024.spell-resolution-profile` component/schema and governing static-definition
  procedure, attached only to a versioned entity with a valid Feature-31 spell identity.
- Source-cited profile fixtures for a small confirmed set representing a non-Concentration instant
  spell and a Concentration duration spell. Exact spell keys/locators are confirmed before writing.
- Focused catalog validation/tests only. No actor resources, cast action, target selection, D20
  roll, slot/action spend, active-effect entity, duration, condition, HP, or event.

### Data contract and required state

The profile is closed and immutable. It names the exact spell identity/version; action family;
range/target/area family; duration family; whether Concentration is required; resolution family
(none, attack, save, or declared special); and canonical consequence-family keys. It records no
numeric range, target id, area coordinates, component/focus inventory, slot level, DC, modifier,
die/damage/healing amount, duration remaining, active effect id, condition, or code.

The source spell identity’s level/source/version must match. A profile has one source locator to
the spell’s casting/duration/effect entry and only allowed compatible family combinations. Missing
identity, stale/mismatched source/version, unknown/duplicate/out-of-order key, impossible
Concentration/duration combination, or extra field rejects unchanged.

### Recording behaviour, result, and effects

Catalog authoring/readback validates source profile data. The result is canonical static metadata
with zero effects and no player-facing matching phrase. A range declaration cannot calculate
distance; an attack/save declaration cannot roll; a concentration declaration cannot create effect
or concentration state; an instant consequence declaration cannot change a creature.

### Invariants, failure behaviour, and non-goals

- Exactly one profile attaches to a spell identity/version; a source correction creates a reviewed
  successor content version rather than mutating a profile referenced by an effect/receipt.
- Profile family keys are declarative interfaces, never effect scripts or a generic pass-through
  to arbitrary child mechanics.
- Rejection/readback leaves all spell resources, actions, actors, positioning, items, effects,
  conditions, HP, campaigns, events, and audits unchanged.

### Slice 1 implementation sequence

1. Re-read Feature 31 spell identity boundary, source registry, current action/D20/save/damage/
   condition/tactical contracts, and Feature 18’s required active-effect protocol. Repeat ownership
   searches for spell effect, duration, target, area, spell attack, cast, ritual, and concentration.
2. Stop for permanent-ID/family/fixture confirmation and explicit Feature-31/32 content attachment
   decision. Do not encode a future effect object or script in profile data.
3. Author schema/procedure/fixtures and focused tests together, verifying exact source locators.
4. Test identity/version/source compatibility, canonical ordering, incompatible families,
   malformed/extra input, immutable revision, replay, and zero-effect isolation.
5. Query every artifact back; run `roleplay validate catalog`, focused tests, full suite, and `git
   diff --check`; write a receipt and stop before Slice 2.

### Slice 1 acceptance matrix

| Case | Exact assertion |
| --- | --- |
| Source profiles | Each confirmed spell identity has one exact source-cited resolution profile with canonical family declarations. |
| Distinct lifecycle | The instant and Concentration-duration fixtures differ in source-declared duration/concentration metadata only; neither creates an active effect or concentration component. |
| Closed shape | Missing/null/wrong-type/unknown/duplicate/out-of-order/extra family data, mismatched spell identity/version, wrong source, and incompatible family combinations reject unchanged. |
| Immutability | Rewriting an existing profile or adding a second profile for the same identity/version rejects; successor source data uses a distinct versioned identity. |
| Isolation | Profile reads return zero effects and cannot alter slot state, action budget, D20 state, target position, HP, conditions, effects, or campaign state. |
| Determinism | Equivalent reads return byte-identical data with no random call, routing phrase, or derived calculation. |
| Repository | Catalog validation, focused tests, full suite, diff check, and source/catalog query-backs pass. |

### Slice 1 exit gate

Slice 1 is verified only when source-cited static spell resolution profiles have one owner, closed
versioned data, effect-free readback, rejection/immutability/isolation evidence, catalog and
repository checks, and a receipt. Stop before active effects, casting, targeting, rolls, spending,
or consequences.

## Active-effect and consumer map

```text
spell-resolution profile
├─ Feature-31 availability / slot projection ────> cast admission
├─ Feature-12 Action/Bonus/Reaction spend ───────> cast admission
├─ Feature-20/21/34 target/area/range/sight ─────> spatial validation
├─ Feature-32 spell attack or Feature-4 save ────> resolution evidence
├─ Features 9, 13, 15–17 consequence owners ────> instant effects
├─ Feature-32 active-effect entity / duration ───> source/target/end lifecycle
├─ Feature-18 concentration state ───────────────> one active effect reference
└─ Feature-33 clock/rest owner ──────────────────> expiry and recovery timing
```

## Plan-quality audit

- One spell-resolution/effect-lifecycle capability, concrete source, scope/non-goals, ownership
  inventory, and recursive graph: yes.
- Static profile, resources, action spend, targeting, D20/save, consequence, effect identity,
  duration, and concentration are all distinct owners: yes.
- Slice 1 is an independent static leaf after Feature 31 identity, with closed data, source,
  immutability, failure, isolation, and repository gates: yes.
- No runtime game artifact was created by this planning pass: yes.

## Plan-change rule

Revise before implementation if Feature 31 changes spell identity/reference semantics, a compatible
effect lifecycle exists, Feature 18 requires a different effect protocol, or tactical/time owners
select incompatible input models. Do not use caller-provided target/DC/slot/effect values, a
generic spell script, a caster/target copied effect list, an implicit duration clock, direct HP or
condition writes, or a second concentration owner.
