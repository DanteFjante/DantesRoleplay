# D&D 2024 component convergence — encounter lifecycle receipt

Status: **accepted**  
Accepted: 2026-08-28  
Implementation: `prototype/dnd2024/planning/DND2024-ENCOUNTER-LIFECYCLE-SOL-SLICE-3-IMPLEMENTATION.md`

## Delivered boundary

- Activated `dnd2024.encounter.participation`, `dnd2024.combat.initiative`,
  `dnd2024.encounter.round`, `dnd2024.encounter.turn`, and
  `dnd2024.combat.turn-budget` as the authoritative encounter lifecycle owners.
- Initiative now converts the pre-combat containment roster into independently addressable
  participation entities, locked per-participation Initiative results, and explicit encounter/actor
  relationships in the same transaction as existing active-rest consequences.
- Start, advance, wrap, and end create or complete explicit round and turn entities and move active
  relationships atomically. Later lifecycle mechanics no longer consult containment.
- Every turn owns a fresh counted Action, Bonus Action, Reaction, and interaction budget. Movement
  spending records exact rational metres and derives the walk limit from authoritative metric Speed
  and Exhaustion. A participant's latest turn remains available for bounded off-turn Reaction use.
- Removed the fabricated character budget from basic character creation and retired
  `dnd2024.encounter-initiative-order`, `dnd2024.encounter-turn-state`, and
  `dnd2024.turn-budget` without aliases, dual reads, or dual writes.
- Added immutable generic `stateSpaceId` mechanic context so catalog JavaScript can construct a
  complete relationship-edge reference without placing D&D vocabulary or branching in C#.
- Preserved existing mechanic IDs, deterministic tie decisions, rest interruption, replay, and
  typed-effect transaction behavior. Atomic materialization is bounded to 18 actors so its worst
  permitted cleanup remains within the generic 128-effect transaction limit.

## Verification

- `roleplay validate catalog`: passed; 144 records validated, with the 21 pre-existing
  near-duplicate warnings and no live-data writes.
- Focused generic projection/application tests: passed, 30/30.
- Focused encounter/lifecycle/budget tests: passed, 14/14 before the added late-failure rollback
  proof; that proof also passes in the final full suite.
- Prototype record audit: 2,329/2,329 planned records, zero missing or duplicate IDs, unresolved
  references, component errors, or archetype-composition errors.
- Prototype tests: passed, 107/107.
- Full `DantesRoleplay.slnx` test suite: passed, 1,410 core tests plus 21 Local AI tests;
  1,431/1,431 total.
- `git diff --check`: no whitespace errors attributable to this slice.
- No protocol walk was required because this slice changed no MCP operation or dependency surface.

## Deliberate exclusions

Joining or leaving after Initiative, surprise, Readied/delayed actions, authored reaction windows,
tactical position/path/terrain, encounter creation, live-state migration, companion UI, decomposed
items/inventory, source-complete character creation, conditions/effect entities, rest convergence,
and new gameplay remain outside this slice. Item/inventory convergence is the next ordered leaf.
