# Feature 25 — Slice 1 receipt

Date: 2026-08-21

## Delivered boundary

- Extended Feature 7's immutable `dnd2024.weapon-profile` owner with canonical property tags,
  distinct normal Ranged and Thrown range fields, structured ammunition and Versatile facts, and
  one static mastery identity.
- Migrated the three canonical SRD profiles: Dagger (Finesse, Light, Thrown 20/60, Nick),
  Shortbow (Ammunition/Arrow, Two-Handed, 80/320, Vex), and Battleaxe (Versatile 1d10, Topple).
- Revised the normal administrative writer to reject unordered or incompatible property data,
  without creating a player-facing property or mastery action.

## Explicitly not delivered

No weapon instance, custody or held state, hand capacity, mastery permission, ammunition spend,
Loading ledger, attack ability choice, Disadvantage, alternate damage roll, target, condition,
temporary effect, movement, action, or damage/HP change. Existing attack and damage behavior is
unchanged.

## Evidence

- `dotnet test DantesRoleplay.Tests\\DantesRoleplay.Tests.csproj --filter
  "FullyQualifiedName~CatalogFeature25Tests|FullyQualifiedName~CatalogFeature7Tests|
  FullyQualifiedName~CatalogFeature8Tests|FullyQualifiedName~CatalogFeature9Tests" --no-restore`
  — passed, 9/9.
- `roleplay validate catalog` — valid disposable import, 304 records, 34 general near-duplicate
  warnings. No live data was touched.
- Tests prove the three complete static profile facts, closed-field rejection, writer canonical
  ordering, and compatibility with existing Feature 7, 8, and 9 behavior.

## Next boundary

Slice 2 remains blocked until the source-backed mastery grant/selection semantics are confirmed.
It must create a closed learned-mastery state and reader only; it may not infer mastery from a
weapon profile or proficiency.
