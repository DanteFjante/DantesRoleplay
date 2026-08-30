# D&D 2024 campaign recording actions receipt

Status: **accepted for the DM Campaign projection**
Date: 2026-08-30

## Delivered boundary

- Added reviewed application actions that attach an exact already-relevant World entity to an ended
  session or a resolved/abandoned arc without changing retained component prose.
- Added the trusted location-visit action that derives one campaign/location visit identity, reads
  the authoritative World clock, and atomically creates or advances the aggregate visit record.
- All actions require active/terminal lifecycle state, exact campaign ownership, and existing
  campaign relevance. Visit update additionally requires the exact existing derived record and
  rejects backward time.
- Exact operation replay remains owned by the verified application action runner, so the same
  authorized operation cannot add a second link or increment a visit twice.
- The browser remains read-only and never infers links or visits from prose, maps, containment, or
  current location.

## Verification

- Catalog validation: 154 records valid with 26 pre-existing near-duplicate warnings; no live data
  was touched.
- .NET/Jint isolated sandbox: campaign visit creation and ended-session reference passed.
- Node smoke: campaign visit creation and ended-session reference passed.
- Complete D&D website suite: 131 passed, 0 failed.
- Production website bundle: built successfully.
- Combined campaign-recording, namespace-containment, and owner-ledger run: 11 passed, 0 failed.
  The four campaign tests cover session/arc reference acceptance and rejection, derived visit
  creation/update, missing update role, and backward-clock rejection.
- Full shared-suite acceptance was attempted. It remains unavailable because the known missing
  `dnd2024.weapon-profile.json` causes the broad D&D harness to fail repeatedly; unrelated current
  audience-lifecycle and weapon-damage contract tests also failed before that point. The run was
  stopped after 131 seconds rather than repeating the same missing-file failure across the matrix.

## Deliberate exclusions

- No live campaign state or database row was changed during acceptance.
- No session, recap, arc, clue, campaign membership, or World membership is created by these actions.
- Player Places Visited remains empty until A5 supplies a server-filtered campaign envelope; the
  Player browser is not allowed to enumerate raw visit relationships.
