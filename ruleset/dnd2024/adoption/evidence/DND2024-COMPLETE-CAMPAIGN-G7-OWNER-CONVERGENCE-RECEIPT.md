# D&D 2024 complete-campaign G7 owner convergence receipt

Status: **accepted**
Date: 2026-08-30

## Delivered boundary

G7 now records one D&D runtime identity: `dnd2024.game.core.*`. Reusable `game.core.*` IDs are
catalog-local mapping keys, and `dnd2024.world.root` / `dnd2024.campaign.root` are migration-only
prototype inputs. No live entity, component, relationship, schema, or migration was changed.

## Evidence

- [Convergence matrix](complete-campaign-world-campaign-owner-convergence.json) records the exact
  world, location, and campaign identities and dispositions.
- [G7 implementation document](../../DND2024-WORLD-CAMPAIGN-OWNER-CONVERGENCE-IMPLEMENTATION.md)
  records the user-confirmed decision and exclusions.
- Direct readback confirmed the D&D knowledge binding and game server use
  `dnd2024.game.core.world.root`, `dnd2024.game.core.world.location`, and
  `dnd2024.game.core.campaign.root`; the application projection resolver maps local contract keys
  through `mapping.Components`.
- `dotnet test` compiled `Dnd2024WorldCampaignOwnerConvergenceTests` in an isolated output folder.
  The compiled x64 test host could not start because this machine lacks an x64 .NET runtime; this is
  an environment limitation, so execution is not represented as a passing test.
- `dotnet DantesRoleplay.Tools/bin/Debug/net10.0/roleplay.dll validate catalog` passed: 145 records
  validated, with 24 pre-existing near-duplicate warnings and no live-data access.
- JSON syntax and `git diff --check` passed. The latter only reported existing line-ending notices.

## Deliberate exclusions and next gate

No legacy record is treated as migrated. G8/G9 and the verified full-state backup/migration leaves
remain required before live world creation, Thalos/country authoring, campaign creation, or any
prototype-owner rewrite.
