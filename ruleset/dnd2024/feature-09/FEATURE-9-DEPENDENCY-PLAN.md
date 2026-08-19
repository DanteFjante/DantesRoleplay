# Feature 9 dependency plan — Weapon damage and transactional Hit Point loss

Status: **Complete — both slices verified through file-first catalog import**
Last updated: 2026-08-19

## Execution rule

Runtime content is authored first in `catalog/`, then dry-run, imported, and verified against the
database. Both slices completed through that file-first workflow. This plan records their ownership
and reproducible evidence; Feature 10 remains a separate planning-only next feature.

## Target capability

After a GM or caller has confirmed a successful Feature 8 weapon attack, resolve its seeded base
weapon damage and—only through a dependent parent—subtract that damage from the target's
authoritative current Hit Points atomically.

### Included

- One effect-free seeded damage-roll child using the canonical weapon damage expression and the
  same selected ability modifier as its already-confirmed attack.
- Normal and Critical Hit damage: roll the weapon's base damage dice once normally and twice on a
  critical, then add the ability modifier once and clamp a negative result to zero.
- One composed parent that reads the child's structured damage evidence and changes exactly the
  target `dnd2024.hit-points` component through one `component.set` effect.
- Current Hit Points bounded at zero while maximum and the fixed Hit Point source reference remain
  byte-for-byte authoritative.

### Excluded

- Determining whether an attack hits, its d20, target AC, ability selection, weapon proficiency,
  or natural-20/1 classification; Feature 8 owns those facts.
- Attack range, ownership, ammunition, weapon properties, extra attacks, other damage dice,
  spells, unarmed strikes, class features, bonuses/penalties other than the selected ability
  modifier, and any target selection or turn legality.
- Resistance, Vulnerability, Immunity, temporary Hit Points, healing, conditions, Bloodied
  triggers, unconsciousness, death, massive damage, damage history, and damage-type-specific
  behavior. These need their own state/owner decisions in later features.
- A generic damage component, a persisted attack result, direct database writes, a caller-provided
  damage total/die result/Hit Point delta, or a revision of Feature 8 merely to add consequences.

## Official source basis

The existing `source.dnd2024.srd-5.2.1` entity identifies the official SRD 5.2.1 (published
2025-05-01 under CC-BY-4.0). The Feature 9 implementation must cite these stable locators:

- `Playing the Game > Damage and Healing > Hit Points`, PDF page 16: damage is subtracted from
  current Hit Points; current Hit Points cannot go below 0.
- `Playing the Game > Damage and Healing > Damage Rolls`, PDF page 16: roll the named damage dice,
  add modifiers, and deal zero rather than negative damage; weapon damage adds the same ability
  modifier used for the attack roll.
- `Playing the Game > Damage and Healing > Critical Hits`, PDF page 16: roll the attack's damage
  dice twice and add relevant modifiers normally.
- `Equipment > Weapons`, PDF page 89: the canonical profile supplies the base weapon damage dice
  and type. `Playing the Game > D20 Tests > Attack Rolls`, PDF page 7 remains the source of the
  selected ability and Critical Hit classification owned by Feature 8.

## Verified existing dependencies

| Dependency | Current evidence |
| --- | --- |
| File-first catalog workflow and transactional effects | Catalog/database verification reports 71 matching records; `EffectApplier` validates a complete batch before one transaction applies it. |
| Authoritative Hit Points | Feature 6 verified `dnd2024.hit-points` owns only `current`, `maximum`, and its fixed source reference; normal writer intentionally rejects deltas. |
| Canonical weapon damage facts | Feature 7 verified `dnd2024.weapon-profile` with positive `count`, allowed faces, physical damage type, and fixed `Equipment > Weapons` source reference. |
| Attack evidence | Feature 8 verified `mechanic.dnd2024.weapon-attack`, which returns selected ability, ability modifier, hit, critical, and zero effects without persistence. |
| Deterministic random mechanism and composition | Existing Jint `ctx.randomInt` and declared-child composition are verified by Features 3 and 5; child effects are proposals until the parent returns its batch. |
| Existing damage owner search | Catalog searches for `damage`, `critical`, `Hit Point`, and `weapon attack` find only static weapon-profile facts, Feature 6 state recording, and Feature 8's explicit no-damage resolver. No damage-roll or damage-application mechanic exists. |

