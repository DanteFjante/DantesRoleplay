# Web control center Slice 15 receipt — Codex Luna host model selection

Status: **accepted**  
Date: **2026-08-24**  
Implementation: [Slice 15](WEB-CONTROL-CENTER-SLICE-15-IMPLEMENTATION.md)

## Delivered boundary

- Set `Codex:Model` to exact `gpt-5.6-luna` in the development host configuration.
- The bridge now sends that model only with new `thread/start` requests; `thread/resume` omits it so existing external threads preserve their own model.
- Added the model to the bounded existing Codex status response and to the assistant panel's ready state.
- Published the reviewed control-center bundle as active revision 4 after exporting live revision 3. The browser has no model request field or picker.

## Compatibility and live evidence

- Official OpenAI documentation identifies `gpt-5.6-luna` as the Luna model ID and describes it as the cost-efficient GPT-5.6 tier.
- A direct installed-CLI app-server smoke initialized JSONL and started an **ephemeral, no-turn** thread with Luna. The response returned `model: "gpt-5.6-luna"` and `modelProvider: "openai"`.
- After rebuilding and restarting the known loopback host, `/api/control/assistants/codex/status` returned ready with `model: "gpt-5.6-luna"`, bridge pin `0.149.1`, read-only approval-gated sandbox, and network disabled.
- Live control-center revision 4 and its public `/ui` route both show the selected model in the assistant panel.

## Verification

- Focused `CodexBridgeTests` and `WebInterfaceTests` — 84 passed.
- Standard MCP host build — passed before restart.
- `git diff --check` passed; existing unrelated worktree changes remain preserved.

## Deliberate exclusions

No model turn was sent as verification. This does not guarantee Luna availability for every account; an unavailable entitlement follows the existing bounded provider error path. There is no browser-selected arbitrary model, per-message override, existing-thread migration, approval/sandbox/network change, migration, route, or external exposure.
