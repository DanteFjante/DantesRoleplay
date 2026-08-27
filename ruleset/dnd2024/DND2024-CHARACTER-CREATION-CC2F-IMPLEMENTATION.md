# Character creation CC2F implementation - authenticated rest episode start

Status: **accepted**
Owner/roadmap: [D&D 2024 application roadmap](ROADMAP.md)
Dependency tree/leaf: [character creation dependency tree](DND2024-CHARACTER-CREATION-DEPENDENCY-PLAN.md), CC2F within CC2
Ruleset alignment: `dnd2024-owned` with an explicit `game` base-application dependency
Source ID and locator: `source.dnd2024.srd-5.2.1`, *Rules Glossary > Long Rest* (PDF p. 185)
and *Rules Glossary > Short Rest* (PDF p. 187)
Outcome: Start one Short or Long Rest episode from the accepted immutable policy, authoritative
current Hit Points, and the active generic world's authoritative clock coordinate.
Exclusions: time advancement, sleep/activity accumulation, interruption detection, ready/completed
transition, recovery, the 16-hour restart receipt, Resourceful, and public endpoints.
Allowed files/areas: this document/tree/roadmap; D&D rest-episode component, begin mechanic, and
procedure; D&D source-registry procedure; the smallest generic ECS base-owner admission correction
and regression test; disposable D&D harness/base mapping and focused tests; completion receipt.
Stop point: accepted authenticated active rest start with no progress or benefit.

## Confirmed decisions

- Re-adopt permanent `dnd2024.rest-episode`, `mechanic.dnd2024.rest.begin`, and
  `procedure.mechanic.dnd2024.rest-episode` IDs from retained recovery evidence, correcting pinned
  SRD page locators and adapting them to the application-scoped ECS.
- Reuse, never copy, `game.core.world.root` and `game.core.world.clock` through the kernel's
  explicit base-application mapping. The user's earlier request for automapped component
  dependencies across modular stateless components confirms this dependency direction; no new
  world component, formula, or C# rule branch is introduced.
- `dnd2024` registrations that use rest mechanics declare the existing `game` owner as an ordered
  base application. A state space without that exact base fails projection instead of accepting
  caller-provided time.
- The episode's `ready` schema state is reserved for a later authenticated progress slice. CC2F
  creates only `active` and neither assumes quiet activity nor marks elapsed time.

## D&D 5e 2024 alignment

| Rule concern | Source meaning | Existing owner | Consequence |
| --- | --- | --- | --- |
| Eligibility | starting either rest requires at least 1 HP | `dnd2024.hit-points` | require valid authoritative current HP >= 1 |
| Duration | Short Rest is 60 minutes; Long Rest is at least 480 | accepted immutable standard policy | derive `requiredMinutes`; caller supplies only kind |
| Start coordinate | elapsed duration requires one authoritative current time | base `game.core.world.clock` | record clock minute/revision-derived start, never request time |
| Active world | rest belongs to the selected campaign world's time scope | base `game.core.world.root` plus D&D relationship | require active root and add exact rest-world membership |
| Activity/interruption | validity depends on later sleep/light-activity and interruption evidence | no accepted complete event family yet | start only; no progress or benefit inference |

## External implementation reference

Foundry dnd5e commit `275bed0be4ccfa15e6b3347acccb8da8784726d9` was inspected at
`module/applications/actor/rest/long-rest-dialog.mjs` and
`module/documents/actor/actor.mjs`. Useful reference behavior is a distinct initiation/configuration
phase before result calculation, bulk mutation, and the completed-rest hook. CC2F adopts only that
phase separation; it does not adopt UI, direct actor mutation, code, data, assets, optional settings,
or a runtime dependency.

## Prerequisite evidence

- [CC2E receipt](evidence/DND2024-CHARACTER-CREATION-CC2E-RECEIPT.md) proves exact immutable policy
  values and corrected source locators.
- [Application-kernel Slice 5 receipt](../../platform/application-kernel/receipts/APPLICATION-KERNEL-SLICE-5-RECEIPT.md)
  proves cross-application reads are allowed only from explicitly declared base applications.
- Generic typed component/relationship effects, atomic application actions, replay, and exact
  projection mapping are accepted owners.
