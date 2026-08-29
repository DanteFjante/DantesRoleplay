# D&D 2024 character creation dependency tree

Status: **all-class basic-playable MVP, CC1–CC2H2, CC3A–CC3C, CC3D1A, CC3E1–CC3E3, and CC3F1 verified**
Ruleset alignment: `dnd2024-owned`
Source: `source.dnd2024.srd-5.2.1`, *Character Creation > Create Your Character* and
*Step 2: Character Origin* and *Step 3: Ability Scores* (PDF pp. 19–21); *Character Origins >
Character Backgrounds > Parts of a Background / Acolyte / Criminal / Sage / Soldier* (PDF p. 83);
*Rules Glossary > Long Rest* (PDF p. 185) and
*Rules Glossary > Short Rest* (PDF p. 187)
Owning roadmap: [D&D 2024 application roadmap](ROADMAP.md)

Token-constrained delivery may use the separate
[basic character-creation MVP plan](DND2024-CHARACTER-CREATION-MVP-PLAN.md). It creates an explicitly
`basic-playable` actor with a durable unresolved-entitlement ledger in one accepted vertical slice;
it does not replace or falsely satisfy this source-complete dependency tree.
The accepted [all-class expansion](DND2024-CHARACTER-CREATION-ALL-CLASS-IMPLEMENTATION.md) adds
source-bound level-1 models for all twelve SRD classes to that same incomplete-but-honest path.

## Outcome and non-goals

Create one source-cited level-1 D&D 2024 character through a complete, stateless request and one
atomic transaction. Content declarations supply class, species, background, feat, and equipment
grants; the coordinator consumes their exact component dependencies and never hard-codes a content
ID or D&D rule in C#.

This tree does not declare partial sheets complete, add a server-side wizard draft, implement a UI,
or activate optional/homebrew behavior in `dnd2024-core`.

## Existing owners and evidence

| Concern | Owner | State | Evidence |
| --- | --- | --- | --- |
| Raw ability state | `dnd2024.abilities` | verified | active schema/procedure and ability/check consumers |
| Level and Proficiency Bonus | `dnd2024.character-level` | verified | active recorder; bonus remains derived |
| Profile | `dnd2024.character.profile` | verified | active explicit record/correct mechanic |
| Skills, saves, languages, tools, weapons | existing independent D&D components | verified | active schemas and recorders |
| HP, AC, Speed, Size | existing independent D&D components | verified for the MVP | basic creation derives class HP and baseline unequipped AC, and applies selected Size/base Speed; source-complete alternatives and grants remain later |
| Class identity/progression | `dnd2024.character.content-definition`, `dnd2024.class-progression`, and `dnd2024.class-creation-profile` | verified models; partial behavior | all twelve SRD classes have source-bound level-1 models and feature identities; Fighter also has level 2; feature behavior remains explicitly unimplemented |
| Atomic effect application | generic `ApplicationActionRunner`/typed effects | verified | current multi-effect actions replay and roll back transactionally |
| Absent-entity composition | generic `IStagedWorldComposer` | verified | accepted generic staged-world owner under `src/system/state/` |
| Campaign participation | Campaign C15 meaning plus D&D application-local relationship kinds | verified for the MVP | basic create atomically writes active participation and both links; source-complete composition remains later |
| Background declarations/fixed grants | `dnd2024.background-creation-profile` plus existing proficiency owners | verified CC3A; partial behavior | all four SRD models compose with all twelve classes; fixed skills/tools/Common apply, while choices, equipment, and feat behavior remain pending |
| Species/feat behavior grants | D&D catalog JavaScript/content | missing | adoption Parent 11 gate requires family schemas and a transactional grant owner |

## Dependency tree

```text
Complete level-1 character creation                                      [planned]
├─ source-bound ability generation and background increases              [verified CC1]
├─ species definition, selection, traits, Size, and Speed                 [partial: selection/Size/Speed verified]
├─ background skills, tool, feat, languages, and equipment grants         [partial: CC3A–CC3C proficiencies/choices/feat identity verified]
├─ class membership, saves, proficiencies, HP, and level-1 feature grants [partial: 12 models/core state/feature identity/armor training verified]
├─ feat behavior for every supported origin path                          [planned]
├─ immutable selection/grant/completion receipts                          [partial: basic selections and feature grants verified]
└─ one staged, campaign-attached create transaction                       [verified for basic-playable]
   └─ stateless discovery/validation handoff                              [planned]
```

