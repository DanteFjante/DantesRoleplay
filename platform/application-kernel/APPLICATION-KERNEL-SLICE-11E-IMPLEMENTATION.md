# Application kernel Slice 11E implementation — bounded string constraints and final contract registration

Status: **accepted**  
Owner/roadmap: [Platform enabling features roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)  
Dependency tree/leaf: [Application-kernel I / application-owned component contracts](APPLICATION-KERNEL-DEPENDENCY-PLAN.md)  
Ruleset alignment: **ruleset-neutral schema profile plus dnd2024-compatible contract migration**  
Source ID and locator: **not applicable** — no D&D rule is implemented.  
Outcome: Add a versioned, resource-bounded schema profile for the existing `pattern` and
`date-time` constraints, preserve v1 registrations and replay, and prove all 32 legacy
`game.core.*` sidecars register on a fresh disposable host.  
Exclusions: Arbitrary regular expressions or formats; `patternProperties`; changing the two
catalog contracts' validation meaning; component values/backfill; state-space migration; default-host registration;
`stats`; projections/aliases/mechanics; remote MCP; vectors; and AI orchestration.  
Allowed files/areas: The two deferred sidecars only to remove their top-level non-validating
`title` annotations; schema-validation contracts/validator/tests; component registry and ECS or
projection validation call sites only as required for exact profile selection; component-type and
fresh-host tests; the component-profile database constraint and one additive migration/snapshot;
this document/receipt and concise status links.  
Stop point: Stop after v1 compatibility and replay remain exact, the two deferred schemas validate
representative strings and timestamps, all 32 contracts register through the existing MCP surface,
and `dnd2024.stats` plus all state remain absent.

## Confirmed decisions

- The user continued after Slice 11D identified bounded regex/date-time support as the next gate.
- `system-json-schema-2020-12/v1` remains immutable. Schemas using only its original keywords retain
  the v1 profile and hash. The additive profile is `system-json-schema-2020-12/v2`.
- Profile selection uses the least capable accepted profile: only schemas containing `pattern` or
  `format: date-time` select v2. Callers still cannot choose a profile.
- `pattern` is restricted to anchored, non-branching ASCII expressions made from literals, simple
  character classes, final `+`, and bounded `{n}` quantifiers. Pattern count, length, repetition,
  and any unbounded matching input are capped. This admits the authored contracts without exposing
  arbitrary backtracking regex.
- `format` accepts only `date-time` and is asserted during value evaluation rather than retained as
  an annotation.

## D&D 5e 2024 alignment

| Rule concern | Source meaning | Existing owner | Consequence |
| --- | --- | --- | --- |
| Schema validation | Generic string-shape and timestamp validation. | `schema-validation` | No game vocabulary or rule branch enters C#. |
| Campaign evidence/recap | Existing validation constraints remain authored authority. | Catalog component schemas | Remove only unsupported `title` annotations and register every constraint unchanged. |

## External implementation reference

No Foundry review applies because this is generic JSON Schema validation and contract adoption, not
a D&D rule. JsonSchema.Net remains the existing offline evaluator; its format assertion option is
enabled explicitly, while the kernel performs stricter preflight on accepted pattern syntax.

## Prerequisite evidence

- [Slice 11D receipt](receipts/APPLICATION-KERNEL-SLICE-11D-RECEIPT.md) proves 30 contracts register
  and isolates only checkpoint/recap string constraints.
- [Slice 5 receipt](receipts/APPLICATION-KERNEL-SLICE-5-RECEIPT.md) fixes v1 as an immutable closed
  profile, so this extension requires v2 rather than silently changing v1.

## Runtime artifacts

- Add v2 profile identity and deterministic least-profile selection.
- Add closed inspection for `pattern` and exact `format: date-time`.
- Evaluate values against their stored profile with asserted format validation.
- Remove only the two top-level, non-validating `title` annotations.
- Change the database profile constraint from v1-only to v1-or-v2 using one forward migration;
  existing rows are not rewritten.
- Add no MCP kind, application ID, component ID, state row, or catalog rewrite.

## Authoritative state and closed input

Catalog sidecars remain authored authority and SQLite remains runtime type authority. The trusted
caller supplies only application, qualified type ID, and schema JSON. The kernel derives the least
profile, normalized schema, hash, and version. ECS and projection evaluation use the immutable
stored profile rather than silently upgrading it.

## Behavior, result, and typed effects

Original-keyword schemas compile as v1 with their existing hash. A schema using an accepted bounded
pattern or asserted date-time compiles as v2 and hashes under v2. Invalid/unanchored/branching,
oversized, excessive, or input-unbounded patterns and every other format reject at compilation.
Date-time values must satisfy the evaluator's Draft 2020-12 assertion. Typed effects: none.
Transaction owners remain the existing component registry and MCP registration service.

## Failure, replay, and rollback contract

Rejected v2 keywords create no schema/type row. Existing v1 operation replays resolve the same
profile/hash/version. A migration failure leaves the prior constraint and rows intact. Invalid
pattern/date-time component values produce no ECS or projection mutation. No automatic downgrade
may delete immutable type history.

## Implementation sequence

1. Add profile-selection, bounded-pattern, asserted-format, and v1 compatibility tests.
2. Implement v2 inspection/evaluation and stored-profile validation.
3. Expand the database constraint with one migration and verify fresh/upgrade/model drift.
4. Update preflight/fresh-host evidence from 30 to 32 registrations and exact value cases.
5. Run focused/full/local-AI/catalog/build/diff checks, record receipt, and stop.

## Acceptance matrix

| Concern | Required evidence |
| --- | --- |
| v1 stability | Original schema normalizes/hashes to the exact prior v1 identity; prior registration replay is unchanged. |
| Pattern | Authored anchored ID/digest patterns accept matches and reject mismatches; unsafe or unbounded forms reject compilation. |
| Date-time | RFC 3339 date-time examples pass; malformed/calendar-invalid or offset-less values fail. |
| Registration | All 32 legacy sidecars register through exact dry-run/commit on a disposable fresh host. |
| Migration | Fresh and pre-v2 databases allow v1/v2 rows without rewriting existing versions; drift passes. |
| Boundary | `stats`, state, rules, projections, aliases, AI, and new protocol kinds remain absent. |

## Verification commands

- Focused schema validation, ECS/component administration, migration/model-drift, and fresh-host MCP tests.
- Catalog validation; full shared/local-AI suites; warning-free solution build; `git diff --check`.

## Completion receipt and exit gate

Record acceptance in `receipts/APPLICATION-KERNEL-SLICE-11E-RECEIPT.md`, mark this document
accepted, update Slice 11 status links, and stop before values/state/default activation or `stats`.
