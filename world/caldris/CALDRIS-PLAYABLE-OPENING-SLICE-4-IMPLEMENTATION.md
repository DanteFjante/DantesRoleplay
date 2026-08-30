# Caldris playable opening slice 4 implementation — Sessions 1–3

Status: **accepted 2026-08-30**
Owner/roadmap: Caldris implementation map; D&D 2024 web campaign presentation; application World knowledge
Dependency tree/leaf: `CALDRIS-PLAYABLE-OPENING-DEPENDENCY-PLAN.md`, quest-card projection and opening packet
Ruleset alignment: `dnd2024-compatible`
Source ID and locator: not applicable; no D&D rule calculation is implemented
Outcome: a DM can browse all prepared adventures and run Sessions 1–3 of the opening mystery
Exclusions: lifecycle-managed quest mutation, numeric encounters/stat blocks, automated rewards,
travel/economy/weather simulation, completed-session history, and invented player choices
Allowed files/areas: this document and receipt; Caldris session/handout documents, additive reviewed
World manifests, Caldris visual assets, D&D 2024 connected campaign presentation and focused tests,
and a page-bundle publication
Stop point: Q01–Q48 render honestly in Campaign → Quests; Sessions 1–3 and their visual packet are
reviewed and live for the DM; no lifecycle or play outcome is fabricated

## Confirmed decisions

The user's request confirms the new Session 1–3 reference IDs, Campaign quest-card behavior, visual
assets, additive live import, and page publication within this boundary. Existing records are not
deleted or reclassified.

## D&D 5e 2024 alignment

| Concern | Source meaning | Existing owner | Consequence |
| --- | --- | --- | --- |
| Checks, combat, damage, rests, rewards | not implemented by this slice | existing D&D owners | prose offers situations and approaches but no derived DC, modifier, damage, or result |
| Campaign presentation | application workflow, not an SRD rule | connected D&D web adapter | project only authorized state and label preparation honestly |

## External implementation reference

No Foundry dnd5e inspection is required because this slice implements no D&D rule or mechanical
state. A later numerical encounter slice must perform the full SRD/Foundry alignment gate.

## Prerequisite evidence

Slice 3 proves 104 places, 103 knowledge entries including Q01–Q48, 124 people/creatures, 34
factions, and the active opening arc/chapter. Existing website tests prove audience filtering and
campaign projection boundaries.

## Runtime artifacts

- Three new GM preparation records beneath the active chapter, one per planned session.
- Three open handout/evidence records revealed only as authorized by existing projection.
- A reusable Session 1–3 handbook in the Caldris authored package.
- One Tibb Fallow portrait, one Bramblebridge market scene, and one bell-token/item handout image.
- No new component type, relationship kind, migration, catalog rule, or protocol kind.

## Authoritative state and closed input

SQLite remains live authority. The import is one closed `system.world-state.sync` manifest with an
exact application, state space, campaign root, expected revisions, and additive entities. The web
adapter receives only its already-authorized knowledge projection; it cannot request hidden data.

## Behavior, result, and typed effects

Quest cards are derived from titles matching `QNN — title`. Bounded labels (`Hook`, `Layers`,
`Objectives`, `Routes`, `Clues`, `Riddle`, `Creative constraints`, `Failure forward`, `Aftermath`)
are parsed for display without changing source text. Q01 is `Active` because it matches the active
chapter; all other cards are `Prepared`. When no matching records exist, the existing campaign-goal
cards remain the fallback.

The live import creates additive knowledge entities and containment only. Preview precedes the
identical commit. Visual files are reviewed before project copies are referenced by the handbook.

## Failure, replay, and rollback contract

Malformed seed text yields a minimal prepared card rather than invented fields. Unauthorized seeds
never reach the adapter. Invalid IDs, stale revisions, unknown types, size limits, or blocked effects
reject the entire manifest. The request token is replay-safe. Existing page revision 21 remains the
rollback point for presentation.

## Implementation sequence

1. Add focused quest-projection tests and the smallest adapter change.
2. Author the Session 1–3 handbook and exact additive manifest.
3. Generate and inspect the three visual assets; save project-bound copies.
4. Preview and commit the manifest, build and publish the page bundle.
5. Run focused/full acceptance, inspect the live website, and write the receipt.

## Acceptance matrix

| Case | Expected evidence |
| --- | --- |
| Q01 | active card with three parsed objectives and DM layers |
| Q02–Q48 | prepared cards; no fabricated progress |
| no seeds | existing party-goal fallback remains |
| Player projection | no GM seed text crosses the authorized boundary |
| malformed seed | safe minimal card, no exception or invented objective |
| live import | preview and identical commit; six new records readable |
| visuals | three inspected image files in the project with hashes |
| browser | Campaign → Quests shows 48 pursuits and Q01 details |

## Verification commands

- `npm test` and `npm run build:server` in the D&D 2024 web application.
- Focused host web-interface tests.
- JSON parse and governed live readback for the additive manifest.
- Browser inspection of Campaign → Quests, Lore/handouts, and the campaign overview.

## Completion receipt and exit gate

Accepted evidence is recorded in `CALDRIS-PLAYABLE-OPENING-SLICE-4-RECEIPT.md`. The slice stops
before numeric encounters, rewards, lifecycle mutation, or recording any session as played.
