# Feature 17 dependency plan — dying, death saves, stabilization and death

Status: **Planned; the zero-Hit-Point policy and death-state slices are independent leaves. Event
reactions remain blocked on their producing features and the confirmed condition schema**
Last updated: 2026-08-20

## Execution rule

Planning-only artifact under `AGENTS.md` and the active `procedure.system.create-feature`. Repository
catalog files are the development authority. Each implementation pass completes one lowest slice,
validates the catalog in a fresh disposable database, records objective evidence, and stops for
review. A persistent catalog import belongs only to an explicit integration-play or release
boundary. This plan creates no procedure, component, mechanic, subscription, event type, fixture,
or game state.

**This is the largest feature in Tier F, at seven slices.** That is not scope creep. Dying is where
five previously independent subsystems — Hit Points, damage, conditions, events, and the turn
lifecycle — have to agree, and every attempt below to fold two slices together produced a slice
that could not be stopped safely at its own gate.

## Target capability

When a creature is reduced to 0 Hit Points, the system decides — without the GM adjudicating it —
whether it dies outright, drops unconscious and starts rolling death saving throws at the start of
each of its turns, or is already beyond saving; tracks its successes and failures; kills it on the
third failure; stabilizes it on the third success; and ends the whole state the moment it regains a
single Hit Point.

### Included

- A minimal zero-Hit-Point policy distinguishing a creature that makes death saves from one that
  dies at 0, without claiming that policy is an intrinsic creature kind.
- Death-save state: successes, failures, stability, and death.
- A guard that makes any path to the condition list safe, so a reaction can apply Unconscious
  without duplicating Feature 13's validation.
- Automatic consequences of damage: falling unconscious, instant death from massive damage, a
  monster dying at 0, and death-save failures from damage taken while dying.
- The death saving throw itself, including its natural-20 and natural-1 rules.
- The two exits: regaining any Hit Points, and stabilizing.
- Death from Exhaustion level 6.

### Excluded

- Resurrection, revivification, and any return from death.
- Lingering injuries, maiming, and any non-SRD death consequence.
- A Stable creature regaining 1 Hit Point after 1d4 hours. That needs elapsed time, and Tier E's
  non-goals rule out a scheduler and a clock. Feature 37 owns time; the explicit healing path
  remains available meanwhile.
- The Medicine check used to stabilize another creature as a *positional, action-costing* act —
  Feature 17 provides the stabilization transition and the DC-10 Wisdom (Medicine) check runs
  through Feature 2's existing named-skill resolver. Who may reach the dying creature is Feature 20.
- Monster stat blocks, Challenge Rating, and creature building (Feature 35).
- Massive damage as a variant rule beyond the SRD's instant-death sentence.

## Official source basis

`source.dnd2024.srd-5.2.1`, *System Reference Document 5.2.1* (2025-05-01, CC-BY-4.0), within
`Playing the Game > Damage and Healing` (PDF pp. 17–18), subsection `> Dropping to 0 Hit Points`
and its parts:

- `> Instant Death`: when damage reduces a creature to 0 Hit Points and damage remains, it dies if
  the remaining damage equals or exceeds its Hit Point maximum.
- `> Falling Unconscious`: damage that reduces a creature to 0 without killing it gives it the
  Unconscious condition until it regains any Hit Points.
- `> Death Saving Throws`: rolled when a creature starts its turn at 0 Hit Points. Roll a d20 with
  no ability, Proficiency Bonus, or circumstance modifier; 10 or higher succeeds. Three successes make it Stable; three
  failures kill it. Successes and failures need not be consecutive. Both counts reset to zero when
  the creature regains any Hit Points or becomes Stable. A natural 20 regains 1 Hit Point; a
  natural 1 counts as two failures.
- `> Stabilizing a Character`: a DC 10 Wisdom (Medicine) check stabilizes a creature at 0 Hit
  Points. A Stable creature makes no death saving throws, and stops being Stable if it takes damage.
- `> Character Demise` / monsters: a monster normally dies the instant it drops to 0 Hit Points.
- `Rules Glossary > Exhaustion`: a creature dies when its Exhaustion level reaches 6.

The verified rules settle the former blockers: damage at 0 Hit Points adds one failure (two on a
Critical Hit); becoming Stable resets both tallies; Exhaustion is on PDF p. 181; and its `-2 × level`
penalty applies to a Death Saving Throw because it is a D20 Test. The plan preserves the Death Save's
separate no-ability/no-proficiency rule.

## Planning inventory and overlap result

