# Feature 14 dependency plan — Exhaustion levels and their effects

Status: **Revised after Feature 13 verification. Slice 0 is the first authorized implementation
pass: ordinary-action declared-event propagation**
Last updated: 2026-08-20

## Execution rule

Planning-only artifact under `AGENTS.md` and the active `procedure.system.create-feature`. Repository
catalog files are the development authority. Each implementation pass completes one lowest slice,
validates the catalog in a fresh disposable database, records objective evidence, and stops for
review. A persistent catalog import belongs only to an explicit integration-play or release
boundary. This plan creates no procedure, component, mechanic, fixture, or game state. The
implementation revision below adds a kernel prerequisite because ordinary actions currently discard
a mechanic's declared events before the event ledger can validate or record them.

## Target capability

A creature can accumulate and shed Exhaustion levels, and while it has any, every D20 Test it makes
is reduced by twice its level and its movement allowance is reduced by five feet per level —
automatically, everywhere, without a GM applying arithmetic by hand.

### Included

- Exhaustion stored as a leveled entry on the existing `dnd2024.conditions` component.
- Gaining and recovering levels through the existing condition writer, extended with two modes.
- A flat numeric D20 Test penalty of `-2 × level` derived by the existing state-effects resolver
  and consumed by all four D20 owners.
- A movement allowance reduction of `5 × level` feet applied where the allowance is restored.
- The level-6 lethal threshold recorded as state and announced as an event.

### Excluded

- **Death itself.** Level 6 means the creature dies, but "dead" is a state Feature 17 owns.
  Feature 14 records the level, declares the event, and stops. This is a boundary, not an omission —
  see decision 5.
- Recovery pacing. The SRD removes one level on a Long Rest; rests are Feature 33. Feature 14
  provides only the explicit manual recovery transition that Feature 33 will later call.
- Every cause of Exhaustion — forced march, starvation, spell effects, monster abilities. Causes
  call the writer; they are not modeled here.
- Speed as a derived character fact (Feature 20), and the distinction between Speed and the
  per-turn movement allowance Feature 12 tracks.
- Exhaustion immunity, resistance to Exhaustion, and any non-SRD level scale.

## Official source basis

`source.dnd2024.srd-5.2.1`, *System Reference Document 5.2.1* (2025-05-01, CC-BY-4.0),
`Rules Glossary > Exhaustion` (PDF p. 181).

The rule as it must be implemented:

- Exhaustion is cumulative. Each time a creature receives it, its level rises by 1.
- While a creature has Exhaustion, every D20 Test it makes is **reduced by 2 × level**. This is a
  flat penalty on the total, not Disadvantage — the distinction is the single most important fact
  in this feature and is why the resolver's `derivedModifiers` channel exists.
- Speed is reduced by **5 × level** feet.
- The creature dies when its level reaches 6.
- Finishing a Long Rest removes one level; the condition ends at level 0.

## Planning inventory and overlap result

| Inquiry | Evidence and conclusion |
| --- | --- |
| Existing exhaustion owner | Nothing in `catalog/` mentions exhaustion. Feature 13's plan deliberately excludes `exhaustion` from its fourteen-id vocabulary and names Feature 14 as the owner that adds it by revision. |
| Condition store | Feature 13 Slice 1 (blocked, planned) defines `dnd2024.conditions` as `{entries, sourceRef}`, where each non-Exhaustion entry has `condition` and optional `sourceEntityId`, unique by that pair. Feature 14 may add exactly one source-absent Exhaustion entry with `level`; it must preserve every source-aware non-Exhaustion entry unchanged. |
| Modifier channel | Feature 13 Slice 2 (blocked, planned) defines `mechanic.dnd2024.d20-test.state-effects` returning `derivedModifiers`, empty in every Feature 13 slice and reserved for this feature. |
| D20 consumers | Four mechanics will compose the resolver after Feature 13: `check.ability` (v5), `saving-throw`, `weapon-attack`, `initiative.roll`. Each already maintains an auditable `modifiers` list — `procedure.mechanic.dnd2024.check.ability` requires a modifier entry with an explanatory source string, and `procedure.mechanic.dnd2024.saving-throw` requires "auditable modifiers" in its result. The channel this feature needs already exists in every consumer's result contract. |
| Movement allowance | Feature 12 Slice 1 (blocked, planned) defines `dnd2024.turn-budget` with `movementRemainingFeet` and a provisional `movementMaximumFeet`; Feature 12 Slice 2 restores the remaining value from the recorded maximum at turn start. |
| Event model | E1 is verified. `procedure.event.define` registers a versioned payload schema; `procedure.event.react` forbids a rule from declaring a `world.*` type but permits any other dotted id. Notifications remain unavailable. |

