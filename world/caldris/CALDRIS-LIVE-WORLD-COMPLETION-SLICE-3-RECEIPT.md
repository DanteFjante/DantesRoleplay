# Caldris live World completion slice 3 — receipt

Accepted: 2026-08-30

## Delivered boundary

The live Caldris campaign now exposes a two-continent low-magic medieval setting with twelve
polities, thirty-six primary cities, forty-eight second-ring places, and the existing starting
region. The website reads 104 known places across fifteen inferred regions.

The accepted live reference also contains:

- 29 chronology events, combining pre-kingdom eras and polity-level incidents;
- 103 lore entries: living customs, concealed historical layers, existing lore, and Q01–Q48
  prepared adventure packets;
- 124 visible people and creatures: the existing cast, ninety added actors, six named dragons, and
  twenty rare mythical or folk creatures;
- 34 visible factions with motives and exact recorded influence;
- one active opening arc, `Volume I — Thirteen Bells`, and one active chapter,
  `The Thirteenth Bell`, under `The Measure of Mercy`.

The campaign seeds retain layered reveals, misleading apparent bosses, higher sponsors,
independent next-adventure doors, redundant clues, recoverable failure, riddles, social routes, and
creative non-combat solutions. Their tone mixes cozy ordinary life and humour with realistic
consequences, grief, political pressure, and rare magic.

## Governed import evidence

Every mutable package was previewed before its identical commit through
`system.world-state.sync`. None of the commits was a replay. Accepted effect operation IDs are:

- geography: `7dfb6997b8431e9169164ee9a1c8af06`,
  `e858ea6708661282856023c3fe8ae826`, `f0c84dfeba24f4a293b4648de05f2ec5`,
  `acf01dc9afaf197a709b7121190c0da1`, `45122ebd592d481d40b03042042ff4f7`;
- history and lore: `65cd06f00194e294dd8d44b0b59502c8`,
  `02c13a7d461cf548b0c64c6f49ce0281`, `ba5fc88eb3cabe381400a0feb4fc2cf5`,
  `25a68f06a619931f8ae0ff543329df6a`, `4bf80041d93a5f7f02adf016c7a1e0bd`;
- creatures and cast: `d475d2e87ba3d2d76f78f4ea09f1736a`,
  `33c25069949ebef90fef67ddae745d96`, `e4a9ba68eff2ec22efbc9d0d948eed76`,
  `09a9ddf930a101e0fb141ae035240f8b`;
- factions: `925f7e8f9880704c68af70b909d8ed8a`,
  `0f052a6634b9565766c7298af415b6bb`, `d223e01d2f42dccaaa11648a624b4ccb`,
  `a99efff8a47eb9cdcf24d1f7f285735d`;
- campaign and prepared adventures: `2f42aeb6881a612f9de4a6a9f4151345`,
  `21fce6f16099cddd4813acd9074354ee`, `5294e71569d346657396b7388378194f`,
  `51d030c8aaf6f5dba0bfa5c228e623c7`;
- atlas media and polity anchors: `7f27138173de9f6c9d56d9a095185a83`,
  `5eafe2a9afde1d827a69d353bb175ebb`.

The first hierarchy shape exceeded the generic 100-subject snapshot gate. The gate was preserved;
the server was stopped, the verified pre-slice database was restored, and history, lore, and
factions were replayed beneath their owning polities. Recovery evidence is retained in
`runtime/backups/dantesroleplay-partial-v3-before-hierarchy-replay-20260830T1325.db` and
`runtime/backups/dantesroleplay-pre-caldris-world-completion-v3-20260830T1330.db`.

## Maps and presentation

The built-in image generator produced reviewed illustrated parchment atlases for Eredane and
Solasca. Project copies are `visuals/map-eredane-continent-v1.png` and
`visuals/map-solasca-v1.png`. Their SHA-256 hashes are respectively
`FD5B8AE00AA1A9B5A6A05C9453ADB9783BE3B67D282DEFD6CC4365BDE8FFCB5A` and
`D0CE7784BB500AC0C236FE83A6DA6542EB10A6D786B253BDC80DE752D550DF6B`.

Website page revision 21 was published from
`runtime/backups/dnd2024-play-caldris-atlas-v3-revision21.zip` (SHA-256
`44559617AB4A6CB1AC2794D34BED9BA1C139F74BE786F99A727621DA61079C8D`). Browser verification opened
both closer maps and confirmed six of six polities on Eredane and six of six on Solasca.

## Verification

- Website JavaScript tests: 138 passed, 0 failed.
- Focused host web-interface tests: 90 passed, 0 failed.
- Host build completed and both new atlas images were present in the served output.
- Live browser readback: 104 places, 29 history events, 124 people/creatures, 34 factions, and 103
  lore entries.
- Live browser search returned `Q01 — The Thirteenth Bell` with its hook, reveal layers,
  objectives, routes, clues, and creative constraints.
- Campaign overview returned `Volume I — Thirteen Bells` and `The Thirteenth Bell` as the active
  continuity records.

## Deliberate retained gates

The generic live MCP host does not register the legacy specialized campaign/quest lifecycle owner.
Consequently, Q01–Q48 are live, searchable GM campaign seeds rather than forty-eight falsely
claimed lifecycle-tracked quests. The Campaign → Quests view therefore retains its existing
prototype records. Only the opening arc and chapter are active; later volumes remain fully authored
in the reviewed campaign tapestry and the forty-eight stored seeds until a registered lifecycle
owner can represent them honestly.

Executable encounters, stat blocks, item mechanics, completed session recaps, and automated
travel, economy, or weather are outside this accepted slice. Player preview continues to fail
closed wherever the current host cannot prove an authorized projection.
