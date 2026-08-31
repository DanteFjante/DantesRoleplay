# Thalorien canon reconciliation plan

**Status:** Canon reconciliation complete — S1–S5 completed; Brackenford and campaign imported; S6 completed  
**Owner:** Thalorien world curation  
**Ruleset alignment:** Ruleset-neutral  
**Live authority:** The local MCP world `world.thalorien`  
**Portable authority:** `artifacts/World_Creation_Ledger.md`  
**Existing owners:** `procedure.game.core.world.location`, `procedure.game.core.world.knowledge`, and `procedure.world.change`

## Outcome and boundary

Bring the portable ledger, live facts, and location topology into a coherent, searchable state without
inventing unresolved canon or changing game rules.

This work includes ledger parity, named-location completion, confirmed topology, and explicitly
modeled historical/faction details. It excludes new D&D mechanics, map rendering, terrain geometry,
automatic travel routes, player-facing knowledge authorization, and creating facts merely to fill
open creative questions.

## Audit baseline

- Live MCP state currently has **148 fact records**, **3 secret records**, **1 faction**, and **23 location records**.
- The portable ledger is intentionally more concise, but it omits large groups of already-confirmed
  imperial, magical, and wider-world facts.
- The six kingdom regions and the World Tree Grounds have canonical adjacency links, but no
  coordinates, routes, or nested capital/city records.
- Several important places are confirmed in prose but cannot yet be modeled as locations because
  their names, owners, or exact containment are unresolved.
- The current `knowledge-answer` intent surface remains unavailable because its local development
  audience is not enabled. This is an infrastructure configuration issue, not a missing lore fact.

## Dependency tree

```text
R0: Thalorien reconciliation
├─ S1: Portable-ledger parity                                  [completed]
├─ S2: Central territory and hex topology decisions            [completed]
│  └─ S3: Named civic/trade/ruin location records              [completed]
├─ S4: Magic towers, mountains, forests, and volcanic sites    [completed]
├─ S5: Merchant coalition and peace-threat world entities      [completed]
└─ S6: Knowledge-answer local audience configuration           [completed]
```

## Slice S1 — Portable-ledger parity

**Status:** Completed  
**Outcome:** Add concise, grouped summaries of already-confirmed MCP facts to the ledger. No live
world changes and no new canon.

**Content to reconcile:**

- The Thalmos succession: eight heirs, sibling council, incompatible priorities, rebellion,
  seven-way division, and the Great Seven-Kingdom War.
- Imperial institutions: annual council, constitution, university, civil service, welfare system,
  military school, trade roads, and specialist schools.
- Magic and the towers: constitutional tower role, battle-magic role, surviving-tower state,
  hidden networks, and scattered/hunted magicians.
- Wider-world context: known-world limit, dangerous wilderness, sea monsters, and the current
  scholarly question about the age's end.

**Allowed artifacts:**

- `artifacts/World_Creation_Ledger.md`
- `artifacts/World_Creation_Import_Receipt.md`

**Stop point:** Do not alter MCP facts, archive records, create locations, or resolve omissions.

**Acceptance:** Every added statement must map to an existing active fact, superseded names remain
revision history only, and the ledger's open-question list contains only genuinely unresolved items.

## Slice S2 — Central territory and hex topology decisions

**Status:** Completed  
**Outcome:** Closed the geographic semantics and committed the confirmed topology.

**Confirmed decisions:**

1. The World Tree Grounds are neutral shared territory, politically independent of Aldros and directly adjoining it.
2. Each outer kingdom borders Aldros and its two neighboring outer kingdoms.
3. The Southwestern Volcanic Region belongs to Waylos.
4. The southeastern mountain range belongs to Valeros; Harrowfall's trade advantage came from distinct northern cold highlands.

**Existing owners:** World topology uses containment for hierarchy and
`game.core.world.location.connected-to` for canonical adjacency. Map anchors are optional display
metadata and must not be used to infer travel or borders.

**Delivered:** Four fact records and thirteen canonical adjacency relationships were dry-run, committed, and read back. No coordinates or routes were created.

