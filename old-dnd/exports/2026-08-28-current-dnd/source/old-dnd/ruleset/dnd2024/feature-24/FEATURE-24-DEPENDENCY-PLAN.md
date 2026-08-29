# Feature 24 dependency plan — armor, shields, and derived Armor Class

> **D&D implementation reference:** When this work includes D&D 2024 mechanics, inspect [Foundry VTT dnd5e](https://github.com/foundryvtt/dnd5e) before designing or coding. Use it as a licensed engineering reference—not a direct dependency or rules authority—while retaining the exact `source.dnd2024.srd-5.2.1` locator and all applicable MIT, CC-BY, and asset-license notices for any reused material.

Status: **Slices 1–4 accepted; training penalties, Speed, and timed don/doff remain blocked by their named seams.**
Last updated: 2026-08-21

## Execution rule

This plan records completed repository implementation slices. Slice 1 revises the existing
Feature-23 static item-definition contract and catalog definitions only. Slice 2 owns only the
closed armor-training record and effect-free diagnostic reader. Slice 3 derives direct worn armor
and held Shield selections without a rule consequence. A later implementation pass must re-read
current contracts, select exactly one verified next slice, validate a disposable import, record
evidence, and stop.

## Target capability

A creature's Armor Class, armor-training drawbacks, and heavy-armor Speed penalty can be determined
from authoritative Dexterity and directly equipped physical armor or Shield, while retaining an
explicit future path for alternative base-AC sources such as class features and natural armor.

### Included

- Immutable source-cited definitions for the twelve SRD mundane armor suits and Shield.
- Armor category, base-AC formula, Dexterity allowance, Strength threshold, Stealth disadvantage,
  equipment mode, and don/doff duration as static item facts.
- One worn armor and one held Shield at a time, derived from Feature 23 custody/equipment state.
- Default 10 + Dexterity base AC, armor base AC, and trained Shield bonus.
- Armor-training state plus untrained armor and Shield consequences.
- Heavy-armor Speed reduction and derived Dexterity (Stealth) disadvantage.

### Excluded

- Class/species/feat training grants and alternative base-AC calculations (Features 26–27).
- Monster natural armor (Feature 35); magic armor, attunement, and magical stacking (Feature 29).
- Weapon hand use/two-weapon fighting, pricing/economy, crafting/durability, and equipment UI.
- Clock advancement, interruption, and combat-time management for minute-based don/doff work.

## Official source basis

The registered source is source.dnd2024.srd-5.2.1: System Reference Document 5.2.1
(Wizards of the Coast LLC, 2025-05-01, CC-BY-4.0), [Equipment > Armor, PDF p. 91](https://media.dndbeyond.com/compendium-images/srd/5.2/SRD_CC_v5.2.pdf), and [Rules Glossary > Armor Class and Armor Training, PDF p. 176](https://media.dndbeyond.com/compendium-images/srd/5.2/SRD_CC_v5.2.pdf).

- The default base AC is 10 plus Dexterity modifier; only one base calculation applies.
- Light armor uses full Dexterity modifier, Medium caps it at +2, Heavy adds none; Shield adds +2.
- Untrained armor gives Disadvantage on Strength/Dexterity D20 Tests and prevents spellcasting.
  An untrained Shield gives no AC bonus. Armor still provides its table base AC when worn.
- Listed Strength requirements reduce Speed by 10 feet when unmet. Marked armor gives
  Disadvantage on Dexterity (Stealth) checks.
- Only one suit and one Shield may be used. Light takes one minute to don/doff; Medium 5/1;
  Heavy 10/5; Shield uses a Utilize action to don/doff.

## Planning inventory and overlap result

| Inquiry | Repository evidence and conclusion |
| --- | --- |
| Final AC | Feature 6 owns dnd2024.armor-class as a manually recorded final integer, and Feature 8 reads it directly. Leaving that owner unchanged creates competing manual and derived AC truths. |
| Dexterity | Feature 3 owns ability scores and modifiers. Armor cannot store a copied Dexterity modifier. |
| Equipment | Feature 23 supplies immutable definitions, instances, custody, held/worn state, and an item-state reader. It deliberately has no armor, AC, slot, timing, or action rule. |
| Definitions | The current item-definition kind enum has weapon/currency/gear only. Armor and Shield kinds/profile facts require a reviewed schema and catalog migration. |
| D20 effects | Features 3, 4, and 8 own D20 circumstances. Feature 13 state effects are condition-specific; untrained armor needs a separate derived-equipment effect reader, never caller-supplied disadvantage. |
| Speed | Feature 20 owns base Speed and Feature-12 movement refresh. Armor must modify effective Speed at use time, not stored base Speed or a second budget. |
| Training | No current armor-training owner exists. Feature 24 owns the closed state/reader; Features 26, 27, and 35 later provide grants. |
| Timing | Feature 12 spends an Action but does not define its meaning or advance time. No verified minute-duration/interruption owner exists. |

## Recursive dependency analysis

~~~text
Feature 24: armor, shields, and derived Armor Class
├─ source rules                                                [implemented source basis]
├─ abilities / modifiers                                       [implemented: Feature 3]
├─ legacy final-AC record                                      [deprecated historical record: Feature 6]
├─ item definitions, custody, and equipment state              [implemented: Feature 23]
├─ Action allowance                                            [implemented: Feature 12]
├─ armor/shield static profile and definitions                 [implemented: Slice 1]
├─ armor-training state and reader                             [implemented: Slice 2]
├─ equipped-item aggregation/exclusivity reader                [implemented: Slice 3]
├─ derived AC reader and Feature-6/8/22 migration              [accepted: Slice 4]
├─ derived D20 equipment effects                               [blocked]
├─ effective-Speed integration                                 [blocked: Feature 20]
├─ timed don/doff lifecycle                                    [blocked: clock owner]
└─ player-facing armor/shield transitions                      [blocked]
~~~

Slice 1 is static source data under Feature 23's immutable-definition owner. It does not claim that
a creature's AC, training, equipment state, movement, or action has changed.

## Dependency and ownership decisions

1. Armor facts are immutable item-definition data. Extend Feature 23 with armor and shield kinds
   and one closed armorProfile: category, base AC, Dexterity rule (full, max-2, none), optional
   Strength minimum, Stealth flag, and source-cited don/doff duration. Shield has AC bonus 2 and
   held eligibility; a suit has worn eligibility. Instances copy no profile facts.
2. Equipment state remains the only equipped-state record. Readers derive legal direct custody,
   held/worn state, and one-at-a-time selection. They do not add wornArmorId or shieldId to a
   creature or infer missing equipment.
3. Base AC is derived, not an equip side effect. An effect-free AC calculator reads Dexterity,
   validated equipped profile facts, and an explicit future alternative-base selector. Feature 6/8
   must migrate together so manual final AC cannot coexist as a competing source of truth.
4. The initial calculator has only default and armor base calculations. Later class/species/natural
   sources register eligible bases and a selector chooses one; bases never stack. Shield is a bonus,
   and only trained Shield use supplies it.
5. Feature 24 owns a closed armor-training record: light, medium, heavy, shield, canonical order,
   and source attribution. Missing means training unknown, never automatically untrained.
6. Armor drawbacks are derived equipment effects, not conditions. A reader supplies source-backed
   D20 circumstances, Shield eligibility, and effective walk adjustment. It cannot forge condition
   evidence or persist disadvantage.
7. Feature 20 consumes the minus-10 adjustment when calculating usable walking movement. Base Speed
   and Feature-12 spending remain unchanged.
8. Feature 23 generic equipment changes are not proof a timed don/doff completed. A future armor
   parent must compose verified state effects with Utilize/clock completion.

## Confirmation boundary

| Decision | Required confirmation |
| --- | --- |
| Item schema | Exact kind/profile shape, definition migration, source locator, and no price/economy field. |
| Seed data | Permanent ids, masses, modes, and all twelve suits plus Shield table facts. |
| Training | Component/writer ids, missing semantics, provenance, and future class/species/monster grant paths. |
| Aggregation | Direct custody, worn/held validation, duplicate failure, contents projection, and corrupt state behavior. |
| AC migration | Fate of Feature-6 manual AC, reader envelope, Feature-8 target requirements, fixtures, and natural-armor seam. |
| D20 effects | Result shape and every Strength/Dexterity D20 consumer; spellcasting prohibition handoff to Features 31–32. |
| Speed | Feature-20 effective-speed ordering with conditions/exhaustion and a zero floor. |
| Timing | Clock owner, Utilize binding, completion/interruption/cancellation, encounter policy. |

## Slice order and stop gates

| Order | Slice | Starts only when | Exit gate |
| --- | --- | --- | --- |
| 1 | Static armor/Shield profile and definitions | **Verified.** | All twelve suits and Shield have validated static data; no instance, state, AC, or action changes. |
| 2 | Armor-training state/reader | **Verified.** | Closed state reports category eligibility without inferred grants or AC effect. |
| 3 | Equipped armor aggregation reader | **Verified.** | Effect-free reader identifies at most one direct worn suit and held Shield, rejecting invalid/exclusive state. |
| 4 | Derived AC calculator/migration | **Accepted.** | Weapon and Unarmed Strike consume calculated AC; the legacy manual writer is retired without a second truth. |
| 5 | Armor-effects reader/D20 consumption | Slices 2–3 and D20 contracts re-read. | Untrained armor and Stealth penalties derive once, merge correctly, and cannot be caller-forged. |
| 6 | Effective-Speed integration | Slice 3 and Feature-20 speed contract. | Unmet Strength reduces usable walk movement exactly 10 feet without changing base Speed/budget ownership. |
| 7 | Don/doff lifecycle | Slices 1–3, Feature 12, clock/action owner. | Shield uses verified Utilize timing; suits complete only after exact elapsed duration. |

## Slice 1 — immutable armor/shield profile data and definitions

### Runtime artifacts

- **Implemented:** revision of Feature 23 item-definition schema/procedure and catalog validation
  coverage to permit armor and shield definition kinds plus closed armorProfile data.
- Twelve mundane armor definitions and one Shield definition, all source-cited and immutable.
- Focused fresh-import and schema tests.
- No physical instance, worn state, AC component, training component, D20 change, Speed, time, or
  runtime equip mechanic.

### Data and validation

An armor suit is a separate-stack armor item, eligible only to be worn, with category light, medium,
or heavy; base AC 11–18; Dexterity rule; optional 13/15 Strength threshold; Stealth flag; and
closed minute durations. A Shield is a separate-stack shield, eligible only to be held, with
AC bonus 2 and a closed Utilize don/doff descriptor. Both retain Feature 23 rational mass/source
attribution.

The initial definitions are Padded, Leather, Studded Leather; Hide, Chain Shirt, Scale Mail,
Breastplate, Half Plate; Ring Mail, Chain Mail, Splint, Plate; and Shield. Price is deliberately
absent because Feature 23 excludes economy.

### Acceptance matrix and exit gate

| Case | Exact assertion |
| --- | --- |
| Table facts | Each definition records exact category/base/Dex/Strength/Stealth/time/mass/mode from the SRD table. |
| Closed shape | Invalid kind/category/formula/base/Dex combination/threshold/time/shield bonus/mode/extra field rejects. |
| Definition discipline | Stable immutable ids and source refs; existing weapon/currency/gear definitions retain valid byte-identical shapes. |
| No mechanics yet | Import creates no instance, equipped state, AC calculation, training state, budget, or D20 behavior change. |
| Compatibility | Fresh validation/import and Feature-23 definition/instance tests pass; no player-facing wear-armor routing is added. |

**Verified.** The source table, invalid shapes, and existing inventory compatibility are covered by
the focused suite and disposable catalog validation recorded in [the Slice 1 receipt](FEATURE-24-SLICE-1-RECEIPT.md).
Do not calculate AC, create armor training, apply penalties, write worn state, or begin timing.

## Slice 2 — armor-training state and diagnostics

**Verified.** `dnd2024.armor-training` records a complete canonical subset of Light, Medium, Heavy,
and Shield training with fixed SRD attribution. Its writer is closed to record/correct input and its
reader reports present/valid diagnostics without emitting effects. Missing state remains unknown;
an explicit empty set is known-no-training. The focused fresh-import test, disposable validation,
and full suite are recorded in [the Slice 2 receipt](FEATURE-24-SLICE-2-RECEIPT.md).

Do not add a class/species/monster grant, inspect equipped armor, calculate AC, apply an untrained
drawback, modify Speed, prevent spellcasting, or introduce an action/timing transition here.

## Slice 3 — direct equipped armor aggregation

**Verified.** `mechanic.dnd2024.armor-equipment.read` derives one direct worn suit and one direct
held Shield from Feature 23 custody/equipment state and immutable definitions. Explicitly
unequipped items produce no selection; nested items never qualify; duplicate or invalid direct
relevant state fails closed. The reader is effect-free and does not apply training, AC, D20, Speed,
spellcasting, action, timing, burden, or capacity rules. Evidence is in [the Slice 3 receipt](FEATURE-24-SLICE-3-RECEIPT.md).

## Slice 4 — derived Armor Class calculator and combat migration

**Accepted.** `mechanic.dnd2024.armor-class.read` derives default/Light/Medium/Heavy Armor Class
and a trained direct-held Shield bonus from authoritative Dexterity, direct equipment, and explicit
training state. Weapon Attack and Unarmed Strike each compose exactly one result. The legacy manual
writer is deprecated and legacy `dnd2024.armor-class` state is deliberately not a fallback. Fixed
Feature 10 fixtures now preserve their AC through actual worn armor. Evidence is in
[the Slice 4 receipt](FEATURE-24-SLICE-4-RECEIPT.md).

Do not add alternative/natural/magical bases, armor drawbacks, Speed, spellcasting, equipment
mutation, or don/doff timing to this accepted boundary.

## Later-slice invariants

- At most one direct worn suit and one direct held Shield count. Invalid/missing/corrupt/duplicate
  candidates fail closed rather than selecting an arbitrary winner.
- Armor replaces the default base; Shield adds only when trained and is never a base. Unknown
  training cannot silently grant Shield AC.
- Untrained armor supplies the stated D20 drawbacks but retains its armor-table base AC. The
  spellcasting restriction waits for Feature 31/32, not an invented failure here.
- Strength shortfall changes effective walk movement only; it cannot overwrite base Speed, movement
  remaining, or condition/exhaustion effects.
- Timed don/doff cannot change state before completion; invalid, interrupted, or failed transitions
  leave every state unchanged.

## Plan-quality audit

- Capability, source basis, owner search, graph, closed ownership, confirmation gates, routing,
  replay/effect boundaries, and three completed lowest slices: specified.
- Slice runtime artifacts and verification are recorded in the linked receipts; no persistent
  catalog import occurred.

## Plan-change rule

Stop and revise if Feature 23 changes definition/equipment semantics, Feature 20 selects an
incompatible effective-Speed design, Feature 27 already owns armor training, or the Feature-6/8
migration cannot prevent manual and derived AC from coexisting. Do not store Dexterity in armor,
treat Shield as worn armor, mutate base Speed, infer training, or treat generic equip as completed
timed don/doff.
