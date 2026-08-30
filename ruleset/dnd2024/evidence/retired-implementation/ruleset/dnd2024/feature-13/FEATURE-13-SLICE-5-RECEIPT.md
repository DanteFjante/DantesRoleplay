# Feature 13 Slice 5 receipt — condition-derived Initiative

Completed: 2026-08-21

## Outcome

The shared condition state-effects resolver now derives Initiative disadvantage from effective
Incapacitated and Initiative advantage from Invisible. Implied Incapacitated, including from
Stunned, produces the same auditable condition evidence.

The individual Initiative resolver composes that branch, keeps caller, derived, and merged
circumstances separate, reports whether condition state is known, and reserves the `condition:`
source prefix. Its count calculation, seeded roll behavior, and no-effects contract remain
unchanged. The encounter Initiative-order mechanic was not revised.

## Verification

- `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore --filter
  "FullyQualifiedName~CatalogFeature5Tests|FullyQualifiedName~CatalogFeature10Tests|FullyQualifiedName~CatalogFeature13Tests"`:
  10 passed.
- `roleplay validate catalog`: 239 records valid, 4 advisory near-duplicate warnings, and no live
  data touched. Validation reported no errors.
- Focused Feature 13 coverage proves absent versus known-empty state, Stunned's implied
  Incapacitated disadvantage, Invisible advantage, cancellation, reserved provenance rejection,
  and one-roll normal resolution. Feature 5 coverage confirms the arbitrary-roster order and tie
  behavior still pass.

## Next boundary

Slice 6 blocks Action, Bonus Action, and Reaction spending for effective Incapacitated and movement
spending for the Speed-0 conditions, without changing the existing turn-budget owner.
