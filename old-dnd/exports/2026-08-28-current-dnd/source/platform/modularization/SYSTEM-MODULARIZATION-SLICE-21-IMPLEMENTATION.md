# System modularization Slice 21 implementation — Travel adapter quarantine

Status: **accepted**  
Owner/roadmap: [Platform roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)  
Dependency tree/leaf: [Game-code eviction branch](SYSTEM-MODULARIZATION-DEPENDENCY-PLAN.md#dependency-tree)  
Ruleset alignment: **ruleset-neutral infrastructure consumer with game-facing travel semantics**  
Source ID and locator: **not applicable**  
Outcome: Move journey planning, mode-aware itinerary, and small-world composition code and focused
tests into a Travel adapter quarantine.  
Exclusions: MCP tools, APIs/namespaces/assemblies, behavior, catalog mechanics, local routing, local
AI, DbContext, and the user's dirty files.  
Allowed files/areas: Named World travel domain, SmallWorld planner, three focused tests,
inventory/evidence.  
Stop point: Travel/guard tests and build pass.

## Confirmed decisions

World, traveller, conveyance, journey, itinerary, and small-world concepts are game-adapter
vocabulary. They may consume generic state and action capabilities but do not belong to the kernel.

## D&D 5e 2024 alignment

No D&D rule is implemented or changed.

## External implementation reference

No Foundry review is relevant.

## Prerequisite evidence

- [Knowledge quarantine receipt](SYSTEM-MODULARIZATION-SLICE-20-RECEIPT.md).
- Existing journey, itinerary, and small-world suites cover moved behavior.

## Runtime artifacts

None; same types, assemblies, namespaces, persistence, and host registrations.

## Authoritative state and closed input

Existing state snapshots, authored components, and request contracts remain authoritative and
unchanged.

## Behavior, result, and typed effects

Physical placement only. Planning, validation, traversal, fingerprints, and staged composition are
unchanged.

## Failure, replay, and rollback contract

Existing focused tests preserve invalid, unknown, unreachable, blocked, conflict, and no-change
outcomes.

## Implementation sequence

Move domain/planner/tests; remove stale overrides; verify; receipt.

## Acceptance matrix

| Case | Evidence |
| --- | --- |
| Positive/negative | Three focused Travel suites pass. |
| Boundary | Game travel vocabulary is outside system capability roots. |
| Compatibility | Same APIs, assemblies, storage, registration, and results. |

## Verification commands

- `dotnet test ... --filter "FullyQualifiedName~WorldJourneyPlanTests|FullyQualifiedName~WorldModeAwareItineraryTests|FullyQualifiedName~WorldFeature17SmallWorldCompositionTests|FullyQualifiedName~GuardTests"`
- `dotnet build DantesRoleplay.slnx --no-restore`

## Completion receipt and exit gate

Record Slice 21 receipt and stop before local routing or local AI.
