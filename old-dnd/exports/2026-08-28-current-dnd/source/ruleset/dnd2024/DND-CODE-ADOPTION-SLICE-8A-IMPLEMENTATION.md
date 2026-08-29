# D&D code-adoption Slice 8A implementation — creature Speed profile

Status: **accepted**  
Owner/roadmap: [D&D 2024 application roadmap](ROADMAP.md), native-recovery lane  
Dependency tree/leaf: [D&D code-adoption dependency plan](DND-CODE-ADOPTION-DEPENDENCY-PLAN.md), Slice 8 / first archived mechanic family  
Ruleset alignment: `dnd2024-owned`  
Source ID and locator: `source.dnd2024.srd-5.2.1`, `Rules Glossary > Speed` (PDF p. 188; PDF page index 187)  
Outcome: Recover one authoritative creature base-Speed profile and its administrative write and diagnostic read mechanics.  
Exclusions: Turn-budget refresh/spending, conditions and temporary Speed changes, grid position, paths, terrain, travel pace, jumping, reach, fixtures, migrations, public protocol changes, donor runtime code, and archive removal.  
Allowed files/areas: `catalog/applications/dnd2024/` Speed artifacts, D&D focused tests, this plan, the D&D roadmap/dependency status, and one Slice 8A receipt.  
Stop point: Speed records and read/write mechanics pass family acceptance; stop before action economy, Conditions, or encounter-lifecycle integration.

## Confirmed decisions

- The user's 2026-08-25 instruction to implement Slice 8 confirms recovery of the first dependency-ready archived family and reuse of its already-classified permanent IDs: `dnd2024.speed`, `mechanic.dnd2024.speed.write`, `mechanic.dnd2024.speed.read`, and `procedure.mechanic.dnd2024.speed`. No invented alias or replacement ID is permitted.
- `dnd2024.speed` is canonical persistent base state. Zero in a special-Speed field is the repository's closed representation of absence; walk Speed must be positive.
- The 1,000-foot ceiling and five-foot increments are repository safety/canonicalization bounds retained from the verified archived implementation, not claims that the SRD imposes those universal limits.
- This slice does not revise the accepted encounter-turn owner. Action-economy integration is a later cross-owner semantic gate.

## D&D 5e 2024 alignment

| Rule concern | SRD 5.2.1 meaning used | Existing owner | Implementation consequence |
| --- | --- | --- | --- |
| Base Speed | Speed is the distance in feet a creature can cover when it moves on its turn. | New recovered `dnd2024.speed` state owner | Persist explicit feet; never infer a default from Size, name, or remaining movement. |
| Special Speeds | A creature may have Burrow, Climb, Fly, or Swim Speed and may choose/switch among available Speeds while moving. | Same component, distinct fields | Store the five source-backed base modes separately; do not flatten to one maximum. |
| Changes to Speeds | Temporary increases/decreases affect Speed and special Speeds together. | Deferred Conditions/effects owner | Persist no derived or temporary adjustment and perform no change calculation in this slice. |
| Movement resolution | Switching, movement costs, and turn remaining distance are movement/action-economy behavior. | Deferred Slice 8 families | Reader reports state only; writer does not move or spend anything. |

## External implementation reference

Pinned Foundry dnd5e commit `275bed0be4ccfa15e6b3347acccb8da8784726d9` was inspected as MIT-licensed engineering reference only:

- `module/data/shared/movement-field.mjs` stores movement modes in a keyed `speeds` mapping and normalizes absent default modes separately.
- `module/config.mjs` distinguishes walk, burrow, climb, fly, jump, and swim. This slice retains only the SRD Speed/special-Speed state; jumping remains a derived movement rule, not a persisted special Speed.
- `module/data/actor/templates/attributes.mjs` applies conditions, bonuses, multipliers, and reductions during derived actor preparation. That supports keeping this component as base state and deferring derived changes rather than copying Foundry's runtime model.

No Foundry source, Foundry globals, formula fields, assets, or runtime dependency are imported.

## Prerequisite evidence

- Slice 7's revalidated receipt proves exact application activation, projection-observed component revisions, atomic typed effects, replay, and rollback for the current D&D application.
- Slice 2B's accepted classification names `dnd2024.speed`, its writer/reader, procedure, archived tests, and dependency closure as a recovery candidate. Its former kernel-compatibility blocker is closed by Slice 7; this plan supplies the required source and Foundry review.
- Archived Feature 20 Slice 1 is verified evidence for the exact base-Speed shape and diagnostics. Its turn-budget migration is deliberately not adopted in this leaf.
- Catalog/code search finds no current active Speed owner or conflicting ID.

