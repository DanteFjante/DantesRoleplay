---
id: procedure.mechanic.dnd2024.character-experience
category: ruleset.dnd2024.core.advancement.experience
name: Govern D&D 2024 character experience
governs: commit(kind: "component") declaring dnd2024.character-experience; commit(kind: "mechanic") authoring mechanic.dnd2024.character-experience.write or mechanic.dnd2024.character-experience.read; commit(kind: "action") recording, correcting, or reading character experience
status: active
---

## Description

Owns one character's persistent D&D 2024 experience-point total and the read-only calculation of
whether that total reaches the next **total character level** threshold. It is a data and
diagnostic owner, not a campaign reward, authorization, class, or level-up owner.

## Instructions

1. Source basis (SRD 5.2.1, Character Creation > Level Advancement; CC-BY-4.0,
   https://www.dndbeyond.com/srd): XP is a character total. The fixed threshold table starts at
   0 XP for level 1 and ends at 355,000 XP for level 20.
2. Keep at most one closed `dnd2024.character-experience` component on a character. It has only
   nonnegative safe-integer `total` and its fixed source reference.
3. `mechanic.dnd2024.character-experience.write` accepts exactly `record` or `correct` and a
   complete total. It fixes provenance, requires absence for record and complete valid existing
   state for correction, and proposes one component add or set effect.
4. `mechanic.dnd2024.character-experience.read` accepts `{}`. It reads this component together
   with `dnd2024.character-level` and reports only diagnostic state plus the derived exact
   next-level threshold. It never defaults missing data, awards XP, writes an authorization, or
   changes a level.
5. Campaign C14 owns normal XP awards and authorization. Character CH9 and Feature 27 own every
   actual level-up consequence. They must consume this reader's result rather than duplicate the
   threshold table.

## Constraints

- `total` is a JavaScript-safe integer from 0 through 9,007,199,254,740,991. Gaining a level does
  not spend, reset, reduce, or otherwise alter it.
- The component stores no campaign, policy, award delta/history, recipient, threshold, eligibility,
  authorization, target level, class level, Hit Points, feature, grant, or choice.
- For valid current total level 1–19, eligibility means only `total >=` the threshold for exactly
  the next total level. Level 20 is capped; no level 21 is derived.
- Missing, malformed, or invalid experience or total-level state is unknown. It is never inferred
  as zero or as eligibility.
- Administrative record/correct is setup and repair only. It accepts no award amount, campaign,
  policy, target level, class, authorization, source payload, reason, or effects.
