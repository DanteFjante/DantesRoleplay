# D&D 2024 World-tab completion dependency tree

Status: **planning active; canonical placement and chronology owner verified; chronology projection active with live DM records**
Ruleset alignment: **dnd2024-compatible presentation over ruleset-neutral World owners**
Source: **not applicable**; no D&D rule is defined or changed

Roadmap owners: `WORLD_AND_LORE_PLAN.md` and `web/WEB-INTERFACE-ROADMAP.md`, Feature 5
Parent UI plan: `DND2024-WEB-INFORMATION-HUB-DEPENDENCY-TREE.md`

## Outcome and non-goals

Complete the authoritative World data needed by the D&D 2024 World tab: record-owned maps and
imagery, multi-scope placement, dated chronology, Player-safe directories, reusable entity media,
and fuller NPC profiles. The website remains a read-only consumer. It does not infer ownership from
filenames, derive history from lore, expose DM records to Players, or create D&D rules.

## Existing owners and evidence

| Concern | Owner | State | Evidence |
| --- | --- | --- | --- |
| World/Region/City/Location hierarchy | location components plus containment | verified | `procedure.game.core.world.location`; live Thalorien → Thalos → Region → place readback |
| Map ownership and audience variants | `game.core.world.map.visual` | verified but incomplete coverage | 12 live nodes own exact Player/DM asset keys; World root and most Locations have no visual |
| Region-plane coordinates | `game.core.world.map.anchor` | verified | 24 live normalized anchors and W9 receipts |
| World- and City-plane coordinates | same anchor owner | verified | W18 Slice 1 generalizes direct-container planes with focused evidence |
| World History UI | audience-safe chronology HTTP projection | verified and active | Slice 20 tests plus Slice 21 activation, 35-record DM readback, Player non-disclosure, and browser verification |
| DM people/faction/holding directory | live containment, faction, motive reads | verified for local DM | connected hub tests and live directory receipt |
| Player-safe structured directories | no closed projection owner | missing | Player preview deliberately omits the trusted-GM directory |
| Map alt text | `game.core.world.map.visual` | verified for map variants | closed variant schema |
| General portraits/illustrations/provenance/history | none | missing | map visuals do not own portraits, credits, hashes, revisions, or rollback history |
| NPC identity/location/faction/motive | entity, containment, relationships, motive | verified | existing World owners |
| Reusable NPC biography/background | none | missing | parent information-hub tree records the gap |

## Dependency tree

~~~text
D&D 2024 World tab backed by authoritative World records                    [planned]
├─ A. Record-owned atlas associations                                       [partly verified]
│  ├─ A1. Live map visual owner and 12 current scopes                       [verified]
│  └─ A2. Reviewed World/Location coverage and asset approval               [planned]
├─ B. Canonical placement on World, Region, and City planes                 [verified]
│  ├─ B1. One anchor owner over direct active plane containment             [verified]
│  ├─ B2. Per-plane coordinate uniqueness                                   [verified]
│  └─ B3. Plane-scoped trusted-GM layout recipe                             [verified]
├─ C. Dedicated dated World chronology                                      [verified for current DM records]
│  ├─ C1. W19 permanent chronology record/schema/procedure                  [verified]
│  ├─ C2. Audience-safe projection and History consumer                     [verified and active]
│  └─ C3. Live Thalorien chronology                                         [35 GM-only records verified]
├─ D. Player-safe People, Creatures, and Factions                           [planned]
│  ├─ D1. Closed audience-filtered directory projection                     [missing]
│  └─ D2. Player World-tab consumption with secret-exclusion proof          [blocked by D1]
├─ E. Entity-owned portraits and illustrations                             [verified]
│  ├─ E1. Media variants, alt text, provenance, hashes, lifecycle/history   [verified; Slice 23 accepted]
│  └─ E2. Approved bytes and fail-closed web registry                       [verified with three reviewed Caldris assets]
└─ F. Reusable NPC profiles                                                  [missing]
   ├─ F1. Biography/background presentation record                          [awaiting schema/ID confirmation]
   └─ F2. Audience-safe directory/detail projection                         [blocked by F1 and D1]
~~~

## Conflicts and decisions

- The bounded website asset registry remains necessary to translate safe asset keys into served
  bytes. It must not select owners, hierarchy, audience, or visibility.
- Extend `game.core.world.map.anchor`; do not create a second scoped-coordinate component.
- Chronology must be dated World state, not inferred from knowledge text or the structural event log.
- Player directories need a server-side closed projection. CSS and client filtering are not an
  authorization boundary.
- General media must remain separate from map-plane visuals and from NPC biography.
- Biography/background must remain separate from D&D character mechanics and recurring motives.

## Ordered leaves

| Order | Leaf | Depends on | Exit gate |
| ---: | --- | --- | --- |
| 1 | W18 multi-plane anchor contract — verified | existing topology/anchor owners; user confirmation | Root, Region, and settlement planes share one closed anchor model with per-plane uniqueness |
| 2 | Live Thalorien World/City coordinate manifest | 1 plus reviewed coordinates and exact child records | Dry-run-first live readback proves canonical World/City placements |
| 3 | Remaining map visual coverage | reviewed existing assets and exact associations | World/Location records select approved audience variants; website has no entity-ID association table |
| 4 | World chronology owner — verified | confirmed permanent ID/schema | Dated audience-classified records validate and import without copying knowledge/event logs |
| 5 | World chronology projection — verified and active | 4 plus confirmed HTTP surface | Player/DM chronology bytes are audience-safe, 35 GM records render, and History never derives events from knowledge |
| 6 | Player-safe structured directories | authorized projection contract | Player bytes contain only authorized people, creatures, factions, links, and aggregate counts |
| 7 | General entity media owner — Slice 23 accepted | confirmed permanent ID/schema and three reviewed Caldris assets | Entity variants include alt/provenance/lifecycle evidence, hidden variants fail closed, and rollback is explicit |
| 8 | NPC profile owner | exact permanent ID/schema confirmation | Reusable biography/background renders separately from motive and D&D statistics |

## Next dependency boundary

W18 and W19 Slice 1 are verified; Slice 21 activated the chronology projection and authored 35 GM-only Thalorien records with Player non-disclosure. Live inspection confirms Crownmere and Merrowgate currently have
no direct child Location records, so the next map leaf cannot invent City markers or permanent
geography. Player chronology publication remains a separate audience decision. The general-media
leaf is verified by Slice 23's generic owner, live three-entity Caldris import, revision 26
publication, and DM/Player browser evidence. The NPC-profile leaf remains separately gated.

## Confirmation gates

The user's 2026-08-30 confirmations approve multi-plane anchor semantics, the six outcome branches,
the exact W19 chronology IDs/schema, and Slice 23's exact `game.core.world.media.visual` /
`procedure.game.core.world.media` contract, three reviewed Caldris assets and bindings, public
read-model additions, governed activation/import, page publication, and completed slice acceptance.
Separate confirmation remains required for NPC-profile IDs/schemas, further live mutations outside
that boundary, and other public-surface changes.

## Current boundary receipt

- W18 extends the existing anchor authority rather than duplicating it.
- W19 adds the dedicated chronology owner and trusted-GM recipe.
- Slice 20 adds the confirmed audience-safe HTTP projection and dedicated History consumer.
- Slice 21 activates that projection, adds 35 GM-only Thalorien records, and verifies the live DM timeline with Player non-disclosure.
