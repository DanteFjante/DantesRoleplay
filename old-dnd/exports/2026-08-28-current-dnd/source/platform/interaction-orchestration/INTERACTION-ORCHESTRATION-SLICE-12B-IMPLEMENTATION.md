# Interaction orchestration Slice 12B implementation — authority and contract foundation

Status: **accepted**  
Owner/roadmap: [Platform enabling features roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)  
Dependency tree/leaf: [Interaction orchestration Leaf B and Slice 12B](INTERACTION-ORCHESTRATION-DEPENDENCY-PLAN.md#lowest-ready-leaf)  
Completion evidence: [Slice 12B receipt](receipts/INTERACTION-ORCHESTRATION-SLICE-12B-RECEIPT.md)  
Ruleset alignment: **ruleset-neutral**  
Source ID and locator: **not applicable**  
Outcome: Ratify one server-authoritative, application-scoped interaction boundary and, only after
confirmation, add bounded pure contracts, canonical fingerprints, guards, and in-memory fakes for
intent resolution, proposals, typed non-resolution, role binding, and explicit execution consent.  
Exclusions: SQLite tables/migrations; feature/vector indexes; local or remote model invocation;
Codex process/API implementation; protocol kinds; web routes/components; action execution; receipt
persistence; recipe persistence/promotion; catalog records; D&D adapters/rules; and live database
changes.  
Allowed files/areas after confirmation: new `src/system/interaction-orchestration/component.json`,
`domain/*.cs`, and `tests/*.cs`; focused component/guard registration only if required; this plan,
its receipt, and concise owner status links.  
Stop point: Stop after pure contract tests prove the confirmed boundary. Do not start retrieval,
persistence, a provider adapter, public protocol, execution, web UI, or recipe learning.

## Confirmed decisions

The user confirmed the following package on 2026-08-24:

1. Adopt `interaction-orchestration` as the component owner. It may depend on application/catalog,
   authorization, assistant-conversation, local-AI contracts, actions, and operations/audit ports,
   but none may depend back on orchestration. Local AI remains game- and orchestration-unaware.
2. Reuse `TrustedPrincipalContext` as the transport-derived identity. Add an internal generic
   application-authorization port whose closed capabilities are plan, execute, and read-receipt.
   The first host may keep the accepted private single-operator policy; no grant-management system
   is required.
3. Bind application ID, state-space/session context, principal reference, inner/outer role, model,
   reasoning effort, conversation ID, parent delegation ID, current revisions, budgets, and
   authorization evidence on the server. A browser/model may not supply or override them.
4. Store player/assistant message text only through the existing authorized conversation owner.
   Interaction receipts store the canonical intent-envelope SHA-256 fingerprint, bounded safe
   summaries/queries, and authoritative references—not duplicate raw intent, hidden prompts, or
   chain-of-thought. A non-conversation request is transient apart from its hash and safe evidence.
5. Planning and execution are separate. A resolved plan is inert. Execution later requires a new
   authorized request naming the exact resolution receipt and proposal fingerprint; a `plan` call,
   local-model success, remote-model success, or recipe match never implies consent.
6. Use the closed resolution statuses `resolved`, `needs-input`, `ambiguous`, `unknown`,
   `unsupported`, `unavailable`, `unsafe`, and `stale`. Non-resolution is a normal result with
   evidence and no mutation, not an exception or fabricated fallback.
7. Persist future append-only interaction receipts/recipes in the main host SQLite database because
   they are authoritative runtime evidence. Keep embeddings and lexical/vector index material in a
   separate disposable derived-index database. Slice 12B creates neither store.
8. Learn only when a later explicit execution request carries `learn: true`. One successful,
   validated execution may create a candidate; candidates cannot execute; verification requires an
   explicit private-operator review. Runtime learning never edits catalog/source files.
9. Keep runtime AI roles closed: inner is `gpt-5.6-luna`/`low`; outer is
   `gpt-5.6-luna`/`high`. Both consume only bounded server-mediated observations and emit a closed
   proposal/non-resolution schema. Resuming cannot change role, application, model, or effort.
10. Fail closed on model isolation. A product-role provider must attest that filesystem, shell,
    network, arbitrary MCP, approval, and direct execution authority are absent. The current pinned
    Codex app-server bridge exposes read-only/no-network policy but still exposes repository reads,
    shell activity, and approvals, so it is not eligible for inner/outer product roles unchanged.
    Recommended: keep that bridge for the administrative control center and implement the Slice 12E
    product adapter through a provider surface that supplies only the closed orchestration tools—or
    no tools—such as the Responses API with an explicit tool allowlist. If no eligible provider is
    configured, return `unavailable`; never weaken isolation.
11. Defer exact public `system.*` kinds, serialized schemas, route/custom-element names, retention
    periods, and database table names to their owning later slices. The conceptual names in the
    master plan are not permanent IDs.

## D&D 5e 2024 alignment

| Rule concern | Source meaning | Existing owner | Consequence |
| --- | --- | --- | --- |
| Application intent | No D&D rule is interpreted here. | Application adapter and catalog contracts | Generic contracts carry opaque application/feature references only. |
| Mechanic outcome | Existing catalog JavaScript remains authoritative. | Mechanics/actions | Neither planner nor orchestration contract may supply effects or derived outcomes. |
| Game state | Exact application ECS/state-space revisions are authoritative. | Application kernel | Callers cannot claim current state, eligibility, or authorization. |

## External implementation reference

No Foundry dnd5e reference applies because this slice adds ruleset-neutral contracts only.

Official [OpenAI Responses documentation](https://developers.openai.com/api/reference/cli/resources/responses/methods/create)
exposes an explicit tool list and `tool_choice`, including the ability to provide no tools or an allowed subset. Repository evidence for pinned Codex app-server
`0.149.1` proves only read-only filesystem/no-network policy with approval handling; it does not
prove a no-shell/no-filesystem tool boundary. Slice 12B therefore specifies a provider capability
attestation and fail-closed behavior rather than relying on prompt instructions or assuming an
undocumented app-server parameter.

## Prerequisite evidence

- [Slice 12A receipt](../application-kernel/receipts/APPLICATION-KERNEL-SLICE-12A-RECEIPT.md) proves
  application-isolated, vector/local-AI-free catalog traversal through one public provider.
- [Slice 11J receipt](../application-kernel/receipts/APPLICATION-KERNEL-SLICE-11J-RECEIPT.md) proves
  application ECS projection and read-only exact mechanic evaluation without public game commands.
- `src/system/authorization` already owns opaque trusted principal context and a closed private-host
  policy; this slice does not create identity or grants.
- `src/system/assistant-conversations` already owns operator-scoped messages, turns, idempotency,
  model evidence, and audit linkage; interaction receipts must reference rather than duplicate it.
- `src/system/codex-bridge` proves the pinned app-server lifecycle and current read-only/no-network
  boundary, while its command/file/MCP activity and approvals show why it is not a product-role
  no-tools boundary unchanged.

## Runtime artifacts after confirmation

- `interaction-orchestration` component metadata with ruleset-neutral ownership and dependency
  direction.
- Immutable value contracts for host context, intent envelope, resolution status/result, exact
  contract references, proposal steps/dependencies, non-resolution evidence, AI role profile,
  provider capability attestation, and execution-consent reference.
- Canonical bounded JSON/fingerprint helpers. Equal semantic input produces equal uppercase SHA-256;
  dictionary/property order, whitespace, and caller object identity cannot alter it.
- Pure validators that reject forbidden caller authority, cross-application references, malformed
  dependency graphs, unbounded text/collections/JSON, invalid status transitions, role/profile
  changes, and a provider missing required isolation capabilities.
- In-memory fake authorization/receipt/reference resolvers sufficient for contract tests only. No
  planner, model, action, or persistence implementation.

## Authoritative state and closed input

Caller input is limited to a bounded idempotency key, bounded intent text, optional role/entity
hints treated as untrusted references, bounded authorized conversation-fact references, maximum
plan steps within the host ceiling, planner preference, and plan-only intent. Slice 12B defines no
execute or learning operation.

The host supplies principal/application/state-space/session scope, authorization decision,
application/source/effective-set revisions, conversation/role/delegation linkage, effective model
profile, budgets, timestamps, IDs, current contract references, and all validation truth. Model
output may supply only a schema-valid proposal or typed non-resolution and is never authority.

## Behavior, result, and typed effects

1. Copy and bound caller input; reject fields reserved for host authority.
2. Bind trusted host context and immutable AI role profile.
3. Canonicalize and fingerprint the authorized envelope with an explicit domain/version prefix.
4. Validate a proposed read/query/action dependency DAG structurally, but do not inspect catalogs,
   execute queries, or run actions in this slice.
5. Return either one inert proposal with a canonical fingerprint or one closed non-resolution with
   bounded safe evidence.
6. Define an execution-consent reference that can later bind receipt ID, proposal fingerprint,
   principal/application scope, and idempotency key; do not expose an executor.

This slice produces no typed effects and owns no transaction.

## Failure, replay, and rollback contract

Malformed/unbounded input, unknown status/role, caller-supplied host fields, cross-application
contract references, cyclic/duplicate steps, invalid dependencies, non-canonical JSON, changed role
profile, missing authorization evidence, or insufficient provider isolation produces a typed
rejection/non-resolution and no mutation. Equal idempotency key plus equal fingerprint is a replay;
the same key with different content is a conflict. Because the slice is pure, rollback means no
artifact beyond the returned value and test evidence.

## Implementation sequence after confirmation

1. Add component metadata and immutable value/status/profile/provider-capability contracts.
2. Add canonical JSON and domain-separated SHA-256 fingerprinting.
3. Add pure bounds, ownership, role, DAG, and forbidden-authority validation.
4. Add in-memory fakes and focused positive/negative/determinism/replay/isolation tests.
5. Run focused and full verification, write the receipt, mark 12B accepted, and stop.

## Acceptance matrix

| Concern | Required evidence |
| --- | --- |
| Positive | A bounded private-host interaction produces one immutable authorized envelope and inert proposal fingerprint. |
| Non-resolution | Every closed status round-trips with bounded evidence and no mutation. |
| Authority | Caller/model fields cannot set principal, application authority, revisions, hashes, role profile, validation result, effect, or execution consent. |
| Isolation | Provider missing any no-filesystem/no-shell/no-network/no-MCP/no-approval/no-execution attestation is unavailable. |
| Namespace | `system` cannot masquerade as an application; every non-system reference has one matching opaque application owner. |
| Determinism | Equivalent dictionaries/JSON produce byte-identical canonical data and fingerprints. |
| Dependency graph | Ordered acyclic steps pass; missing, duplicate, self, cyclic, and excess dependencies fail. |
| Replay | Equal key/fingerprint replays; same key/different fingerprint conflicts. |
| Compatibility | Existing assistant, authorization, catalog, action, local-AI, Codex-control-center, and three-verb protocol behavior is unchanged. |
| Independence | Contracts and tests contain no D&D/game vocabulary; local AI gains no project reference. |

## Verification commands

- Focused interaction contract, canonicalization, authorization-boundary, provider-isolation,
  namespace, dependency-DAG, replay, and component guard tests.
- Full shared and standalone local-AI test suites.
- Isolated solution build with zero warnings/errors and `git diff --check`.
- Catalog validation and protocol walk are not required unless an accidental catalog/public-surface
  change occurs; such a change is outside this slice and must be reverted or separately confirmed.

## Completion receipt and exit gate

After all recommended decisions are explicitly confirmed and implementation passes, write
`platform/interaction-orchestration/receipts/INTERACTION-ORCHESTRATION-SLICE-12B-RECEIPT.md`, mark
12B accepted in the master plan/roadmap, and stop. Slice 12C remains a separate implementation turn.