## Conflicts and decisions

- The retained `old-dnd/` character implementation is recovery evidence only. Its useful policy
  and background declaration shapes may be re-adopted, but its C# D&D validators and parallel
  world model are forbidden.
- `dnd2024.abilities` remains the sole raw-score owner. Creation resolvers return canonical data;
  they do not store modifiers or a second ability record.
- Background increases are resolved from the selected background entity's declared component,
  not from a `switch` on Soldier or another permanent content ID.
- `dnd2024-core` remains SRD-faithful. Optional content must arrive through a separately selectable
  source profile before campaign creation.

## Ordered leaves

| Order | Leaf | Recommended model | Depends on | Exit gate |
| --- | --- | --- | --- | --- |
| 1 | CC1 ability generation and background increases | `gpt-5.6-terra` high | ability owner, immutable catalog content, projection roles | **Verified** by [the CC1 receipt](evidence/DND2024-CHARACTER-CREATION-CC1-RECEIPT.md): Standard Array and point-cost declarations plus both legal Soldier patterns resolve deterministically with no effects; malformed, source-drifted, ineligible, over-cap, or derived input fails closed. |
| 2 | CC2 species selection/grant family | See the CC2 table below; each remaining row is a required sub-slice | CC1 plus species schemas and trait owner map | **CC2A–CC2H2 verified.** Spell/exertion adapters, finish/recovery, Resourceful, remaining species grants, and final composition still block this family. |
| 3 | CC3 background/class/feat grant families | See the CC3 table below; each remaining row requires its own active document | CC1–CC2 and each target owner | **CC3A–CC3C and CC3E1–CC3E3 verified.** Feature identities, armor training, selectable class tools, and restricted Martial membership are durable; one source-complete origin/class path still requires behavior, remaining class state, and equipment. |
| 4 | CC4 atomic creation root | `gpt-5.6-sol` max; subdivide validation, staged creation, attachment, and rollback | CC1–CC3, participation, items, staged composition | Validate is effect-free; create commits actor, grants, containment, participation, and receipt together or not at all. |
| 5 | CC5 stateless discovery and guided handoff | `gpt-5.6-terra` high; use `gpt-5.6-sol` high for final cross-surface review | accepted create root | A fresh client discovers choices, validates, creates, reads, and plays without structural knowledge. |

## CC2 sub-slices and named model assignments

The recommendation follows current OpenAI model roles: Luna for bounded high-volume catalog work,
Terra for balanced implementation, and Sol for cross-owner or transaction-heavy reasoning. A larger
row must be divided again in its active implementation document before code changes.