## Verified existing dependencies

| Dependency | Evidence |
| --- | --- |
| Source registry | `catalog/world/entities/source.dnd2024.srd-5.2.1.json` — version, URLs, CC-BY attribution, locator format. |
| Modifier auditing | `mechanic.dnd2024.check.ability` v4 returns `modifiers` with a source string such as `"proficiency (level <n>; <skill>)"`; the pattern for a named, auditable numeric adjustment is established and verified. |
| Event type registration | E1's six verified slices, `procedure.event.define`, and the existing `world.*` type/schema pairs under `catalog/event-types/`. |
| Atomic effects and replay | `procedure.mechanic.run`; Features 6–9 exit gates. |

## Recursive dependency analysis

```text
Feature 14: Exhaustion levels and their effects
├─ SRD Exhaustion rule                                            [implemented source basis]
├─ event type registration                                         [implemented: E1]
├─ D20 result modifier channel in all four consumers               [implemented: Features 1-5, 8]
├─ condition component and writer                                  [BLOCKED: Feature 13, Slice 1]
├─ state-effects resolver and its derivedModifiers channel         [BLOCKED: Feature 13, Slice 2]
├─ turn budget and its restore path                                [BLOCKED: Feature 12, Slices 1-2]
└─ Exhaustion as enforced state                                   [blocked parent]
   ├─ leveled entry, gain/recover transitions, lethal event        [missing leaf: Slice 1]
   ├─ flat D20 penalty derived and consumed                        [blocked: Slice 2]
   └─ movement allowance reduction                                 [blocked: Slice 3]
```

No dependency requires a new migration, MCP tool, commit kind, query kind, C# helper, or external
service.

## Dependency and ownership decisions

1. **Exhaustion lives on `dnd2024.conditions`, not on a component of its own.** It is a condition
   in the SRD, a GM asking "what conditions does this creature have?" must see it, and a separate
   `dnd2024.exhaustion` component would mean two answers to that question. Feature 13's entry shape
   was designed for source-aware non-Exhaustion instances. Feature 14 adds one source-absent entry
   `{"condition":"exhaustion","level":<1..6>}` while preserving every other entry's optional
   source identity. Feature 14 revises the definition, the schema, and the writer.

2. **Gaining is not applying.** `dnd2024.conditions`'s `apply` mode refuses an already-present id,
   which is correct for the fourteen non-stacking conditions and wrong for Exhaustion. Rather than
   weaken `apply`, Feature 14 adds two modes — `exhaust` and `recover` — and **`apply`, `clear`,
   continue to reject `exhaustion` outright**. A stacking rule and a non-stacking rule
   must not share a transition; one of them would have to be special-cased inside the other, and the
   special case would be invisible at the call site.

3. **The penalty is a modifier, not Disadvantage.** This is the decision that shapes Slice 2. The
   2024 rule reduces the roll by a number; it does not grant Disadvantage. Routing it through
   `derivedCircumstances` would produce the wrong arithmetic, would interact wrongly with
   cancellation, and would silently break every Feature 13 acceptance assertion about circumstance
   counts. It goes through `derivedModifiers`, which Feature 13 created empty for exactly this.

4. **All four D20 consumers change in one slice, and that is deliberate.** The guide warns against
   bundling siblings, and this is the exception it also allows: a slice must leave the system
   internally valid. A system where an Exhausted creature's ability check carries the penalty but
   its saving throw does not is not a partially built feature — it is a wrong rules engine, and a
   GM cannot tell which of the four is right. The change is also literally identical in each
   consumer (read `derivedModifiers`, append to `modifiers`, include in `total`), so there is one
   decision, not four. Slice 2's gate requires all four proven together.