| Inquiry | Evidence and conclusion |
| --- | --- |
| Existing dying owner | Nothing in `catalog/` implements death, dying, unconsciousness, or death saves. Four contracts disclaim it in writing: `procedure.mechanic.dnd2024.hit-points` ("unconsciousness, death, massive damage, death saves … out of scope"), `procedure.mechanic.dnd2024.weapon-damage.roll` ("unconsciousness, death, massive damage … are later owners"), `procedure.mechanic.dnd2024.weapon-damage.apply` ("Zero Hit Points has no condition, death, or other consequence in this slice"), and `procedure.mechanic.dnd2024.saving-throw` ("Death saves … need separate contracts"). |
| Death save is not a saving throw | `procedure.mechanic.dnd2024.saving-throw` requires `dnd2024.abilities`, `dnd2024.character-level`, and `dnd2024.saving-throw-proficiencies`, derives an ability modifier and a Proficiency Bonus, and takes an `ability` input. A death save uses **none** of those. It is a d20 against a fixed 10 with no modifiers. Reusing that resolver would mean adding a "no modifiers" branch to a verified mechanic that has no ability to name — a separate owner is correct, and its own contract already says so. |
| Zero-Hit-Point policy | **Nothing in the catalog records whether a creature makes death saves or dies at 0.** `catalog/world/entities/` holds `creature.dnd2024.feature-10.hero.json` and `creature.dnd2024.feature-10.training-target.json`, distinguished only by id and name. Slice 1 records that rule outcome directly; it does not infer it from a player/monster label. |
| Damage event | Feature 15 Slice 4 (blocked, planned) registers `dnd2024.damage.dealt` with `targetId`, `sourceId`, `rawAmount`, `type`, `finalAmount`, mitigation flags, `beforeCurrent`, `afterCurrent`, `maximum`, `overkill`, `critical`, and — after Feature 16 Slice 3 — `temporaryBefore`, `temporaryAfter`, `temporaryAbsorbed`. `overkill` is computed after temporary absorption. |
| Healing event | Feature 16 Slice 2 (blocked, planned) registers `dnd2024.healing.received` with `requestedAmount`, `appliedAmount`, `lostToMaximum`, `beforeCurrent`, `afterCurrent`, and `maximum`. |
| Exhaustion event | Feature 14 Slice 1 (blocked, planned) registers `dnd2024.exhaustion.reached-lethal` with `creatureId`, `level` (const 6), and `sourceRef`. |
| Reaction constraints | `procedure.subscription.create` requires that a subscribed mechanic "declare no child mechanics". `procedure.event.react` says a reaction's effects are proposed like any other change, face the same guards, and count against the chain budget one level deeper; any failure aborts the entire root change; notifications are rejected as unavailable. |
| Guard constraints | `procedure.event.guard`: a guard reads one immutable proposed structural event after its batch has been applied inside an uncommitted transaction, returns exactly allow or deny with a code and reason, cannot return effects, fails closed, and short-circuits on the first deterministic deny. |
| Chain budget | `procedure.event.chain-limits` must be read before setting `maxExecutionsPerChain` above 1. |

## Verified existing dependencies

| Dependency | Evidence |
| --- | --- |
| Source registry | `catalog/world/entities/source.dnd2024.srd-5.2.1.json`. |
| Event infrastructure | E1 verified across six slices: guards, subscriptions, deterministic chains, causation ids, and ledger readback through `query(kind: "events")`. |
| Seeded d20 rolling | `mechanic.dnd2024.check.ability` v4 and `mechanic.dnd2024.saving-throw` both roll `ctx.randomInt(1, 20)` and report ordered rolls, selected die, and seed; Features 3 and 4 exit gates cover replay. |
| Bounded Hit Point state | Feature 6 Slice 2 verified. |
| Transactional damage | Feature 9 Slice 2 verified: one `component.set`, clamping, atomicity, 3/3 focused and 302/302 full at the time. |
| Closed-writer pattern | Features 6 and 7 `write` mechanics. |

## Recursive dependency analysis

```text
Feature 17: dying, death saves, stabilization and death
├─ SRD dropping-to-0 rules                                        [implemented source basis]
├─ event guards, subscriptions, deterministic chains              [implemented: E1]
├─ seeded d20 rolling and replay                                  [implemented: Features 3-4]
├─ bounded Hit Point state and transactional change               [implemented: Features 6, 9]
├─ conditions, incl. Unconscious and its implications             [BLOCKED: Feature 13]
├─ mitigated damage + dnd2024.damage.dealt with overkill          [BLOCKED: Feature 15, Slice 4]
├─ healing + dnd2024.healing.received                             [BLOCKED: Feature 16, Slice 2]
├─ temporary Hit Points absorbing before overkill                 [BLOCKED: Feature 16, Slice 3]
├─ dnd2024.exhaustion.reached-lethal                              [BLOCKED: Feature 14, Slice 1]
└─ dying as enforced, automatic state                             [blocked parent]
   ├─ zero-Hit-Point policy + writer                               [missing leaf: Slice 1]
   ├─ death-state component + administrative writer                [blocked: Slice 2]
   ├─ condition-integrity guard                                    [blocked: Slice 3]
   ├─ dropping to 0 as a damage reaction                           [blocked: Slice 4]
   ├─ the death saving throw                                       [blocked: Slice 5]
   ├─ leaving the dying state                                      [blocked: Slice 6]
   └─ death from Exhaustion                                        [blocked: Slice 7]
```

Slice 1 is the hidden dependency this plan exists to have found. Slice 3 is the second: without it,
Slice 4's reaction would have to reimplement Feature 13's condition-list rules, which the guide's
red-flag list forbids.

## Dependency and ownership decisions

1. **A zero-Hit-Point policy is the real missing dependency, and it is minimal.**
   `dnd2024.zero-hit-points-policy` is a closed component holding exactly
   `{policy: "death-saves"|"die-at-zero", sourceRef}`. The SRD normally gives a character the first
   policy and a monster the second, while allowing the GM to treat an individual monster like a
   character; the durable fact is therefore the policy, not a misleading intrinsic
   `character|monster` identity. It is not a stat block, species, class, or player-account link.

   **Absence is a hard failure, not a default.** A creature at 0 Hit Points with no policy fails the
   reaction and therefore the entire damage transaction. Defaulting would silently grant or remove
   death saves. Slice 1's contract records the policy for combat fixtures; Feature 35 may supersede
   it with richer creature data under an explicit migration boundary.

2. **Death-save state is its own component, not a field on Hit Points.** `dnd2024.hit-points` is a
   closed three-field component with a fixed locator, verified by Feature 6 and consumed by
   Features 8, 9, 15, and 16. `dnd2024.death-state` holds `successes`, `failures`, `stable`, `dead`,
   and `sourceRef`. It is present only while the creature is dying, stable, or dead, and is removed
   when the state ends — the same absence-means-none discipline Feature 16 uses for the temporary
   buffer.

