# Current project guide

This directory is the maintained entry point for humans and LLMs working on DantesRoleplay. Read [AGENTS.md](../../AGENTS.md) and this page first. In most tasks, read only one topic guide below, then inspect the relevant code, catalog records, and focused tests.

## Task routing

| Task | Read next |
| --- | --- |
| Resume the active upgrade, master landing, cleanup, manual, website, and DND2024 work | [UPGRADE-COORDINATION.md](UPGRADE-COORDINATION.md) |
| Decide where behavior belongs or understand runtime boundaries | [ARCHITECTURE.md](ARCHITECTURE.md) |
| Understand the generic platform's implemented requirements, dynamism, web, AI, retrieval, and storage | [PLATFORM-REQUIREMENTS.md](PLATFORM-REQUIREMENTS.md) |
| Discuss the intended product, differences from today's platform, and the draft alignment plan | [PRODUCT-DIRECTION.md](PRODUCT-DIRECTION.md) |
| Plan platform implementation, shared contracts, and parallel agent ownership | [PLATFORM-IMPLEMENTATION.md](PLATFORM-IMPLEMENTATION.md) |
| Establish the shared implementation foundation before platform workstreams | [00 — Shared foundation](platform-implementation/00-shared-foundation.md) |
| Implement the proposed field-based website/object-read design while preserving strict ECS writes | [FIELD-BASED-OBJECTS-PLAN.md](FIELD-BASED-OBJECTS-PLAN.md) |
| Upgrade or repair the D&D 2024 catalog and React website against the generic platform | [DND2024-UPGRADE-PLAN.md](DND2024-UPGRADE-PLAN.md) |
| Change code, tests, schemas, or catalog content | [DEVELOPMENT.md](DEVELOPMENT.md) |
| Improve test runtime, select affected domains, or remove obsolete/redundant tests in slices | [TEST-SUITE-IMPROVEMENT.md](TEST-SUITE-IMPROVEMENT.md) |
| Compare, validate, export, or import catalog/database records | [CATALOG.md](CATALOG.md) |
| Run the server, connect a client, or verify the protocol | [OPERATIONS.md](OPERATIONS.md) |
| Inspect the delivered item dossier, supported boundaries and closed IV00–IV10 release | [ITEM-VIEW-IMPLEMENTATION.md](ITEM-VIEW-IMPLEMENTATION.md) |
| Execute cleanup slices 0–17 in order, with registered objects, C# batching and preauthorized unattended implementation | [SYSTEM-AUDIT.md](SYSTEM-AUDIT.md) |
| Review open correctness and acceptance follow-ups from slices 0–17 | [SLICE-REVIEW.md](SLICE-REVIEW.md) |
| Find prioritized code cleanup, optimization and simplification opportunities | [SOLUTION-CLEANUP.md](SOLUTION-CLEANUP.md) |

Do not preload every guide. `docs/world/` contains world-specific working material and media, including the unfinished Thalorien workspace; `docs/pdfs/` contains source references. Neither is general implementation context. Implementation history is available in version control and should not be recreated as permanent plans or receipts by default.

## Verification status

This entry page does not cache volatile test totals or catalog counts. Run the focused and full
checks required by [DEVELOPMENT.md](DEVELOPMENT.md) against the current checkout. The numbered
entries in [SYSTEM-AUDIT.md](SYSTEM-AUDIT.md) retain the dated acceptance evidence for that cleanup
program; Git retains the implementation history.

## Documentation rule

Keep this directory small and current. Add durable guidance only when it helps future contributors make architectural, development, catalog, or operational decisions. Put task plans in the task or issue, and rely on tests and version control for implementation history.