## Runtime artifacts

| Artifact | Change |
| --- | --- |
| `dnd2024.speed` definition/schema | Recover as one closed application-owned component. |
| `mechanic.dnd2024.speed.write` | Recover record/correct administrative transition. |
| `mechanic.dnd2024.speed.read` | Recover effect-free diagnostic reader. |
| `procedure.mechanic.dnd2024.speed` | Recover the governing contract, revised to this standalone boundary. |
| D&D focused regression coverage | Extend current activated D&D harness; no production fixture. |

No C# production code, schema migration, public kind, relationship kind, entity fixture, or source overlay is added.

## Authoritative state and closed input

The component contains exactly `walkFeet`, `burrowFeet`, `climbFeet`, `flyFeet`, `swimFeet`, and fixed `sourceRef`. Walk is an integer multiple of five from 5 through 1,000. Each special Speed is an integer multiple of five from 0 through 1,000; zero means absent.

The writer accepts exactly `mode` plus the five numeric fields. `mode` is `record` or `correct`; provenance and all derived/current movement values are host/catalog-owned and rejected if supplied. Record requires absence. Correct requires a complete, parseable, valid current component.

The reader accepts exactly `{}` and one `subject` role. It reports present/valid/problem plus either the complete Speed profile or null. It never supplies a default or effect.

## Behavior, result, and typed effects

The writer validates closed input and existing state before proposing exactly one `component.add` for record or `component.set` for correction. It fixes the exact source reference and uses no randomness. The generic application action runner owns revision checking, transaction, audit, and replay.

The reader returns deterministic diagnostic data with zero effects. `absent`, `malformed`, and `invalid` are distinct diagnostic states; only a valid complete profile is returned as Speed data.

## Failure, replay, and rollback contract

Missing role, non-object/extra/missing input fields, invalid mode, noninteger/fractional/out-of-range/non-five-foot values, supplied provenance, duplicate record, correction of absent state, and correction of corrupt/invalid existing state fail with no mutation. Stale component revisions reject the whole effect transaction. Replaying the same successful action returns the existing operation and applies no second effect.

## Implementation sequence

1. Add the closed component definition/schema and governing procedure.
2. Adapt the archived JavaScript writer/reader to current catalog layout without importing its broader module graph.
3. Register the component only inside the disposable focused test harness and add activated action/evaluation coverage.
4. Run syntax, schema, focused, catalog, full-suite, and diff checks; record external worktree blockers separately.
5. Write the Slice 8A receipt, update the roadmap/dependency status once, and stop.

## Acceptance matrix

| Class | Required evidence |
| --- | --- |
| Positive | Record stores the exact canonical profile with fixed source; correction replaces it once. |
| Read | Valid, absent, malformed, and invalid state produce exact effect-free diagnostics. |
| Boundaries | Walk 5/1,000 and specials 0/1,000 pass; zero walk, 1,005, fractions, and non-five-foot values fail. |
| Closed input | Missing/extra keys, unknown mode, caller source/effect/current-movement fields fail unchanged. |
| Replay/determinism | Same action identity replays without a second effect; same state/input returns byte-identical reader data. |
| Rollback | Invalid/corrupt correction and duplicate record preserve exact bytes/revision. |
| Compatibility | Existing Slice 7 D&D tests and combined application/ECS regressions remain green. |
| Surface | No MCP/protocol/dependency registration changes; no protocol walk required. |

## Verification commands

- `node --check` for both Speed mechanics and all D&D application JavaScript.
- Focused `Dnd2024AbilityCheckTests` (including new Speed cases).
- Combined D&D/application-execution/ECS-effect/Trail Survival regression filter.
- `dotnet build DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore`.
- `roleplay validate catalog` against its disposable database.
- Full `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-build --no-restore`.
- `git diff --check`.

## Completion receipt and exit gate

Record evidence at `adoption/evidence/DND-CODE-ADOPTION-SLICE-8A-RECEIPT.md`. Accept only after every Slice 8A-owned check passes and any unrelated dirty-worktree failures are identified by owner/evidence. Then stop before Conditions, turn-budget state, Speed-derived refresh, or movement execution.
