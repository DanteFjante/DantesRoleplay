# Application kernel Slice 7 remediation — schema-aware projection validation

Status: **accepted**  
Owner/roadmap: [Platform enabling features roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)  
Dependency tree/leaf: [Generic application kernel dependency plan](APPLICATION-KERNEL-DEPENDENCY-PLAN.md), F  
Ruleset alignment: **ruleset-neutral**  
Source ID and locator: **not applicable**  
Outcome: Close the Slice 7 validation findings without changing projection identity, storage, or public surfaces.  
Exclusions: New projection semantics, cache, effects, application activation, legacy integration, protocol, catalog, application content, or AI work.  
Allowed files/areas: `src/system/projection-materialization/{persistence,tests}/`, this document, the existing Slice 7 receipt, and status/link-only plan updates.  
Stop point: Accepted schema constructs resolve safely for structural source paths and the original Slice 7 acceptance matrix has direct regression evidence; stop before Slice 8A remediation.

## Confirmed remediation

The user authorized fixing Slice 7, Slice 8A, and the validation findings on 2026-08-24. This pass:

- replaces the shallow projection schema-path check with a bounded walker over the already accepted
  Slice 5 profile, including local `$ref`, `allOf`, `anyOf`, `oneOf`, `properties`, `items`, and
  `prefixItems` without external resolution or schema evaluation;
- preserves the conservative rule that a source path must be demonstrably available in every
  applicable branch; ambiguous/open schemas do not authorize undeclared reads;
- adds direct registry append/replay, duplicate/unknown/path/bound/role, multi-component/dependency,
  missing-component, output-validation, state-space isolation, and one-batch-read evidence; and
- creates no migration, ID, table, endpoint, or behavior outside the accepted Slice 7 boundary.

## Acceptance

Focused projection tests, existing ECS/schema/migration tests, the full shared suite, local-AI
suite, and `git diff --check` must pass. Record the revalidation in the existing Slice 7 receipt.
