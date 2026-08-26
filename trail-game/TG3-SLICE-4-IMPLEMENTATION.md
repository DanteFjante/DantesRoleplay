# Trail Game TG3 Slice 4 implementation — stable headless replay and final acceptance

Status: **accepted 2026-08-25**; [receipt](TG3-SLICE-4-RECEIPT.md)
Owner/roadmap: [Customizable trail-survival roadmap](TRAIL-GAME-ROADMAP.md)
Dependency tree/leaf: [TG3 simulation / TG3.4](TG3-SIMULATION-DEPENDENCY-PLAN.md)
Ruleset alignment: **ruleset-neutral**
Source ID and locator: **not applicable; original Trail Survival rules**
Outcome: Close TG3 with exact generic audit metadata, byte-stable known-seed runs, rollback/replay
evidence, catalog validation, and full compatibility acceptance.
Exclusions: Authored starter scenario, browser/public/MCP seam, migration, startup, or live state.
Allowed files/areas: generic application-execution/ECS-effect audit contracts and tests, Trail
tests/documents, and no game-specific C#.
Stop point: Record final TG3 receipt and make TG4 next; do not begin TG4 content.

## Confirmed decisions

The user's request to implement TG3 plus the [simulation confirmation](TG3-SIMULATION-CONFIRMATION.md)
authorizes the generic cross-owner audit enrichment required by TG3's pre-existing exit gate.
Tests must prove it remains ruleset-neutral and compatible with ordinary ECS effect batches.

## External implementation reference

No external implementation applies.

## Prerequisite evidence

[TG3.1](TG3-SLICE-1-RECEIPT.md), [TG3.2](TG3-SLICE-2-RECEIPT.md), and
[TG3.3](TG3-SLICE-3-RECEIPT.md) prove the complete simulation behavior through the real activated
runner. Existing operation rows already own generic mechanic/version/seed/projection columns.

## Runtime artifacts

- Optional ruleset-neutral mechanic audit fields on `ApplicationEcsEffectBatch`.
- Application runner population and effect-applier operation recording of those fields.
- Final headless deterministic/audit tests and TG3 receipt.
- No schema migration or public contract.

## Authoritative state and closed input

The runner serializes the already-frozen evaluated projection and exact inspected mechanic metadata;
no caller gains a new input. The existing execution request fingerprint remains replay authority.

## Behavior, result, and typed effects

The same atomic operation that records effect success also records mechanic ID/version, seed, and
projection. Two disposable state spaces execute the same known-seed setup/event/victory sequence
and compare canonical entity/component/containment JSON byte-for-byte.

## Failure, replay, and rollback contract

Partial/malformed audit metadata rejects the batch before mutation. Failed mechanics still produce
no effect operation. Effect failures record failure after rollback. Exact replay adds no operation
or state change and conflicting request fingerprints reject.

## Implementation sequence

1. Add bounded optional generic audit metadata and pass it from the runner.
2. Record it in the existing root operation without changing transaction ownership.
3. Add deterministic canonical snapshot and exact audit assertions.
4. Run focused tests, catalog validation, full shared/local-AI suites, build and audit checks.

## Acceptance matrix

Exact audit, ordinary batch compatibility, malformed metadata/no-change, byte-stable known seed,
divergent seed evidence, replay conflict, injected rollback, terminal blocking, catalog revision,
cross-application isolation, catalog validation, full suite, build, links, whitespace, and diff.

## Verification commands

Focused application-execution/ECS-effect/Trail tests, disposable catalog validation, full shared
and local-AI suites, warning-free isolated solution build, link/whitespace/diff audit. No protocol
walk because no public/MCP surface or dependency registration changes.

## Completion receipt and exit gate

Record `TG3-SLICE-4-RECEIPT.md`, collapse the TG3 plan, mark roadmap TG3 accepted, and stop with TG4
as the next inactive boundary.
