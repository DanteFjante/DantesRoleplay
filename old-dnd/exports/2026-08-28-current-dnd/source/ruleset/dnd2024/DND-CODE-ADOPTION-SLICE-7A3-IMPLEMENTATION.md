# D&D code-adoption Slice 7A3 implementation — explicit Advantage and Disadvantage

Status: **accepted 2026-08-26**
Owner/roadmap: [D&D 2024 application roadmap](ROADMAP.md)
Dependency tree/leaf: [D&D code-adoption dependency tree](DND-CODE-ADOPTION-DEPENDENCY-PLAN.md), Parent 7 / 7A3
Ruleset alignment: **dnd2024-owned**
Source ID and locator: `source.dnd2024.srd-5.2.1`; `Playing the Game > D20 Tests > Advantage/Disadvantage` (PDF p. 7)
Outcome: extend the existing fixed-DC ability check with explicit, auditable circumstances that select normal, Advantage, or Disadvantage d20 handling.
Exclusions: persistent conditions, derived condition state, rerolls, saves, Initiative, attacks, and any new component, entity, migration, or C# behavior.
Allowed files/areas: the existing D&D ability-check mechanic/procedure, focused D&D tests, and Slice 7 status/evidence documents.
Stop point: raw and named-skill ability checks have deterministic non-stacking Advantage/Disadvantage; no other D20-test family changes.

## Confirmed boundary

Parent 7 was authorized for autonomous completion by the user on 2026-08-25. This leaf revises only `mechanic.dnd2024.check.ability` and `procedure.mechanic.dnd2024.check.ability`; it introduces no permanent identity. The caller supplies a closed, audit-only list of circumstances, while the mechanic owns roll mode and dice selection.

## Rule alignment and state boundary

SRD 5.2.1 says to roll two d20s and use the higher or lower result when a D20 Test has Advantage or Disadvantage. Multiple sources of the same kind do not stack; any mixture of both kinds results in neither and one d20. The check continues to obtain ability scores and optional proficiency state from components. Circumstances are action input, deliberately not persistent state; condition-to-circumstance derivation is deferred to the later condition slice.

`rollCircumstances` is optional. When supplied it is a nonempty array whose members are exactly `{kind, source}`; `kind` is `advantage` or `disadvantage`, `source` is a trimmed nonempty audit label, and no identical `(kind, source)` pair may repeat. The caller may not provide a selected roll, roll mode, modifiers, or outcome.

## Behavior, failure, replay, and effects

Validate state and all input before RNG. No circumstances, only Advantage, only Disadvantage, and a mixture resolve to `normal`, `advantage`, `disadvantage`, and `normal` respectively. Normal rolls one seeded d20; Advantage/Disadvantage rolls two seeded d20s and uses max/min. The result exposes `rollMode`, all `rolls`, selected `roll`, and accepted `rollCircumstances`, preserving established ability/proficiency arithmetic. The mechanic remains effect-free; ordinary generic action replay/transaction behavior remains its owner. Invalid circumstances fail before a result or state change.

## Acceptance matrix

| Case | Expected evidence |
| --- | --- |
| No circumstances | one d20 and `normal` mode; existing raw and named checks remain compatible |
| Same-kind sources | two d20s only, with max for Advantage or min for Disadvantage |
| Mixed sources | one d20 and `normal`, regardless of counts |
| Audit input | closed members, no duplicate pair, blank or future `condition:` source rejected |
| Replay | same seed/input has identical roll list and selected die |

## Verification and exit

Run focused D&D tests, catalog validation, and the complete test suite. Record a receipt as verified and ready for final Sol review, then begin 7A4 only.