## Recursive dependency analysis

```text
Feature 9: confirmed weapon hit deals base damage and changes target HP
├─ catalog workflow, source registry, transactional effects                 [implemented]
├─ canonical weapon damage expression and source                            [implemented: Feature 7]
├─ selected attack ability / critical classification                         [implemented transient evidence: Feature 8]
├─ bounded target Hit Point state                                            [implemented: Feature 6]
├─ seeded base weapon damage resolver                                        [verified: Slice 1]
│  ├─ validate confirmed-hit ability/critical input
│  ├─ read canonical profile and derive ability modifier
│  └─ roll base dice once or twice, clamp total to zero
└─ composed damage application                                               [verified: Slice 2]
   ├─ consume one Slice 1 child result
   ├─ validate target current/maximum HP state
   └─ atomically replace only target current HP
```

The child is a standalone, testable leaf and intentionally proposes no effect. The parent cannot
begin until that result envelope is stable, avoiding duplicated dice/critical rules in two owners.

## Dependency and ownership decisions

1. `dnd2024.weapon-profile.damage` remains Feature 7's one canonical base damage expression. It
   is read, never copied into a damage component or caller input.
2. Feature 8 continues to own hit/miss, Critical Hit classification, and the chosen ability. Its
   action result is transient by design. Feature 9 therefore accepts only a closed confirmation
   `{ability,critical}` after the GM/caller has observed a successful Feature 8 result. It never
   accepts `hit`, a d20, an AC, profile facts, a modifier, dice, total damage, or an HP delta.
3. Slice 1 owns damage mathematics and evidence: exact dice in generation order, base-dice
   multiplier, ability modifier, nonnegative final damage, type, and source. It has zero effects.
4. Slice 2 owns applying the Slice 1 result to target `dnd2024.hit-points`. It uses one
   `component.set` effect carrying the whole valid component object, preserves maximum/sourceRef,
   and clamps only current to zero. It does not use the Feature 6 administrative writer, because a
   damage-caused change is not correction of a supplied complete state.
5. The lack of persisted attack evidence is deliberate. A future composed combat action may bind
   Feature 8's actual `hit`/`critical` output to Feature 9 without caller confirmation, but that
   requires a new composition input flow and is outside this feature. Slice 2 is usable as the
   explicit two-step GM workflow, not as proof an arbitrary caller independently hit a target.
6. Zero Hit Points is a numeric boundary only in this feature. No condition, death, massive
   damage, nonlethal choice, or automatic state transition is inferred from it.

## Slice order and stop gates

| Order | Slice | Starts only when | Exit gate |
| --- | --- | --- | --- |
| 1 | Seeded confirmed-hit weapon damage | This plan is reviewed; Features 6–8 remain verified | Catalog-authored contract/mechanic returns complete deterministic damage evidence with no effects, passes fresh-import coverage, catalog import/verify, full suite, and diff check. |
| 2 | Apply composed weapon damage to Hit Points | Slice 1 is imported, verified, and reviewed | Catalog-authored parent consumes exactly one Slice 1 child result, emits one atomic target HP set on hits, preserves all other state, and passes fresh-import, atomicity, replay, catalog, and repository gates. |

## Slice 1 — Resolve seeded confirmed-hit weapon damage

### Runtime artifacts

- New active `procedure.mechanic.dnd2024.weapon-damage.roll` under
  `ruleset.dnd2024.core.gameplay.weapon-damage`.
- New active `mechanic.dnd2024.weapon-damage.roll` in the same category and scope
  `dnd2024-srd-5.2.1`.
- No component definition, entity, schema migration, or effect-producing mechanic.

### Governing contracts and source locator

Immediately before authoring, re-read `procedure.system.create-feature`, `procedure.action.run`,
the Feature 7 weapon-profile contract/mechanic, Feature 8 weapon-attack contract/mechanic, and
the existing deterministic-random convention. Cite the Damage Rolls and Critical Hits locators on
PDF page 16 and `Equipment > Weapons` on PDF page 89.

