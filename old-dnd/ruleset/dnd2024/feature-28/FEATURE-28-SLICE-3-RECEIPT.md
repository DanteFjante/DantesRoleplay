# Feature 28 Slice 3 implementation receipt — universal origin languages

Status: **Implemented and accepted**

Date: 2026-08-21

## Delivered boundary

Slice 3 implements the SRD 5.2.1 universal player-character language rule at *Character Creation
> Step 2: Character Origin > Choose Languages*, PDF page 20. A CH5-staged actor with valid active
C15 participation receives exactly one add-only `dnd2024.language-proficiencies` fragment for
`common` plus two selected non-Common standard languages.

- The closed selected set is Common Sign Language, Draconic, Dwarvish, Elvish, Giant, Gnomish,
  Goblin, Halfling, and Orc. The fragment uses the existing full-vocabulary canonical ordering and
  fixed source reference.
- `ICharacterOriginLanguageResolver` has no direct-write, transaction, randomization, public
  action, species/background selection, grant receipt, or later-language-grant authority.
- It rejects malformed, duplicate, Common, rare, unknown, wrong-case, and extra-field selections;
  absent/inactive scope; and already-present or corrupt language state before it emits an effect.

## Artifacts

- `procedure.mechanic.dnd2024.origin-languages`
- `mechanic.dnd2024.origin-languages.resolve` (draft, internal CH5 composition declaration)
- [Resolver interface](../../../DantesRoleplay/Characters/CharacterOriginLanguage.cs)
- [Resolver implementation](../../../DantesRoleplay.DataAccess/CharacterOriginLanguageResolver.cs)
- [Focused regression tests](../../../DantesRoleplay.Tests/CatalogFeature28Slice3Tests.cs)

## Verification

- `dotnet build DantesRoleplay.Tests/DantesRoleplay.Tests.csproj -c Release --no-restore` — passed.
- `CatalogFeature28Slice3Tests` — 5 passed.
- `CatalogValidationTests` (disposable-catalog validation used because `roleplay` CLI is unavailable) — 2 passed.
- `CatalogFeature28Slice2Tests` — 6 passed.
- `CatalogFeature28Tests` plus `CharacterFeature05Slice0Tests` — 3 passed.

## Deferred

CH3 still owns source selection/provenance and receipts. Species, background, class, feat, and
later language grants remain independent owners. CH5 remains the only root allowed to append and
apply this fragment.
