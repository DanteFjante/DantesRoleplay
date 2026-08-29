# D&D code-adoption Slice 7B–7D receipt — encounter and combat vertical

Date: 2026-08-25  
Status: **accepted — revalidated and hardened after Sol runtime review**
Boundary: Parent 7 / 7B–7D

## Delivered

- Added encounter Initiative-order and lifecycle components, declared child fan-out, explicit tie input, and start/advance/wrap/end mechanics.
- Added AC, HP, weapon-proficiency, and weapon-profile canonical writers; effect-free attack and damage mechanics; and composed target-HP application.
- Extended generic application action evaluation for declared pure child mechanics: recursively maps their declared components, executes stable bounded fan-out, materializes child results, and rejects unsafe child output proposals.
- Added a fresh SQLite-host acceptance path covering component recording, combat resolution, composed HP transaction, encounter order, and the full two-participant turn lifecycle.
- Closed the review follow-ups: component revisions now cover declared role, contained, and referenced projections; containment snapshots cover every container list materialized through the declared depth; and containment evidence is bounded and validated before transaction work.
- Preserved atomic setup mechanics by allowing a declared `component.add` only when its entity was created earlier in the same proposed effect batch. Existing or undeclared state still requires an exact projection-observed revision.
- Added regression evidence for bounded/malformed containment expectations, stale structural rollback, nested projection snapshots, and create-plus-component setup transactions.

## Verification

- `dotnet build DantesRoleplay.DataAccess/DantesRoleplay.DataAccess.csproj --no-restore` — passed, 0 warnings/errors.
- `dotnet build DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore -p:BuildProjectReferences=false` — passed, 0 warnings/errors.
- `dotnet build DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore` — passed, 0 warnings/errors.
- `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-build --no-restore --filter FullyQualifiedName~Dnd2024AbilityCheckTests` — passed, 13/13.
- Combined D&D, application-execution, ECS-effect, and Trail Survival regression filter — passed, 43/43.
- `node --check` over all D&D application JavaScript mechanics — passed, 17/17.
- `git diff --check` — passed; Git emitted only existing line-ending notices.
- The original Slice 7 `roleplay validate catalog` acceptance passed with 144 records valid and 21 advisory warnings. The current revalidation attempt is blocked before catalog inspection by unrelated pending EF model changes in the shared worktree; the validator uses a disposable database and did not touch live data.

## Broader verification boundary

The full test command completed with 988 passed and 21 failed out of 1009. No Slice 7, application-execution, ECS-effect, or Trail Survival regression failed. Twenty failures share the unrelated `PendingModelChangesWarning` from unfinished database-model/migration work in the dirty checkout; the remaining catalog-coverage failure names five new assistant-conversation columns that have not yet been classified. The same pending-model condition blocks the current disposable catalog validator. Those files and schema decisions are outside this D&D slice and were not changed here.

## Deliberate exclusions

No live campaign data, migrations, generic D&D rules in C#, imported donor module graph, conditions, reactions, spells, attacks beyond the bounded weapon seam, healing, or archive removal was introduced.

## Sol review

Final runtime review approved after the action owner was hardened to use projection-observed component revisions, transactionally recheck direct encounter containment snapshots, and bound both child execution and dependency traversal. This revalidation closes its recorded follow-ups: containment-expectation bounds and shape checks are enforced, stale/limit regression cases exist, and structural snapshots now extend through the complete declared projection depth.
