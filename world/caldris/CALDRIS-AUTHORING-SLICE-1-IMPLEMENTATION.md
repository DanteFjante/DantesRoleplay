# Caldris Authoring Slice 1 implementation — expanded review-content and visual pack

Status: **accepted as authored review content; content-only tabletop review remains**
Owner/roadmap: [World and lore roadmap](../../WORLD_AND_LORE_PLAN.md)
Dependency tree/leaf: [Caldris implementation map](../DND2024-LOW-MAGIC-TWO-CONTINENT-WORLD-IMPLEMENTATION-MAP.md), review-content Leaves 1–5 and Leaf 6 preparation
Ruleset alignment: **dnd2024-compatible**
Source ID and locator: **not applicable**; this slice authors original setting and campaign review
content and does not define D&D rules.
Outcome: expand Caldris into a substantial two-continent setting package with a feature backlog,
macro World bible, gazetteer, factions, NPCs, interwoven campaign stories, quests, maps, and
storybook concept art.
Exclusions: catalog/source records, permanent runtime IDs, schemas, mechanics, migrations, public
operations, live SQLite writes, player authorization, stat-block invention, D&D rule changes,
accepted chronology records, or feature-completion claims.
Allowed files/areas: `world/caldris/**`, this slice's status/receipt, the Caldris implementation map,
and its existing World-roadmap paragraph.
Stop point: stop after a linked review-content pack and presentation-only visual set exist in the
workspace and pass structural/link/image checks; do not import, activate, or bind media to live
state.

## Confirmed decisions

- The user delegated ordinary creative choices and asked for many places, NPCs, quests, stories,
  layered apparent-boss/higher-power reveals, unrelated next-adventure handoffs, maps, and images.
- The [Caldris creative charter](../CALDRIS-CREATIVE-CHARTER.md) governs tone, realism, magic,
  humour, sadness, personality variety, campaign range, and default sensitivity.
- Caldris remains a working review name. Content-local slugs and numbering in these documents are
  editorial references only, never proposed runtime IDs.
- Visuals are presentation-only concept assets. They contain no canonical coordinates, routes,
  audience policy, character identity binding, or item mechanics.

## D&D 5e 2024 alignment

| Rule concern | Source meaning | Existing owner | Consequence |
| --- | --- | --- | --- |
| Setting, plot, personality, geography | Not a D&D rule | World/campaign/quest/story owners | Author original review content without redefining rules. |
| Player magic in a low-magic society | Accepted mechanics remain authoritative | D&D catalog mechanics/content | Describe rarity and reactions only; impose no unstated spell/class restriction. |
| Monsters, items, checks, combat, rewards | Rule behavior is outside this slice | D&D mechanics and content owners | Mention narrative roles only; later playable binding must select accepted records and exact sources. |
| Quests and clues | Current repository contracts remain authoritative | Quest and World knowledge owners | Use three-objective editorial shapes and robust clue planning without claiming runtime transitions. |

## External implementation reference

No Foundry dnd5e review applies. This slice creates original prose and images and adopts no D&D
rule behavior, data flow, or edge case.

## Prerequisite evidence

- The implementation map records the verified World topology/faction/knowledge and narrow
  campaign/quest/storytelling owners.
- The creative charter closes the content-direction gate and authorizes ordinary invention.
- Current runtime blockers remain explicit in the implementation map; this slice does not bypass
  them through prose or image files.

## Runtime artifacts

None. New Markdown and image files are review/presentation artifacts only. They do not enter the
catalog manifest, source registries, application package, database seed, public API, or live world.

## Authoritative state and closed input

The only authoritative input to this slice is the user's request plus the confirmed creative
charter. The author may choose names, histories, relationships, mysteries, and visual compositions
within that boundary. The author may not choose permanent runtime IDs, D&D results, live state,
audience authorization, or schema meaning.

The review package must include:

- two continents, twelve polities, seven eras, thirty-six primary cities, and substantial smaller
  place coverage;
- faction, NPC, dragon, creature, faith, trade, magic, and historical context;
- multiple story layers, boss ladders, unrelated adventure bridges, quests, clue routes, riddles,
  failure consequences, and cozy anchors;
- a feature backlog distinguishing ready review-content work from blocked runtime capabilities; and
- at least two maps plus character, location, and item visuals using one coherent illustrated style.

## Behavior, result, and typed effects

There is no runtime behavior, result envelope, effect list, or transaction. Documents use explicit
cross-links, concise editorial keys, and repeated dossier shapes so later review can detect gaps.
Visual files use descriptive names and a manifest recording subject, prompt intent, status, and
presentation-only limits.

## Failure, replay, and rollback contract

- Missing required sections, broken local links, absent visual files, duplicate city names within a
  polity, count mismatches, or contradictions fail the review check.
- An image that changes canonical geography, adds illegible labels, breaks the charter's material
  tone, or cannot be tied to its brief is regenerated or retained only as a rejected draft.
- Re-running image generation never overwrites an accepted asset without explicit replacement;
  later variants use versioned filenames.
- No failure can partially change live state because this slice has no live write path.

## Implementation sequence

1. Author this bounded slice and feature backlog.
2. Author the World bible, gazetteer, cast/faction atlas, and campaign/quest tapestry.
3. Add a visual brief and manifest, then generate distinct image assets through the image-generation
   skill and copy the accepted outputs into `world/caldris/visuals/`.
4. Check required sections, document links, target counts, image existence/decoding, and diff
   whitespace.
5. Write the Slice 1 receipt, update this document to accepted, and stop before runtime work.

## Acceptance matrix

| Case | Expected evidence |
| --- | --- |
| Creative breadth | Both continents, all twelve polities, all thirty-six primary cities, seven eras, factions, NPCs, creatures, stories, and quests are present. |
| Layered stories | At least three apparent-boss/higher-authority ladders and several unrelated adventure handoffs have explicit clues and off-ramps. |
| Agency | Prepared quests contain alternative approaches, multiple clue paths, recoverable failures, and outcomes that do not require one NPC or one roll. |
| Tone | Humour/personality contrasts, cozy anchors, sincere sadness, and material consequences follow the charter. |
| Consistency | Names, place containment, chronology, politics, magic limits, NPC ties, and story references do not contradict their ledgers. |
| Visuals | Maps, characters, locations, and items exist as decodable workspace images and are listed in the visual manifest. |
| No authority drift | No catalog manifest, schema, mechanic, source record, migration, public operation, or live database is changed. |
| Compatibility | Rules-adjacent prose defers to accepted D&D owners and labels later bindings as pending. |

## Verification commands

- Focused PowerShell review script for required files, headings, counts, cross-links, unique names,
  and image decoding.
- `git diff --check -- WORLD_AND_LORE_PLAN.md world/CALDRIS-CREATIVE-CHARTER.md world/DND2024-LOW-MAGIC-TWO-CONTINENT-WORLD-IMPLEMENTATION-MAP.md world/caldris`
- No catalog validation, full test suite, or protocol walk is required because this slice changes no
  catalog, code, registration, or MCP surface.

## Completion receipt and exit gate

Evidence is recorded in the
[Slice 1 receipt](CALDRIS-AUTHORING-SLICE-1-RECEIPT.md). The authored pack and visuals passed the
stated structural checks. Human tabletop review remains the next content leaf. Stop before creating
runtime IDs, registering media, writing live state, or claiming the World/campaign is playable in
the application.
