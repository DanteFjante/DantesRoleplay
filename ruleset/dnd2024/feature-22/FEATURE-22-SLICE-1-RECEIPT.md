# Feature 22 — Slice 1 receipt

Date: 2026-08-21

## Delivered boundary

- Added `procedure.mechanic.dnd2024.unarmed-strike.damage` and the effect-free
  `mechanic.dnd2024.unarmed-strike.damage` resolver.
- Derived a Strength-based D20 attack, total-level Proficiency Bonus, Feature-13 condition
  circumstances, natural 20/1 classification, and fixed `max(0, 1 + Strength modifier)`
  Bludgeoning damage evidence.
- Preserved the established seeded D20, circumstance cancellation, and condition-child contracts.

## Explicitly not delivered

No reach or position validation, player-facing Action, Action-budget spend, Hit Point effect,
damage mitigation, Grapple, Shove, forced movement, item/equipment rule, improvised-weapon policy,
attack ledger, or two-weapon action. An Unarmed Strike is not represented by a weapon profile.

## Evidence

- `dotnet test DantesRoleplay.Tests\\DantesRoleplay.Tests.csproj --filter
  "FullyQualifiedName~CatalogFeature22Tests|FullyQualifiedName~CatalogFeature8Tests|FullyQualifiedName~CatalogFeature13Tests" --no-restore`
  — passed, 10/10.
- `roleplay validate catalog` — valid disposable import, 273 records, 23 near-duplicate warnings.
  No live data was touched.
- Focused Feature 22 coverage proves Proficiency Bonus bands, Strength fixed-damage floor,
  Armor-Class comparison, Advantage/Disadvantage/cancellation, natural 20/1, fixed-damage
  criticals, condition-derived disadvantage, closed input, replay, corrupt state rejection, and
  zero effects with unchanged actor bytes.
