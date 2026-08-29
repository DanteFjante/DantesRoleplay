# Feature 26 Slice 2 implementation receipt — selected-species reference

Status: **Implemented and accepted**

Date: 2026-08-21

## Delivered boundary

Slice 2 adds the single durable actor-side reference to an immutable active D&D 2024 species
definition. `ICharacterSpeciesSelectionResolver` validates a CH1 `species` identity and matching
`dnd2024.species-profile` in the C15-scoped CH5 staged-world overlay, then returns exactly one
add-only `dnd2024.selected-species` fragment.

- The component contains only `speciesDefinitionId`; Human’s v1 definition and all eight other
  active source profiles validate without adding a source copy, Size, Speed, creature type, trait,
  language, skill, feat, choice, or effect.
- The resolver rejects missing/inactive scope, malformed actor/definition IDs, missing or corrupt
  immutable content, existing/corrupt selection state, and every direct-write substitute before it
  returns an effect.
- CH5 remains the only root that may append and apply the fragment. This is not a public character
  creation flow and creates no provenance receipt.

## Artifacts

- `dnd2024.selected-species`
- `procedure.mechanic.dnd2024.species-selection`
- `mechanic.dnd2024.species-selection.resolve` (draft, internal CH5 composition declaration)
- [Resolver interface](../../../DantesRoleplay/Characters/CharacterSpeciesSelection.cs)
- [Resolver implementation](../../../DantesRoleplay.DataAccess/CharacterSpeciesSelectionResolver.cs)
- [Focused regression tests](../../../DantesRoleplay.Tests/CatalogFeature26Slice2Tests.cs)

## Verification

- `CatalogFeature26Slice2Tests` — 5 passed.
- `CatalogValidationTests` (disposable-catalog validation used because `roleplay` CLI is unavailable) — 2 passed.
- `CatalogFeature26Tests` plus `CharacterFeature05Slice0Tests` — 5 passed.

## Deferred

Species-specific choices, Human’s Size/Skillful/Versatile decisions, all trait consequences, and
the public character-origin creation/receipt root remain separate Feature 26, Feature 28, CH3,
and Feature 30 work.