5. **Level 6 is recorded and announced; death is Feature 17's.** Feature 14 stores level 6 as valid
   state and, in the same transition, declares the event `dnd2024.exhaustion.reached-lethal`
   carrying the creature id, the resulting level, and the source locator. Feature 17 subscribes and
   owns what "dead" means. This mirrors the lesson recorded in the Feature 15 plan about
   `dnd2024.damage.dealt`: **if the feature that produces a fact does not announce it, the feature
   that must react to it has to re-plumb the producer later.** Declaring the event now costs one
   event-type registration and saves a revision of a verified mechanic.

   The event is declared even though no subscriber exists yet. `procedure.event.define` registers
   schemas only, and an event with no subscriber is a legitimate, inspectable ledger entry.

6. **The writer refuses to exceed 6 and refuses to go below 0.** `exhaust` beyond 6 fails rather
   than clamping, because clamping would silently discard a level the caller believed was applied.
   `recover` below 0 fails for the same reason. At level 0 the entry is removed from the list
   entirely rather than stored as `level: 0`, so "has the Exhaustion condition" stays a single
   membership test.

7. **Movement reduction is applied at restore, not stored.** `movementMaximumFeet` remains the
   creature's recorded unreduced allowance. Feature 12 Slice 2's restore computes
   `max(0, movementMaximumFeet - 5 × level)`. Writing the reduced number into the maximum would
   destroy the original when the Exhaustion ended — the same reasoning Feature 13 decision 9 records
   for Speed-0 conditions.

## Slice order and stop gates

| Order | Slice | Starts only when | Exit gate |
| --- | --- | --- | --- |
| 0 | Ordinary-action declared-event propagation | Feature 13 verified; plan revised | A declared non-structural event is validated, guarded, ledgered, and routed atomically with a root action's effects; malformed or rejected events roll back the effects. |
| 1 | Leveled entry, gain/recover transitions, lethal event | Feature 13 verified; plan reviewed; clean `roleplay validate catalog` | A creature gains and sheds Exhaustion levels 0–6 through two dedicated modes; the plain condition modes still reject `exhaustion`; level 6 records and announces exactly one lethal event; every boundary and corrupt case is rejected without state change. |
| 2 | Flat D20 penalty, derived and consumed by all four owners | Slice 1 verified | An Exhausted creature's ability check, saving throw, attack roll, and Initiative roll each carry a `-2 × level` auditable modifier, and an unexhausted creature's results are byte-identical to the pre-revision behavior. |
| 3 | Movement allowance reduction | Slice 2 verified | Restoring a turn gives an Exhausted creature `max(0, maximum - 5 × level)` feet while leaving `movementMaximumFeet` untouched. |

## Slice 0 — ordinary-action declared-event propagation

### Status and scope

The first implementation pass after Feature 13 verification. It corrects the discovered kernel
handoff before any Exhaustion catalog record is authored.

### Runtime boundary

- Revise ActionRunner, IEffectApplier, and EffectApplier so a root action passes its declared
  mechanic events alongside its proposed effects through the existing transactional event pipeline.
- Reuse the existing declared-event checks: active registered type, non-structural type, valid
  payload schema, and live named entity ids. Structural events remain derived solely from effects.
- Root declarations share the action operation id as correlation id and have no causation event.
  Reactions retain their existing execution and causation behavior.
- A rejected declaration rolls back every root effect and writes no event. Effects-only actions
  remain unchanged. A valid declaration with no effects is still recorded.

### Acceptance and exit gate

Prove: one root action commits one effect and one custom event atomically; unknown type, invalid
schema, structural type, or missing event entity commits neither; guard or reaction failure rolls
back both; ordering is deterministic; effects-only actions retain their existing event and replay
behavior. Run focused kernel tests, the full suite, catalog validation, and diff check. Slice 1
stays blocked until this gate passes.

## Slice 1 — leveled entry, gain and recovery, lethal event

### Runtime artifacts

