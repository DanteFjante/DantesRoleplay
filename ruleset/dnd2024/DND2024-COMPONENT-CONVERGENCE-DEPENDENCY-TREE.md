# DND2024-COMPONENT-CONVERGENCE dependency tree — canonical mechanics on the complete ECS model

Status: **active; item runtime core verified**
Ruleset alignment: **dnd2024-compatible**
Source: repository component contracts and mechanics; no D&D rule meaning changes in this plan
Owning roadmap: `ruleset/dnd2024/ROADMAP.md`, gameplay breadth and character-creation lanes
Parent plan: [complete-campaign dependency graph](DND2024-COMPLETE-CAMPAIGN-DEPENDENCY-GRAPH.md)
Plan role: **subordinate evidence/subgraph; remaining ordering does not select the next leaf independently**
Machine-readable evidence: `evidence/modeling/canonical-component-crosswalk.json`

## Outcome and non-goals

Make the authoritative catalog's active JavaScript mechanics operate on the complete canonical
ECS model without creating parallel state authorities. Preserve useful rule algorithms and generic
transaction behavior while replacing incompatible component contracts through reviewed cohorts.

This plan does not authorize permanent component IDs, schema changes, mechanic edits, live-state
migration, database writes, or new D&D behavior. Retired-source hashes are historical evidence only.

## Existing owners and evidence

| Concern | Owner | State | Evidence |
| --- | --- | --- | --- |
| Current runtime components | `catalog/applications/dnd2024/components/` | verified | 40 registered component schemas |
| Current executable rules | `catalog/applications/dnd2024/mechanics/` | verified | 67 active JavaScript mechanics |
| Current procedures | `catalog/applications/dnd2024/procedures/` | verified | mechanic role and component requirements |
| Complete target model | `catalog/applications/dnd2024/components/` | verified, canonical | prototype-cutover receipt plus current catalog validation |
| Canonical records | `catalog/applications/dnd2024/content/` | verified, canonical | prototype-cutover receipt plus subsequent accepted content slices |
| Metric policy | `DND2024-METRIC-MEASUREMENT-DEPENDENCY-TREE.md` | prototype complete; catalog migration ready | exact metres, kilometres, kilograms, and litres |
| Deferred mechanic families | `ruleset/dnd2024/adoption/evidence/DND-CODE-ADOPTION-SLICE-11-REMAINING-COMPLEX-FAMILY-GATES.md` | verified | explicit prerequisites for incomplete behavior |
| Historical implementation | `adoption/evidence/retained-archive-inventory-13a.json` and `evidence/retired-implementation/` | historical evidence only | immutable hashes and unique receipts preserved after source retirement |

No Foundry reference is relevant to this crosswalk: it maps two repository-owned ECS contracts and
does not decide a D&D rule. Every later ruleset-owned mechanic slice still requires its own SRD
locator and relevant Foundry review.

## Compatibility result

The two systems share the ECS/JavaScript architecture but are not wire-compatible.

| Measure | Result |
| --- | ---: |
| Current canonical component IDs | 40 |
| Prototype component IDs | 154 |
| Exact ID matches | 0 |
| Same marker payload, ID migration only | 1 |
| Near-shape migrations | 4 |
| Field/reference/unit maps | 8 |
| Canonical components merged into normalized owners | 6 |
| Canonical monoliths split across target owners | 19 |
| Prototype owner gaps | 2 |

For the current mechanic implementations, 12 algorithms can be retained after rebinding, 16 need
contract adaptation, 10 need replacement because ownership/derived-state semantics change, and 2
must wait for missing target owners.

## Complete 40-component map

Legend: **retain** keeps the rule algorithm; **adapt** keeps useful logic but changes its contract;
**replace** changes the state/transaction owner; **defer** waits for a missing target owner.