| Slice | Boundary | Recommended model | State |
| --- | --- | --- | --- |
| CC2A | Nine species profiles and selection planning | `gpt-5.6-luna` high | verified |
| CC2B | Human Skillful resolution | `gpt-5.6-terra` high | verified |
| CC2C | Human Versatile with recommended Skilled | `gpt-5.6-terra` high | verified |
| CC2D | Shared Heroic Inspiration presence/grant | `gpt-5.6-terra` high | verified |
| CC2E | Immutable standard-rest policy | `gpt-5.6-luna` high | verified |
| CC2F | Authenticated rest start | `gpt-5.6-terra` high | verified |
| CC2G | Clock-derived rest progress and explicit interruption | `gpt-5.6-sol` high | verified |
| CC2H1 | Automatic weapon-damage interruption | `gpt-5.6-terra` high; Sol high review | verified |
| CC2H2 | Automatic Initiative interruption | `gpt-5.6-sol` high | verified |
| CC2H3 | Automatic non-Cantrip-spell interruption | `gpt-5.6-sol` xhigh | blocked: no spell execution root/component exists |
| CC2H4 | Automatic one-hour walking/physical-exertion interruption | `gpt-5.6-sol` high | blocked: needs a D&D wrapper/accumulator over base timed travel; combat movement cannot safely stand in for the one-hour rule |
| CC2I1 | Hit Point Dice authoritative state, spend, and restore-all owner | `gpt-5.6-sol` high | planned; class/multiclass derivation boundary must be confirmed in its active document |
| CC2I2 | Normal/current HP maximum and ability-score reduction owners | `gpt-5.6-sol` xhigh | planned; must preserve current raw-score/HP owners without duplicate authority |
| CC2I3 | Finish Short/Long Rest and compose all source recovery atomically | `gpt-5.6-sol` max | blocked on CC2I1–CC2I2 and source-specific recharge declarations |
| CC2J | Human Resourceful Long Rest grant | `gpt-5.6-terra` high; Sol high review | blocked on CC2I3 |
| CC2K1 | Dragonborn trait/grant family | `gpt-5.6-sol` xhigh | planned |
| CC2K2 | Dwarf trait/grant family | `gpt-5.6-sol` xhigh | planned |
| CC2K3 | Elf lineage and trait/grant family | `gpt-5.6-sol` xhigh | planned |
| CC2K4 | Gnome lineage and trait/grant family | `gpt-5.6-sol` xhigh | planned |
| CC2K5 | Goliath ancestry and trait/grant family | `gpt-5.6-sol` xhigh | planned |
| CC2K6 | Halfling trait/grant family | `gpt-5.6-terra` xhigh; Sol high review | planned |
| CC2K7 | Orc trait/grant family | `gpt-5.6-sol` xhigh | planned |
| CC2K8 | Tiefling legacy and trait/grant family | `gpt-5.6-sol` xhigh | planned |
| CC2K9 | Final species-selection grant composition, including Human | `gpt-5.6-sol` max | blocked on CC2J and CC2K1–CC2K8 |

## CC3 sub-slices and named model assignments

| Slice | Boundary | Recommended model | State |
| --- | --- | --- | --- |
| CC3A | Four SRD background models plus fixed origin/class proficiency composition | `gpt-5.6-sol` high | verified by [the CC3A receipt](evidence/DND2024-CHARACTER-CREATION-CC3A-RECEIPT.md) |
| CC3B | Optional closed input for two Standard languages and selectable background tool, preserving the incomplete compatible path | `gpt-5.6-terra` high; Sol high review | verified by [the CC3B receipt](evidence/DND2024-CHARACTER-CREATION-CC3B-RECEIPT.md) |
| CC3C | Durable character feature-identity grants for background and class entitlements without implying behavior | `gpt-5.6-sol` high | verified by [the CC3C receipt](evidence/DND2024-CHARACTER-CREATION-CC3C-RECEIPT.md) |
| CC3D | Origin-feat behavior families, including Magic Initiate selections | `gpt-5.6-sol` xhigh | blocked on spell selection/execution owners for Magic Initiate; subdivide per feat |
| CC3D1A | Alert Initiative Proficiency opt-in through existing grant, level, and Initiative owners | `gpt-5.6-sol` xhigh | verified by [the CC3D1A receipt](evidence/DND2024-CHARACTER-CREATION-CC3D1A-RECEIPT.md) |
| CC3E | Remaining class armor/tool/feature/resource grant families | `gpt-5.6-sol` xhigh to max | planned; subdivide by canonical state/effect owner |
| CC3E1 | Restore the armor-training state owner and apply exact level-1 class grants | `gpt-5.6-sol` high | verified by [the CC3E1 receipt](evidence/DND2024-CHARACTER-CREATION-CC3E1-RECEIPT.md) |
| CC3E2 | Resolve optional Bard/Monk level-1 class tool choices through the existing membership owner | `gpt-5.6-sol` high | verified by [the CC3E2 receipt](evidence/DND2024-CHARACTER-CREATION-CC3E2-RECEIPT.md) |
| CC3E3 | Preserve Monk/Rogue property-qualified Martial weapon membership without claiming attack enforcement | `gpt-5.6-sol` xhigh | verified by [the CC3E3 receipt](evidence/DND2024-CHARACTER-CREATION-CC3E3-RECEIPT.md) |
| CC3F | Background and class starting-equipment/cash selection and item instantiation | `gpt-5.6-sol` max | planned; depends on a source-bound package planner and atomic item composition |
| CC3F1 | All-cash background/class alternative with one physical Gold Piece stack | `gpt-5.6-sol` xhigh | verified by [the CC3F1 receipt](evidence/DND2024-CHARACTER-CREATION-CC3F1-RECEIPT.md) |

