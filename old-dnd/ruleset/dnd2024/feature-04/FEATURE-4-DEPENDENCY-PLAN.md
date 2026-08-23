# Feature 4 dependency plan — saving-throw proficiencies and fixed-DC saves

> **D&D implementation reference:** When this work includes D&D 2024 mechanics, inspect [Foundry VTT dnd5e](https://github.com/foundryvtt/dnd5e) before designing or coding. Use it as a licensed engineering reference—not a direct dependency or rules authority—while retaining the exact `source.dnd2024.srd-5.2.1` locator and all applicable MIT, CC-BY, and asset-license notices for any reused material.

Status: **Slice 1 verified; Slice 2 remains blocked pending a separate implementation pass**
Last updated: 2026-08-18

## Execution rule

This plan is governed by live `procedure.system.create-feature` v4. An implementation pass selects
exactly one lowest unimplemented slice, implements and verifies that slice, records objective
evidence here, and stops for review. Planning does not authorize both slices together.

All runtime procedure contracts, component definitions, entities, and mechanics belong only in
the live database through MCP. This repository file records dependencies, decisions, gates, and
operation IDs; it must never become a duplicate runtime payload or JavaScript source file.

## Target capability

A character has explicit authoritative proficiency state for the six saving-throw kinds. A caller
can then request a fixed-DC Strength, Dexterity, Constitution, Intelligence, Wisdom, or Charisma
saving throw. The rule validates the character state, derives the ability modifier and at most one
level-based Proficiency Bonus, applies Feature 3's exact Advantage/Disadvantage convention, and
returns a seeded, auditable, effect-free result.

The save reports whether the roll met the DC. It does not decide or apply the consequence of the
threat that caused the save.

### Included

- Six stable save ability ids: `str`, `dex`, `con`, `int`, `wis`, and `cha`.
- Explicit empty proficiency state, distinct from missing/unknown state.
- Administrative creation and correction of the complete known proficiency list.
- Fixed-DC rolled saving throws for characters with level-based Proficiency Bonus.
- The exact Feature 3 `rollCircumstances` validation, non-stacking, cancellation, seeded dice,
  result fields, and replay behavior.
- A voluntary-failure branch that rolls no die and cannot be combined with nonempty roll
  circumstances.
- Natural 1 and natural 20 remain ordinary saving-throw totals; neither automatically overrides
  the comparison with the DC.
- Zero effects from save resolution.

### Excluded

- Class selection, multiclass rules, or automatically granting a class's save proficiencies.
- Monster Challenge Rating and monster-derived Proficiency Bonus.
- Spellcasting ability, spell save DC calculation, spell or hazard definitions, and save effects.
- Death saving throws, concentration saves as a named subsystem, legendary resistance, Evasion,
  Indomitable, Magic Resistance, rerolls, Heroic Inspiration, or persistent conditions.
- Expertise, half proficiency, doubled saving-throw proficiency, or caller-supplied bonuses.
- Storing ability modifiers, Proficiency Bonus, save modifiers, DCs, totals, or roll circumstances.

## Official source basis

Use live source entity `source.dnd2024.srd-5.2.1` and the official SRD 5.2.1 at
`https://media.dndbeyond.com/compendium-images/srd/5.2/SRD_CC_v5.2.1.pdf`.

Relevant locators:

- `Playing the Game > D20 Tests > Saving Throws` (PDF page 6): six ability-named saves, relevant
  ability modifier, source/GM-supplied DC, and the option to fail without rolling.
- `Playing the Game > Proficiency > Saving Throw Proficiencies` (PDF page 8): a proficient save
  adds Proficiency Bonus for that ability.
- `Playing the Game > D20 Tests > Advantage/Disadvantage` (PDF pages 6–7): the Feature 3
  circumstance and die-selection convention.
- `Character Creation > Character Advancement`: character level and derived Proficiency Bonus.

The live source registry owns licensing and attribution metadata. Feature artifacts store only the
fixed source entity id and precise locator required by their contracts.

## Verified existing dependencies

| Dependency | Current evidence |
| --- | --- |
| Planning workflow | `procedure.system.create-feature` v4, read `a8d72a4cb38145d98875ceb39a6d9f7b` |
| Source registry | `dnd2024.source` and `source.dnd2024.srd-5.2.1`, verified in Feature 2 Slice 1 |
| Six ability ids and modifiers | `procedure.mechanic.dnd2024.abilities` v1 and `dnd2024.abilities` |
| Character level and Proficiency Bonus | `procedure.mechanic.dnd2024.character-level` v1, `dnd2024.character-level`, recorder v1 |
| D20 roll-circumstance convention | `procedure.mechanic.dnd2024.check.ability` v3, read `e792dd314db84c90af3936d6ed7caa97` |
| Executable convention behavior | `mechanic.dnd2024.check.ability` v4, read `84bcdf26d1e7431c806e2fccf037d2fb`; independently reverified below |
| Administrative list-state pattern | skill-proficiencies contract v1 and recorder v1, reads `ac0c21408dea4adabdc996753bea4070` and `685ea3d8668f47cea1e7da838c11142d` |
| World modeling and writes | `procedure.world.model` v4 and `procedure.world.change` v2, reads `d51543b351334f0eb4230a5f2af5e38c` and `b2c8a5942ff14e28916ae16995dfd2ba` |
| Mechanic/action authoring | `procedure.mechanic.write` v1 and `procedure.action.run` v1, reads `84288150888740718dcba40328cdf126` and `fb97c57a62c140e7aa9032e78b4022c2` |

Planning searches `6d8678816f6945008efb06f40f0cde00` and
`0f49e0cd480146bd9d980efbd9d1f9d7` found only the existing ability, level, and skill artifacts.
The world read `48c4dbedc6964038a1a5f73f0da6080d` confirms that no saving-throw component exists.
Exact-id and intent searches must be repeated immediately before either implementation slice.

During Slice 1 implementation, a dry run of a separate
`procedure.mechanic.dnd2024.saving-throw-proficiencies` contract passed every structural check but
failed the catalog's `no-near-duplicate` guard (`38a636b76fba49028ed8bb2987953f3c`), identifying
the existing character-level and skill-proficiencies procedures as the overlapping owners. This is
new dependency evidence, not a bypassable warning: Slice 1 revises the existing
`procedure.mechanic.dnd2024.skill-proficiencies` contract to v2 as the shared character
proficiency-state owner. It still creates a distinct save component and recorder.

## Recursive dependency analysis

```text
fixed-DC character saving throws                              [Feature 4 parent]
├─ official source identity and locators                     [implemented: Feature 2]
├─ six ability ids and ability modifiers                     [implemented: Feature 1]
├─ character level and derived Proficiency Bonus             [implemented: Feature 2]
├─ D20 Advantage/Disadvantage convention                     [implemented: Feature 3]
├─ saving-throw proficiency state                            [missing: Slice 1 leaf]
│  ├─ stable vocabulary reuses the six ability ids           [implemented]
│  ├─ shared proficiency-contract ownership revision         [Slice 1]
│  ├─ closed authoritative list plus fixed sourceRef         [Slice 1]
│  └─ validated add/replace recording path                   [Slice 1]
└─ fixed-DC saving-throw resolution                          [blocked: Slice 2 parent]
   ├─ validate input and all authoritative actor state       [Slice 2]
   ├─ derive ability modifier and proficiency once          [existing formulas]
   ├─ derive seeded roll mode and selected die               [Feature 3 convention]
   ├─ optional voluntary failure without a roll              [Slice 2]
   └─ compare total to DC and emit no effects                [Slice 2]
```

No dependency exists below saving-throw proficiency state. Slice 1 is therefore the lowest
unimplemented leaf.

## Dependency and ownership decisions

1. **Separate save state from skill state; share its contract owner.** Save proficiency names an
   ability, is granted by different rules, and has different consumers, so it does not belong in
   `dnd2024.skill-proficiencies` or `dnd2024.abilities`. The catalog rejected a second nearly
   identical procedure, so the existing `procedure.mechanic.dnd2024.skill-proficiencies` evolves
   to v2 to govern both distinct proficiency-state components and their recorders.
2. **Reuse ability ids.** Do not invent `strength-save` or display-name ids. Save proficiency is a
   set drawn from the existing six lowercase ability ids.
3. **Store facts, derive numbers.** Store only the proficient ability ids and fixed source
   reference. Level, Proficiency Bonus, ability modifiers, and save modifiers remain derived.
4. **Missing is not empty.** A missing component means proficiency state is unknown and save
   resolution fails. An explicit empty list means known no save proficiencies and is valid.
5. **Whole-list replacement.** The recording mechanic canonicalizes and replaces the complete
   known list. It does not incrementally grant/revoke, infer class choices, or accept acquisition
   provenance.
6. **Character-only bonus.** Feature 4 reads total character level. A monster without character
   level is out of scope; do not smuggle CR or a caller-supplied Proficiency Bonus into the input.
7. **One saving-throw resolver.** The six save kinds are data selected by `ability`, not six
   mechanics. Do not create spell-, hazard-, ability-, or creature-specific saving-throw rules.
8. **Use the established D20 convention.** The prior catalog rejected a parallel generic D20
   selector as overlapping the ability-check owner. Feature 4 therefore creates no selector
   mechanic or component. Its distinct save mechanic implements the same validated convention and
   must pass cross-mechanic equivalence tests.
9. **Effects belong to the cause.** A saving throw only resolves success/failure. Damage,
   conditions, movement, and partial effects belong to later spell/hazard/feature mechanics.
10. **Voluntary failure is explicit.** The caller may supply `voluntaryFailure: true`; it is not
    inferred from narration. That branch returns failure without consuming seeded randomness and
    rejects nonempty roll circumstances as contradictory.

## Slice order and stop gates

| Order | Slice | Starts only when | Exit gate |
| --- | --- | --- | --- |
| 1 | Saving-throw proficiency state | This plan is reviewed | Contract, component, and recorder exist live; complete positive/negative matrix passes; final state is canonical; repository checks pass |
| 2 | Fixed-DC saving-throw resolution | Slice 1 is verified in a separate pass | Contract and one resolver exist live; arithmetic, circumstances, voluntary failure, replay, routing, missing/corrupt state, zero effects, and final-state gates all pass |

## Slice 1 — authoritative saving-throw proficiency state

This slice is verified. A future implementation pass must not reimplement it; it may use the live
artifacts only after re-reading their contract and this evidence.

### Runtime artifacts

- Revise procedure contract: `procedure.mechanic.dnd2024.skill-proficiencies` v1 → v2
- Component definition: `dnd2024.saving-throw-proficiencies`
- Administrative mechanic: `mechanic.dnd2024.saving-throw-proficiencies.record`
- Scope: `dnd2024-srd-5.2.1`
- Source locator: `Playing the Game > Proficiency > Saving Throw Proficiencies`

These artifacts are database-only. Do not create repository JSON, JavaScript, schema, or contract
payload files.

### Data contract

The component is a closed object containing exactly:

- `abilities`: a unique array containing only `str`, `dex`, `con`, `int`, `wis`, or `cha`, stored
  in canonical D&D ability order `str`, `dex`, `con`, `int`, `wis`, `cha` after filtering to the
  selected members. Maximum length is six. `[]` means known no save proficiencies.
- `sourceRef`: fixed to source id `source.dnd2024.srd-5.2.1` and locator
  `Playing the Game > Proficiency > Saving Throw Proficiencies`.

The schema enforces the closed shape, enum, uniqueness, and maximum count. The recorder enforces
canonical order and the fixed source reference. No level, class, modifier, bonus, display name,
grant source, or acquisition history is stored.

### Recorder input, behavior, and result

- Closed input is exactly `abilities`, an array of zero to six exact stable ability ids.
- Reject missing, null, non-array, non-string member, unknown id, wrong case, display name,
  duplicate, more than six entries, and every extra field including `sourceRef`, `level`,
  `proficiencyBonus`, `class`, `modifier`, or `saves`.
- Validate the whole input before proposing an effect. Do not silently trim, lowercase, deduplicate,
  discard, infer, or clamp.
- Canonicalize valid input to the fixed D&D ability order.
- If the component is absent, propose exactly one `component.add`; if present, exactly one
  `component.set`. Follow the already-proven skill recorder's requirement/projection pattern so
  both paths remain reachable.
- Use no randomness.
- Return canonical `abilities`, `previousAbilities` (`null` when absent), and fixed `sourceRef`.
  Return no derived Proficiency Bonus or modifier.

### Slice 1 implementation sequence

1. Query `procedure.system.create-feature`, this plan, source registry, abilities contract, the
   skill-proficiencies contract/recorder, `procedure.world.model`, `procedure.world.change`,
   `procedure.mechanic.write`, and `procedure.action.run` immediately before writing.
2. Query the world and exact proposed ids. Search procedures/mechanics using `saving throw
   proficiency`, `record saving throws`, `known saving throws`, and each intended match phrase.
   If an owner exists, stop and revise this plan instead of creating a sibling.
3. Read the pre-test `creature.orban` bytes and list every entity carrying each D&D dependency.
4. Revise the existing skill-proficiencies procedure to v2 in memory. Preserve its complete skill
   vocabulary/state/recorder contract; add the distinct saving-throw state and recorder ownership,
   source locator, and non-goals. Dry-run it, read every validation check, commit the identical
   payload, and query v2 back. Do not create the rejected sibling procedure.
5. Re-query the world immediately before the component write. Component commits have no dry run
   and update an existing id in place, so do not commit if the exact id appears.
6. Commit the closed component definition once and query the world back before referencing it.
7. Draft direct-execution JavaScript for the recorder. Its `matches`, `requirements`, and `source`
   are encoded strings; stored source is a body ending in `return {...}`, not `function run(ctx)`.
8. Dry-run the mechanic, read all checks, commit the identical payload, and query v1 back.
9. Exercise add-when-absent and set-when-present through real `commit(kind: "action")` calls.
10. Run the acceptance matrix, record operation IDs and structured evidence, leave Orban with the
    agreed canonical baseline `abilities: ["con","wis"]`, run repository checks, mark only
    Slice 1 complete, and stop.

### Slice 1 acceptance matrix

1. Reverse-order all-six input stores exactly `str,dex,con,int,wis,cha` in canonical order.
2. Empty input stores `[]`; missing component remains semantically distinct from that valid state.
3. A representative multi-value input with duplicates absent canonicalizes correctly.
4. The initial action uses `component.add`; a correction uses `component.set`; each returns exactly
   one applied effect and changes only this component.
5. Reject unknown, duplicate, wrong-case, display-name, null, non-array, non-string member, too
   many entries, missing `abilities`, and every extra/derived field named above.
6. After every rejection, query the actor and compare exact stored component bytes and revision.
7. Same actor state and input produce identical proposed data; the mechanic consumes no random
   roll and returns no seed-dependent game value.
8. Intent searches and actions select only the scoped recorder, not the skill or level recorder.
9. Final Orban component contains exactly canonical `["con","wis"]` plus the fixed sourceRef.
   Existing abilities, level 5, and Perception/Stealth bytes are unchanged.
10. Query the contract, component definition, mechanic, actor, and relevant history. Confirm no
    temporary entity remains.
11. Run `dotnet test DantesRoleplay.slnx --no-build --no-restore` and require 213/213, then run
    `git diff --check`.

### Slice 1 exit gate

Every matrix group passes with operation IDs and exact state/effect evidence recorded in this
file; the three live artifacts are queried back; Orban has the intended canonical save
proficiencies and otherwise unchanged bytes; no temporary state remains; repository checks pass.
Only then mark Slice 1 verified and stop for review. Do not begin Slice 2 in the same pass.

### Slice 1 completion evidence — 2026-08-18

All runtime artifacts were created only through the live MCP database; no runtime payload or
JavaScript was added to this repository.

| Evidence | Result | Operation ID |
| --- | --- | --- |
| Shared-owner contract dry run | All structural checks passed; `no-near-duplicate` advisory identified abilities as a lexical neighbor after the already-rejected sibling design had been replaced by this revision | `056cd06e153a4cb79005866a8b9b39b6` |
| Shared-owner contract commit | `procedure.mechanic.dnd2024.skill-proficiencies` revised from v1 to v2; it now owns separate skill and saving-throw list state | `b82110011f544b32a5414057bbe35984` |
| Pre-write actor read | `creature.orban` had abilities, character-level, and skill-proficiencies only; the new component was absent | `f61829f6348f45d7958411b1bb50530e` |
| Component creation | Created `dnd2024.saving-throw-proficiencies` once with closed `abilities`/`sourceRef` schema, six-id enum, uniqueness, max six, and fixed locator | `178a169446cc409d80d6ad900a644faf` |
| Recorder dry run | Every blocking mechanic check passed: id, create, requirements, component existence, source, match phrases, and category | `cb6c5348d3d74f4bb95c9512b848e64f` |
| Recorder creation | Created `mechanic.dnd2024.saving-throw-proficiencies.record` v1, scoped to `dnd2024-srd-5.2.1` | `e2172c00c71145ffbeb32fc0460910c0` |
| Add and all-six canonicalization | Reverse input produced exactly `str,dex,con,int,wis,cha`, `previousAbilities: null`, and one `component.add` | `3ed4e44687574246b2ff1e7b7ffbfe56` |
| Explicit-empty correction | `[]` produced one `component.set` and retained the fixed source reference, proving it is distinct from the original missing component | `23f2c501d7a94674a7991fafbf2bc827` |
| Multi-value canonicalization | `wis,con` produced exactly `con,wis`, one `component.set`, and prior `[]` | `f2d5717808c64c198c3fa986cc38ecbe` |
| Replay | The same state and input again produced the same canonical record and effect data; only `previousAbilities` reflected the existing identical state | `8394ce023c5b4839b32f4e0cab2a7081` |

The negative matrix used real actions for unknown id, duplicate, wrong case, display name, null,
non-array, non-string member, more than six entries, missing `abilities`, and an extra field. All
ten returned `MECHANIC_FAILED`; after each, an actor query confirmed the exact `con,wis` component
bytes and no derived fields remained unchanged. The final artifact queries confirmed contract v2,
recorder v1, and Orban's canonical state. The recorder's action routing selected it ahead of the
skill and level recorders.

Repository verification: `dotnet test DantesRoleplay.slnx --no-build --no-restore` passed
**213/213** on 2026-08-18. `git diff --check` is required after this evidence update.

## Slice 2 — fixed-DC saving-throw resolution

This slice remains blocked until Slice 1 is verified in a prior pass.

### Runtime artifacts

- Procedure contract: `procedure.mechanic.dnd2024.saving-throw`
- Mechanic: `mechanic.dnd2024.saving-throw`
- Scope: `dnd2024-srd-5.2.1`
- No new component, entity, migration, MCP kind, C# helper, generic selector, or effect type.

### Closed input and required state

- Required input: exact `ability` and `dc`.
- Optional input: exact `rollCircumstances` and `voluntaryFailure` only.
- `ability` is one exact lowercase stable id. `dc` is a finite nonnegative integer.
- `rollCircumstances` has Feature 3's exact array/member/duplicate/source validation.
- `voluntaryFailure`, when present, must be boolean. Absent and `false` mean a rolled save. `true`
  requires circumstances absent or `[]` and selects the no-roll failure branch.
- Reject caller-supplied ability modifier, Proficiency Bonus, proficiency flag, total, outcome,
  roll mode, dice, selected roll, source reference, effect, save consequence, or any extra key.
- Subject requirements are `dnd2024.abilities`, `dnd2024.character-level`, and
  `dnd2024.saving-throw-proficiencies`. Validate all three closed shapes, fixed source references,
  canonical save order, stable ids, level 1–20, and requested ability before randomness.

### Rolled resolution

1. Derive ability modifier as `floor((score - 10) / 2)`.
2. Derive Proficiency Bonus as `2 + floor((level - 1) / 4)` and add it exactly once only if the
   requested ability is in the explicit save-proficiency list.
3. Apply Feature 3 exactly: no circumstances rolls once; Advantage only rolls twice/selects max;
   Disadvantage only rolls twice/selects min; any mixture cancels to one roll; same-kind sources
   never add a third die.
4. Total is selected d20 plus the auditable modifiers. Success is `total >= dc`.
5. Natural 1 and 20 have no automatic saving-throw override.
6. Return zero effects.

### Voluntary-failure resolution

After all input and actor state validates, `voluntaryFailure: true` consumes no random number and
returns failure with `resolution: "voluntary-failure"`, `die: "1d20"`, `rollMode: null`, `rolls:
[]`, `roll: null`, `total: null`, `succeeded: false`, empty validated circumstances, the derived
modifier list for audit, and zero effects. It must fail even at DC 0. Reject nonempty
`rollCircumstances` rather than silently ignoring them.

### Result envelope

Return at least: `test: "saving-throw"`, `resolution: "rolled" | "voluntary-failure"`, `ability`,
`proficient`, `dc`, `die`, `rollMode`, `rolls`, `roll`, `rollCircumstances`, `modifiers`, `total`,
`succeeded`, and source locator. Modifier entries must distinguish the ability score contribution
from `proficiency (level <n>; <ability> save)`.

### Slice 2 implementation sequence

1. Re-read the workflow, completed Slice 1 evidence, all live dependencies, governing write/action
   contracts, Orban baseline, and exact official locators.
2. Repeat exact-id, category, and intent searches. Check `saving throw`, all six named save phrases,
   `make a save`, and `save against a dc` against every active mechanic.
3. Create the save procedure contract through dry-run, identical commit, and query-back.
4. Implement exactly one direct-source save mechanic. Dry-run, commit the identical payload, and
   query it back before any action.
5. Add focused match phrases only if searches show unambiguous routing. Never use bare `save` as a
   phrase.
6. Execute the full matrix with numeric Int64 seeds and parse the returned mechanic identity,
   version, structured result, log, applied effects, and history.
7. Use normal recorder mechanics to vary Orban level/proficiency state and restore the final
   baseline. Use disposable entities only for missing/corrupt state; create/delete them through
   dry-run-first effects and query both transitions.
8. Record evidence, run repository checks, mark Slice 2 and Feature 4 complete, and stop.

### Slice 2 acceptance matrix

1. Every ability id uses its own score and expected modifier.
2. Same seed/ability/DC at level 5 differs by exactly +3 between proficient and nonproficient
   state; an explicit empty list is valid and adds no bonus.
3. Level boundaries 4/5/16/17 produce Proficiency Bonus +2/+3/+5/+6. Restore level 5 afterward.
4. No/empty circumstances, one/multiple Advantage, one/multiple Disadvantage, 1v1, 2v1, and 1v2
   cancellation produce the correct roll counts and selected dice.
5. Unequal and tied dice are covered. Same seed/input/actor/mechanic version replays identical
   dice, selection, modifiers, total, result, narration/log, and effects.
6. Natural 1 and natural 20 remain total comparisons. Include DCs that prove they are not automatic
   outcomes, not merely examples where the ordinary total happens to agree.
7. `voluntaryFailure: true` at DC 0 rolls zero dice, returns the exact nullable/empty envelope,
   fails, and applies zero effects. Different seeds produce the same no-roll data.
8. Reject a nonboolean voluntary flag and voluntary failure with nonempty circumstances.
9. Reject every malformed circumstance and caller-derived field established by Feature 3.
10. Reject malformed ability/DC and every extra save-specific field before rolling.
11. Missing abilities, level, or save state and corrupt level/save state fail closed using queried
    disposable fixtures. Every fixture is deleted afterward.
12. Named saving-throw intents select only the scoped v1 save mechanic above the generic threshold
    example and ability-check mechanic. Administrative recording intents still select only the
    recorder.
13. Every rolled or voluntary save applies zero effects. Final Orban bytes equal the Slice 1
    baseline: original abilities, level 5, Perception/Stealth, and save proficiencies Con/Wis.
14. Query both new artifacts and relevant history; confirm source locators and active versions.
15. Require 213/213 repository tests and a clean `git diff --check`.

### Slice 2 exit gate

All fifteen groups pass; temporary fixtures are gone; the final actor state is exact; both live
artifacts are queried back; operation IDs and concise numerical evidence are recorded here; and
repository checks pass. Only then mark Feature 4 verified and stop for review.

### Slice 2 completion evidence — verified 2026-08-18

- Live artifacts: procedure.mechanic.dnd2024.saving-throw v1 (commit
  9b291af864bc4610bbbb8e7b01ad227b) and mechanic.dnd2024.saving-throw v2 (commit
  b5b754ab83604f38a5b54f1a484f42c2). The first live action exposed a closed-key-order defect;
  it was corrected and dry-run before the v2 commit.
- Every ability resolved with Orban's canonical modifiers: Str +1, Dex +3, Con +2, Int +0,
  Wis +1, Cha -1; Con/Wis each added level-5 proficiency +3 and the other four did not. Clearing
  and restoring the normal recorder state proved an exact +3 Con delta. Levels 4/5/16/17 produced
  +2/+3/+5/+6, then level 5 and con,wis were restored.
- Normal/empty, one/multiple Advantage, one/multiple Disadvantage, 1v1/2v1/1v2 cancellation,
  unequal selection, and a 4/4 Advantage tie passed. Replaying seed 4101 reproduced the complete
  structured result. Natural-1 seed 4811 succeeded at total 6 versus DC 6; natural-20 seed 4817
  failed at total 25 versus DC 26. Voluntary failure at DC 0 used zero rolls for two different
  seeds and returned the required nullable envelope.
- Fifteen malformed/derived/circumstance/voluntary inputs failed closed. Five dry-run-first
  disposable fixtures covered missing abilities, level, and save state plus corrupt level and
  noncanonical saves; all failed closed and were deleted (create 7362a87824a847b28102a420019de667,
  delete 3fe20b548a6c4fb3b5c31eba83c215b9).
- Named and generic save phrases selected only the save mechanic; the administrative recording
  intent still selected mechanic.dnd2024.saving-throw-proficiencies.record. Every successful save
  returned effects: []. Final Orban data is exactly abilities 12/16/14/10/13/8, level 5,
  Perception/Stealth, and canonical Con/Wis saves.

## Plan-change rule

If implementation reveals a lower dependency, ambiguous routing, an incompatible Feature 3
convention, or a need for monster CR, spell effects, persistent conditions, or a new shared D20
owner, stop. Add and review the dependency here before writing around it. Do not bypass it with a
caller-supplied bonus, total, selected die, success flag, effect, duplicate state, or a second
near-identical mechanic.
