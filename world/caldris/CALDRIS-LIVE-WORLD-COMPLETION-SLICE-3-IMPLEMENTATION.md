# Caldris live World completion slice 3 — full reference corpus

Status: **accepted 2026-08-30**
Owner/roadmap: application World state, authorized knowledge, chronology, and D&D web presentation
Dependency tree/leaf: replay-safe `system.world-state.sync` transactions beneath `world.caldris`
Ruleset alignment: `dnd2024-compatible`; authored setting and campaign-reference state only
Source ID and locator: not applicable; no D&D rule calculation is implemented
Outcome: the reviewed Caldris corpus becomes navigable live World reference material
Exclusions: executable stat blocks, automated travel/economy/weather, and hidden Player-byte
projection not already supported by the current owners
Allowed areas: this implementation document and receipt; additive Caldris manifests; reviewed
Caldris map/media bytes and registry; live Caldris World/campaign records through existing owners
Stop point: all reviewed places, history/lore, cast, factions, dragons/creatures, campaign volumes,
and prepared quest packets have either been imported through their existing owner or recorded as an
explicit retained gate with no false claim of live implementation

## Confirmed decisions

The user's instruction to add the remaining reviewed world and campaign confirms the permanent
Caldris IDs, additive live imports, campaign continuity records, prepared quest records, required
page-bundle publication, and reviewed map/media bindings inside this boundary. Existing IDs and
state remain intact. Every mutable package is previewed before commit and backed up before the first
write.

## Source corpus

- `CALDRIS-WORLD-BIBLE.md`: continents, twelve polities, seven eras, faith, magic, dragons, and
  creature traditions.
- `CALDRIS-GAZETTEER.md` and `CALDRIS-EXPANDED-LOCATIONS.md`: thirty-six primary cities and
  forty-eight second-ring places, in addition to the starting region already live.
- `CALDRIS-HISTORY-AND-LORE-ATLAS.md`: twenty-four incidents and twenty-four living customs or
  disputed beliefs.
- `CALDRIS-CAST-AND-FACTIONS.md` and `CALDRIS-ADDITIONAL-CAST.md`: ninety-five character anchors,
  four campaign-spine figures, and thirty-four factions.
- `CALDRIS-CAMPAIGN-TAPESTRY.md`: four enduring threads, six volumes, four boss ladders, consequence
  paths, cozy returns, and independent adventure bridges.
- `CALDRIS-QUEST-ATLAS.md` and `CALDRIS-ADDITIONAL-QUESTS.md`: Q01–Q48 with three objectives,
  alternate approaches, redundant clues, recoverable failure, and next-story doors.

## Transaction decomposition

The existing synchronizer accepts at most 64 entities, 64 relationships, and 128 derived effects.
The corpus is therefore divided by stable ownership and containment, never by arbitrary partial
effects:

1. Geography A: twelve polities and their thirty-six primary cities.
2. Geography B/C: forty-eight second-ring places.
3. History/lore: seven-era completion plus forty-eight incident/custom records.
4. Creatures: six named dragons and twenty creature traditions as narrative creature/fact records.
5. Cast A/B/C: remaining starting, wider, second-ring, and campaign-spine characters.
6. Factions A/B: remaining polity and cross-border institutions with exact links.
7. Campaign continuity: the opening arc and chapter through the registered generic World-state
   owner, preserving the single-active-arc and single-active-chapter invariant.
8. Prepared adventures: Q01–Q48 as GM-visible campaign seeds beneath the Caldris atlas. The generic
   live host does not register the legacy quest lifecycle owner, so these are not misreported as
   lifecycle-managed quest roots.

Each package has a unique 32-character lowercase hexadecimal request token, exact expected
revisions, one preview, one identical commit, and live readback before the next dependent package.

## Authority and projection

SQLite remains runtime authority. Markdown remains review-source evidence and is not queried by the
website. Public facts, places, chronology, and public motives may be projected to the appropriate
audience only through existing readers. GM-only secrets, ladders, and hidden sponsorship never
become public lore merely to make them visible in the interface.

## Failure and rollback

Any stale revision, unknown schema, missing parent, duplicate relationship, over-limit package, or
owner rejection stops that package without a partial write. The pre-slice SQLite backup and active
page-bundle export are retained. Later packages depend only on committed readback, never on planned
IDs.

## Acceptance

- All twelve polities and their reviewed places appear under the correct continent.
- History and lore retain public/disputed/hidden distinctions and present consequences.
- People and factions show the reviewed personality, motive, membership, and territory material
  supported by current schemas.
- Dragons and mythical creatures remain rare narrative presences, not everyday infrastructure.
- The opening arc and chapter are resumable from stored state, and all forty-eight prepared
  adventures are searchable as GM campaign seeds.
- Every retained runtime limitation is named; prose-only material is never reported as live.
- Focused website and host tests, live readback, browser inspection, and replay checks pass for every
  accepted package. No catalog record changed in this slice, so catalog validation was not required.

## Completion receipt

See `CALDRIS-LIVE-WORLD-COMPLETION-SLICE-3-RECEIPT.md`.
