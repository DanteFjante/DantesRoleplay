# D&D code-adoption Slice 7A1 verification receipt — raw ability-score fixed-DC check

Status: **accepted 2026-08-25**
Implementation: [Slice 7A1 implementation](../../DND-CODE-ADOPTION-SLICE-7A1-IMPLEMENTATION.md)
Ruleset alignment: **dnd2024-owned**
Source: `source.dnd2024.srd-5.2.1`, `Playing the Game > The Six Abilities > Ability Scores/Ability Modifiers` (PDF pp. 5–6) and `Playing the Game > D20 Tests > Ability Checks/Difficulty Class` (PDF p. 6)

## Delivered boundary

- Authored the application-owned `dnd2024.abilities` schema and metadata.
- Authored the two confirmed D&D procedures and `mechanic.dnd2024.check.ability`.
- The mechanic accepts only ability/DC, derives `floor((score - 10) / 2)`, uses one kernel-seeded d20, compares the total to the DC, and proposes no effects, events, or notifications.
- Added a disposable-state integration test that registers `dnd2024-core`, previews and activates the normal source overlay, registers the component, executes the check, and proves replay.

## Verification

| Check | Result |
| --- | --- |
| Focused compile | `dotnet build DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore -p:BuildProjectReferences=false` — passed |
| Focused Slice 7A1 tests | `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-build --no-restore --filter FullyQualifiedName~Dnd2024AbilityCheckTests` — 3 passed |
| Catalog validation | `dotnet DantesRoleplay.Tools/bin/Debug/net10.0/roleplay.dll validate catalog` — valid; 21 pre-existing general warnings; no live data touched |
| Full test suite | `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-build --no-restore` — 948 passed |

The ordinary build route was initially blocked by the already-running local server holding its normal
output assemblies. The test project was compiled without rebuilding project references, then all
tests were run against that newly compiled test assembly. No service was stopped and no live
application database was changed.

## Deliberate exclusions

7A2 proficiency/skills, 7A3 Advantage/Disadvantage, 7A4 saving throws, and all later D&D gameplay
families remain unimplemented. The user confirmed Slice 7A1 feature acceptance on 2026-08-25;
this receipt records that decision and the bounded stop point.
