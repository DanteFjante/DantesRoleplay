# Feature 28 Slice 2 implementation receipt — background ability increases

Status: **Implemented and accepted.**
Date: 2026-08-21

## Delivered boundary

- `dnd2024.background.ability-increase-options` records a source-cited, immutable ability-choice
  declaration on the existing Soldier background definition.
- `IBackgroundAbilityScoreIncreaseResolver` consumes a trusted background ID, an actor's CH2 base
  abilities, and C15 scope. It returns exactly one `dnd2024.abilities` merge fragment or no
  effects with a named correction.
- The resolver accepts both source forms: a distinct `+2/+1` pair or `+1` to all three eligible
  abilities. The ratified Soldier choice is `+2 str, +1 con`.
- It contains no background actor selection, provenance receipt, feat, skill/tool/language/item
  grant, direct write, transaction, event, audit, public action, or final-score input.

## Verification

| Gate | Result |
| --- | --- |
| Slice 2 focused tests | Passed: 6/6. |
| Feature 28 + CH5 focused regression | Passed: 9/9. |
| Catalog validation test | Passed: 2/2. |
| Diff check | No whitespace errors; existing line-ending advisories remain unrelated. |

## Deferred

CH3 still owns the background selection and grant receipt. Feature 28's universal language and
background-feat paths, plus their mechanical effects, remain separately blocked. CH5 remains the
only root that may append and apply the fragment.