## Slice S3 — Named civic, trade, and ruin locations

**Status:** Completed  
**Outcome:** Created the confirmed named civic, trade, and ruin locations as draft live records.

**Delivered records:**

- Crownmere, contained in Aldros as the capital settlement.
- Larkspire University, contained in Aldros as a university site west of the World Tree Elven Enclave.
- Merrowgate, contained in Merceros as the surviving southern trade-city settlement.
- Harrowfall, contained in Evandos as the ruined northern trade-city settlement beside the Northern Cold Highlands.

Each location has an active, searchable supporting fact scoped to Thalorien. Named mountain sites remain in S4.

## Slice S4 — Regional features and magic-tower sites

**Status:** Completed  
**Outcome:** Promoted the approved regional concepts into concrete live locations and facts.

**Delivered:**

- The Greenmantle and Oathshield Range as draft sites in Valeros, including the forest's goblin-frontier role.
- The Starwell Tower in Minevros, Dawnbell Tower in Rhiannos, Emberwright Tower in Waylos, and the reachable ruins of Bannerfall Tower in Valeros.
- Mount Cinderwake and the Ashen Crown Isles as canonical named facts and revised summaries on the pre-existing volcanic site locations.

**Stop point honored:** No coordinates, travel links, or player-facing discovery state were created.

## Slice S5 — Merchant coalition and peace-threat entities

**Status:** Completed  
**Outcome:** Recorded the background threat as an active GM-only faction with supporting secrets,
without exposing hidden state as public player knowledge.

**Delivered:**

- The Gilded Concord, a secret Merrowgate-centered faction with a ready agenda against Book of Truth education.
- GM-only secrets for coordinated market manipulation, its distributed Merrowgate network, and its private audience with all seven rulers.
- No named ruler, individual member, territorial controller, or headquarters was invented.

**Existing owners:** Facts/rumours/secrets use the knowledge procedure; a formal coalition requires
the existing faction owner, not a custom component.

**Stop point:** Do not create factions, rumours, or secrets until audience/sensitivity decisions are
confirmed.

## Slice S6 — Intent-answer availability

**Status:** Completed  
**Outcome:** The local development audience is enabled for `query(kind: "knowledge-answer")`, bound to
`campaign.thalorien.brackenford`, and the campaign is active and linked to `world.thalorien`. The standard
world clock is present so the knowledge timeline can resolve current world state.
verify an intent question retrieves only authorized knowledge for the configured audience.

This is deliberately separate because it changes local knowledge-access configuration rather than
Thalorien canon. No workaround should bypass the audience policy.

## Ordered implementation sequence

1. **S1:** Repair the portable ledger from existing live facts.
2. **S2:** Complete the geographic semantics and canonical adjacency topology.
3. **S3:** Create named civic/trade/ruin locations and verify the containment graph.
4. **S4:** Create approved regional-feature and tower locations.
5. **S5:** Model the merchant coalition under the existing faction/knowledge owners.
6. **S6:** Configure local intent-answer access, create the separately governed campaign bootstrap, then test player-safe retrieval.

## Verification

- Before each live change: read the governing procedure and existing target records, dry-run the
  exact effects, then commit the identical payload.
- After each live change: query the entity/fact back; for topology, also query the bounded graph
  rooted at `world.thalorien`.
- After ledger work: verify every added summary against the relevant active live fact and preserve
  archived/superseded wording only in revision history.
- Record each completed slice in `artifacts/World_Creation_Import_Receipt.md`.

## Planning receipt

- Runtime artifacts created: sixteen fact entities, three secret entities, one faction entity, ten named location entities, two revised volcanic site summaries, and thirteen canonical location-adjacency relationships.
- Canon changes created: neutral World Tree Grounds, Waylos's volcanic territory, the Valeros mountain
  distinction, the central-and-ring border topology, Crownmere, Larkspire University, Merrowgate, Harrowfall's placement in Evandos, the completed regional/tower set, and the Gilded Concord.
- Next authorized action: continue developing the Brackenford campaign through its opening chapter; authorized retrieval and campaign resume are available.
