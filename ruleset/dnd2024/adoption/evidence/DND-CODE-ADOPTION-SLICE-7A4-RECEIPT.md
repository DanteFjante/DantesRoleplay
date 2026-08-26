# D&D code-adoption Slice 7A4 receipt — saving throws

Date: 2026-08-25  
Status: **verified — final Sol review pending**
Boundary: Parent 7 / 7A4 only

## Delivered

- Added closed, canonical `dnd2024.saving-throw-proficiencies` state and its add-or-set recorder.
- Added a fixed-DC saving-throw resolver using ability, character-level, save-proficiency, and explicit 7A3 roll-circumstance state.
- Supports voluntary no-roll failure; saves remain effect-free and do not decide a threat's consequence.

## Verification

- `dotnet build DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore -p:BuildProjectReferences=false` — passed, 0 warnings/errors.
- `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-build --no-restore --filter FullyQualifiedName~Dnd2024AbilityCheckTests` — passed, 9/9 activated-host tests.
- `roleplay validate catalog` — presently blocked by unrelated dirty migration state: `trigger_phone_device_structure_scope_insert` refers to missing `trigger_observation_structure`. Earlier 7A3 catalog validation passed before this migration failure appeared.
- The complete suite is likewise blocked by the same unrelated pending migration/trigger-scheduling worktree changes recorded in the 7A3 receipt.

## Deliberate exclusions

No class grants, monster CR, persistent conditions, rerolls, spell/hazard definitions, or applied save consequences were introduced.