| Artifact | Proposed ID / category | Change |
| --- | --- | --- |
| Governing contract | `procedure.mechanic.dnd2024.conditions` | **Revised** to add the Exhaustion vocabulary entry, the level rules, the two new modes, and the event. |
| Component definition and schema | `dnd2024.conditions` | **Revised**: the entry enum gains `exhaustion`; an Exhaustion entry requires `level`, an integer 1–6, and forbids `sourceEntityId`; every non-Exhaustion entry continues to forbid `level` while retaining its optional source entity. `entries.maxItems` rises from 14 to **15**. The writer also enforces that there is at most one Exhaustion entry, irrespective of the general non-Exhaustion `(condition, sourceEntityId)` uniqueness rule. Widening the enum, raising the bound, and adding a conditional field are backward compatible with stored Feature 13 components. |
| Writer | `mechanic.dnd2024.conditions.write` | **Revised** to add modes `exhaust` and `recover` and to reject `exhaustion` in `apply` and `clear`. |
| Event type and schema | `dnd2024.exhaustion.reached-lethal` | New. Closed payload: `creatureId`, `level` (const 6), `sourceRef`. |
| Regression coverage | `CatalogFeature14Tests` | New fresh-import coverage. |

### Governing contracts and source locator

Before writing, re-read `procedure.system.create-feature`,
`procedure.mechanic.dnd2024.conditions` as implemented, `procedure.event.define`,
`procedure.event.react` (for the rules on declaring a non-`world.*` event),
`procedure.mechanic.run`, `procedure.mechanic.projection`, and `procedure.world.change`. Confirm the
`Rules Glossary > Exhaustion` PDF p. 181. Re-search `exhaustion`, `exhausted`, `fatigue`, `tired`,
and the proposed mode phrases against the authored catalog.

`sourceRef` on the event payload is fixed to
`{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Rules Glossary > Exhaustion"}`. The component's
existing `sourceRef` (`Rules Glossary`) is unchanged.

### Data/input contract and required state

- `exhaust` input is exactly `{"mode":"exhaust","levels":<integer 1..6>}`.
- `recover` input is exactly `{"mode":"recover","levels":<integer 1..6>}`.
- Both require role `subject` with a valid existing `dnd2024.conditions` component.
- Exhaustion is a single aggregate level, not a source-instance condition. Its entry has no
  `sourceEntityId`; the writer's existing optional `source` role remains available only to
  `apply`/`clear`, and `exhaust`/`recover` reject a bound source role and all provenance in input.
  Causes retain their own provenance at their call site or event rather than forging it into the
  aggregate state.
- `exhaust` computes `newLevel = currentLevel + levels`, where `currentLevel` is 0 when no
  Exhaustion entry is present. `newLevel > 6` fails; it does not clamp.
- `recover` computes `newLevel = currentLevel - levels`. `currentLevel === 0` fails with a distinct
  "not exhausted" reason; `newLevel < 0` fails with a distinct "would recover below zero" reason.
  The two are separately assertable.
- `newLevel === 0` removes the Exhaustion entry from the list; any other value writes or updates it
  with the entry re-sorted into canonical position.
- Both modes apply exactly one `component.set` carrying the complete re-sorted list.
- `exhaust` reaching exactly 6 additionally declares exactly one
  `dnd2024.exhaustion.reached-lethal` event naming `subject`. Reaching 6 from 5 and reaching 6 from
  3 in one call both announce once. A creature already at 6 cannot `exhaust` further, so the event
  cannot fire twice for one creature without an intervening `recover`.
- Rejected before any effect: `levels` of 0, negative, fractional, non-finite, or above 6; a
  missing `levels`; a caller-supplied `level`, `newLevel`, `entries`, `sourceRef`, `dead`, `events`,
  or `effects` field; any extra key; and `exhaustion` appearing in an `apply` or `clear` array.

### Recording behavior

1. Validate closed input, then the complete existing component: vocabulary membership, the
   source-aware uniqueness and canonical order of non-Exhaustion instances, `sourceRef`, and — for
   Exhaustion — exactly zero or one source-absent entry with integer `level` 1–6. No
   non-Exhaustion entry may carry `level`.
2. Reject before constructing an effect or declaring an event. No randomness is consumed.
3. Compute the new level and the new list; propose one `component.set`; declare the lethal event
   only on the exact transition to 6.
