# Character creation CC2H2 implementation - automatic Initiative rest interruption

Status: **accepted**
Owner/roadmap: [D&D 2024 application roadmap](ROADMAP.md)
Dependency tree/leaf: [character creation dependency tree](DND2024-CHARACTER-CREATION-DEPENDENCY-PLAN.md), CC2H2 within CC2
Ruleset alignment: `dnd2024-owned`
Source ID and locator: `source.dnd2024.srd-5.2.1`, *Playing the Game > Combat > The Order of Combat > Initiative* (PDF p. 13), *Rules Glossary > Long Rest* (PDF p. 185), and *Rules Glossary > Short Rest* (PDF p. 187)
Outcome: setting the authoritative encounter Initiative order automatically applies active-rest interruptions for every rolling participant in the same transaction.
Exclusions: standalone preview mutation, damage, spellcasting, walking/physical exertion, finish/recovery, Resourceful, and turn lifecycle.
Allowed files/areas: existing individual/encounter Initiative mechanics and procedures, the rest-episode procedure, focused D&D tests, this document, dependency plan, roadmap/status, and one receipt.
Stop point: stop after encounter Initiative interruption is accepted; do not implement spell or movement adapters.
Recommended model: `gpt-5.6-sol`, reasoning `high`.

## Confirmed decisions

- The user's 2026-08-27 instruction to finish CC2 confirms this bounded cross-owner behavior and in-place result-shape extension. No new permanent ID, schema, migration, public kind, or C# rule is needed.
- `mechanic.dnd2024.initiative.roll` remains the effect-free per-participant calculator and returns a closed optional interruption plan from its declared participant projection.
- `mechanic.dnd2024.encounter-initiative-order` is the authoritative combat-start/root owner. It validates every child plan and commits the order plus all active-rest consequences atomically.
- A duration-ready rest is unchanged and corrupt rest scope fails the complete encounter-order action.

## D&D 5e 2024 alignment

| Rule concern | Source meaning | Existing owner | Consequence |
| --- | --- | --- | --- |
| Initiative | Every combat participant rolls Initiative when combat starts | individual Initiative child and encounter-order root | Each contained participant is evaluated exactly once by the existing composition. |
| Rest interruption | Rolling Initiative interrupts Short and Long Rests; Long Rest resumption adds 1 hour | rest episode/interruption owners | Each active Short Rest stops with no benefit; each active Long Rest gains one count/hour. |
| Authority | Roster, Dexterity, roll, episode, membership, and effects are not caller values | containment, role projections, seeded child composition | Callers still supply only per-participant roll circumstances and actual tie decisions. |

## External implementation reference

Foundry dnd5e commit `275bed0be4ccfa15e6b3347acccb8da8784726d9`,
`module/documents/actor/actor.mjs` lines 1884-1918, separates the actor Initiative roll, combatant
update, and pre/post hooks. CC2H2 adopts the phase separation and root orchestration idea only; no
Foundry code, data, UI, assets, hooks, or runtime dependency is reused.

## Prerequisite evidence

- [CC2G](evidence/DND2024-CHARACTER-CREATION-CC2G-RECEIPT.md) proves rest shapes/consequences.
- [CC2H1](evidence/DND2024-CHARACTER-CREATION-CC2H1-RECEIPT.md) proves an existing root may consume optional participant rest state without caller rest roles.
- Existing encounter tests prove exact roster composition, deterministic rolls/ties, one immutable order, replay, and transaction ownership.

## Runtime artifacts

- Revise individual Initiative requirements/output in place to project optional rest state and return `restInterruption` plan data with no effects.
- Revise encounter-order JavaScript to validate and translate child plans into typed effects.
- Revise the three governing procedures and focused tests. Production C# is forbidden.

## Authoritative state and closed input

Individual input remains empty or exact roll circumstances. Encounter input remains exact
`participants` child inputs plus `tieDecisions`. Optional episode/relationships are declared on the
child subject role. No rest value is caller authority.

## Behavior, result, and typed effects

The individual child validates optional episode/membership state and returns null for absent/ready
rest, a Short Rest removal plan, or a Long Rest next-episode plan. The encounter root validates the
subject identity and every plan, calculates/sorts Initiative as before, then emits one order add plus
each participant's planned rest effects. Events, notifications, recovery, and benefits remain empty.

## Failure, replay, and rollback contract

Bad roster/input/ties, malformed child output, corrupt/orphaned rest, unsafe duration, stale effects,
or transaction failure yields no order or rest change. Exact replay creates neither another order
nor another interruption.

## Implementation sequence

1. Extend the individual projection/result and encounter validation/effects.
2. Update governing procedures.
3. Add mixed Short/Long, absent/ready, corruption, replay, and compatibility tests.
4. Run focused tests, complete D&D tests, catalog validation, and sequential full suite.
5. Record acceptance and stop before CC2H3.

## Acceptance matrix

| Case | Expected evidence |
| --- | --- |
| Mixed active Short/Long participants | Order, Short removal, and Long count/hour commit together. |
| No rest | Existing one-order-effect behavior remains. |
| Ready rest | Order commits; ready episode/membership remain unchanged. |
| Corrupt/orphaned participant rest | No order or rest effect. |
| Replay | No duplicate order/interruption. |
| Tie/roll compatibility | Existing deterministic ordering remains green. |

## Verification commands

- Focused encounter/Initiative/rest tests.
- `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore --filter FullyQualifiedName~Dnd2024AbilityCheckTests`
- `dotnet run --project DantesRoleplay.Tools/DantesRoleplay.Tools.csproj --no-restore -- validate catalog`
- `dotnet test DantesRoleplay.slnx --no-build --no-restore --maxcpucount:1`

No protocol walk is required because no protocol surface or dependency registration changes.

## Completion receipt and exit gate

Accepted by [the CC2H2 completion receipt](evidence/DND2024-CHARACTER-CREATION-CC2H2-RECEIPT.md).
Stop before the non-Cantrip spell adapter.
