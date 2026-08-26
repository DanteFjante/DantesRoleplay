# D&D code-adoption Slice 7 design — first recovered gameplay seams

Status: **7A1–7A2 accepted; 7A3–7D verified after Sol review**
Owner/roadmap: [D&D 2024 application roadmap](ROADMAP.md)
Dependency tree: [D&D code-adoption dependency tree](DND-CODE-ADOPTION-DEPENDENCY-PLAN.md), Parent 7
Ruleset alignment: **dnd2024-owned**

## Purpose

Split the broad Parent 7 grouping into recoverable, source-aligned seams. The delivered seams keep
ability/proficiency/saving-throw resolution, encounter ordering/lifecycle, combat state, and
fresh-host acceptance as separately evidenced boundaries rather than one donor-sized transaction.

## Subslice order

| Leaf | Capability | Readiness | Boundary |
| --- | --- | --- | --- |
| 7A1 | Ability-score component and raw fixed-DC ability check (archived Feature 1) | accepted | one D&D-owned component, procedure, mechanic, no effects |
| 7A2 | Character-level/proficiency and skill-check cohort (archived Feature 2) | accepted | separate source and component/effect closure |
| 7A3 | Advantage/Disadvantage D20 convention (archived Feature 3) | verified after Sol review | composes after 7A2; explicit circumstance input, no silent normal-mode fallback |
| 7A4 | Saving-throw proficiency and saving-throw cohort (archived Feature 4) | verified after Sol review | separate source/effect/replay closure |
| 7B | Initiative and turn flow | verified after Sol review | [implementation](DND-CODE-ADOPTION-SLICE-7B-IMPLEMENTATION.md) |
| 7C | AC, HP, weapons, and damage | verified after Sol review | [implementation](DND-CODE-ADOPTION-SLICE-7C-IMPLEMENTATION.md) |
| 7D | Fresh-host encounter acceptance | verified after Sol review | [implementation](DND-CODE-ADOPTION-SLICE-7D-IMPLEMENTATION.md) |

## 7A1 D&D alignment

| Rule concern | SRD 5.2.1 meaning | Existing evidence/owner | Consequence |
| --- | --- | --- | --- |
| Ability scores | six named scores; score is authoritative, modifier is derived | [Slice 3 source review](adoption/evidence/DND-CODE-ADOPTION-SLICE-3-SOURCE-REVIEW.md), `Playing the Game > The Six Abilities > Ability Scores/Ability Modifiers` (PDF pp. 5–6) | one exact six-field component; never store modifier |
| Raw ability check | d20 plus selected ability modifier; total meets/exceeds GM-supplied DC | same review, `Playing the Game > D20 Tests > Ability Checks/Difficulty Class` (PDF p. 6) | closed input is ability ID + fixed DC; kernel RNG only |
| Natural 1/20 | automatic outcome language reviewed only for attack rolls | same review, `Playing the Game > D20 Tests > Attack Rolls > Rolling 20 or 1` (PDF p. 7) | no automatic ability-check success/failure branch |
| Consequence | the check answers a result, not a world change | accepted application action/effect owner | no effects/events/notifications in 7A1 |

## Foundry and archive evidence

The existing Slice 3 review inspected Foundry dnd5e at pin
`275bed0be4ccfa15e6b3347acccb8da8784726d9`, `module/dice/d20-roll.mjs`, as reference-only. It
supports the separation of die/modifiers/target and does not authorize Foundry code reuse.

The exact first-party recovery candidates are:

- `old-dnd/ruleset/dnd2024/feature-01/02-component-abilities.json`;
- `old-dnd/ruleset/dnd2024/feature-01/04-mechanic-check-ability.json`; and
- their catalog JavaScript/procedure counterparts under `old-dnd/catalog/`.

The archive's broader Feature 1 runbook is evidence, not a copy authorization. Its old component
and mechanic envelopes must be mapped to the current application kernel before activation.

## 7A1 confirmation

The required confirmation was granted on 2026-08-25 when the user replied **Continue** to the
explicit 7A1 confirmation request. It authorizes all of the following together:

1. Create/restore active application-owned identities for the D&D source, `dnd2024.abilities`,
   `procedure.mechanic.dnd2024.abilities`, `procedure.mechanic.dnd2024.check.ability`, and
   `mechanic.dnd2024.check.ability` after current-owner/ID collision checks.
2. Adopt the stated 2024-only raw ability-check semantics and result envelope, including no
   proficiency, skill, condition, Advantage/Disadvantage, saving throw, or natural-1/20 override.
3. Author the component/projection/mechanic/effect mapping for the current application catalog and
   activate it only through the normal reviewed application boundary.

This is the recorded permanent-ID, D&D semantic, and first-production-activation decision under the
repository working agreement. The active implementation boundary is
[Slice 7A1 implementation](DND-CODE-ADOPTION-SLICE-7A1-IMPLEMENTATION.md).

Feature acceptance was confirmed by the user on 2026-08-25. The implementation and verification
evidence are retained in its [receipt](adoption/evidence/DND-CODE-ADOPTION-SLICE-7A1-RECEIPT.md).

## Collision evidence

On 2026-08-25, the active `catalog/`, kernel, and test owners were searched for all five proposed
7A1 IDs. No active owner or catalog record uses them; matching copies are confined to the retained
archive and adoption-planning material. Confirmation therefore authorizes a new active D&D boundary,
not a replacement or migration of an existing active owner.
