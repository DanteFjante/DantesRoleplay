# D&D code-adoption Slice 11 design — complex behavior families

Status: **accepted selected scope**  
Owner/roadmap: [D&D 2024 application roadmap](ROADMAP.md), complex-behavior lane  
Dependency tree: [D&D code-adoption dependency tree](DND-CODE-ADOPTION-DEPENDENCY-PLAN.md), Slice 11  
Ruleset alignment: every implemented family is `dnd2024-owned`  
Outcome: recover or adapt one dependency-complete complex mechanic family at a time without adding
a second state, rules, RNG, or transaction authority.  
Exclusions: a bulk archive import, live-state migration, automatic campaign upgrades, direct Foundry
runtime dependencies, and batching combat, progression, and spell behavior into one transaction.

## Family scheduling rule

Every family follows four separately accepted leaves:

| Leaf | Boundary | Exit evidence |
| --- | --- | --- |
| 11A | exact rule, owner, dependency, reuse, and transaction decision | exact SRD locator, Foundry reference path, current-owner map, and deliberate exclusions |
| 11B | state/profile or primitive/effect seam | closed schemas/inputs, declared dependencies, no duplicate owner, focused positive and no-change tests |
| 11C | behavior composition and root mutation | SRD ordering, typed effects, replay, rollback, compatibility, and boundary tests |
| 11D | family acceptance | fresh activation, catalog validation, full regression, attribution, and receipt |

Only one leaf is active at a time. Later families remain unscheduled until the preceding family has
a complete 11D receipt.

## First family — damage mitigation

Damage mitigation is the lowest-risk dependency-ready complex family because the current application
already owns damage types through weapon profiles, Hit Points, Conditions (including Petrified),
weapon-damage rolling/application, declared mechanic composition, typed component effects, one
generic action transaction, replay, and rollback. The archived Feature 15 implementation and tests
provide first-party recovery evidence.

~~~text
dnd2024.damage-mitigation (canonical stored type membership)
        + mechanic.dnd2024.d20-test.state-effects (Condition-derived Petrified state)
        -> mechanic.dnd2024.damage.resolve (effect-free defender profile)
        -> mechanic.dnd2024.weapon-damage.apply (later 11C consumer)
        -> component.set dnd2024.hit-points (existing generic transaction)
~~~

The dependency-aware profile seam is deliberate: the damage resolver composes the existing Condition
state-effects owner instead of duplicating Condition validation or discovering the application
structure at runtime.

## Damage-mitigation family leaves

| Leaf | Scope | State |
| --- | --- | --- |
| 11A | decide SRD semantics, source locators, current owners, archived reuse, Foundry reference behavior, and transaction boundary | accepted |
| 11B | recover/adapt `dnd2024.damage-mitigation`, its closed writer, and an effect-free resolver composed with Condition state-effects | accepted |
| 11C | make weapon-damage application consume the resolver and apply Immunity, one Resistance halving, then Vulnerability before the existing HP effect | accepted |
| 11D | run family-wide fresh activation, transaction/replay/rollback, catalog, full-suite, attribution, and compatibility acceptance | accepted |

Temporary Hit Points, healing, damage events, dropping to 0 HP, death saves, concentration, damage
adjustments, thresholds, bypass properties, monster bootstrap, source-grant tracking, and non-weapon
damage causes are not smuggled into this family. They require their own owner decisions or later
families.

## Second family — Temporary Hit Points and healing

Archived Feature 16 is the next dependency-ready family. Current Hit Points, mitigation, weapon
damage composition, typed effects, atomic actions, replay, and rollback already own its dependencies.

| Leaf | Scope | State |
| --- | --- | --- |
| 11E | decide Healing/Temporary HP rules, owners, archive reuse, Foundry reference, and transaction boundary | accepted |
| 11F | recover the positive Temporary HP state/writer and bounded healing transition | accepted |
| 11G | spend optional Temporary HP after mitigation and before actual HP in weapon damage | accepted |
| 11H | fresh activation, replay, rollback, catalog, full regression, attribution, and family acceptance | accepted |

Long Rest expiry, dying/death state, conditions, healing sources, damage events, non-weapon damage,
and concentration remain separate families.

## Remaining-family closure

Fighter behavior, advancement, dying, reactions/timing, tactical movement, rest, Heroic Inspiration
use, spellcasting, monsters, and magic items were re-inventoried after the two accepted families.
None has a complete current dependency/effect/transaction boundary. Their evidence, blockers, and
executable close conditions are recorded in the accepted
[remaining complex-family gate map](adoption/evidence/DND-CODE-ADOPTION-SLICE-11-REMAINING-COMPLEX-FAMILY-GATES.md).
They move to independent product feature plans and are not pending Parent 11 import work.

## Parent acceptance

Leaves 11A–11H deliver two complete complex families. Slice 11I closes every other candidate as
already owned or explicitly deferred. Parent 11 has zero ambiguous pending rows and adds no bulk
archive import, live migration, campaign rebinding, public operation, or generic C# rule behavior.
