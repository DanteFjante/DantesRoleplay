# System modularization Slice 3 receipt — per-component composition

Status: **Verified**  
Completed: 2026-08-23

## Delivered

- Split the former central registration list into internal component-owned entry points under
  `src/system/*/hosting` and `src/game-adapters/dantes-roleplay/hosting`.
- Retained `AddDantesRoleplayDataAccess` and authenticated registration as compatibility facades
  with the same signatures, validation, lifetimes, conditional SQLite wiring, and service set.
- Kept the current knowledge/Ollama wiring in the quarantined game-adapter composition pending the
  later standalone local-AI extraction.
- Extended the production-source inventory to cover compiled source below `src`.

## Evidence

- Focused guard/DI consumer matrix: 28 passed, 0 failed.
- Solution build: succeeded; the only warning was the existing xUnit analyzer warning in
  `KnowledgeAcquisitionCoordinatorTests` when tests were rebuilt.
- Full suite: 817 passed and 2 `CatalogFeature20Tests` failed while setting encounter initiative.
  Those tests construct `ActionRunner` and all dependencies directly and never use the changed DI
  facade; both failures reproduce in isolation and are outside this composition slice.

## Boundary retained

No implementation source moved, service lifetime changed, local-AI capability was added, or
catalog/database/MCP contract was changed. Physical movement of one generic component at a time is
next.
