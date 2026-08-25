# Web Interface Feature 2 Slice 5 implementation — host setting definitions and redacted read view

Status: **accepted**  
Owner/roadmap: [Web interface roadmap](WEB-INTERFACE-ROADMAP.md)  
Dependency tree/leaf: [Control-center dependency plan](WEB-CONTROL-CENTER-DEPENDENCY-PLAN.md), safe server settings / setting definitions and redacted read view  
Ruleset alignment: **ruleset-neutral**  
Source ID and locator: **not applicable**  
Outcome: Give the authorized operator a bounded, read-only view of the host-owned local-completion setting definitions, resolved startup values, sources, redaction, mutability, disruption, pending/restart state, and exact JSON Schemas.  
Exclusions: setting writes, overrides, persistence, migrations, live refresh, restart triggers, local-model registration or calls, arbitrary configuration enumeration, secret retrieval, database/listen/MCP/Tailscale settings, assistants, Codex, and game/catalog/page changes.  
Allowed files/areas: host composition/configuration and project dependency; local-AI option validation seam; web setting contracts/projection/routes/registration/tests; `<server-settings-panel>` source; web/component/Feature 2 documentation and receipt.  
Stop point: the seven-setting read view is accepted and the settings remain non-editable; stop before Slice 6 overrides or Slice 7 provider/conversation work.

## Confirmed decisions

- The user's **continue** on 2026-08-24, immediately after Slice 5 was identified as blocked on this Sol gate, confirms the new public setting keys, host-provider/web-consumer interface, two read routes, shared local-AI validation seam, and the complete allowlist below.
- `dantes-roleplay-host` owns definitions and resolved configuration. The web project owns only a consumer port and safe HTTP/UI projection; it never enumerates `IConfiguration` itself.
- The existing `Knowledge:Completion` configuration section remains the startup source. No key is renamed and no previously ignored configuration is made effective.
- The current host does not register `ILocalStructuredCompletionProvider`. The catalog therefore reports runtime state `not-registered`, exposes no `effectiveValue`, and explains that the resolved startup values do not drive an active provider. Registering the provider belongs to Slice 7.
- All seven first-release settings are `public-value`, `restart-required`, and `host-restart` disruption. There is no production secret setting. The generic projection must still redact every value field for any `configured-only` definition supplied by a host implementation.
- `source` is deliberately closed to `default` or `configuration`; provider-specific filenames, environment-variable names, command lines, user-secret keys, and raw configuration paths are never disclosed.
- No pending override exists in this slice, so `pendingValue` is null and `restartRequired` is false for every production item. Slice 6 owns versioned overrides and may change those fields only after its migration/transaction gate.

## Complete first-release allowlist

Order is fixed as listed. The response key is permanent; the startup path is host-private and is not returned.

| Key | Startup path | Default / JSON Schema constraint |
| --- | --- | --- |
| `local-completion.enabled` | `Knowledge:Completion:Enabled` | `false`; boolean |
| `local-completion.endpoint` | `Knowledge:Completion:Endpoint` | `http://localhost:11434/`; absolute loopback HTTP/HTTPS URI, expressed with `format: uri` and `x-loopbackOnly: true` |
| `local-completion.model` | `Knowledge:Completion:Model` | `qwen3:8b`; nonblank string, max 200 |
| `local-completion.profile` | `Knowledge:Completion:Profile` | `standard`; trimmed nonblank string, max 100 |
| `local-completion.max-output-tokens` | `Knowledge:Completion:MaxOutputTokens` | `1024`; integer 64–8192 |
| `local-completion.timeout-seconds` | `Knowledge:Completion:Timeout` | `90`; integer projection of a positive `TimeSpan`, max 600 seconds |
| `local-completion.max-concurrent-requests` | `Knowledge:Completion:MaxConcurrentRequests` | `1`; integer 1–8 |

`OllamaCompletionOptions.ValidateProviderSettings()` is the confirmed shared validation seam for
these fields and the existing prompt/response/readiness/keep-alive bounds. Existing `Validate()`
calls it before validating task-class registration, preserving full provider validation.

## Prerequisite evidence

- [Slice 0 receipt](WEB-CONTROL-CENTER-SLICE-0-RECEIPT.md) verifies the `control.read` route boundary and the reserved `control.settings.write` capability without requiring this read slice to use the write capability.
- `DantesRoleplay.MCPServer/ServerConfiguration.cs`, `Program.cs`, and the application manifest own host composition, configuration, and process lifetime.
- `OllamaCompletionOptions` owns defaults and validation bounds. The current MCP host intentionally has no local-AI project reference or provider registration before this slice.
- The Slice 1 shell already reserves `<server-settings-panel>` with independent loading/error/retry behavior.

## Runtime artifacts

- New web consumer contract in `DantesRoleplay.Web.Settings`:
  - `IHostSettingDefinitionProvider.GetCatalog()`;
  - immutable `HostSettingCatalog`, `HostSettingRuntime`, and `HostSettingDefinition` records;
  - closed `HostSettingSensitivity`, `HostSettingMutability`, and `HostSettingDisruption` enums.
