# System modularization Slice 20 implementation — Knowledge adapter quarantine

Status: **accepted**  
Owner/roadmap: [Platform roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)  
Dependency tree/leaf: [Game-code eviction branch](SYSTEM-MODULARIZATION-DEPENDENCY-PLAN.md#dependency-tree)  
Ruleset alignment: **ruleset-neutral infrastructure consumer with game-facing epistemic semantics**  
Source ID and locator: **not applicable**  
Outcome: Move game-facing knowledge state/search/answer/background/auth contracts, implementations,
and unmodified focused tests into Knowledge adapter quarantine.  
Exclusions: Ollama/provider implementation, generic embedding/completion contracts, development host
policy and its dirty test, MCP worker/tools, semantics, APIs/namespaces/assemblies, DbContext, and
local-AI extraction.  
Allowed files/areas: Named Knowledge/Security domain, DataAccess Knowledge/retrieval implementations,
unmodified Knowledge tests, inventory/evidence.  
Stop point: Knowledge/guard tests and build pass; provider code remains for later local AI.

## Confirmed decisions

Knowledge concepts such as world, fact, rumour, clue, audience, and authorization stay in this
consumer adapter. The future local-AI component receives only generic documents/tasks.

## D&D 5e 2024 alignment

No D&D rule is implemented or changed.

## External implementation reference

No Foundry review is relevant.

## Prerequisite evidence

- [Story quarantine receipt](SYSTEM-MODULARIZATION-SLICE-19-RECEIPT.md).
- Existing knowledge state/search/answer/background suites cover moved behavior.

## Runtime artifacts

None; same types/assemblies/namespaces.

## Authoritative state and closed input

Existing world knowledge state, authorization policy, search/index requests, and model-consumer
validation remain unchanged.

## Behavior, result, and typed effects

Physical placement only; knowledge semantics, scoping, authorization, indexing, model fallback,
and background work remain unchanged.

## Failure, replay, and rollback contract

Existing knowledge tests retain stale/denied/unknown/fallback/no-change coverage.

## Implementation sequence

Move knowledge domain/persistence/unmodified tests; keep providers/development dirty files; remove
stale overrides; verify; receipt.

## Acceptance matrix

| Case | Evidence |
| --- | --- |
| Positive/negative | Knowledge suites pass. |
| Boundary | Game epistemic vocabulary stays outside local AI/system kernel. |
| Compatibility | Same APIs, assemblies, storage, registration, and fallbacks. |

## Verification commands

- `dotnet test ... --filter "FullyQualifiedName~Knowledge|FullyQualifiedName~AuthorizedKnowledge|FullyQualifiedName~GuardTests"`
- `dotnet build DantesRoleplay.slnx --no-restore`

## Completion receipt and exit gate

Record Slice 20 receipt and stop before travel/routing or local AI.
