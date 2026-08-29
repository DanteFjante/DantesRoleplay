# D&D code-adoption Slice 9B receipt — stateless core character calculations

Date: 2026-08-26  
Status: **accepted; repository-wide hold cleared by Slice 9C**
Boundary: `mechanic.dnd2024.character-sheet.read` and
`procedure.mechanic.dnd2024.character-sheet`

## Delivered

- Activated the two permanent IDs after explicit user confirmation on 2026-08-26.
- Added one empty-input, read-only character-sheet mechanic over exactly the current abilities,
  total character level, skill-proficiency, and saving-throw-proficiency components.
- Derived level-based Proficiency Bonus, six canonical ability score/modifier entries, six saving-
  throw entries, eighteen skill entries, Dexterity initiative, and base Passive Perception with a
  complete breakdown. Flat modifier maps remain present as normalized conformance fields.
- Kept every derived field ephemeral. The mechanic consumes no RNG and emits no effects, events,
  or notifications; repeated action execution replays without component changes.
- Preserved the pinned donor's MIT notice and recorded exact per-symbol source and target hashes.
  Donor campaign state, content packs, effect stacks, spellcasting, AC/HP, inventory, RNG,
  persistence, and dependencies were not imported.
- Closed all seventeen donor derivation groups in `slice-9-closure.json`: one activated cohort,
  retained current owners, explicit later owners, two rejections, and zero unresolved groups.

## Verification

- All D&D catalog JavaScript syntax checks — passed, 52/52.
- Focused character-sheet, wrapper, and closure tests — passed, 16/16, including all ten
  Proficiency Bonus boundary levels.
- Full activated D&D suite — passed, 75/75.
- Combined D&D, application-execution, ECS-effect, and Trail Survival seam suite — passed, 99/99.
- SRD/adapted conformance — passed, 12/12 comparisons.
- Donor/adapted conformance — passed, 12/12 comparisons.
- Provenance and source-vector Draft 2020-12 schema validation — passed.
- `dotnet build DantesRoleplay.slnx --no-restore` — passed with 0 warnings and 0 errors.
- `roleplay validate catalog` — passed, 144 core records with the same 21 advisory warnings; no
  live data was touched. Fresh D&D source preview and activation also pass in every focused harness.
- Local-AI suite — passed, 20/20.
- `git diff --check` over the Slice 9 boundary — passed.

## Cleared parent acceptance hold

The original main repository run completed with 1,064 passing and three failures outside Slice 9:

1. six new `system_task*` tables are not yet classified for catalog round-tripping;
2. their columns are likewise not yet classified; and
3. `src/system/system-task-orchestration/component.json` is incomplete under the component-manifest
   contract.

Those files were concurrent user-owned work and were deliberately left unchanged. On 2026-08-26,
Slice 9C reran the current repository and recorded 1,106/1,106 shared tests plus 21/21 local-AI
tests passing. The hold is cleared and Parent Slice 9 is accepted.

## Deliberate exclusions

No stored projection, component, schema, migration, public operation, C# D&D rule, live-state
write, contextual Advantage/Disadvantage, expertise, half proficiency, temporary modifier,
spellcasting, static content, terrain, multiattack, or donor production dependency was added.
