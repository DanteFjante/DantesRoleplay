# Character starting-equipment Slice 1 receipt — Fighter weapons

Accepted: 2026-08-21

## Scope delivered

The confirmed first-build Fighter Package A now has source-cited static definitions for its three
weapons:

- `weapon.dnd2024.greatsword` and `item.dnd2024.greatsword.v1`: Martial Melee, Strength, 2d6
  Slashing, Heavy/Two-Handed, Graze, 6 lb.
- `weapon.dnd2024.flail` and `item.dnd2024.flail.v1`: Martial Melee, Strength, 1d8 Bludgeoning,
  Sap, 2 lb.
- `weapon.dnd2024.javelin` and `item.dnd2024.javelin.v1`: Simple Melee, Strength, 1d6 Piercing,
  Thrown 30/120, Slow, 2 lb.

All use the existing Feature 7 `dnd2024.weapon-profile` owner and fixed
`source.dnd2024.srd-5.2.1`, `Equipment > Weapons` attribution. Their versioned item definitions
use the existing Feature 23 `dnd2024.item-definition` owner and reference profiles rather than
copying combat statistics.

## Intentionally not delivered

- No campaign item instance, transfer, placement, equipment state, or character-creation grant.
- No Weapon Mastery permission or Graze, Sap, Slow, Heavy, Two-Handed, or Thrown gameplay effect.
- No Dungeoneer's Pack entity. It is a source-defined list of individual objects, whose remaining
  definitions and atomic starting-package grant still need their own confirmed slice.
- No class membership, Fighter feature, HP, Armor Class, or MCP public-surface change.

## Verification

- `CatalogCharacterStartingEquipmentTests` fresh-imports a disposable catalog and reads all six
  entities back with exact profile and definition facts.
- `dotnet run --project DantesRoleplay.Tools -- validate catalog` passed with 399 valid records and
  73 pre-existing routing/near-duplicate warnings; it touched no live data.
- Focused Feature 7, Feature 23, and starting-equipment tests passed: 5/5.
- Full suite: 788 passed, 1 failed. The sole failure is the known unrelated
  `CatalogFeature10Tests.Imported_catalog_replays_the_feature_10_vertical_session_in_two_fresh_databases`
  transcript expectation, which does not yet expect the imported `dnd2024.encounter-sides`
  component. It exercises no starting-equipment artifact.