| Current canonical component | Prototype owner or owners | Schema action | Mechanic action |
| --- | --- | --- | --- |
| `dnd2024.abilities` | `dnd2024.creature.ability-scores` | map six fields to vocabulary-keyed scores | retain |
| `dnd2024.armor-class` | `dnd2024.creature.defenses`, `dnd2024.creature.defense-basis` | split; final AC becomes derived | replace writer; adapt consumers |
| `dnd2024.armor-training` | `dnd2024.creature.proficiencies` | merge into keyed proficiency state | adapt |
| `dnd2024.background-creation-profile` | background + choice-set + grant components | split embedded package | adapt |
| `dnd2024.background.ability-increase-options` | choice-set + option + grant components | split numeric choices | adapt |
| `dnd2024.character-creation-record` | origin selections + choice resolutions + entitlements + provenance | split; receipts own operation evidence | replace |
| `dnd2024.character-experience` | `dnd2024.character.experience` | near-shape rename/provenance move | retain |
| `dnd2024.character-feature-grants` | `dnd2024.character.feature-entitlements` | map strings to source-qualified references | adapt |
| `dnd2024.character-level` | `dnd2024.character.class-membership` | split by class; derive total level | replace writer; adapt readers |
| `dnd2024.character.ability-assignment-policy` | choice-set + option + grant components | **gap:** numeric policy semantics incomplete | defer |
| `dnd2024.character.content-definition` | core source/version/presentation plus archetype-specific component | split and retire generic kind component | replace |
| `dnd2024.character.profile` | `dnd2024.character.identity` | near-shape superset | retain |
| `dnd2024.class-creation-profile` | class + choice-set + grant + spell-source definition | split embedded traits | adapt |
| `dnd2024.class-progression` | progression + grant + character Hit Dice owners | split immutable grants from mutable pools | adapt |
| `dnd2024.conditions` | active-effect state/provenance and effect definitions/operations | split each instance into an entity | replace |
| `dnd2024.creature-size` | `dnd2024.creature.body` | map enum to size entity reference | retain |
| `dnd2024.damage-mitigation` | `dnd2024.creature.defenses` | map lists to qualified response entries | retain |
| `dnd2024.encounter-initiative-order` | initiative + participation + round + turn components | split snapshot into lifecycle entities | replace |
| `dnd2024.encounter-turn-state` | encounter round + turn components | split mutable cursor into lifecycle entities | replace |
| `dnd2024.equipment-state` | `dnd2024.item.equipment` | map enum to bearer/slots/configuration | adapt |
| `dnd2024.feat-profile` | feat + prerequisite + grant components | split identity, eligibility, and benefits | adapt |
| `dnd2024.heroic-inspiration` | `dnd2024.character.heroic-inspiration` | marker rename; payload already equivalent | retain |
| `dnd2024.hit-points` | `dnd2024.creature.hit-points` | near-shape; move provenance, add maximum reduction | retain |
| `dnd2024.item-activity` | activity membership + activation + cost + applied effects | split descriptors into activity entities | replace |
| `dnd2024.item-definition` | composed physical/container/equippable/armor/weapon/ammunition/price/activity components | split monolith and migrate units | adapt |
| `dnd2024.item-instance` | definition link + quantity/equipment state + generic relationships | split identity from state and custody | adapt |
| `dnd2024.item-quantity` | item quantity + definition link | map count; remove stored stack key | adapt |
| `dnd2024.language-proficiencies` | `dnd2024.creature.languages` | map strings to capability/source entries | retain |
| `dnd2024.rest-episode` | `dnd2024.exploration.rest` plus generic participants/events/clock | split into lifecycle entity | replace |
| `dnd2024.rest-policy` | none | **gap:** immutable rest-policy definition missing | defer |
| `dnd2024.saving-throw-proficiencies` | `dnd2024.creature.proficiencies` | merge into keyed proficiency state | retain |
| `dnd2024.selected-species` | `dnd2024.character.origin-selections` | merge species/background origin selection | adapt |
| `dnd2024.skill-proficiencies` | `dnd2024.creature.proficiencies` | merge with explicit rank and sources | retain |
| `dnd2024.species-profile` | species + classification/body/movement bases + grants/choices | split authored profile | adapt |
| `dnd2024.speed` | `dnd2024.creature.movement` | map fixed feet fields to metric mode map | adapt |
| `dnd2024.temporary-hit-points` | `dnd2024.creature.temporary-hit-points` | near-shape ID/provenance migration | retain |
| `dnd2024.tool-proficiencies` | `dnd2024.creature.proficiencies` | merge into keyed proficiency state | retain |
| `dnd2024.turn-budget` | combat turn budget + encounter turn | split, count resources, record metric movement | replace |
| `dnd2024.weapon-proficiencies` | `dnd2024.creature.proficiencies` | merge category/property memberships | adapt |
| `dnd2024.weapon-profile` | item weapon + attack/damage/range activity components | split static profile into authored activities | adapt |

The machine-readable crosswalk records the exact field-level requirements for every row.

