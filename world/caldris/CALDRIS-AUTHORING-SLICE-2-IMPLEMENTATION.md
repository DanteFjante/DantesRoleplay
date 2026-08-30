# Caldris Authoring Slice 2 implementation — second-ring story expansion

Status: **accepted as authored review content; human tabletop review remains**
Owner/roadmap: [World and lore roadmap](../../WORLD_AND_LORE_PLAN.md)
Dependency tree/leaf: [Caldris implementation map](../DND2024-LOW-MAGIC-TWO-CONTINENT-WORLD-IMPLEMENTATION-MAP.md), parallel review-content expansion; does not advance runtime Leaves 6–11
Ruleset alignment: **dnd2024-compatible**
Source ID and locator: **not applicable**; this slice authors original setting and campaign review
content and defines no D&D rules.
Outcome: deepen Caldris with a second ring of locations, historical incidents, folklore, recurring
characters, relationship-driven story features, and additional three-objective quest packets.
Exclusions: catalog/source records, permanent runtime IDs, schemas, mechanics, migrations, public
operations, live SQLite writes, stat blocks, D&D rule changes, player-visible secret policy,
canonical coordinates, registered media, or claims that tabletop/runtime gates are complete.
Allowed files/areas: `world/caldris/**`, the Caldris implementation map, and the existing prospective
Caldris paragraph in `WORLD_AND_LORE_PLAN.md`.
Stop point: stop after the linked authored supplements pass structural, count, duplicate-name,
cross-link, and whitespace checks; do not import, activate, mechanically bind, or simulate a human
tabletop acceptance.

## Confirmed decisions

- The user explicitly requested that the existing plan continue with more story features,
  locations, quests, lore, history, and characters.
- The [Caldris creative charter](../CALDRIS-CREATIVE-CHARTER.md) remains the tone, realism, magic,
  humour, sadness, personality, and sensitivity authority for review prose.
- The [Slice 1 receipt](CALDRIS-AUTHORING-SLICE-1-RECEIPT.md) is evidence for the existing content
  inventory and authority boundary, not permission to bypass the next human tabletop review.
- Numbered quest labels and supplement headings are editorial navigation only, never permanent IDs.
- New material should connect to existing locations, cast, factions, eras, and campaign threads,
  while ensuring many adventures remain genuinely unrelated to the Measure conspiracy.

## D&D 5e 2024 alignment

| Rule concern | Source meaning | Existing owner | Consequence |
| --- | --- | --- | --- |
| Setting, history, personality, and plot | Not a D&D rule | World/campaign/quest/story owners | Author original review prose without redefining mechanics. |
| Checks, combat, monsters, items, magic, and rewards | Outside this slice | Accepted D&D catalog owners | Describe obstacles and possible approaches, but assign no DC, stat block, spell result, item property, or reward formula. |
| Quest structure | Current bounded quest shape remains authoritative | Quest owner | Give every new packet exactly three initial editorial objectives and defer lifecycle binding. |
| Secrets and relationships | Runtime visibility/disposition owners remain incomplete | World knowledge and narrative-NPC gates | Mark GM-facing ideas as review prose; create no player projection or durable relationship value. |

## External implementation reference

No Foundry dnd5e review applies. This slice adopts no D&D rules behavior, data flow, edge case, or
licensed game content.

## Prerequisite evidence

- The creative charter confirms broad authorial discretion inside the stated tone and safety limits.
- The Slice 1 receipt verifies the existing twelve-polity, thirty-six-city, campaign, quest, and
  visual review pack.
- The implementation map keeps human tabletop review and every runtime prerequisite explicit.

## Runtime artifacts

None. New Markdown files remain review artifacts and do not enter the catalog, source registry,
application package, database seed, public API, or running World.

## Authoritative state and closed input

The closed input is the user's expansion request, the creative charter, and the accepted Slice 1
review pack. The author may invent additional names, events, relationships, rumours, mysteries,
and quest situations consistent with those documents. The author may not create permanent IDs,
rules, authoritative chronology records, visibility decisions, or live state.

