# D&D code adoption Slice 6A implementation — projection/dependency mapping manifest

Status: **accepted**
Owner/roadmap: [D&D 2024 application roadmap](ROADMAP.md)
Dependency tree/leaf: [D&D code-adoption dependency tree](DND-CODE-ADOPTION-DEPENDENCY-PLAN.md), D4 / Slice 6A
Ruleset alignment: **ruleset-neutral**
Source ID and locator: not applicable; this slice describes generic dependency declarations, not a D&D rule.
Outcome: Add a closed, candidate-only mapping manifest which records a projection candidate's exact component and projection inputs, role bindings, target fields, and canonical reverse-impact roots.
Exclusions: No projection registration or materialization, catalog activation, D&D rule logic, JavaScript execution, result/effect allowlist, database write, transaction, replay, rollback, public endpoint, migration, or permanent runtime ID.
Allowed files/areas: `ruleset/dnd2024/adoption/mapping/**`, this Slice 6A document, the D&D adoption plan status, and the D&D roadmap status.
Stop point: The fixture and validator prove static mapping closure and deterministic evidence only. Stop before Slice 6B result/effect allowlisting.

## Confirmed decisions

- Existing `projection-materialization` owns versioned definitions, dependency validation, materialization, and reverse-impact traversal; this slice must not duplicate or alter that kernel owner.
- A mapping is a reviewable candidate. It cannot register, activate, or reserve the projection reference it names.
- Component and projection dependencies are both exact version/hash references. Mapping manifests may only describe data movement, never calculation or ruleset outcomes.

## External implementation reference

No Foundry review applies: this is reusable, ruleset-neutral adoption tooling and introduces no D&D behavior or external implementation reuse.

## Prerequisite evidence

- [Slice 3A receipt](adoption/evidence/DND-CODE-ADOPTION-SLICE-3A-RECEIPT.md) proves the generic kernel can materialize a declared component-to-projection-to-projection graph and report reverse impact.
- [Slice 5C receipt](adoption/transformation/evidence/DND-CODE-ADOPTION-SLICE-5C-RECEIPT.md) proves candidate-only, deterministic, reject-whole-batch tooling conventions.
- `src/system/projection-materialization/domain/ProjectionContracts.cs` and `persistence/ProjectionImpactService.cs` remain the runtime owners.

## Runtime artifacts

- New adoption-only JSON Schema and neutral fixture under `adoption/mapping/`.
- New PowerShell validator/test harness which compiles the schema, enforces semantic closure, and emits a deterministic declaration report.
- No new runtime, catalog, database, public, or permanent artifact; therefore no confirmation gate is consumed.

## Authoritative state and closed input

The JSON manifest is the complete candidate declaration. Its projection and input references are opaque exact identities supplied by reviewed upstream work; the validator does not infer IDs, versions, hashes, input fields, role bindings, or dependency roots. Callers cannot supply campaign state, projection values, results, effects, or activation intent.

## Behavior, result, and typed effects

Validate schema first, then require unique input IDs and target pointers, role bindings to declared roles, exact canonical roots for every input, and a unique dependent projection target. The harness reports a SHA-256 fingerprint of the canonical manifest text and its ordered impact roots. It writes no state and produces no typed effects.

## Failure, replay, and rollback contract

Malformed JSON/schema, duplicate mappings, an unknown role/input, a mismatched canonical root, a self-dependency, or an undeclared dependent projection fails before a report is accepted. Repeated validation of unchanged input must yield the same report bytes. Because this slice is read-only, failure, replay, and rollback leave all runtime and catalog state unchanged.

## Implementation sequence

1. Define the closed manifest schema and one neutral candidate fixture.
2. Implement schema and semantic validation with deterministic reporting.
3. Exercise positive, malformed, duplicate, role/root, self-dependency, and deterministic cases.
4. Record focused/catalog/full-suite evidence and stop before Slice 6B.

## Acceptance matrix

| Case | Expected result |
| --- | --- |
| Valid component and projection inputs | schema and semantic validation succeed with two ordered impact roots |
| Unknown object property or malformed hash | schema rejects |
| Duplicate input ID or target pointer | semantic validation rejects |
| Unknown role, unknown impact input, or root mismatch | semantic validation rejects |
| Candidate references itself as a projection input | semantic validation rejects |
| Repeat validation | byte-identical reports; no runtime/catalog writes |

## Verification commands

`pwsh -NoProfile -File ruleset/dnd2024/adoption/mapping/tools/Test-ProjectionDependencyMapping.ps1`

`roleplay validate catalog`

`dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore --nologo`

## Completion receipt and exit gate

Write `adoption/mapping/evidence/DND-CODE-ADOPTION-SLICE-6A-RECEIPT.md` after the stated verification. Update the plan/roadmap once. Do not create Slice 6B artifacts or effects in this acceptance boundary.