## Existing mechanics that can be reused

### Retain after component rebinding

- ability-score and proficiency arithmetic in ability checks, saves, Initiative, weapon attacks,
  carrying capacity, and character-sheet derivation;
- Experience Point threshold reads/writes;
- character profile updates and Heroic Inspiration grant presence;
- Size recording;
- damage mitigation ordering and response arithmetic;
- Hit Point, Temporary Hit Point, healing, and buffer-before-HP damage arithmetic;
- language, skill, saving-throw, and tool membership normalization.

### Adapt to the decomposed target model

- character creation and species/feat resolvers;
- weapon attacks and damage using referenced item activities and properties;
- inventory, stacks, currency, burden, transfer, equip, and item-use mechanics;
- Speed reads/writes and every consumer of imperial movement;
- armor/weapon proficiency writers;
- class progression reads.

### Replace rather than wrap

- administrative final-Armor-Class writes;
- monolithic creation-record writes and stored total level;
- aggregate Condition/Exhaustion writes;
- Initiative-order and encounter-turn cursor persistence;
- boolean/imperial turn-budget state transitions;
- embedded item-activity execution;
- rest episode lifecycle persistence.

## Dependency tree

```text
Canonical mechanics on the complete ECS model [planning]
├── 40-to-154 component crosswalk [verified]
├── Permanent target keys and schema meanings [Sol-routed convergence decisions confirmed by user]
├── Compatibility and live-state migration policy [HI no-state path verified; remaining policy planned]
├── Exact/near-shape state cohort [verified]
│   ├── Heroic Inspiration marker [verified]
│   ├── character identity/profile [verified]
│   ├── Experience [verified]
│   ├── Hit Points [verified]
│   └── Temporary Hit Points [verified]
├── Normalized creature state cohort [depends on near-shape cohort]
│   ├── abilities and body
│   ├── unified proficiencies and languages
│   ├── movement and exact metric units
│   └── derived defenses and damage responses
├── Encounter lifecycle cohort [depends on creature state]
│   ├── participant Initiative
│   ├── explicit rounds and turns
│   └── counted turn budgets and reaction windows
├── Item/inventory cohort [runtime core verified; static catalog migration active]
│   ├── decomposed item definitions
│   ├── definition-linked instances and quantity [verified]
│   └── equipment, activities, and custody relationships
├── Character content/creation cohort [depends on normalized state + item cohort]
│   ├── species/background/class/feat definitions
│   ├── source-complete choices, grants, and progressions
│   └── atomic character creation without a monolithic duplicate record
├── Conditions/effects cohort [depends on activity execution]
└── Rest cohort [blocked on rest-policy target owner]
```

The remaining broad canonical component surface provides activity/effect, spellcasting, resource,
exploration, hazard, magic-item, monster, vehicle, crafting, and play-state owners needed by future
mechanics. It is owned directly by the application catalog rather than a parallel prototype.

## Ordered leaves

| Order | Leaf | Depends on | Exit gate |
| --- | --- | --- | --- |
| 1 | Confirm target ownership | crosswalk | permanent IDs, aliases, retirement policy, and no-dual-authority rule confirmed |
| 2 | Define migration policy | leaf 1 | fresh and non-empty campaign paths, rollback, replay, and old-ID rejection are testable |
| 3 | Heroic Inspiration seam | leaves 1-2 | **verified:** [receipt](evidence/DND2024-COMPONENT-CONVERGENCE-HEROIC-INSPIRATION-RECEIPT.md) proves the exact marker migration and retained grant behavior without dual writes |
| 4 | Near-shape state cohort | leaf 3 | **verified:** [receipt](evidence/DND2024-COMPONENT-CONVERGENCE-NEAR-SHAPE-RECEIPT.md) proves identity, Experience, HP, and Temporary HP migration with compatible mechanic results |
| 5 | Normalized creature state | leaf 4 | **verified:** unified source-qualified proficiencies plus relationship-derived total level/Proficiency Bonus and source-derived Armor Class |
| 6 | Encounter lifecycle | leaf 5 | **verified:** [receipt](evidence/DND2024-COMPONENT-CONVERGENCE-ENCOUNTER-LIFECYCLE-RECEIPT.md) proves explicit participation, Initiative, rounds, turns, counted metric budgets, replay, and rollback without snapshot/cursor authorities |
| 7 | Item/inventory convergence | leaf 5 plus metric catalog migration | **active:** [runtime-core receipt](evidence/DND2024-COMPONENT-CONVERGENCE-ITEM-RUNTIME-CORE-RECEIPT.md) proves definition-linked instances, positive quantities, generic custody, conservation, readers, replay, and rollback; decomposed metric definitions remain |
| 8 | Character content/creation | leaves 5 and 7 | one source-complete creation root writes only target owners |
| 9 | Conditions/effects | activity execution prerequisite | each effect is independently addressable and existing D20/damage consequences retain parity |
| 10 | Rest | rest-policy target owner | begin, progress, interrupt, and later completion share one clock-authoritative lifecycle |

