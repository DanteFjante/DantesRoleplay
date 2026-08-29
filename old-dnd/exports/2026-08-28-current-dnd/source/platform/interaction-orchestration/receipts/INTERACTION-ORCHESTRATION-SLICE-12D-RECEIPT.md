# Interaction orchestration Slice 12D receipt — append-only interaction evidence

Status: **accepted**  
Completed: 2026-08-24  
Accepted implementation: [Slice 12D](../INTERACTION-ORCHESTRATION-SLICE-12D-IMPLEMENTATION.md)

## Delivered

- Added authoritative main-SQLite resolution, execution, and ordered execution-step receipt tables
  with opaque IDs, exact application/principal/state-space provenance, unique replay identities,
  immutable parent/operation links, and a non-destructive downgrade guard.
- Added bounded append/replay/conflict contracts and one store transaction for an execution receipt
  plus its steps. Equal replays return the original receipt; divergent reuse writes nothing.
- Added authorized redacted receipt reads. The store evaluates a fresh `ReadReceipt` request through
  the configured generic policy and compares the resulting principal/application/state-space to the
  stored row. Default composition denies all receipt reads until a host policy is configured.
- Stored only fingerprints, opaque references, closed outcomes, bounded safe summaries/evidence,
  and operation links. Raw intent/query text, conversation messages, prompts, traces, paths,
  projections, result JSON, effects, and copied catalog bodies are absent.
- Classified all three tables and fields as runtime-only evidence outside catalog import/export.
  Added no planner/model call, action execution, recipe, catalog artifact, public protocol kind,
  web route/component, game rule, or live-database mutation.

## Review findings closed

- Replaced acceptance of caller-supplied authorization decisions with store-owned fresh policy
  evaluation, including exact returned-decision/request scope comparison.
- Added fail-closed default authorization composition so the receipt store is resolvable but cannot
  disclose evidence before an application host installs a policy.
- Strengthened SQLite check constraints for application revisions, identifiers, safe summary and
  evidence sizes, application/state/session fields, replay keys, and step/operation references.
  A focused test bypasses domain constructors and proves SQLite rejects an oversized row.

## Evidence

- Focused receipt, authorization, redaction, replay, rollback, database-bound, migration-drift, and
  catalog-coverage checks: 15 passed, 0 failed.
- Full shared suite: 739 passed, 0 failed.
- Standalone local-AI suite: 20 passed, 0 failed.
- Isolated-output solution build: passed with 0 warnings and 0 errors.
- `git diff --check`: passed; only existing CRLF conversion notices were emitted.
- Catalog validation and protocol walk were not required because no catalog or public protocol
  artifact changed. No normal live database was opened or migrated.

## Deliberate exclusions and next gate

This receipt accepts receipt contracts and persistence only. The execution-shaped record remains
inert and is not authored by an executor. Slice 12E next owns the bounded planner loop, common
local/remote proposal verifier, and closed inner/outer provider profiles; it must add no action
execution or public protocol surface.
