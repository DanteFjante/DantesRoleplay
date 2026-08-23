# D&D 2024 roadmap — from one test session to complete play

Status: **E1 verified; this document supplies rationale for the primary Feature 11–38 index.**
Last updated: 2026-08-20

## Purpose and authority

`ROADMAP.md` orders Features 1–10, whose end state is one reproducible vertical test session:
a check, an Initiative order, an attack, damage, a save. This document orders what comes after
it, so that "what is left for complete D&D" is a list rather than a feeling.

It is a **planning inventory, not a plan**. Every row still requires its own recursive dependency
plan under `TERRA-FEATURE-PLANNING-GUIDE.md`, its own contract, and its own slice-by-slice
implementation under `procedure.system.create-feature` v4. A row here means "this is missing and
this is where it sits in the order", nothing more.

The rule source remains SRD 5.2.1 (`source.dnd2024.srd-5.2.1`, CC-BY-4.0). Anything outside the
SRD — published adventures, non-SRD subclasses, setting material — is out of scope everywhere in
this document and is not a gap to be closed.

## The honest summary

Features 1–10 give you a fight between two creatures where nobody has equipment, nobody moves,
nothing recovers, and nothing happens on its own. That first reproducible-session milestone is
complete; it is still roughly a tenth of what running a campaign asks for.

The remaining work divides into four tiers. The order matters: **Tier E is a prerequisite, not an
afterthought.** Reactions, conditions that expire, concentration and death saves are all "something
happens because something else happened", and the kernel has no events. Building conditions before
events means building conditions twice.

---

## Tier E — engine capabilities the ruleset cannot fake

These are kernel features, not game rules. Each already has a repository plan except where noted.

| # | Capability | Depends on | Why the ruleset is blocked without it | Plan |
| --- | --- | --- | --- | --- |
| E1 | Pre-commit event guards, subscriptions, deterministic event chains, and tracked-item notifications | composition | Immunities/wards must veto proposed changes before commit; conditions, reactions, concentration, and tracked notices depend on accepted changes. | Verified — see [receipt](../../platform/e1/EVENTS_AND_SUBSCRIPTIONS_RECEIPT.md) |
| E2 | Selection ranking that respects phrases | — | A direct player match phrase now outranks incidental name and description tokens. Exact phrase collisions still need authoring controls as the catalog grows. | Verified by `MechanicStoreTests` |
| E3 | Hierarchical catalog navigation | — | Categories are already dotted paths; nothing can browse a branch. A GM cannot find one spell among hundreds by scrolling a flat list. | `HIERARCHICAL_CATALOGS_PLAN.md` |
| E4 | Local intent routing | E2, E3 | Turns "I swing at the goblin with my axe" into the right rule plus the right roles without the GM model spending its context on lookup. | `LOCAL_INTENT_ROUTING_PLAN.md` |
| E5 | Numeric fidelity across the sandbox boundary | — | Seeds are 64-bit and JavaScript numbers are not. Feature 5 worked around it by not storing seeds; a rule that ever needs an exact large integer has no honest option today. | none yet |

**Non-goals for the whole tier:** a scheduler, real elapsed time, background simulation, or any
rule that runs without an action having been committed.

---

## Tier F — combat that behaves like combat

| # | Capability | Depends on | Deliberate non-goals |
| --- | --- | --- | --- |
| 11 | Turn and round lifecycle: whose turn, advance turn, round counter, end of encounter | Feature 5 complete | Simultaneous turns, initiative re-rolls per round, delaying or readying |
| 12 | Action economy: Action, Bonus Action, Reaction, Free Interaction, and one Move per turn, spent and restored | 11 | Legendary and lair actions, multiattack routines |
| 13 | Conditions as state: the SRD condition set attached, listed, and cleared, with their stated mechanical effects on checks, saves and attacks | 12, E1 | Homebrew conditions, condition immunity by species, stacking rules beyond the SRD's |
| 14 | Exhaustion levels, and their effect on D20 Tests and speed | 13 | Recovery pacing beyond the long-rest rule |
| 15 | Damage types, plus resistance, immunity and vulnerability applied in the SRD's order | Feature 9 | Damage transfer, shared damage, absorption |
| 16 | Temporary hit points and healing, including the rules that stop them stacking | Feature 6, 15 | Regeneration on a timer, healing over time |
| 17 | Dying: 0 HP, unconsciousness, death saving throws, stabilizing, instant death from massive damage | 13, E1 | Resurrection magic, lingering injuries |
| 18 | Concentration: one at a time, the save on taking damage, and what ends it | E1, 13 | Metamagic or feature-based exceptions |
| 19 | Reactions in play: opportunity attacks and triggered abilities | 12, E1 | Counterspell timing puzzles, held actions |
| 20 | Position and movement: speed, distance between participants, difficult terrain, and reach as a precondition for attacking | 11 | A rendered grid, pathfinding, flanking, elevation as a full 3D model |
| 21 | Cover and ranged combat: half and three-quarters cover, long range Disadvantage, firing into melee | 20 | [Slice 1 verified: static Shortbow 80/320 range data](feature-21/FEATURE-21-DEPENDENCY-PLAN.md); line of sight as geometry, projectile physics |
| 22 | Unarmed and improvised combat: unarmed strike, grapple, shove, two-weapon fighting | Feature 8, 20 | [Slice 1 verified: effect-free Unarmed Strike Damage evidence](feature-22/FEATURE-22-DEPENDENCY-PLAN.md); wrestling subsystems, called shots |

---

## Tier G — the character sheet

