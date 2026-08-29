# D&D code-adoption Slice 6A receipt — candidate projection/dependency mapping

Status: **accepted 2026-08-25**
Implementation: [Slice 6A implementation](../../../DND-CODE-ADOPTION-SLICE-6A-IMPLEMENTATION.md)
Parent: [D&D code-adoption dependency tree](../../../DND-CODE-ADOPTION-DEPENDENCY-PLAN.md)

## Delivered boundary

Added a ruleset-neutral, candidate-only projection/dependency mapping contract. It records exact
component and projection references, source/target pointers, candidate role bindings, and the
canonical reverse-impact root required for each input. The focused validator compiles the closed
schema, rejects incomplete or contradictory declarations, and emits byte-stable read-only evidence.

The contract is intentionally not a projection definition or registration request. It does not
materialize a projection, resolve a live registry, activate catalog content, calculate a D&D rule,
run JavaScript, produce results/effects, or mutate state.

## Artifact fingerprints

| Artifact | SHA-256 |
| --- | --- |
| `adoption/mapping/contracts/projection-dependency-mapping.schema.json` | `95ADD0B02903741EFD82997AE475797391777A8608B444EE216A528C6E4F7753` |
| `adoption/mapping/fixtures/projection-dependency-mapping.valid.json` | `E734A65793099007EEA287F29A5F70D5C932CB465F9ABC03A82CD2CCBC424767` |
| `adoption/mapping/tools/Test-ProjectionDependencyMapping.ps1` | `BDA3338600A324065C05B62A2A1DD624C006E24277CB94A11B265A53A194CA6F` |

## Evidence

- Focused validator: `pwsh -NoProfile -File ruleset/dnd2024/adoption/mapping/tools/Test-ProjectionDependencyMapping.ps1` — passed: one schema compilation, one valid document, two schema negatives, seven semantic negatives, and deterministic report proof.
- Catalog validation: `./roleplay.cmd validate catalog` — valid: 144 records with 21 existing advisory near-duplicate warnings; no live data touched.
- Full repository suite: `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore --nologo --logger "console;verbosity=minimal"` — passed: 928/928.

## Contract and no-change result

For every component or projection input, the validator requires exactly one impact-evidence entry.
The entry's canonical root must match the declared exact source type/projection, version, and source
pointer; it must name the candidate projection by identical qualified ID, version, and content hash.
Duplicate input IDs, target pointers, or impact declarations are rejected. An input may not depend
on its own candidate projection, and component/role bindings must use only the candidate's declared
roles. Repeated validation generates identical report bytes. No runtime, catalog, database, or
application-registration artifact is written.

## Deliberate exclusions and next leaf

Slice 6A does not add runtime reverse-impact execution, replay/rollback proof, or any result/effect
mapping. It creates no permanent runtime ID, schema-meaning change, migration, public endpoint, or
activation. Slice 6B is next and must define the closed result/effect allowlist with Sol's required
boundary review before any execution or transaction work begins.
