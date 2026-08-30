# D&D 2024 mechanic repair — canonical weapon activities

Status: **implemented; focused acceptance passed, parent acceptance pending**  
Parent: [mechanic contract-owner repair](DND2024-MECHANIC-CONTRACT-REPAIR-DEPENDENCY-TREE.md)  
Ruleset alignment: **dnd2024-compatible**

## Outcome

Replace the retired monolithic `dnd2024.weapon-profile` dependency with the normalized weapon and
activity owners already present in the canonical schema. Make every authored SRD weapon definition
executable for its base attack modes, then adapt the retained weapon writer, attack resolver, and
damage roller to those current owners.

## Confirmed boundary

The user confirmed current-style permanent IDs and missing D&D feature owners on 2026-08-30.
Duplicate search found no authored weapon activities and no current entity IDs below the selected
namespace. The slice may add only activity-definition entities named from their owning weapon:

- `dnd2024.equipment.weapon.<weapon>.attack` for one-mode definitions;
- suffixes such as `.melee`, `.thrown`, `.one-handed`, and `.two-handed` only when the source table
  exposes distinct attack or damage facets.

No replacement weapon-profile component is created. Existing owners are:

- `dnd2024.item.weapon` for category, properties, and mastery identity;
- `dnd2024.activity.membership` for the weapon-to-activity collection;
- `dnd2024.activity.activation`, `dnd2024.activity.attack`, `dnd2024.activity.damage`, and
  `dnd2024.activity.range` for executable base attack facts;
- `dnd2024.creature.proficiencies` and the existing derived level/Armor Class children for attack
  arithmetic.

## Implementation rules

1. Populate all 38 current SRD weapon definitions from the local SRD 5.2.1 weapon table, retaining
   exact damage, ability eligibility, melee/reach or normal/long range, and Versatile alternatives.
2. Require the selected activity to be an active activity definition referenced by the weapon's
   membership. Fail closed for mismatched weapon/activity pairs, malformed facets, unavailable
   references, unsupported properties, or noncanonical ability choices.
3. Keep base damage rolling and attack arithmetic in JavaScript mechanics. Activity records declare
   dice, type, range, and delivery; they never store a final attack or damage total.
4. Adapt `mechanic.dnd2024.weapon-profile.write` as an administrative normalized-facet writer rather
   than restoring the retired component.
5. Preserve existing mechanic IDs and the existing `ruleset.dnd2024.*` category names so activation
   cannot create duplicate capabilities.

## Deliberate exclusions

Property behavior (Ammunition consumption, Heavy, Light, Loading, mastery effects, and hand-capacity
enforcement), tactical distance/visibility, and exact attack-to-damage transaction binding remain
separate follow-on owners. This slice records the source facts needed by those owners but does not
claim those behaviors are complete.

## Verification

- focused current-schema tests for one-mode, Finesse, ranged, thrown, Versatile, fixed-damage, and
  malformed/mismatched cases;
- every weapon membership is nonempty and every referenced activity validates its archetype;
- all mechanic bodies compile;
- active-contract component-owner audit has no `dnd2024.weapon-profile` reference;
- `roleplay validate catalog`, prototype tests, and final parent acceptance.

Focused acceptance passed on 2026-08-30: all 38 weapons own one or more of 51 unique,
schema-valid attack activities; four mechanic bodies compile; 7 focused tests pass; catalog
validation reports 144 valid records and only the 21 pre-existing advisories; and `git diff
--check` passes. The prototype suite passes 163/165 tests, with only the two existing local-DM-seat
expectation failures in `game-server-context.test.js` remaining outside this slice.
