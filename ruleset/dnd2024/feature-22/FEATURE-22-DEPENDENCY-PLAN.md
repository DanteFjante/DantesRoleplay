# Feature 22 dependency plan — unarmed, improvised, and two-weapon combat

Status: **Slice 1 verified; Grapple, Shove, improvised weapons, and two-weapon fighting remain blocked by their named tactical and equipment seams.**
Last updated: 2026-08-21

## Execution rule

This plan records a completed repository implementation slice under `AGENTS.md` and the Terra
planning guide. Slice 1 adds one diagnostic mechanic and its governing contract, but no component,
entity, actor state, event, subscription, or game state. A later implementation pass must re-read
current contracts, select exactly one verified next slice, validate a disposable import, record
evidence, and stop.

## Target capability

Within a tactical encounter, a creature can resolve the three SRD Unarmed Strike options
(damage, Grapple, and Shove), make a source-audited improvised-weapon attack, and make the
conditional Light-weapon bonus attack—without treating a held item, condition, position, or spent
Action as a caller-supplied fact.

### Included

- Strength/PB unarmed attacks and fixed 1 + Strength modifier Bludgeoning damage.
- Grapple/Shove saves, source-aware Grappled/Prone transitions, and their Size/free-hand limits.
- Shove's forced five-foot push through the movement owner, not a direct position write.
- Bounded GM adjudication of an improvised weapon as an equivalent weapon or a 1d4 profile.
- The Light-property, distinct-weapon, same-turn Attack-action, Bonus-Action, and ability-modifier
  rules for two-weapon fighting.

### Excluded

- Pins, called shots, disarm, overrun, shove-aside, mounted/swimming grapples, and non-SRD
  wrestling systems.
- Weapon mastery/properties other than this feature's consumption of `Light`; Feature 25 owns
  those properties. Fighting Style and Extra Attack belong to Feature 27.
- A universal body-slot grid, damage mitigation, temporary HP, dying, hazards, spell effects,
  opportunity attacks, and player-authorisation UI.

## Official source basis

