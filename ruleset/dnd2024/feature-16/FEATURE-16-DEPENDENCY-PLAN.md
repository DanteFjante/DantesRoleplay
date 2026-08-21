# Feature 16 dependency plan — Temporary Hit Points and healing

Status: **Planned; Slices 1 and 2 are independent passes on verified Feature 6 and E1. Slice 3 is
blocked until both pass and Feature 15 Slice 4 is verified**
Last updated: 2026-08-20

## Execution rule

Planning-only artifact under `AGENTS.md` and the active `procedure.system.create-feature`. Repository
catalog files are the development authority. Each implementation pass completes one lowest slice,
validates the catalog in a fresh disposable database, records objective evidence, and stops for
review. A persistent catalog import belongs only to an explicit integration-play or release
boundary. This plan creates no procedure, component, mechanic, fixture, or game state.

## Target capability

A creature can be granted a buffer of Temporary Hit Points that absorbs damage before its real Hit
Points do and refuses to stack with an existing buffer, and it can be healed back up to — never
past — its Hit Point maximum, with both changes audited and announced.

### A boundary question, answered

Phase 1 of the planning guide asks whether two outcomes joined by "and" are one feature. Temporary
Hit Points and healing are close to separable: healing never touches the temporary buffer, and the
buffer never heals anything. They stay one feature for two reasons that are worth writing down
rather than assuming. First, both exist only because `dnd2024.hit-points` has a `maximum` and a
clamp, and both are defined in the SRD by what they are *not* — "Temporary Hit Points aren't
healing" is a rule that needs both halves present to be testable. Second, Feature 17 needs both:
regaining Hit Points ends the dying state, and a temporary buffer that absorbs a killing blow must
not. Splitting them would put half of Feature 17's precondition in a renumbered feature.

They remain **separate slices with independent exit gates**, so the pass that ships one is not
authorized to ship the other.

### Included

- One creature-owned Temporary Hit Point component, present only when a buffer exists.
- A grant transition implementing the SRD's no-stacking choice, and an explicit expiry transition.
- Healing as its own owner: raise current Hit Points, never above maximum, never below.
- Revision of the existing damage path so the buffer absorbs damage first.
- Registered events for both healing received and the revised damage record.

### Excluded

- **Every source of healing and of Temporary Hit Points.** Potions, spells, class features, and
  rests all *call* these transitions; none is modeled here (Features 27, 29, 31, 32, 33).
- Expiry timing. The SRD ends a temporary buffer after a Long Rest; rests are Feature 33. Feature 16
  supplies the explicit expiry transition Feature 33 will call, and no clock.
- Healing over time, regeneration, and timed effects — there is no scheduler and Tier E's non-goals
  say there will not be one.
