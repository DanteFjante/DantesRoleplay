# Web Interface Feature 2 Slice 5 receipt — host setting definitions and redacted read view

Status: **accepted**  
Accepted boundary: [Slice 5 implementation document](WEB-CONTROL-CENTER-SLICE-5-IMPLEMENTATION.md)  
Recorded: **2026-08-24**

## Delivered boundary

- Added the host-owned `ConfiguredHostSettingDefinitionProvider` with exactly seven permanent
  local-completion keys: enabled, loopback endpoint, model, profile, maximum output tokens, timeout
  seconds, and maximum concurrency. It materializes one immutable startup snapshot from only the
  named `Knowledge:Completion` paths and distinguishes exact `default` versus `configuration`
  sources.
- Split `OllamaCompletionOptions` validation so provider-wide settings can be checked independently
  from the task allowlist while existing full `Validate()` behavior remains intact. Invalid parses,
  remote endpoints, bounds, and fractional timeout projections fail host provider construction.
- Added a bounded web consumer port and `ControlSettingsExplorer`. It accepts no caller-supplied
  values or paths, rejects invalid/duplicate/oversized catalogs, redacts all value fields for a
  configured-only definition, and exposes stable list/detail documents with no-store caching.
- Added GET-only `/api/control/settings` and `/api/control/settings/{key}` routes under the existing
  `control.read` boundary. Unknown keys return 404 and invalid route identifiers return a stable 400.
- Replaced `<server-settings-panel>` with a read-only list and exact JSON Schema detail. Controls are
  disabled; the panel explains that overrides/restarts are later work and truthfully reports
  `not-registered` because the current host still has no active local-completion provider.

No override, database table, migration, setting write, live refresh, restart trigger, provider
registration/call, conversation, assistant, Codex bridge, MCP route/tool change, arbitrary
configuration enumeration, secret retrieval, or game/catalog/page write was added.

## Verification evidence

- Focused web/host-setting tests: **60 passed**, 0 failed. They cover exact key order, defaults and
  configured sources, schemas, inactive runtime truth, shared bounds, invalid startup values,
  generic configured-only redaction, invalid/duplicate catalogs, route metadata, disabled UI, and
  the existing web regressions.
- Local-AI tests: **19 passed**, 0 failed, including the unchanged full option validation behavior.
- Solution build: **passed**, 0 warnings and 0 errors.
- The first full solution run passed **19/19** local-AI tests and **629/629** shared tests. After a
  concurrent MCP legacy-source test was added, the final run passed **19/19** and **629/630**;
  `SystemCatalogMcpWalkTests.Dnd2024_legacy_sources_register_preview_and_activate_without_claiming_system_or_fixture_files`
  throws while eagerly evaluating `dryRun.Error.GetRawText()` for an otherwise successful result at
  its line 638. That untracked test and legacy-source registration are outside Slice 5; the final
  60-test settings/web selection and clean solution build pass on the same current tree.
- Public system-catalog protocol walk: **2 passed**, 0 failed; the added host dependency leaves the
  three-verb MCP surface and callable cursor recovery unchanged.
- Catalog validation: **passed**, 144 records validated; 17 existing near-duplicate warnings and no
  live-data change.
- Disposable local HTTP/browser walk:
  - uploaded the source `control-center` page to a disposable database;
  - observed all seven settings with their configuration/default sources and restart-required
    labels while the panel reported `not-registered`;
  - opened maximum output tokens and observed a disabled value field plus the exact 64–8192 JSON
    Schema and no pending restart;
  - verified the disabled control is not enabled and the browser logged no errors; and
  - closed the test tab and stopped the disposable host.
- `git diff --check`: no whitespace errors; only working-copy line-ending warnings were reported.

The user's already-running MCP server continued to hold the ordinary build output. Final build and
test commands therefore used the ignored `.tmp/slice5-artifacts` tree, avoiding interruption while
compiling and testing the same source graph.

## Deliberate exclusions and next gate

Resolved startup values are deliberately not called effective while the local provider is absent.
Production contains no configured-only/secret definition, but the generic projection's redaction is
tested for future host-owned definitions. Slice 6 is the next ordered leaf and remains blocked on a
Sol-owned versioned override schema/migration, audit transaction, concurrency, apply-versus-stage,
restart, failure, and recovery contract. Slice 7 separately owns provider registration and
conversation/local-LLM behavior.
