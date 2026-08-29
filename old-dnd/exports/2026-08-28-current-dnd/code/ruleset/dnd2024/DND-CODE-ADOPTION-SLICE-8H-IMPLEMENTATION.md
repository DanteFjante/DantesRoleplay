# D&D code-adoption Slice 8H implementation — seeded dice primitive

Status: **accepted**  
Parent: [Slice 8 complete native-recovery design](DND-CODE-ADOPTION-SLICE-8-DESIGN.md), leaf 8H  
Prerequisite: accepted deterministic mechanic sandbox RNG  
Ruleset alignment: `dnd2024-owned`  
Outcome: Recover the final classified mechanic as a bounded, effect-free seeded dice primitive.  
Exclusions: D20-test rules, Advantage/Disadvantage, damage, tables, hidden rolls, state mutation,
events, migrations, public operations, and archive deletion.  
Allowed areas: the classified dice mechanic/procedure, D&D activated-path tests, Parent 8 evidence.  
Stop point: deterministic default/explicit/boundary/error acceptance passes.

## Boundary and acceptance

Input is closed with optional count, sides, and modifier; defaults are 1d20+0. Count is 1–100,
sides 2–1,000,000, modifier is a safe integer, and exact total overflow fails. Every die uses only
`ctx.randomInt`; output includes every roll and total and has no effects/events/notifications.
Acceptance proves seed replay, bounds, closed input, no world state, syntax, preview/activation, and
regressions.
