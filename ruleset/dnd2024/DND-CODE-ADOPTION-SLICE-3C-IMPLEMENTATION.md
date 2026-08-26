# D&D code-adoption Slice 3C implementation — neutral parity and isolation proof

Status: **accepted 2026-08-25**
Owner/roadmap: [D&D 2024 application roadmap](ROADMAP.md)
Dependency tree/leaf: [D&D code-adoption plan, Slice 3 / 3C](DND-CODE-ADOPTION-DEPENDENCY-PLAN.md)
Parent design: [Slice 3 design](DND-CODE-ADOPTION-SLICE-3-DESIGN.md)
Ruleset alignment: **dnd2024-owned**
Source ID and locator: `source.dnd2024.srd-5.2.1`, `Playing the Game > The Six Abilities > Ability Modifiers` (PDF pp. 5–6), `Playing the Game > D20 Tests > Ability Checks > Difficulty Class` (PDF p. 6), and attack-only `Rolling 20 or 1` (PDF p. 7).
Outcome: compare the accepted test-only wrapper with the retained first-party raw-check source on shared seeded vectors and prove result/state isolation.
Exclusions: production registration, donor execution, effects/events/notifications, transactions, migrations, public operations, state writes, skills, proficiency, conditions, Advantage/Disadvantage, and intentional semantic differences.
Allowed files/areas: this document; development-only parity files under `ruleset/dnd2024/adoption/probes/ability-check/`; the generic adoption-probe test; and, because the parity probe demonstrated the generic frozen-context defect, `JintMechanicEngine` plus its sandbox regression test; Slice 3C evidence; dependency-plan and roadmap status.
Stop point: stop after parity, determinism, isolation, and negative evidence pass; do not promote the candidate or begin a gameplay family.

## Confirmed decisions

- Slices 3A/3B are accepted; their operation view and wrapper are the candidate.
- The retained Feature 1 raw check is a comparison source only. Its legacy envelope is normalized through manifest-declared JSON pointers, never adopted as an active owner.
- The standalone donor's whole ability-check path remains excluded; its pure modifier is supporting review evidence, not a runtime dependency.
- A mismatch fails this slice and stops for separate classification/confirmation.

## D&D 5e 2024 alignment

| Rule concern | Source meaning | Consequence |
| --- | --- | --- |
| Modifier | floor-derived from a score | vectors span scores 1, 8, 10, 16, 30 |
| Ability check | d20 plus modifier against DC | compare ability, DC, roll, modifier, total, success |
| Natural 1/20 | no ability-check override | seeded 1 and 20 retain total comparison |
| State authority | operation view only | fixtures are in-memory strings, never game state |

## Evidence, behavior, and failure contract

The Foundry/donor review remains [Slice 3 source review](adoption/evidence/DND-CODE-ADOPTION-SLICE-3-SOURCE-REVIEW.md). No Foundry or donor source executes. The retained comparator is `old-dnd/ruleset/dnd2024/feature-01/04-mechanic-check-ability.json`, using only its source string in isolated Jint runs.

One closed parity manifest/schema names the accepted wrapper, archived record, shared vectors, and normalized JSON pointers. The generic test builds separate ephemeral projections, compares declared shared fields, repeats a seed for byte stability, changes one seed for a controlled result change, checks zero proposals, and hashes source before/after. Malformed scenario fields, missing source/view/input, invalid wrapper output, source failure, or any mismatch fails; no mismatch is silently adapted.

## Verification and exit gate

Run focused adoption-probe tests, AJV/parsing/provenance checks, catalog validation, full suite, and documentation/diff checks. Record vectors/outcomes in `adoption/evidence/DND-CODE-ADOPTION-SLICE-3C-RECEIPT.md`; mark Slice 3 accepted only with full parity. Stop before permanent registration, activation, or any later cohort.
