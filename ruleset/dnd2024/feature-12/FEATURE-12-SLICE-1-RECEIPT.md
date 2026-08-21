# Feature 12 Slice 1 receipt — turn-budget admission

Completed: 2026-08-20

## Outcome

Added the participant-owned `dnd2024.turn-budget` component, its fixed-source closed schema, and
the administrative `mechanic.dnd2024.turn-budget.write` record/correct path. The Feature 10 hero
and training-target fixtures now carry full 30-foot budgets. No turn restoration or resource
spending behaviour was added in this slice.

## Verification

- `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore --filter
  FullyQualifiedName~CatalogFeature12Tests`: 2 passed.
- `roleplay validate catalog`: 218 records valid, no warnings, and no live data touched.

## Next boundary

Slice 2 adds the effect-free fan-out reader and revises Feature 11 start/advance transitions to
restore the newly active participant's budget. It is a separate review boundary because it changes
the existing lifecycle actions' projections and effect counts.
