# Character creation CC2G implementation - rest activity and interruption progress

Status: **accepted**
Owner/roadmap: [D&D 2024 application roadmap](ROADMAP.md)
Dependency tree/leaf: [character creation dependency tree](DND2024-CHARACTER-CREATION-DEPENDENCY-PLAN.md), CC2G within CC2
Ruleset alignment: `dnd2024-owned` with the accepted `game` base-application dependency
Source ID and locator: `source.dnd2024.srd-5.2.1`, *Rules Glossary > Long Rest* (PDF p. 185)
and *Rules Glossary > Short Rest* (PDF p. 187)
Outcome: Progress one active Short or Long Rest using declared sleep/light activity over an elapsed
interval derived only from the authoritative base-world clock, and record exact source interruptions.
Exclusions: automatic event adapters, time advancement, rest finishing, Hit Die spending/recovery,
HP/maximum/ability/Exhaustion recovery, source-specific recharge, the 16-hour restart receipt,
Resourceful, Heroic Inspiration, and public endpoints.
Allowed files/areas: this document/tree/roadmap/status; the existing D&D rest-episode schema,
description, begin mechanic, and procedure; new progress/interruption mechanics; disposable D&D
harness helpers and focused tests; one acceptance-only explicit web service-binding annotation for
an unrelated uncommitted route test discovered by the full run; completion receipt.
Stop point: accepted activity/interruption accounting and duration-ready state with no benefit.

## Confirmed decisions

- The user's continuing character-creation instruction and prior approval of SRD-faithful modular
  changes confirm the permanent `mechanic.dnd2024.rest.progress` and
  `mechanic.dnd2024.rest.interrupt` IDs and the bounded evolution of `dnd2024.rest-episode`.
- Caller input declares only gameplay intent: `activity` for the complete unclassified clock interval
  or an exact source interruption kind at the current coordinate. Minutes, revisions, policy values,
  counters, readiness, and effects remain derived state.
- `ready` means only that required duration/activity evidence is satisfied. It is not a finished
  rest and grants nothing.
- The current application action owner rejects event-reaction mechanics and event output. CC2G does
  not bypass that boundary or claim automatic damage/Initiative/spell/travel interruption detection;
  those adapters remain a prerequisite before finish/recovery can be accepted.

## D&D 5e 2024 alignment

| Rule concern | Source meaning | Existing owner | Consequence |
| --- | --- | --- | --- |
| Short Rest activity | one hour doing nothing more strenuous than reading, talking, eating, or standing watch | standard rest policy plus rest episode | accept only `light`; classify the whole authoritative elapsed interval; ready at 60 minutes |
| Short Rest interruption | Initiative, a non-Cantrip spell, or damage stops the rest and confers no benefits | standard rest policy | exact interruption removes episode and membership atomically; grants nothing |
| Long Rest activity | at least eight hours, at least six sleeping, no more than two light activity | standard rest policy plus rest episode | accumulate sleep/light separately and require all limits before `ready` |
| Long Rest interruption | four exact kinds; immediate resume adds one hour per interruption | standard rest policy | increment interruption count and derived required duration by 60 minutes |
| Partial credit | at least one hour before a Long Rest interruption grants Short Rest benefits | later benefit owner | report whether credit is eligible but do not apply or persist a benefit |
| Time authority | rest rules specify duration, not caller-authored timestamps | base `game.core.world.clock` | derive the unclassified interval and next observed coordinate/revision from exact mapped state |

## External implementation reference

Foundry dnd5e commit `275bed0be4ccfa15e6b3347acccb8da8784726d9` was inspected at
`module/applications/actor/rest/long-rest-dialog.mjs` and
`module/documents/actor/actor.mjs` around `initiateRest`, `_rest`, pre-completion calculation,
bulk update, time advancement, and `dnd5e.restCompleted`. Useful evidence is the separation of rest
configuration, result calculation, one bulk mutation, time advancement, and the post-completion
hook. CC2G adopts phase separation only; it uses no Foundry code, data, UI, assets, optional settings,
or runtime dependency.

## Prerequisite evidence

- [CC2F receipt](evidence/DND2024-CHARACTER-CREATION-CC2F-RECEIPT.md) proves authenticated start,
  exact base-world mapping, and atomic episode/membership creation.
- The accepted immutable policy owns every duration, activity limit, interruption list, and benefit
  handoff used here.
- Generic application actions already own exact projection mapping, optimistic component and
  relationship revisions, replay, and atomic typed effects.

## Runtime artifacts

