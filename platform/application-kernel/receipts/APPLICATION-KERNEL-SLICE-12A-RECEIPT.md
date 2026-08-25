# Application kernel Slice 12A receipt — planner-neutral catalog handoff and host independence

Status: **accepted**  
Completed: 2026-08-24  
Accepted implementation: [Slice 12A](../APPLICATION-KERNEL-SLICE-12A-IMPLEMENTATION.md)

## Delivered

- Accepted the existing public application-catalog provider/navigator as the read-only kernel handoff
  for future local and remote planners; no parallel retrieval or AI-specific catalog owner was needed.
- Added exact-active tests for two simultaneously published, non-game applications. Each independently
  lists and deterministically searches/inspects its own records through one production provider.
- Proved empty publication discloses no application and cross-application search text, qualified IDs,
  exact content, and snapshot cursors cannot cross scopes or fall back to another application.
- Extended the live three-verb walk with a second non-game catalog and proved its remote record content
  exactly equals direct in-process consumption through the same provider.
- Re-ran the accepted activated `dnd2024` catalog walk and the kernel/component/local-AI vocabulary
  guards. Local completion, embeddings, and vectors are unnecessary for every accepted read.
- Added no runtime API, MCP kind/field, component, catalog record, database, migration, application
  adapter, game rule, or local-AI dependency. No live database or authored catalog content changed.

## Evidence

- Focused activated-provider, live direct/remote protocol, `dnd2024`, component, kernel, and local-AI
  isolation checks: 8 passed, 0 failed.
- Full shared suite: 691 passed, 0 failed.
- Standalone local-AI suite: 20 passed, 0 failed.
- Isolated-output solution build: passed with 0 warnings and 0 errors.
- `git diff --check`: passed. Catalog validation was not required because no catalog artifact changed.

## Deliberate exclusions and next gate

This receipt accepts only the application kernel's read handoff and independence proof. It does not
create a trusted feature index, vector store, planner completion loop, interaction/plan/receipt/recipe
contract, orchestration persistence, authorization/redaction adapter, public interaction kind,
execution coordinator, or web conversation surface. The next gate is interaction-orchestration
Slice 12B: confirm its threat model and the remaining permanent/public semantics before runtime work.
