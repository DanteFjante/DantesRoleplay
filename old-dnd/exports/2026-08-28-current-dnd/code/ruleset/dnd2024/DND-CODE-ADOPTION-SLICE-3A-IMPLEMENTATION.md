# D&D code-adoption Slice 3A implementation — dependency-aware operation view

Status: **accepted 2026-08-25**
Owner/roadmap: [D&D 2024 application roadmap](ROADMAP.md)
Dependency tree/leaf: [D&D code-adoption plan, Slice 3 / 3A](DND-CODE-ADOPTION-DEPENDENCY-PLAN.md)
Parent design: [Slice 3 design](DND-CODE-ADOPTION-SLICE-3-DESIGN.md)
Ruleset alignment: **dnd2024-compatible**
Source ID and locator: `source.dnd2024.srd-5.2.1`, `Playing the Game > The Six Abilities >
Ability Scores` (PDF p. 5) and `Ability Modifiers` (PDF pp. 5–6); source is fixture provenance in
3A, not behavior implemented by C#
Outcome: prove a dependency-aware, operation-specific view can be materialized from exact
application component state and handed off without exposing unrelated state.
Exclusions: D&D calculations, rolling, result normalization, archived/donor execution, production
code changes, permanent IDs, catalog/source/component/projection/mechanic registration, public
operations, effects, transactions, migrations, activation, and archive modification.
Allowed files/areas: this document; development-only probe manifest/schema under
`ruleset/dnd2024/adoption/probes/ability-check/`; one generic test file under
`src/system/application-execution/tests/`; a Slice 3A receipt; dependency-plan and roadmap status.
Stop point: stop when the disposable two-projection chain, reverse-impact evidence, deterministic
view, and closed failure cases pass. Do not add or run the D&D JavaScript wrapper.

## Confirmed decisions

- The accepted Slice 2C selection fixes the first cohort and excludes broader skill, level,
  condition, donor-state, persistence, event, and reducer dependencies.
- The accepted adoption policy permits a test-only first-party recovery candidate but forbids
  automatic catalog output or activation.
- 3A uses existing structural projection and application execution contracts. Test-only IDs exist
  only in a disposable SQLite fixture and require no permanent-ID confirmation.
- Any required production-kernel modification is outside this leaf and causes a stop, not an
  opportunistic fix.

## D&D 5e 2024 alignment

| Rule concern | Source meaning | Existing owner | Implementation consequence |
| --- | --- | --- | --- |
| Ability state | Six abilities have scores; normal score range reaches 1–30 | archived `dnd2024.abilities` candidate; no active D&D owner | probe fixture validates exactly six integer scores 1–30 |
| Ability modifier | Derived from score and rounded down | 3B JavaScript, not 3A | no formula or derived modifier may appear in C# or projection mappings |
| D20 check/DC | Out of 3A scope | future 3B wrapper | no roll, DC comparison, or outcome in this leaf |
| Source identity | exact SRD record and locator | Slice 3 source-review evidence | manifest carries provenance; no runtime source registration |

## External implementation reference

Foundry dnd5e commit `275bed0be4ccfa15e6b3347acccb8da8784726d9`,
`module/dice/d20-roll.mjs` (blob `33d1551d5ed8fcc1aaac6a28d1238101d71b2035`) was reviewed.
It keeps d20 formula parts, target, and roll mode as separate inputs and normalizes to normal mode
when neither Advantage nor Disadvantage applies. 3A adopts no Foundry behavior or bytes because it
only materializes state.

## Prerequisite evidence

- [Slice 2C receipt](adoption/evidence/DND-CODE-ADOPTION-SLICE-2C-RECEIPT.md) selects the bounded
  raw ability-score/fixed-DC seam.
- [Slice 3 source review](adoption/evidence/DND-CODE-ADOPTION-SLICE-3-SOURCE-REVIEW.md) verifies the
  relevant official headings/pages and pinned engineering references.
- `src/system/projection-materialization/component.json` owns versioned structural projections,
  dependency validation, materialization, and impact graphs.
- `src/system/application-execution/component.json` owns exact application mechanic projections and
  read-only evaluation.
- `catalog/procedures/mechanics/procedure.mechanic.projection.md` forbids lazy/store access and
  limits JavaScript to declared frozen input.

## Runtime artifacts

None. Planned development-only artifacts:

- `ruleset/dnd2024/adoption/probes/ability-check/operation-view.probe.json`: bounded manifest with
  fixture application/state/entity, exact component schema/value, two structural projection
  definitions, role bindings, and expected output/impact graph;
