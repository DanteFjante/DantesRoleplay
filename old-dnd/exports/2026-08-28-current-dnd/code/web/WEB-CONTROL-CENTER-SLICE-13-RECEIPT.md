# Web control center Slice 13 receipt — Codex CLI compatibility refresh

Status: **accepted**  
Date: **2026-08-24**  
Implementation: [Slice 13](WEB-CONTROL-CENTER-SLICE-13-IMPLEMENTATION.md)

## Delivered boundary

- Configured this development host to use the accessible standalone executable at `C:\Users\dante\AppData\Local\Programs\OpenAI\Codex\bin\codex.exe`.
- Added the host-owned `Codex:PinnedVersion` configuration and updated the bridge's reviewed exact pin to `0.149.1`.
- Updated the narrow checked-in app-server capability descriptor and MCP operator documentation.
- Kept the prior safety boundary unchanged: fixed repository root, read-only sandbox, network disabled, explicit turn-scoped approvals, and no browser input for executable path or version.

## Compatibility evidence

- The standalone CLI reports `codex-cli 0.149.1`.
- `codex app-server --help` confirms `--stdio` / `stdio://` support.
- `codex app-server generate-json-schema` produced the version-specific v1/v2 schemas. Review confirmed the bridge's used initialization, thread/turn, notification, and approval methods remain represented; the repository intentionally retains a concise reviewed capability descriptor rather than vendoring the CLI's generated schema bundle.
- An initialization-only JSONL smoke sent `initialize` and `initialized` to the configured app-server and received the expected response. It did not start a thread or turn, invoke a model, create a conversation, or request approval.
- After rebuilding and restarting the known loopback host, `/api/control/assistants/codex/status` reported `ready: true` with matching observed and pinned `0.149.1` versions. `/`, home, and control-center routes each returned HTTP 200.

## Verification

- `dotnet build DantesRoleplay.MCPServer/DantesRoleplay.MCPServer.csproj --no-restore` — passed.
- Focused `CodexBridgeTests` — 16 passed.
- Focused `WebInterfaceTests` — 67 passed.
- Full `dotnet test DantesRoleplay.slnx --no-restore` ran and exposed one pre-existing unrelated failure: `MigrationDriftTests.No_migration_needs_an_operation_that_cannot_run_in_a_transaction`, caused by `ApplicationSchemaProfileV2` issuing `PRAGMA foreign_keys = 0` outside a transaction.
- The complete remaining suite, excluding only that named unrelated failure, passed: 684 main tests and 20 local-AI tests.
- `git diff --check` passed; the existing dirty worktree and unrelated migration work were preserved.

## Deliberate exclusions

No database schema, page revision, setting override, MCP route, remote/Tailscale exposure, actual Codex model turn, approval decision, provider capability, or policy authority changed. A future Codex CLI version requires another reviewed compatibility slice.
