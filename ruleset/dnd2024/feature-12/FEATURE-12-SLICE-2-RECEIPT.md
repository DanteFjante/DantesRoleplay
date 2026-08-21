# Feature 12 Slice 2 receipt — start-of-turn restoration

Completed: 2026-08-20

## Outcome

Added the effect-free `mechanic.dnd2024.turn-budget.read` fan-out reader. Feature 11's start and
advance transitions now read every roster budget, restore the newly active participant to its full
recorded allowance, and apply that restoration as a second atomic effect alongside the lifecycle
state transition. Start rejects an encounter with any absent or invalid budget; advance rejects an
invalid newly active participant while leaving other non-active diagnostics non-blocking.

## Verification

- `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore --filter
  "FullyQualifiedName~CatalogFeature11Tests|FullyQualifiedName~CatalogFeature12Tests"`: 9 passed.
- `roleplay validate catalog`: 222 records valid; it reported 7 advisory near-duplicate warnings
  from the shared catalog and did not touch live data.

## Next boundary

Slice 3 adds the normal spend transition. It will be the first path that consumes a budget and must
enforce the acting-participant rule while preserving the off-turn Reaction exception.