### Data/input contract and required state

- Require `subject` with valid `dnd2024.abilities` and `weapon` with valid
  `dnd2024.weapon-profile`; no target is required for the effect-free child.
- Input is exactly `{"ability":"str"|"dex","critical":true|false}`. It represents a confirmed
  hit; invocation itself is the confirmation, so `hit` is neither accepted nor inferred.
- The ability must be permitted by the profile. Validate the same closed ability-score/profile
  shapes and fixed source reference already owned by Features 7–8 before consuming randomness.
- `critical` is a Boolean. No null/missing, `rollCircumstances`, AC, attack total, proficiency,
  attack result, profile, damage expression, multiplier, modifier, selected/rolled dice, total,
  target, HP, damage type override, or effects field is accepted.

### Resolution/recording behavior

1. Validate closed input, roles, ability scores, and canonical profile before any die is consumed.
2. Derive the selected ability modifier from authoritative score. Read base `count`, `faces`, and
   `type` from the profile.
3. Set `damageDiceCount = profile.damage.count * (critical ? 2 : 1)`; roll exactly that many
   `1..faces` dice in order with the action seed.
4. Add all dice and the ability modifier once. Set `damage = max(0, subtotal + abilityModifier)`.
   Proficiency Bonus is never damage and is not read or accepted.
5. Return explanatory structured data with zero effects; do not create a result entity or mutate
   the weapon or subject.

### Result and effects

Return `test: "weapon-damage"`, subject/weapon ids, chosen ability, `critical`, weapon damage
type, base count/faces, actual dice count and ordered rolls, dice subtotal, ability modifier,
final nonnegative damage, and source locators. `effects` is exactly `[]`.

### Invariants, failure behavior, and non-goals

- Normal damage rolls base dice once; critical damage doubles base dice only, never the ability
  modifier. A negative ability modifier can reduce damage to zero but never below it.
- Same seed, state, and input return byte-identical data. Invalid input/corrupt state consumes no
  randomness and leaves subject/weapon bytes unchanged.
- No target, Hit Point component, hit validation, Resistance, Vulnerability, Immunity, temporary
  Hit Points, extra damage source, or condition appears in this slice.

### Slice 1 implementation sequence

Searches confirmed that no existing damage owner existed. The procedure, mechanic metadata, source,
and fresh-database catalog test were authored, dry-run, imported, and verified as recorded below.
Slice 2 has not begun.

### Slice 1 acceptance matrix

- Fresh import finds exactly the new procedure/mechanic and existing canonical Dagger, Shortbow,
  and Battleaxe profiles; it creates no component/entity/damage history.
- Normal Dagger and Battleaxe cases prove the profile's count/faces/type, ordered dice, one ability
  modifier, and `damage == max(0, diceSubtotal + modifier)`; a low-Strength fixture proves zero
  clamp.
- A critical Dagger fixed seed rolls exactly two base dice for every base die, adds the modifier
  once, and differs from an otherwise identical normal result only by the extra base dice.
- Fixed seeds prove unequal dice, dice generation order, reproducibility, and safe-integer bounds;
  profile damage-count and face boundaries are covered with disposable valid/corrupt fixtures.
- Reject malformed root, missing/non-Boolean critical, unavailable ability, extra/derived values,
  wrong/missing roles, absent/corrupt abilities/profile, invalid source reference, invalid dice
  profile, and caller-supplied target/HP/damage/effects before a roll; compare exact role bytes.
- Intent routing chooses this damage-roll mechanic for confirmed-hit damage language rather than
  Feature 8 attack, Feature 6 HP writer, Feature 7 profile writer, generic dice, or saving throws.
- Assert zero effects and unchanged subject/weapon state on valid, replayed, and rejected actions.

### Slice 1 exit gate

All matrix rows have structured dice/result/effect/state evidence; new artifacts import/read back
from a fresh database; catalog verification is clean; temporary fixtures are removed with normal
effects; full repository tests and `git diff --check` pass; this plan records reproducible evidence.
Stop for review before Slice 2.