- Every consequence of reaching or leaving 0 Hit Points: unconsciousness, death saves, stabilizing,
  and the reset of death-save tallies on regaining Hit Points (Feature 17, which subscribes to this
  feature's healing event).
- Hit Dice, maximum Hit Point changes, and Constitution-driven recalculation (Features 27 and 33).
- Damage mitigation arithmetic, which stays entirely inside Feature 15's resolver.

## Official source basis

`source.dnd2024.srd-5.2.1`, *System Reference Document 5.2.1* (2025-05-01, CC-BY-4.0), all within
`Playing the Game > Damage and Healing` (PDF p. 17):

- `> Healing`: Hit Points can be restored by magic or by a Short or Long Rest. A creature's Hit
  Points cannot exceed its Hit Point maximum, and any excess healing is lost.
- `> Temporary Hit Points`: they are a buffer against damage, are lost first, are not healing, are
  not added to the Hit Point maximum, and **do not stack** — when a creature that already has some
  receives more, it chooses which set to keep rather than adding them. They end after a Long Rest.
- `> Hit Points`: the existing basis of `dnd2024.hit-points`, already cited by Feature 6.

The no-stacking rule is settled: when a creature that already has Temporary Hit Points receives
more, it chooses which set to keep. The grant transition therefore takes an explicit, audited choice
only when a buffer is present.

## Planning inventory and overlap result

| Inquiry | Evidence and conclusion |
| --- | --- |
| Existing temporary Hit Point owner | Nothing in `catalog/` implements temporary Hit Points. `dnd2024.hit-points.json`'s own description states the component "stores no damage, healing, temporary Hit Points, resistance, immunity, conditions, or death state", and `procedure.mechanic.dnd2024.hit-points` puts them out of scope in writing. |
| Existing healing owner | None. `procedure.mechanic.dnd2024.hit-points` says explicitly: "This component owns only state, not why it changed. Feature 9 will own damage-caused Hit Point loss; **a future healing feature will own healing-caused increase**." That sentence is this feature's mandate, written by an earlier pass. |
| Hit Point state shape | `dnd2024.hit-points.schema.json`: closed `{current, maximum, sourceRef}`; `current` an integer `0..maximum`; `maximum` a positive safe integer; `sourceRef.locator` a const of `Playing the Game > Damage and Healing > Hit Points`. The writer enforces `current <= maximum` in code because JSON Schema draft 2020-12 has no portable cross-property comparison. |
| Administrative writer | `mechanic.dnd2024.hit-points.write` with `record` and `correct` modes is the administrative path. Its contract says callers "never author source references, deltas, damage types, or effects directly" — a healing mechanic is therefore a *different* owner, not a third mode on the recorder. |
| Damage path | Feature 15 Slice 4 (blocked, planned) leaves `mechanic.dnd2024.weapon-damage.apply` composing `weapon-damage.roll` and `damage.resolve`, applying one `component.set`, and declaring one `dnd2024.damage.dealt` event carrying raw amount, type, mitigation flags, before and after current, maximum, overkill, and critical. |
| Event model | E1 verified. Revising an event type never invalidates an already-recorded event, because payloads validate against the version active at emission — stated in `procedure.event.define`. |

## Verified existing dependencies

| Dependency | Evidence |
| --- | --- |
| Source registry | `catalog/world/entities/source.dnd2024.srd-5.2.1.json`. |
| Hit Point state and its clamp | Feature 6 Slice 2 verified: bounded current/maximum pair, `record`/`correct` discipline, corrupt records rejected rather than repaired. |
| Transactional Hit Point change | Feature 9 Slice 2 verified: one `component.set`, overkill clamped at zero, maximum and `sourceRef` preserved, atomic dry-run/apply, 3/3 focused and 302/302 full at the time. |
| Closed-writer pattern | Features 6 and 7 `write` mechanics. |
| Composition and frozen child results | `procedure.mechanic.projection`; `mechanic.dnd2024.weapon-damage.apply` as the worked example. |
| Event registration and declaration | E1's six verified slices; `procedure.event.define`; `procedure.event.react`. |

## Recursive dependency analysis

```text
Feature 16: Temporary Hit Points and healing
├─ SRD healing and temporary Hit Point rules                       [implemented source basis]
├─ bounded current/maximum Hit Point state                         [implemented: Feature 6]
├─ transactional Hit Point change and clamping                     [implemented: Feature 9]
├─ event type registration and declared events                     [implemented: E1]
├─ mitigated damage path and its damage event                      [BLOCKED: Feature 15, Slice 4]
└─ Hit Points that go up, and a buffer that goes down first        [blocked parent]
   ├─ temporary Hit Point state + grant/expire transitions          [missing leaf: Slice 1]
   ├─ healing owner + healing event                                 [missing leaf: Slice 2]
   └─ the damage path spends the buffer first                       [blocked: Slice 3]
```

Slices 1 and 2 are independent leaves; Slice 3 depends on both, on Feature 15 Slice 4, and on
nothing else. Slice 2 could in principle precede Slice 1, and is ordered second only so that the
component whose absence Slice 3 must tolerate exists first.

## Dependency and ownership decisions

1. **Temporary Hit Points are a separate component, and absence means none.** They are not part of
   `dnd2024.hit-points`. Merging them would change that component's fixed `sourceRef.locator`,
   force a migration of every stored record, and reopen a closed writer that Feature 6 verified.
   `dnd2024.temporary-hit-points` exists **only while a buffer exists**: it is added on grant and
   removed on expiry or exhaustion. There is no `amount: 0` state, because the SRD has no such
   thing — a buffer reduced to nothing is gone.

   This makes missing-versus-empty unusually clean here: absence is the *only* representation of
   "no buffer", so no consumer can confuse them. Every consumer declares the component as optional
   and reports it as absent rather than as zero.

2. **Healing is its own mechanic, not a mode on the recorder.** `mechanic.dnd2024.hit-points.write`
   is administrative — it records and corrects state that a GM asserts. Healing is a game event with
   a cause, a clamp, and a downstream consequence. Its contract already disclaims healing in
   writing. `mechanic.dnd2024.healing.apply` is the owner.

3. **Healing never touches the buffer, and the buffer is never healing.** Healing raises `current`
   and clamps at `maximum`; it does not create, extend, or consume Temporary Hit Points, and it
   does not raise `maximum`. Granting a buffer does not raise `current`. Both directions are
   required negative assertions in their slices' matrices, because conflating them is the single
   most common implementation error in this rule.

4. **The buffer absorbs damage inside the existing damage parent, after mitigation.** Order is
   fixed and testable: roll → mitigate (Feature 15) → spend the buffer → subtract the remainder
   from `current`. Resistance halves the incoming instance before the buffer sees it, which is the
   SRD's "after all other modifiers" rule applied consistently. The damage parent proposes **at
   most two effects** in one transaction — one `component.set` on Hit Points, and one
   `component.set` or `component.remove` on the buffer — and exactly one when no buffer exists.

   The alternative, a separate "spend temporary Hit Points" action the GM must remember to run
   first, was rejected: it is the narrated workaround `ROADMAP.md` already rules out.

5. **Healing declares an event, for the same reason Feature 15's damage does.** Feature 17 must end
   Unconscious and reset death-save tallies when a creature regains Hit Points, and "regained Hit
   Points" is not recoverable from the component alone — a `world.component.replaced` on
   `dnd2024.hit-points` cannot distinguish healing from a GM correction, and the *reason* is exactly
   what Feature 17 needs. `dnd2024.healing.received` carries the target, the requested amount, the
   amount actually applied, the amount lost to the clamp, and before and after `current`.

   This is the third instance of the same lesson, and the plans now state it as a rule: **a feature
   that produces a fact a later feature must react to declares an event for it in the pass that
   produces the fact.** Retrofitting one means revising a verified mechanic and re-running its exit
   gate.

6. **`dnd2024.damage.dealt` is revised, not supplemented.** Slice 3 adds `temporaryBefore`,
   `temporaryAfter`, and `temporaryAbsorbed` to the existing payload. A second event
   (`dnd2024.temporary-hit-points.spent`) would mean a subscriber had to correlate two events to
   answer one question, and `procedure.event.define` guarantees that revising a type leaves already
   recorded events valid against the version they were emitted under.

7. **Overkill is computed after the buffer, and this matters more than it looks.** Feature 15's
   `overkill` field exists so Feature 17 can implement instant death. A buffer that absorbs damage
   must reduce the overkill, or a creature with 5 Temporary Hit Points would die instantly from a
   blow it survived. Slice 3's revised formula is
   `overkill = max(0, finalAmount - temporaryAbsorbed - beforeCurrent)`, and it has its own matrix
   row.

8. **The grant choice is audited, whichever way the SRD settles the blocker.** If it is a caller
   choice, `onExisting` is a legitimate transient input and the result reports which set was kept
   and which was discarded. If the SRD says the higher simply applies, the field is removed and the
   result still reports both values and the outcome. Either way the audit answers "why does this
   creature have 8 and not 12?".

## Slice order and stop gates

| Order | Slice | Starts only when | Exit gate |
| --- | --- | --- | --- |
| 1 | Temporary Hit Point state, grant and expiry | Feature 6 verified; plan reviewed; clean `roleplay validate catalog` | A creature gains a buffer, a second grant resolves the no-stacking rule with both values audited, expiry removes the component, and every boundary, missing, and corrupt case is rejected without state change. |
| 2 | Healing | Feature 6 and E1 verified; plan reviewed | Healing raises current Hit Points, clamps at maximum with the excess reported, never touches maximum or the buffer, and declares exactly one healing event. |
| 3 | The buffer absorbs damage first | Slices 1–2 and Feature 15 Slice 4 verified | A damaged creature with a buffer spends the buffer before its Hit Points, the buffer's removal at exhaustion is exact, overkill accounts for the absorption, and every Feature 9 and Feature 15 acceptance row still passes. |

## Slice 1 — Temporary Hit Point state, grant and expiry

### Runtime artifacts

| Artifact | Proposed ID / category | Change |
| --- | --- | --- |
| Governing contract | `procedure.mechanic.dnd2024.temporary-hit-points` in `ruleset.dnd2024.core.data.temporary-hit-points` | New. |
| Component definition and schema | `dnd2024.temporary-hit-points` | New closed creature-owned component, present only while a buffer exists. |
| Writer | `mechanic.dnd2024.temporary-hit-points.write` in the same category, scope `dnd2024-srd-5.2.1` | New, with modes `grant` and `expire`. |
| Regression coverage | `CatalogFeature16Tests` | New fresh-import coverage. |

### Governing contracts and source locator

Before writing, re-read `procedure.system.create-feature`, `procedure.mechanic.dnd2024.hit-points`,
`procedure.mechanic.run`, `procedure.mechanic.projection`, and `procedure.world.change`. Reconfirm
the Temporary Hit Points text at SRD PDF p. 17. Re-search `temporary hit points`, `temp hp`,
`buffer`, `ward`, `absorb`, `heal`, and `healing` against the authored catalog.

`sourceRef` is fixed to
`{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Playing the Game > Damage and Healing > Temporary Hit Points"}`.

### Data/input contract and required state

- The component is closed with exactly `amount` and `sourceRef`. `amount` is an integer `1..safe`;
  **0 is not representable**.
- `grant` input is exactly `{"mode":"grant","amount":<integer 1..safe>,"onExisting":"keep"|"replace"}`,
  where `onExisting` is required when the component is present and forbidden when it is absent.
  Requiring it only when it is meaningful is what keeps it a decision rather than noise.
- `expire` input is exactly `{"mode":"expire"}` and requires the component to be present.
- One **required** role `subject`, declaring `dnd2024.temporary-hit-points`. Note the kernel's actual
  semantics: `RoleRequirement.Optional` is a role-level flag, and a declared component the entity
  lacks is simply absent from the projection without failing it. There is no "optional component" —
  the role stays required, absence is legal, and the mechanic branches on it explicitly. Every
  reference to a component being optional in this plan means exactly that.
- `grant` with no existing buffer applies one `component.add`.
- `grant` with an existing buffer applies one `component.set` when `onExisting` is `replace`, and
  **exactly zero effects** when it is `keep`. A zero-effect success is a legitimate outcome under
  `procedure.mechanic.run`, and it is the honest representation of "the creature kept what it had".
  It must be asserted as zero effects, not as an unchanged rewrite.
- `expire` applies one `component.remove`.
- Rejected before any effect: `amount` of 0, negative, fractional, non-finite, or above the safe
  integer; a missing `amount` on `grant`; `onExisting` supplied without an existing buffer or
  omitted with one; an unknown `onExisting` value; `expire` with no component; and every
  caller-supplied `sourceRef`, `current`, `maximum`, duration, `effects`, or extra key.

### Recording behavior

1. Validate closed input, then the existing component when present: closed shape, `amount >= 1`,
   integer, and `sourceRef`.
2. Reject before constructing an effect. No randomness is consumed.
3. Propose the single effect the mode requires, or none for a kept buffer.
4. Return mode, `previousAmount` (null when absent), `grantedAmount`, `resultingAmount`, `kept` or
   `replaced`, `discardedAmount`, and the source reference.

### Invariants, failure behavior, and non-goals

- `amount` is never 0 and never negative; the component is absent rather than empty.
- The writer never changes `dnd2024.hit-points`, `maximum`, conditions, or any other entity, and
  declares no event.
- Granting is not healing: `current` is byte-identical before and after every grant, and this is a
  required assertion.
- No duration, expiry time, source-of-grant, or stacking count is stored. Feature 33 calls `expire`;
  it does not read a clock stored here.

### Slice 1 implementation sequence

1. Confirm Feature 6 is verified; record clean focused-test and `roleplay validate catalog`
   baselines.
2. Re-read the listed contracts; repeat overlap and routing searches.
3. Author the contract, component definition and schema, mechanic `.md`/`.js` pair, manifest
   entries, and the focused fresh-import test as catalog files first.
4. Run `roleplay validate catalog`; resolve every schema, write-side, or routing failure in its
   disposable validation database. Do not import into the persistent database.
5. Exercise the acceptance matrix on disposable creatures in fresh test databases without altering
   catalog fixtures or persistent game state.
6. Run focused tests, the full suite, `roleplay validate catalog`, and `git diff --check`; record
   evidence; mark only Slice 1 verified; stop for review.

### Slice 1 acceptance matrix

| Class | Required assertion |
| --- | --- |
| Happy path | `grant 8` on a creature with no buffer applies one `component.add`; the readback component has exactly two fields with the fixed `sourceRef`. |
| No-stacking | `grant 5` over an existing 8 with `keep` applies **zero** effects and leaves bytes identical; with `replace` applies one `component.set` to 5 and reports the discarded 8. `grant 12` over 8 with `replace` reports the discarded 8; with `keep` applies zero effects. The lower-replaces-higher case is legal and must be proven, since the rule is a choice, not an optimisation. |
| Boundaries | `amount` of 1 and of the safe-integer maximum both grant; 0 and −1 fail; a buffer of 1 expires cleanly. |
| Not healing | Every grant leaves `dnd2024.hit-points` byte-identical, asserted on a creature at partial Hit Points. |
| Closed input | Missing `amount`; fractional, non-finite, string, or null `amount`; `onExisting` present with no buffer; `onExisting` absent with a buffer; unknown `onExisting`; `expire` with an extra key; supplied `sourceRef`/`current`/`maximum`/`duration`/`effects`; extra key — each fails with zero effects. |
| Missing state | `expire` with no component fails with a distinct reason. |
| Corrupt state | A stored `amount` of 0, negative, fractional, or missing, a wrong `sourceRef`, an extra field, or malformed JSON is rejected by `grant`-over-existing and by `expire` before any effect. |
| Determinism | Equivalent databases and input produce byte-identical components; no `ctx.randomInt` call. |
| Routing | `grant temporary hit points` and `expire temporary hit points` select only this writer; `record hit points`, `heal the character`, and `apply confirmed weapon damage` must not select it, and it must not capture them. |
| Effects | Exactly one effect of the expected type per state-changing success; exactly zero for a kept buffer; zero on every rejection. |
| State integrity | Before/after byte comparison on the subject's Hit Points and on one untouched sibling for every case. |
| Readback | Contract, definition, schema, and mechanic are loaded from the fresh validation database at intended version and scope; the component's absence is read after `expire`. |
| Restoration | Disposing the fresh test database removes fixtures; Feature 10–15 baselines remain untouched. |
| Repository | `roleplay validate catalog`, focused and full suites, and `git diff --check` pass; no persistent import occurs. |

### Slice 1 exit gate

Every row passes with recorded mechanic id and version, parsed result fields, exact effect counts
including the zero-effect case, before/after bytes, disposable-database readback, cleanup evidence,
and repository checks. Slice 2 remains independently authorizable after review.

## Slice 2 — healing

### Status and prerequisite

Independently authorizable after Feature 6 and E1 are verified. Adds `procedure.mechanic.dnd2024.healing`,
`mechanic.dnd2024.healing.apply`, and the event type `dnd2024.healing.received` with its schema.
Revises `procedure.mechanic.dnd2024.hit-points` only to replace its forward reference — "a future
healing feature will own healing-caused increase" — with the name of the owner that now exists.

### Data/state and resolution contract

- One required role `subject` declaring `dnd2024.hit-points`. Closed input is exactly
  `{"amount":<integer 1..safe>}`. No target delta, no final value, no maximum, no source, no
  effects, no cause.
- Validate the complete Hit Point component and its `sourceRef` before any effect.
- `missing = maximum - beforeCurrent`; `appliedAmount = min(amount, missing)`;
  `afterCurrent = beforeCurrent + appliedAmount`; `lostToMaximum = amount - appliedAmount`.
  This computes against the bounded missing amount, so valid safe-integer healing is clamped without
  an unnecessary `beforeCurrent + amount` overflow rejection.
- Applies exactly one `component.set` carrying the complete three-field object with `maximum` and
  `sourceRef` unchanged — **including when `appliedAmount` is 0** because the creature is already at
  maximum. Feature 9's apply parent sets the precedent: it writes the full valid after-state even in
  zero-damage cases, and matching it keeps the two Hit Point consumers shaped alike.
- Declares exactly one `dnd2024.healing.received` event naming the subject, carrying `targetId`,
  `requestedAmount`, `appliedAmount`, `lostToMaximum`, `beforeCurrent`, `afterCurrent`, `maximum`,
  and `sourceRef`. It is declared on every success, including the zero-applied case, so a
  subscriber can distinguish "healed for nothing" from "did not happen".
- Healing does not touch `maximum`, does not touch `dnd2024.temporary-hit-points`, does not clear a
  condition, and consumes no randomness.

### Acceptance and exit gate

Prove: healing a creature at 3/10 by 4 yields 7/10 with one effect and one event; healing by 20
yields 10/10 with `lostToMaximum: 13`; healing a creature already at maximum yields one effect, one
event, `appliedAmount: 0`, and byte-identical component data; healing a creature at 0 yields the
expected current and does **not** clear any condition or death state, which is Feature 17's job and
is asserted here as a negative; a buffer present before healing is byte-identical after; `maximum`
is byte-identical in every case; `amount` of 0, negative, fractional, non-finite, missing, or above
the safe-integer boundary each fails with zero effects and no event; supplied
`current`/`maximum`/`sourceRef`/`final`/`effects` and extra keys fail; an absent or corrupt Hit
Point component fails before any effect; the event validates against its registered schema, appears
exactly once, names the subject, and has disposable-database causation evidence; a failed healing
declares no event; replay is exact; no `ctx.randomInt` call;
routing selects this mechanic for "heal the character" and "restore hit points" and not for "record
hit points", "grant temporary hit points", or "apply confirmed weapon damage"; readback and cleanup
hold. Run the full suite, `roleplay validate catalog`, and `git diff --check`; no persistent import
occurs. Slice 3 stays blocked.

## Slice 3 — the buffer absorbs damage first

### Status and prerequisite

Blocked until Slices 1–2 and Feature 15 Slice 4 are verified. Revises `procedure.mechanic.dnd2024.weapon-damage.apply` and
`mechanic.dnd2024.weapon-damage.apply`, the `dnd2024.damage.dealt` event type and schema, and
`procedure.mechanic.dnd2024.temporary-hit-points` (to record that the damage path is now a second,
legitimate consumer of the buffer). Adds no new mechanic and no new component.

### Data/state and resolution contract

- The apply parent's `target` role declares `dnd2024.temporary-hit-points` alongside its existing
  components; the role stays required and the buffer's absence is the ordinary case.
- Order after Feature 15's mitigation child returns `finalAmount`:
  `temporaryAbsorbed = min(temporaryBefore, finalAmount)`;
  `temporaryAfter = temporaryBefore - temporaryAbsorbed`;
  `toHitPoints = finalAmount - temporaryAbsorbed`;
  `afterCurrent = max(0, beforeCurrent - toHitPoints)`;
  `overkill = max(0, toHitPoints - beforeCurrent)`.
- Effects, in a fixed order within one transaction:
  - no buffer → exactly one `component.set` on Hit Points;
  - buffer with `finalAmount: 0` → exactly one `component.set` on Hit Points; the buffer is
    byte-identical and receives no no-op effect;
  - buffer partly spent → one `component.set` on the buffer, then one on Hit Points;
  - buffer exactly exhausted → one `component.remove` of the buffer, then one `component.set` on
    Hit Points.
  The Hit Point effect is always applied, even when `toHitPoints` is 0, matching Feature 9's
  zero-damage precedent. A present buffer is written only when its amount changes.
- The `dnd2024.damage.dealt` payload gains `temporaryBefore`, `temporaryAfter`, and
  `temporaryAbsorbed`. Existing fields keep their meanings, with `overkill` now computed after
  absorption per decision 7.
- The parent still never rolls, never recomputes mitigation, never trusts a caller-supplied amount,
  never grants a buffer, never heals, and never applies a condition or death consequence.

### Acceptance and exit gate

Prove: a target with no buffer produces exactly the Feature 15 Slice 4 result, byte-identical for
the same seed — required; a buffer larger than the damage absorbs it all, leaves Hit Points
byte-identical, and produces two effects; a buffer exactly equal to the damage is removed, not set
to 0, and the component's absence is queried; a buffer smaller than the damage is removed and the
remainder reduces Hit Points; a resistant target with a buffer halves *before* absorbing, proven by
a case where the two orders give different answers; a critical hit against a resistant target with a
buffer resolves double-then-halve-then-absorb in that order; overkill is 0 when a buffer absorbs a
blow that would otherwise exceed current Hit Points, and this row is required because Feature 17
depends on it; overkill is correct when the buffer only partly absorbs; a zero-damage instance
against a buffered target leaves the buffer byte-identical, applies only the required Hit Point
set, and still records one event; a corrupt buffer fails the
whole application before any effect with Hit Points byte-identical; the revised event validates,
appears exactly once, and carries the three new fields; already-recorded pre-revision events remain
readable and valid against their emitted version; every Feature 9 Slice 2 and Feature 15 Slice 4
acceptance row still passes; two frozen child results are separately reported; replay is exact;
routing unchanged; disposable readback and cleanup hold. Run the full suite,
`roleplay validate catalog`, and `git diff --check`; no persistent import occurs.

Feature 16 is verified only after the Slice 3 gate passes and this plan records evidence; then stop
before Feature 17.

## Forward dependencies this plan deliberately leaves open

| Concern | Owner | Note |
| --- | --- | --- |
| Ending Unconscious and resetting death saves on regaining Hit Points | Feature 17 | Subscribes to `dnd2024.healing.received` and acts only when `appliedAmount > 0`; a clamped zero-applied event is an audit fact, not a revival. The event exists from Slice 2 so Feature 17 need not revise the healing owner. |
| Instant death using overkill | Feature 17 | Decision 7's post-absorption overkill is what makes it correct. |
| Long-rest expiry of the buffer, and Hit Dice healing | Feature 33 | Calls `expire` and `healing.apply`; supplies the pacing this feature refuses to invent. |
| Spells, potions, and features that heal or grant a buffer | Features 29, 31, 32 | Each calls these transitions; none re-models the arithmetic. |
| Maximum Hit Point changes | Features 27, 33 | Out of scope here; `maximum` is byte-identical in every Feature 16 transition. |

## Plan-quality audit

1. Yes — one capability with an explicitly reasoned answer to the "joined by and" question, and
   every source of healing or buffering excluded and assigned.
2. Yes — the source entity, three headings, PDF page 17, and the explicit no-stacking choice are
   concrete.
3. Yes — temporary hit points, temp hp, buffer, ward, absorb, heal, and healing were searched; the
   Hit Point contract's own forward reference names this feature as the owner.
4. Partly — Feature 6 and 9 rows cite verified exit gates; the Feature 15 rows cite an unimplemented
   plan, which is why this feature is blocked.
5. Yes — Slices 1 and 2 are independently authorized leaves; Slice 3 is a blocked parent.
6. Yes — buffer state, Hit Point state, the healing cause, the damage cause, the absorption
   arithmetic, expiry pacing, and every granting source have single named owners.
7. Yes — each state slice lands with its only safe write path; Slice 3 revises the existing owner.
8. Yes — Slices 1 and 2 are independently named as next, with only verified prerequisites.
9. Yes — absence as the sole "no buffer" representation, the zero-applied healing case, and the
   exactly-exhausted buffer case are all explicit.
10. Yes — every formula, effect count, effect order, event payload, and result field is testable
    without guessing.
11. Yes — the matrix covers happy, boundary (including safe-integer bounds without pre-clamp overflow),
    differential, closed-input, missing, corrupt, replay, routing, effect-exactness including a
    required **zero-effect** success, event validity, state integrity, readback, cleanup, and
    repository classes. The **random-selection and natural-roll classes do not apply**: no slice
    consumes randomness, and Slice 3 inherits Feature 9's dice child rather than re-testing it.
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

- The SRD re-read contradicts the confirmed choice of which Temporary Hit Point set to keep. Revise
  the input rather than retaining a caller-supplied decision the rule does not allow.
- Feature 15 ships an apply parent whose effect list or event payload differs from what Slice 3
  assumes, in which case Slice 3's formulas and effect ordering must be re-derived from the
  implemented shape rather than from this plan.
- The SRD re-read shows healing at 0 Hit Points has a Hit-Point-arithmetic consequence beyond
  `min(maximum, current + amount)`, which would move part of Feature 17's boundary into this
  feature.
- A repository search finds any existing healing or temporary Hit Point owner.

Descend to a new dependency rather than adding a healing mode to the administrative recorder,
storing a zero buffer, merging the buffer into `dnd2024.hit-points`, computing overkill before
absorption, or omitting the healing event and leaving Feature 17 to re-plumb this feature.
