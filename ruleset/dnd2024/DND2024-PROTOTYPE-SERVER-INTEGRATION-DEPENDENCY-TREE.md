# DND2024 prototype-server integration dependency tree — one authoritative game system

Status: **active; campaign/character/current continuity projection verified**
Ruleset alignment: **dnd2024-compatible**
Source: repository-owned server, catalog, and prototype contracts; no D&D rule meaning changes
Owning roadmap: `ruleset/dnd2024/ROADMAP.md`, runtime-to-prototype ECS convergence lane

## Outcome and non-goals

Make the D&D 2024 prototype web app a read-only/action-requesting companion of the existing
server. Durable campaign state remains in SQLite, authored application contracts and content remain
in `catalog/`, and JavaScript catalog mechanics remain the only rule implementation.

This work does not make `prototype/dnd2024` a second catalog, import the Eldervale fixture as a
live campaign, add D&D vocabulary or rule logic to C#, or permit a browser to call a privileged
MCP endpoint directly.

## Existing owners and evidence

| Concern | Owner | State | Evidence |
| --- | --- | --- | --- |
| Authoritative D&D application contracts/content/mechanics | `catalog/applications/dnd2024/` | verified | 76 component contracts and 103 authored content records |
| Existing generic campaign/world model | `catalog/components/game.core.*` | verified | world root/location/faction/fact/map-anchor and campaign chapter/arc/session/participation owners |
| Durable campaign/world state | SQLite through generic ECS/state-space services | verified | `query(kind: "world" | "entities" | "graph")` |
| Generic chat/system transport | `DantesRoleplay.MCPServer` | verified | `/mcp`, public tools `orient`, `query`, and `commit` |
| Same-origin web companion host | `MapDantesRoleplayWeb` | verified | `/ui/dnd2024-play` plus generic application-state and mechanic endpoints |
| Player/campaign binding | `system.audience-context` | verified | ambient configured seat; no caller-selected identity |
| Existing prototype companion | `prototype/dnd2024/app/api/hub/route.ts` | verified | projects the separate Eldervale in-memory fixture |
| Target ECS model | `prototype/dnd2024/schemas/components/` | provisional | 154 component schemas and 2,329 records |
| Contract convergence | `prototype/dnd2024/planning/DND2024-COMPONENT-CONVERGENCE-DEPENDENCY-TREE.md` | active | prototype and catalog are explicitly not wire-compatible |
| Remote security boundary | `McpPrivateOperatorAuthorizer` | verified | privileged MCP access is direct loopback only |
| Hosted prototype runtime | `prototype/dnd2024/.openai/hosting.json` | verified | Cloudflare-hosted server runtime cannot reach a user's local loopback service |

## Dependency tree

```text
Prototype companion reads and requests authoritative server play [planning]
├── One canonical ECS contract per concept [active: component convergence]
│   ├── Map the 154 provisional prototype schemas to current catalog owners [planned]
│   ├── Retire or migrate superseded catalog owners without dual writes [planned]
│   └── Select reviewed authored records; do not import fixture state [awaiting confirmation]
├── Existing-campaign read binding [verified]
│   ├── Resolve the host-authorized existing campaign, state space, and actor [verified: `GET /api/audience-context`]
│   ├── Reuse existing world root/location/faction/fact/map-anchor owners [verified]
│   ├── Map only server-owned state into the companion read model [partial: campaign root and character record]
│   └── Bind the selected campaign to its world and player knowledge [planned]
├── Server-owned companion projection [partial]
│   ├── Bounded player-knowledge notebook contract [verified: existing `IAuthorizedKnowledgeNotebookReader`]
│   ├── Read only generic state/projection APIs and catalog-owned definitions [partial: notebook only]
│   └── Prove no secret data crosses a player boundary [verified for notebook adapter]
├── Server-served prototype route [ready]
│   ├── Serve the prototype through the existing `/ui/{id}` page/bundle route [ready]
│   └── Use same-origin generic application-state and mechanic APIs [ready]
├── Existing World/Campaign context selection [verified]
│   ├── Discover exact readable campaign roots inside the authorized state space [verified]
│   ├── Group campaigns under their existing World identity without copying World state [verified]
│   └── Revalidate every browser-requested campaign on the server-side adapter [verified]
├── Optional external hosted deployment [planned]
│   └── Remote, authenticated relay/gateway for hosted prototype use [new public surface]
└── Prototype adapter [partial]
    ├── Existing-campaign context and direct campaign/actor reads [verified]
    ├── Campaign root and character-record projection [verified]
    ├── Audience-filtered campaign/world knowledge [verified]
    ├── Player-safe known-location directory [verified: active locations named only through admitted knowledge]
    ├── Detailed locations/maps/factions projection [planned]
    └── Send action requests through the existing command path; never mutate local fixture state
```

## Conflicts and decisions

1. The current hub envelope describes a bespoke Eldervale world, maps, secrets, and party. It is
   development fixture data only and must not be imported as a campaign. The companion instead
   reads the host-authorized existing campaign from the server. The server already owns compatible
   generic world locations, factions, facts, map anchors, campaign chapters, arcs, sessions, and
   participation.
2. The existing server is correctly application-neutral: it offers generic state/catalog queries
   and an MCP command channel. It must not gain a D&D-specific `hub` endpoint.
   Its generic relationship/component explorers are structure tools, not a player-information
   projection: a detailed player view must not enumerate them and filter in the browser. The next
   projection seam must apply the server-owned knowledge audience before a record is returned.
