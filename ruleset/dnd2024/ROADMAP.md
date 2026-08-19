# D&D 2024 ruleset development roadmap

Status: **Features 3–10 verified; first reproducible vertical session complete**
Last updated: 2026-08-19

## Purpose and authority

This roadmap orders the remaining work needed for a small but genuine D&D 2024 test session. It
is not a promise to implement all of D&D and it does not authorize bundling several features into
one pass.

`procedure.system.create-feature` is the governing workflow. For each feature, create or review
its recursive dependency plan, implement exactly one lowest unimplemented slice, meet that
slice's exit gate, record evidence, and stop. Catalog files are the canonical development source
for rule contracts, component definitions, entities, and mechanics; dry-run, import, and verify
them into the live database. Repository planning documents hold plans and evidence, not copied
runtime payloads.

`TERRA-FEATURE-PLANNING-GUIDE.md` is the reusable planning playbook for expanding future roadmap
rows to this quality bar. It requires a planning-only pass, live ownership/dependency evidence,
complete slice specifications, a plan-quality audit, and a stop before implementation.

The official rule source is SRD 5.2.1, represented by the existing live source entity
`source.dnd2024.srd-5.2.1`.

## Verified foundation

- Feature 1: ability scores and seeded fixed-DC ability checks.
- Feature 2: character level, derived Proficiency Bonus, the 18 skill IDs, character skill
  proficiency state, and proficient named-skill checks.
- Shared deterministic dice mechanic exists, but it is not a substitute for D20 Test rules.
- Repository regression baseline: 304/304 tests.

Intent searches performed during this planning pass found no live D&D mechanic for
Advantage/Disadvantage, saving throws, Initiative, attack rolls, or damage. Generic dice and
threshold mechanics remain examples, not D&D rule implementations.

## Minimum test-session dependency graph

```text
small D&D 2024 test session
├─ exploration checks                                      [implemented: Features 1–2]
│  ├─ abilities and modifiers                              [implemented]
│  ├─ skill proficiency and level bonus                    [implemented]
│  └─ Advantage/Disadvantage on checks                     [Feature 3 verified]
├─ defensive D20 Tests                                     [Feature 4 verified]
│  ├─ shared Advantage/Disadvantage convention             [Feature 3]
│  ├─ saving-throw proficiency state                       [Feature 4 Slice 1 verified]
│  └─ saving-throw resolution                              [Feature 4 Slice 2 verified]
├─ combat entry                                             [Feature 5 verified]
│  ├─ closed action-input transport                        [system Slice 0 verified]
│  ├─ Dexterity-based Initiative roll                      [Feature 5 Slice 1 verified]
│  └─ deterministic arbitrary-roster order and tie policy [Feature 5 Slice 2 verified]
├─ combatant durability                                     [Feature 6 verified]
│  ├─ Armor Class state                                    [Feature 6 Slice 1 verified]
│  └─ current/max Hit Points state                         [Feature 6 Slice 2 verified]
├─ weapon attacks                                           [Features 7–8 verified]
│  ├─ weapon profile and proficiency state                 [Feature 7 Slices 1–2 verified]
│  ├─ attack roll vs Armor Class                            [Feature 8 Slice 1 verified]
│  └─ natural 20/1 and Critical Hit classification         [Feature 8 Slice 1 verified]
├─ damage and consequences                                  [Feature 9 verified]
│  ├─ seeded damage dice and critical extra dice           [Feature 9 Slice 1 verified]
│  └─ validated Hit Point application                      [Feature 9 Slice 2 verified]
└─ vertical acceptance session                              [Feature 10 verified]
   ├─ one player character and one simple opponent         [fixtures verified, not new rules]
   ├─ exploration check, Initiative, attack, damage, save  [all parents verified]
   └─ exact replay and final-state audit                    [Feature 10 verified]
```

## Ordered features and boundaries

