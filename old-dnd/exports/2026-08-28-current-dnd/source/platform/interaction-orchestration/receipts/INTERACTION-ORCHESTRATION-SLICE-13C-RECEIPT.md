# Interaction orchestration Slice 13C receipt — query contracts and typed result references

Status: **accepted 2026-08-25**

## Delivered boundary

- Registered application sources can author strict `queries/**/*.json` contracts. Catalog search
  exposes their descriptions and exact projection contracts while deterministic directory overlay
  selection remains owned by the application kernel.
- The closed `projection` query executor can perform only the existing read-only structural
  projection operation. Query verification pins the current application, projection version,
  content/schema hashes, role set, catalog fingerprint, and exposure policy.
- Proposal steps support at most 32 immutable structural result bindings from successful earlier
  query dependencies. Bindings can fill a declared role with a JSON string or copy a typed value
  into an absent object-input location; they cannot evaluate expressions, coerce values, read
  action output, overwrite static values, or bind from future/non-dependent steps.
- `model-visible` projections are returned and persisted in full as their authored safe boundary.
  `binding-only` raw output exists only for the current execution and is neither persisted nor
  exposed. Both modes retain deterministic result, schema, and source-revision fingerprints.
- Query-only and ordered query-to-action plans run through the existing interaction execution and
  consent surfaces. Equal replay returns the persisted safe receipt without rerunning a query or
  action, and conflicting evidence fails closed.
- Migration `20260825095926_InteractionQueryReceipts` adds the per-query-step receipt table and its
  constraints. No MCP tool, route, authorization capability, arbitrary data access, or game rule
  was added.

## Evidence

- Focused query/planning/execution/projection/application-catalog suite: **36 passed**.
- Additional focused integration and persistence runs during implementation: **54 passed**, then
  **38 passed**, with no failures.
- `dotnet build DantesRoleplay.slnx --no-restore`: **0 warnings, 0 errors**.
- `dotnet test DantesRoleplay.slnx --no-restore`: **824 shared tests and 20 local-AI tests passed**.
- Catalog validation: **144 records valid** (14 mechanics, 50 procedures, 33 components,
  10 event types, 2 subscriptions, and 35 entities); the existing 21 advisory warnings remained,
  and no live data was touched.
- EF migration drift check: no model changes remain after the new migration.
- Protocol walk: **6 passed, 2 intentionally skipped, 0 failed**.
- `git diff --check`: passed with line-ending notices only.
- Ruleset-neutral production scan found no D&D, caravan, attack, or `game.core` vocabulary in the
  added query/binding execution boundary.

## Deliberate stop

Slice 13C adds no arbitrary SQL/file/network query, JavaScript query implementation, field-level
redaction, action-result binding, model transformation expression, automatic consent, bounded task
agenda, runtime work batch, or recipe generalization/promotion. Those later orchestration concerns
remain assigned to Slices 13D–13F. Slice 13D has not begun.

The user confirmed the complete Slice 13C semantic/public contract and completed-feature acceptance
on 2026-08-25.
