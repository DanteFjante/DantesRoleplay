# D&D code-adoption Slice 6B receipt — candidate result/effect allowlist

Status: **accepted 2026-08-25**
Implementation: [Slice 6B implementation](../../../DND-CODE-ADOPTION-SLICE-6B-IMPLEMENTATION.md)
Parent: [D&D code-adoption dependency tree](../../../DND-CODE-ADOPTION-DEPENDENCY-PLAN.md)

## Delivered boundary

Added a ruleset-neutral, candidate-only result/effect allowlist. It resolves an exact Slice 6A
mapping reference, validates the candidate result against its pinned schema, and converts only
declared proposal kinds into a read-only plan for an existing generic structural effect. Candidate
output never selects a kernel effect type, target role, component type/version/schema hash, or
relationship kind.

The result is an in-memory plan only. It does not execute JavaScript, call an effect applier,
materialize state, register a projection/mechanic, activate catalog content, or write to a database.

## Artifact fingerprints

| Artifact | SHA-256 |
| --- | --- |
| `adoption/effects/contracts/result-effect-allowlist.schema.json` | `936ECB78A6AB0CF3C47AA8143641623A1AAA093BE2B681DBB3A0DD59643CF96E` |
| `adoption/effects/contracts/result-effect-allowlist.result.schema.json` | `C92661F0CBCCC6E518C695F7F0DFF284BE0BBC94FB9BB29F6B6D60D71615A0C1` |
| `adoption/effects/fixtures/result-effect-allowlist.valid.json` | `8FBAF5C8A030BCFFBEBD00C80FD8AF2B814B1DC748DFC2A5F5BE081BF8CE4EF9` |
| `adoption/effects/fixtures/result-effect-allowlist.result.json` | `966728517914809040427FB9F9AB619360A67BF8AEC65F4FE00435DC175FB671` |
| `adoption/effects/tools/Test-ResultEffectAllowlist.ps1` | `B0313D9E0BDEE68AAD0315C3E3D0FC00D586A5103738C3DAC16C1506FBF68C68` |
| `adoption/effects/review/SOL-SLICE-6B-BOUNDARY-REVIEW.md` | `D3AA825B1B9481619DE91678DCE2FBE61384A425685EC4FFA720F9C005BFDB0E` |

## Evidence

- Focused validator: `pwsh -NoProfile -File ruleset/dnd2024/adoption/effects/tools/Test-ResultEffectAllowlist.ps1` — passed: two schema compilations, two positive documents, three schema negatives, four semantic negatives, three conversion negatives, deterministic plan proof, and no writes.
- Catalog validation: `./roleplay.cmd validate catalog` — valid: 144 records with 21 existing advisory near-duplicate warnings; no live data touched.
- Full current no-build suite: `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-build --no-restore --nologo` — 926/928 passed. The two failures are both in untracked `TrailSurvivalRunDomainTests` and concern its separate component-schema/replay work; this slice does not touch that test, application, component, or owner.
- A normal build/test was separately attempted but its build output was locked by the pre-existing running MCP host. No running host was stopped or altered.

## Boundary review and corrections

The assigned Sol boundary-review packet is ready at `adoption/effects/review/`; it documents the
decision to keep candidate proposal kinds as the only candidate-controlled dispatch token. During
implementation review, the allowlist was corrected to verify the actual Slice 6A mapping path/hash
and to compare proposal kinds and role names case-sensitively. It also validates the pinned result
schema before conversion and rejects missing payload pointers.

## Deliberate exclusions and next leaf

No runtime effect allowlist is registered in generic C#, and no D&D-owned rule, permanent runtime
ID, public operation, migration, source/license decision, or activation was added. Slice 6C is next:
it may prove impact, transaction, replay, and rollback behavior against the accepted candidate
mapping and allowlist, but may not turn the neutral fixture into D&D game authority.