| Feature | Capability | Depends on | Deliberate non-goals |
| --- | --- | --- | --- |
| 3 | Advantage/Disadvantage for ability checks, plus one reusable D20 Test input/result convention | Features 1–2; Slice 1 contract verified | Heroic Inspiration, rerolls, persistent conditions, automatic circumstance discovery |
| 4 | Six saving-throw proficiencies and fixed-DC saving throws | Feature 3 and character level; two-slice plan ready | Spell effects, death saves, monster CR, legendary resistance |
| 5 | Initiative rolls and deterministic encounter ordering | Feature 3 and abilities | Full turn economy, surprise state beyond its supplied roll circumstance, ready/delay |
| 6 | Authoritative Armor Class and Hit Point state | Source registry | Armor-building formulas, classes, equipment loadouts, temporary HP, resistance |
| 7 | Minimal weapon profiles and character weapon proficiency | Source registry and level | Complete equipment catalog, mastery, ammunition, range/cover, class grants |
| 8 | Weapon attack rolls against Armor Class | Features 3, 6, 7 | Multiattack, opportunity attacks, spell attacks, damage application |
| 9 | Weapon damage and transactional Hit Point loss | Features 6–8 and deterministic dice; both slices verified | Resistance, immunity, vulnerability, healing, unconsciousness, death saves |
| 10 | One reproducible vertical test session | Features 1–9; both slices verified | Campaign management, character builder, complete combat engine |

Feature numbers express dependency order, not permission to begin them. Features 1–5 are verified.
Feature 5's file-first catalog import gate exercises the composition runtime and encounter-order
matrix. Feature 6 records final Armor Class and bounded current/maximum Hit Point state through
the catalog; both slices are verified in feature-06/FEATURE-6-DEPENDENCY-PLAN.md. Feature 7 now
provides canonical Dagger, Shortbow, and Battleaxe profile data plus authoritative Simple/Martial
weapon-category proficiency state. Feature 8 resolves effect-free attacks against final AC,
including natural-20/1 classification, without damage or persistence. Feature 9 now provides
effect-free confirmed-hit damage evidence and a composed, transactional target Hit Point
application parent. Feature 10's catalog-owned baseline fixtures and two-database deterministic
vertical-session harness are verified. The first reproducible D&D 2024 session is complete.

Work past Feature 10 is ordered in `ROADMAP-COMPLETE-PLAY.md`, which covers what a complete SRD
5.2.1 experience needs beyond the first vertical session.

**Known kernel weakness, discovered during Feature 5 Slice 2.** Action selection scores the number
of distinct query tokens appearing anywhere in a mechanic's id, name, description and match
phrases, and breaks ties by ascending id. A phrase match does not outrank an incidental token
match, so an unrelated rule whose id happens to sort earlier can capture another rule's intent —
it did, twice, during this slice. Until that is fixed, every new rule must be routing-tested
against the intents of its neighbours, and authors must watch the words they put in a description.
This deserves its own kernel dependency plan before the ruleset grows much further.

## Global quality gates

Every feature plan must require all of the following:

1. Query live dependencies and governing procedures immediately before writing.
2. Search mechanics and intent phrases before choosing IDs or match text.
3. Define closed input, authoritative state, derived values, result shape, effects, failure
   behavior, source locators, non-goals, and state-restoration obligations.
4. Dry-run every supported write and commit the identical payload.
5. Run the mechanic through `commit(kind: "action")`; parse and assert the returned data rather
   than treating `ok: true` as sufficient.
6. Test boundary, malformed, missing-state, deterministic replay, routing, and zero/unexpected
   effect cases proportionately to the slice.
7. Query every committed artifact and changed entity back. Temporary fixtures must be created and
   deleted through dry-run-first audited effects.
8. Run the full repository suite and `git diff --check`.
9. Record operation IDs and objective results without copying live payloads into the repository.
10. Mark only the current slice complete and stop for review.

## When the first test run is honest

The system is ready for the Feature 10 vertical session only when Features 3–9 have met their own
exit gates. A narrated workaround, caller-supplied attack total, manually chosen damage result, or
generic threshold roll does not satisfy a missing D&D mechanic.
