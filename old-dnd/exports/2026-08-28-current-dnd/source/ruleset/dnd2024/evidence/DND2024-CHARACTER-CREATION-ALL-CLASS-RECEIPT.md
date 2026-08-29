# D&D 2024 all-class level-1 character creation receipt

Status: **accepted**
Date: 2026-08-27
Owner: [CC-MVP-C1 implementation](../DND2024-CHARACTER-CREATION-ALL-CLASS-IMPLEMENTATION.md)
Source: `source.dnd2024.srd-5.2.1`, the Core Traits and level-1 feature tables for all twelve SRD
classes (PDF pp. 28–78)

## Delivered boundary

- Added immutable `dnd2024.class-creation-profile` declarations for Barbarian, Bard, Cleric,
  Druid, Fighter, Monk, Paladin, Ranger, Rogue, Sorcerer, Warlock, and Wizard.
- Added twelve matching level-1 class progression models and all 34 referenced level-1 feature
  identities. The existing Fighter IDs and level-2 progression remain intact.
- Generalized the existing basic creator's trusted `class` role. It now derives class Hit Points,
  saving throws, deterministic legal class skills, and complete weapon-category proficiencies from
  the selected model without a class switch or D&D rule in C#.
- Preserved exact primary-ability meaning and level-1 spell-table quantities, including Fighter's
  one-of Strength/Dexterity choice and Wizard's six-spell spellbook declaration.
- Armor, tool choices, spells, restricted Martial subsets, starting equipment, and every class
  feature without an accepted mechanic are stored as sorted pending entitlements and grant no
  approximate behavior.
- The creation request, atomic actor/participation transaction, replay identity, and rollback owner
  are unchanged.

## Evidence

- Focused basic-creation/class-model matrix: 42 passed, 0 failed.
- Complete `Dnd2024AbilityCheckTests`: 244 passed, 0 failed.
- Class integrity check: exactly 12 class models; every referenced feature identity exists; all 44
  newly added class-slice JSON artifacts parse.
- Fresh disposable catalog validation: 144 valid base records and 21 existing non-blocking
  near-duplicate advisories; no live data touched.
- Full solution: 1,283 shared tests and 21 Local AI tests passed, 0 failed.
- Acceptance also corrected an incomplete web-interface call site to supply the registry parameter
  already required by `GetPageAsync`; this is a generic compile repair and adds no D&D behavior.
- No protocol walk was required because protocol registration did not change.

## Deliberate exclusions

This receipt proves correct level-1 class models and basic creation, not source-complete character
resolution. Spell selection/casting, class-feature behavior and resources, armor/equipment/tool
application, restricted Martial property resolution, levels 2–20 beyond the retained Fighter row,
subclasses, multiclassing, background choice, and UI discovery remain independent future slices.
Pending entries grant no behavior until an accepted owner consumes them.
