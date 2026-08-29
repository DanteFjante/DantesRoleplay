# Interaction orchestration Slice 13B receipt — inner-first typed outer fallback

Status: **accepted 2026-08-25**

## Delivered boundary

- Every actionable application-conversation turn now attempts one local inner resolution first,
  including an initial outer `direct-plan` decision.
- Inner `unknown`, `unsupported`, and `unavailable` results are returned once to the selected outer
  AI as bounded safe status/code/summary/evidence/receipt context. Needs-input, ambiguity, unsafe,
  and stale results stop without fallback.
- Only a second outer `direct-plan` decision starts one outer planning attempt. Repeated delegation,
  ordinary response, or unavailability stops without a loop.
- Inner and outer attempts use distinct `.inner`/`.outer` idempotency keys and the same host-created
  delegation correlation. Both append truthful independent resolution receipts.
- Local outer planning now uses the dedicated Slice 13A outer Ollama profile and task allowlist;
  it does not reuse the inner local model profile. Remote outer planning retains the accepted
  no-tools remote outer role/profile.
- Resolved plans remain inert until the existing separate player confirmation. No automatic
  execution or automatic learning was added.

## Evidence

- Focused application-conversation/planning/outer-provider suite: **42 passed**.
- `dotnet build DantesRoleplay.slnx --no-restore`: **0 warnings, 0 errors**.
- `dotnet test DantesRoleplay.slnx --no-restore`: **806 shared tests and 20 local-AI tests passed**.
- `git diff --check`: passed with working-copy line-ending notices only.
- No catalog changed, so catalog validation was not applicable. No MCP kind/tool registration
  changed, so the protocol walk was not required.

## Deliberate stop

Query steps remain unsupported. Slice 13B adds no query-result binding, task list, runtime work
batch, automatic recipe promotion, migration, durable conversation, game rule, or live-database
change. Those remain owned by Slices 13C–13F.
