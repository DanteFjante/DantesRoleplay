# Knowledge and facts — Slice 6 readiness and Terra handoff

Status: **Slices 6A–6C implemented; a loopback development bridge exists, while production public exposure remains blocked by one external authentication owner**  
Prepared: 2026-08-21

## Outcome

Slices 6A–6C are complete without inventing identity, accounts, tokens, roles, or party state.
Production Slice 6D remains the blocked leaf: it binds the completed core to a real authenticated
transport principal. A separate, explicitly development-only loopback bridge is permitted solely
for local use and must not be treated as authentication.

The current MCP host is stateless and trusted-GM-only. It registers no ASP.NET authentication or
authorization middleware and exposes no claims principal to application services. Campaign Feature
5 independently records the same missing audience-policy owner. Active campaign character
participation proves only that a campaign offers an actor; its governing contract explicitly says it
is not player control or authentication.

## Reconciled first-generation scope

The already-approved knowledge semantics support world, containing-region, faction, and actor
knowledge. Party scope was explicitly deferred until an authoritative party/membership owner exists.
Slice 6 therefore proves these cases:

- authenticated GM for one campaign;
- authenticated principal controlling one named actor in that campaign;
- that actor inheriting world, current-region, or current-faction knowledge;
- explicit actor state, including an explicit `unknown` exception;
- an actor outside the applicable region/faction;
- missing, revoked, ambiguous, expired, or cross-campaign grants.

It must not reinterpret `party`/`gm` visibility labels, campaign references, active participation, or
the loopback network as authorization. Party-principal retrieval remains a successor until a party
owner exists. This corrects the broad Slice 6 exit wording without changing any approved catalog
meaning.

## Required external policy seam

Add a provider-neutral application contract. Its implementation reads ambient authenticated
transport context; the method deliberately accepts no principal, role, actor, or audience argument:

```csharp
public interface IAuthenticatedCampaignAudiencePolicy
{
    Task<CampaignAudienceResolution> ResolveAsync(
        string campaignId,
        CancellationToken cancellationToken = default);
}
```

A grant is closed evidence with:

```text
principalId      stable opaque identity from the authentication provider
campaignId       exact requested campaign
role             gm | actor
actorId          required only for actor; absent for gm
policyRevision   stable nonblank revision used for revocation/freshness evidence
```

Denial returns one generic external error and no grant. The resolver must fail closed for missing,
malformed, expired, revoked, ambiguous, or wrong-campaign identity. It rechecks revocation for every
request; Slice 6 introduces no authorization cache. An actor grant independently proves that the
principal controls that actor. Campaign participation may be checked afterward for campaign scope,
but can never create or upgrade the grant.

The public request contains `campaignId`, question, optional fact/subject/time filters, and bounded
result limits only. It contains no world ID, principal ID, actor ID, role, audience, include-hidden
flag, arbitrary allowed-ID list, policy revision, or GM override.

## Authorization and perspective rules

Authorization is evaluated before model use and before returning search candidates:

1. Resolve the ambient principal for the requested campaign. On denial, load no campaign/world or
   knowledge data and return the same generic denial regardless of campaign existence.
2. Resolve the campaign's one active world link. Reject malformed or mismatched scope without
   widening.
3. A GM grant may read canonical current/as-of knowledge in that campaign world.
4. An actor grant must match one active campaign participation as defense-in-depth. Then resolve
   `IKnowledgeStateCoordinator.ResolveAsync(actorId, knowledgeId)` for each candidate universe item.
5. Explicit actor state wins. Otherwise the existing current faction/region/world baselines apply.
   `unknown` always denies, including over a world baseline. Outsiders derive `unknown`.
6. Descriptive visibility and sensitivity never grant access and never override an effective state.

Actor presentation is intentionally not the trusted-GM record shape:

| Effective state | Search/answer behavior |
| --- | --- |
| `known` | May use the proposition text; return stance `known`. |
| `suspected` | May use the proposition text; return stance `suspected`, never canonical truth status. |
| `believed` | May use the claim text; return stance `believed`. |
| `doubted` | May use the encountered claim text; return stance `doubted`. |
| `disbelieved` | May use the encountered claim text; return stance `disbelieved`. |
| `familiar` | Do not return the proposition. A matching request may return only a generic “recognize the topic but lack details” unresolved result. |
| `unknown` | Exclude completely and reveal neither existence nor count. |

Actor results omit canonical knowledge IDs, sensitivity, source kind, model candidates, hidden counts,
and canonical `fact`/`secret` truth labels. They may expose only statement text, stance, and a safe
presentation kind: `statement`, `rumour`, or `evidence`; canonical `secret` maps to `statement`.
Internal citations retain canonical IDs for validation and diagnostics but are not serialized to the
actor. GM application services may retain the existing detailed trusted shape.

## Retrieval design: authorize before ranking

Post-filtering the top hybrid results is forbidden: hidden records could crowd visible records out,
making results incomplete, and candidate metadata could reach the model before authorization.

For the first player-safe implementation:

1. Read the canonical world knowledge IDs after policy grant and campaign/world scope validation.
2. Resolve effective state for every ID and build a host-only allowed-ID set. Keep familiar IDs in a
   separate recognition set; do not include their proposition documents.
3. Extend the internal FTS request/index boundary with a host-owned allowed-ID constraint. Apply it
   in SQL before `ORDER BY`/`LIMIT`, preferably through one bounded JSON parameter joined with
   `json_each`, not one parameter per ID.
4. Hydrate and reauthorize every returned ID before use. A state or policy revision change discards
   the result and retries at most once from the beginning; a second change returns a generic stale
   result.
