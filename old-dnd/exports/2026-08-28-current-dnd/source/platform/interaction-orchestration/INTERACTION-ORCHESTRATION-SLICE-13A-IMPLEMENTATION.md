# Interaction orchestration Slice 13A implementation — explicit local outer provider

Status: **accepted 2026-08-25**  
Owner/roadmap: [Platform enabling features roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)  
Dependency tree/leaf: [Slice 13A](INTERACTION-ORCHESTRATION-SLICE-13-DEPENDENCY-PLAN.md#dependency-tree)  
Ruleset alignment: **ruleset-neutral**  
Source ID and locator: **not applicable**

## Outcome and boundary

Add an explicit host-selected outer conversation provider. The selected provider is either a
dedicated local Ollama completion profile or the existing no-tools OpenAI Responses outer provider.
The local provider uses the existing immutable outer-turn and narration prompts, schemas, task
classes, and response parsing. The selected provider has no tools, callbacks, execution access, or
automatic provider fallback.

Exclusions: inner-first fallback, direct outer planning changes, query execution, task lists,
recipe promotion, new routes/MCP kinds, persistence/migrations, live database changes, game rules,
and changes to player authorization or execution consent.

Allowed files/areas: interaction-orchestration domain/hosting/tests; the MCP host's startup
configuration/composition and focused tests; this document and its receipt; minimal roadmap and
dependency status links. Stop after the selected provider can make outer-turn and narration calls
through the current interfaces and all non-selected/disabled/error paths fail closed.

## Confirmed decisions

The user confirmed on 2026-08-25:

1. Host mode is exactly `local` or `remote`; no `automatic` mode exists in this slice.
2. The host's selection is startup configuration, never browser/MCP/player/model input. A local
   selection never sends a request to the remote provider; a remote selection is the only path that
   may call OpenAI.
3. Local outer completion has a separate startup profile and closed bounds from the inner planner.
   It is instantiated from `InteractionOuter:Local` rather than the `Knowledge:Completion` inner
   provider settings. Its configured model identity/profile is checked by the local completion
   adapter and is never reported as the fixed remote Luna profile.
4. A disabled, missing, malformed, timed-out, or schema-invalid selected provider returns the
   existing typed unavailable result. It does not fall back to the other provider.
5. Existing permanent task classes `system.interaction.outer-turn` and
   `system.interaction.narration`, and their v1 schemas, are reused. No new public protocol kind,
   database record, setting override key, or model authority is introduced.

## Prerequisite evidence

- Slice 12E accepts `ILocalStructuredCompletionProvider` as the schema-only no-tools local seam
  and `OpenAiInteractionPlanningOptions` as the closed Responses credential boundary.
- Slice 12F accepts the outer-turn/narration contracts, fixed remote outer profile, and permanent
  local task identifiers. Its current `OpenAiResponsesOuterInteractionProvider` is the remote
  adapter reused here.
- The existing `OllamaStructuredCompletionProvider` validates an allowlisted task class, local-only
  endpoint, prompt/output/schema bounds, model identity, and JSON schema before returning output.

## Runtime artifacts and closed configuration

The host reads only these startup configuration values:

- `InteractionOuter:Provider`: `local` or `remote`, default `local`.
- `InteractionOuter:Local:Enabled`, `Endpoint`, `Model`, `Profile`, `MaxPromptCharacters`,
  `MaxResponseCharacters`, `MaxOutputTokens`, `MaxConcurrentRequests`, and `Timeout`.

The local options use the existing `OllamaCompletionOptions` validation and only allow the two
already-confirmed outer task classes. `Endpoint` remains absolute loopback HTTP/HTTPS. The remote
selection reuses the existing `InteractionPlanning:Remote` secret-backed options. Neither provider
accepts model, profile, endpoint, prompt, schema, tools, or selection values from a player turn.

`LocalInteractionOuterProvider` adapts the local structured completion port to
`IInteractionOuterTurnProvider` and `IInteractionNarrationProvider`. It passes only a fixed task
class/prompt/schema and serialized closed request, applies the same exact response parsing as the
remote outer adapter, and maps provider failure to safe local-unavailable codes.

`SelectedInteractionOuterProvider` owns dispatch. It selects one registered adapter at host startup
and forwards each turn/narration only to that adapter. It has no retry/fallback logic.

## Failure and no-change contract

| Condition | Result | Required evidence |
| --- | --- | --- |
| Invalid provider mode/configuration | Host startup fails before serving | No provider request or state change. |
| Local selected but disabled/unavailable/invalid | Existing outer/narration unavailable result with local code | Remote adapter call count remains zero. |
| Remote selected but disabled/unavailable/invalid | Existing unavailable result with remote code | Local adapter call count remains zero. |
| Cancellation | Propagates only caller cancellation; no fallback | No execution/plan/state call. |
| Invalid or oversized model JSON | Existing response-invalid unavailable result | No plan/execution/state call. |

## Implementation sequence

1. Add the local adapter and selected-provider dispatcher, reusing the accepted outer protocol.
2. Add closed host configuration parsing and dedicated local Ollama composition.
3. Replace the host's hardwired remote outer registration with the selected dispatcher.
4. Add focused adapter, dispatcher, configuration, and host-composition tests.
5. Run focused tests, build, then the full suite; write the receipt and update the dependency state.

## Acceptance matrix and verification

- Local outer turn and narration use their fixed task classes/prompts/schemas and parse valid
  closed JSON.
- Local malformed/schema/identity/output failure is unavailable and never reaches remote.
- Local selection never calls remote; remote selection never calls local.
- Bad mode and non-loopback/bad local options are rejected at startup.
- Remote behavior and no-tools request shape remain covered by the existing focused tests.

Run:

```powershell
dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore --filter "FullyQualifiedName~InteractionOuterProviderTests|FullyQualifiedName~InteractionOuterProviderSelectionTests|FullyQualifiedName~ConfiguredInteractionOuterProviderOptionsTests"
dotnet build DantesRoleplay.slnx --no-restore
dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore
```

## Receipt and stop

Completion evidence: [Slice 13A receipt](receipts/INTERACTION-ORCHESTRATION-SLICE-13A-RECEIPT.md).
Stop before Slice 13B.
