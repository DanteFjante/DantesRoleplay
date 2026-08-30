# D&D 2024 general media projection Slice 23 — completion receipt

Status: **accepted**
Accepted: **2026-08-30**
Owner: `web/WEB-INTERFACE-ROADMAP.md`, Feature 5; World-tab completion E1–E2
Ruleset alignment: **dnd2024-compatible presentation; no D&D rule change**

## Delivered boundary

- Added the generic `game.core.world.media.visual` component and
  `procedure.game.core.world.media` contract with closed portrait, setting, scene, and handout
  slots; exact Player/DM variants; reviewed provenance; dimensions; MIME; SHA-256; and lifecycle.
- Added fail-closed D&D 2024 projection and presentation for portraits, location settings, exact
  current-scene precedence, and authorized clue handouts. Browser output contains only resolved
  URL, alt text, width, and height; asset keys, provenance, hidden variants, and private hashes are
  not serialized.
- Retained an already-authorized notebook entry's own media owner through the host adapter so a
  clue may own its handout even when its visible subject is a location. Familiar and excluded
  entries retain no owner ID, and the temporary owner is stripped before the page envelope.
- Served reviewed bytes from immutable content-addressed `/components/media/` routes. Maps remain
  separately owned and unchanged.
- Bound the reviewed Tibb Fallow portrait, Bramblebridge setting, and thirteenth-stroke token to
  their three existing live Caldris entities. No entity, reveal state, participant, scene selector,
  or inferred association was created.

## Governed live evidence

- Pre-write online backup:
  `world/caldris/runtime/backups/dantesroleplay-pre-visual-media-v5a-20260830.db`;
  `PRAGMA quick_check=ok`.
- Component registration version 1 used request/operation
  `ca1d215e000000000000000000000024`; schema hash
  `3BD104CBD710315B4E1054BAF71A0FD40E1C7EB457E7269D49C2A271852CB2CD`.
- Application preview was valid with fingerprint
  `6165FA322AC6340EE38040F31A316B3452DFBDC20D51F6A8CCD1E779808D1C04` and zero problems.
  Activation request/operation `ca1d215e000000000000000000000025` produced revision 12 and
  fingerprint `093246482C7162FA9D728E74C860ADC1C02C42266F8577A4602DC038C225D3CC`.
- The first broad Caldris sync dry-run failed closed at the existing `WORLD_SCOPE_TOO_LARGE` gate
  and wrote nothing. The reviewed manifests were split by exact safe scope and then dry-run/commit
  matched:
  - Alderwick: effect `58ab8f7de117d97583e029d7b01d4520`, outer operation
    `0020a03592fb4674b3db3585d990be4a`;
  - exact clue: effect `1455520059a5c5d2935645d6c3371092`, outer operation
    `5022ba48fb734cd7923a9b4cb120ba92`.
- Readback found all three media components at revision 1 with the registered schema hash.
- Post-verification online backup:
  `world/caldris/runtime/backups/dantesroleplay-post-visual-media-v5a-verified-20260830.db`,
  2,441,216 bytes, SHA-256
  `53ECFA3FB72DC01BFABF0F58FF61474052A0D584738D30241FC6BB86CE950236`,
  `PRAGMA quick_check=ok`.

## Publication and browser evidence

- Exported active revision 25 before publication to
  `dnd2024-play-pre-visual-media-v5a-revision25.zip`; SHA-256
  `44E21FAD0C7AA9C015756356C07245F58DC30EBD8F6C2EF84E74635885019996`.
- Published five-asset revision 26 from
  `dnd2024-play-caldris-visual-media-v5a-revision26.zip`, 7,840,254 bytes, SHA-256
  `2B325D81047CAC8E6ED302BA9FCCAC7FEC5E63171B860E8C45BA793039933FD5`.
- Live DM browser verification showed Tibb's 1024×1536 portrait, Bramblebridge's 1536×1024
  setting/current-scene plate, and the clue's 1254×1254 token handout loaded from their exact
  immutable hash routes.
- Live Player-preview verification showed zero clue and image bytes for the unavailable private
  notebook, while the illustrated Caldris world map and closer-map controls remained usable.

## Verification

- `roleplay validate catalog`: 158 valid records, 28 existing near-duplicate warnings, no errors.
- D&D website: 144/144 tests passed; production server bundle built successfully.
- Web host and admitted-notebook boundary: 91/91 focused tests passed.
- Catalog/component/protocol count evidence: 4/4 focused tests passed after adopting the current
  24-procedure, 39-component, 134-winner catalog totals.
- Repository acceptance excluding two independently failing D&D fixture owners: 1,166/1,166 tests
  passed. The unfiltered run was also attempted; it remains red because the checkout does not
  contain the pre-existing `catalog/applications/dnd2024/content/entities/character-creation`
  fixtures and the unrelated weapon-damage contract test fails on its existing input contract.
  Neither failure touches this slice's files or behavior.
- `git diff --check`: no whitespace errors.

## Deliberate exclusions

No media authoring/upload surface, new content entity, clue reveal write, generated session/chat
history, inferred scene/participant, map-owner change, D&D mechanic, migration, or NPC biography was
added. Those remain separate confirmed boundaries.
