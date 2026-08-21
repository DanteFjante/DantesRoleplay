# Feature 13 Slice 6 receipt — condition-derived turn-budget prohibitions

Completed: 2026-08-21

## Outcome

The shared condition state-effects resolver now emits a resource-unique prohibition list. Effective
Incapacitated blocks Action, Bonus Action, and Reaction; Grappled, Paralyzed, Petrified,
Restrained, Stunned, and Unconscious block movement. Stunned also blocks the first three through
its implied Incapacitated state.

`mechanic.dnd2024.turn-budget.spend` composes that list after validating the requested resource and
encounter state. A condition prohibition takes precedence over ordinary budget exhaustion and
proposes no effect. Free interaction remains available, and clearing the condition restores the
ordinary spend path within the same turn.

## Verification

- `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore --filter
  "FullyQualifiedName~CatalogFeature10Tests|FullyQualifiedName~CatalogFeature12Tests|FullyQualifiedName~CatalogFeature13Tests"`:
  14 passed.
- `roleplay validate catalog`: 239 records valid, 0 warnings, and no live data touched.
- After transient concurrent-catalog failures, an isolated serial full-suite run against the stable
  catalog passed: 521 passed, 0 failed, 0 skipped. It rebuilt embedded catalog content and disabled
  test-collection parallelism only to avoid snapshot races; application and catalog behavior were
  unchanged.

## Acceptance boundary

Feature 13 is verified. Feature 14 may now begin.
