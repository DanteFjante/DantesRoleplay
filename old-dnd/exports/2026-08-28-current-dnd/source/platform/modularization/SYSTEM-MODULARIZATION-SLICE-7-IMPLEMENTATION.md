# System modularization Slice 7 implementation — feedback physical component

Status: **accepted**  
Owner/roadmap: [Platform roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)  
Dependency tree/leaf: [Modularization Leaf 7](SYSTEM-MODULARIZATION-DEPENDENCY-PLAN.md#ordered-leaves)  
Ruleset alignment: **ruleset-neutral**  
Source ID and locator: **not applicable**  
Outcome: Co-locate generic system-feedback domain, persistence, hosting, and service tests.  
Exclusions: CLI/MCP adapters, EF mappings/migrations, APIs/namespaces, remote feedback, game
semantics, and local AI.  
Allowed files/areas: SystemFeedback domain/services/tests, feedback manifest, planning evidence.  
Stop point: Focused feedback/guard tests and build pass from component paths.

## Confirmed decisions

Compile-link relocation is verified. CLI and MCP adapters remain consumers in their own future
components.

## D&D 5e 2024 alignment

Not applicable; feedback is generic operational evidence.

## External implementation reference

No Foundry reference is relevant.

## Prerequisite evidence

- [Slice 6 receipt](SYSTEM-MODULARIZATION-SLICE-6-RECEIPT.md).
- Existing feedback service, administration, and retention tests cover this owner.

## Runtime artifacts

None; types retain assemblies and namespaces.

## Authoritative state and closed input

Existing feedback records, services, and DbContext mappings remain unchanged.

## Behavior, result, and typed effects

Physical placement only; append-only evidence, triage, and retention semantics are unchanged.

## Failure, replay, and rollback contract

Build/focused tests reject missing source, duplicate inclusion, or service drift.

## Implementation sequence

Move domain/services/service tests; mark manifest migrated; run focused matrix/build; receipt.

## Acceptance matrix

| Case | Evidence |
| --- | --- |
| Positive | Feedback service/admin/retention tests pass. |
| Boundary | CLI and MCP adapters remain outside this component. |
| Compatibility | Same types, assemblies, mapping, and registration. |

## Verification commands

- `dotnet test ... --filter "FullyQualifiedName~SystemFeedback|FullyQualifiedName~GuardTests"`
- `dotnet build DantesRoleplay.slnx --no-restore`

## Completion receipt and exit gate

Evidence is recorded in [the Slice 7 receipt](SYSTEM-MODULARIZATION-SLICE-7-RECEIPT.md). Stop before another move.
