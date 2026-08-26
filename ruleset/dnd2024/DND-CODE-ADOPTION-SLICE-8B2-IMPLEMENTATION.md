# D&D code-adoption Slice 8B2 implementation — action-economy diagnostics, refresh, and spend

Status: **accepted**  
Parent: [Slice 8 complete native-recovery design](DND-CODE-ADOPTION-SLICE-8-DESIGN.md), leaf 8B  
Prerequisites: accepted 8A Speed, 8B1 turn-budget admission, 8C Conditions/state effects, and Slice
7 encounter lifecycle/projection transaction evidence  
Ruleset alignment: `dnd2024-owned`  
Source: `source.dnd2024.srd-5.2.1`, `Playing the Game > Actions; Bonus Actions; Reactions;
Interacting with Objects; Combat > Your Turn`, `Rules Glossary > Speed`, and `Rules Glossary >
Exhaustion`  
Outcome: Complete the classified turn-budget family with diagnostics, start-of-turn restoration,
and explicit spending.  
Exclusions: Inferring costs for attacks/spells/features, movement position/path/terrain, event
subscriptions, reactions/triggers, fixtures outside disposable tests, migrations, public operations,
and archive deletion.  
Allowed areas: turn-budget reader/spender, accepted start/advance lifecycle mechanics and contract,
D&D tests, this plan, Parent 8 evidence, and one 8B receipt.  
Stop point: complete action-economy family acceptance passes.

## Cross-owner transition

Start and advance retain encounter lifecycle ownership but gain one atomic participant budget reset
as a declared child-derived effect. The newly active participant is derived only from the validated
Initiative snapshot. Its Action, Bonus Action, Reaction, and free interaction reset to available;
remaining movement resets to `max(0, walkFeet - 5 × Exhaustion level)`. No other participant changes.

The current generic action runner remains transaction owner. Its declared child projections and
containment revisions make lifecycle and participant reset one stale-checked atomic batch. Existing
start/advance outputs add explicit restoration audit fields and effect counts change from one to two.

## Reader and spender

The reader accepts exactly `{}` and reports budget absent/malformed/invalid/valid separately from
Condition absent/malformed/invalid/valid, including Exhaustion level. It is effect-free and safe for
bounded encounter fan-out.

The spender accepts exactly one resource, plus positive five-foot-multiple `feet` for movement. It
validates complete budget, Speed for movement, active encounter state, exact Initiative/containment
roster, membership, and shared Condition prohibitions. Action, Bonus Action, free interaction, and
movement require the active participant; Reaction may be spent by any admitted roster participant.
One successful spend replaces exactly the subject budget. It changes no encounter, Initiative,
Speed, Conditions, position, or other entity.

## Acceptance

Acceptance covers all reader states; start and advance restoration including Exhaustion reduction;
atomic two-effect revision/replay/rollback; each Boolean resource; movement partial/exact/overspend;
off-turn Reaction and other off-turn rejection; nonmember/inactive/roster drift; Condition
prohibition precedence; closed input; stale participant/encounter rejection; and all existing Slice
7/8 D&D regressions.
