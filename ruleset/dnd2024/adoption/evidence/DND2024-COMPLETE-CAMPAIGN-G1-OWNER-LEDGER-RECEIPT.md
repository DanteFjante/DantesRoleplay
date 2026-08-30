# D&D 2024 complete-campaign G1 owner-ledger receipt

Status: **accepted**
Date: 2026-08-30
Owner/roadmap: [D&D 2024 roadmap](../../ROADMAP.md)
Implementation: [G1 owner-ledger implementation](../../DND2024-COMPLETE-CAMPAIGN-OWNER-LEDGER-IMPLEMENTATION.md)

## Delivered boundary

- Added deterministic `dnd2024-complete-campaign-owner-ledger/v1` evidence over 3,314 closed input
  files with SHA-256 `b63507bc0b6262b9b1f22da3664ad893feb3b81733b6418390b11a9427e03b33`.
- Added focused regression coverage for the fingerprint, 69 active mechanics, 13 active retired-
  contract mechanics, 14 duplicate tool identity groups, two current category anomalies, and the
  canonical world/campaign conflict evidence.
- Corrected the evidence path for the canonical component crosswalk and included the current server
  campaign-context binding in the closed audit input set.

## Verification

`dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --filter FullyQualifiedName~Dnd2024CompleteCampaignOwnerLedgerTests --no-restore`

Result: **4 passed, 0 failed, 0 warnings**.

## Deliberate exclusions and next gate

No database, live campaign/world/location, server authorization, schema, component, mechanic,
procedure, source-profile, migration, or UI state changed.

G7 remains conflicting: `game.core.*`, `dnd2024.game.core.*`, and `dnd2024.*` world/campaign shapes
need one confirmed canonical owner and one migration/transaction boundary before live world creation,
Thalos creation, country movement, or location CRUD can begin.
