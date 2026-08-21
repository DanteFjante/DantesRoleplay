# Campaign Feature 15 — Slice 1/2 receipt

Date: 2026-08-21

## Delivered boundary

- Verified Slice 1's closed campaign-owned participation state, canonical active-scope verifier,
  service registration, and focused structural tests.
- Added Slice 2's `attach-character-participation` operation behind the existing
  `commit(kind: "campaign")` family. Its closed payload has only `operation`, `campaignId`, and
  `actorId`.
- The server derives an opaque participation id, atomically creates the participation entity and
  active component, then creates the two canonical empty-data links. It never changes the actor.

## Explicitly not delivered

No character creation, profile, authentication, party/roster management, transfer/reactivation,
withdrawal, retirement, XP, authorization, class/level state, or level-up behavior. C15 Slice 3
remains the CH13-owned withdrawal composition seam.

## Evidence

- `dotnet test DantesRoleplay.Tests\\DantesRoleplay.Tests.csproj --no-restore --filter
  "FullyQualifiedName~CampaignFeature15"` — passed, 6/6.
- `dotnet build DantesRoleplay.DataAccess\\DantesRoleplay.DataAccess.csproj --no-restore` —
  passed, 0 warnings and 0 errors.
- `roleplay validate catalog` — valid disposable import, 253 records. No live data was touched.
- `git diff --check` over the C15 implementation paths — passed; repository line-ending notices
  were informational only.
