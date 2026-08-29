# D&D code adoption Slice 6B implementation — candidate result/effect allowlist

Status: **accepted**
Owner/roadmap: [D&D 2024 application roadmap](ROADMAP.md)
Dependency tree/leaf: [D&D code-adoption dependency tree](DND-CODE-ADOPTION-DEPENDENCY-PLAN.md), Slice 6B
Ruleset alignment: **ruleset-neutral**
Source ID and locator: not applicable; this defines generic proposal-to-effect declarations, not a D&D rule.
Outcome: Add a closed, candidate-only result/effect allowlist which maps declared proposal kinds to existing generic effect types and exact target roles/references.
Exclusions: No D&D rule logic, production projection/mechanic registration, catalog activation, JavaScript execution, database mutation, runtime action-runner change, public endpoint, migration, live replay, or rollback transaction.
Allowed files/areas: `ruleset/dnd2024/adoption/effects/**`, this Slice 6B document, and the D&D adoption plan/roadmap status lines.
Stop point: The fixture and harness must prove candidate proposal mapping and rejection, then stop before Slice 6C's live impact/replay/rollback proof.

## Confirmed decisions

- `effects-and-transactions`, `ecs-effects`, and `application-execution` own the generic effect vocabulary, translation, transaction, audit, and replay identity. This slice must not add a game-specific effect or modify their runtime behavior.
- A candidate may emit only an opaque declared proposal kind. It cannot choose a kernel effect type, component identity, target entity, relationship kind, or activation route at runtime.
- The allowlist resolves proposal kinds to existing generic structural effects. Unknown kinds and fields needed by the selected template reject before any effect is proposed.

## External implementation reference

No Foundry review applies: this is reusable, ruleset-neutral adoption tooling and adds no D&D behavior or direct external reuse.

## Prerequisite evidence

- [Slice 6A receipt](adoption/mapping/evidence/DND-CODE-ADOPTION-SLICE-6A-RECEIPT.md) provides the candidate projection/dependency boundary that this contract identifies by key and hash.
- `src/system/effects-and-transactions/domain/Effect.cs` is the authoritative generic mechanic-effect vocabulary.
- `src/system/application-execution/persistence/ApplicationActionRunner.cs` owns later generic translation to application ECS effects and transactions.

## Runtime artifacts

- New adoption-only JSON Schema, neutral allowlist fixture, and focused PowerShell harness.
- The harness returns an in-memory candidate effect plan only; it never invokes an effect applier.
- No new runtime/public/permanent schema or ID is created, so no registration or activation confirmation gate is consumed.

## Authoritative state and closed input

The allowlist manifest is authoritative for candidate proposal kinds, their permitted generic effect type, exact component/relationship references, and role targets. A candidate result may contain only an ordered array at the declared proposals pointer. Its proposal kind selects an allowlist item; all identity-bearing fields are resolved from the manifest and a caller may never provide them.

## Behavior, result, and typed effects

Validate schema and semantic closure first. For each proposal, resolve its kind once, extract only the template-declared payload pointer, and return a read-only candidate plan containing the fixed generic effect type, role references, fixed component/relationship identity, and copied payload. Component mutations require exact component version/schema hash; relationship mutations require an exact qualified relationship; structural types reject irrelevant template fields. No typed effect is applied in this slice.

## Failure, replay, and rollback contract

Malformed manifests/results, duplicate proposal kinds, undeclared proposal kinds, missing payload, unknown roles, self-contradictory templates, or unsupported fields fail before a candidate plan exists. Repeated mapping of unchanged manifest/result produces byte-identical plans. The harness is read-only, so failure, replay, and rollback leave catalog, database, state, and audit records unchanged.

## Implementation sequence

1. Define the closed allowlist/result schema and neutral component-set fixture.
2. Implement static closure and candidate-plan conversion in the focused harness.
3. Verify schema, role/template, unknown-proposal, missing-payload, deterministic, and no-write cases.
4. Record evidence and stop before Slice 6C.

## Acceptance matrix

| Case | Expected result |
| --- | --- |
| Declared component-set proposal | converts to exactly one fixed component-set candidate plan |
| Unknown property or malformed exact hash | schema rejects |
| Duplicate proposal kind or unknown role | semantic validation rejects |
| Unknown proposal kind | conversion rejects before an effect plan is returned |
| Missing declared data pointer | conversion rejects |
| Repeat conversion | byte-identical plan; no writes |

## Verification commands

`pwsh -NoProfile -File ruleset/dnd2024/adoption/effects/tools/Test-ResultEffectAllowlist.ps1`

`./roleplay.cmd validate catalog`

`dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore --nologo`

## Completion receipt and exit gate

Write `adoption/effects/evidence/DND-CODE-ADOPTION-SLICE-6B-RECEIPT.md` after verification and the required boundary review. Update plan/roadmap once. Do not begin Slice 6C or invoke a live effect applier.
