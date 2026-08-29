# D&D 2024 web UI Slice 7D0 implementation — authorized knowledge core adoption

Status: **accepted 2026-08-27**
Owner/roadmap: [Knowledge and facts roadmap](../knowledge/KNOWLEDGE_AND_FACTS_PLAN.md),
supporting [Web interface roadmap](WEB-INTERFACE-ROADMAP.md) Feature 5
Dependency tree/leaf: [D&D 2024 web UI dependency plan](DND2024-WEB-UI-DEPENDENCY-PLAN.md),
Order 7D0 / F2
Ruleset alignment: **ruleset-neutral**
Source ID and locator: **not applicable**. This slice adopts generic authorization and retrieval
infrastructure; it implements no D&D rule.
Outcome: the current modular code owns a fail-closed, provider-neutral knowledge authorization
core over application-scoped ECS state, without compiling or depending on retained `old-dnd`.
Exclusions: audience implementation, fixed actor selection, campaign state writes/imports,
knowledge-state invention, public MCP/HTTP/query registration, browser UI, maps, images, vector
retrieval, migrations, new catalog IDs, schema changes, and D&D-specific vocabulary in C#.
Allowed files/areas: a new `src/system/knowledge/` component; its focused tests; this document and
the D&D web dependency/roadmap/status summaries; a component-registration extension that remains
unbound until Slice 7D1 supplies the mandatory policies.
Stop point: stop after contracts, application-ECS canonical projection, effective-state
resolution, pre-limit allowlisted lexical retrieval, safe answer coordination, and focused/full
evidence pass. Do not register a host audience, endpoint, query dispatch, or player surface.

## Confirmed decisions

- The user's 2026-08-27 instruction to continue explicitly accepts Slice 7D0 and the expansion
  from browser work into the required cross-owner authorization foundation.
- The existing `knowledge-answer` request/result semantics, policy-first denial, actor-state
  precedence, separate familiar recognition, and recheck-after-inference behavior are retained.
- The current modular application-scoped ECS and edge stores replace the legacy `IWorldStore`
  seam. Ruleset/application vocabulary is supplied through a validated binding contract; generic
  C# contains no D&D or `game.core` IDs.
- This slice creates the ruleset-neutral system-component owner `knowledge` but creates no catalog,
  procedure, query-kind, schema, database, application, or campaign-state identity.
- No permissive default policy or vocabulary binding is registered. A host cannot resolve the
  composed authorized service until later work supplies both explicitly.

## Outcome and boundary

One private host composition can:

1. ask an ambient audience policy for a campaign grant before reading game state;
2. resolve an exact application/state-space vocabulary binding after that grant;
3. project validated canonical knowledge documents from the bound ECS state;
4. derive explicit, applicable non-world baseline, world baseline, or unknown actor state;
5. filter the actor's allowed IDs before lexical ranking and limiting;
6. send only safe authorized candidates to a bounded no-tools completion; and
7. re-resolve the complete input after inference before returning ID-free statements.

This slice does not make that composition reachable from a browser, MCP client, or current host.

## Prerequisite evidence

- [`procedure.game.core.world.knowledge`](../catalog/procedures/game/core/world/procedure.game.core.world.knowledge.md)
  owns canonical truth/state meaning and requires the separate audience-safe query.
- [Knowledge Slice 6 readiness](../knowledge/KNOWLEDGE_AND_FACTS-SLICE-6-READINESS.md) fixes the
  policy-first, allowlist-before-limit, safe-result, and post-inference recheck contracts.
- [Knowledge Slice 6 receipt](../knowledge/KNOWLEDGE_AND_FACTS-SLICE-6-RECEIPT.md) proves those
  semantics in the retained implementation; it is evidence, not a runtime dependency.
- Application-scoped ECS, state-space registry, and edge stores are accepted current modular owners
  for campaign state. They preserve application/state-space isolation and exact component versions.

## Runtime artifacts

| Artifact | Status and meaning |
| --- | --- |
| `KnowledgeAudienceGrant` and `IAuthorizedKnowledgeAudiencePolicy` | New provider-neutral ambient policy seam; no default implementation. |
| `KnowledgeApplicationBinding` and resolver | New validated descriptor for exact component, relationship, JSON-field, state, and presentation vocabulary. |
| Authorized request/result/candidate contracts | Re-adopted safe shapes; public results contain no canonical IDs, sensitivities, hidden counts, policy revisions, or source truth kinds. |
| Application canonical source | New read-only projector over `IStateSpaceRegistry`, `IEntityComponentStore`, and `IStateSpaceEdgeStore`. |
| Effective-state resolver | New read-only explicit/baseline/unknown resolver driven only by the validated binding. |
| Lexical retriever | New deterministic bounded in-memory projection whose allowlist is applied before scoring and `Take`. It is derived and writes nothing. |
| Candidate and answer coordinators | New fail-closed private host composition with one retry after policy/state/document changes. |
| `AddAuthorizedKnowledgeCore` | New opt-in registration extension. The generic server does not call it in this slice. |

No migration, table, catalog record, application registration, component schema, query kind, or
HTTP/MCP surface changes.

## Authoritative state and closed input

The public answer request contains only campaign ID, bounded question, optional presentation-kind
and subject filters, optional world minute, and a candidate limit. It cannot carry principal,
actor, role, application/state-space/world identity, policy revision, component/relationship IDs,
allowed IDs, sensitivity, visibility, exact knowledge ID, or an include-hidden flag.

