# Feature 20 Slice 3 plan — tactical melee admission

Status: **Verified; evidence is recorded in FEATURE-20-SLICE-3-TACTICAL-MELEE-RECEIPT.md.**
Last updated: 2026-08-21

## Capability and boundary

One player-facing tactical-melee action may resolve the existing Feature 8 weapon-attack evidence
only after a bounded grid proves the attacker and target are in the attacker's base reach. The
action is effect-free: it does not spend an Action, choose equipment, deal damage, move a creature,
or persist an attack outcome.

Feature 8 remains the only owner of weapon attack arithmetic, condition circumstances, d20 rolls,
natural-roll precedence, and final Armor Class. Feature 20 remains the owner of its map,
Size-derived footprint distance, placement validity, and base reach. Feature 25 remains the future
owner of weapon Reach exceptions; this slice accepts only a canonical weapon profile whose kind is
melee.

## Source and dependencies

- source.dnd2024.srd-5.2.1: Playing the Game > Playing on a Grid and Playing the Game > Combat >
  Melee Attacks > Reach establish the five-foot grid and base reach.
- Feature 20 Slice 2: valid map, placement, Size, and base-reach state.
- Feature 8: mechanic.dnd2024.weapon-attack, the immutable effect-free attack resolver.
- E6 accepted typed dependent child composition: one earlier child's complete closed object data
  may be the sole input to a later non-foreach sibling.

~~~text
tactical melee attack
├─ tactical admission child (map + roster + positions + Size + base reach + melee weapon)
│  └─ returns exactly Feature-8 closed attack input
└─ Feature-8 weapon-attack child (dependent input)
   └─ returns frozen seeded attack evidence
~~~

## Runtime contract

New permanent ids:

- procedure.mechanic.dnd2024.tactical-melee
- mechanic.dnd2024.tactical-melee.admit
- mechanic.dnd2024.tactical-melee.attack

The root input is exactly:

~~~json
{"kind":"melee","attack":{"ability":"str"}}
~~~

attack may additionally carry Feature 8's existing rollCircumstances; no distance, reach, map
result, dice, Armor Class, hit, damage, effect, or target outcome is caller supplied.

The admission child receives the complete root input, verifies it, all tactical state, direct
participant roster membership, and a canonical melee weapon profile. If legal, it returns exactly
the closed Feature-8 attack input. The attack child receives that object solely through
inputFromChildData; therefore a failed admission prevents the d20 child from executing. The parent
returns frozen Feature-8 evidence with child id/version/seed provenance and zero effects.

## Acceptance

- Legal exact-base-reach melee routes to the tactical parent, executes Feature 8 once, and has
  zero effects.
- Out-of-reach, absent/corrupt map/position/Size/reach, non-roster roles, and ranged weapon all
  fail before a d20 child exists; entity state is unchanged.
- Wrong kind, extra/forged root fields, and invalid Feature-8 attack input fail unchanged.
- The direct Feature-8 diagnostic resolver remains separately routable.
- Same state/input/seed yields byte-identical child evidence and parent output.

Stop before Action spending, damage, weapon Reach properties, unarmed strikes, cover, sight, or
movement.