### Slice 1 completion evidence — 2026-08-19

- Added catalog-authored active `procedure.mechanic.dnd2024.weapon-damage.roll` and
  `mechanic.dnd2024.weapon-damage.roll`; no component, entity, schema, or Hit Point writer was
  introduced.
- `CatalogFeature9Tests` imports a complete catalog copy into a fresh database. It proves normal
  and critical canonical damage profile use, doubled base dice and one ability modifier, zero
  clamp, replay, forbidden profile ability, malformed/derived input and corrupt profile rejection,
  intent routing with the disambiguating confirmed-damage phrase, zero effects, and exact unchanged
  subject/weapon state.
- The resolver caps an execution at 100 actual dice; a profile that would exceed the critical-hit
  capacity rejects before rolling. This is a sandbox safety capacity, not an added profile fact;
  all catalog weapons remain within it.
- Catalog dry-run reported exactly **2 new** records (`mechanic.dnd2024.weapon-damage.roll` and
  `procedure.mechanic.dnd2024.weapon-damage.roll`) and **67 unchanged**. Import created **2** and
  updated **0**. `roleplay verify catalog` then reported **69 unchanged**.
- Focused integration tests passed **2/2**. Full repository tests passed **301/301** and
  `git diff --check` passed with only existing line-ending conversion warnings. Slice 2 remains
  unimplemented regardless of those results.

## Slice 2 — Apply composed weapon damage to authoritative Hit Points

### Status and prerequisite

Complete — Slice 1's procedure/result envelope and full exit gate were verified and reviewed.

### Runtime artifacts

- New active `procedure.mechanic.dnd2024.weapon-damage.apply` and
  `mechanic.dnd2024.weapon-damage.apply` under `ruleset.dnd2024.core.gameplay.weapon-damage` in
  scope `dnd2024-srd-5.2.1`.
- The parent declares exactly one child, `mechanic.dnd2024.weapon-damage.roll`, binding parent
  `subject` and `weapon` and inheriting the identical closed input.
- No new component, entity, writer, or schema. Feature 6's administrative HP writer and Feature
  8's attack resolver are not revised.

### Governing contracts and source locator

Re-read the completed Slice 1 contract, `procedure.action.run`, `procedure.mechanic.dnd2024.hit-
points`, Feature 7 weapon-profile contract, and effect-transaction behavior. Cite Damage Rolls,
Critical Hits, and Hit Points on PDF page 16.

### Data/input contract and required state

- Require `subject` abilities, `weapon` profile, and `target` valid `dnd2024.hit-points`; child
  requirements own subject/weapon validation and parent independently validates the closed target
  state plus source reference before proposing effects.
- Parent input is identical to Slice 1: exactly `{"ability":"str"|"dex","critical":true|false}`.
  It does not accept current/maximum HP, a target delta, damage amount, child data, or effects.
- The parent must require exactly one successful declared child result, verify its stable result
  shape/ids/ability/critical/positive-safe-integer damage, and reject malformed child evidence
  without effects. It may not reroll or recompute damage.

### Resolution/recording behavior

1. Composition executes the child once using a derived child seed; parent reads its frozen result.
2. Validate the target HP object is the Feature 6 closed state and child data names the same
   subject/weapon and input facts.
3. Compute `afterCurrent = max(0, beforeCurrent - child.damage)` without altering `maximum` or
   `sourceRef`.
4. Return exactly one `component.set` effect for target `dnd2024.hit-points` containing the whole
   valid after-state. The action runner dry-runs and applies that single batch transactionally.

### Result and effects

Return parent data identifying child mechanic/version/seed, subject/target/weapon ids, child
damage/type/critical facts, before/after current HP, unchanged maximum, and source. Return exactly
one target `component.set` effect for a valid action, including zero-damage and already-zero HP
cases; no effect is produced for invalid parent/child/target state.

### Invariants, failure behavior, and non-goals

- Parent never re-rolls damage, never trusts caller damage/HP fields, and never modifies subject,
  weapon, target maximum, or any other component.