3. **The death saving throw is a separate mechanic from `mechanic.dnd2024.saving-throw`.** It shares
   the word "saving throw" and nothing else: no ability, no modifier, no Proficiency Bonus, no
   caller DC, and two natural-roll rules the ordinary save explicitly does not have
   (`procedure.mechanic.dnd2024.saving-throw` states "Natural 1 and 20 have no automatic saving-throw
   outcome"). Its contract also already says death saves need a separate contract. Routing must keep
   them apart, and "make a death saving throw" versus "make a saving throw" is the tightest phrase
   collision in the ruleset — it gets its own matrix rows.

4. **Consequences are reactions to declared events, not revisions of the damage parent.** This is
   why Features 14, 15, and 16 each declare an event. Feature 17 registers subscriptions and writes
   no line of the damage path. `procedure.event.react` guarantees a reaction's effects join the same
   transaction as the change that caused it, so a creature never exists in a committed state where
   it is at 0 Hit Points and not yet Unconscious.

5. **A reaction cannot compose a child, so the condition list needs a guard.** `procedure.subscription.create`
   forbids a subscribed mechanic from declaring child mechanics, so Slice 4's reaction cannot reuse
   `mechanic.dnd2024.conditions.write`. Three options were considered:
   - *Duplicate Feature 13's list arithmetic inside the reaction.* Rejected: two implementations of
     one rule, which is the guide's red flag.
   - *Relax the kernel's no-children rule for reactions.* Rejected for now: it is a
     `procedure.system.modify` change to a verified E1 constraint, made to serve one caller.
   - *Register a guard that validates any proposed change to `dnd2024.conditions`, whatever wrote
     it.* **Chosen.** The guard becomes the single validator of the invariant — closed vocabulary,
     uniqueness, canonical order, Exhaustion level bounds, correct `sourceRef` — and both the
     Feature 13 writer and this feature's reaction must satisfy it. Duplication becomes enforcement,
     and every future writer of that component is covered for free.

   The guard fails closed, denies with a code and reason, and short-circuits, all of which
   `procedure.event.guard` already guarantees. It is Slice 3, before the first non-writer path
   exists — not after.

6. **Instant death is computed from `overkill`, which is why Feature 15 declares it.** The rule is
   "remaining damage equals or exceeds the Hit Point maximum". After application, `current` is
   clamped at 0 and the remainder is gone from the world; only the event carries it. Feature 16
   Slice 3 further requires that `overkill` be computed *after* temporary absorption, or a buffered
   creature would die from a blow it survived. Both of those are recorded in the producing plans
   precisely so this feature needs no re-plumbing.

7. **The two zero-Hit-Point policies are branches of one reaction.** Splitting them
   into two subscriptions on one event would mean two rules racing over one creature with an
   ordering tiebreak deciding the outcome. One reaction, one branch on policy, one deterministic
   answer.

8. **Death saves are automatic at turn start.** A new `dnd2024.turn.started` event is emitted by
   the Feature 11 start/advance transitions after they select and restore the active participant.
   The Feature 17 death-save reaction consumes that event and owns the random roll and state change.
   The transition itself remains deterministic; the subscribed reaction is the one rule that rolls.
   This produces no GM-reminder workaround and avoids trying to run a child for every roster member
   before the parent knows which one is active.

9. **Death is terminal within this feature.** `dead: true` is never reversed by anything here. The
   death-state writer refuses to clear it, the healing reaction refuses to revive, and resurrection
   is excluded. A later feature that adds it must own the reversal explicitly.

## Slice order and stop gates

| Order | Slice | Starts only when | Exit gate |
| --- | --- | --- | --- |
| 1 | Zero-Hit-Point policy and its writer | Feature 6 verified; plan reviewed; clean `roleplay validate catalog` | Every combat fixture carries a valid zero-Hit-Point policy; the writer records and corrects it; absence, unknown values, and corrupt state are rejected without state change. |
| 2 | Death-state component and its administrative writer | Slice 1 verified | Death-save state can be created, corrected, and removed through one closed writer, with tally bounds, terminal death, and corrupt cases rejected. |
| 3 | Condition-integrity guard | Slice 2 plus Features 13 and 14 verified | Every proposed change to `dnd2024.conditions` is validated by one guard regardless of its writer; a hand-authored invalid list is denied and rolls back the whole root change; every Feature 13 and 14 acceptance row still passes. |
| 4 | Dropping to 0 as a reaction to damage | Slice 3 and Feature 15 Slice 4 verified | Damage that reduces a death-saves-policy creature to 0 makes it Unconscious and dying in the same transaction; massive damage kills it; a die-at-zero-policy creature dies; damage while dying adds the right number of failures. |
| 5 | Automatic death saving throw at turn start | Slice 4, Feature 11, and the confirmed turn-start/healing-request bridges verified | A dying creature's turn starts exactly one death save; it tallies correctly, kills on the third failure, stabilizes on the third success, requests one Hit Point on a natural 20, and takes two failures on a natural 1. |
| 6 | Leaving the dying state | Slice 5 and Feature 16's confirmed healing-request bridge verified | Regaining any Hit Points ends Unconscious and clears the death state in the same transaction; explicit stabilization sets Stable and stops death saves; a Stable creature that takes damage resumes them. |
| 7 | Death from Exhaustion | Slice 6 and Feature 14 Slice 1 verified | Reaching Exhaustion level 6 marks the creature dead through the same terminal transition, with no Hit Point change and no death saves. |

## Slice 1 — zero-Hit-Point policy and its writer

### Runtime artifacts

| Artifact | Proposed ID / category | Change |
| --- | --- | --- |
| Governing contract | `procedure.mechanic.dnd2024.zero-hit-points-policy` in `ruleset.dnd2024.core.data.zero-hit-points-policy` | New. Records the removal criterion naming Feature 35. |
| Component definition and schema | `dnd2024.zero-hit-points-policy` | New closed creature-owned component. |
| Writer | `mechanic.dnd2024.zero-hit-points-policy.write` in the same category, scope `dnd2024-srd-5.2.1` | New, with `record` and `correct` modes. |
| Catalog fixtures | `creature.dnd2024.feature-10.hero.json`, `creature.dnd2024.feature-10.training-target.json` | **Revised** to carry `death-saves` and `die-at-zero` policies respectively. |
| Regression coverage | `CatalogFeature17Tests` | New fresh-import coverage. |

### Governing contracts and source locator

Before writing, re-read `procedure.system.create-feature`, `procedure.mechanic.dnd2024.hit-points`
(the closed-writer pattern), `procedure.world.model` and `procedure.world.naming` (this component
makes a claim about what an entity *is*, which is the world model's territory),
`procedure.mechanic.run`, `procedure.mechanic.projection`, and `procedure.world.change`. Re-search
`zero hit point policy`, `monster`, `character`, `player character`, `npc`, and `creature type`
against the authored catalog — and confirm that nothing in Features 26,
27, or 35's planning material has since claimed this ground.

`sourceRef` is fixed to
`{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Playing the Game > Damage and Healing > Dropping to 0 Hit Points"}`.
The locator is the rule that makes the distinction matter, which is the honest citation; the SRD
does not have a "creature kind" section.

### Data/input contract and required state

- The component is closed with exactly `policy` and `sourceRef`. `policy` is exactly `death-saves`
  or `die-at-zero`.
- Writer input is exactly `{"mode":"record"|"correct","policy":"death-saves"|"die-at-zero"}`.
- One **required** role `subject`, declaring `dnd2024.zero-hit-points-policy`. As elsewhere in this block,
  "optional component" is not a kernel concept: `RoleRequirement.Optional` is a role-level flag, and
  a declared component the entity lacks is simply absent from the projection without failing it. The
  role stays required and the mechanic branches on absence.
- `record` requires absence and applies one `component.add`; `correct` requires a valid existing
  component and applies one `component.set`.
- Rejected before any effect: unknown or wrong-case `policy`, missing `policy`, non-string `policy`,
  non-object root, extra keys, and every caller-supplied `sourceRef`, species, class, stat block,
  `dead`, or `effects` field.

### Recording behavior

1. Validate closed input, then the existing component for `correct`.
2. Reject before constructing an effect. No randomness is consumed.
3. Propose exactly one effect carrying the complete two-field object.
4. Return mode, `previousPolicy` (null for `record`), `policy`, and the source reference.

### Invariants, failure behavior, and non-goals

- One policy per creature; `record` never overwrites, `correct` never creates.
- The writer changes no other component and no other entity, and declares no event.
- The component asserts only which death rule applies. It is not a creature type, not a species,
  not a monster stat block, and not a player-account association. Feature 35 may supersede it, and
  the contract says so.
- The two Feature 10 combat fixtures gain the component in this slice, which is a fixture revision
  rather than a behavior change; their existing acceptance rows must still pass unchanged.

### Slice 1 implementation sequence

1. Confirm Feature 6 is verified and the plan's policy boundary has been reviewed; record a clean
   `roleplay validate catalog` baseline.
2. Re-read the listed contracts; repeat overlap searches, including against later features' planning
   material.
3. Author the contract, component definition and schema, mechanic `.md`/`.js` pair, the two revised
   fixture files, manifest entries, and the focused fresh-import test as catalog files first.
4. Run `roleplay validate catalog`, which imports the authored catalog into a fresh migrated
   disposable database and runs write-side checks without touching the persistent game database.
5. Exercise the acceptance matrix against disposable catalog fixtures in that isolated validation
   run; record the focused-test evidence.
6. Run focused tests, the full suite, and `git diff --check`; record evidence; mark only Slice 1
   verified; stop for review.

### Slice 1 acceptance matrix

| Class | Required assertion |
| --- | --- |
| Happy path | `record death-saves` and `record die-at-zero` each apply one `component.add`; the queried component has exactly two fields with the fixed `sourceRef`. |
| Differential | Two creatures differing only in policy have components differing in exactly that one string. |
| Closed input | `"character"`, `"monster"`, `"Character"`, `"pc"`, `""`, `null`, a number, a missing `policy`, a non-object root, an extra key, and a supplied `sourceRef` or `effects` each fail with zero effects. |
| Missing state | `correct` against an absent component fails with a distinct reason. |
| Existing state | `record` against a present component fails atomically; original bytes unchanged. |
| Corrupt state | A stored unknown `policy`, wrong `sourceRef`, extra field, or malformed JSON is rejected by `correct` before any effect. |
| Fixture migration | Both Feature 10 fixtures query back carrying the expected policy; every Feature 10 acceptance row still passes; `roleplay validate catalog` accepts the intended fixture revisions and nothing else. |
| Determinism | Equivalent databases and input produce byte-identical components; no `ctx.randomInt` call. |
| Routing | `record zero Hit Point policy` selects only this writer; `record hit points`, `apply the poisoned condition`, `make a saving throw`, and `heal the creature` must not select it. |
| Effects | Exactly one effect per success; zero on every rejection. |
| State integrity | Before/after byte comparison on the subject and one untouched sibling for every rejection. |
| Readback | Contract, definition, schema, mechanic, and both fixtures queried back at intended versions. |
| Restoration | Disposable creatures deleted through validated effects; absence queried. |
| Repository | Import dry-run/import/verify clean; focused and full suites pass; `git diff --check` passes. |

### Slice 1 exit gate

Every row passes with recorded operation ids, mechanic id and version, parsed result fields, exact
effect counts, fixture readback, before/after bytes, cleanup evidence, and repository checks. Slice
2 stays blocked until a new review authorizes it.

## Slice 2 — death-state component and its administrative writer

### Status and prerequisite

Blocked until Slice 1 is verified. Adds `procedure.mechanic.dnd2024.death-state`, the
`dnd2024.death-state` component and schema, and `mechanic.dnd2024.death-state.write`.

### Data/state and resolution contract

- Closed component: `successes` and `failures`, integers `0..2`; `stable` and `dead`, Booleans; and
  `sourceRef`. **A tally of 3 is never stored**: three successes resolve to `stable: true` with
  tallies reset, and three failures resolve to `dead: true`. Storing 3 would mean a state that both
  is and is not resolved, and every consumer would have to re-decide which.
- The component is present only while the creature is dying, Stable, or dead, and is removed when
  the state ends.
- Writer modes: `begin` (requires absence; adds `{0,0,false,false}`), `correct` (requires a valid
  existing component; sets any legal combination), and `end` (requires presence and `dead: false`;
  removes the component).
- `dead: true` is terminal: `correct` refuses to clear it and `end` refuses to remove a dead
  creature's state.
- `stable: true` with a nonzero tally is rejected as an illegal combination, as is
  `dead: true` with `stable: true`.
- Rejected before any effect: tallies outside `0..2`, fractional or non-integer tallies,
  non-Boolean flags, missing fields, illegal combinations, extra keys, and every caller-supplied
  `sourceRef`, Hit Point, condition, roll, or `effects` field.
- The writer is administrative. It is not the reaction path and not the death-save path; both are
  later slices and neither may be reached through this mechanic's phrases.

### Acceptance and exit gate

Prove: `begin` creates the zeroed state; `correct` reaches every legal combination of tallies 0–2
and the two flags; a tally of 3 or −1 fails; `stable` with a nonzero tally fails; `dead` with
`stable` fails; clearing `dead` fails; `end` on a dead creature fails; `end` on a living one removes
the component and its absence is queried; `begin` against a present component and `correct`/`end`
against an absent one each fail with distinct reasons; corrupt stored data is rejected before any
effect; determinism, effect-exactness, state integrity, routing (against "make a saving throw" and
"make a death saving throw" specifically), readback, cleanup, and repository checks all hold. Slice
3 stays blocked.

## Slice 3 — condition-integrity guard

### Status and prerequisite

Blocked until Slice 2 and Features 13–14 are verified. Adds `procedure.mechanic.dnd2024.conditions.guard`,
`mechanic.dnd2024.conditions.guard` in guard mode, and one subscription. Revises
`procedure.mechanic.dnd2024.conditions` to record that the invariant is now guard-enforced for every
writer. Adds no component.

### Data/state and resolution contract

- Read `procedure.event.guard` and `procedure.event.chain-limits` immediately before writing.
- `requirements.event` declares `mode: "guard"` and the exact structural types
  `world.component.added` and `world.component.replaced`, with `dnd2024.conditions` projected onto
  the entities the event touches.
- The guard allows immediately unless the event concerns `dnd2024.conditions`. When it does, it
  validates the proposed list: closed source-instance entry shape; the Feature 13 vocabulary plus
  `exhaustion`; non-Exhaustion uniqueness by `(condition, sourceEntityId)` and canonical ordering;
  valid referenced source entities; Petrified/Poisoned incompatibility; and exactly zero or one
  source-absent Exhaustion entry with integer `level` 1–6. Every non-Exhaustion entry forbids
  `level`, and the fixed `sourceRef` is required.
- It returns `{decision: "allow"}` or `{decision: "deny", code, reason}` with a code of 3–64
  characters matching the contract's format and a reason at most 500 characters. It returns no
  effects, no events, and no notifications — `procedure.event.guard` forbids all three, and
  `procedure.subscription.create` records that notifications do not exist.
- The subscription binds no fixed roles, filters no tracked entities, uses `maxExecutionsPerChain: 1`,
  and takes an order value reserved in the contract so later guards can be placed relative to it.
- The guard fails closed by construction: any invalid, unavailable, or throwing guard rolls back the
  root transaction, which is the desired behavior for a validator of this component.

### Acceptance and exit gate

Prove: every Feature 13 Slice 1 and Feature 14 Slice 1 acceptance row still passes unchanged with
the guard active — the guard must be invisible to a correct writer, and this is the primary
assertion; a hand-authored `commit(kind: "effects")` proposing an out-of-order list, a duplicate, an
  unknown id, duplicate source instance, forged/missing source entity, incompatible Petrified and
Poisoned, a `level` on a non-Exhaustion entry, a missing `level` on Exhaustion, a `level` of 0 or
7, or a wrong `sourceRef` is each denied with its specific code and rolls back the whole root
change including any unrelated effect in the same batch; a valid hand-authored list is allowed; the
guard is not consulted for changes to other components; denial short-circuits deterministically; the
subscription is dry-run before enabling and queried back; the event ledger and execution rows show
what ran; replay is exact; the full suite and `roleplay validate catalog` pass. Slice 4 stays blocked.

## Slice 4 — dropping to 0 as a reaction to damage

### Status and prerequisite

Blocked until Slice 3 and Feature 15 Slice 4 are verified. Adds `procedure.mechanic.dnd2024.dying`,
`mechanic.dnd2024.dying.on-damage` in reaction mode, and one subscription to
`dnd2024.damage.dealt`. Revises no existing mechanic.

### Data/state and resolution contract

- `requirements.event` declares `mode: "reaction"` and the exact type `dnd2024.damage.dealt`, with
  `dnd2024.hit-points`, `dnd2024.zero-hit-points-policy`, `dnd2024.death-state`, and `dnd2024.conditions`
  projected onto the entities the event touches. The event names the target in `entityIds`, so the
  target is in `ctx.eventEntities`.
- Branch order, evaluated on `ctx.event.payload`:
  1. `finalAmount === 0` → no change. A mitigation- or buffer-absorbed event is an audit fact, not
     damage that can start or advance dying.
  2. `afterCurrent > 0` → no change. Return `{effects: []}` with narration. A consulted rule with
     nothing to do is an explicit legitimate outcome under `procedure.event.react`.
  3. Target has no `dnd2024.zero-hit-points-policy` → **fail**, aborting the whole root change. Per decision
     1, this is deliberate.
  4. `policy === "die-at-zero"` → the creature dies and gains no Unconscious condition.
  5. `overkill >= maximum` → instant death: same terminal transition for a death-saves creature.
  6. `beforeCurrent === 0` and the creature already has `dnd2024.death-state` → damage while dying:
     add 1 failure, or 2 if `critical`. Three or more resolves to
     `dead: true`. A Stable creature also loses `stable`.
  7. Otherwise → falling unconscious: a `begin`-equivalent zeroed `dnd2024.death-state`, and the
     Unconscious condition written to `dnd2024.conditions`, which the Slice 3 guard validates.

- **The condition effect's type depends on whether the component exists, and the branch must say
  so.** Feature 13 makes `record` the only creator of `dnd2024.conditions` and requires the
  component to exist for `apply`, and a reaction cannot compose that writer. So this reaction
  proposes `component.add` with the full Feature 13 shape —
  `{"entries":[{"condition":"unconscious"}],"sourceRef":...}` — when the component is absent,
  and `component.set` with the re-sorted source-instance list when it is present — `ComponentAdd` faults on a present
  pair and `ComponentRemove` on an absent one, but `ComponentSet` is an upsert, so choosing wrongly
  either faults or silently discards existing conditions. Both forms face the Slice 3 guard, which
  is what makes either safe.

  Unlike the missing zero-Hit-Point policy in branch 3, an absent condition component is **not** a failure. A
  creature that has never had a condition is an ordinary creature, and the SRD gives it the
  Unconscious condition all the same; failing would abort a legitimate damage transaction. The two
  absences are treated differently on purpose: a missing policy means the system does not know
  which death rule applies, while a missing condition list means the creature has no conditions.
- Every branch proposes effects that face the same guards at their own depth and count against the
  chain budget, per `procedure.event.react`. The reaction reads `.before` and `.after` from the
  payload rather than from `eventEntities`, because `eventEntities` shows the world as it now stands
  and only the payload says what it stood at.
- The reaction declares no event of its own in this slice, returns no notification, and never heals,
  never rolls, and never touches the turn budget or Initiative order.

### Acceptance and exit gate

Prove each branch with exact effect counts and resulting state, in one transaction with the damage
that caused it: damage leaving a death-saves-policy creature above 0 changes nothing; a death-saves-policy creature reduced to exactly 0
becomes Unconscious with a zeroed death state; a death-saves-policy creature reduced to 0 with `overkill >= maximum`
is dead and gets no death state tallies; `overkill === maximum - 1` is *not* instant death, the
boundary that most implementations get wrong; a die-at-zero-policy creature reduced to 0 is dead
without Unconscious; a creature with no policy aborts the whole damage transaction with Hit Points
byte-identical; **a death-saves-policy creature with no condition component gains one through
`component.add` carrying the full source-aware Unconscious component, and a creature with an
existing condition list gains it through `component.set` with the list correctly
re-sorted and its other conditions preserved** — both are required rows, and the wrong effect type in
either direction is what they exist to catch; a dying creature
  taking non-critical damage gains one failure and a critical gains two; a third failure resolves to
dead; a Stable creature taking damage loses `stable` and resumes; a temporary buffer that absorbs
the whole blow leaves everything untouched, which proves Feature 16's ordering; the Unconscious
condition passes the Slice 3 guard in both effect forms; the chain's causation ids read back as expected through
`query(kind: "events")`; a reaction failure rolls back the damage with no execution row retained;
replay from the same root seed is exact; every Feature 9, 15, and 16 acceptance row still passes;
the subscription is dry-run, enabled, and queried back; disposable fixtures are deleted. Full suite,
`roleplay validate catalog`, `git diff --check`. Slice 5 stays blocked.

## Slice 5 — automatic death saving throw at turn start

### Status and prerequisite

Blocked until Slice 4 is verified and the two confirmation-boundary bridges below are approved.
Revises the Feature 11 turn-start contracts and transitions to
declare `dnd2024.turn.started`, registers its event type, and adds
`mechanic.dnd2024.death-save.on-turn-start` in reaction mode with a subscription to that event.

### Data/state and resolution contract

- The reaction reads the active participant from `ctx.event`; it has no action input. Its event
  entity declares `dnd2024.hit-points`, `dnd2024.death-state`, and `dnd2024.conditions`.

**Which modifiers apply, and an unresolved composition boundary.** A death saving throw takes no ability modifier and no
Proficiency Bonus; it is a flat d20 against 10. But in the 2024 rules a saving throw **is** a D20
Test, and Exhaustion reduces every D20 Test by `2 × level` — which Feature 14's own target
capability restates as applying to every D20 Test the creature makes. The design here is therefore:
use Feature 13's `derivedModifiers` result only (that is, Exhaustion), and no ability modifier,
Proficiency Bonus, or condition circumstance. The SRD question is settled: the Exhaustion penalty
applies because a death save is a D20 Test.

The current subscription contract prohibits child mechanics, so this reaction cannot compose the
state-effects resolver. It must **not** silently recalculate Feature 14's penalty in a second owner.
This slice therefore waits for one approved bridge: either a narrowly scoped kernel change allowing
a reaction to compose this static no-input resolver, or a new producer-owned event/projection contract
that exposes the subject's already-derived D20 modifiers to a reaction. The plan recommends the
former only if it can be made generally safe and independently tested; no bridge is created by this
feature plan.
- Validate before rolling: complete Hit Point state with `current === 0`; complete death state;
  `dead: false`; `stable: false`. A creature that is stable, dead, or above 0 fails with a distinct
  reason and consumes no randomness.
- Roll exactly one `ctx.randomInt(1, 20)`. Natural 20 → the creature regains 1 Hit Point and the
  death state is removed. Natural 1 → two failures. Otherwise 10 or higher is one success and 9 or
  lower is one failure.
- Third success → `stable: true` with tallies reset to zero. Third failure → `dead: true`.
- **The natural-20 branch requests healing; it never writes Hit Points or forges a receipt.** It
  declares `dnd2024.healing.requested` with `{targetId, amount:1, cause:"death-save-natural-20"}`.
  Feature 16 must provide an approved subscribed healing-owner bridge that applies the bounded Hit Point
  effect and emits `dnd2024.healing.received`; the latter then triggers this plan's leaving-dying
  reaction. This is a required revision to Feature 16's plan before Slice 5/6 is authorized, not a
  reason to make a second healing writer here.
- Effect counts per branch are fixed by the contract and asserted exactly: one effect for an
  ordinary success or failure (the death-state change); for a natural 20, zero direct effects and
  one declared request event, followed by the Feature 16 healing-owner reaction in the same chain.
- **Feature 13's condition circumstances are deliberately not consulted**, because no SRD condition
  grants Advantage or Disadvantage on a death save. Feature 14's Exhaustion modifier is consulted
  through the approved bridge only.
- The result reports the roll, the outcome, before and after tallies, whether the creature became
  stable or dead or regained a Hit Point, and the source locator.

### Acceptance and exit gate

Prove with fixed seeds: rolls of 10 and above succeed and 9 and below fail, tested at exactly 9, 10,
and 11; a natural 20 declares exactly one healing request, regains exactly 1 Hit Point through the
Feature 16 owner, and never writes Hit Points or a forged `healing.received` receipt itself; a natural 1 adds two failures and
resolves to dead when the tally was 1 or 2; three non-consecutive successes stabilize with tallies
reset; three failures kill; a stable, dead, or above-0 creature is rejected with no randomness
consumed, verified by seed-advance comparison; a **Poisoned** dying creature's roll and total are
byte-identical to an unconditioned one for the same seed, since no condition grants a circumstance
here; an **Exhausted level 1–5** dying creature's total is reduced by exactly `2 × level` while its
raw roll is unchanged — and level 6 is not a test case, because a level-6 creature is already dead;
closed input rejects `ability`, `dc`, `rollCircumstances`,
`voluntaryFailure`, and every extra key; effect counts per branch are exact; replay is exact;
readback confirms both approved bridge contracts and routing distinguishes the automatic turn-start
subscription from an ordinary saving throw; replay, readback, and cleanup hold. Full suite, verify,
diff-check. Slice 6 stays
blocked.

## Slice 6 — leaving the dying state

### Status and prerequisite

Blocked until Slice 5 and Feature 16's approved healing-request bridge are verified. Revises `procedure.mechanic.dnd2024.dying`; adds
`mechanic.dnd2024.dying.on-healing` in reaction mode with a subscription to
`dnd2024.healing.received`, and `mechanic.dnd2024.dying.stabilize`.

### Data/state and resolution contract

- The healing reaction fires on `dnd2024.healing.received`, projects the same four components, and
  acts only when `appliedAmount > 0` and the target has a death state. It removes the death state,
  removes the Unconscious condition through a guard-validated change, and never revives a creature
  with `dead: true` — a healing event on a dead creature is denied, aborting the root change, so a
  GM cannot half-resurrect by accident. `appliedAmount === 0` changes nothing and returns an empty
  effect list.
- `mechanic.dnd2024.dying.stabilize` takes input exactly `{}`, requires a subject at 0 Hit Points
  with a death state that is neither stable nor dead, and sets `stable: true` with tallies reset. It
  does **not** roll the DC 10 Wisdom (Medicine) check — that runs through
  `mechanic.dnd2024.check.ability` with `skill: "medicine"`, and the caller stabilizes after
  succeeding. Building the check into this mechanic would duplicate a verified resolver and hide
  which creature made it.
- A Stable creature keeps the Unconscious condition and stays at 0 Hit Points. Only regaining a Hit
  Point ends Unconscious.

### Acceptance and exit gate

Prove: healing a dying creature by 1 removes the death state and Unconscious in the same
transaction as the Hit Point change; healing by 0 changes nothing; healing a dead creature is denied
and rolls the whole change back; healing a creature that was never dying changes nothing;
stabilization sets `stable` with tallies reset and leaves Hit Points and Unconscious unchanged; a
stable creature is rejected by the death-save mechanic; a stable creature taking damage resumes
death saves per Slice 4; stabilizing a dead, healthy, or already-stable creature fails with distinct
reasons; the DC 10 Medicine check is proven to route to the existing ability-check resolver and not
to this mechanic; chain causation ids read back as expected; every Feature 16 acceptance row still
passes; replay, routing, effect-exactness, readback, and cleanup hold. Full suite, verify,
diff-check. Slice 7 stays blocked.

## Slice 7 — death from Exhaustion

### Status and prerequisite

Blocked until Slice 6 and Feature 14 Slice 1 are verified. Revises `procedure.mechanic.dnd2024.dying`; adds
`mechanic.dnd2024.dying.on-exhaustion` in reaction mode with a subscription to
`dnd2024.exhaustion.reached-lethal`. Adds no component and no new state.

### Data/state and resolution contract

- The reaction projects `dnd2024.death-state` onto the creature the
  event names, and marks it dead through the same terminal transition Slice 4 uses — one owner for
  "becomes dead", reached from two causes.
- It changes no Hit Points, adds no Unconscious condition, starts no death saves, and applies to
  every creature, since the SRD's Exhaustion rule does not distinguish zero-Hit-Point policies.
- A creature already dead is a no-op returning an empty effect list rather than a failure: the event
  is a true statement either way, and failing would abort an otherwise valid Exhaustion write.

### Acceptance and exit gate

Prove: reaching level 6 marks the creature dead in the same transaction as the Exhaustion write,
with Hit Points byte-identical and no death state tallies created; a creature already dying becomes
dead and its tallies are resolved consistently with Slice 4's terminal transition; a creature
already dead is unchanged with zero effects; reaching level 5 fires nothing; recovering from 6 to 5
does not revive; the subscription is dry-run, enabled, and queried back; the chain's causation ids
read back as expected; every Feature 14 acceptance row still passes; replay, effect-exactness,
readback, and cleanup hold. Full suite, `roleplay validate catalog`, `git diff --check`.

Feature 17 is verified only after the Slice 7 gate passes and this plan records evidence; then stop
before Feature 18.

## Confirmation boundary before Slice 5

Slices 1–4 and 6–7 have identified contracts and can be reviewed independently. Slice 5 cannot be
authorized from the current catalog because it requires two new cross-feature public contracts:

1. `dnd2024.turn.started`, emitted once by Feature 11 after the active participant is selected or
   restored, with an exact active-participant payload suitable for an event reaction.
2. `dnd2024.healing.requested`, emitted by a rule requesting bounded healing and consumed by a
   Feature 16-owned reaction that applies Hit Points and then emits the existing
   `dnd2024.healing.received` receipt.

It also needs a decision on how an automatic reaction obtains Feature 14's `derivedModifiers`
without duplicating the Exhaustion formula. The recommended choice is a narrowly tested, general
extension that permits a reaction to compose a declared static no-input child; it must preserve
event ordering, replay seeding, chain limits, and atomic rollback. The alternative is a new
producer-owned derived-modifier projection/event contract. Do not implement any of these permanent
ids or alter the reaction-child prohibition without explicit approval and a separate kernel plan.

## Forward dependencies this plan deliberately leaves open

| Concern | Owner | Note |
| --- | --- | --- |
| Rolling a death save automatically at turn start | Feature 11 plus approved event/reaction bridge | Requires the confirmed `dnd2024.turn.started` contract and a way for a reaction to consume Feature 14's derived modifier result without reimplementing it. |
| Regaining a Hit Point on a natural 20 | Feature 16's healing owner | Requires the confirmed `dnd2024.healing.requested` bridge; Feature 17 requests healing and never writes Hit Points or `healing.received` itself. |
| A Stable creature regaining 1 Hit Point after 1d4 hours | Feature 37 | Needs elapsed time, which Tier E's non-goals exclude. |
| Resurrection and any reversal of `dead: true` | Out of scope in Tier F and Tier H | A later feature must own the reversal explicitly; nothing here can clear it. |
| Reaching the dying creature to stabilize it | Feature 20 | Feature 17 owns the transition, not who can reach whom. |
| Creature data replacing the zero-Hit-Point policy | Feature 35 | The removal criterion is recorded in Slice 1's contract. |
| Concentration ending on Incapacitated or death | Feature 18 | Nothing to break yet. |

## Plan-quality audit

1. Yes — one capability, dying as automatic enforced state, with resurrection, timing, and creature
   data explicitly excluded and assigned.
2. Partly — the source entity and seven headings are concrete and PDF pages 17–18 are evidenced;
   the two new cross-feature event contracts and the reaction-composition decision are explicitly
   held at a confirmation boundary rather than guessed.
3. Yes — dying, death, unconscious, death save, stabilize, monster, and character were all searched;
   four existing contracts were found to have disclaimed ownership in writing, and one — the saving
   throw contract — names death saves as needing a separate contract.
4. Partly — E1, Feature 3/4/6/9 rows cite verified gates; the Feature 13–16 rows cite unimplemented
   plans, which is why this feature is blocked rather than ready.
5. Yes — every missing dependency was expanded, and expanding them found **two hidden leaves** that
   no roadmap row mentions: the zero-Hit-Point policy and the condition-integrity guard.
6. Yes — zero-Hit-Point policy, death-save state, condition list integrity, the damage cause, the healing cause, the
   Exhaustion cause, the roll, and the terminal transition each have one named owner.
7. Yes — each state slice lands with its only safe write path; each reaction slice lands with its
   subscription; every slice leaves a system that can be stopped at.
8. Yes — Slice 1 alone is named as next; Slice 5 is explicitly held pending confirmation.
9. Yes — absence of the policy as a hard failure, absence of the death state as "not dying",
   the never-stored tally of 3, and terminal death are all explicit.
10. Yes — every branch order, boundary, effect count, and result field is testable without guessing;
    the `overkill === maximum - 1` boundary is called out by name.
11. Yes — the matrix covers every class. **Natural rolls** get their own rows in Slice 5 with a
    counterexample target number; **determinism** is asserted by seed-advance comparison for every
    no-roll rejection; **differential** distinguishes Poisoned from Exhaustion.
12. Yes — disposable validation, subscription dry-run, event-ledger readback, and catalog
    validation sequencing are stated per slice.
13. Yes — disposable fixture isolation and baseline preservation are explicit, and the Feature 10
    fixture revision in Slice 1 is called out as a deliberate, gated change.
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

- A source correction changes the damage-at-0 failure count, reset rule for becoming Stable,
  die-at-zero policy, or Exhaustion's D20 Test penalty.
- Feature 15 or Feature 16 ships a `dnd2024.damage.dealt` payload without `overkill` computed after
  temporary absorption. Slice 4 branches 4 and 5 are unimplementable without it, and the correct
  response is to descend into revising the producer, not to approximate the value here.
- Feature 13 ships a condition component whose invariant the guard in Slice 3 cannot express, or the
  kernel turns out to run guards in a way that makes a per-component validator impractical. Then
  decision 5's rejected options must be reconsidered on their merits, including the kernel change.
- Feature 35's planning claims the zero-Hit-Point-policy ground before Slice 1 runs.
- A catalog search finds any existing dying, death, or zero-Hit-Point-policy owner.

Descend to a new dependency rather than duplicating condition-list validation inside a reaction,
defaulting an absent zero-Hit-Point policy, storing a tally of 3, computing instant death from component
state instead of the event, building the Medicine check into the stabilize mechanic, or bundling
Feature 18's concentration into this feature.
