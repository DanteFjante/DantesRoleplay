# Feature 21 — Slice 1 receipt

Date: 2026-08-21

## Delivered boundary

- Extended the existing `dnd2024.weapon-profile` static contract with required
  `rangeFeet: { normal, long }` for `kind: "ranged"` only.
- Migrated the source-backed Shortbow profile to normal range 80 feet and long range 320 feet.
- Revised the existing profile writer and the weapon-attack and weapon-damage readers to validate
  the new closed profile shape without changing their attack or damage behavior.

## Explicitly not delivered

No position, distance calculation, cover, line of effect, visibility, combat side, close-combat
circumstance, long-range Disadvantage, out-of-range refusal, Action use, equipment requirement, or
new player-facing ranged-attack action. Thrown ranges remain Feature 25 work.

## Evidence

- `dotnet test DantesRoleplay.Tests\\DantesRoleplay.Tests.csproj --filter
  "FullyQualifiedName~CatalogFeature7Tests|FullyQualifiedName~CatalogFeature8Tests|FullyQualifiedName~CatalogFeature9Tests"`
  — passed, 7/7.
- `roleplay validate catalog` — valid disposable import, 266 records, 8 pre-existing
  near-duplicate warnings. No live data was touched.
- Focused profile assertions cover Shortbow 80/320, required/closed ranged range, Melee omission,
  invalid range shapes with unchanged stored bytes, and the direct attack/damage compatibility
  boundary.
