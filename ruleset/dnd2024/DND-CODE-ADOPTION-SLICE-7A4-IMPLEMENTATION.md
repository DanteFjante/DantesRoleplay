# D&D code-adoption Slice 7A4 implementation — saving throws

Status: **accepted 2026-08-26**
Owner/roadmap: [D&D 2024 application roadmap](ROADMAP.md)
Dependency tree/leaf: [D&D code-adoption dependency tree](DND-CODE-ADOPTION-DEPENDENCY-PLAN.md), Parent 7 / 7A4
Ruleset alignment: **dnd2024-owned**
Source ID and locators: `source.dnd2024.srd-5.2.1`; `Playing the Game > D20 Tests > Saving Throws` (PDF p. 7) and `Playing the Game > Proficiency > Saving Throw Proficiencies` (PDF p. 9)
Outcome: record canonical saving-throw proficiency state and resolve fixed-DC saves using existing ability, level, and explicit D20-circumstance conventions.
Exclusions: class grants, CR/monster bonuses, spell/hazard causes or effects, death/concentration saves, persistent conditions, rerolls, and attack behavior.

## Boundary

This leaf introduces the distinct `dnd2024.saving-throw-proficiencies` component and its recorder, plus one save resolver. Ability scores and character level are existing state. The resolver accepts only the requested ability, DC, optional explicit circumstances, and optional voluntary failure; it never stores or accepts a bonus, modifier, selected die, total, outcome, or consequence.

## Behavior

The proficiency component is `{abilities, sourceRef}`: a canonical subset of the six existing ability IDs, where `[]` is known no save proficiencies and absent is unknown. The recorder validates and fully replaces that list. A save validates all three state components before RNG, adds the ability modifier and its level-derived Proficiency Bonus only if its save ability is listed, and reuses 7A3's non-stacking/cancelling d20 convention. Voluntary failure returns a failed, zero-roll result and rejects nonempty circumstances. Natural 1/20 remain ordinary totals. Save resolution is effect-free: the threatening feature owns its result.

## Acceptance

Focused tests cover canonical save recording, proficient/unproficient arithmetic, advantage/disadvantage parity, voluntary failure, and effect-free replay. The current dirty-worktree migration state blocks catalog validation and the full suite; the receipt records the exact external failure.
