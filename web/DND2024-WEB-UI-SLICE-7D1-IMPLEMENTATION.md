# D&D 2024 web UI Slice 7D1 implementation — fixed local player seat

Status: **accepted 2026-08-27**
Owner/roadmap: [Knowledge and facts roadmap](../knowledge/KNOWLEDGE_AND_FACTS_PLAN.md),
supporting [Web interface roadmap](WEB-INTERFACE-ROADMAP.md) Feature 5
Dependency tree/leaf: [D&D 2024 web UI dependency plan](DND2024-WEB-UI-DEPENDENCY-PLAN.md),
Order 7D1 / F2
Ruleset alignment: **ruleset-neutral host policy with catalog-owned D&D vocabulary**
Source ID and locator: the existing activated `dnd2024` source set plus the new generic application
metadata locator `catalog/applications/{applicationId}/metadata/authorized-knowledge.json`.
Outcome: the private host can resolve exactly one loopback-only local player grant for Orban in
`campaign.thalorien.brackenford`, then bind that campaign to the exact activated D&D application
vocabulary and independently prove active character participation.
Exclusions: knowledge-state creation/import, inferring what Orban knows, GM grants, request-selected
actor/role/application/state space, public authentication, remote/Tailscale access, MCP/HTTP/query
surface changes, browser UI, maps, images, migrations, and retained `old-dnd` runtime dependencies.
Allowed files/areas: `src/system/knowledge/`; a generic activated-document read seam in the current
application-activation/catalog-navigation owners; D&D application metadata; private host
configuration/registration; focused tests; this document and status/roadmap/dependency summaries.
Stop point: stop after loopback/revocation/cross-campaign/binding/participation/registration evidence
passes. Do not author or import a baseline or actor knowledge relationship; that is Slice 7D2.

## Confirmed decisions

- The user's instruction to continue confirms Order 7D1 and the fixed local Orban player seat.
- The fixed campaign is `campaign.thalorien.brackenford`; the fixed actor is
  `actor.thalorien.brackenford.orban`; the application is `dnd2024`.
- The seat role is always `Actor`. No configuration or request may elevate it to game master.
- Browser campaign, character, entity, or tab selection is presentation state only and never feeds
  the audience policy.
- The temporary policy is private development infrastructure, not authentication. It grants only
  when the current HTTP peer is loopback and the exact configured campaign is requested.
- Ruleset vocabulary belongs in activated application metadata. Generic C# contains no `dnd2024`,
  `game.core`, campaign, or Orban identity.
- The application metadata format/locator is a new permanent application surface accepted for this
  slice: `system.knowledge.binding.v1` at the application metadata locator above.

## Outcome and boundary

For each well-formed authorized knowledge request, the current host:

1. reads the current ambient HTTP connection and current fixed-seat configuration;
2. denies unless the peer is loopback, the seat is enabled, and the requested campaign is exact;
3. issues an actor-only grant for the fixed principal/campaign/actor with a deterministic revision;
4. loads the exact active application `metadata/authorized-knowledge.json` winner,
   verifying retained source registration, path containment, byte length, and SHA-256 fingerprint;
5. locates exactly one registered state space for that application containing the exact campaign
   root described by the metadata;
6. validates all knowledge, campaign, participation, faction, location, relationship, JSON-field,
   status, state, and presentation vocabulary without embedding it in the kernel; and
7. independently proves one active campaign-participation entity linking that campaign to the fixed
   actor before any canonical knowledge document is returned.

This slice makes the private authorized core resolvable by the current host. It adds no caller-
reachable answer surface.

## Authoritative state and ownership

| Concern | Authority |
| --- | --- |
| Enabled local seat, principal, application, campaign, actor | Trusted host configuration, reread for every request |
| Peer locality | Current server-side HTTP connection; forwarded/user-supplied identity is ignored |
| Ruleset/application vocabulary | Exact fingerprinted active application metadata winner |
| Application/state-space association | Current application/state-space registry |
| Campaign, actor, participation, and links | Current application-scoped ECS and relationship stores |
| What the actor knows | Existing knowledge state/baseline owners; unchanged and still empty until 7D2 |

The metadata document is descriptive application binding, not campaign game state. It declares no
campaign, actor, principal, known fact, or baseline identity.

## Runtime artifacts

| Artifact | Meaning |
| --- | --- |
| `IActivatedApplicationDocumentReader` | Generic fail-closed reader for one exact active text winner with retained registration/path/hash verification. |
| `KnowledgeApplicationBindingDocument` | Strict `system.knowledge.binding.v1` parser/validator for application-owned vocabulary. |
| Activated knowledge binding resolver | Resolves the configured application, exact campaign, and unique matching state space only after the audience grant. |
| Application actor participation verifier | Proves campaign-to-participation-to-actor structure and active status using binding-supplied vocabulary. |
| Fixed local audience policy | Actor-only, loopback-only, exact-campaign ambient grant with per-request configuration and deterministic policy revision. |
| Host registration | Registers the explicit policy/binding/participation owners and the accepted authorized knowledge core, without adding a tool or route. |