4. Return mode, `previousLevel`, `newLevel`, `levelsChanged`, whether the entry was created,
   updated, or removed, whether the lethal event was declared, and the source reference.

### Invariants, failure behavior, and non-goals

- Exactly one Exhaustion entry, or none; `level: 0` is never stored.
- `apply` and `clear` remain the non-stacking path and never touch Exhaustion; every
  Feature 13 Slice 1 acceptance row still passes unchanged.
- The writer applies no death, no Unconscious condition, no Hit Point change, and no speed change;
  it changes no other entity.
- The lethal event asserts a fact; it does not kill anything. With no subscriber registered it is a
  ledger entry and nothing more, which is the intended state until Feature 17.

### Slice 1 implementation sequence

1. Confirm Feature 13 is verified through Slice 6; record clean focused-test and `roleplay validate
   catalog` baselines.
2. Re-read the listed contracts and repeat overlap and routing searches.
3. Author the revised contract, revised definition and schema, revised mechanic `.md`/`.js` pair,
   the new event type and schema, manifest entries, and the focused fresh-import test as catalog
   files first.
4. Run `roleplay validate catalog`; resolve every schema, write-side, routing, or event-schema
   failure in its disposable validation database. Do not import into the persistent database.
5. In fresh disposable test databases, exercise the full acceptance matrix, including emitted event
   payload and causation evidence; dispose fixtures without changing catalog fixtures or persistent
   game state.
6. Run focused tests, the full suite, `roleplay validate catalog`, and `git diff --check`; record
   evidence; mark only Slice 1 verified; stop for review.

### Slice 1 acceptance matrix

| Class | Required assertion |
| --- | --- |
| Happy path | `exhaust 1` on an unexhausted creature creates the entry at level 1 in canonical position with exactly one effect; `exhaust 1` again reads level 2. |
| Boundaries | Levels 1 through 6 each record; `exhaust` to exactly 6 succeeds and announces; `exhaust` beyond 6 from 5, from 1 with `levels: 6`, and from 6 with `levels: 1` all fail with no state change and no event; `recover` to exactly 0 removes the entry; `recover` below 0 fails. |
| List boundary | A creature already holding all fourteen non-Exhaustion conditions can still be exhausted, producing a 15-entry list that validates and stores in canonical order. This row exists because it is the exact case the raised `maxItems` exists for. |
| Differential | Two creatures differing only in Exhaustion level have components differing in exactly that one integer; all other entries are byte-identical. |
| Closed input | `levels` of 0, −1, 1.5, `"1"`, `null`, missing, and 7; supplied `level`/`newLevel`/`entries`/`sourceRef`/`effects`; one extra key — each fails with zero effects and no event. |
| Mode separation | `apply ["exhaustion"]` and `clear ["exhaustion"]` each fail with a distinct reason; `exhaust` and `recover` with a source role, `sourceEntityId`, or any non-Exhaustion field fail. |
| Missing state | `exhaust` and `recover` against an absent `dnd2024.conditions` component each fail with a distinct reason. |
| Not-exhausted | `recover` on a creature at level 0 fails with the "not exhausted" reason, distinct from the below-zero reason. |
| Corrupt state | A stored Exhaustion entry with `level: 0`, `level: 7`, a fractional level, a missing level, a `level` on a non-Exhaustion entry, or a wrong canonical position is rejected by both modes before any effect. |
| Events | Reaching 6 declares exactly one event with the exact closed payload; reaching 5 declares none; recovering from 6 declares none; recovering from 6 to 5 and returning to 6 declares a second event, and this is asserted as intended rather than incidental. The event validates against its registered schema version. |
| Determinism | Equivalent databases and input produce byte-identical components and equivalent event payloads; no `ctx.randomInt` call. |
| Routing | `exhaust the character` and `recover a level of exhaustion` select only this writer. `apply the poisoned condition`, `record hit points`, `spend my action`, and `take a long rest` must not select it. |
| Effects | Exactly one effect per success; zero on every rejection; the event never appears without its effect. |
| State integrity | Before/after byte comparison on the subject and one untouched sibling for every rejection. |
| Readback | Revised contract, definition, schema, and mechanic at new versions with prior versions readable; the new event type queried back. |
| Restoration | Disposable creatures deleted through validated effects; absence queried; Feature 10–13 baselines untouched. |
| Repository | `roleplay validate catalog`, every Feature 13 Slice 1 row, focused tests, the full suite, and `git diff --check` pass; no persistent import occurs. |