Until this tier exists, every character has to be assembled by hand-writing components, which is
exactly the "deciding an outcome yourself and writing it in as effects" that `procedure.world.change`
warns against.

| # | Capability | Depends on | Deliberate non-goals |
| --- | --- | --- | --- |
| 23 | Equipment and inventory: items as entities, carried through containment, currency, weight and the encumbrance option | Feature 7 | A shopping economy, crafting, item durability |
| 24 | Armor, shields, and Armor Class derived from what is actually worn | 23, Feature 6 | [Slice 1 verified: static mundane armor and Shield data](feature-24/FEATURE-24-DEPENDENCY-PLAN.md); natural armor formulas for every monster, magical AC stacking beyond the SRD |
| 25 | Weapon properties and mastery: finesse, versatile, thrown, loading, ammunition, and the 2024 mastery properties | 23, Feature 8 | [Slice 1 verified: static Dagger, Shortbow, and Battleaxe data](feature-25/FEATURE-25-DEPENDENCY-PLAN.md); a complete weapon catalog, with the SRD list as boundary |
| 26 | Species traits: the SRD species and their mechanical grants | Feature 2 | [Slice 1 verified: immutable SRD species profiles](feature-26/FEATURE-26-DEPENDENCY-PLAN.md); non-SRD species, custom lineages |
| 27 | Classes and levels: class features by level, hit dice, subclass choice, proficiency grants, multiclassing rules | Feature 2; CH4; C14; CH9; Feature 33 | [Slice 1 verified: immutable Fighter progression and reader](feature-27/FEATURE-27-DEPENDENCY-PLAN.md); non-SRD subclasses, homebrew progression |
| 28 | Backgrounds, feats, and ability score improvements | 27 | Non-SRD feats |
| 29 | Attunement and magic items: the SRD item set, attunement slots, and item-granted effects | 23, E1 | [Slice 1 verified: immutable representative magic-item profiles](feature-29/FEATURE-29-DEPENDENCY-PLAN.md); artifacts, sentient items, item creation |
| 30 | Character creation as one guided procedure that produces a legal, playable sheet | 23–28 | A visual character builder |

---

## Tier H — the world around the fight

| # | Capability | Depends on | Deliberate non-goals |
| --- | --- | --- | --- |
| 31 | Spellcasting resources: spell slots, known and prepared spells, spellcasting ability, spell save DC and attack bonus | 27 | [Slice 1 verified: immutable spell identities](feature-31/FEATURE-31-DEPENDENCY-PLAN.md); spell point variants, non-SRD spells |
| 32 | Spell resolution: spell attack rolls, spell saves, areas of effect, targeting and duration | 31, 18, 20 | [Slice 1 verified: immutable resolution profiles](feature-32/FEATURE-32-DEPENDENCY-PLAN.md); every SRD spell authored — that is content, tracked separately |
| 33 | Rests: short rest, long rest, hit dice spending, and what each resource recovers | 27, 14 | Gritty realism and other optional rest variants |
| 34 | Vision and light, hiding, passive Perception, and surprise at the start of an encounter | 20, 13 | Dynamic lighting as geometry |
| 35 | Monsters and stat blocks: creatures as data, CR, traits, actions, and encounter building | Feature 6–9, 27 | The full SRD bestiary as content; the capability comes first |
| 36 | Advancement: XP or milestone, levelling a character up through the class features it already has | 27; Campaign C14; Character CH9 | [Slice 1 implemented: character XP state and eligibility](feature-36/FEATURE-36-DEPENDENCY-PLAN.md); campaign awards, automatic optimisation, respec tooling |
| 37 | D&D travel pace and elapsed-time integration over the existing world routes, itinerary, and clock | 33, core world travel/time, E1 | Rebuilding routes, maps, clocks, conveyances, weather simulation, hex crawl generation |
| 38 | Social interaction: attitude, influence checks, and the rules that make persuasion more than a raw check | Feature 3 | Personality simulation for NPCs |

---

## Tier J — the campaign, not the rules

These are not D&D features; they are what makes a session resumable. `STORY_FIRST_ROADMAP.md`
treats them as MVP-blocking, and they do not depend on any tier above.

| # | Capability | Depends on | Deliberate non-goals |
| --- | --- | --- | --- |
| J1 | Campaign frame as world data: chapter, motive, clue | — | A plot generator |
| J2 | `procedure.play.storytelling` landed as a contract, not a file at the repo root | J1 | Prose style enforcement |
| J3 | Campaign snapshot and restore, written down | — | Multi-user save management |
| J4 | Session lifecycle: start, resume the open chapter, end and summarise | J1, J2 | Scheduling, player accounts |

---

## What "complete" honestly means

There is no row here for "all SRD spells" or "all SRD monsters", and that is deliberate. Those are
**content**, authored through `commit(kind: "mechanic")` and `commit(kind: "effects")` once the
capability they need exists — which is the entire premise of this engine. A system that can express
one spell correctly can express two hundred; a system that cannot express concentration cannot
express any of them honestly.

Two consequences worth stating plainly:

1. **Tier E first, or the ruleset gets built twice.** Conditions, death saves, concentration and
   reactions are all events. Building them against polling or against the GM remembering to check
   is the "narrated workaround" that `ROADMAP.md` already rules out.
2. **Tier G is what makes the game playable by someone else.** A playthrough where the GM must
   hand-author every component is a demo of the kernel, not a game.

A reasonable ordering for the next several sessions, if the goal is playable rather than complete:
implement Features 11–17, then Tier J. That gets to a real fight with real
consequences and a story that survives the session boundary — with Tiers G and H filled in as the
campaign demands them, which is also how they will be tested.