5. Use lexical retrieval only for actor requests initially. The current sqlite-vec KNN query limits
   by world before it can constrain arbitrary allowed IDs, so post-filtered vector results cannot
   satisfy exact visible-only ranking. GM paths may retain hybrid search. Add actor vector search
   only after the vector index proves an allowed-ID predicate is applied before `k`.
6. Supply only authorized, perspective-safe candidates to Mode A/B. Generated Mode B searches keep
   the same immutable policy grant, campaign/world scope, allowed set, time, and filters.

No player request may use exact canonical knowledge-ID lookup in the first public slice. No-result,
unknown-only, hidden-only, wrong-world, and nonexistent searches return the same bounded unknown
shape. Search mode, hidden candidate count, and fallback internals are not returned to actors.

## Implementation slices

### 6A — policy and result contracts

Add provider-neutral types under `DantesRoleplay/Security/` and the closed authorized request/result
types under `DantesRoleplay/World/`. Do not register a permissive default policy. A host lacking
`IAuthenticatedCampaignAudiencePolicy` cannot resolve the authorized service.

Tests use a fake policy and prove the service calls policy first. They also prove no request member
can carry principal, actor, role, audience, world, allowed IDs, or include-hidden behavior.

**Exit:** grant/denial, role/actor invariant, policy revision, generic external errors, and
fail-before-data-read behavior are fixed and tested.

### 6B — authorized lexical candidate set

Add an internal candidate resolver using `IKnowledgeSearchDocumentSource`,
`IKnowledgeStateCoordinator`, campaign world scope, and the existing campaign participation verifier.
Add the FTS allowed-ID predicate before limit; do not change the canonical database schema. The FTS
projection is derived and rebuildable.

**Exit:** world baseline, explicit unknown exception, current region, current faction, explicit
non-unknown state, outsider, cross-world, stale state, and familiar-only cases return exactly the
allowed set. Hidden records cannot displace visible hits.

### 6C — perspective-safe answering

Add an authorized answer coordinator that consumes only 6B candidates. Reuse the strict structured
completion adapter and internal citation checks, then project to the actor-safe shape. Capture the
policy revision and state fingerprints before the model call and recheck after it. No answer is
returned when inputs became stale.

**Exit:** adversarial candidate text, invented citations, kind changes, model identity drift,
unavailable Ollama, state revocation during completion, and every epistemic state fail or project as
specified. The complete call writes no game, discovery, cache, event, notification, or audit row.

### 6D — authenticated public integration (blocked leaf)

After a real provider implements the policy seam, bind it through ASP.NET authentication and expose
one narrow authorized read through the chosen transport. Then add/revise the permanent procedure and
public query ID at that explicit synchronization boundary, update `VerbSurface`, audit every other
read route, run the protocol walk, and prove denial before data access.

Do not add an MCP/HTTP endpoint, procedure ID, manifest entry, or permissive development identity in
6A–6C. Advertising a public operation before authentication exists would create a bypass by design.

## Bypass audit list

The implementation and acceptance review must cover every current knowledge-bearing path:

- `IKnowledgeSearchDocumentSource.ReadAsync/ReadWorldAsync`;
- `IKnowledgeTimelineCoordinator.ReadAsOfAsync`;
- `IKnowledgeStateCoordinator.ResolveAsync`;
- `IKnowledgeLexicalSearchCoordinator.SearchAsync`;
- `IKnowledgeHybridSearchCoordinator.SearchAsync` and exact-ID handling;
- `IKnowledgeFactAnswerCoordinator.AnswerAsync`;
- `IKnowledgeReadAgentCoordinator.AnswerAsync`;
- campaign continuity/resume, quest, graph, entity, history, and future website/API projections that
  can contain knowledge summaries or GM context;
- logs, errors, model prompts, background proposals, and diagnostics.

Existing interfaces remain explicitly trusted-host/GM-only. They must not be injected into a
player endpoint. The authorized service is a separate closed surface; it does not weaken or silently
reinterpret the trusted interfaces.

## Acceptance matrix

| Case | Required outcome |
| --- | --- |
| GM grant | Scoped canonical result; no cross-campaign/world data. |
| Actor + world baseline | Safe proposition unless explicit `unknown`. |
| Actor + current region/faction | Safe inherited proposition; outsider receives generic unknown. |
| Explicit suspected/believed/doubted/disbelieved | Content with stance; no canonical truth/secret metadata. |
| Familiar | Generic recognition only; no proposition/ID. |
| Missing/revoked/expired/wrong-campaign grant | Same denial before game reads. |
| Hidden-only/no-match/nonexistent | Same actor unknown shape; no hidden count or ID. |
| Policy/state changes during request | Discard; one bounded restart, then generic stale result. |
| Prompt injection or invented citation | Fail closed; no extra read or content. |
| Vector enabled globally | Actor path remains correctly allowed-ID lexical until pre-k filtering exists. |
| All public read routes | No alternate entity/graph/campaign/website route exposes hidden knowledge. |
| Isolation | Zero game/auth/cache/event/notification writes. |

## One remaining external decision

Choose and implement the authentication owner that supplies the ambient principal and campaign
grant. Acceptable implementations include an existing identity provider/claims adapter or a
separately governed local credential service with explicit membership, actor-control, revocation,
and policy revision. Loopback location, an MCP connection, a campaign reference, or character
participation alone is not acceptable.

Once that provider is named, 6D and Campaign Feature 5 can share the same policy seam. Until then,
6A–6C are safe implementation work and 6D must remain absent.