### Slice 1 exit gate

Every row passes with recorded mechanic id and version, parsed result fields, exact effect counts,
event payload evidence, before/after bytes, disposable-database readback, cleanup evidence, and
repository checks. Slice 2 stays blocked until a new review authorizes it.

## Slice 2 — the flat D20 penalty, derived and consumed

### Status and prerequisite

Blocked until Slice 1 is verified. Revises `procedure.mechanic.dnd2024.d20-test.state-effects` and
its mechanic, and all four D20 consumers: `check.ability`, `saving-throw`, `weapon-attack`, and
`initiative.roll`, together with their governing contracts. Adds no new mechanic and no new
component.

### Data/state and resolution contract

- The resolver populates `derivedModifiers` with, at most, one entry:
  `{"value": -2 * level, "source": "condition:exhaustion (level <n>)"}`. The source string follows
  the established auditable-modifier convention used for `"proficiency (level <n>; <skill>)"`.
- It adds `exhaustion` to `effectiveConditions` and exposes its level in a dedicated
  `exhaustionLevel` result field; `sourcesByCondition.exhaustion` is the empty array because this
  aggregate condition has no entity source. The existing non-Exhaustion source arrays are copied
  unchanged.
- `derivedModifiers` is a subject-wide report: Exhaustion reduces every D20 Test the projected
  subject makes, so it contains the one entry whenever that subject has Exhaustion and is otherwise
  empty. The resolver accepts no `test` or `against` input; its established static `{}` child input
  cannot receive those fields. A defender's report is therefore visible to the attack parent but is
  not an attacker modifier.
- Each consumer appends the resolver's `derivedModifiers` to its own `modifiers` list, in a fixed
  position defined by its revised contract, and includes them in `total`. No consumer recomputes
  the penalty and no consumer stores it.
- The penalty applies to the total. It never changes the number of dice, the roll mode, the
  selected die, the natural-20/natural-1 classification, or the DC. Feature 8's rule that a natural
  20 always hits is unaffected by any Exhaustion level — an exhausted creature that rolls a natural
  20 still hits, and that is a required test.
- The `weapon-attack` consumer appends only the subject/attacker child's `derivedModifiers` and
  deliberately ignores the target child's list. A defender's Exhaustion must never alter the
  attacker's total.

### Acceptance and exit gate

Prove, for all four consumers: levels 1 through 6 produce totals reduced by exactly 2, 4, 6, 8, 10,
and 12 against an identical seed and state; the modifier appears once in the audit list with the
exact source string; an unexhausted creature's result is byte-identical to the pre-revision result
for the same seed, ability, DC, skill, weapon, and state; the penalty does not alter roll mode,
dice count, selected die, or natural-roll classification; a natural 20 attack by a level-6 exhausted
creature still hits and still classifies as a Critical Hit; a level-6 exhausted attacker attacking
a level-6 exhausted defender has exactly one −12 modifier, not two; automatic saving-throw failure
from Feature 13 Slice 3 still consumes no randomness and does not append a modifier to a
non-existent total; every Feature 13 Slice 2–5 acceptance row still passes; replay is exact;
routing is unchanged; revised artifacts are loaded from a fresh validation database while prior
versions remain readable. Run the full suite, `roleplay validate catalog`, and `git diff --check`;
no persistent import occurs. Slice 3 stays blocked.

## Slice 3 — movement allowance reduction

### Status and prerequisite

Blocked until Slice 2 is verified. Revises `procedure.mechanic.dnd2024.turn-budget`, the fan-out
reader `mechanic.dnd2024.turn-budget.read`, and the two Feature 11 transitions that Feature 12
Slice 2 revised: `mechanic.dnd2024.encounter-turn.start` and `.advance`. Adds no new mechanic and no
new component.

### Data/state and resolution contract

