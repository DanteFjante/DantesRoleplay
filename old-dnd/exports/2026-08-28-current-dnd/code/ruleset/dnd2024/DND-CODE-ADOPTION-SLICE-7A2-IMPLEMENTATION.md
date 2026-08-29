# D&D code-adoption Slice 7A2 implementation — character proficiency and skill checks

Status: **accepted 2026-08-25**
Owner/roadmap: [D&D 2024 application roadmap](ROADMAP.md)
Dependency tree/leaf: [D&D code-adoption dependency tree](DND-CODE-ADOPTION-DEPENDENCY-PLAN.md), Parent 7 / 7A2
Ruleset alignment: **dnd2024-owned**
Source ID and locator: `source.dnd2024.srd-5.2.1`; `Playing the Game > Proficiency` (PDF pp. 8–9), `Playing the Game > Proficiency > Skill Proficiencies and Skills` (PDF pp. 8–9), and `Character Creation > Level Advancement > Character Advancement` (PDF p. 23)
Outcome: add canonical character-level and skill-proficiency state, recorders, and the named-skill extension of the accepted fixed-DC ability check.
Exclusions: Expertise/half proficiency, tools, conditions, Advantage/Disadvantage, saves, monster Challenge Rating, class/background grants, effects from a check, and any level-up workflow.
Allowed files/areas: `catalog/applications/dnd2024/**`, focused D&D application tests, and Slice 7 status/evidence documents.
Stop point: a raw or named-skill ability check can derive one character Proficiency Bonus from level and explicit skill state. Do not implement 7A3 or later behavior.

## Confirmed decisions

The user authorized completion of Parent 7 on 2026-08-25. For this leaf that authorizes the new
application-owned identities `dnd2024.character-level`, `dnd2024.skill-proficiencies`,
`procedure.mechanic.dnd2024.character-level`, `procedure.mechanic.dnd2024.skill-proficiencies`,
`mechanic.dnd2024.character-level.record`, and
`mechanic.dnd2024.skill-proficiencies.record`, plus the confirmed revision of the accepted
ability-check procedure/mechanic. No active D&D owner currently provides these records.

## D&D 5e 2024 alignment

| Rule concern | SRD 5.2.1 meaning used | Existing owner | Implementation consequence |
| --- | --- | --- | --- |
| Proficiency Bonus | Character level bands produce +2 through +6; it is used once for a proficient D20 Test | accepted raw ability check | store level only; derive `2 + floor((level - 1) / 4)` |
| Skill proficiency | A proficient creature adds its bonus to an ability check involving that skill; lack of proficiency does not prevent the check | component type registry and action effects | store a closed set of the 18 SRD skill IDs; explicit empty is known none, missing is unknown |
| Skill ability | The GM determines relevance; table/default association is advisory | accepted raw ability input owner | caller still supplies ability; result reports the default mapping without remapping it |
| Check result | Named skill extends the existing ability check, not a new dice mechanism | accepted `mechanic.dnd2024.check.ability` | preserve raw input/result and one seeded d20; append at most one derived bonus |
| Consequence | A check itself need not change world state | generic action/effects owner | check remains effect-free; only administrative recorders propose one component effect |

## External implementation reference

The previously reviewed Foundry dnd5e file at pin
`275bed0be4ccfa15e6b3347acccb8da8784726d9`, `module/dice/d20-roll.mjs`, remains
reference-only evidence for keeping die, modifiers, and target separate. The official SRD—not
Foundry—defines the proficiency bands and skill application. No Foundry code or data is reused.

## Prerequisite evidence

- [7A1 receipt](adoption/evidence/DND-CODE-ADOPTION-SLICE-7A1-RECEIPT.md): active D&D source,
  canonical ability scores, seeded effect-free raw check, and replay.
- [Slice 3 source review](adoption/evidence/DND-CODE-ADOPTION-SLICE-3-SOURCE-REVIEW.md): exact
  ability-check/source-review baseline.
- Archived Feature 2 is a first-party recovery candidate only. Its level, skill-state, and
  skill-check behavior are adapted to the current application catalog/action contracts.

## Runtime artifacts

- Two closed component schemas: `dnd2024.character-level` and
  `dnd2024.skill-proficiencies`.
- Two active procedures and two administrative recorders. They construct their fixed source
  references and add/set their one component only.
- A revision of the accepted ability-check procedure and mechanic. Its closed input becomes
  ability/DC plus optional exact skill ID; raw calls remain supported.

## Authoritative state and closed input

A character level is exactly `{level, sourceRef}`, where level is an integer 1–20 and sourceRef
is fixed to `source.dnd2024.srd-5.2.1` / `Character Creation > Level Advancement > Character
Advancement`. Skills are exactly `{skills, sourceRef}`; skills is a unique canonical-order
subset of the 18 stable IDs and sourceRef is fixed to the Skill Proficiencies locator.

Recorders accept only `{level}` or `{skills}`. A named check accepts
`{ability, dc, skill}`; raw `{ability, dc}` remains valid. Callers may never provide a source
reference, proficiency bonus, modifier, total, outcome, default ability, or proficiency flag.

## Behavior, effects, failures, replay, and rollback

Validate all input/state before RNG. For a skill check, validate both required components and their
fixed source references, derive the level-band bonus, and append it only when the named skill is
listed. Empty skill state is valid nonproficiency; missing or malformed state fails. The check rolls
one kernel-seeded d20 and proposes no effects/events/notifications.

Each recorder validates before proposing exactly one `component.add` or `component.set` effect.
Generic application action ownership provides atomicity, rollback, and replay. Invalid input and
failed check evaluation leave component state unchanged.

## Acceptance matrix

| Case | Expected evidence |
| --- | --- |
| Level bands | 1/4/5/8/9/12/13/16/17/20 derive +2/+2/+3/+3/+4/+4/+5/+5/+6/+6 |
| Skill state | canonicalizes a reverse-order valid list; explicit empty is valid; unknown/duplicate/wrong-case inputs fail |
| Skill check | same seed and ability/DC: proficient Stealth exceeds nonproficient Acrobatics by exactly the derived bonus |
| GM pairing | Strength (Intimidation) retains Strength and reports Charisma as advisory default |
| Raw compatibility | the accepted raw input/result remains one-d20 and effect-free |
| Failure/replay | missing skill state or extra derived input fails unchanged; recorders and checks replay by identity |
| Activated path | schemas/records are present in the normal fresh application source overlay |

## Verification and exit

Run focused D&D tests, catalog validation, and the full suite before acceptance. Record a receipt,
then stop. 7A3 alone may add Advantage/Disadvantage; no circumstance or save behavior is allowed
in this leaf.
