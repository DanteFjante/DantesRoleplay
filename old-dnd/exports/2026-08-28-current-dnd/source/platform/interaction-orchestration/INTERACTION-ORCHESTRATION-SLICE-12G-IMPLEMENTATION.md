# Interaction orchestration Slice 12G implementation — reviewed recipe learning

Status: **accepted 2026-08-24**  
Owner/roadmap: [Platform enabling features roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)  
Dependency tree/leaf: [Interaction orchestration Slice 12G](INTERACTION-ORCHESTRATION-DEPENDENCY-PLAN.md#lowest-ready-leaf)  
Receipt: [Slice 12G completion receipt](receipts/INTERACTION-ORCHESTRATION-SLICE-12G-RECEIPT.md)  
Ruleset alignment: **ruleset-neutral**  
Source ID and locator: **not applicable**  
Outcome: Let an explicitly opted-in successful interaction create an inert, reviewable recipe
candidate; let a private operator inspect and promote or retire it; and let both the local planner
and a remote MCP planner retrieve only current verified recipes as non-authoritative planning aids.  
Exclusions: Automatic learning without opt-in; automatic promotion; model-authored code/effects;
catalog/source-file writes; durable conversation changes; ruleset logic; arbitrary input-value
reuse; recipe execution without the common verifier; public remote MCP; recipe deletion; and final
combined Slice 12 acceptance.  
Allowed files/areas after confirmation: `src/system/interaction-orchestration`, its tests and
component manifest; the smallest receipt/execution/gateway additions needed to carry learning
evidence; `DantesRoleplay.DataAccess` recipe entities/mapping/migration/registration; the existing
generic `DantesRoleplay.MCPServer` three-verb adapters/catalog/guards; the application-conversation
execute request needed for opt-in; system procedure documentation; this document, its receipt, and
concise owner-status links.  
Stop point: Stop when opted-in successful executions produce inert candidates, explicit review is
audited, only verified/current recipes are retrievable, every reuse passes current hydration and
the common verifier, and stale/poisoned/candidate recipes cannot change state. Do not begin Slice
12H or add a new verb.

Implementation model: **GPT-5.6 Sol High**. This slice owns a migration, public kind/schema changes,
stored user text, poisoning controls, and promotion authority; Terra may perform bounded mechanical
edits only after this contract is confirmed and must return to Sol for acceptance review.

## Confirmation package

The following package was confirmed by the user on 2026-08-24 and is active as one boundary.

1. **Learning is explicit.** Extend `system.interaction-execute` with optional `learn` (default
   `false`) and `learningIntent`. `learningIntent` is the exact original closed intent object and is
   required only when `learn` is `true`; otherwise it is forbidden. The server reconstructs the
   original authorized envelope and requires its fingerprint to equal the resolution receipt. A
   different or invented intent conflicts before candidate creation and never changes the already
   completed execution result.
2. **Web opt-in is explicit.** Extend the application conversation execute body with optional
   `learn` (default `false`). The server, not the browser, supplies the exact pending intent retained
   in the ephemeral conversation. The reusable element displays an unchecked “Remember this
   route” choice; confirmation of the action does not imply confirmation of learning.
3. **Bounded stored text.** Opt-in permits storing the normalized original `intentText` as a recipe
   retrieval example, at most 500 characters. It is private-operator review data and deterministic
   lexical input only: it is never returned in recipe projections or copied into receipts, mechanic
   input, a model prompt, a vector document, a public feature document, or narration. Recipe vector
   text uses only authoritative mechanic names/descriptions and role-slot names. Oversized or
   control-bearing text leaves execution successful but returns a typed `learning-not-created`
   result.
4. **Candidate only after complete success.** Derivation runs only after an authorized execution
   receipt has disposition `succeeded` and every step is `succeeded` or `replayed`. Failed,
   partial, stale, cancelled, timed-out, unauthorized, or unreceipted outcomes never create or
   update a recipe. Equal execution replay retries candidate derivation idempotently.
5. **Safe first template format.** A candidate may contain action steps only. It stores exact
   current mechanic qualified IDs/versions/hashes, step dependencies, and declared role **slot
   names**, never bound entity IDs. The first delivery accepts only canonical empty `{}` mechanic
   input; a non-empty input returns `RECIPE_INPUT_PARAMETERIZATION_UNSUPPORTED` without affecting
   the successful execution. General typed input-slot inference is deferred rather than guessing
   which values are identity, state, or reusable constants.
6. **Permanent identity and fingerprints.** Recipe IDs have form
   `<application-id>.recipe.<32 lowercase hexadecimal characters>`. IDs are deterministic from the
   application and template fingerprint, so repeated successful examples converge on one identity.
   Confirm fingerprint
   domains `dantes-roleplay/interaction-recipe-id/v1` and
   `dantes-roleplay/interaction-recipe-template/v1`.
7. **Append-only authoritative storage.** Add main-SQLite tables `interaction_recipe`,
   `interaction_recipe_revision`, and `interaction_recipe_evidence`. The identity/template row is
   immutable; status changes append a numbered revision; successful provenance/use/failure rows
   append evidence keyed by execution receipt. A unique application/template fingerprint merges
   repeated successful examples without replacing prior evidence. Records are retained
   indefinitely; `retired` is a revision, not deletion. No normal live database is initialized or
   migrated during development.
8. **Closed statuses and review.** Statuses are exactly `candidate`, `verified`, `stale`, and
   `retired`. The initial revision is `candidate`. Only an explicit private-operator review may
   append `verified` or `retired`. Current-authority drift may append `stale`; nothing may transition
   out of `stale` or `retired`. A changed route is learned as a new candidate rather than repairing
   old evidence.
9. **Public administration remains three-verb.** Add query kind
   `system.interaction-recipes` with `applicationId` plus exact `id` or bounded `query`, optional
   status filter, cursor, and limit. Add commit kind `system.interaction-recipe-review` with closed
   payload `{requestToken, applicationId, recipeId, expectedVersion, decision, reason}` where
   decision is `verify` or `retire`. The query returns candidate template/provenance/status only to
   the verified private operator and returns only a fingerprint for stored retrieval examples;
   ordinary feature search and planners see verified safe projections only.
10. **Review is independent verification.** Promotion rehydrates the current application revision,
    activation/effective set, state-independent mechanic records, roles, versions, and hashes;
    verifies every provenance resolution/execution receipt and operation link; rejects fixed entity
    values, code, effects, prompts, paths, credentials, untrusted text, unsupported input, and
    cross-application references; and records the reviewer’s opaque principal plus bounded reason.
    A request token replays only an identical review request and conflicts otherwise.
11. **Verified retrieval is deterministic.** Exact and lexical retrieval are always complete and
    application-isolated. Optional vectors use a separate `TrustedRecipe` lane and generation in
    the existing disposable interaction index; disabled, absent, stale, or wrong-dimension vectors
    fall back to the identical deterministic lexical result. Candidate/stale/retired rows never
    enter planner retrieval.
12. **Reuse remains inert and current.** The planner first looks for verified recipes. A unique
    current match with all required role hints may reconstruct an inert proposal; ambiguous or
    incomplete matches fall back to ordinary search/model planning or typed `needs-input`. The
    resolver rehydrates every current mechanic and calls `IInteractionProposalVerifier`; execution
    still requires a new explicit consent request and performs the Slice 12F execution-time checks.
    Remote callers may inspect the same verified recipe through the new query and submit a proposal
    through `system.interaction-plan`; they gain no direct recipe-execution operation.
13. **Structured use evidence.** Add an optional chosen-recipe reference (ID, version, template
    fingerprint) to the resolution result/receipt projection and persisted resolution row. It is
    written only when the final verified proposal exactly matches the current recipe template.
    Successful or failed later execution appends bounded use evidence; it never edits the recipe
    revision or the original receipts.

## D&D 5e 2024 alignment

| Rule concern | Source meaning | Existing owner | Consequence |
| --- | --- | --- | --- |
| Route meaning | No D&D rule is interpreted or persisted by the recipe owner. | Application catalog mechanics and JavaScript sandbox | Recipes reference exact mechanics and generic roles only. |
| Entity roles | Actor/target-like meanings are application contract data. | Mechanic requirements and common proposal verifier | Storage keeps slot names and never prior entity bindings. |
| Outcomes | Eligibility, calculations, effects, and narration remain authoritative mechanics. | `IApplicationActionRunner` and application ECS effect owner | A recipe cannot store or replay outcomes/effects. |

No SRD locator or Foundry inspection applies because this is generic persistence, retrieval,
authorization, and proposal plumbing. No ruleset vocabulary belongs in production additions.

## Prerequisite evidence

- [Slice 12F receipt](receipts/INTERACTION-ORCHESTRATION-SLICE-12F-RECEIPT.md) accepts exact
  application action execution, at-most-once replay, partial-progress receipts, public plan/execute
  protocol, private authorization, and the ephemeral application conversation.
- [Slice 12D receipt](receipts/INTERACTION-ORCHESTRATION-SLICE-12D-RECEIPT.md) accepts append-only
  receipt storage and safe authorized projections. Recipe provenance must reference these rows
  rather than duplicate action truth.
- [Slice 12C receipt](receipts/INTERACTION-ORCHESTRATION-SLICE-12C-RECEIPT.md) accepts deterministic
  lexical retrieval and optional disposable vectors with complete vector-disabled fallback.
- Existing `IVerifiedInteractionRecipeResolver` and its empty registration are the intended planner
  seam. Current planner behavior deliberately rejects a non-empty resolver until this slice.

## Runtime artifacts after confirmation

- Pure recipe ID, reference, template, status, candidate, evidence, review, projection, retrieval,
  and learning-result contracts under `interaction-orchestration`.
- `InteractionRecipeStore`, derivation/review service, current-authority validator, verified recipe
  resolver, lexical retrieval, and optional derived-vector adapter.
- Three append-only EF entities, mappings, one generated `InteractionRecipes` migration, snapshot
  update, DI registration, and disposable-database tests.
- Revised execution request/outcome, resolution receipt recipe reference, gateway/conversation
  opt-in plumbing, two MCP kinds, capability metadata, guards, protocol walk, and system procedure.

No recipe is authored in catalog files and no application/game adapter is added.

## Authoritative state and closed input

The host derives recipe ID/version/status, template and fingerprints, normalized slot names,
contract references, application/base revision, activation/effective-set fingerprint, provenance
receipt/operation links, reviewer identity, vector generation, current-authority validation, and all
transition results.

The execute caller may add only `learn` and the exact original `learningIntent`; it cannot supply a
recipe ID, template, slots, status, provenance, review, current hashes, or retrieval terms. The
review caller supplies only the confirmed closed payload. The planner supplies no recipe status or
validation truth.

## Behavior and transaction ownership

1. Execute and persist the Slice 12F receipt exactly as today. Learning is downstream and cannot
   convert a failed action into success or a successful action into failure.
2. When opt-in is false, do no recipe read/write. When true, reconstruct the original envelope and
   require the stored fingerprint, successful execution receipt, exact proposal, and current
   application scope.
3. Derive the canonical role-slot/action template without entity IDs or input values. Validate it
   again through current inspected contracts, compute its fingerprint/ID, and append the candidate
   identity/revision/evidence in one recipe-store transaction. Equal evidence replays; disagreement
   conflicts without replacement.
4. Review reads current authority and all provenance before appending one verified/retired revision
   plus the ordinary public operation audit. A race on `expectedVersion` fails without a revision.
5. Retrieval obtains the latest revision, lazily appends `stale` when current application,
   activation, winner, or contract hashes differ, filters to verified/current records, and ranks
   exact then lexical then optional vector candidates deterministically.
6. A matched recipe reconstructs role bindings only from the current authorized intent envelope,
   rehydrates current mechanic contracts, and calls the common verifier. It returns an inert plan
   and a chosen-recipe reference; no action runs until explicit execution.
7. After a recipe-backed execution, append use evidence from the execution receipt. Failure evidence
   informs review/diagnostics only and never mutates or auto-demotes a verified revision; stale
   authority appends `stale` separately.

Recipe storage owns its own append transactions. It does not join the action/ECS transaction and
never claims rollback of completed application state. A crash after action/receipt but before
candidate storage is recoverable by repeating the exact execute request, which replays the action
and receipt before idempotently deriving the candidate.

## Failure, replay, and no-change contract

| Failure | Result | No-change evidence |
| --- | --- | --- |
| `learn` absent/false | `learning-not-requested` | No recipe read/write. |
| Missing/mismatched learning intent | `LEARNING_INTENT_MISMATCH` | Execution receipt remains authoritative; no candidate. |
| Failed/partial/stale/cancelled execution | `LEARNING_EXECUTION_INELIGIBLE` | No recipe identity/revision/evidence. |
| Non-empty input or unsupported/query/event step | typed not-created result | No recipe record; execution result unchanged. |
| Fixed entity ID/value, code/effect/prompt/path/untrusted content | `RECIPE_TEMPLATE_UNSAFE` | No candidate/promotion. |
| Equal learning replay | prior candidate/evidence | No duplicate revision or evidence. |
| Conflicting learning/review token | conflict | No replacement or transition. |
| Candidate used for planning/execution | ignored/rejected | No proposal/action from candidate. |
| Review with stale expected version or denied principal | conflict/unauthorized | No revision or audit claiming success. |
| Contract/app/overlay changed | append `stale`, lexical fallback | Recipe omitted; ordinary planning remains available. |
| Vector disabled/corrupt/stale | lexical fallback | Same complete ordered verified set; no authoritative writes except independently detected stale revision. |
| Several equal recipe matches | ambiguous recipe result then ordinary planner | No automatic route selection or action. |
| Recipe resolver/storage unavailable | ordinary planner or typed unavailable | No direct execution fallback. |

## Implementation sequence for the coding AI

1. Confirm this document is `active`, record the confirmed package, inspect the dirty worktree, and
   preserve all prior slice/user changes. Never open or migrate the normal host database.
2. Add pure closed recipe/template/reference/status/review/learning contracts and exhaustive
   no-value/no-code validation tests. Do not add persistence or protocol until these pass.
3. Add EF entities/mappings and one migration. Test on fresh/disposable SQLite that identity,
   revisions, evidence, foreign keys, uniqueness, replay, races, and rollback are append-only.
4. Implement derivation and independent promotion validation. Use receipt/current snapshot ports;
   never query another owner’s tables directly from orchestration logic.
5. Implement exact/lexical and optional-vector verified retrieval with application isolation,
   deterministic ties, generation invalidation, and lexical parity.
6. Replace the empty resolver. Rehydrate and call the existing proposal verifier; record a recipe
   reference only for an exact current template match. Keep provider/model paths unchanged.
7. Add opt-in execution/conversation plumbing and post-receipt candidate/use evidence. Prove crash
   recovery and that learning failures never rewrite execution truth.
8. Add both public kinds, closed payloads, private-host authorization, descriptions/examples,
   dispatch/guards, and system procedure together. Retain exactly three verbs.
9. Run focused tests, fresh migration/database tests, catalog validation, full suites, isolated
   build, protocol walk, architecture/poisoning searches, and `git diff --check`.
10. Perform Sol review, write the Slice 12G receipt, update the owner status once, and stop before
    Slice 12H.

## Acceptance matrix

| Concern | Required evidence |
| --- | --- |
| Opt-in | Default/off creates nothing; exact opt-in stores one candidate after success only. |
| Provenance | Candidate links one successful resolution/execution and authoritative operations without copying effects. |
| Parameterization | Entity bindings and all input values are absent; only current action references and role slots remain. |
| Replay/race | Equal retry returns the same recipe/evidence; conflicts and concurrent duplicates do not fork identity. |
| Review | Candidate cannot execute; verified requires fresh private review and exact expected version; retire/stale are terminal. |
| Poisoning | Prompts, instructions, code, effects, paths, credentials, untrusted documents, and prior entity IDs reject. |
| Retrieval | Exact/lexical verified search is deterministic, paged, application-isolated, and complete without vectors/local AI. |
| Vector fallback | Missing/stale/corrupt/wrong-dimension recipe vectors preserve identical lexical results. |
| Reuse | Current verified match rebinds roles, rehydrates contracts, passes the common verifier, and remains inert until execute. |
| Invalidation | Application/base/activation/effective winner/version/hash change marks stale before proposal or action. |
| Symmetry | Inner planner and remote MCP planner can inspect the same verified safe template and submit through the same verifier. |
| Learning evidence | Successful/failing recipe-backed executions append evidence without automatic promotion/demotion. |
| Authorization/redaction | Candidate template/provenance is private-operator-only, stored retrieval text is never projected, and ordinary planners see only verified safe projections. |
| Compatibility | Existing execute requests without learning remain byte/behavior compatible; direct action and web/MCP hosts still pass. |
| Surface | Exactly three verbs remain; catalog, dispatchers, examples, guards, and protocol walk agree on two new kinds. |

## Verification commands

- Focused recipe contract/store/migration/derivation/review/retrieval/resolver/execution/web/protocol
  and authorization tests, plus existing interaction execution and receipt suites.
- Fresh disposable migration application and rollback/race tests; never the normal database.
- `roleplay validate catalog` after the system procedure change.
- Full shared suite and standalone local-AI suite.
- Isolated-output solution build and `git diff --check`.
- Protocol walk because query/commit kinds, payloads, descriptions, and registration change.
- Static searches proving no game literals, prior entity IDs/input values, prompts, JavaScript,
  effects, paths, credentials, or untrusted document text enter recipe records/projections; no
  recipe bypasses `IInteractionProposalVerifier` or `IInteractionExecutionCoordinator`; and no
  local-AI reverse dependency appears.

## Completion receipt and exit gate

After all evidence and Sol review, write
`receipts/INTERACTION-ORCHESTRATION-SLICE-12G-RECEIPT.md`. Mark 12G accepted only when candidate
creation, review, verified retrieval/reuse, invalidation, replay, poisoning, authorization, and
three-verb compatibility all pass. Stop before Slice 12H final combined acceptance.
