# D&D 2024 Campaign World references — Slice 18 receipt

Status: **verified**
Date: 2026-08-30

## Delivered boundary

- Authorized non-familiar knowledge entries may carry the exact hydrated subject ID and name;
  familiarity still carries no subject.
- The live D&D adapter reads
  `dnd2024.game.core.campaign.record.references-world-entity` only for ended sessions, closed
  chapters, and terminal arcs in DM perspective.
- Adventure logs, outcomes, and evidence clues convert those exact IDs to the existing Campaign
  links only when the target is already present in the audience-projected World.
- Unknown, cross-world, malformed, unavailable, and unauthorized targets produce no link.
- No recap bytes, live relationship, component, or database row was changed.

## Follow-up write acceptance

The later campaign-recording slice added reviewed post-closure actions for ended sessions and
terminal arcs. Each action can create only the empty-data relationship already defined here, only
to an entity already present on the active campaign's exact `campaign.references` edges. The recap
and arc component bytes remain unchanged, and action-operation replay prevents duplicate commits.

## Verification

- `npm test`: 115 passed, 0 failed.
- `npm run build:server`: production bundle built successfully.
- Isolated `dotnet build src/system/web-interface/DantesRoleplay.Web/DantesRoleplay.Web.csproj
  --no-restore --artifacts-path .tmp/artifacts-s18`: succeeded with 0 warnings and 0 errors.
- Focused shared C# test execution was attempted after an isolated restore, but the current test
  assembly does not compile because `CatalogWorldFeature19Tests.cs` references the unrelated missing
  `JourneyPlanReader` and `ModeAwareItineraryReader` types. The affected production projects compile;
  this receipt does not represent the blocked C# tests as passing.
- Focused `git diff --check`: no whitespace errors; only existing line-ending notices.

## Deliberate exclusions

This slice did not restore a retired adapter, infer relevance from prose, or treat any reference as
a visit. Reference capture remains a separate reviewed action after lifecycle closure rather than a
mutation of retained recap or outcome prose.
