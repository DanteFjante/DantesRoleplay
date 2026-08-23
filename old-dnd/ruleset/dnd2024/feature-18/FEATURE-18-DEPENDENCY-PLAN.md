# Feature 18 dependency plan — concentration

> **D&D implementation reference:** When this work includes D&D 2024 mechanics, inspect [Foundry VTT dnd5e](https://github.com/foundryvtt/dnd5e) before designing or coding. Use it as a licensed engineering reference—not a direct dependency or rules authority—while retaining the exact `source.dnd2024.srd-5.2.1` locator and all applicable MIT, CC-BY, and asset-license notices for any reused material.

Status: **Planned; no Feature 18 runtime slice is authorised. The next work is an upstream composition-and-effect-source confirmation, followed by its owning dependency plans.**
Last updated: 2026-08-20

## Execution rule

This is a planning-only artifact under `AGENTS.md`, `procedure.system.create-feature`, and the Terra planning guide. Repository/catalog files remain the development authority. This document creates no runtime artifact: no procedure, component, mechanic, event type, subscription, fixture, database migration, or live game state.

Concentration crosses spell effects, damage, conditions, saving throws, reactions, and death. An implementation pass may complete only one verified lowest slice, validate the catalog in a fresh disposable database, record evidence in a receipt, and stop. It must not import the persistent catalog/database except at an explicit integration or release boundary.

## Target capability

When a creature sustains one concentration-requiring effect, the system ends the old effect when a new one begins, ends it voluntarily or when the creature becomes Incapacitated or dies, and resolves the required Constitution save after each damaging event in the same transaction. A failed save ends concentration; a successful save leaves the exact state unchanged.

### Included

- One authoritative, creature-owned record of the single effect currently being concentrated on.
- A source-bound effect identity supplied by the spell/effect owner, never free-form caller prose.
- Voluntary ending, replacement by a new concentration effect, damage-triggered Constitution saves, and automatic end on effective Incapacitated or death.
- Recorded seed/dice/save result and a durable, reasoned concentration-end event.
- Atomic interaction with the causing cast, damage, condition, or death change.

### Excluded

- Spell lists, slots, preparation, casting time, components, targets, areas, duration counting, and every spell's individual effect; Features 31 and 32 own those.
- The source or duration of an effect, concentration immunity, Legendary Resistance, rerolls, War Caster, class traits, magic items, and homebrew rules.
- Damage mitigation, Temporary Hit Points, healing, dying/death state, and the creation of Incapacitated; Features 13 and 15–17 own them.
- A GM prompt or a pending check that must later be remembered. Concentration checks are automatic consequences of accepted damage, not manual bookkeeping.
- A new MCP tool or commit kind. The existing `action`, `mechanic`, event, and subscription surface is sufficient once the dependency contract is confirmed.

## Official source basis

The registered source is `source.dnd2024.srd-5.2.1`: *System Reference Document 5.2.1* (Wizards of the Coast LLC, 2025-05-01, CC-BY-4.0), [Rules Glossary > Concentration, PDF p. 179](https://media.dndbeyond.com/compendium-images/srd/5.2/SRD_CC_v5.2.pdf).

- A creature can sustain only one concentration-requiring spell or effect; beginning another ends the former one.
- The creator may end concentration without an action.
- Damage requires a Constitution saving throw. Its DC is `min(30, max(10, floor(damageTaken / 2)))`.
- Incapacitated and death end concentration. The SRD's Incapacitated entry confirms the same result; Paralyzed, Petrified, Stunned, and Unconscious imply Incapacitated and must therefore have the same consequence through the condition owner, not copied condition lists.

The phrase “damage taken” is the final amount that reached the creature after the damage owner's mitigation and Temporary Hit Point rules. A zero-damage event never triggers a concentration save.

## Planning inventory and overlap result

| Inquiry | Evidence and conclusion |
| --- | --- |
| Existing concentration owner | Searches for `concentration`, `maintain spell`, `break concentration`, and `damage save` find no component, procedure, mechanic, event, or subscription that owns the rule. Feature 13 explicitly defers Incapacitated ending concentration to this feature. |
| Saving throw owner | `mechanic.dnd2024.saving-throw` is the sole fixed-DC character-save resolver. It validates Constitution/proficiency state, uses the seeded D20 convention, and returns zero effects. Concentration must reuse it rather than duplicate ability-modifier or proficiency arithmetic. |
| Condition owner | `dnd2024.conditions` and `procedure.mechanic.dnd2024.conditions` own stored condition instances. Feature 13's effective-state resolver owns implications; Feature 18 must not re-list Paralyzed, Petrified, Stunned, and Unconscious. |
| Damage owner | Feature 15 Slice 4 is the first planned producer of `dnd2024.damage.dealt`, including the final damage amount. Feature 16 revises that payload after Temporary Hit Point absorption. The existing Feature 9 application mechanic deliberately declares no damage event. |
| Death owner | Feature 17 owns death state and the transition to death. It must expose one stable semantic death event or equivalent projection; a Feature 18 structural reaction must not infer death from a private component shape. |
| Spell/effect owner | Features 31–32 are planned in the roadmap but have no dependency-plan artifact. No current entity/component identifies a persistent, concentration-requiring effect or its exact content version. A caller-supplied effect id would be an unaudited second authority. |
| Reaction/composition boundary | `procedure.subscription.create` currently forbids child mechanics in a reaction. The composer also cannot bind a child input from `ctx.event.payload`. A concentration reaction therefore cannot reuse the existing save resolver with the derived damage DC. Directly reproducing the save formula would violate the single-owner rule. |

## Verified existing dependencies

| Dependency | Current evidence |
| --- | --- |
| Source registry | `catalog/world/entities/source.dnd2024.srd-5.2.1.json` provides the exact document/version, canonical URL, PDF URL, and locator convention. |
| Fixed-DC Constitution save | Feature 4 is verified; `procedure.mechanic.dnd2024.saving-throw` owns ability modifier, proficiency, circumstance, seeded dice, and structured save results. |
| Events and atomic reaction chains | E1 is verified: accepted events, guards, reactions, causation, deterministic derived seeds, rollback, and event-ledger inspection. |
| Conditions state/writer | Feature 13 Slice 1 catalog artifacts exist, but the Feature 13 plan remains the owner of effective-condition implications and consumers. It is not sufficient evidence for concentration's Incapacitated rule on its own. |
| Damage application | Feature 9 applies weapon damage transactionally but intentionally has no declared damage event. Feature 15/16 must provide the event/payload used here. |
| Turn lifecycle | Features 11–12 are verified, but concentration has no turn-start rule. Turns are not an implicit duration clock. |

## Recursive dependency analysis

```text
Feature 18: concentration
├─ SRD concentration rule and source registry                         [implemented source basis]
├─ seeded fixed-DC Constitution saving throw                           [implemented: Feature 4]
├─ atomic events, reactions, audit, and replay                         [implemented: E1]
├─ effective Incapacitated state                                       [blocked: Feature 13 Slice 2]
│  └─ condition foundation and action-economy dependency               [verified: Features 12 and 13 Slice 1]
├─ final damage event after mitigation/buffer                          [blocked: Feature 15 Slice 4; Feature 16 Slice 3]
│  └─ Feature 15 mitigation consumer                                  [blocked parent on Feature 13]
├─ authoritative death transition/event                                [blocked: Feature 17]
├─ persistent concentration-requiring spell/effect identity            [missing owner: Feature 32 plan]
│  └─ spellcasting resources/content eligibility                       [missing upstream: Features 27 and 31]
├─ reaction composition + event-payload child input                    [missing platform confirmation]
└─ concentration lifecycle                                             [blocked parent]
   ├─ source-bound concentration state and voluntary/replacement end   [blocked: effect identity]
   ├─ damage save reaction                                              [blocked: damage event + composition boundary]
   └─ Incapacitated/death termination                                  [blocked: semantic condition/death inputs]
```

The lowest newly discovered planning leaf is **the generic composition boundary**: decide whether an event reaction may compose an explicitly effect-free child and bind a closed child-input field from an accepted event payload. That decision belongs to the mechanic/event platform owner, not to Feature 18. It must be planned and confirmed separately before any concentration slice is assigned. The independent spell/effect source leaf belongs to a new Feature 32 dependency plan.

## Dependency and ownership decisions

1. **Concentration is creature state, not spell state.** A creature can sustain at most one effect, while a spell/effect may have many possible creators/targets over its lifetime. The effect owner records what the effect is; concentration records which one creature currently sustains it.
2. **Effect identity must come from Feature 32.** The eventual state refers to one exact active effect instance and its source content/version. It must not hold spell text, duration, targets, slots, a copied caster profile, or a caller-provided label. Until Feature 32 defines that instance and its ending semantics, no valid normal write path exists.
3. **The damage DC is derived, never stored or supplied.** Consume `finalAmount` from the accepted damage event, calculate `min(30, max(10, floor(finalAmount / 2)))`, and pass that closed value only through a confirmed composition binding to the fixed-DC Constitution-save owner.
4. **A failed save ends concentration; the save mechanic never does.** The existing save resolver remains effect-free. Feature 18 consumes its frozen result and performs one concentration end transition. A successful save produces no concentration effect and preserves bytes exactly.
5. **Effective Incapacitated is owned once.** Feature 18 consumes Feature 13's effective-condition report or a semantic event it declares. It never duplicates implication rules or watches only the literal `incapacitated` entry.
6. **Death is a semantic input, not an inferred component mutation.** Feature 17 must provide a stable accepted event/projection for a creature becoming dead. Feature 18 then ends the state; it does not inspect a private death-state schema or alter death state.
7. **Ending is observable.** The permanent end reason is not recoverable from component removal. The final design should declare one `dnd2024.concentration.ended` event with closed reason vocabulary `voluntary`, `replaced`, `damage-failed`, `incapacitated`, or `dead`; it names the creature and effect instance. The exact schema/version and whether a replacement additionally emits a start event are semantic confirmation items, not implementation permission.
8. **No second save algorithm.** If the platform cannot make the event reaction reuse the current saving-throw resolver, the correct response is to plan that platform extension—not to add a concentration-specific `randomInt`/Constitution/proficiency implementation.

## Confirmation boundary

Before any Feature 18 runtime work, ratify these together with the owning plans:

| Decision | Required confirmation |
| --- | --- |
| Effect protocol | Feature 32's active-effect instance identity, exact source/version reference, concentration-required marker, and state-ending behavior. |
| Reaction composition | Whether reactions may declare bounded effect-free children; how event-payload values bind to a child's closed input; derived-seed/evidence behavior; limits and rollback semantics. |
| Damage payload | Feature 15/16's final `dnd2024.damage.dealt` schema field named by the concentration formula, including the zero-damage meaning and temporary-buffer ordering. |
| Condition input | Feature 13's stable effective-Incapacitated result/event, including implication changes and clear transitions. |
| Death input | Feature 17's stable creature-death event/projection and ordering relative to other reactions. |
| Concentration state | Candidate `dnd2024.concentration` component shape, missing/active semantics, source attribution, normal start/end path, and no-copy constraints. |
| End evidence | `dnd2024.concentration.ended` event identity/schema/reason vocabulary and subscription ordering to avoid competing termination reactions. |

No Feature 18 permanent id, component schema, event type, subscription, mechanic, public surface, or fixture is authorised before this boundary is reviewed. A different Feature 32 effect model or platform composition decision requires this plan to be revised first.

## Slice order and stop gates

| Order | Slice | Starts only when | Exit gate |
| --- | --- | --- | --- |
| 0 | Upstream composition and effect-source confirmation | Owning platform and Feature 32 plans exist. | Confirmed owner, closed binding semantics, no duplicate save algorithm, and exact effect identity; no Feature 18 runtime artifact. |
| 1 | Concentration state and explicit lifecycle | Slice 0, Feature 32 effect protocol, and Feature 13 effective state are verified. | One source-bound active concentration can be begun, voluntarily ended, or replaced atomically; old/new effect visibility and end evidence are exact. |
| 2 | Damage-triggered Constitution save | Slice 1, reaction composition, Feature 15 Slice 4, and Feature 16 Slice 3 are verified. | Every positive final-damage event runs exactly one seeded Constitution save; DC/result/end outcome and rollback are proven. |
| 3 | Incapacitated and death termination | Slice 1, Feature 13 effective-condition input, and Feature 17 death input are verified. | Effective Incapacitated and death end concentration exactly once, with no duplicated condition/death logic. |
| 4 | Spellcasting integration proof | Slices 1–3 and Feature 32 casting are verified. | Casting, replacing, damage, conditions, death, and voluntary ending form one reproducible spell-effect vertical slice. |

## Slice 1 — concentration state and explicit lifecycle

### Runtime artifacts

Subject to the confirmation boundary: `procedure.mechanic.dnd2024.concentration`, a creature-owned `dnd2024.concentration` component, one end/replacement lifecycle mechanic or Feature-32-owned composed transition, and `dnd2024.concentration.ended`. Exact ids/categories and schema remain proposed until the effect protocol is confirmed.

### Data/input contract and required state

The state contains only an exact active effect-instance reference and the fixed SRD source reference. Absence means the creature concentrates on nothing; an empty object, null effect id, or a stale/deleted/non-concentration effect is invalid. It stores no duration, spell level, slot, target, save DC, damage amount, reason history, derived condition state, or copied spell content.

The normal start path is supplied by the spell/effect owner and validates that the referenced effect is active and requires concentration. Starting a second effect atomically ends the first before recording the new reference. Voluntary end requires the current expected effect identity; it removes concentration and invokes the Feature-32 ending protocol. Stale/replayed requests fail without changing either state.

### Acceptance matrix and exit gate

Prove absent/active/replaced/voluntary paths; one-effect-only invariant; stale, foreign, inactive, non-concentration, null/extra-field, and corrupt-state rejection; exact event reason/affected ids; atomic rollback at effect/concentration/event/audit failures; deterministic readback; and fixture cleanup. Stop after this state/lifecycle gate; do not add damage or condition reactions.

## Slice 2 — damage-triggered Constitution save

The reaction consumes exactly one accepted positive-final-damage event for a creature with valid concentration state. It derives the capped DC, invokes the existing saving-throw resolver through the confirmed child binding with `ability: "con"`, and records the resolver's seed, rolls, modifiers, total, and outcome in the reaction/audit evidence. It adds no caller circumstances, modifier, DC, selected die, or outcome.

Success returns zero concentration effects. Failure invokes the Slice 1 end transition once and declares the end event with `damage-failed`. Zero final damage, a different target, missing concentration, and repeated structural events caused by the end itself do not roll. One root damage change with several distinct concentrated targets causes one check per affected target in canonical event sequence; the subscription limit/chain budget is explicitly set and tested.

The acceptance matrix must cover final amounts 1, 19, 20, 21, 59, 60, and a higher amount (DCs 10, 10, 10, 10, 29, 30, and 30 respectively); proficient/nonproficient Constitution; condition-derived save effects; fixed seeds with success/failure; zero damage; malformed event/state; event payload version; subscription ordering; action/root rollback; exact ledger causation; and byte-unchanged successful-save state. Stop before condition/death termination.

## Slice 3 — Incapacitated and death termination

One reaction consumes the Feature-13 effective-Incapacitated semantic input; another consumes the Feature-17 death input. Each ends one valid concentration state with its own closed reason and does nothing for an already-unconcentrating creature. The condition path must cover explicit Incapacitated and every condition the Feature-13 resolver marks as effectively Incapacitated; it must also prove that clearing an unrelated condition does not end concentration. The death path must preserve Feature 17's state/ordering and cannot make a death decision itself.

Prove idempotence, ordered interaction when damage also makes a creature Incapacitated or dead, no duplicate end event, corrupt source/state rollback, and fresh ledger/audit readback. Stop before spellcasting vertical integration.

## Slice 4 — spellcasting integration proof

Feature 32 supplies one real concentration-requiring SRD effect and its non-concentration control. The vertical proof casts it, replaces it with another concentration effect, voluntarily ends it, takes damage with both save outcomes, becomes effectively Incapacitated, and dies. Every transition must leave source effect, concentration state, event chain, and audit records mutually consistent. This slice does not broaden spell content, duration/area/target rules, or player UI.

## Plan-quality audit

- One player-facing capability and explicit exclusions: yes.
- Official source/version/locator: yes; registered SRD source plus official PDF p. 179.
- Existing-owner and overlap search: yes; save, conditions, damage, death, spells, and event composition are individually classified.
- Every dependency expanded: yes; the plan identifies the missing platform and spell-effect owners rather than inventing caller data or duplicate logic.
- One currently actionable lowest leaf: yes, but it belongs to a separate platform/effect-source planning pass. No Feature 18 implementation is authorised until that pass is accepted.
- Closed state/input, ordering, formula, atomicity, replay, negative cases, and cleanup: specified for each dependent slice.
- Runtime payload/source duplication: none; this plan describes contracts and behavior only.

## Plan-change rule

Revise this document before implementation if Feature 32 models effects without durable instances, the event platform chooses a different composition interface, Feature 15/16 names a different final-damage field, Feature 13 exposes Incapacitated differently, Feature 17 lacks a semantic death signal, or a new source rule alters the concentration formula. Do not bypass any such change with a manual check, caller-supplied DC, direct database write, duplicated saving-throw arithmetic, or a second condition/death owner.