The delivered supplement must include at least:

- forty-eight additional named locations, distributed across all twelve polities;
- twenty-four historical incidents and twenty-four pieces of folklore, disputed belief, or local
  custom with present-day story use;
- thirty-six additional recurring characters, distributed across all twelve polities and connected
  to existing places or factions;
- forty reusable story features covering relationships, returns, travel, downtime, secrets,
  humour, consequences, and unrelated-adventure transitions; and
- eighteen additional quest packets, continuing editorial labels Q31–Q48, each with exactly three
  initial objectives, multiple approaches, recoverable failure, and a consequence or next door.

## Behavior, result, and typed effects

There is no runtime behavior, result envelope, typed effect, or transaction. Repeated dossier and
packet shapes make the review material searchable and later mappable to confirmed owners.

Every added location receives a material purpose, memorable sensory or social identity, present
pressure, hidden layer, and adventure use. Every character receives a playable personality,
competence, immediate want, vulnerability, relationship, and change trigger. Historical and lore
entries distinguish broadly accepted record, contested interpretation, and present consequence.

## Failure, replay, and rollback contract

- Missing target counts, duplicate names, broken local links, contradictory polity placement,
  quests without exactly three objectives, or material that breaks the charter fail review.
- A higher-layer reveal must not erase the value of resolving its lower layer.
- Unrelated adventures must remain genuinely capable of standing alone.
- Re-running authoring adds a new slice or deliberate revision; it does not silently overwrite an
  accepted image, runtime record, or player-known fact.
- No authoring failure changes live state because this slice has no write path to it.

## Implementation sequence

1. Author this active boundary and update the working plan.
2. Create the location and history/lore supplements.
3. Create the additional cast and reusable story-feature catalog.
4. Create Q31–Q48 and connect them to existing campaign threads and independent transitions.
5. Update the authoring index, implementation map, and roadmap summary.
6. Verify counts, unique names, quest-objective shape, local links, and whitespace.
7. Write the Slice 2 receipt, mark this document accepted as review content, and stop.

## Acceptance matrix

| Case | Expected evidence |
| --- | --- |
| Geographic breadth | All twelve polities receive four additional locations with distinct material purposes and story uses. |
| Historical depth | Twenty-four incidents and twenty-four lore/custom entries connect older eras to present places, institutions, or conflicts. |
| Character variety | Every polity receives three recurring characters; personalities, occupations, loyalties, vulnerabilities, and humour vary. |
| Story utility | Forty features support recurring relationships, cozy returns, consequences, secrets, comedy, travel, and fresh adventure doors. |
| Quest agency | Q31–Q48 each contain three objectives, multiple approaches, redundant clue or discovery routes where relevant, and recoverable failure. |
| Layering | New boss or sponsor reveals preserve lower-layer victories; several quests are wholly independent of the central conspiracy. |
| Compatibility | Rules-adjacent choices remain narrative prompts pending accepted D&D owners and sources. |
| No authority drift | No catalog, schema, mechanic, source, code, migration, public operation, media registration, or live database changes. |

## Verification commands

- Focused PowerShell review for required files, per-polity distribution, target counts, unique names,
  Q31–Q48 sequence, exactly three objectives per quest, and local Markdown links.
- Trailing-whitespace scan for all Slice 2 Markdown.
- `git diff --check -- WORLD_AND_LORE_PLAN.md world/DND2024-LOW-MAGIC-TWO-CONTINENT-WORLD-IMPLEMENTATION-MAP.md world/caldris`
- No catalog validation, full suite, or protocol walk: this slice changes no catalog, code,
  registration, or protocol surface.

## Completion receipt and exit gate

Delivered evidence is recorded in the
[Slice 2 receipt](CALDRIS-AUTHORING-SLICE-2-RECEIPT.md). Only the authored review supplement is
accepted. Human tabletop review remains the next playable-content gate, and all runtime
creation/import work remains separately blocked.