### Current CC2 readiness blockers

- **CC2H3:** the active D&D catalog has no spell definition, spell-slot, prepared-spell, Magic
  action, or spell execution mechanic. Attaching interruption to a content identity would not prove
  that a non-Cantrip spell was cast.
- **CC2H4:** `mechanic.game.core.world.route.travel-on-foot` owns timed base-world travel and clock
  advancement, while the D&D combat movement budget owns only feet per turn. The SRD threshold is
  one hour; interrupting per movement spend would over-count and teaching the base mechanic D&D
  state would reverse the application dependency.
- **CC2I:** HP and Exhaustion have owners, and class progression declares Hit Die sides, but no
  character Hit Point Dice state exists. Current HP/ability components also do not retain normal
  values after a reduction, so a finish root cannot source-faithfully restore them yet.
- **CC2J/CC2K9:** Resourceful is a Long Rest completion benefit and therefore cannot be granted from
  duration readiness alone.

## Lowest ready leaf

CC1 and [CC2A](DND2024-CHARACTER-CREATION-CC2A-IMPLEMENTATION.md)–[CC2G](DND2024-CHARACTER-CREATION-CC2G-IMPLEMENTATION.md)
and [CC2H1](DND2024-CHARACTER-CREATION-CC2H1-IMPLEMENTATION.md)–[CC2H2](DND2024-CHARACTER-CREATION-CC2H2-IMPLEMENTATION.md)
are verified. [CC3A](DND2024-CHARACTER-CREATION-CC3A-IMPLEMENTATION.md) is also verified and makes
all four SRD backgrounds selectable with fixed skills/tools/Common applied. CC3B is verified and
optionally completes the two Standard languages and Soldier Gaming Set while preserving the
incomplete request. CC3C is verified and persists exact source-bound background/class feature
identities without claiming their behavior. CC2H3 cannot activate
until an accepted non-Cantrip spell execution root exists.
Physical exertion, finish/recovery, Resourceful, remaining species grants, and source-complete actor
composition remain later leaves; no such leaf is active. The separate
[basic-playable MVP](evidence/DND2024-CHARACTER-CREATION-MVP-RECEIPT.md) is accepted and creates an
honest incomplete actor without satisfying those leaves. Its
[all-class expansion](DND2024-CHARACTER-CREATION-ALL-CLASS-IMPLEMENTATION.md) supplies correctly
schematized level-1 class models while leaving absent mechanics pending. Magic Initiate remains
blocked on spell owners, while CC3D1A verifies Alert Initiative Proficiency and keeps Initiative
Swap separate. CC3E1 restores the canonical armor-training owner and applies all class grants.
CC3E2 verifies optional Bard/Monk class tool choices and the complete 37-tool vocabulary. CC3E3
verifies Monk/Rogue restricted-Martial membership while leaving property-aware enforcement honest.
CC3F1 verifies the all-cash starting-equipment alternative as one physical contained Gold Piece
stack across all 48 background/class pairs. Conditional attacks, physical packages/nested
reference automapping, and class resources remain later boundaries.

Foundry dnd5e commit `275bed0be4ccfa15e6b3347acccb8da8784726d9` was reviewed at
`module/data/item/background.mjs`,
`module/documents/advancement/ability-score-improvement.mjs`, and
`module/applications/advancement/advancement-manager.mjs`. Useful reference behavior is the
separation of content declarations from selected advancement values, staging choices on a clone,
enforcing ability caps, and applying the final actor/item delta in one bulk completion. No Foundry
code, data, or assets are runtime dependencies.

## Confirmation gates

The user's 2026-08-27 request to implement character creation confirms CC1's permanent component,
content, mechanic, and procedure IDs inside this bounded SRD-faithful leaf. It does not confirm a
new MCP kind, database migration, optional rule, public endpoint, or the later completion receipt.
Each later leaf requires its own active implementation document after every rule behavior it grants
has an owner.

## Planning receipt

- Runtime artifacts created by planning: none.
- Selected token-constrained delivery: CC-MVP and its twelve-class level-1 model expansion are
  accepted; the source-complete tree remains open.