No table, migration, component type, query kind, mechanic, procedure, entity fixture, relationship,
event, notification, or public endpoint is added.

## Binding document shape

The metadata document has closed top-level `format`, `applicationId`, and `binding` members. The
binding contains the exact fields already required by `KnowledgeApplicationBinding` plus campaign
participation vocabulary. It intentionally omits state-space, campaign, actor, principal, and
audience values; those are resolved from host configuration and current state.

All members are required, bounded, case-sensitive strings or bounded unique arrays. Unknown fields,
duplicate knowledge kinds, invalid/disjoint epistemic states, unsupported format/application,
missing active winner, file drift, or multiple campaign-bearing state spaces fail closed.

## Failure, replay, and rollback contract

| Case | Result and no-change guarantee |
| --- | --- |
| Disabled/malformed/revoked seat | Generic denial before binding or ECS reads. |
| Missing HTTP context, non-loopback peer, remote/Tailscale peer | Generic denial before binding or ECS reads. |
| Wrong campaign, browser-selected actor/role, or caller identity text | Generic denial; caller values cannot alter the fixed seat. |
| Missing/inactive/drifted/invalid metadata | No binding; generic denial/unavailable with no path or vocabulary leak. |
| Unknown application/state space/campaign, zero or multiple campaign matches | No binding; no cross-application or cross-campaign fallback. |
| Missing, withdrawn, malformed, duplicated, cross-campaign, or wrong-actor participation | Denial before canonical knowledge reads. |
| Configuration/activation/state revision changes | Policy/scope revision changes; the accepted 7D0 freshness recheck discards stale work. |
| Cancellation/replay | No writes exist to duplicate or roll back. |

## Implementation sequence

1. Extend the generic binding contract with campaign-participation vocabulary.
2. Add an exact verified active-document reader and strict knowledge-binding document parser.
3. Add the unique state-space binding resolver and application participation verifier.
4. Add the loopback-only fixed actor policy and explicit current-host registration/configuration.
5. Author the D&D activated metadata document and update development configuration from the stale
   GM/old-campaign values to the exact Orban actor seat.
6. Add focused identity, ordering, revocation, drift, ambiguity, participation, registration, and
   ruleset-neutrality tests.
7. Validate the catalog, run focused/full tests and the protocol walk, record the receipt, then stop.

## Acceptance matrix

| Case | Evidence required |
| --- | --- |
| Policy-first | Disabled, malformed, remote, and wrong-campaign requests touch no binding/ECS dependency. |
| Fixed actor | Exact loopback campaign request grants Actor/Orban; role and actor are absent from request input. |
| Revocation | Disabling or changing the configured seat changes the next request without stale grant reuse. |
| Activated metadata | Only the exact active winner with matching registration/path/length/hash parses. |
| Vocabulary ownership | No D&D/game/campaign/Orban identity occurs under generic `src/system/knowledge` C#. |
| Campaign binding | Exactly one current application state space must contain the exact active campaign root. |
| Participation | Exact active campaign-participation-actor graph grants; withdrawn/missing/duplicate/wrong links deny. |
| Cross-scope | Another application, state space, campaign, or actor never grants or supplies a binding. |
| Host registration | Current host resolves the candidate service with explicit owners; no new MCP/HTTP route appears. |
| Isolation | Database game/event/notification/operation/information counts remain unchanged. |

## Verification commands

```powershell
dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --filter FullyQualifiedName~KnowledgeBinding
dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --filter FullyQualifiedName~LocalKnowledgeAudience
dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore
dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --filter FullyQualifiedName~ProtocolWalk
dotnet run --project DantesRoleplay.MCPServer -- roleplay validate catalog
rg -n "dnd2024|game[.]core|thalorien|orban" src/system/knowledge -g "*.cs"
git diff --check -- src/system/knowledge src/system/application-activation src/system/catalog-navigation catalog/applications/dnd2024/metadata DantesRoleplay.MCPServer web STATUS.md
```

Use the repository's actual catalog-validation command discovered from the current CLI rather than
assuming the illustrative `dotnet run` form above. A live application reactivation is a separate,
explicit synchronization boundary and is not implied by catalog validation.

## Completion receipt and exit gate

Record delivered artifacts, catalog/focused/full/protocol results, fail-before-read evidence,
current-host resolution, and deliberate exclusions in
`web/DND2024-WEB-UI-SLICE-7D1-RECEIPT.md`. Mark Order 7D1 accepted and stop. Slice 7D2 remains the
separate reviewed synchronization boundary that may record what Orban actually knows.