The registered source is `source.dnd2024.srd-5.2.1`: *System Reference Document 5.2.1*
(Wizards of the Coast LLC, 2025-05-01, CC-BY-4.0), [Rules Glossary > Unarmed Strike, PDF
pp. 189–190](https://media.dndbeyond.com/compendium-images/srd/5.2/SRD_CC_v5.2.pdf),
[Rules Glossary > Grappled, PDF p. 181](https://media.dndbeyond.com/compendium-images/srd/5.2/SRD_CC_v5.2.pdf),
and [Equipment > Weapons, PDF pp. 89–91](https://media.dndbeyond.com/compendium-images/srd/5.2/SRD_CC_v5.2.pdf).

- An Unarmed Strike is a melee attack against a target within 5 feet. Its Damage option adds
  Strength and Proficiency Bonus to hit, dealing 1 + Strength modifier Bludgeoning damage.
- Grapple/Shove give the target a Strength or Dexterity save against
  DC 8 + attacker Strength modifier + Proficiency Bonus. Both limit the target to no more than one
  Size larger; Grapple needs a free hand. Shove chooses Prone or a five-foot push away.
- Grappled gives Speed 0, restricts attacks against creatures other than the grappler, limits one
  target per free hand, and has stated escape, release, incapacitation, distance, and movement
  rules.
- A non-equivalent improvised weapon is a nonproficient 1d4 of GM-appropriate type; one thrown has
  range 20/60.
- A creature that took the Attack action and attacked with one Light weapon can make one later
  Bonus-Action attack with a different Light weapon; it omits nonnegative ability modifier damage.

## Planning inventory and overlap result

| Inquiry | Repository evidence and conclusion |
| --- | --- |
| Attack owner | Feature 8's `mechanic.dnd2024.weapon-attack` is an effect-free weapon-profile resolver. It cannot represent an unarmed attack without inventing a false weapon. |
| Damage owner | Feature 9's damage roll/application pair has a closed weapon-damage envelope and reads a weapon profile. Feature 15 must supply the shared typed mitigation/application seam before unarmed HP loss is claimed. |
| Saves | Feature 4 owns fixed-DC saving throws, modifiers, seeded dice, and condition consumption. Grapple/Shove derive their DC and invoke it; neither accepts a DC or defender modifier. |
| Conditions | Feature 13 owns source-aware `grappled`/`prone` instances and deliberately defers Grappled's target exception and endings here. Feature 22 must call its writer, never replace a condition list. |
| Tactical state | Feature 20 Slice 2 is the prospective position, Size, and base-reach reader. No forced-movement transition exists for Shove's push. |
| Turn economy | Feature 12 owns resource spending but intentionally records no action kind or attack history. “Action spent” cannot prove the Light bonus-attack prerequisite. |
| Equipment | Feature 23 has item instances, custody, held state, and a read seam; it expressly has no hand count, slots, attack, or dual-wield rules. |
| Weapon properties | Feature 25 has no current owner/plan. `Light`, held-weapon combat eligibility, and hand use cannot be inferred from Feature 7 profiles. |
| Composition | Child inputs are inherited/static/parent-object only and child effects are proposals. Derived DC, condition effects, spending, and forced movement need verified child evidence in one all-or-nothing parent. |

## Recursive dependency analysis

~~~text
Feature 22: unarmed, improvised, and two-weapon combat
├─ SRD source rules                                            [implemented source basis]
├─ D20 / ability / PB conventions                              [implemented: Features 3, 4, 8]
├─ source-aware condition state                                [implemented partly: Feature 13]
├─ Action and Bonus Action spend                               [implemented: Feature 12]
├─ physical item custody / held state                          [implemented: Feature 23]
├─ unarmed damage-only attack evidence                         [missing leaf: Slice 1]
├─ tactical position, Size, and base reach                     [blocked: Feature 20 Slice 2]
├─ typed damage mitigation/application                         [blocked: Feature 15]
├─ manipulation capacity and outgoing-grapple reader           [missing design leaf]
├─ source-specific condition/spend composition                 [missing confirmation]
├─ forced five-foot relocation                                 [missing Feature-20 extension]
├─ improvised-object adjudication                              [missing policy leaf]
├─ Light / weapon-hand property model                          [blocked: Feature 25]
├─ same-turn Attack-action ledger                              [missing state leaf]
└─ player-facing parents                                       [blocked]
   ├─ unarmed Damage                                           [position + typed damage]
   ├─ Grapple / release / escape                               [capacity + conditions + position]
   ├─ Shove Prone / push                                      [position; push needs forced move]
   ├─ improvised attack                                       [adjudication + equipment]
   └─ Light bonus attack                                      [properties + ledger + equipment]
~~~

Slice 1 deliberately follows Feature 8's diagnostic boundary: one closed, effect-free unarmed
result, not a player-facing Action, position check, or Hit Point change.

## Dependency and ownership decisions

1. **Unarmed Strike is not a weapon profile.** Its invariant formula belongs to a Feature-22
   resolver, never a fictional `weapon.dnd2024.unarmed` or Feature-7 catalog record.
2. **Position and Size remain Feature 20 facts.** Every non-diagnostic unarmed action consumes the
   reach reader. Coordinates, distance, Size comparison, and “in range” are never action input.
3. **Defensive save ability is declared intent.** The closed `str|dex` choice records the
   defender/GM's allowed choice, while the DC derives from attacker state and Feature 4 remains the
   sole saving-throw resolver.
4. **Grapple keeps Feature 13 source identity.** Apply/clear `grappled` only through its writer
   with the grappler in the `source` role. No `grappledBy` field or parallel relationship exists.
5. **Hands need capacity, not slots.** Confirm a creature numerical manipulation-capacity profile
   and encounter reader that count held-hand use and source-attributed outgoing grapples. It must
   specify unusual-creature/missing/corrupt semantics and not turn Feature 23 into a slot grid.
6. **Shove push belongs to movement.** Feature 20 needs a validated forced-one-step relocation
   with cause/mode/direction evidence. Feature 22 supplies the successful Shove cause only; it
   never spends target movement or infers collision/hazard effects.
7. **Improvised classification is audited GM policy.** One normal record binds a physical object to
   an equivalent canonical weapon or a bounded improvised profile. No per-attack caller chooses
   damage type, die, range, category, or Proficiency Bonus.
8. **Two-weapon fighting needs a real attack ledger.** Feature 12's Boolean Action stays its owner.
   Feature 22 needs a distinct turn-reset record, written only after a valid Action spend, with
   qualifying attack evidence. Feature 25 supplies Light/hand facts; Feature 27 later extends it
   for Extra Attack/Fighting Style.
9. **Damage is evidence before HP loss.** Slice 1 has zero effects. A later parent uses Feature
   15's typed damage/application boundary; it never copies Feature 9's weapon-specific HP writer.

## Confirmation boundary

| Decision | Required confirmation |
| --- | --- |
| Unarmed resolver | Exact permanent IDs, source locator, result envelope, fixed-damage critical convention, and routing boundary. |
| Tactical reach | Feature 20 coordinate, footprint, Size, base reach, and missing-placement semantics. |
| Composition | Safe parent return of Feature-13 condition-writer and Feature-12 spend effects, with child validation and rollback. |
| Capacity | Data owner, species/monster variety, held-item hand use, grapple aggregation, and Feature-25 relationship. |
| Forced movement | Feature-20 direction/collision/bounds/mode semantics, no-voluntary-budget guarantee, and event ordering. |
| Improvised policy | Authorized classification lifecycle, equivalence reference, eligible physical objects, and source citation. |
| Attack ledger | State shape, reset point, Action-spend linkage, qualifying-Light evidence, one-extra-attack rule, and Feature-27 seam. |
| Damage application | Feature-15 typed damage/mitigation and fixed Bludgeoning application contract. |

## Slice order and stop gates

| Order | Slice | Starts only when | Exit gate |
| --- | --- | --- | --- |
| 1 | Effect-free Unarmed Strike Damage resolver | **Verified.** | Strength/PB D20 result exposes hit/critical/fixed damage evidence and zero effects. |
| 2 | Tactical unarmed Damage action | Slice 1, Feature 20 Slice 2, Feature 15 application seam, and spend composition. | Within-reach Attack action spends one Action and applies verified final Bludgeoning damage atomically. |
| 3 | Manipulation capacity and Grapple reader | Feature 20 Slice 2, Feature 23, anatomy semantics. | Reader reports free capacity and all outgoing grapples from encounter state. |
| 4 | Grapple, release, escape | Slice 3, Feature 4, Feature 13 writer, composition. | Legal Grapple applies source-aware state; every immediate ending clears only that instance. |
| 5 | Shove to Prone | Feature 20 Slice 2, Feature 4, Feature 13 writer composition. | Legal within-reach Shove applies only Prone after failed declared-choice save. |
| 6 | Shove push | Slice 5 and Feature-20 forced relocation. | Failed save forces one legal five-foot away step with no target movement spend. |
| 7 | Improvised adjudication/resolver | Feature 23, Feature 15, GM policy. | Equivalent and 1d4 paths are audited; no per-attack profile is caller-authored. |
| 8 | Attack-action ledger | Features 11–12 and Feature-27 extension boundary. | A qualifying Action attack records evidence only after Action spend and resets next turn. |
| 9 | Light two-weapon bonus attack | Slices 7–8 and Feature 25. | One distinct Light bonus attack later that turn has exact modifier/refusal behavior. |

## Slice 1 — effect-free Unarmed Strike Damage resolver

### Runtime artifacts

- **Implemented:** new governing contract and `mechanic.dnd2024.unarmed-strike.damage` in
  `ruleset.dnd2024.core.gameplay.unarmed-strikes`.
- Focused fresh-import catalog tests.
- No component, entity, item, position, condition, Action budget, Hit Point effect, or migration.

### Data/input and behavior

Require attacker `dnd2024.abilities`, `dnd2024.character-level`, and condition state; require
target `dnd2024.armor-class` and condition state. Declare the same two Feature-13 state-effect
children as Feature 8. The sole input is optional normal `rollCircumstances`; reject caller
condition evidence, Armor Class, PB, Strength modifier, dice, outcome, damage, position, hand,
weapon/profile, Hit Point delta, and effects.

Derive Strength modifier and level-band PB, execute the established seeded D20/circumstance/
natural-20/1 convention, then report `max(0, 1 + Strength modifier)` Bludgeoning potential damage
on a hit. The resolver returns subject/target/source, Strength/PB, circumstance provenance,
dice/selected/total, hit reason, critical, potential damage/type, and exactly `effects: []`. A
critical carries the normal classification but does not increase fixed damage because no damage dice
exist.

### Acceptance matrix and exit gate

| Case | Exact assertion |
| --- | --- |
| Formula | All PB bands and Strength modifiers are exact; hit damage is 1 + Strength modifier clamped at zero. |
| D20 | Normal, Advantage, Disadvantage, cancellation, tied dice, AC equality, natural 20, and natural 1 match Feature 8. |
| Conditions | Both Feature-13 branches merge without caller-forged `condition:` evidence and reproduce with a fixed seed. |
| Closure | Extra/derived fields, malformed circumstances, bad roles/state/source reject before a die. |
| Non-effects | Valid, replayed, and rejected runs leave every role byte-identical and return no effects. |
| Routing | “unarmed strike damage” selects this diagnostic rule, not weapon attack/damage, saves, or condition writing. |

**Verified.** Focused adjacent-contract tests and a disposable catalog validation pass are recorded
in [the Slice 1 receipt](FEATURE-22-SLICE-1-RECEIPT.md). Do not add a tactical action, HP change,
Grapple, Shove, item profile, or turn ledger.

## Later-slice invariants

- Grapple rejects self/oversized/no-capacity cases before its save/effect. Escape is an Action using
  Strength (Athletics) or Dexterity (Acrobatics) against the stored source's DC; confirm the skill
  consumer before making it player-facing.
- Incapacitation and excessive distance clear only the affected source instance. Drag/carry costs
  wait for Feature 20 path-cost composition; do not add a second movement spender.
- Shove chooses exactly one branch, Prone or directly-away forced step; callers supply neither
  target coordinate nor both branches.
- Improvised classification never silently makes an object proficient, magical, Light, Finesse, or
  equivalent.
- The Light extra attack cannot use the same weapon, precede the qualifying attack, occur off turn,
  spend an Action, or add a nonnegative ability modifier; it does not imply Fighting Style.

## Plan-quality audit

- One capability, source locators, owner searches, closed input, recursive graph, atomic/effect
  boundaries, routing, replay, cleanup, and one completed lowest slice: yes.
- Slice 1 runtime artifacts and verification are recorded in [the receipt](FEATURE-22-SLICE-1-RECEIPT.md);
  no persistent catalog import occurred.

## Plan-change rule

Stop and revise if Feature 20 changes its reach model, Feature 15 selects a different typed-damage
envelope, Feature 25 supplies a hand model, Feature 13 changes condition provenance, or a parent
cannot safely return verified child effects. Never model Unarmed Strike as a weapon profile,
Grapple as caller source identity, Shove as a direct coordinate change, or two-weapon eligibility
as a generic “Action spent” Boolean.
