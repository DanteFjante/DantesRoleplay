# D&D 2024 Campaign Places Visited — Slice 19 receipt

Status: **DM read and trusted writer accepted; Player projection pending**
Date: 2026-08-30

## Delivered boundary

- Added the closed generic `game.core.campaign.location-visit` component schema. D&D runtime
  qualification is `dnd2024.game.core.campaign.location-visit`.
- Fixed the campaign ownership and exact location-target relationship contracts as
  `dnd2024.game.core.campaign.has-location-visit` and
  `dnd2024.game.core.campaign.location-visit.at-location`.
- The live adapter reads only exact DM visit records, validates safe minute/count/status/text fields,
  rejects ambiguous targets and duplicate campaign/location records, and joins only a World location
  already present in the DM projection.
- Places Visited now renders those records with canonical World name/region and campaign-minute labels.
- Added `dnd2024.mechanic.campaign.location-visit.record`. It derives the visit identity, reads the
  authoritative World clock, creates or updates the component and exact edges atomically, and
  requires the existing record role for updates.
- The writer enforces active campaign/World/location state, exact campaign World scope, prior
  campaign relevance, one derived campaign/location identity, monotonic minutes, and bounded count.
- Player and Player-preview remain empty and perform no raw visit read in this slice.
- No live campaign database was mutated during implementation or verification.

## Verification

- `roleplay validate catalog`: 154 records valid; 26 existing near-duplicate warnings; no live data touched.
- Direct sandbox smoke: visit creation and session-reference effects passed.
- Combined campaign-recording, namespace-containment, and owner-ledger run: 11 passed, 0 failed.
  The four campaign tests cover session/arc reference acceptance and rejection, derived visit
  creation/update, missing update role, and backward-clock rejection.
- `npm test`: 131 passed, 0 failed.
- `npm run build:server`: production bundle built successfully.
- Isolated `dotnet build src/system/web-interface/DantesRoleplay.Web/DantesRoleplay.Web.csproj`:
  succeeded with 0 warnings and 0 errors.
- Focused `git diff --check`: no whitespace errors; only line-ending notices.

## Remaining gates

- Player projection is blocked by A5's server-filtered campaign envelope; raw relationship IDs are
  deliberately not fetched for Player browsers.
