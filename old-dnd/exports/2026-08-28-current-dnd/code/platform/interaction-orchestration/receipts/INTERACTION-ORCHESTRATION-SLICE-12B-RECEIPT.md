# Interaction orchestration Slice 12B receipt — authority and contract foundation

Status: **accepted**  
Completed: 2026-08-24  
Accepted implementation: [Slice 12B](../INTERACTION-ORCHESTRATION-SLICE-12B-IMPLEMENTATION.md)

## Delivered

- Ratified the ruleset-neutral `interaction-orchestration` owner and a one-way dependency on the
  existing application registry and trusted-principal authorization contracts. Local AI remains a
  separate zero-dependency component and gained no project or orchestration reference.
- Added strict bounded caller-intent parsing. Unknown or host-owned fields—including application,
  principal, revisions, role/model settings, effects, execution, and learning—fail closed.
- Added immutable host context and generic plan/read-receipt/execute authorization contracts. The
  plan envelope binds exact application/state-space/session/principal/role/delegation/revision/
  budget/evidence values supplied by the host, not by a browser or model.
- Added the closed inner Luna Low and outer Luna High profiles, resume-compatibility checks, and a
  product-provider isolation attestation that requires filesystem, shell, network, arbitrary MCP,
  approval, and direct-execution authority all to be absent. An insufficient provider produces
  normal `unavailable` evidence.
- Added canonical bounded JSON and domain-separated uppercase SHA-256 fingerprints. Equivalent
  object/dictionary order and whitespace produce identical envelope/proposal identity while array
  order remains explicit.
- Added exact `system.*` versus opaque application-qualified contract references and inert ordered
  query/action proposal DAGs. Cross-application, stale, duplicate, self, forward/cyclic, excessive,
  malformed, and over-budget proposals reject without effects or state access.
- Added all eight closed resolution statuses, bounded safe non-resolution evidence, replay/conflict
  classification, and an execution-consent reference that binds the exact resolution receipt,
  proposal fingerprint, opaque principal, application, state space, and idempotency key.
- Added no persistence, migration, catalog artifact, model call, provider adapter, action execution,
  protocol kind, web route/component, recipe, application adapter, or live-database change.

## Evidence

- Focused interaction and component/architecture guard checks: 33 passed, 0 failed.
- Full shared suite: 722 passed, 0 failed.
- Standalone local-AI suite: 20 passed, 0 failed.
- Isolated-output solution build: passed with 0 warnings and 0 errors.
- `git diff --check`: passed. Catalog validation and protocol walk were not required because no
  catalog or public protocol artifact changed.

## Deliberate exclusions and next gate

This receipt accepts only the threat model and pure internal contracts. It does not claim that a
provider is configured, a proposal has been semantically verified, an action can execute, a receipt
or recipe is persisted, or an interaction is publicly callable. Slice 12C is next and must author
one active retrieval slice for effective trusted feature documents plus exact/lexical and optional
vector/hybrid search. It may not start planner model calls, receipt persistence, execution, public
protocol, or learning.
