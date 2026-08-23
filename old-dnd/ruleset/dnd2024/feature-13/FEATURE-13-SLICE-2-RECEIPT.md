# Feature 13 Slice 2 receipt — condition-derived ability checks

Completed: 2026-08-20

## Outcome

Added the effect-free `mechanic.dnd2024.d20-test.state-effects` resolver and its governing
contract. It distinguishes absent condition state from known-empty state, expands the fixed implied
conditions, preserves source identities, and returns one stable report for all future D20 consumers.

`mechanic.dnd2024.check.ability` now composes that resolver. In this slice, Poisoned derives one
Disadvantage circumstance for ability checks. Caller circumstances remain supported but cannot use
the reserved `condition:` source prefix; the result separately reports caller, derived, and merged
circumstances plus `conditionsKnown`.

## Verification

- `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore --filter
  "FullyQualifiedName~CatalogFeature10Tests|FullyQualifiedName~CatalogFeature13Tests"`: 5 passed.
- `roleplay validate catalog`: 233 records valid; 4 advisory near-duplicate warnings; no live data
  touched.
- `git diff --check` on the Feature 13 artifacts reported no whitespace errors (the workspace has
  its existing CRLF advisory warning on the roadmap).

## Next boundary

Slice 3 fills the resolver's already-present saving-throw branch and makes saving throws consume
it, including automatic Str/Dex failures for Paralyzed, Petrified, Stunned, and Unconscious.
