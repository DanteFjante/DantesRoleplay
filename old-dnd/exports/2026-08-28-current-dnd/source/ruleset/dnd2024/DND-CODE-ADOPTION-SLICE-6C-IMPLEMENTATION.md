# D&D code adoption Slice 6C implementation — candidate impact, replay, and rollback proof

Status: **accepted**
Owner/roadmap: [D&D 2024 application roadmap](ROADMAP.md)
Dependency tree/leaf: [D&D code-adoption dependency tree](DND-CODE-ADOPTION-DEPENDENCY-PLAN.md), Slice 6C
Ruleset alignment: **ruleset-neutral**
Source ID and locator: not applicable; this is generic adoption-boundary evidence, not a D&D rule.
Outcome: Prove the accepted candidate mapping/allowlist chain remains exact while the existing generic kernel provides declared reverse impact, at-most-once replay, and atomic rollback.
Exclusions: No D&D mechanic/content, production candidate registration/activation, runtime host behavior change, source/license decision, public operation, database migration, or permanent runtime ID.
Allowed files/areas: `ruleset/dnd2024/adoption/impact-proof/**`, this Slice 6C document, the D&D adoption plan/roadmap status lines, and a narrowly scoped test-only adoption proof if the existing generic tests cannot express the artifact chain.
Stop point: Static chain verification and test-only impact/replay/rollback evidence pass. Stop before Slice 7's first recovered gameplay cohort.

## Confirmed decisions

- `projection-materialization` remains the sole owner of structural dependency registration, impact roots, and reverse traversal.
- `ecs-effects` remains the sole owner of atomic apply, rollback, audit, and execution identity replay. Candidate tooling must not emulate a second transaction mechanism.
- Slice 6A/6B artifacts are candidates, not runtime input. Slice 6C can prove their exact references and generic host behavior together, but cannot register or execute them as a production D&D mechanic.

## External implementation reference

No Foundry review applies: this is reusable ruleset-neutral infrastructure evidence and implements no D&D behavior.

## Prerequisite evidence

- [Slice 6A receipt](adoption/mapping/evidence/DND-CODE-ADOPTION-SLICE-6A-RECEIPT.md) supplies the accepted candidate mapping and impact roots.
- [Slice 6B receipt](adoption/effects/evidence/DND-CODE-ADOPTION-SLICE-6B-RECEIPT.md) supplies the pinned result/effect allowlist and candidate plan conversion.
- `ApplicationAdoptionProbeTests` proves exact component-field reverse impact through a declared two-projection graph.
- `ApplicationEcsEffectApplierTests` proves at-most-once identity replay and late-failure rollback under the generic transaction owner.

## Runtime artifacts

- New read-only impact-proof manifest and focused verifier under `adoption/impact-proof/`.
- The verifier reads exact artifact hashes, invokes the existing focused generic tests, and writes no runtime state.
- No C# production code, catalog record, source registration, application activation, or public surface is added.

## Authoritative state and closed input

The impact-proof manifest is authoritative only for the exact candidate artifact paths/hashes and named generic proof cases. It accepts no campaign/state request. The verifier derives all data from checked files and test results; callers may not substitute mapping/allowlist/result paths, hashes, roots, transaction identities, or effect payloads.

## Behavior, result, and typed effects

First reject stale/missing path/hash references. Then require the candidate mapping's declared roots and the allowlist's mapping reference to agree exactly. Execute existing focused generic tests that materialize a declared projection and analyze reverse impact, replay an execution identity without a second write, and roll back a late stale effect. The only result is a deterministic proof report. No typed effect is created or applied by this slice.

## Failure, replay, and rollback contract

Any stale candidate hash/path, mismatched mapping key, malformed proof manifest, unavailable focused test, impact-test failure, replay-test failure, or rollback-test failure rejects the proof. The verifier is read-only. Repeating it is byte-stable and cannot alter application state, catalog state, operations, or the running MCP host.

## Implementation sequence

1. Define a closed proof manifest that pins Slice 6A/6B artifacts and expected generic tests.
2. Build a verifier for hash/path/chain closure and deterministic reports.
3. Run the focused generic impact, replay, and rollback tests through the current no-build assembly.
4. Record the bounded evidence and stop before Slice 7.

## Acceptance matrix

| Case | Expected result |
| --- | --- |
| Exact candidate chain and generic tests | ready proof with reverse impact, replay, and rollback evidence |
| Stale mapping/allowlist/result hash or missing path | reject before focused tests run |
| Allowlist mapping key/hash disagrees with Slice 6A | reject |
| Focused generic test failure | reject proof |
| Repeated proof | byte-identical report; no writes |

## Verification commands

`pwsh -NoProfile -File ruleset/dnd2024/adoption/impact-proof/tools/Test-ImpactReplayRollbackProof.ps1`

`./roleplay.cmd validate catalog`

`dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-build --no-restore --nologo --filter "FullyQualifiedName~ApplicationAdoptionProbeTests|FullyQualifiedName~ApplicationEcsEffectApplierTests"`

## Completion receipt and exit gate

Write `adoption/impact-proof/evidence/DND-CODE-ADOPTION-SLICE-6C-RECEIPT.md` after verification. Update the plan/roadmap once. Do not begin Slice 7 or activate the candidate artifacts.
