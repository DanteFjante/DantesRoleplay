# System modularization Slice 14 implementation — catalog physical component

Status: **accepted**  
Owner/roadmap: [Platform roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)  
Dependency tree/leaf: [Modularization Leaf 7](SYSTEM-MODULARIZATION-DEPENDENCY-PLAN.md#ordered-leaves)  
Ruleset alignment: **ruleset-neutral**  
Source ID and locator: **not applicable**  
Outcome: Co-locate generic catalog layout/read/validate/import/export, authored-file models,
bootstrap readers/seeders, hash backfill, hosting, and focused generic tests.  
Exclusions: `catalog/` authored content, feature/ruleset catalog tests, dirty concurrent coverage
test, CLI commands, APIs/namespaces, migrations, and local AI.  
Allowed files/areas: DataAccess Catalog/Bootstrap/ContentHashBackfill sources, named generic catalog
tests, catalog manifest/evidence.  
Stop point: Generic catalog/guard tests and build pass; authored content is untouched.

## Confirmed decisions

Catalog file mechanics and bootstrap installation share the authored-catalog component. CLI
commands remain catalog-tools consumers. The concurrently modified `CatalogCoverageTests.cs` stays
at its existing path to avoid moving user work during this slice.

## D&D 5e 2024 alignment

Not applicable; no authored rule/content changes.

## External implementation reference

No Foundry reference is relevant.

## Prerequisite evidence

- [Slice 13 receipt](SYSTEM-MODULARIZATION-SLICE-13-RECEIPT.md).
- Catalog import/export/validation, bootstrap rule, hash, and procedure-file tests own the generic
  behavior.

## Runtime artifacts

None; existing types retain assemblies/namespaces.

## Authoritative state and closed input

Repository `catalog/` remains authored authority. Existing options, file shapes, hashes, and stores
remain unchanged.

## Behavior, result, and typed effects

Physical placement only; reading, validation, disposable import, export, bootstrap seeding, and
backfill behavior remain unchanged.

## Failure, replay, and rollback contract

Generic catalog tests retain invalid/atomic/fresh-import coverage. No live catalog import runs.

## Implementation sequence

Move generic sources and unmodified focused tests; leave authored content/feature tests/dirty test;
update manifest; verify; receipt.

## Acceptance matrix

| Case | Evidence |
| --- | --- |
| Positive/negative | Generic import/export/validation/bootstrap suites pass. |
| Boundary | Authored catalog and game-feature tests stay outside. |
| Compatibility | Same types, assemblies, registration, and file behavior. |

## Verification commands

- `dotnet test ... --filter "FullyQualifiedName~CatalogImportTests|FullyQualifiedName~CatalogExportTests|FullyQualifiedName~CatalogValidationTests|FullyQualifiedName~BootstrapRuleTests|FullyQualifiedName~ContentHashTests|FullyQualifiedName~ProcedureFileHashTests|FullyQualifiedName~GuardTests"`
- `dotnet build DantesRoleplay.slnx --no-restore`

## Completion receipt and exit gate

Evidence is recorded in [the Slice 14 receipt](SYSTEM-MODULARIZATION-SLICE-14-RECEIPT.md). Stop before catalog-tools or another move.