- The focused implementation probe found two incomplete parts of the same accepted base seam: the
  ECS component write guard admitted only the primary application, and automatic action mapping
  returned before testing the matching owner of an explicitly qualified base component. CC2F may
  correct both generically against the immutable revision's exact primary/direct-base set;
  unrelated owners must remain rejected.
- Root world and clock schemas/mechanic already own active-world identity, bounded current minute,
  clock revision, and scoped clock-advance events in authored `catalog/`.

## Runtime artifacts

| Artifact | Boundary |
| --- | --- |
| `dnd2024.rest-episode` | one active/ready timing-evidence state on a creature; no recovery/result |
| `dnd2024.rest.world` | application-owned relationship from exact active world to episode holder |
| rest begin mechanic | validates creature/world/policy and atomically adds episode plus membership |
| rest-episode procedure | governs start state and forbids progress/completion inference |
| source-registry procedure update | declares `game` base dependency for world/clock-consuming D&D mechanics |
| generic base-state seam | admits exact primary/direct-base component owners and resolves an explicitly qualified base ID against its matching allowed owner |

## Authoritative state and closed input

Roles are exactly `creature`, `world`, and `policy`. Creature projects Hit Points and optional rest
episode; world projects base-owned root/clock plus relationships; policy projects the accepted D&D
rest policy. Input is exactly `{"kind":"short"}` or `{"kind":"long"}`. Caller cannot provide
world/policy IDs in input, time, HP, duration, status, source, interruption, activity, recovery, or
effects.

## Behavior, result, and typed effects

Validate distinct roles, canonical policy identity/data, active world root, exact bounded world
clock, valid HP, absent episode, and absent matching membership. Derive the start minute and duration
from state. Return exactly one component add and one relationship create in one generic transaction,
fixed source provenance, empty events/notifications, and deterministic result data.

## Failure, replay, and rollback contract

Missing/extra/invalid input, missing base mapping, wrong/inactive/corrupt world, corrupt clock,
wrong/corrupt policy, missing/corrupt/zero HP, duplicate episode/membership, non-distinct roles, or
out-of-range state fails before effects. Same-operation replay produces no second write; a distinct
duplicate fails and preserves component/relationship revisions. Generic transaction rollback owns
injected failure across the two effects.

## Implementation sequence

1. Add the bounded episode component/schema and procedure.
2. Add the begin mechanic and exact D&D-to-`game` base dependency declaration.
3. Correct the generic ECS write guard to match the accepted primary/direct-base projection boundary
   and add a kernel regression proving base allowed/unrelated rejected.
4. Extend only the disposable D&D harness with the explicit base registration/types/mapping.
5. Add focused positive, negative, replay, duplicate, and base-boundary tests.
6. Run focused tests, D&D regression, catalog validation, full acceptance, and write the receipt.

## Acceptance matrix

| Case | Expected evidence |
| --- | --- |
| Short/Long start | state derives 60/480 minutes and exact source locator from bound policy |
| Authoritative coordinate | stored start equals base world clock; caller time is rejected |
| HP gate | 1 HP succeeds; 0/missing/corrupt HP fails without effects |
| World/policy gate | inactive/corrupt/wrong policy or missing base mapping fails closed |
| Duplicate/replay | replay is stable; distinct second start fails without revision change |
| Atomicity | episode and relationship appear together or neither does |
| No premature completion | no progress, ready, recovery, event, notification, or Inspiration grant |
| Compatibility | CC1-CC2E, D&D regressions, catalog validation, and full suite remain green |

## Verification commands

- `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~Dnd2024AbilityCheckTests.Character_creation_rest_begin`
- `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~Dnd2024AbilityCheckTests`
- `dotnet run --project DantesRoleplay.Tools/DantesRoleplay.Tools.csproj -c Release --no-build -- validate catalog`
- `dotnet test DantesRoleplay.slnx -c Release --no-restore`

No protocol walk is required because CC2F adds no MCP kind/endpoint or dependency registration
service change.

## Completion receipt and exit gate

Accepted by [the CC2F completion receipt](evidence/DND2024-CHARACTER-CREATION-CC2F-RECEIPT.md).
CC2F is collapsed to verified in the dependency tree. Work stopped before rest
progress/interruption, completion/recovery, Resourceful, or actor creation.
