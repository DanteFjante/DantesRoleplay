# Caldris playable opening slice 4 — receipt

Accepted: 2026-08-30

## Delivered boundary

The Caldris campaign is now immediately runnable for its first three sessions. The DM receives a
storybook-toned handbook with scene flow, opening prose, NPC voices, clue redundancies, a riddle,
creative approaches, failure-forward branches, character hooks, cozy closing beats, and choices
that can redirect later adventures without inventing player decisions.

Campaign → Quests now projects all forty-eight authorized `QNN — title` preparation records as
adventure cards. `Q01 — The Thirteenth Bell` appears first as the active opening adventure with
three actionable objectives. Q02–Q48 appear as prepared adventures without fabricated progress.
When campaign seeds are absent, the previous public party-goal cards remain the safe fallback.

The browser audience check proved the boundary in both directions:

- the DM sees forty-eight prepared pursuits and three investigation clues;
- the Player sees only the three existing public party goals and no unrevealed clues;
- malformed preparation text produces a minimal honest card rather than invented content.

## Opening packet and live state

`CALDRIS-THIRTEENTH-BELL-SESSIONS-1-3.md` contains the run-ready packet for:

1. **Rain at Noon**, the apparently driverless tax-wagon incident in Bramblebridge;
2. **Above the Market**, an investigation through civic embarrassment, theatre rigging, and
   competing witnesses;
3. **Six Quiet Minutes**, the bell-tower confrontation and its first meaningful campaign choice.

The governed additive manifest
`runtime/caldris-playable-opening-sessions-v4a.json` created three GM preparation records and three
unrevealed clue records with twelve relationships. Its preview reviewed six entities and thirty-six
effects. The identical commit succeeded without replay.

- Preview effect operation: `4e16d9213dc70a2c346d89a4aeeed8f0`
- Commit effect operation: `02f834f84a0d805b508f8400c1dbf6ba`
- Commit MCP operation: `7005634f968a4662b4ab2ea5ab61f572`

SQLite remains live authority. No direct database edit, new protocol kind, component type,
relationship kind, migration, or rules mechanic was introduced.

## Visual packet

The built-in image generator produced and the handbook references three reviewed storybook assets:

- `visuals/portrait-tibb-fallow-v1.png` — Tibb Fallow in his rain-dark watch cloak, map and sonnet
  in hand; SHA-256 `3AE0336E89155A4A00FB0D982AE903BF9ED1137CD292B097B252FD38C1501FA3`.
- `visuals/location-bramblebridge-thirteenth-bell-v1.png` — the rainy market and runaway tax
  wagon beneath Bramblebridge's bell tower; SHA-256
  `254C6488C19C30A6A1093AA7B32DA22A0AFA784DA047AEDFB1E7A590EF69002B`.
- `visuals/item-thirteen-stroke-token-v1.png` — a weathered barge token bearing exactly thirteen
  single notches; SHA-256
  `F4B9B932C8357B0FA468DBE81C150ACAA31056DEF510C3CD943139AECDF20F13`.

The prompts consistently requested grounded historical storybook watercolor/gouache, ordinary
medieval materials, humane humor, rare rather than spectacular magic, and no written labels. The
token prompt was refined until its evidence-critical count was exactly thirteen.

## Published presentation

Website page revision 24 was published from
`runtime/backups/dnd2024-play-caldris-playable-opening-v4-final.zip`, SHA-256
`FE46BFB0D2E786C4A584CB7251F88EA5FE4C960CCCD106D09CF54730D2E33A95`.
Browser inspection at the published page confirmed Q01 first, Q02 prepared, forty-eight of
forty-eight DM pursuits, three of three DM clues, the closed Player projection, and a successful
World overview after returning to the DM perspective.

## Verification

- Website JavaScript tests: 141 passed, 0 failed.
- Focused host web-interface tests: 90 passed, 0 failed, using the accepted built binaries while
  the local site retained its file lock.
- Server build completed before publication and produced the final revision-24 bundle.
- Manifest parse: 6 entities and 12 relationships.
- Published website readback: HTTP 200 with the local server still running.

An attempted rebuilding host-test run could not replace assemblies held by the running website;
this was an environmental file lock, not a test failure. The acceptance rerun used `--no-build`
against the same accepted binaries and passed all ninety focused tests.

## Deliberate retained gates

The current generic host still does not register the former specialized quest-lifecycle owner.
The forty-eight cards are therefore honest projections of live GM preparation records, not falsely
claimed lifecycle-managed quests. No adventure has been marked played or completed.

Numerical encounters, creature stat blocks, item mechanics, DCs, damage, rewards, automated travel,
economy and weather, completed-session recaps, and quest-lifecycle restoration remain outside this
slice. The next coherent campaign-content slice may add rules-aligned Session 1–3 mechanics and
rewards; lifecycle restoration remains a separate platform dependency.