- New host implementation `ConfiguredHostSettingDefinitionProvider`, constructed from `IConfiguration`, validates and materializes exactly the seven definitions above and is registered explicitly by `Program.cs` before web registration.
- The MCP host adds a project reference to `DantesRoleplay.LocalAI` only to reuse option defaults and validation. It does not register or call a model and does not change MCP tools/routes.
- New web-only `ControlSettingsExplorer` projects a maximum of 32 unique definitions, preserves host order, redacts configured-only values, and returns:
  - list document `{ state, message, items }`;
  - summary `{ key, displayName, description, sensitivity, mutability, disruption, source, configured, runtimeState, value, effectiveValue, pendingValue, restartRequired }`;
  - exact detail `{ summary, schema }`.
- Confirmed GET-only routes:
  - `GET /api/control/settings`
  - `GET /api/control/settings/{key}`
- A fallback provider registered with `TryAdd` reports `unavailable` and zero definitions for web-only composition tests; the production host always supplies the configured provider.

## Authoritative state and closed input

The host derives keys, labels, descriptions, schema, defaults, startup paths, source, resolved JSON
values, sensitivity, mutability, disruption, runtime state, effective/pending values, and restart
state. The caller supplies no setting value, configuration path, source, runtime claim, secret flag,
or restart claim. Exact detail accepts only one route-safe registered key.

## Behavior, result, and typed effects

- The host reads only the seven named startup paths. A missing path uses the corresponding
  `OllamaCompletionOptions` default and source `default`; a present path uses source
  `configuration`, even when its parsed value equals the default.
- Invalid boolean, URI, integer, `TimeSpan`, or provider-setting bound throws during host provider
  construction. The host cannot silently publish invalid configuration as effective.
- Production items expose their bounded resolved value in `value`; `effectiveValue` and
  `pendingValue` are null while runtime state is `not-registered`. A configured-only item exposes
  none of those values and only the `configured` boolean.
- List order is the fixed allowlist order. Exact detail returns the same summary plus its immutable
  JSON Schema. Unknown keys are not synthesized from configuration.
- These are read-only configuration projections. No database transaction, typed effect, operation,
  event, provider call, restart, file write, or configuration mutation occurs.

## Failure, replay, and rollback contract

- Invalid route keys return stable 400; unknown registered-format keys return 404; provider catalogs
  over 32 entries, duplicate keys, invalid enum/state/source data, or an invalid definition fail
  closed as host/configuration errors rather than returning a partial list.
- Wrong identity is rejected by the existing control filter before projection invocation. GET
  responses use `Cache-Control: no-store`.
- Repeated reads return the immutable startup snapshot and make no change. Configuration changes
  require process restart because the provider is constructed once; this slice has no refresh path.
- There is no rollback transition because there is no write. Removing this projection leaves host
  configuration and runtime behavior unchanged.

## Implementation sequence

1. Add the shared local-AI provider-setting validation seam and host-owned immutable definition provider with focused allowlist/default/configuration/invalid-value tests.
2. Add the web consumer contract, redacting bounded projection, two GET routes, registration fallback, and route/projection tests.
3. Replace only `<server-settings-panel>` with a read-only definition/detail view and explicit not-registered/restart guidance.
4. Run focused tests, clean isolated solution build/full suite if the live host locks normal output, catalog validation for component metadata, browser walk, and `git diff --check`; record the receipt and stop.

## Acceptance matrix

| Case | Evidence |
| --- | --- |
| Exact seven keys/order/defaults/schemas | host provider tests |
| Configured value equal to default still reports `configuration` | host provider tests |
| Invalid parse/bound/loopback setting fails startup construction | host/local-AI tests |
| Provider is not registered and no value is called effective | host/projection tests |
| Configured-only values redact resolved/effective/pending content | projection fake-provider test |
| Unknown/invalid key and over-limit/duplicate provider catalogs | projection/endpoint tests |
| GET-only no-store routes and wrong identity | route/control-filter tests |
| Read-only disabled UI with exact schema detail | source/browser tests |
| No migration/write/model/MCP surface change | diff/build/full-suite evidence |

## Verification commands

- `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --filter "FullyQualifiedName~WebInterfaceTests|FullyQualifiedName~HostSetting"`
- `dotnet test src/system/local-ai/DantesRoleplay.LocalAI.Tests/DantesRoleplay.LocalAI.Tests.csproj`
- `dotnet build DantesRoleplay.slnx`
- `dotnet test DantesRoleplay.slnx --no-build`
- `roleplay validate catalog` after component metadata changes
- `git diff --check`

Run the included public `SystemCatalogProtocolTests` walk because host dependency registration
changes, even though the MCP endpoint and tool surface remain unchanged.

## Completion receipt and exit gate

Record evidence in `web/WEB-CONTROL-CENTER-SLICE-5-RECEIPT.md`, update the Feature 2 status once,
and stop before overrides, migration, restart/apply behavior, local-model registration or calls,
conversation persistence, assistants, Codex, or any arbitrary configuration reader.
