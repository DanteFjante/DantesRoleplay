# D&D 2024 prototype audience-projection implementation — player knowledge panel

Status: accepted
Owner/roadmap: `ruleset/dnd2024/ROADMAP.md`
Dependency tree/leaf: `DND2024-PROTOTYPE-SERVER-INTEGRATION-DEPENDENCY-TREE.md` / server-owned companion projection
Ruleset alignment: dnd2024-compatible
Source ID and locator: not applicable; this slice does not implement a D&D rule
Outcome: the connected prototype displays only the existing server's player-safe campaign/world knowledge beside its already-bound campaign and character summary.
Exclusions: new C# D&D endpoint, browser state enumeration, direct relationship browsing, world-map/location/faction dashboard projection, mutations, rules logic, catalog/data changes, and fixture fallback.
Allowed files/areas: prototype server adapter, connected-page read model/rendering, prototype tests, the existing integration dependency tree, and this document.
Stop point: consume only the existing generic authorized knowledge endpoint after ambient context binding; do not broaden its payload or create a new public route.

## Confirmed decisions

- The user requested a server-owned, audience-filtered campaign/world projection on 2026-08-29.
- The server stays application-neutral. `IAuthorizedKnowledgeNotebookReader` and its existing web route are the projection owner; no D&D-specific C# hub API is introduced.
- The server route obtains the campaign ID solely from `GET /api/audience-context`, never from browser input. It renders no knowledge bytes when the authorized projection is unavailable or malformed.

## Prerequisite evidence

- `catalog/procedures/game/core/world/procedure.game.core.world.knowledge.md` defines the one player-safe knowledge-query boundary and forbids visibility flags as authorization.
- `catalog/applications/dnd2024/metadata/authorized-knowledge.json` binds the D&D application to generic campaign/world/knowledge owners.
- `AuthorizedKnowledgeNotebookReader` checks ambient audience, activated binding, active participation, scoped world, effective actor knowledge state, validity, and hydration before yielding identity-free entries.
- `GET /api/applications/{applicationId}/campaigns/{campaignId}/knowledge` already maps that reader and has the normal web read boundary/rate limit.
- The preceding bound-audience slice already returns the host-selected application, campaign, state space, and actor only after validation.

## Runtime artifacts

No new route, permanent ID, component, mechanic, schema, or transaction is created. The prototype server adapter adds one read to the existing knowledge route using its already-bound application and campaign ID. Its closed UI projection contains at most 100 entries, each with only `text`, `stance`, and `presentationKind`.

## Authoritative state and closed input

The server owns campaign selection, audience role, actor, application binding, world scope, actor participation, knowledge state, and knowledge text. The prototype supplies no query string, kinds, subject IDs, as-of time, campaign ID, actor ID, world ID, visibility override, record ID, or include-hidden flag. It receives only the server's closed notebook envelope after its existing bound context is resolved.

## Behavior, failure, replay, and rollback contract

1. Resolve the local server origin and ambient audience context exactly as the previous slice.
2. After a `bound` context, read the existing campaign/actor summary components and request the existing knowledge endpoint with that bound application/campaign pair.
3. Accept only `ready` or `empty` knowledge responses with bounded identity-free entry fields. Present `ready` entries in server order; `empty` has no entries.
4. A denied, unavailable, malformed, or transport-failed knowledge response renders no entries and the connected summary states that knowledge is currently unavailable. It never falls back to Eldervale or a browser-side world graph.

The slice is read-only. It creates no effects, transaction, replay token, cacheable authoritative state, or rollback work. The existing server projection owns validity and actor-state rechecks.

## Acceptance matrix

| Case | Expected result |
| --- | --- |
| Bound actor with ready knowledge | identity-free authorized entries appear beside the connected campaign/character summary |
| Bound actor with empty knowledge | connected summary appears with an explicit empty-state message |
| Knowledge 403/503/malformed response | no entry is exposed; connected summary identifies the unavailable projection |
| Audience denial before binding | no entity, component, or knowledge request follows |
| Browser-supplied campaign or actor | ignored; no browser request field selects projection scope |
| Secret/unrecognized knowledge | absent because the server reader filters it before response |
| Eldervale fixture unavailable | no fallback or displayed fixture data |

## Verification commands

```text
node --test test/game-server-context.test.js
npm test
npm run build
dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore --filter "FullyQualifiedName~SystemAudienceContextToolsTests|FullyQualifiedName~InteractionPlanningTests|FullyQualifiedName~InteractionQueryTests|FullyQualifiedName~InteractionOrchestrationAcceptanceTests"
```

## Completion receipt and exit gate

Record focused verification and deliberate exclusions in `ruleset/dnd2024/evidence/DND2024-PROTOTYPE-AUDIENCE-PROJECTION-RECEIPT.md`. Stop after the prototype consumes the existing safe notebook projection; detailed locations/maps/factions remain a separate, explicitly audience-projected slice.
