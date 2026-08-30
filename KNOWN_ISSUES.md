# Known issues

Last reviewed: 2026-08-26

Only current reproducible problems belong here. A fixing receipt or version control preserves
resolved history.

## D&D condition state-effects test uses the character-sheet schema

`Dnd2024AbilityCheckTests.Condition_state_effects_distinguish_unknown_and_derive_stable_shared_branches`
fails reproducibly by validating the `dnd2024.mechanic.d20-test.state-effects` result against
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

## D&D 2024 gameplay mechanics were accidentally deleted from the working tree (recovered 2026-08-29)

**Root cause (corrected from an earlier, wrong diagnosis in this file):** this was not a design gap.
Uncommitted work after commit `6b5699c4` ("fixes") deleted 62 mechanic file pairs (124 files, `.js`+`.md`)
and all 57 `procedures/*.md` design-contract docs from `catalog/applications/dnd2024/mechanics/` and
`catalog/applications/dnd2024/procedures/` in the working tree, without committing those deletions and
without replacing them. A small subset — `dice`, `speed.read`, `speed.write`, `healing.apply`,
`temporary-hit-points.write`, `heroic-inspiration.grant`, `character-experience.write` — had been moved
to new category folders and improved beyond HEAD in the same uncommitted work. Everything else was
simply gone from disk but still present in git history at `6b5699c4`.

Separately, and already in place before this recovery, the same uncommitted working tree had grown a
new flat `catalog/applications/dnd2024/components/` directory (166 component+schema pairs, richer
schemas than the old nested `components/<category>/` layout it replaces), a restructured
`content/entities/` tree (10 topic directories, ~2,300 files, replacing 103 old files), and a new
`catalog/applications/dnd2024/schemas/` directory (111 files, including archetype definitions). None
of that was touched here — it is pre-existing, already-authored "current structure" content.

**What was recovered and verified today:**
- All 62 deleted mechanic pairs and all 57 procedure docs restored via `git checkout HEAD --`/
  `git show HEAD:` (a few files needed the `git show` form due to a sandbox permission quirk on
  `unlink`).
- Two duplicate mechanic IDs, produced by the restore coexisting with the already-improved versions,
  resolved by removing the stale old-path copies: `dnd2024.mechanic.character-experience.write` (kept
  `advancement/`, removed the restored `proficiency/` copy) and `dnd2024.mechanic.heroic-inspiration.grant`
  (kept `combat/`, removed the restored `data/` copy — the kept version has extra `character.profile`
  validation the HEAD version lacks).
- `dnd2024.mechanic.check.ability` — I had mistakenly overwritten the pre-existing (already
  improved-beyond-HEAD) file with a hand-authored version that explicitly blocked named-skill checks.
  That was a real mistake: I hadn't checked `git diff`/`git show HEAD` first. Restored from HEAD, which
  turns out to already fully support named-skill checks (see next point) — the immediately-prior
  working-tree version (possibly even better than HEAD, per the pattern above) is not recoverable.
- **The "class-membership relationship read" blocker described in the earlier version of this entry was
  wrong.** `dnd2024.mechanic.character-level.read.js` (present at HEAD, now restored) already derives
  total level and Proficiency Bonus from `dnd2024.character.class-membership` relationships, using a
  `relationshipComponents` declaration in its Requirements JSON that composes a related entity's
  specific components — a capability `procedure.mechanic.projection.md` documents but that I had missed.
  `check.ability`'s named-skill path and `dnd2024.mechanic.saving-throw` both already use this pattern
  at HEAD. No kernel change or design decision is needed for this part.
- Verification: all 66 mechanic `.js` files parse cleanly (`new Function('ctx', source)`); 66 unique
  mechanic IDs, 0 duplicates; every `children.mechanicId` reference resolves to a real mechanic.

**Still genuinely blocked — real redesign work, not a mechanical port:** cross-checking every
mechanic's declared `components` against the new flat `components/` directory shows 32 of 66 mechanics
are fully consistent with the current structure, and 34 reference component IDs that don't exist under
it, because the new architecture decomposed several old monolithic components into multiple granular
ones (e.g. old `dnd2024.weapon-profile` → new `dnd2024.item.weapon` + `item.physical` + `item.price` +
`item.quantity` + `item.equippable`; old `dnd2024.item-definition` → nine separate `dnd2024.item.*`
facet components; old `dnd2024.conditions` → likely `dnd2024.effect.active-effect-state` plus related
`effect.*` components; old `dnd2024.rest-episode`/`rest-policy` → the new, much simpler
`dnd2024.exploration.rest`; old `dnd2024.species-profile` → `dnd2024.advancement.species`). Affected
mechanic groups: the full item/inventory family (`item-instance.*`, `item-stack.*`, `item.equip`/
`unequip`/`transfer`/`equipment.read`, `inventory.read`, `item-burden.read`, `item-activity.use`,
`currency-value.read`, `carrying-capacity.read`), the weapon family (`weapon-attack`,
`weapon-damage.roll`/`apply`, `weapon-profile.write`), conditions/effects (`conditions.write`,
`d20-test.state-effects`, `turn-budget.spend`), rest (`rest.begin`/`interrupt`/`progress`), character
creation and species/background (`character.basic.create`, `character-abilities.resolve`,
`character-content-definition.record`, `class-progression.read`, `species-selection.resolve`,
`species-skillful.resolve`, `species-versatile-skilled.resolve`), and `initiative.roll` (references
`dnd2024.character-feature-grants` and `dnd2024.rest-episode`, both old-shaped).

Mapping each of these to the new decomposed components is a real design task — picking the right
subset of new components per mechanic and rewriting the read/write logic — not a lookup, and risks
inventing incorrect shapes if rushed (see the `check.ability` mistake above, at 1/34th this scale).
This should be scoped explicitly with the ruleset owner before proceeding, ideally a few mechanics at
a time with schema validation against the real target component schemas at each step.

All of the above (the 62+57 restored files, the duplicate cleanup, and `check.ability`) is currently
**uncommitted** in git, same as the pre-existing components/content/schemas restructuring. Nothing has
been committed as part of this recovery.