Audience grant, application/state-space binding, campaign root, world root/current minute, actor
participation, canonical documents, graph vocabulary, effective states, and allowed IDs are all
backend-resolved. The policy call happens before the binding resolver or any ECS/edge read.

## Behavior, result, and typed effects

- A binding validates exact application/state-space/campaign identities; unique knowledge kinds;
  complete component, relationship, JSON-field, temporal, state, scope, and presentation mappings;
  and disjoint content/familiar/unknown state meanings.
- Canonical projection requires one primary knowledge component, one classification, one world
  edge, one about edge, a live subject, valid optional interval, an active campaign/world, and a
  valid clock. Malformed records are omitted rather than partially projected.
- Explicit actor state wins. Otherwise an applicable active faction or containing-region baseline
  wins in stable scope-ID order, then a world baseline, then the configured unknown state. An
  explicit unknown always overrides every baseline.
- Content states enter the candidate allowlist. Familiar enters only a separate recognition set.
  All other states remain absent.
- The lexical retriever applies world/time/kind/subject/status and host allowlist predicates before
  scoring and limiting. It has stable token scoring and stable ID tie-breaking. It never receives
  unauthorized proposition text through the answer coordinator.
- Safe candidates contain trimmed display text, stance, presentation kind, and an internal
  revision. A secret-like source may map only to a neutral statement presentation through binding.
- The completion is no-tools and structured. Internal citations are validated and removed. Mixed
  stance/presentation citations, invented IDs, canonical-ID echoes, malformed output, and provider
  failure return the same bounded unknown surface.
- Policy, participation, binding, world clock, effective state, and document revisions are
  re-resolved after inference. One changed generation retries; a second returns generic stale.
- Every operation is read-only and produces no typed effects, game/event/notification/audit writes,
  cache authority, or derived persistent index.

## Failure, replay, and rollback contract

| Case | Result and no-change guarantee |
| --- | --- |
| Invalid request | Bounded unknown/invalid result; no policy-bypassing read. |
| Policy throws, denies, is malformed, or grants another campaign | Generic denial before binding/ECS/edge/model access. |
| Missing/mismatched binding, state space, campaign, world, participation, or clock | Generic denial/unavailable; no record details. |
| Malformed or cross-world knowledge graph | Record omitted; no partial statement or identifier leak. |
| Empty/hidden/unknown-only/no-match | Indistinguishable unknown result; no count leak. |
| Familiar-only match | Recognition text only; proposition text remains absent. |
| Model/provider/citation/prompt-injection failure | Generic unavailable/unknown; no fallback over unfiltered data. |
| Concurrent policy/state/document change | Retry once, then generic stale; no stale answer. |
| Cancellation/replay | No writes exist to duplicate or roll back. |

## Implementation sequence

1. Add and validate provider-neutral domain contracts and the unbound component registration seam.
2. Add the application-scoped canonical projector and effective-state resolver.
3. Add deterministic pre-limit allowlisted lexical retrieval and candidate coordination.
4. Add the bounded structured answer coordinator and post-inference recheck.
5. Add focused positive, negative, boundary, stale, and isolation tests.
6. Run focused tests, the full suite, and diff checks; record a receipt and stop at Slice 7D1.

## Acceptance matrix

| Case | Evidence required |
| --- | --- |
| Contract closure | Reflection proves the request has no identity, scope override, visibility, allowed-ID, or exact-record member. |
| Policy ordering | Denial/throw touches no binding, ECS, edge, lexical, or model dependency. |
| Canonical projection | Exact valid app/state-space records project; malformed, duplicate, cross-world, future, archived, and wrong-app records fail closed. |
| State precedence | Explicit content/familiar/unknown, applicable faction/region, world baseline, and outsider unknown match the owner. |
| Pre-limit authorization | A higher-scoring hidden record cannot crowd an allowed record out at limit one. |
| Safe projection | Actor results omit IDs/sensitivity/source kind/hidden count and preserve stance plus neutral presentation. |
| Completion hostility | Injection text, invented/mixed citations, ID echo, malformed output, and provider failure fail closed. |
| Freshness | Policy, participation, binding, state, clock, or document change retries once then fails stale. |
| Isolation | No canonical, event, notification, operation, information, or index-authority write occurs. |
| Registration | The opt-in component resolves with explicit fake policies/bindings and the generic host remains unchanged. |

## Verification commands

```powershell
dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --filter FullyQualifiedName~KnowledgeCore
dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore
git diff --check -- src/system/knowledge DantesRoleplay.DataAccess/DataAccessServiceCollectionExtensions.cs web STATUS.md
```

Catalog validation is not required because this slice changes no catalog artifact. A protocol walk
is not required because it adds no MCP surface or current dependency registration.

## Completion receipt and exit gate

Record delivered files, focused/full results, fail-before-read evidence, pre-limit filtering,
post-inference freshness, and deliberate exclusions in
`web/DND2024-WEB-UI-SLICE-7D0-RECEIPT.md`. Then mark 7D0 accepted and stop. Slice 7D1 remains a
separate confirmation/implementation boundary for a fixed loopback actor seat or authenticated
audience and the exact application knowledge binding.