- `ruleset/dnd2024/adoption/probes/ability-check/operation-view.probe.schema.json`: closed schema for
  that manifest; and
- `src/system/application-execution/tests/ApplicationAdoptionProbeTests.cs`: generic manifest-driven
  test harness. It must not contain D&D IDs, score names, formulas, DCs, or outcomes.

The manifest's application, component, and projection identifiers are explicitly fixture-only and
are never written to `catalog/`, the application manifest, or a non-disposable database.

## Authoritative state and closed input

The disposable ECS component is canonical for the probe run. Its exact registered qualified type,
version, and schema hash must match the projection input. The leaf projection consumes only the
output of its declared dependency. The dependency consumes only the one declared ability-state
component on the exact `subject` role.

The materialization request contains exactly the state-space ID, leaf `ProjectionReference`, and
`subject` entity binding. Callers may not supply component JSON, projection output, source
revisions, dependency results, extra roles, mappings, or content hashes at run time.

The operation view contains exactly:

```json
{"scores":{"str":12,"dex":16,"con":14,"int":10,"wis":13,"cha":8}}
```

All six scores are present because the later mechanic chooses one ability from closed action input;
the operation view still excludes every other component and graph edge.

## Behavior, result, and typed effects

1. The generic test reads and validates the probe manifest.
2. It creates a fresh disposable database, application revision, state space, subject entity,
   component type, and component value.
3. It defines an ability-state projection from the component and a raw-check operation-view
   projection depending on the first, using copy-only JSON Pointer mappings.
4. It materializes only the leaf projection with exact role bindings and asserts canonical output,
   exact source revisions, dependency order, graph fingerprint stability, and forward/reverse
   impact edges from a score field through both projections.
5. It repeats materialization and requires byte-identical output and evidence.

No mechanic executes. Result is the read-only `ProjectionMaterializationResult`. Typed effects,
events, notifications, transaction owner, replay operation, and rollback are none.

## Failure, replay, and rollback contract

The following independently fail before returning a view: malformed/extra manifest properties;
missing component; stale component version/schema hash; stale projection content hash; absent or
extra role binding; wrong application state space; missing source pointer; invalid target pointer;
unknown dependency; dependency cycle; and output-schema failure. Every case leaves ECS values,
revisions, and registry counts unchanged except for its disposable setup, which is discarded.

Repeated successful materialization is deterministic read-only evaluation, not a committed replay.
There is no rollback because no mutation follows setup. The test compares the post-materialization
state/revision and registration counts to their pre-materialization snapshot.

## Implementation sequence

1. Add the closed probe-manifest schema and one valid manifest; validate both independently.
2. Add the generic C# test loader and disposable setup without changing production types.
3. Define/materialize the two-node structural projection graph and assert exact output/evidence.
4. Add negative cases and no-change snapshots.
5. Run focused tests, all JSON/schema checks, the full solution suite, and link/diff checks.
6. Write the 3A receipt, mark 3A accepted/3B next, and stop before wrapper code.

## Acceptance matrix

| Case | Expected evidence |
| --- | --- |
| Positive | exact operation-view JSON and one exact source revision |
| Dependency | leaf output comes only from the declared parent projection |
| Reverse impact | changing the declared component score field names parent then leaf as dependents |
| Determinism | repeated view, source revisions, and graph fingerprint are byte-identical |
| Missing/stale | component/type/projection mismatch rejects before a view exists |
| Scope | wrong state space, absent role, or extra role rejects |
| Mapping | absent pointer, invalid target, unknown dependency, and cycle reject |
| Isolation | unrelated components/edges never appear; no mechanic/store callback is exposed |
| No change | materialization changes no ECS value/revision or registry row |
| Boundary | no production code, catalog record, permanent ID, public surface, or runtime registration |

## Verification commands

- `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --filter FullyQualifiedName~ApplicationAdoptionProbeTests`
- parse and validate every new probe JSON/JSON Schema file;
- `dotnet test DantesRoleplay.slnx` at acceptance;
- `roleplay validate catalog` because the full acceptance check must prove the unrelated authored
  catalog remains valid, even though 3A may not modify it;
- local Markdown link validation and `git diff --check`.

The protocol walk is not required because 3A may not change MCP registration or a public surface.

## Completion receipt and exit gate

Write `adoption/evidence/DND-CODE-ADOPTION-SLICE-3A-RECEIPT.md` with the exact manifest/schema/test
hashes, focused/full command results, graph/output evidence, negative/no-change evidence, and
deliberate exclusions. Mark 3A accepted and 3B next. Stop before adding JavaScript, result schemas,
normalizers, conformance vectors, production registrations, or runtime state.
