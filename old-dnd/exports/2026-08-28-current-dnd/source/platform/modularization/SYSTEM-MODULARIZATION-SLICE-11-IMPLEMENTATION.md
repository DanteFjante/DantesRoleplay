# System modularization Slice 11 implementation — actions physical component

Status: **accepted**  
Owner/roadmap: [Platform roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)  
Dependency tree/leaf: [Modularization Leaf 7](SYSTEM-MODULARIZATION-DEPENDENCY-PLAN.md#ordered-leaves)  
Ruleset alignment: **ruleset-neutral**  
Source ID and locator: **not applicable**  
Outcome: Co-locate the closed generic action envelope/runner, hosting, and focused runner tests.  
Exclusions: Local route proposal contracts/coordinator, story adapters, protocol tools, catalog
mechanics, game actions/rules, APIs/namespaces, migrations, and local AI.  
Allowed files/areas: Generic action domain/runner/tests, action manifest, architecture guard path,
and planning evidence.  
Stop point: Generic action/guard tests and build pass; game route source remains quarantined.

## Confirmed decisions

`LocalActionRouting.cs` is a game-facing local-model consumer and does not move into the generic
actions component. Story runner aliases remain game-adapter composition.

## D&D 5e 2024 alignment

Not applicable; the action host is generic.

## External implementation reference

No Foundry reference is relevant.

## Prerequisite evidence

- [Slice 10 receipt](SYSTEM-MODULARIZATION-SLICE-10-RECEIPT.md).
- Action runner and participant tests cover selection, composition, effects, audit, failures, and
  deterministic behavior.

## Runtime artifacts

None; types retain assemblies/namespaces.

## Authoritative state and closed input

Existing closed `ActionRequest`, `ActionInput`, result/error, and runner contracts remain unchanged.

## Behavior, result, and typed effects

Physical placement only. Selection, projections, mechanics, deterministic seed, effects,
transactions, and audit behavior remain unchanged.

## Failure, replay, and rollback contract

Existing runner tests retain invalid-input, ambiguity, replay, and no-change coverage.

## Implementation sequence

Move generic domain/runner/tests; update retired-recovery source guard path; mark manifest; verify;
receipt.

## Acceptance matrix

| Case | Evidence |
| --- | --- |
| Positive/negative | Generic action runner suites pass. |
| Boundary | Route/story/protocol/game consumers stay outside. |
| Replay/rollback | Existing runner tests retain coverage. |
| Compatibility | Same types, assemblies, DI, and transaction owner. |

## Verification commands

- `dotnet test ... --filter "FullyQualifiedName~ActionRunnerTests|FullyQualifiedName~ActionRunnerParticipantTests|FullyQualifiedName~GuardTests"`
- `dotnet build DantesRoleplay.slnx --no-restore`

## Completion receipt and exit gate

Evidence is recorded in [the Slice 11 receipt](SYSTEM-MODULARIZATION-SLICE-11-RECEIPT.md). Stop before another move.
