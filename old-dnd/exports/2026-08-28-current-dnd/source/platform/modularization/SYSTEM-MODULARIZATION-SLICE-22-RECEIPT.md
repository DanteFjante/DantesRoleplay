# System modularization Slice 22 receipt — Local routing adapter quarantine

Status: **Verified**  
Completed: 2026-08-23

## Delivered

- Moved the model-assisted game-action routing contract, coordinator, and focused tests under
  `src/game-adapters/dantes-roleplay/local-routing`.
- Removed the corresponding legacy inventory overrides.
- Established routing as a consumer of generic completion, not part of local AI.

## Evidence

- Focused local-routing and architecture tests: 19 passed, 0 failed.
- Solution build: 0 warnings, 0 errors.

## Boundary retained

No proposal semantics, catalog/world input, schema validation, APIs, namespaces, assemblies,
registration, fallback, or model-provider behavior changed.
