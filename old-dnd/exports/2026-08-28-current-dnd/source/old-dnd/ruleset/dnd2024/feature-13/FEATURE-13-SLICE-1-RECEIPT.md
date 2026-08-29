# Feature 13 Slice 1 receipt — condition-state admission and mutation

Completed: 2026-08-20

## Outcome

Added the creature-owned `dnd2024.conditions` component and its single administrative writer,
`mechanic.dnd2024.conditions.write`. The writer records an empty known state, applies condition
instances, and clears them using a source entity only when that entity is supplied in the optional
role—not from caller input.

The fourteen non-Exhaustion SRD condition ids are closed and canonically ordered. Independent
sources can retain the same condition independently; source-specific clear removes only its own
instance. Petrified atomically removes Poisoned instances, and Poisoned cannot be applied while
Petrified is effective.

## Plan correction

The original plan required re-validating every persisted source ID against the world. The projection
contract intentionally cannot dynamically load entities named inside component data, so that
requirement was revised before implementation: a source must resolve when its instance is written;
the persisted ID is historical provenance and remains clearable after deletion.

## Verification

- `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore --filter
  "FullyQualifiedName~CatalogFeature13Tests"`: 2 passed.
- `roleplay validate catalog`: 231 records valid; 3 advisory near-duplicate warnings; no live data
  touched.

## Next boundary

Slice 2 adds the effect-free shared state-effects resolver and has only ability checks consume it.
Saving throws, attacks, Initiative, and action-economy prohibitions remain outside this slice.
