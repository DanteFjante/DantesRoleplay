# Feature 13 Slice 4 receipt — condition-derived weapon attacks

Completed: 2026-08-21

## Outcome

Weapon attacks now compose the shared condition state-effects resolver once for the attacker and
once for the defender. Attacker conditions contribute `attackRoll` evidence; defender conditions
contribute `attackAgainst` evidence. Caller-provided circumstances remain separate, while the
merged list controls the established advantage/disadvantage cancellation rule.

The attack result reports the two derived lists, their merged list, and whether condition state was
known for each participant. Caller evidence cannot forge the reserved `condition:` provenance.
Feature 8's Armor Class comparison, proficiency arithmetic, natural-20/1 classification, seeded
roll behavior, and zero-effects contract are unchanged.

## Verification

- `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore --filter
  "FullyQualifiedName~CatalogFeature8Tests|FullyQualifiedName~CatalogFeature10Tests|FullyQualifiedName~CatalogFeature13Tests"`:
  9 passed.
- `roleplay validate catalog`: 238 records valid, 8 advisory near-duplicate warnings, and no live
  data touched. The warnings include the resolver and weapon-attack natural-language matching;
  validation reported no errors.
- Focused Feature 13 coverage proves absent condition state, attacker/defender cancellation,
  same-kind non-stacking, reserved provenance rejection, and unchanged one-roll attack behavior.

## Next boundary

Slice 5 composes the same resolver into Initiative rolls, leaving Feature 5 ordering and tie policy
unchanged.