## Verified seam proof

The Heroic Inspiration marker migrated to `dnd2024.character.heroic-inspiration` as the first seam
proof. The audited live database contained no affected definition or state, so the accepted slice
required no live migration and introduced no compatibility alias. The existing grant mechanic
retains its behavior and now writes only the target marker. Focused replay/no-change behavior, fresh
catalog import, D&D regression, and the full solution are recorded in the receipt.

The next accepted cohort migrated character identity, Experience, Hit Points, and Temporary Hit
Points together and adapted all ten existing JavaScript consumers. Mutable XP/HP state no longer
duplicates rule citations, identity includes player notes, HP consumers preserve optional maximum
reduction, and Temporary HP uses an ECS source reference. Its focused, fresh-import, D&D-wide, and
full-solution evidence is recorded in the NS1 receipt.

The first normalized-state Sol slice corrected provisional ability/movement references and replaced
the five split proficiency owners with `dnd2024.creature.proficiencies`. Armor training, saving
throws, skills/Expertise, tools, and weapon memberships now share one source-qualified owner with
explicit family coverage. Existing mechanic/procedure IDs remain stable; the old component
descriptors are retired. The complete compatibility evidence is recorded in the normalized-
proficiencies receipt.

The second normalized-state Sol slice retired stored total level and final Armor Class. Characters
now relate to independently addressable class-membership entities, and total level plus Proficiency
Bonus are effect-free derivations across those memberships. Creature defense state selects a
source-owned defense basis; the first active basis derives ordinary unarmored Armor Class from
Dexterity. A bounded generic projection declaration exposes only requested relationship-endpoint
components, with revision tracking and no D&D knowledge in C#. Character creation and every active
level/AC consumer now use those target owners. The complete compatibility evidence is recorded in
the [derived-level-and-Armor-Class receipt](evidence/DND2024-COMPONENT-CONVERGENCE-DERIVED-LEVEL-ARMOR-CLASS-RECEIPT.md).

The encounter-lifecycle Sol slice retired the encounter order snapshot, mutable turn cursor, and
participant-owned Boolean budget. Initiative now materializes independently addressable encounter
participations with locked results; start/advance/end create explicit round and turn entities and
move active relationships transactionally. Each turn owns counted resources and exact metric
movement spending, while a participant's latest turn remains the bounded off-turn Reaction budget.
The [encounter-lifecycle receipt](evidence/DND2024-COMPONENT-CONVERGENCE-ENCOUNTER-LIFECYCLE-RECEIPT.md)
records that cohort's compatibility evidence. The item runtime core is also accepted through its
[receipt](evidence/DND2024-COMPONENT-CONVERGENCE-ITEM-RUNTIME-CORE-RECEIPT.md):
runtime items now use one definition link, positive quantity, and generic containment without the
old instance/count owners. Decomposing and metricating static item definitions is the next ordered
sub-slice of leaf 7.

## Confirmation gates

Before runtime work, confirm:

1. whether remaining unconsumed canonical component keys have complete mechanic ownership;
2. whether old canonical keys are migrated atomically or temporarily accepted read-only;
3. that final Armor Class and total character level become derived rather than stored authority;
4. the zero-quantity deletion/absence invariant for item stacks;
5. the missing numeric ability-assignment and immutable rest-policy owners; and
6. each completed migration cohort after full compatibility and rollback evidence.

## Planning receipt

- Covered current components: 40 of 40, exactly once.
- Referenced target component keys: existing canonical application keys only.
- The planning phase created no runtime artifacts. The first accepted implementation seam is now
  recorded by the Heroic Inspiration convergence receipt, and the accepted near-shape cohort is
  recorded by the NS1 receipt.
- Database state and public operations remain unchanged; retired-source evidence is preserved under
  canonical ruleset evidence paths.
