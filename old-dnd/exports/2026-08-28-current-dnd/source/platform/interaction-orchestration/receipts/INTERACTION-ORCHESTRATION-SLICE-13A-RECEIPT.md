# Interaction orchestration Slice 13A receipt — explicit local outer provider

Status: **accepted 2026-08-25**

## Delivered boundary

- Added a dedicated local Ollama outer adapter using only the already-confirmed outer-turn and
  narration task classes, fixed prompts, and strict JSON schemas.
- Added host-only `local`/`remote` provider selection. The selected adapter is the sole adapter
  called; there is no automatic or network fallback.
- Added separate `InteractionOuter:Local` startup configuration for the local outer model/profile
  and completion bounds. Its task allowlist contains only the two outer tasks and its endpoint is
  validated as loopback-only.
- Updated development startup configuration to select the local `qwen3:8b` outer profile.
- Retained the existing no-tools OpenAI Responses adapter as the remote option and did not add any
  public route, MCP kind, persistence record, migration, authorization, consent, planner, or
  execution behavior.

## Evidence

- Focused outer-provider/configuration tests: **13 passed**.
- `dotnet build DantesRoleplay.slnx --no-restore`: **0 warnings, 0 errors**.
- `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore`: **794 passed**.
- `dotnet test DantesRoleplay.slnx --no-restore`: **794 shared tests and 20 local-AI tests passed**.
- No catalog changed, so catalog validation was not applicable. No MCP kind or tool-registration
  changed, so a protocol walk was not required.

## Deliberate stop

Slice 13B remains unimplemented. In particular, an inner `unknown` still does not cause an outer
fallback/direct traversal, and no task-list, query-binding, or recipe-promotion behavior was added.
