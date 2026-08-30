# Feature 13 Slice 3 receipt — condition-derived saving throws

Completed: 2026-08-21

## Outcome

The shared condition state-effects resolver now supplies saving-throw branches. Restrained derives
Disadvantage for Dexterity saves. Paralyzed, Petrified, Stunned, and Unconscious automatically fail
Strength and Dexterity saves, reporting the canonical first condition reason without rolling a die.

`mechanic.dnd2024.saving-throw` composes that branch, preserves caller, derived, and merged
circumstances separately, reserves `condition:` provenance, and reports automatic and voluntary
failure independently. The other four saving abilities retain ordinary save behavior.

## Verification

- `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore --filter
  "FullyQualifiedName~CatalogFeature10Tests|FullyQualifiedName~CatalogFeature13Tests"`: 6 passed.
- `roleplay validate catalog`: 234 records valid, 0 warnings, and no live data touched.
- `git diff --check` on changed Feature 13 files had no whitespace errors; the workspace reports
  its existing CRLF advisory for the roadmap.

## Next boundary

Slice 4 composes the same resolver twice into weapon attacks—once for attacker conditions and once
for defender conditions—without changing Feature 8 hit or critical arithmetic.
