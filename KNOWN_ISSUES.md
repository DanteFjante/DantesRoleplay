# Known issues

Last reviewed: 2026-08-26

Only current reproducible problems belong here. A fixing receipt or version control preserves
resolved history.

## D&D condition state-effects test uses the character-sheet schema

`Dnd2024AbilityCheckTests.Condition_state_effects_distinguish_unknown_and_derive_stable_shared_branches`
fails reproducibly by validating the `mechanic.dnd2024.d20-test.state-effects` result against
`character-sheet.result.schema.json`. The mechanic correctly returns a condition-derived report
whose `test` is `d20-test-state-effects`; the selected schema instead requires a complete
`character-sheet-core` document and rejects additional properties. This is a test/schema-owner
defect outside system-task orchestration, not an orchestration or local-AI failure.

The 2026-08-26 full shared run reported 1,084 passed and two failed. The second failure was the
repository-catalog immutability assertion while tests ran concurrently; that assertion passes when
re-run alone. The condition test remains the sole reproducible failure.

Close this entry only after the D&D adoption owner selects or authors the confirmed state-effects
result contract, updates the test without weakening the mechanic contract, and the focused test and
full shared suite both pass.

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