- **Contents carry no components**, so the Exhaustion level cannot be read from the transition's own
  projection any more than the budget could. Feature 12 Slice 2 already solved this by declaring the
  fan-out child `mechanic.dnd2024.turn-budget.read`, bound with `forEachContentsOf: "encounter"` and
  `roleBindings: {"participant": "$item"}`. This slice **revises that child** to declare
  `dnd2024.conditions` on its `participant` role and to report a structured condition read
  (`conditionsPresent`, `conditionsValid`, `exhaustionLevel`, `conditionProblem`) alongside its
  budget. A missing condition component is valid and reports level 0; malformed or semantically
  invalid condition state is a successful child report rather than a child failure, matching the
  reader's established budget behavior. One fan-out, one child, two facts — rather than a second
  child walking the same contents.
- Restoration sets `movementRemainingFeet` to `max(0, movementMaximumFeet - 5 × level)`, where
  `level` is 0 when there is no Exhaustion entry. `movementMaximumFeet` and `sourceRef` are carried
  through unchanged, and the effect remains a full seven-field `component.set`.
- The result reports `movementMaximumFeet`, the applied reduction, and the restored remaining feet
  separately, so a GM can see why the allowance is short.
- The transitions do not compose the state-effects resolver for this. The reduction is a two-term
  arithmetic expression over one stored integer, and routing it through a child that is already
  invoked once per D20 test for a different purpose would make the resolver's contract mean two
  things. The Exhaustion level is read directly from the participant's already-projected condition
  component. **If a second stateful movement reducer ever appears, that is the moment to introduce
  a movement-effects resolver — and Feature 20 is the likely place.** Recorded so the choice is a
  decision rather than an oversight.
- The reduction never touches `movementMaximumFeet`, never removes the budget component, and never
  changes any other participant.
- Start and advance reject only when the newly active participant's condition report is invalid;
  corrupt state on a non-active participant is reported but does not block the current transition.
  This preserves Feature 12's admission/advance distinction and supplies the same safe correction
  boundary for conditions that it already has for budgets.

### Acceptance and exit gate

Prove: levels 1 through 6 against a 30-foot maximum restore 25, 20, 15, 10, 5, and 0 feet with
`movementMaximumFeet` byte-identical throughout; a level-6 creature with a 25-foot maximum restores
to exactly 0 rather than a negative; a level-1 creature with a 0-foot maximum restores to 0; an
unexhausted participant restores to exactly the maximum, byte-identical to the Feature 12 Slice 2
result; recovering to level 0 restores the full allowance on the next turn; the outgoing
participant's budget is untouched; a corrupt condition component on the newly active participant
fails the whole transition with the turn state byte-identical; a corrupt condition component on a
non-active participant does not block the transition; every Feature 11 and Feature 12 Slice 2
acceptance row still passes; replay, routing, effect-exactness, disposable readback, and cleanup all
hold. Run the full suite, `roleplay validate catalog`, and `git diff --check`; no persistent import
occurs.

Feature 14 is verified only after the Slice 3 gate passes and this plan records evidence; then stop
before Feature 15.

## Forward dependencies this plan deliberately leaves open

| Concern | Owner | Note |
| --- | --- | --- |
| Death at level 6 | Feature 17 | Subscribes to `dnd2024.exhaustion.reached-lethal`. Its condition-integrity guard must preserve Feature 13 source-instance rules and this feature's single source-absent Exhaustion entry with level 1–6. The event exists from Slice 1 precisely so Feature 17 need not revise this writer. |
| Long-rest recovery | Feature 33 | Calls `recover` with `levels: 1`. Feature 14 provides the transition, not the pacing. |
| Speed as a derived fact | Feature 20 | Introduces the eventual authoritative Speed derivation and revises the turn-restoration consumer to use it. It preserves this feature's `5 × level` Exhaustion reduction rather than duplicating it. |
| Causes of Exhaustion | Features 32, 34, 35, 37 | Each cause calls `exhaust`; none re-models the level. |

## Plan-quality audit

1. Yes — one capability, Exhaustion as enforced state, with death and rest pacing explicitly
   excluded and assigned.
2. Yes — the source reference, heading, and verified PDF page 181 are concrete.
3. Yes — exhaustion, exhausted, fatigue, and tired were searched; no owner exists, and Feature 13's
   plan already names this feature as the one that adds the vocabulary by revision.
