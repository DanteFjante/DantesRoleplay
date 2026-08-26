# D&D code-adoption Slice 7A3 receipt — explicit Advantage and Disadvantage

Date: 2026-08-25  
Status: **verified — final Sol review pending**
Boundary: Parent 7 / 7A3 only

## Delivered

- Extended `mechanic.dnd2024.check.ability` with closed explicit `rollCircumstances` input.
- Implemented deterministic non-stacking Advantage/Disadvantage: two d20s and max/min for one kind; one d20 for absent or mixed kinds.
- Preserved raw and named-skill arithmetic, effect-free execution, and kernel-seeded replay; added audit output for mode, all rolls, selected roll, and circumstances.
- Deferred persistent/derived condition sources and all other D20-test families.

## Verification

- `dotnet build DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore -p:BuildProjectReferences=false` — passed, 0 warnings/errors.
- `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-build --no-restore --filter FullyQualifiedName~Dnd2024AbilityCheckTests` — passed, 7/7.
- `dotnet DantesRoleplay.Tools/bin/Debug/net10.0/roleplay.dll validate catalog` — valid, 144 records; 21 pre-existing catalog warnings.
- Full `dotnet test ... --no-build --no-restore` — blocked by unrelated dirty-worktree failures: pending EF migration/model drift, trigger-scheduling constructor mismatch, and missing `trigger_observation_structure.DataClassification` migration column. The Slice 7 focused suite passed before that unrelated failure surface.

## Review handoff

Final Parent 7 review is explicitly requested from Sol after the remaining child slices complete.
