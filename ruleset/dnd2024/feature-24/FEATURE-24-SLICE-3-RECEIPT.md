# Feature 24 Slice 3 receipt — direct equipped armor aggregation

Status: **accepted**
Owner: Feature 24, Armor, shields, armor training, and derived Armor Class
Source: `source.dnd2024.srd-5.2.1`, `Equipment > Armor`, PDF p. 91

## Delivered boundary

- Added `procedure.mechanic.dnd2024.armor-equipment` and
  `mechanic.dnd2024.armor-equipment.read`.
- The effect-free reader resolves at most one direct worn armor suit and one direct held Shield
  from immutable definitions, item instances, direct containment, and explicit equipment state.
- Explicitly unequipped items return no selection; nested/stowed items never qualify; duplicates,
  missing/malformed relevant state, wrong modes, and stacks fail closed.
- Container mass remains Feature 23's separate recursive burden calculation: each container's own
  immutable mass plus the mass of its contents.

## Evidence

- `dotnet test --filter FullyQualifiedName~CatalogFeature24Slice3Tests` — passed (2 tests).
- `dotnet run --project DantesRoleplay.Tools -- validate catalog` — valid: 412 records and 77
  non-blocking near-duplicate warnings; no live data touched.
- `dotnet test --no-restore` — passed: 793 tests, 0 failures.
- `git diff --check` — passed.

## Deliberate exclusions

No equipment mutation, training grant/interpretation, Armor Class calculation or migration, D20
effect, Speed adjustment, spellcasting restriction, action, timing, burden/capacity change,
component/schema/migration, public-surface change, generic C# rule, or persistent catalog import
was added.