- A damage amount greater than current HP results in zero, not negative HP; it causes no condition
  or death behavior. Zero damage still records the canonical unchanged pair through the one
  intended target set effect, making the accepted action auditable.
- If the child, validation, dry-run, or apply fails, the target and all roles remain unchanged;
  effect application is all-or-nothing.

### Slice 2 implementation sequence

Slice 1 and its dependencies were re-read, then the declared-child metadata, parent contract,
source, and fresh-database integration coverage were authored. The catalog preview/import/verify
and full repository checks are recorded below. Feature 10 remains a separate planning-only feature.

### Slice 2 acceptance matrix

- Fresh import reads the parent and its one declared child; parent result exposes child metadata and
  emits exactly one target component set on accepted actions.
- Normal and critical fixed-seed cases prove parent uses the child's exact damage, decreases only
  current HP, preserves target maximum/source and exact subject/weapon bytes, and clamps overkill
  at zero.
- A zero-damage low-modifier case has one auditable target set with unchanged pair; already-zero
  target stays zero. HP maximum, source reference, AC, and unrelated components remain identical.
- Identical parent seed/input/equivalent state reproduces child and parent structured data; a
  dry-run followed by the identical apply predicts exactly the one target change.
- Reject missing/wrong roles, malformed/derived input, absent/corrupt HP state, invalid source,
  corrupted/mismatched child data, and invalid child outcome without any effect or state change.
- Force a batch-validation failure with a disposable invalid effect fixture to prove no partial HP
  write; clean it up through normal effects.
- Routing chooses application language over Feature 6 correction, Feature 8 attack, Slice 1 damage
  roll, generic dice, and later healing/death concepts.

### Slice 2 exit gate

All matrix assertions have structured child/effect/byte-state evidence; catalog import/readback and
verification are clean; disposable fixtures are gone; full repository tests and `git diff --check`
pass; completion evidence is recorded. Stop after Feature 9; do not begin Feature 10 in the same
pass.

### Slice 2 completion evidence — 2026-08-19

- Added catalog-authored active `procedure.mechanic.dnd2024.weapon-damage.apply` and
  `mechanic.dnd2024.weapon-damage.apply`; the parent declares exactly one
  `mechanic.dnd2024.weapon-damage.roll` child and introduces no component, entity, or schema.
- `CatalogFeature9Tests` fresh-import coverage proves child consumption, normal and critical
  damage application, exact one-effect target-only mutation, maximum/source preservation,
  overkill clamp, deterministic replay from restored state, corrupt-HP rejection, and malformed
  input preservation. The child remains effect-free and the parent never recomputes damage.
- Catalog dry-run reported exactly **2 new** records (`mechanic.dnd2024.weapon-damage.apply` and
  `procedure.mechanic.dnd2024.weapon-damage.apply`) and **69 unchanged**. Import created **2** and
  updated **0**. `roleplay verify catalog` then reported **71 unchanged**.
- Focused Feature 9 integration tests passed **3/3**. Full repository tests passed **302/302**;
  `git diff --check` passed with only existing line-ending conversion warnings.

## Plan-quality audit

Yes: one player outcome and explicit non-goals; concrete SRD source/version/page locators; verified
owners and absence search; every missing dependency expanded to Slice 1 or Slice 2; state,
derived values, transient confirmation, resolution, and downstream consequences have one owner;
each slice has a bounded artifact set and stop gate; inputs are closed; dice/critical/HP formulas
are testable; the matrices cover boundaries, invalid/missing/corrupt state, deterministic replay,
routing, effects, atomicity, and cleanup; and both slices have completion evidence. No future
feature is authorized by this completion pass.

## Plan-change rule

Stop and revise this plan if Feature 8 gains a persisted/signed attack-result handoff, composition
can safely transform child output into sibling input, weapon profiles gain a damage-affecting
property, or a new target damage-mitigation component becomes required. Do not bypass those changes
with caller-supplied hit/damage/HP values, a generic damage history component, C# game-rule helper,
or a revision of Feature 6/8 that steals the planned owner's responsibility.
