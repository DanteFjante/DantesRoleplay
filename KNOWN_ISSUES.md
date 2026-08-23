# Known issues

Last reviewed: 2026-08-23

Only current reproducible problems belong here. A fixing receipt or version control preserves
resolved history.

## Game rules implemented in C#

Several recent character-creation implementations put D&D IDs, closed choice rules, score limits,
and resolution behavior in production C# instead of catalog mechanics. Confirmed examples include:

- `src/game-adapters/dantes-roleplay/character/persistence/BackgroundAbilityScoreIncreaseResolver.cs`;
- `CharacterAbilityAssignmentValidator.cs`;
- `CharacterOriginLanguageResolver.cs`;
- `CharacterSpeciesSelectionResolver.cs`;
- `CharacterAbilityScoreRecorder.cs`; and
- `CharacterProfileRecorder.cs`.

The remaining Character files named above are now isolated under the same game-adapter quarantine.
Campaign bootstrap/composition code there also hard-codes `dnd2024` and catalog
component/relationship IDs.
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

The modularization work has established the inventory guard and quarantine placement. Closing this
still requires separately confirmed catalog/schema semantics and parity-tested runtime slices; a
directory move alone is not closure.

## Feature 20 movement/Speed acceptance failures

Against the 2026-08-23 modularization/local-AI worktree, the solution builds with zero
warnings/errors and disposable catalog validation accepts 426 records with warnings only. The
full solution test reports local AI 19 passed, plus 805 passed and two failed in the shared suite:

- `CatalogFeature20Tests.Turn_lifecycle_refreshes_remaining_movement_from_each_active_creature_walk_Speed`
- `CatalogFeature20Tests.Missing_or_corrupt_Speed_rejects_refresh_and_normal_movement_without_mutation`

Both assertions expect a successful action result but receive a rejection. They reproduce when run
alone and directly construct the Action runner; they do not exercise local-AI projects, provider
registration, file scanning, or the moved component registration seams.

The Feature 20 owner must reconcile the current turn-lifecycle/Speed catalog composition and test
fixture, then rerun:

```powershell
dotnet build DantesRoleplay.slnx --no-restore
.\roleplay validate catalog
dotnet test DantesRoleplay.slnx --no-restore
```

Close this entry only when all three commands pass against the same worktree.