| Artifact | Boundary |
| --- | --- |
| revised `dnd2024.rest-episode` | adds observed clock coordinate/revision and closed aggregate activity/interruption evidence; no result or recovery |
| revised rest begin | initializes all derived counters from the authoritative clock |
| `mechanic.dnd2024.rest.progress` | classifies the complete interval since the last observation as light activity or sleep and derives duration readiness |
| `mechanic.dnd2024.rest.interrupt` | applies one exact policy interruption at the fully observed current coordinate |
| revised rest-episode procedure | governs start, progress, interruption, and the no-benefit `ready` boundary |

## Authoritative state and closed input

Both mechanics bind exactly `creature`, `world`, and canonical `policy`. Creature supplies one rest
episode plus exact rest-world membership. World supplies active root and clock through the explicit
`game` base. Policy supplies immutable standard values. Progress input is exactly
`{"activity":"light"}` for Short Rest and exactly `light` or `sleep` for Long Rest. Interrupt input
is exactly one interruption kind present in the episode kind's policy list. Caller cannot supply
time, elapsed minutes, revisions, counters, required duration, status, policy/source identity,
partial credit, readiness, recovery, events, or effects.

## Behavior, result, and typed effects

Begin initializes `observedAtMinute` and `observedClockRevision` to the current clock and all
counters to zero. Progress requires an active episode, exact world membership, a monotonically later
clock minute and revision, and valid policy/state. The complete delta since `observedAtMinute` is
classified once. Short Rest increments light activity and becomes duration-ready at 60 minutes.
Long Rest increments sleep or light activity and becomes duration-ready only when total classified
time reaches `480 + 60 * interruptionCount`, sleep is at least 360, and light activity is at most
120. Exceeding the Long Rest light-activity maximum fails without changing the episode.

Interrupt requires the episode already observed through the current clock coordinate/revision, so
no elapsed interval is silently discarded. A Short Rest interruption atomically removes the episode
and world membership and reports no benefit. A Long Rest interruption increments the counter,
derives the increased duration, preserves aggregate activity, stays active, and reports whether at
least 60 classified minutes makes later Short Rest credit eligible. Both return empty events and
notifications.

## Failure, replay, and rollback contract

Malformed/extra input, missing or corrupt roles/components/relationship, wrong policy/world,
inactive world, backward/unchanged/incoherent clock, corrupt counters, unsupported activity or
interruption, progress after ready, unclassified time at interruption, overflow, and source drift
fail before effects. Same-operation replay writes nothing twice. Distinct stale actions fail through
the application's revision envelope. Short interruption's component and relationship removals are
one root transaction and roll back together on failure.

## Implementation sequence

1. Evolve the bounded episode schema/description and initialize new fields at begin.
2. Add progress and interruption mechanic contracts/JavaScript and revise the governing procedure.
3. Extend only the disposable D&D test harness with clock movement helpers.
4. Add focused positive, boundary, negative, replay, stale, and atomic-removal tests.
5. Run focused tests, the D&D class, catalog validation, the full suite, then write the receipt and
   collapse statuses once.

## Acceptance matrix

| Case | Expected evidence |
| --- | --- |
| Begin compatibility | Short/Long begin initializes exact clock observation and zero counters |
| Short progress | 59 light minutes remains active; the next minute becomes ready; no benefit |
| Long activity | 360 sleep plus 120 light becomes ready at 480; invalid/excess light fails |
| Long interruption | each exact interruption adds 60 required minutes; 60-minute partial-credit eligibility is reported only |
| Short interruption | each exact Short Rest interruption atomically removes episode/membership and grants nothing |
| Clock authority | caller time/revision/counters rejected; unchanged/backward/incoherent clock fails |
| State/source boundary | corrupt episode/policy/root/clock/membership or wrong world fails unchanged |
| Ready/replay/stale | ready cannot progress/interruption; replay stable; stale distinct write rejected |
| Compatibility | CC1-CC2F, D&D regressions, catalog validation, and full solution remain green |

## Verification commands

- `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore --filter FullyQualifiedName~Character_creation_rest_`
- `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore --filter FullyQualifiedName~Dnd2024AbilityCheckTests`
- `dotnet run --project DantesRoleplay.Tools/DantesRoleplay.Tools.csproj -- validate catalog`
- `dotnet test DantesRoleplay.slnx --no-build --no-restore --maxcpucount:1`

No protocol walk is required because CC2G changes no MCP surface or dependency registration.

## Completion receipt and exit gate

Write `evidence/DND2024-CHARACTER-CREATION-CC2G-RECEIPT.md`, inspect every authored artifact, and
mark CC2G accepted only after the matrix is green. Stop before automatic event adapters, finish,
recovery, restart cadence, Resourceful, Heroic Inspiration, or actor creation.
