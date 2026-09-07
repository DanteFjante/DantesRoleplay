# Current project guide

This directory is the maintained entry point for humans and LLMs working on DantesRoleplay. Read [AGENTS.md](../../AGENTS.md) and this page first. In most tasks, read only one topic guide below, then inspect the relevant code, catalog records, and focused tests.

## Task routing

| Task | Read next |
| --- | --- |
| Decide where behavior belongs or understand runtime boundaries | [ARCHITECTURE.md](ARCHITECTURE.md) |
| Change code, tests, schemas, or catalog content | [DEVELOPMENT.md](DEVELOPMENT.md) |
| Compare, validate, export, or import catalog/database records | [CATALOG.md](CATALOG.md) |
| Run the server, connect a client, or verify the protocol | [OPERATIONS.md](OPERATIONS.md) |
| Inspect the delivered item dossier, supported boundaries and closed IV00–IV10 release | [ITEM-VIEW-IMPLEMENTATION.md](ITEM-VIEW-IMPLEMENTATION.md) |
| Execute cleanup slices 0–17 in order, with registered objects, C# batching and preauthorized unattended implementation | [SYSTEM-AUDIT.md](SYSTEM-AUDIT.md) |

Do not preload every guide. `docs/world/` contains world-specific working material and media, including the unfinished Thalorien workspace; `docs/pdfs/` contains source references. Neither is general implementation context. Implementation history is available in version control and should not be recreated as permanent plans or receipts by default.

## Verification status

This entry page does not cache volatile test totals or catalog counts. Run the focused and full
checks required by [DEVELOPMENT.md](DEVELOPMENT.md) against the current checkout. The numbered
entries in [SYSTEM-AUDIT.md](SYSTEM-AUDIT.md) retain the dated acceptance evidence for that cleanup
program; Git retains the implementation history.

## Documentation rule

Keep this directory small and current. Add durable guidance only when it helps future contributors make architectural, development, catalog, or operational decisions. Put task plans in the task or issue, and rely on tests and version control for implementation history.
