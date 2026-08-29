# Interaction orchestration Slice 12G completion receipt

Accepted: **2026-08-24**  
Ruleset alignment: **ruleset-neutral**  
Implementation contract: [Slice 12G implementation](../INTERACTION-ORCHESTRATION-SLICE-12G-IMPLEMENTATION.md)

## Delivered boundary

- Added explicit, opt-in learning after a successful, fully verified interaction execution. Failed,
  partial, unverified, or non-opted-in requests cannot create candidates, and learning failure never
  changes the action receipt.
- Added deterministic, application-scoped recipe identities and append-only SQLite recipe,
  revision, and evidence records. The first template format contains action steps, empty canonical
  inputs, and role-slot names only; entity IDs, prior values, prompts, code, effects, paths, and
  credentials are rejected.
- Added candidate, verified, stale, retired, review-replay, and provenance behavior. Only a private
  audited review can verify or retire a candidate, and every reuse rehydrates current application,
  activation, mechanic, role, and trust authority through the common proposal verifier.
- Added private paginated recipe traversal and review under the existing query and commit verbs.
  The public MCP surface remains exactly three verbs. Stored intent text is available only to the
  private lexical matcher and is absent from projections, model context, and vector documents.
- Added verified recipe resolution to both planning paths. Exact and lexical retrieval always work;
  the optional `TrustedRecipe` vector lane is isolated and contains only current trusted mechanic
  descriptions and role-slot names. Missing, ambiguous, stale, poisoned, or untrusted recipes fail
  closed and cannot mutate application state.
- Added an unchecked web opt-in control. The server retains the exact pending intent only long
  enough to submit an explicitly requested learning attempt.

## Acceptance evidence

- Complete shared suite: **784 passed, 0 failed, 0 skipped**.
- Standalone local-AI suite: **20 passed, 0 failed**.
- Protocol walk with `IncludeProtocolWalkTests=true`: **6 passed, 0 failed, 2 skipped**. The skipped
  rows remain the deliberately retired authored-procedure read/commit paths.
- Catalog validation: **144 valid records**, 21 existing advisory near-duplicate warnings, and
  explicit confirmation that no live data was touched.
- Entity Framework reports no model changes after migration
  `20260824212640_InteractionRecipes`; migration drift and persistence behavior pass in the shared
  suite.
- Isolated-output solution build: **0 warnings, 0 errors**.
- `git diff --check`: passed; only working-copy line-ending notices were emitted.
- Architecture checks and tests cover deterministic/value-free templates, candidate replay,
  terminal status behavior, provenance and operation evidence, current-authority invalidation,
  entity rebinding, safe vector documents, private-text redaction, and learning rejection for the
  unsupported nonempty-input shape.

## State and compatibility

- Existing callers remain unchanged unless they explicitly send `learn: true` with the required
  learning intent. Planning and candidate creation remain inert; only the existing separately
  authorized execution path can change application state.
- Recipe data is retained indefinitely in this first release. Revisions and evidence are appended;
  review does not rewrite prior evidence.
- Existing direct actions, local-AI-disabled planning, web conversations, catalog navigation, and
  protocol clients remain compatible under the complete suite.
- Validation used disposable or test databases. The normal local host database was not migrated or
  otherwise touched for Slice 12G acceptance.

## Deliberate exclusions

Slice 12G does not add automatic learning, automatic promotion, parameterized nonempty action
inputs, query steps in recipes, model-authored code/effects, catalog writes, durable conversations,
public remote recipe administration, or game-specific rules. Those are not implied by the final
combined acceptance in Slice 12H.
