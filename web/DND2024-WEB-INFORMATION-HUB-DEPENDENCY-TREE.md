# DND2024-WEB-INFORMATION-HUB dependency tree — shared World, Campaign, Party, Current View, and Rules browser

Status: **planning active; shared shell and audience foundation accepted**

Ruleset alignment: **dnd2024-compatible**

Source: **not applicable**. The Rules branch may present only reviewed records that retain their
exact `source.dnd2024.srd-5.2.1` citations; this interface does not define D&D rules.

Roadmap owner: [Web Interface Roadmap, Feature 5](WEB-INTERFACE-ROADMAP.md#feature-5--dd-2024-player-and-gm-workspace)
Parent plan: [complete-campaign dependency graph](../ruleset/dnd2024/DND2024-COMPLETE-CAMPAIGN-DEPENDENCY-GRAPH.md)
Plan role: **subordinate UI/audience subgraph; remaining ordering does not select the next leaf independently**

## Outcome and non-goals

Deliver one responsive, information-first D&D page with the same five main tabs in both
perspectives, in this order:

1. **World**
2. **Campaign**
3. **Party**
4. **Current View**
5. **Rules**

An upper-corner **DM / Player** control changes only the authorized projection. Navigation,
selection, layout, and the meaning of each screen remain stable. DM adds secret facts, GM context,
hidden motives, unrevealed locations, and DM-only container contents. Player responses omit those
records before transport; CSS, client filtering, and a browser query parameter are never the
security boundary.

The page is a direct reference tool. It does not expose raw entity IDs, revisions, containment
terminology, protocol history, JSON, developer controls, or an LLM prompt surface. It performs no
hidden LLM request when a tab opens. The initial delivery is read-only and does not create or edit
worlds, campaigns, characters, encounters, inventory, or rules.

World is the durable parent context. Campaigns reference a world rather than own a copy:

~~~text
World — persistent state, locations, people, knowledge, map, holdings, chronology
├─ referenced by Campaign A — chapters, sessions, outcomes, participation
└─ referenced by Campaign B — a later campaign in the same changed world
~~~

A new campaign in an old world must resolve the existing world ID and link to it. It must never
clone the world. A campaign-caused world mutation belongs to the world transaction owner, so a
later campaign observes that changed world. Historical campaign summaries remain campaign-owned.

The first release explicitly excludes an interactive tactical grid, encounter authoring,
character creation, world/campaign editing, speculative travel calculations, arbitrary catalog
browsing, and copied Baldur's Gate 3 art or layout. The Party view may use a cinematic party-RPG
interaction pattern—portrait roster, focused dossier, strong vitals and equipment hierarchy—using
this project's own design and assets.

## Information architecture

| Main tab | Shared structure | Player projection | DM additions |
| --- | --- | --- | --- |
| **World** | Overview; Map; History; Locations; People; Factions; Lore. A selected location opens Details and People & Creatures. | Only authorized known/current locations, safe lore, safe people/factions, and a player-safe map base. | Full location index, secret lore, hidden factions/motives, unrevealed locations, and a **Holdings** view for chests/containers. |
| **Campaign** | Overview; Adventure Log; Places Visited; Outcomes. | Party-facing premise, active question/stake, safe recaps, known outcomes, and authorized visited places. | `gmContext`, private outcomes, unresolved threats, and hidden campaign notes once an owner exists. |
| **Party** | Portrait roster; selected-character Overview, Sheet, Knowledge, Backstory, Origin, and Inventory. | Shared party summaries plus the ambient player's permitted detail. | Authorized private character context and the same character views for the full active roster. |
| **Current View** | One adaptive scene surface: Exploration, Conversation, or Combat. | Safe description, observations, co-present people, current encounter data, declared affordances, and known travel choices. | Hidden scene facts, secret NPC intent, unrevealed combat/context data, and private observations. |
| **Rules** | Searchable topics, categories, concise summaries, detail, and source attribution. | Reviewed player-facing SRD reference content. | The same rules content; rules do not become secret merely because DM is selected. |

Location detail is one reusable route/view reached from the map, location browser, history,
campaign places, current scene, or an NPC card. It does not become a competing copy of location
state. Character detail follows the same rule for Party, Current View, and Campaign links.

For an authenticated DM, the corner control offers **DM** and **Player preview**. For an
authenticated player, the control may report Player perspective but can never self-elevate to DM;
changing browser storage or request parameters must not change that capability. The existing
owner-only local prototype may simulate both views only while it contains no real secrets.

## Existing owners and evidence

| Concern | Owner | State | Evidence |
| --- | --- | --- | --- |
| Current React table | `src/system/web-interface/dnd2024` | `ready` for presentation changes | The canonical server-hosted source owns presentation only and explicitly owns no persistent authority. |
| World identity and reusable location hierarchy | `game.core.world.root`, `game.core.world.location`, containment, `procedure.game.core.world.location` | `verified` | World roots and locations are campaign-independent; containment owns hierarchy and actor presence. |
| Existing-world campaign link | `procedure.campaign.create` and `game.core.campaign.in-world` | `conflicting` in the current runtime | The active catalog procedure says creation links to an existing world and copies nothing, but the former specialized campaign commit tools are excluded from the current MCP build and no active authored campaign-root component definition was found. The contract may not be advertised as a working create flow until restored under current application ownership. |
| World overview and location detail | `procedure.game.core.world.read` plus exact application/state reads | `verified` for trusted GM reads; `planned` for Player | Current graph recipes are trusted-GM material. A bounded audience-safe location projection is still required. |
| Player-safe knowledge and secrets | authorized knowledge core plus the D&D knowledge binding | `verified` for the fixed actor seat | Web Slices 7D0–7D3 prove actor-scoped knowledge and secret exclusion. The host currently lacks a general non-escalating DM/Player session policy. |
| Geographic placement | `game.core.world.map.anchor` and `procedure.game.core.world.spatial` | `verified` as display coordinates only | Anchors are normalized display placement. They do not establish discovery, topology, routes, distance, terrain, or movement. |
| Current location and co-present people | exact parent/direct `presence` containment and Web Slice 7C | `verified` structurally; `planned` for general audience safety | Current place and bounded people can be read without browser-wide scans. Location summaries still require the same audience filtering as every other field. |
| NPC presentation | entity identity, presence, faction relationships, world motive | `planned` | Presence and a short motive summary exist. A reusable NPC biography/background contract does not; creature/stat-block coverage is not a generic world-person owner. |
| Character roster and sheet | campaign character participation, `mechanic.dnd2024.character-sheet.read`, existing character UI | `verified` below an audience projection | Active participation can define the roster. The canonical sheet aggregate exists; current browser routes are primarily private/operator scoped. |
| Character Backstory and Origin | `dnd2024.character.profile`; `dnd2024.character-creation-record` and background content | `verified` as distinct facts | Biography/appearance/pronouns and mechanical background selection have different owners and must remain separate in presentation. |
| Character inventory and location containers | containment, `mechanic.dnd2024.inventory.read`, accepted nested inventory UI | `verified` structurally; `planned` for authorization | The bounded four-level read does not infer secrecy. Ambient character custody and DM-only location holdings need separate audience-safe entry points. |
| Campaign summaries | chapter/arc closing summaries and immutable session recap | `verified` as bounded continuity data; runtime subject to the campaign conflict | Existing summaries can seed an Adventure Log. They do not prove every session event, situation outcome, or visit. |
| World History and Places Visited | no player-facing owner | `missing` | The event ledger is structural/administrative and is not a safe narrative history. Visited places cannot be inferred from prose, names, or current containment. |
| Combat Current View | accepted encounter, initiative, turn-state, turn-budget reads, and `game.core.campaign.current-scene` | `implementation complete; acceptance pending` | The campaign selector names the exact encounter; existing encounter owners remain authoritative. |
| Conversation Current View | `game.core.world.interaction` plus `game.core.campaign.current-scene` | `implementation complete; acceptance pending` | The campaign selector identifies the current accepted conversation without turning the durable interaction into a dialogue engine. |
| Images and maps | current web asset storage can serve bytes | `missing` as game-state association | No reviewed entity-to-visual reference owns a portrait, location image, world map, audience variant, crop, or alt text. Filenames and directories cannot become authority. |
| Rules reference | active source-cited D&D entity catalog | `accepted for registered index` | The Rules tab dynamically indexes every active entity record and preserves exact source-cited detail. Internal mechanics, procedures, queries, and raw JSON remain excluded; richer executable rule text is still separate work. |
| Durable state and hosted access | game SQLite plus a future narrow HTTPS adapter | `planned` | SQLite remains world/campaign authority. The hosted page must not create a second D1/browser-storage copy of game truth; a remote page cannot read the local database directly. |

## Dependency tree

~~~text
Shared D&D information hub                                                    [planned]
├─ A. Product and presentation contract                                      [accepted]
│  ├─ A1. One shell; World/Campaign/Party/Current View/Rules                  [accepted]
│  ├─ A2. Same navigation in DM and Player; Client renamed Player             [accepted]
│  └─ A3. Read-first, non-debug, no automatic LLM interaction                 [accepted]
├─ B. Identity, audience, and transport                                       [planned]
│  ├─ B1. Authenticated principal and server-issued seat                      [accepted for private Site]
│  ├─ B2. Non-escalating DM / Player-preview perspective policy               [accepted for private Site]
│  ├─ B3. Closed, audience-filtered information envelopes                     [accepted for fixture source]
│  └─ B4. HTTPS bridge to authoritative game state; no duplicate state store  [planned]
├─ C. World—the durable parent                                                [planned]
│  ├─ C1. Overview and authorized location browser/detail                     [planned]
│  ├─ C2. Known-place display map from map anchors                            [planned]
│  │  ├─ authorized known-location identities                                 [missing]
│  │  ├─ player-safe base map/layers                                           [missing]
│  │  └─ explicit World → Region → City → Location scope hierarchy             [planned]
│  ├─ C3. Location people and creatures                                       [planned]
│  │  └─ reusable NPC biography/background                                    [missing]
│  ├─ C4. DM-only location holdings via bounded containment                   [planned]
│  ├─ C5. Player-facing World chronology                                      [missing]
│  └─ C6. Entity-owned portraits/location/map visuals                         [missing]
├─ D. Campaign—one history within a world                                     [conflicting]
│  ├─ D1. Current campaign root/link owner in active runtime                   [conflicting]
│  ├─ D2. Read-only overview from active chapter/arc                          [planned]
│  ├─ D3. Adventure Log from reviewed recaps/outcomes                         [planned]
│  └─ D4. Explicit authorized visited-place projection                        [missing]
├─ E. Party                                                                    [planned]
│  ├─ E1. Audience-safe active participation roster                           [accepted]
│  ├─ E2. Canonical character sheet dossier                                   [planned; stored-state presentation accepted]
│  ├─ E3. Knowledge, Backstory, Origin, and bounded Inventory                  [planned; direct inventory and provisional presentation accepted]
│  ├─ E4. Character portrait reference                                        [missing]
│  └─ E5. Shared owned locations, vehicles, and cargo                          [missing ownership projection]
├─ F. Current View                                                             [planned]
│  ├─ F1. Audience-safe Exploration snapshot                  [implementation complete; acceptance pending]
│  ├─ F2. Campaign-owned current encounter selector           [implementation complete; acceptance pending]
│  ├─ F3. Current conversation selector                       [implementation complete; acceptance pending]
│  ├─ F4. Deterministic Combat > Conversation > Exploration   [implementation complete; acceptance pending]
│  ├─ F4A. Preserve projected place context across all modes  [implementation complete; acceptance pending]
│  └─ F5. Known travel choices and declared available actions [implementation complete; acceptance pending]
│     ├─ exact player-known open on-foot routes       [implementation complete; acceptance pending]
│     └─ authored scene affordances/actions            [implementation complete; acceptance pending]
├─ G. Rules                                          [dynamic registered index accepted]
│  ├─ G1. Registered entity reference-entry contract             [accepted]
│  ├─ G2. Source-cited index, category, search, refresh, detail   [accepted]
│  └─ G3. Active-version and current-source fidelity gate         [accepted]
└─ H. Product quality and activation                                           [planned]
   ├─ H1. Responsive, keyboard, screen-reader, contrast, and reduced motion   [planned]
   ├─ H2. Loading, absent, denied, stale, disconnected, and empty states       [planned]
   ├─ H3. Revision-aware bounded refresh with no state writes                 [planned]
   └─ H4. Secret-exclusion and private deployment acceptance                  [blocked by B–G]
~~~

## Conflicts and decisions

| Conflict | Decision |
| --- | --- |
| Separate DM and Player dashboards versus one helper | Replace role-specific navigation with one fixed information architecture. Perspective changes data visibility only. |
| Browser switch versus authorization | The browser requests a permitted perspective; the server derives the effective audience. A player cannot request or tamper into DM. DM may preview Player. |
| Hiding secrets versus excluding secrets | Player envelopes contain no secret records, secret text, private counts, hidden map markers, private asset URLs, or GM-only container contents. |
| World as reusable authority versus campaign-owned copy | Keep one world root. Campaigns reference it. Campaign summaries record campaign history; durable world consequences are world mutations. |
| Accepted campaign contract versus current runtime | Treat existing-world creation/continuity as architecturally intended but currently conflicting. Restore it in the active application boundary before adding create/select promises; do not revive archived C# wholesale or invent a second campaign model in the website. |
| Player map markers versus a revealing map image | Filter markers before transport and use a player-safe base image/layer. A beautiful map whose labels reveal unknown places is still a secret leak. |
| Map placement versus travel rules | Anchors draw markers only. Adjacency/routes/availability and knowledge determine displayed travel choices; the browser never calculates reachability. |
| World history versus technical event log | Add a bounded, audience-aware chronology owner. Never expose structural events, audit rows, or inferred prose as the user-facing history. |
| Campaign visited places versus current position | Add an explicit visit projection/record derived by an authoritative owner. Do not infer visits from the current container, a recap string, or a map click. |
| NPC motive versus biography | Keep motive semantics intact. Add a separate generic presentation profile only after its permanent ID/schema meaning is confirmed. |
| Location inventory versus item secrecy | Containment owns physical structure, not awareness. Only a DM-authorized location-holdings projection may return undiscovered chest contents. |
| Current View inference versus authoritative state | The server returns a closed scene kind and exact references. The browser does not guess from prose or merely choose the first encounter/interaction it finds. |
| Helpful suggestions versus hidden LLM work | Show exact known open routes and authored scene affordances. Affordances are read-only narrative opportunities, not mechanics, eligibility, or execution. Optional generated narration must be explicitly attributed and is not required to use the page. |
| Rules browser versus raw catalog/debug docs | Publish a curated, source-cited reference projection. Never expose internal procedure Markdown or fidelity-failing records as player rules. |
| Hosted persistence versus existing SQLite | SQLite remains canonical. Browser storage holds only harmless preferences; hosted services may store media bytes, but an authoritative entity-to-asset reference and audience policy remain in game state. |

## Ordered leaves

Each leaf is one implementation document and one reviewable user-visible outcome. Siblings that do
not genuinely depend on one another may proceed independently, but the delivery order keeps World
as the primary product surface.

| Order | Leaf | State | Depends on | Exit gate |
| ---: | --- | --- | --- | --- |
| 0 | Confirm this information architecture and visibility contract | `accepted` | Existing prototype and roadmap | This planning document is accepted; no runtime artifact is created. |
| 1 | Shared shell and first World vertical slice | `accepted` | 0 | The componentized shared shell and fixture World slice are deployed and the current full prototype suite passes. |
| 2 | Server-issued audience and read-envelope foundation | `accepted` | 0 | A DM can request DM or Player preview; a Player receives Player only. Secret markers are absent from complete Player responses and initial markup. Switching perspective writes no game state. |
| 2A | Fixture World map presentation and Sites DM seat repair | `accepted` | 2 | The owner can select DM through an exact trusted Sites identity allowlist. A componentized display map shows only projected fixture markers on a label-free base; this is presentation evidence and does not satisfy Leaf 3 or Leaf 4's live-data prerequisites. |
| 2B | Fixture location occupants and DM holdings | `accepted` | 2 and 2A | Componentized location subsections show projected people and creatures, while containers and contents remain absent from Player bytes. This is presentation evidence and does not satisfy Leaf 3 or Leaf 5's live containment prerequisites. |
| 2C | Fixture World History and persistent consequences | `accepted` | 2 through 2B | A componentized chronology shows searchable world events and persistent consequences, while hidden history, private context, and unauthorized entity links are excluded from Player bytes. This is presentation evidence and does not satisfy Leaf 7's live chronology-owner prerequisite. |
| 2D | Fixture People, Factions, and Lore directories | `accepted` | 2 through 2C | Three componentized directories derive people from authorized occupants and project safe faction/lore records with nested-link exclusion. This is presentation evidence and does not satisfy the later live World, faction, profile, or knowledge prerequisites. |
| 2E | Fixture Campaign workspace | `accepted` | 2 and accepted fixture World projection | Overview, Adventure Log, explicit Places Visited, and Outcomes render from a closed audience projection. Campaign records link to authorized World entities without copying them. See the Slice 7 receipt. This is presentation evidence and does not satisfy Leaf 8 or 9's live campaign prerequisites. |
| 2F | Fixture Campaign pursuits and knowledge | `accepted` | 2 and accepted fixture World/Campaign projections | Componentized Quests, Open Threads, and Clues render authored party-known information and DM-only context from one closed campaign projection. Records link only to already projected World entities, with no clock calculation, inference, or campaign write. See the Slice 8 receipt. |
| 3 | Live World overview and location atlas | `planned` | 2 and existing world/location owners | The selected world is independent of campaign selection. DM receives bounded full detail; Player receives only authorized locations and fields. Empty/unknown/denied states are friendly and nontechnical. |
| 4 | Display-only World map | `planned` | 3, known-location projection, accepted anchors | DM sees authorized full placement; Player sees only current/known markers on a safe map base. No distance, route, fog, movement, or tactical meaning is invented. |
| 4A | Scoped World, Region, City, and Location maps | `planned` | 4, confirmed scope/layer/feature owners, safe child-scope projection, and media provenance | Explicit breadcrumbs and parent/child scope links navigate independently authored coordinate spaces. Optional generated Location imagery remains reviewed, illustrative, replaceable, and non-authoritative. See `DND2024-SCOPED-MAP-VIEWS-FUTURE-PLAN.md` and `DND2024-SCOPED-MAP-VIEWS-DEPENDENCY-TREE.md`. Its Slice 1 scope contract and fixture hierarchy are accepted as presentation evidence (`DND2024-SCOPED-MAP-VIEWS-SLICE-1-RECEIPT.md`); this leaf stays `planned` because its live-data prerequisites are untouched. |
| 5 | Location People & Creatures and DM Holdings | `planned` | 3 and 2 | Exact co-present entities render in location detail. DM may inspect bounded location containers; Player responses contain neither hidden containers nor their contents. Creature detail stays limited to accepted owners. |
| 6 | Visual reference and NPC presentation foundation | `missing` | confirmed schemas/storage | Exact entity state selects portraits, location art, and map layers with alt text and audience variants. Missing art is explicit; filenames never associate assets. NPC biography remains separate from motive and D&D stat blocks. |
| 7 | World History | `missing` | 2, world clock/event/domain review | A bounded chronology shows reviewed world events in world time with audience filtering and source references; it does not expose the raw event/audit ledger. |
| 8 | Active campaign authority restoration/read proof | `conflicting` | current generic application boundary | Current code can resolve an existing campaign-to-world link and read continuity without copying World. A focused test proves two campaigns can reference one world and later reads observe a committed world change. Creation itself may remain outside the UI. |
| 9 | Campaign overview, Adventure Log, Outcomes, and Places Visited | `planned` | 2 and 8; explicit visit owner for Places Visited | Safe active chapter/arc and immutable recaps render as friendly narrative sections. Outcomes retain their owner/audience. Visits come only from the explicit projection; missing data is shown as unknown, not guessed. |
| 10 | Party roster and character dossier | `planned` | 2 and active participation | Slice 1 accepts the exact active roster, selection, and six-section provisional dossier. Slice 2 accepts authoritative stored character components, class memberships, and a bounded direct-inventory projection when those records exist, while preserving the explicit provisional fallback. Slice 3 accepts the original cinematic companion Overview, carried-equipment preview, and an honest shared-holdings seam. Remaining acceptance requires the derived character-sheet aggregate, deeper canonical inventory projection, portrait owner, and an explicit audience-safe ownership projection for party locations, vehicles, and cargo; campaign references, visits, containment, and prose never imply ownership. Backstory, mechanical Origin, and Knowledge stay distinct and audience-safe. No character-builder or unsupported spell surface is implied. |
| 11 | Exploration Current View | `implementation complete; acceptance pending` | 2, 3, accepted current-location/people/knowledge reads | A location scene shows safe description, observations, co-present people, exact known open on-foot routes, read-only authored scene affordances, and an optional authorized image without requiring a model response. No action is inferred or executed. |
| 12 | Combat and Conversation Current View | `implementation complete; acceptance pending` | 11 plus confirmed `game.core.campaign.current-scene` | The server deterministically resolves Combat, Conversation, or Exploration. Combat composes accepted encounter/turn reads; Conversation shows exact authorized participants; both retain the same audience-projected place description and observations. No first-entity scan or prose guess selects a mode. |
| 13 | Rules reference registered catalog | `accepted` | 1 | The live page indexes every active D&D entity without a maintained ID list, derives family filters, searches the complete set, refreshes safely, and shows exact source-cited detail. Internal mechanics/procedures/queries and raw JSON are absent. |
| 14 | Responsive, revision-aware private acceptance | `planned` | Delivered leaves from 2–13 | Mobile/desktop, keyboard, screen reader, contrast, reconnect/stale/empty/denied states pass. Automated tests prove Player bytes exclude secrets, DM preview matches Player, tabs require no LLM call, and every read leaves SQLite unchanged. |

If a missing World History or media contract blocks its leaf, Party, Exploration, or the Rules pilot
may proceed after their actual prerequisites. They do not inherit a false dependency on World
History merely because World appears first in navigation.

## Next dependency boundary

Leaf 3 is not yet active. Replacing fixtures with live World data still requires B4's narrow HTTPS
bridge to authoritative game state and an exact audience-safe location projection. Current World
recipes are trusted-GM reads and cannot be exposed to Player merely by passing them through the new
envelope.

The next implementation document must close the bridge origin/authentication, fixed world
selection, bounded read shape, player-known location proof, revision semantics, disconnected/stale
behavior, and no-write evidence. Until those inputs are confirmed, the Site must retain the current
fixture source rather than infer live visibility or create a second state store.

## Acceptance evidence by boundary

- **Positive:** each delivered tab renders only exact current data from its named owner; DM-only
  additions appear for an authorized DM.
- **Negative/no-change:** malformed, unknown, stale, cross-world, cross-campaign, wrong-seat, or
  unavailable input fails closed and leaves world/campaign state unchanged.
- **Secret exclusion:** a canary secret, hidden location, private motive, container item, asset URL,
  and private count are absent from serialized Player responses, logs returned to the browser, and
  rendered markup.
- **World reuse:** two campaigns resolve the same world identity; a world mutation committed between
  them is visible from later authorized world reads; campaign creation never duplicates world rows.
- **Map boundary:** equal safe inputs place markers deterministically; unauthorized anchors and
  revealing asset layers are not delivered; no displayed line/spacing implies travel legality.
- **History boundary:** World chronology and Campaign Adventure Log retain different owners and do
  not silently rewrite one another.
- **Current View:** equal authoritative context resolves the same scene kind; missing/ambiguous
  context fails to a safe Exploration/unknown state rather than selecting an arbitrary entity.
- **Rules compatibility:** reference summaries preserve exact source citation and never override
  executable catalog mechanics.
- **Replay/rollback:** read retries are side-effect free. Any later write-enabled leaf requires its
  own idempotency, stale-state, transaction, replay, and rollback contract.
- **Product quality:** tab navigation, roster/location selection, dialog/focus behavior, responsive
  layouts, screen-reader names, high contrast, reduced motion, refresh, and disconnect recovery have
  focused browser evidence.

## Confirmation gates

The user's stated product direction confirms the five-tab order, World-above-Campaign hierarchy,
one shared DM/Player layout, secret-only perspective difference, and information-first scope. It
does not yet confirm new permanent/runtime contracts.

The user's 2026-08-28 instruction to continue with the next planned slices confirms the private,
server-issued audience/read-envelope Leaf 2. It does not authorize public sharing or app-owned
authentication.

The user's 2026-08-30 confirmation authorizes the campaign current-situation owner using the
existing D&D application namespace. The authored `game.core.campaign.current-scene` component is
therefore runtime-qualified as `dnd2024.game.core.campaign.current-scene`. It does not authorize a
live state write or activation. The user's later instruction to continue implementing the Current
tab confirms the separate read-only player-known route projection described by Slice 3.
The user's next instruction to continue after the permanent-contract gate confirms the authored
`game.core.campaign.scene-affordances` owner, runtime-qualified as
`dnd2024.game.core.campaign.scene-affordances`, and its read-only Current View projection.

Separate confirmation is still required before introducing:

- public sharing, app-owned authentication, or a seat-management surface;
- a current campaign-root definition or restored existing-world campaign-create surface;
- a broader player-known location projection beyond the already confirmed notebook subject and
  exact Current View route boundary;
- a generic NPC presentation-profile component;
- a visual-reference component, asset storage meaning, or database migration;
- a World chronology or campaign visited-place/outcome component;
- any additional current-conversation/current-encounter component or semantic revision to the
  confirmed `game.core.campaign.current-scene` selector;
- a player-facing rules-reference content family;
- any game-state write, public route, schema-meaning change, or final feature acceptance; or
- activation/deployment of a live-data revision.

## Planning receipt

- Selected owner: `web/WEB-INTERFACE-ROADMAP.md`, Feature 5, with runtime presentation isolated to
  `src/system/web-interface/dnd2024` and live integration supplied by its local server adapter.
- Alignment: `dnd2024-compatible`; no D&D calculation, eligibility, timing, or outcome is moved to
  the browser or C#.
- Authoritative state: existing game SQLite/catalog owners, never browser storage or a parallel
  hosted database.
- Runtime artifacts created: **none**.
- Catalog records, schemas, permanent IDs, migrations, public APIs, live database changes, and
  deployments created: **none**.
- Deliberate stop: planning ends before the Leaf 1 implementation document or any site edit.
