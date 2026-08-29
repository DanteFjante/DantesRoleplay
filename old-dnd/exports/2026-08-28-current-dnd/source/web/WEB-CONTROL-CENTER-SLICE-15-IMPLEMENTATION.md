# Web control center Slice 15 implementation — Codex Luna host model selection

Status: **accepted**  
Owner/roadmap: [Web interface roadmap](WEB-INTERFACE-ROADMAP.md)  
Dependency tree/leaf: [control-center dependency plan, Slice 15](WEB-CONTROL-CENTER-DEPENDENCY-PLAN.md#slice-15-packet--codex-luna-host-model-selection)  
Ruleset alignment: **ruleset-neutral**  
Source ID and locator: **not applicable**  
Outcome: Configure `gpt-5.6-luna` as the model for new locally bridged Codex threads and make that selected model visible in existing read-only status.  
Exclusions: Arbitrary browser model selection, per-message override, switching existing threads, entitlement guarantees, model turns, provider changes, relaxed approvals/sandbox/network policy, migrations, and new routes.  
Allowed files/areas: `DantesRoleplay.MCPServer` Codex configuration/docs, `src/system/codex-bridge`, assistant-panel source/tests, and Feature 2 plan/roadmap/receipt.  
Stop point: New threads receive the exact Luna model field, status/panel show it, and the active local host is restarted with the reviewed configuration.

## Confirmed decisions

- The user requested Luna for Codex.
- Official OpenAI documentation identifies the exact model ID as `gpt-5.6-luna`; direct local
  app-server verification accepted it on an ephemeral no-turn thread and returned model provider `openai`.
- Model selection remains a closed host setting, not a caller-controlled request field. It applies
  only when creating a new external Codex thread; resumed threads retain their original model.

## Prerequisite evidence

- [Slice 13 receipt](WEB-CONTROL-CENTER-SLICE-13-RECEIPT.md): exact compatible CLI and read-only/no-network app-server bridge.
- Existing bridge `BuildThreadParameters` owns new/resumed thread configuration.
- Existing assistant status endpoint already exposes a bounded bridge status projection.

## Authoritative state and behavior

`CodexBridgeOptions` and development host configuration own the exact model. The browser sends no
model value. New `thread/start` parameters include `model: "gpt-5.6-luna"` with the existing
repository, approval, and read-only sandbox fields. `thread/resume` omits model to preserve thread
continuity. Status includes the configured host model, and the UI displays it without offering an
editor. No model is invoked by the verification smoke.

## Failure and compatibility contract

If the Codex account does not permit Luna, a later actual turn follows the existing bounded
app-server/provider failure path; the UI does not claim account entitlement. Missing, malformed, or
unrecognized browser model input is impossible because no request field exists. Existing thread
model history remains unchanged. All safety policy and version-pinning failures retain their
existing behavior.

## Acceptance and verification

- Focused protocol tests verify Luna on new threads, absence on resumes/turn requests, and unchanged policy.
- Focused web tests verify bounded model status/panel presentation.
- Direct installed-CLI no-turn ephemeral thread confirms Luna acceptance; restart host and read status.
- Run focused bridge/web tests, isolated MCP build, `git diff --check`, then receipt.

## Completion receipt and exit gate

Write `WEB-CONTROL-CENTER-SLICE-15-RECEIPT.md`, mark plan/roadmap complete, and stop. Later selectable models or dynamic runtime settings require a separate confirmed slice.