4. Partly — kernel, E1, and Feature 1–8 rows cite artifacts and verified gates; the Feature 12 and
   13 rows cite unimplemented plans, which is why this feature is blocked rather than ready.
5. Yes — Slice 1 is a standalone leaf; Slices 2 and 3 are blocked consumers.
6. Yes — stored level, derived penalty, derived movement reduction, transient input, and the death
   consequence each have one named owner.
7. Yes — Slice 1 lands the state with its only safe write path; each later slice revises an
   existing owner.
8. Yes — Slice 1 alone is named as next, and only once Feature 13 is verified.
9. Yes — absent entry, level 0, level 6, over-6, below-0, and corrupt-level semantics are explicit
   and separately assertable.
10. Yes — the `-2 × level` and `5 × level` formulas, their boundaries, effect counts, event
    conditions, and result fields are testable without guessing.
11. Yes — the matrix covers every applicable class. The **random-selection and natural-roll classes
    apply in an unusual way** and are not omitted: this feature must prove it changes *neither*, so
    they appear as negative assertions in Slice 2.
12. Yes — repository-mode disposable catalog validation, event-payload evidence, and persistent
    import boundaries are stated.
13. Yes — disposable fixture deletion and baseline preservation are explicit.
14. Yes — each slice has an objective all-or-nothing exit gate.
15. Yes — no JavaScript, commit payload, or duplicate JSON Schema is embedded.
16. Yes — this planning pass stops before implementation.

## Kernel constraints these plans were checked against

Verified by reading the kernel source during the planning pass, not assumed. Every plan in the
Features 12–17 block depends on all four:

1. **Contents carry no components.** `ProjectionResolver.cs` (~L196–200) materialises each contained
   entity as `new ContainedProjection(Id, Name, Slot)` and nothing more. A role's declared components
   are projected onto the role entity alone. The only way to see a contained entity's components is a
   declared child with `forEachContentsOf` and `roleBindings: {"<role>": "$item"}`, whose own role
   requirements decide what is projected. `mechanic.dnd2024.encounter-initiative-order` is the live
   worked example.
2. **A child's input has exactly three sources.** `MechanicComposer.ResolveInput` (~L223–260):
   inherit the parent's validated input, a static literal object, or `inputFromParentProperty` — a
   top-level key of the parent input whose value is an object. All children resolve before the parent
   source runs, so no child input can be templated from a sibling child's result, a projected
   component value, or a validated scalar.
3. **`component.set` is an upsert.** `EffectApplier.cs` (~L198–220) faults `ComponentAdd` on a
   present pair and `ComponentRemove` on an absent one. `ComponentSet` does neither; it emits
   `world.component.replaced` with `before: null`. Choosing between add and set is therefore a real
   decision with a silent failure mode, not a formality.
4. **There is no "optional component".** `RoleRequirement.Optional` (`MechanicModels.cs` ~L178–183)
   is a role-level flag. A declared component the entity lacks is simply absent from the projection
   and never fails it. Roles stay required; mechanics branch on absence and report it.

## Plan-change rule

Stop and revise before implementation if:

- The SRD re-read shows the 2024 Exhaustion penalty is Disadvantage rather than a flat `-2 × level`.
  The whole of decision 3 and Slice 2 depend on it, and the two rules are not interchangeable.
- Feature 13 ships an entry shape that cannot be extended with one source-absent, leveled Exhaustion
  entry while preserving non-Exhaustion source instances, which would invalidate decision 1 and force
  a descent into how the leveled condition is represented.
- Feature 13's resolver ships without the `derivedModifiers` channel, in which case Slice 2 must
  first add it as its own leaf rather than assuming it.
- Feature 12 ships without the fan-out reader `mechanic.dnd2024.turn-budget.read`, or ships one
  whose `participant` role cannot be extended with a second component. Slice 3 has no other route to
  a contained participant's Exhaustion level.

Descend to a new dependency rather than storing a reduced maximum, clamping a level silently,
routing the penalty through `derivedCircumstances`, or bundling Feature 17's death state into this
feature.
