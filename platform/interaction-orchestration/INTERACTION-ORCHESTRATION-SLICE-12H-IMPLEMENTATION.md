# Interaction orchestration Slice 12H implementation — combined acceptance and independence

Status: **accepted 2026-08-25**  
Owner/roadmap: [Platform enabling features roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)  
Dependency tree/leaf: [Interaction orchestration Slice 12H](INTERACTION-ORCHESTRATION-DEPENDENCY-PLAN.md#lowest-ready-leaf)  
Receipt: [Slice 12H completion receipt](receipts/INTERACTION-ORCHESTRATION-SLICE-12H-RECEIPT.md)  
Ruleset alignment: **ruleset-neutral**  
Source ID and locator: **not applicable**  
Outcome: Prove the accepted Slice 12A–12G capabilities work together while preserving local-AI,
application, trust, authorization, execution, recipe, web, and public-protocol boundaries.  
Exclusions: New runtime capability; new permanent IDs, tables, migrations, schemas, protocol kinds,
verbs, routes, model profiles, catalog mechanics, game rules, provider/network calls, live-database
mutation, or redesign of an accepted slice.  
Allowed files/areas: This document; one consolidated Slice 12H acceptance-test file and existing
interaction-orchestration, application-kernel, MCP protocol/guard, web conversation, local-AI, and
catalog-validation tests; the smallest existing production owner only when a test proves accepted
behavior is broken; the Slice 12H receipt and concise owner-status links.  
Stop point: Record the combined evidence and present the final acceptance gate. Do not start a new
platform feature or extend any deliberate exclusion.

## Confirmed decisions

- The user's 2026-08-24 instruction to continue after Slice 12G confirms commencement of this final
  bounded audit. Completed feature acceptance remains the explicit exit gate after evidence exists.
- The authoritative semantics are the accepted Slice 12A–12G contracts and receipts. Slice 12H may
  expose and correct a concrete regression but may not reinterpret those contracts.
- No normal host database is initialized, migrated, imported, or used. Catalog validation and
  persistence checks use disposable databases; model-provider network calls remain disabled.
- Exactly three public verbs and the existing orchestration kinds remain fixed. No production
  identifier, storage meaning, or public request/response shape is added here.

## D&D 5e 2024 alignment

This slice is ruleset-neutral. It verifies generic scope and independence and therefore has no SRD
source locator or Foundry implementation dependency. Game-specific literals, formulas, eligibility,
or outcomes in generic production code are a blocking finding, not material to reproduce in tests.

## Prerequisite evidence

- [Application-kernel Slice 12A receipt](../application-kernel/receipts/APPLICATION-KERNEL-SLICE-12A-RECEIPT.md)
  records the planner-neutral catalog handoff and zero/two-application independence boundary.
- [Slices 12B–12G receipts](receipts/) record the frozen contracts, trusted retrieval, durable
  receipts, symmetric planners, verified execution/application surface, and reviewed recipe
  learning. Slice 12G's full shared suite accepted 784 tests plus 20 standalone local-AI tests.
- The dependency tree marks A–H accepted and Slice 12H/I as the one remaining combined acceptance
  leaf. Current code and tests, not prospective prose, determine whether each row still holds.

## Runtime artifacts

No new runtime artifact is planned. The acceptance work may add one ruleset-neutral consolidated
test class containing no permanent runtime IDs. A production change is permitted only as the
smallest correction to an already-confirmed invariant and must be named in the completion receipt.
If a finding would require a new ID, migration, schema meaning, public surface, cross-owner semantic
decision, or ruleset behavior, stop and return that exact blocker instead.

## Authoritative state and closed input

- Application/source/type/state-space ownership, effective manifests, overlays, and ECS data remain
  application-kernel authority.
- Trusted feature materialization and recipe retrieval consume current effective winners. Vector
  indexes remain derived and disposable; lexical paths remain complete without them.
- Intent, plan, resolution, execution, operation, and recipe evidence remain owned by the existing
  orchestration and operation stores. Neither model nor browser supplies authority, effects,
  revisions, hashes, roles, profiles, prompts, tools, or approval policy.
- Existing host authorization supplies the verified principal, application, state-space, and
  session context. All model and recipe output remains an untrusted proposal until common
  verification and explicit execution consent.

## Behavior and review method

1. Map every master acceptance row to a current focused test or add a consolidated test where the
   cross-slice relationship is otherwise only implied.
2. Exercise both model-independent routes: local provider unavailable/disabled with remote closed
   proposal submission, and outer direct/delegated planning through the same verifier/executor.
3. Prove current-authority hydration, application isolation, overlay winner changes, safe no-vector
   fallback, exact execution replay/partial progress, candidate lifecycle, verified recipe reuse,
   and stale/poisoned rejection without unauthorized state mutation.
4. Prove the non-control-center web component preserves application/session scope and exposes no
   operator, filesystem, raw model, direct-inner, or MCP authority.
5. Audit production dependency direction and vocabulary. The local-AI project must not reference
   game/application implementations or mutation owners, and the generic orchestration build must
   not require a game pack.
6. Run the entire solution, catalog, migration, protocol, and diff gates from the same worktree.

Slice 12H owns no transaction. Tests invoke the existing transaction owners and assert their
accepted replay, atomicity, partial-progress, and no-change behavior.

## Failure, replay, and rollback contract

- Disabled/unavailable/malformed/budget-exhausted model paths remain typed and inert; no provider
  availability may remove remote traversal/proposal capability.
- Unknown, ambiguous, unauthorized, cross-application, untrusted, forged, stale, and poisoned input
  fails before application mutation and leaves sufficient safe receipt/audit evidence.
- Execution replay returns prior evidence without repeating an operation. A committed earlier step
  is reported as committed when a later independent step fails; no false rollback is claimed.
- Candidate, stale, or retired recipes cannot execute. Verified recipes rebind roles from the
  current request and pass current authority plus the common proposal verifier on every use.
- A required all-or-nothing application change remains one existing action/composition transaction.
- A test-infrastructure or disposable-store failure may fail the audit but must not trigger normal
  host database initialization or mutation.

## Implementation sequence

1. Inventory existing test names and map them to every acceptance row; do not duplicate well-proven
   behavior merely to raise a test count.
2. Add the smallest consolidated ruleset-neutral acceptance tests for uncovered cross-slice seams.
3. Run focused acceptance/guard/web/protocol tests. Diagnose failures against current contracts and
   make only minimal accepted-behavior corrections.
4. Run static dependency/vocabulary/privacy searches, migration consistency, disposable catalog
   validation, the protocol walk, the full solution suite, standalone local-AI tests, isolated build,
   and `git diff --check`.
5. Write the Slice 12H completion receipt, request/record final feature acceptance, update the owner
   status once, and stop.

## Acceptance matrix

| Concern | Required combined evidence |
| --- | --- |
| Local disabled / no-AI | Remote lexical traversal, exact inspection, closed proposal submission, explicit execution, and optional candidate learning remain complete with local completion, embeddings, and vector storage disabled. |
| Role isolation | Inner Luna Low and outer Luna High profiles are host-owned; delegation is bounded and linked; browser/model output cannot alter roles, tools, prompts, execution, or approval authority. |
| Proposal parity | Local, outer-direct, outer-delegated, and remote submitted proposals use the same hydration, semantic verifier, execution coordinator, receipt, and learning lifecycle. |
| Application isolation | System scope, one application plus confirmed bases, unrelated application, state-space, principal, and parent-interaction boundaries fail closed without leakage or mutation. |
| Retrieval/overlay/trust | Exact/lexical parity survives vector failure; deterministic higher winner/base reveal/equal conflict and untrusted/shadowed exclusion remain true before indexing and execution. |
| Receipts/current authority | All terminal outcomes are typed/redacted; selected contracts are rehydrated before proposal and execution; forged/stale versions, hashes, IDs, and fingerprints are inert. |
| Execution | Explicit consent, at-most-once replay, partial-progress truth, operation linkage, and application-action transaction ownership hold together. |
| Learning | Only opted-in successful validated execution creates a candidate; private audited review alone verifies it; safe verified reuse rebinds current roles; candidate/stale/poisoned recipes never execute. |
| Embedded application surface | The reusable component works outside the control center, preserves its containing application/session context, defaults learning off, and exposes no operator or direct system authority. |
| Generic independence | Local AI and generic orchestration contain no game rules or reverse dependency on an application pack; zero/two-application composition and the generic build remain valid. |
| Compatibility/surface | Existing direct action clients still work with orchestration providers disabled; exactly three verbs remain and advertised kinds, schemas, dispatch, guards, and protocol walk agree. |
| Repository acceptance | Consolidated focused tests, full shared and standalone local-AI suites, disposable catalog validation, migration consistency, protocol walk, isolated build, architecture searches, and `git diff --check` pass together. |

Every rejection row asserts no unauthorized state mutation and the appropriate safe receipt or audit
evidence when the contract requires one.

## Verification commands

- Focused Slice 12 interaction/retrieval/receipt/planner/execution/recipe, application-kernel
  overlay/independence, MCP protocol/guard, and web conversation test filters.
- `dotnet test DantesRoleplay.slnx --verbosity minimal`.
- Standalone local-AI suite result from the solution run, plus a separately reported project run if
  needed to keep the evidence unambiguous.
- `roleplay.cmd validate catalog`, which must report that no live data was touched.
- `dotnet ef migrations has-pending-model-changes --project DantesRoleplay.DataAccess
  --startup-project DantesRoleplay.DataAccess --no-build` after a successful current build.
- Protocol walk with `IncludeProtocolWalkTests=true` because this is final public-surface acceptance,
  even though Slice 12H changes no public surface.
- Isolated-output `dotnet build DantesRoleplay.slnx` with a disposable artifacts directory.
- Static project-reference, game-vocabulary, prompt/privacy, three-verb, public-kind, recipe bypass,
  and normal-database-path searches; then `git diff --check`.

## Completion receipt and exit gate

Record delivered evidence and any minimal correction in
`receipts/INTERACTION-ORCHESTRATION-SLICE-12H-RECEIPT.md`. Do not mark Slice 12 or its roadmap owner
accepted until the full evidence is green and the user confirms the final feature-acceptance gate.
Stop afterward; exclusions remain planned only under a separately authored owner/slice.
