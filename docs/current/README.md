# Current project guide

This directory is the maintained entry point for humans and LLMs working on DantesRoleplay. Read [AGENTS.md](../../AGENTS.md) and this page first. In most tasks, read only one topic guide below, then inspect the relevant code, catalog records, and focused tests.

## Task routing

| Task | Read next |
| --- | --- |
| Decide where behavior belongs or understand runtime boundaries | [ARCHITECTURE.md](ARCHITECTURE.md) |
| Change code, tests, schemas, or catalog content | [DEVELOPMENT.md](DEVELOPMENT.md) |
| Compare, validate, export, or import catalog/database records | [CATALOG.md](CATALOG.md) |
| Run the server, connect a client, or verify the protocol | [OPERATIONS.md](OPERATIONS.md) |

Do not preload every guide. `docs/world/` contains world-specific working material and media, including the unfinished Thalorien workspace; `docs/pdfs/` contains source references. Neither is general implementation context. Implementation history is available in version control and should not be recreated as permanent plans or receipts by default.

## Current system state

Last checked: 2026-08-31.

- The solution builds with no warnings or errors.
- Catalog validation succeeds for 438 records: 29 mechanics, 74 procedures, 54 components, 14 event types, 2 subscriptions, and 265 entities.
- Catalog validation reports no warnings.
- Focused catalog and world-feature tests pass.
- Full-suite acceptance is not currently claimed. Some D&D application-layout/source-registration tests still expect an older catalog shape.

Treat this status as orientation, not as a substitute for running checks relevant to a change.

## Documentation rule

Keep this directory small and current. Add durable guidance only when it helps future contributors make architectural, development, catalog, or operational decisions. Put task plans in the task or issue, and rely on tests and version control for implementation history.
