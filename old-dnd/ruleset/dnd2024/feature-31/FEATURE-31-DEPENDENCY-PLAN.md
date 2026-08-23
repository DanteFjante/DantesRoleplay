# Feature 31 dependency plan — spellcasting resources and casting statistics

> **D&D implementation reference:** When this work includes D&D 2024 mechanics, inspect [Foundry VTT dnd5e](https://github.com/foundryvtt/dnd5e) before designing or coding. Use it as a licensed engineering reference—not a direct dependency or rules authority—while retaining the exact `source.dnd2024.srd-5.2.1` locator and all applicable MIT, CC-BY, and asset-license notices for any reused material.

Status: **Slice 1 verified; immutable spell identities are implemented. Slice 2 remains blocked on a ratified caster source/class seam.**
Last updated: 2026-08-21

## Execution rule

Slice 1 created only the static catalog component, definition procedure, and two source-cited
spell entities. It created no actor or gameplay state. A later implementation pass must re-read
the current class, ability, level, character-integration, and spell contracts; reconcile
catalog/database drift; confirm permanent IDs; validate a disposable catalog import; write a
receipt; and stop after one reviewed slice.

## Target capability

The game can identify source-cited spells and, in later slices, derive a supported character’s
legal spell availability, slots, casting ability, spell save DC, and spell attack bonus without
storing any derived statistic or resolving a spell effect.

### Included

- Immutable spell identity/level/source catalog data and later immutable spellcasting
  profile/spell-list declarations.
- One source-backed single-class casting-resource state: profile reference, known/prepared choices
  where the profile requires them, and remaining spell slots.
- Effect-free availability, casting-statistics, and slot diagnostic readers.
- Typed resource transitions for Feature 32 casting and Feature 33 rest recovery only after their
  composition/lifecycle contracts are accepted.

### Excluded

- Spell attacks, target selection, areas, saves, damage/healing, conditions, duration, ritual
  execution, concentration, components/foci, and spell effects; Feature 32 owns those.
- A generic character spell-list component, caller-selected class/profile/list, multiclass slot
  aggregation, pact/alternative casting, spell points, non-SRD spells, feats/species/items, and a
  public “cast spell” action.
- Direct changes to ability scores, total level, class membership, action economy, rests, item
  custody, or campaign/character creation state.

## Official source basis

The source is `source.dnd2024.srd-5.2.1`, *System Reference Document 5.2.1* (Wizards of the Coast
LLC, 2025-05-01, CC-BY-4.0): [Equipment > Spellcasting and Character Creation, PDF pp. 102–105](https://media.dndbeyond.com/compendium-images/srd/5.2/SRD_CC_v5.2.pdf), [Classes and spell lists, PDF pp. 28–82](https://media.dndbeyond.com/compendium-images/srd/5.2/SRD_CC_v5.2.pdf), and [Spells, PDF pp. 106–175](https://media.dndbeyond.com/compendium-images/srd/5.2/SRD_CC_v5.2.pdf).

- A spellcaster’s class features state its spellcasting ability and available spells; a level-1+
  spell expends a spell slot of its level or higher, while a Cantrip needs no slot.
- Spell save DC is 8 plus spellcasting ability modifier plus Proficiency Bonus. Spell attack
  modifier is ability modifier plus Proficiency Bonus.
- A spell’s casting time, range, duration, targeting, components, attack/save, and effect are
  spell-resolution facts, not Feature-31 resource state.

## Planning inventory and ownership result

| Inquiry | Evidence and decision |
| --- | --- |
| Ability and level | Features 1–2 own authoritative abilities and total level/derived Proficiency Bonus. Feature 31 reads them and never accepts/stores a casting modifier, DC, attack bonus, or level. |
| Class source/level | Feature 27 owns immutable class progression; CH4/CH9 own class membership and transitions. Feature 31 requires one confirmed caster profile/class-level seam and cannot infer casting from a class name. |
| Character integration | Character CH10 explicitly assigns spell definitions, slots, known/prepared state, casting ability, DC, and attack bonus to Feature 31. CH5/CH9 remain the atomic creation/advance roots. |
| Spell content | Searches find no D&D spell component, spell slot state, prepared/known list, casting statistic, or spell-definition owner. Existing character content definition excludes spells, so Feature 31 needs a distinct ruleset spell identity owner. |
| Spell execution | Feature 32 owns targeting, actions, spell attacks/saves, effects, durations, and actual slot use in play. Feature 31 provides a typed resource seam; it does not expose a player-facing cast operation. |
| Concentration | Feature 18 awaits persistent spell-effect identity/ending protocol. Feature 31 must not represent a maintained effect as spell preparation or a slot. |
| Action/rest lifecycle | Feature 12 owns action resources and Feature 33 owns rest recovery. Slot spend must compose only with Feature 32; slot recovery/preparation changes must compose only with Feature 33. |
| Magic items/species | Features 26 and 29 may later declare spell access, but they cannot add parallel slots or spell lists. Their source declarations must route through the accepted Feature-31 model. |

## Recursive dependency analysis

```text
Feature 31: spellcasting resources and casting statistics
├─ SRD spellcasting/source identity                              [implemented source basis]
├─ immutable spell identities                                    [implemented: Slice 1]
├─ immutable class spellcasting profile and spell-list data      [blocked: caster source/class seam]
├─ single-class resource state and diagnostic reader             [blocked: profile + Feature 27/CH10]
├─ casting-statistics reader                                     [blocked: resource + Features 1–2]
├─ known/prepared selection and mutation                         [blocked: profile + CH10 + Feature 33]
├─ typed slot consumption                                        [blocked: resource + Feature 32 composition]
├─ rest recovery / preparation refresh                           [blocked: Feature 33]
├─ multiclass/alternate/item/species casting                     [excluded blocked successors]
└─ playable spell casting                                        [blocked parent: Feature 32]
```

The lowest leaf is static spell identity. It lets later profiles reference exact versioned spells
without claiming that a character knows, prepares, casts, or resolves any of them.

## Dependency and ownership decisions

1. A new immutable `dnd2024.spell-identity` belongs on a versioned ruleset spell-content entity.
   It carries only a stable spell key/version, spell level, and source reference. It avoids
   extending character content definition, whose closed vocabulary intentionally excludes spells.
2. A later `dnd2024.spellcasting-profile` is immutable source data: casting ability key,
   single-class level-to-slot progression, selection convention, and spell-list references. It is
   neither actor state nor a class-membership replacement; profile/class compatibility is checked
   from explicit source references.
3. A future actor `dnd2024.spellcasting-resources` records only current mutable resource/selection
   facts for one accepted profile. Missing means no supported spellcasting state, never empty slots
   or an inferred Cantrip list. It cannot contain a class level, ability score/modifier, maximum
   slots, DC, attack bonus, source prose, target, duration, or cast history.
4. Slot maxima, spell save DC, and spell attack modifier are derived by readers from the profile,
   class level, abilities, and total-level Proficiency Bonus. They are never fields supplied by a
   caller or stored on an actor.
5. Known/prepared state is closed reference membership selected according to the profile’s one
   source convention. A spell identity appearing in a catalog does not imply legal selection;
   profile/list/version/class-level rules remain authoritative.
6. Feature 32 is the only user-facing casting owner. It validates a spell availability projection,
   action/target/effect prerequisites, and invokes a confirmed Feature-31 resource transition as
   part of its root. Feature 31 never spends a slot because a caller says a spell was cast.
7. Feature 33 owns recovery timing and preparation change points. Feature 31 can expose a typed
   reset/replace transition only after Feature 33 supplies an authorised completed-rest context.

## Confirmation boundary

| Decision | Required confirmation before implementation |
| --- | --- |
| Spell identity | Exact component/procedure ids, spell entity ID syntax, source locator shape, level vocabulary, and immutable revision policy. |
| First content | Exact initial spells and locators, catalog content scope, and whether Feature 32 will attach a separate resolution profile. |
| Casting profile | Source class/profile identity, casting ability, level/slot table, selection convention, spell-list reference shape, and single-class compatibility check. |
| Actor state | Component/reader ids, exact remaining-slot/known/prepared shapes, canonical ordering, missing/empty/replacement semantics, and corrupt/stale reference policy. |
| Derived reader | Class-level and total-level consistency, ability modifier calculation, Proficiency Bonus use, slot maximum calculation, and closed diagnostic/result form. |
| Mutation handoffs | Feature 32 typed slot-consumption route, Feature 33 rest/preparation route, CH5/CH9 virtual/existing actor composition, and event/audit order. |

## Slice order and stop gates

| Order | Slice | Starts only when | Exit gate |
| --- | --- | --- | --- |
| 1 | Immutable spell identity catalog | Permanent identity vocabulary and initial source entries confirmed. | **Verified 2026-08-21:** Fire Bolt and Cure Wounds read back deterministically with zero actor or gameplay effects. |
| 2 | Immutable casting profile and spell-list catalog | Slice 1, Feature 27 class seam, and one ratified caster source path. | A source profile declares its precise selection/slot facts without an actor record. |
| 3 | Single-profile resource state and diagnostic reader | Slice 2 and CH10/CH5/CH9 composition agreement. | One actor’s closed selection/remaining slots validates; all maximums/DC/attack values are read-only derivations. |
| 4 | Source-bound selection/preparation mutation | Slice 3, profile timing, and Feature 33/CH10 contract. | Legal choices change only at the profile-authorised root boundary and never leave stale references. |
| 5 | Slot consumption/recovery transitions | Slices 3–4 plus Features 32–33 composition. | A slot is changed exactly once in a casting/rest root or the entire root rolls back. |
| 6 | Caster character integration | Slices 2–5 plus CH10/CH5 or CH9 fixture. | One source-cited caster receives only its legal resource state atomically. |
| 7 | Expansion | Slice 6 and each new owner/source review. | A new class/list/casting model is added by amendment, never by permissive generic input. |

## Slice 1 — immutable spell identity catalog

### Runtime artifacts

- A confirmed `dnd2024.spell-identity` schema/component and governing static-definition procedure.
- Versioned `content.dnd2024.spell.*.v1` entities for a small source-confirmed initial identity set.
  The verified set is Fire Bolt, Cure Wounds, and Dancing Lights: an instant Cantrip, an instant
  level-1 spell, and a concentration-duration Cantrip needed by Feature 32’s static-profile proof.
- Focused catalog validation/tests only. No spellcasting actor state, class profile, spell list,
  slot, selection, resource action, effect, or public intent phrase.

### Data contract and required state

Each identity is closed and immutable: stable key/version, `spellLevel` from 0 through 9, and a
fixed source reference to its individual SRD spell entry. It has no school, casting time, range,
components, target, duration, damage dice, saving throw, attack, condition, concentration state,
class list, resource cost, caster, or executable effect. Those facts are owned by later static
Feature-32 profiles or class/profile declarations as appropriate.

An identity’s entity id/key/version must agree exactly with its component. Missing/mismatched
identity, invalid level, source outside the registered SRD, unknown/duplicate version, or extra
field rejects unchanged. A correction is a distinct reviewed versioned entity, never a mutation of
one a character profile or receipt may later reference.

### Recording behaviour, result, and effects

Static catalog authoring validates/reads immutable source data. A read returns canonical id/key,
version, spell level, and source reference, with zero effects. It has no actor role, random call,
selection/cast intent, resource mutation, or child effect. A valid level-1 identity does not grant
a slot; a Cantrip identity does not grant a known spell or permission to cast it.

### Invariants, failure behaviour, and non-goals

- The spell catalog has one source of truth for stable spell identity/level; no character stores
  an unversioned name or copied level.
- This slice must not use `dnd2024.character.content-definition`, add a generic spell payload, or
  reserve Feature-32 target/effect semantics in an opaque field.
- Rejections leave all existing entities, actors, inventory, action state, campaign state, and
  audit/event success evidence unchanged.

### Slice 1 implementation sequence

1. Re-read the source registry, content-definition conventions, Feature 27/CH10 boundaries, and
   current catalog vocabulary. Repeat searches for spell, slot, preparation, Cantrip, ritual,
   casting ability, and magic action ownership.
2. Stop for permanent ID/entity syntax/source fixture confirmation. Confirm the boundary with
   Feature 32 so resolution data is not prematurely claimed here.
3. Author schema/procedure/entities and focused tests together. Check exact source entries and
   locators without copying spell prose or executable mechanics into the catalog.
4. Test valid catalog readback plus wrong source/key/version/level, duplicate/out-of-order data,
   extra fields, immutable revision, replay, and zero-effect isolation.
5. Query artifacts back; run `roleplay validate catalog`, focused tests, full suite, and `git diff
   --check`; write a receipt and stop before casting profiles or actor state.

### Slice 1 acceptance matrix

| Case | Exact assertion |
| --- | --- |
| Source identities | Each confirmed initial spell has one active versioned identity, exact level, and fixed individual SRD locator. |
| Cantrip versus levelled | Spell level 0 and a level-1 identity round-trip as distinct values; neither creates a slot, resource, selection, or action. |
| Closed data | Missing/null/wrong-type/out-of-range/extra identity fields, mismatched key/version, duplicate version, and wrong source reject unchanged. |
| Immutability | An attempted in-place rewrite and second same key/version reject; a reviewed successor uses a distinct version. |
| Isolation | Reads have zero effects and leave abilities, level, class, HP, actions, items, conditions, and campaign state byte-identical. |
| Determinism | Equivalent reads are byte-identical with no randomness, player routing, or source-derived calculation. |
| Repository | Catalog validation, focused tests, full suite, diff check, and query-backs pass. |

### Slice 1 exit gate

Slice 1 is verified only when immutable spell identity has one source-cited owner, closed
versioned source data/readback, rejection/immutability/isolation evidence, catalog validation,
repository checks, and a receipt. Stop before declaring a spell list, profile, actor resource,
casting statistic, or cast action.

## Later resource and consumer map

```text
spell identity
├─ class spellcasting profile / list ───────────> Feature 27 + CH10 source declaration
├─ actor known/prepared and remaining slots ────> Feature 31 resource owner
├─ ability modifier + Proficiency Bonus ────────> Features 1–2 derived reader inputs
├─ slot maximum / DC / attack modifier ─────────> Feature 31 derived reader outputs
├─ selection/preparation change ────────────────> CH10 + Feature 33 authorised timing
├─ Action / target / slot spend / resolution ───> Feature 32 + Feature 12 composition
├─ duration / concentration / ending ───────────> Features 18 + 32
└─ rest recovery / item/species/feat expansion ─> Feature 33 / Features 26, 28–29
```

## Plan-quality audit

- One resource/statistics capability, official source, explicit scope/non-goals, owner inventory,
  and recursive graph: yes.
- Static spell identity, immutable profile, actor mutable state, derived calculations, casting, and
  recovery have separate owners: yes.
- Slice 1 is an independent static catalog leaf with closed data, source, immutability, failure,
  isolation, and repository acceptance: yes.
- No runtime game artifact was created by this planning pass: yes.

## Plan-change rule

Revise before implementation if a compatible spell identity owner appears, Feature 32 requires a
different static-content relation, Feature 27/CH10 selects a different class/profile seam, or the
platform cannot compose typed resource fragments in CH5/CH9 roots. Do not store a spell name/list,
slot maximum, DC, attack bonus, caster class level, cast result, or effect on a character outside
the confirmed owner; do not make a generic spell script or public cast endpoint.
