# Known issues

Last reviewed: 2026-08-21

Only current reproducible problems belong here. A fixing receipt or version control preserves
resolved history.

## Game rules implemented in C#

Several recent character-creation implementations put D&D IDs, closed choice rules, score limits,
and resolution behavior in production C# instead of catalog mechanics. Confirmed examples include:

- `DantesRoleplay.DataAccess/BackgroundAbilityScoreIncreaseResolver.cs`;
- `CharacterAbilityAssignmentValidator.cs`;
- `CharacterOriginLanguageResolver.cs`;
- `CharacterSpeciesSelectionResolver.cs`;
- `CharacterAbilityScoreRecorder.cs`; and
- `CharacterProfileRecorder.cs`.

Campaign bootstrap/composition code also hard-codes `dnd2024` and catalog component/relationship IDs.
Some generic C# orchestration may remain, but ruleset-specific validation, formulas, choice patterns,
IDs, and effect derivation violate [the architecture boundary](ARCHITECTURE.md).

Close this with a separately approved remediation feature:

1. inventory every production C# branch/constant that encodes ruleset or catalog meaning;
2. assign each rule to an existing or reviewed catalog procedure/mechanic/schema owner;
3. keep only generic declared-context, transaction, effect, audit, and composition plumbing in C#;
4. migrate one coherent slice at a time without changing accepted behavior;
5. add a guard test that rejects new ruleset IDs/vocabulary in production C# outside an explicit,
   reviewed allow-list; and
6. preserve focused, rollback, replay, catalog, and full-suite evidence in receipts.

This documentation cleanup does not authorize that runtime refactor.

## Feature 10 transcript does not admit encounter-side fixture state

Against the 2026-08-21 worktree, the solution builds with zero warnings/errors and disposable
catalog validation accepts 399 records with warnings only. The full suite reports 788 passed and one
failed:

`CatalogFeature10Tests.Imported_catalog_replays_the_feature_10_vertical_session_in_two_fresh_databases`

`AssertExpectedDeltas` expects no extra participant component, while the imported fixture now
contains `dnd2024.encounter-sides`. This is a fixture/expectation ownership mismatch in concurrent
tactical-side work, not a documentation failure.

The tactical/Feature 10 owner must decide whether encounter sides are part of the accepted transcript
baseline, then update the fixture or expectation through that owner and rerun:

```powershell
dotnet build
.\roleplay validate catalog
dotnet test --no-build
```

Close this entry only when all three commands pass against the same worktree. Do not pin the count in
roadmap or architecture files.