3. The prototype can instead be served by this server at `/ui/{id}`. In that arrangement its
   browser requests are same-origin calls to the existing generic web API; they do not use `/mcp`.
   A separately hosted prototype still cannot call the local-only privileged MCP connection without
   a separately authorized remote gateway.
4. The active component-convergence plan records that the prototype and current catalog do not
   share component IDs. A bulk schema/record import before a reviewed crosswalk would duplicate
   component authority and bypass the current migration policy.

## Fixture-to-server read-model crosswalk

| Fixture area | Canonical owner | Disposition |
| --- | --- | --- |
| World summary and lifecycle | `game.core.world.root` | reuse; entity name supplies the world name |
| Locations, regions, visibility, and summaries | `game.core.world.location` plus containment | reuse; fixture-only description/atmosphere/landmarks need a content/knowledge representation |
| Location pins | `game.core.world.map.anchor` | reuse; normalize the fixture's percentage coordinates to the existing 0–1000 integer range |
| Routes | `game.core.world.route` and relationships | reuse after fixture route links are mapped to directed edges |
| Factions, goals, methods, agendas, and membership | `game.core.world.faction` and relationships | reuse; influence/monogram are presentation gaps |
| Public/party/GM world knowledge | `game.core.world.fact`, `rumour`, `secret`, `clue`, classification, validity, and knowledge-state relationships | reuse; `playerKnown` becomes server-authorized knowledge state, never a browser flag |
| Campaign chapter, arcs, sessions, and player membership | existing campaign chapter/arc/session/recap/checkpoint/participation owners | reuse; restore the retained `campaign.root` owner through reviewed source activation rather than a C# special case |
| Map documents, layers, nested map scopes, and image variants | none | missing owner; define as application data before import |
| Campaign overview, outcomes, place visits, threads, overlays, and milestones | no complete current owner | missing owner; split by lifecycle rather than copying the dashboard object |
| NPC appearance/identity and holdings | creature identity/appearance and generic containment/item owners | map after those active contracts are confirmed; do not embed an NPC list in locations |
| Lore/history entries and their cross-links | knowledge entities and relationships | map to facts/rumours/secrets with provenance, classification, validity, and explicit subject links |

The fixture contains 7 locations, 5 factions, 9 history entries, 10 lore entries, 9 maps, 6
campaign-log entries, 5 visits, 5 outcomes, 4 quests, 4 threads, 5 clues, 7 map overlays, 3 party
summaries, and 3 rules summaries. It is a UI-shape reference only, not a seed campaign or a
generic UI document.

## Ordered leaves

| Order | Leaf | Depends on | Exit gate |
| --- | --- | --- | --- |
| 1 | Select the first companion projection | convergence owners | fields, visibility, source components, empty/denied behavior, and no-leak tests are declared |
| 2 | Resolve the existing campaign through a generic web-safe audience binding | leaf 1 | **verified:** `GET /api/audience-context` has no parameters and returns only the existing validated binding or an empty denial |
| 3 | Complete the fixture-to-server read-model crosswalk | leaf 2 | every displayed value is server state, catalog content, or deliberately unavailable; no fixture fallback. **Partial:** campaign premise/goals and active character entries read from their server components. |
| 4 | Implement the generic server projection seam | leaf 3 | **partial:** the prototype consumes the existing identity-free authorized notebook; no D&D-specific C# logic or new route. Locations/maps/factions remain separate projections. |
| 5 | Serve/connect the prototype through the existing page/bundle route | leaf 4 | fixture is not used when connected; browser reads same-origin generic APIs |
| 5A | Project current Campaign chapter and arc into the live companion | leaf 5 and existing chapter/arc owners | **verified:** overview and Open Threads use stored chapter/arc fields; closed chapter and terminal arc summaries feed their existing pages; unsupported visits/clues remain empty and Player receives no GM context |
| 5B | Select an existing authorized World/Campaign context | leaf 5 and accepted Web UI Slice 5A discovery pattern | **verified:** the TopBar popup lists only exact readable campaign roots grouped under their existing World; a local DM can switch the complete hub context, an actor remains bound to its authorized campaign, injected IDs fail closed, and all reads are side-effect free. |
| 6 | Optionally host the prototype separately | leaf 5 | remote identity and gateway are explicitly authorized |

## Lowest ready leaf

The prototype's server-only adapter now resolves the existing host-selected campaign, then reads
only its campaign root and bound actor's temporary character record in addition to their entity
identity. It also consumes the existing authorized, identity-free knowledge notebook and renders
only server-filtered entries without an Eldervale fallback. The notebook additionally provides a
safe directory of active locations only where a non-familiar admitted entry explicitly concerns
that location; it exposes a name and the already-admitted entries, never an ID, summary, route, or
world graph. The next implementation leaf maps detailed locations, maps, and factions into companion
views. A separately hosted slice remains optional and needs a generic authenticated relay/gateway;
it cannot reuse the loopback-only operator path.

## Confirmation gates

- No live campaign import is part of this integration. A later schema or source migration still
  requires its own confirmed crosswalk and database export.

## Planning receipt

- Runtime artifacts created: none.
- Live database changes: none.
