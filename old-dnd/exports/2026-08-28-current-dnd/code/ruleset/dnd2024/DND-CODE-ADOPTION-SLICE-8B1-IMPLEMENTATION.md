# D&D code-adoption Slice 8B1 implementation — turn-budget admission

Status: **accepted**  
Parent: [Slice 8 complete native-recovery design](DND-CODE-ADOPTION-SLICE-8-DESIGN.md), leaf 8B  
Ruleset alignment: `dnd2024-owned`  
Source: `source.dnd2024.srd-5.2.1`, `Playing the Game > Actions; Bonus Actions; Reactions;
Interacting with Objects; Combat > Your Turn`  
Outcome: Recover the closed participant turn-budget component and administrative record/correct
writer.  
Exclusions: Reader diagnostics, lifecycle refresh, Conditions, resource spending, movement
execution, action-cost inference, fixtures, migrations, public operations, and archive deletion.  
Allowed areas: D&D application turn-budget catalog artifacts, the activated D&D test harness, this
plan, Parent 8 status/evidence, and one 8B1 receipt.  
Stop point: exact record/correct, invalid/no-change, revision, and replay evidence passes.

## Confirmed decisions and ownership

- The permanent IDs `dnd2024.turn-budget`, `mechanic.dnd2024.turn-budget.write`, and
  `procedure.mechanic.dnd2024.turn-budget` are accepted-matrix recovery IDs, not new aliases.
- The component stores availability of Action, Bonus Action, Reaction, one free interaction, and
  remaining movement feet. Base Speed remains separate `dnd2024.speed`; encounter turn identity
  remains `dnd2024.encounter-turn-state`.
- Administrative write is not a gameplay refresh or spend. The application action runner remains
  the sole revision, transaction, audit, rollback, and replay owner.
- Remaining movement is bounded to 0–1,000 feet as a repository safety bound. It is not a claimed
  universal SRD maximum and is not a stored movement maximum.

## Source and external-reference review

SRD 5.2.1 distinguishes Actions, Bonus Actions, Reactions, the free combat object interaction, and
movement on a turn. A Reaction is unavailable again until the start of the creature's next turn.
These rules justify distinct resource fields but do not require this repository's storage shape.

Pinned Foundry dnd5e commit `275bed0be4ccfa15e6b3347acccb8da8784726d9` was inspected as
reference only. `module/config.mjs` distinguishes activity activation types `action`, `bonus`, and
`reaction`; the activity usage dialog has its own consumption workflow, while actor movement is
derived separately. No Foundry code, globals, activity model, or runtime dependency is imported.

## Closed state and transition

The component contains exactly `action`, `bonusAction`, `reaction`, `freeInteraction`,
`movementRemainingFeet`, and fixed `sourceRef`. Availability fields are Boolean and remaining feet
is an integer from 0 through 1,000.

The writer accepts exactly `mode` plus the five mutable fields. `record` requires absence and
proposes one `component.add`; `correct` requires a parseable valid current component and proposes
one `component.set`. Caller provenance, maximum movement, Speed, encounter/turn identity, deltas,
history, and effects are rejected.

## Failure and acceptance

Missing role, non-object/extra/missing input, unknown mode, wrong field types, fractional or
out-of-range movement, duplicate record, absent correction, and malformed/invalid existing state
fail before an effect. Failed actions preserve exact bytes and revision. A repeated successful
operation replays without a second effect.

Focused acceptance covers lower/upper bounds, exact canonical bytes/source, record/correct
preconditions, invalid-state preservation, deterministic evaluation, revision increments, replay,
and existing D&D regression compatibility. No protocol walk is required because no public surface or
dependency registration changes.
