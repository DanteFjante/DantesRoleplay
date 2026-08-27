# D&D code-adoption Slice 9 design — pure derivation gap closure

Status: **accepted; 9A–9C complete**
Owner/roadmap: [D&D 2024 application roadmap](ROADMAP.md), donor-gap-filling lane  
Dependency tree/leaf: [D&D code-adoption dependency plan](DND-CODE-ADOPTION-DEPENDENCY-PLAN.md),
Slice 9 / F1 pure derivations and character-sheet calculations  
Ruleset alignment: `dnd2024-owned` for rule calculations; donor inventory/provenance is
`dnd2024-compatible` development evidence  
Source: `source.dnd2024.srd-5.2.1`, `Character Creation > Step 5: Character Creation Details >
Fill In Numbers` (PDF pp. 21–22) and `Rules Glossary > Passive Perception` (PDF p. 185)

## Outcome and non-goals

Close the pinned donor's independently published pure-derivation surface against the current
post-Slice-8 catalog. Adapt every dependency-ready, SRD-verified character calculation without
storing derived values or importing donor runtime state. Give every other candidate a concrete
retain, reject, or later-owner disposition.

This parent does not import static class/spell/item/monster content (Slice 10), effect stacks,
damage/healing/combat timing, spell resolution, terrain/path execution, multiattack planning, donor
campaign state, packages, reducers, persistence, RNG, IDs, public operations, migrations, or live
state.

## Existing owners and evidence

| Concern | Current owner | State/evidence |
| --- | --- | --- |
| Abilities and modifiers | `dnd2024.abilities`; current ability/check/save/attack mechanics | verified by accepted Slices 7A/7C and Parent 8 |
| Character level and PB | `dnd2024.character-level`; current D20 mechanics | verified by accepted Slice 7A2 |
| Skill/save proficiency | `dnd2024.skill-proficiencies`, `dnd2024.saving-throw-proficiencies` | verified by accepted Slices 7A2/7A4 |
| Active checks and saves | `mechanic.dnd2024.check.ability`, `mechanic.dnd2024.saving-throw` | retained; never duplicated |
| AC, HP, Speed, action economy, weapons | accepted current catalog owners | retained; passthrough is not part of the first character-calculation cohort |
| Donor identity/license | `dnd-srd-engine` commit `ead852b19b9e45f54f43e193caf4f10aad91a91b`, MIT engine code | pinned by the accepted adoption policy and donor lock |
| Candidate closure | [Slice 9 candidate inventory](adoption/evidence/slice-9-derivation-candidates.json) | 17 candidate modules/groups, each with a closed prospective disposition |

## Dependency tree

```text
Slice 9 — pure derivation gap closure [accepted]
├─ 9A candidate/source/owner classification [verified planning evidence]
├─ 9B stateless core character calculations [accepted]
│  ├─ existing ability, level, skill, and save state [verified]
│  ├─ SRD Fill In Numbers and Passive Perception locators [verified]
│  ├─ pinned donor character-view/passive-score evidence [verified]
│  └─ Foundry deterministic actor/skill preparation review [verified]
└─ 9C donor/native/SRD conformance and parent closure [accepted]
```

## Candidate decisions

The donor's public derive index and the exact Slice 1B donor matches were compared with current
catalog IDs. The only dependency-ready uncovered symbols are `computeDerivedCharacter`'s core
ability/level/skill/save calculations and `computePassiveScore`. They form one read-only character
calculation cohort. Existing check/save/PB/Speed/AC/attack/action-economy/carrying owners win by
policy. Spell DC/slots require authoritative spellcasting-feature/class content; terrain and
multiattack require later movement/content/action owners; effect-stack adoption is prohibited.

The donor's encumbrance type exposes no callable public derivation through its index and its
threshold semantics are not established by the selected SRD locators, so it is rejected rather
than silently importing a legacy optional rule.

## Ordered leaves

| Order | Leaf | Depends on | Exit gate |
| ---: | --- | --- | --- |
| 1 | 9A inventory and dispositions | accepted Slices 0–8 | all 17 candidate groups have exact source and current/later owner |
| 2 | 9B character calculations | 9A and permanent-ID confirmation | one activated stateless mechanic derives core sheet numbers from four exact components |
| 3 | 9C conformance and closure | 9B | donor vectors normalized to current native/SRD results, complete candidate closure, catalog/full-suite acceptance |

## Active leaf

Leaf 9B was confirmed on 2026-08-26 and creates no component or stored projection. One subject role
supplies exactly `dnd2024.abilities`,
`dnd2024.character-level`, `dnd2024.skill-proficiencies`, and
`dnd2024.saving-throw-proficiencies`; empty input returns six ability modifiers, PB, six save
modifiers, eighteen skill modifiers, initiative modifier, and base Passive Perception. It proposes
no effects/events/notifications and owns no transaction.

## Confirmation gates

- The user confirmed permanent IDs `mechanic.dnd2024.character-sheet.read` and
  `procedure.mechanic.dnd2024.character-sheet` on 2026-08-26 before runtime activation.
- Any future stored projection, schema meaning, caller-supplied spellcasting ability, public
  operation, or cross-owner AC/HP/content change requires a separate gate.
- Parent acceptance remains a confirmation boundary after 9C evidence passes.

## Planning receipt

- Runtime artifacts created: none. A closed development-only wrapper, schemas, source vectors, MIT
  notice, and reviewed per-symbol provenance are staged under `adoption/`; they introduce no
  catalog ID and cannot be activated by the running application.
- Donor and Foundry repositories were inspected at their exact locked commits in disposable
  temporary checkouts; no package became a production dependency.
