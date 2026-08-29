# System modularization Slice 13 implementation — building blocks physical component

Status: **accepted**  
Owner/roadmap: [Platform roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)  
Dependency tree/leaf: [Modularization Leaves 7/8](SYSTEM-MODULARIZATION-DEPENDENCY-PLAN.md#ordered-leaves)  
Ruleset alignment: **ruleset-neutral**  
Source ID and locator: **not applicable**  
Outcome: Co-locate content hashing and category-path primitives/tests, and remove D&D-specific
examples from the generic category implementation.  
Exclusions: Category grammar/semantics, catalog hierarchy, protocol adapter, runtime IDs, rules,
database, MCP kinds, and local AI.  
Allowed files/areas: ContentHash/CategoryPath sources/tests, building-block manifest, literal
inventory, planning evidence.  
Stop point: Primitive/guard tests and build pass with three fewer compiled `dnd2024` literals.

## Confirmed decisions

Generic diagnostic examples use neutral `catalog.example.*` paths. This changes example/error prose
only, not accepted syntax or category matching.

## D&D 5e 2024 alignment

Not applicable; the purpose is to remove D&D vocabulary from generic code.

## External implementation reference

No Foundry reference is relevant.

## Prerequisite evidence

- [Slice 12 receipt](SYSTEM-MODULARIZATION-SLICE-12-RECEIPT.md).
- Category and content-hash tests own the primitive behavior.
- The architecture literal baseline records exactly three `dnd2024` occurrences in CategoryPath.

## Runtime artifacts

None; existing types retain assemblies/namespaces.

## Authoritative state and closed input

Existing category grammar and content-hash inputs remain unchanged.

## Behavior, result, and typed effects

Physical placement plus neutral diagnostic examples. Parsing, normalization, matching, and hashing
remain byte-compatible for the same inputs.

## Failure, replay, and rollback contract

Existing invalid-category and hash tests retain coverage. There is no persistence or state change.

## Implementation sequence

Move primitives/tests; replace only D&D example prose; reduce exact legacy baseline; update manifest;
verify; receipt.

## Acceptance matrix

| Case | Evidence |
| --- | --- |
| Positive/negative | Category and hash suites pass. |
| Boundary | No `dnd2024` remains in building-block production source. |
| Compatibility | Grammar/API/namespace/assembly unchanged. |

## Verification commands

- `dotnet test ... --filter "FullyQualifiedName~CategoryPathTests|FullyQualifiedName~ContentHashTests|FullyQualifiedName~GuardTests"`
- `dotnet build DantesRoleplay.slnx --no-restore`

## Completion receipt and exit gate

Evidence is recorded in [the Slice 13 receipt](SYSTEM-MODULARIZATION-SLICE-13-RECEIPT.md). Stop before another component move.
