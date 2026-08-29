# Application kernel Slice 11J receipt — complete legacy state adoption and execution parity

Status: **accepted**  
Completed: 2026-08-24  
Accepted implementation: [Slice 11J](../APPLICATION-KERNEL-SLICE-11J-IMPLEMENTATION.md)

## Delivered

- Added application-scoped containment and qualified directed relationships with optimistic
  revisions, state-space isolation, deterministic reads, and generic ECS edge effects.
- Added exact, bounded, dry-run-gated adoption of the complete legacy entity/component/containment/
  relationship graph into one active application state space. The copy is atomic, replayable, and
  preserves legacy values while leaving every legacy row unchanged.
- Added the authenticated `system.state-space.adopt-legacy` commit kind through the existing
  three-verb protocol. It neither runs automatically nor grants remote MCP access.
- Added the read-only `application-execution` component. It selects one exact active catalog
  mechanic and fingerprint, maps declared legacy component/relationship names to exact
  application contracts, projects application ECS state, and invokes the existing bounded Jint
  sandbox without applying proposed effects.
- Proved byte-identical legacy/application projection for declared components, containment,
  relationships, and component references, then selected and invoked all 14 ratified mechanics
  with identical source, seed, and sandbox results.
- Added no game rule, formula, eligibility decision, or application-specific identifier to generic
  C#. Dynamic `<application>.*` write execution remains a downstream orchestration concern.

## Evidence

- Focused Slice 11 protocol, authorization, guard, edge, effect, adoption, and application
  execution checks: 51 passed, 0 failed. The application-execution subset is 2 passed, 0 failed.
- Full shared suite: 702 passed, 0 failed, including fresh migration and EF model-drift coverage.
- Standalone local-AI suite: 20 passed, 0 failed.
- Catalog validation: 144 records valid (14 mechanics, 50 procedures, 33 components, 10 event
  types, 2 subscriptions, and 35 entities); 21 existing near-duplicate warnings, 0 errors.
- Isolated-output solution build: 0 warnings, 0 errors.
- `git diff --check`: passed; only line-ending conversion notices were emitted.
- The normal live database was not migrated or mutated by this acceptance run.

## Deliberate exclusions and next gate

The evaluator is deliberately read-only: it does not publish a dynamic application command or
apply mechanic effects. Those public plan/execute, authorization, receipt, replay, and learning
semantics belong to interaction orchestration. The application kernel's Slice 12A read handoff is
already accepted; the next implementation gate is interaction-orchestration Slice 0 threat-model
and contract confirmation, followed by its own model-routed slices.
