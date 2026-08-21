# Feature 20 Slice 3 receipt — tactical melee admission

Status: **Verified**
Date: 2026-08-21

## Delivered boundary

Feature 20 now provides a player-facing tactical-melee action. It admits only a canonical melee
weapon attack whose attacker and target are direct participants in the same bounded encounter map
and within the attacker's base reach. The action returns frozen Feature 8 weapon-attack evidence
and has zero effects.

The admission child returns only the existing closed Feature 8 attack input. The Feature 8 child
receives that object through E6 inputFromChildData, not from the player action. A failed range,
state, roster, weapon-kind, or root-input check stops composition before the d20 child can run.

## Artifacts

- procedure.mechanic.dnd2024.tactical-melee
- mechanic.dnd2024.tactical-melee.admit
- mechanic.dnd2024.tactical-melee.attack
- Feature 20 Slice 3 dependency and acceptance plan.

## Evidence

Focused Feature 8 and Feature 20 coverage passed 8/8 tests. The new tactical test proves:

- exact-five-foot melee calls Feature 8 once through the tactical parent, with frozen child
  id/version/seed provenance and zero effects;
- identical state, input, and seed replay byte-identically;
- out-of-reach, ranged-weapon, and wrong-kind requests fail with no data/effects and leave
  placement unchanged;
- the direct Feature 8 diagnostic resolver remains separately selected.

Disposable catalog validation passed: 373 records, including 86 mechanics, 104 procedures,
75 components, 12 event types, 4 subscriptions, and 92 entities. It reported 63 existing warnings
and did not touch live data. Diff whitespace validation passed.

## Next boundary

Feature 20 Slice 4 voluntary path movement remains blocked on a reviewed derived-cost-to-budget
spender composition decision. This slice does not spend an Action, deal damage, apply weapon Reach,
or authorize unarmed/ranged attacks.
