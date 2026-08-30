# Character starting-equipment Slice 2 receipt — Dungeoneer's Pack definitions

Accepted: 2026-08-21

## Scope delivered

Added source-cited immutable item definitions for the remaining Dungeoneer's Pack contents:

- separate items: Caltrops (bag), Crowbar, Tinderbox, and Waterskin;
- fungible stacks: Oil (flask), one day's Rations, and Torch.

Each uses `dnd2024.item-definition`, version `1`, kind `adventuring-gear`, an exact mass, and the
fixed SRD source reference `Equipment > Adventuring Gear`. The existing Backpack and 50-foot
Hempen Rope complete the source list of pack content definitions.

## Intentionally not delivered

- No Dungeoneer's Pack item entity, campaign item instance, inventory placement, or character
  grant. CH5 owns the single atomic starting-equipment transaction.
- No water quantity, food consumption, light, fire, Oil, Caltrops, or Crowbar effect. Those need
  their separately owned gameplay rules.
- No new schema, mechanic, procedure, public MCP surface, or C# game-specific rule.

## Verification

- `CatalogCharacterStartingEquipmentTests` fresh-imports a disposable catalog and asserts every
  definition's immutable identity, kind, stack policy, mass, source reference, and deliberate
  absence of use/capacity/equipment behavior.
- Focused starting-equipment and Feature 23 tests passed: 4/4.
- `dotnet run --project DantesRoleplay.Tools -- validate catalog` passed with 406 valid records and
  73 advisory near-duplicate warnings; it touched no live data.
- Full suite: 789 passed, 1 failed. The sole failure remains the tracked Feature 10 transcript
  expectation that does not admit the imported `dnd2024.encounter-sides` component; it exercises
  no starting-equipment artifact.
