# D&D code adoption Slice 5B implementation — deterministic staged content transformer

Status: **accepted**
Owner/roadmap: [D&D 2024 application roadmap](ROADMAP.md)
Dependency tree/leaf: [D&D code-adoption dependency tree](DND-CODE-ADOPTION-DEPENDENCY-PLAN.md), D1 / Slice 5B
Ruleset alignment: **ruleset-neutral**
Source ID and locator: not applicable; the transformer copies declared JSON payloads without rule interpretation.
Outcome: Build a deterministic transformer that verifies frozen source bytes, selects declared
record/payload pointers, validates payloads against the named target schema, and emits only staged
candidate envelopes.
Exclusions: No direct catalog write/import, ID activation, source fetching, schema rewriting,
game-specific mapping, state transaction, or license decision.
Allowed files/areas: `ruleset/dnd2024/adoption/transformation/**`, Slice 5 documents/evidence, and
the D&D adoption plan/roadmap status lines.
Stop point: Valid homogeneous sample records produce byte-identical staged candidates. Batch
collision and license rejection are reserved for 5C.

## Confirmed decisions

- Transformations are pointer selections, not expression execution or field-by-field inference.
- A source file must hash exactly before any payload is read.
- The target payload schema validates payload shape; the staged envelope preserves all review
  metadata needed at the later catalog boundary.
- An explicit apply mode writes only beneath a caller-provided staging directory, never the catalog.

## External implementation reference

No Foundry review applies because this generic transformer does not implement D&D behavior.

## Prerequisite evidence

- Slice 5A supplies the closed manifest and candidate-envelope contracts.
- Slice 0B supplies the license/provenance vocabulary from which the manifest is derived.

## Runtime artifacts

- `Invoke-ContentTransformation.ps1` validates source hashes, pointers, target schemas, and staged
  candidate envelopes.
- The tool has dry-run and staging-only modes. It creates neither catalog records nor runtime files.

## Authoritative state and closed input

The manifest controls all source/schema paths and pointers. The source root is a bounded local
directory supplied by the operator. Source values, output paths, and schemas are never caller
reconstructed by the transformer.

## Behavior, result, and typed effects

For each entry, verify source hash, select `recordPointer`, select `payloadPointer` from that record,
validate the payload with the declared schema, then construct one canonical staged-candidate JSON
envelope. The report and candidate bytes are deterministic. There are no effects/transactions.

## Failure, replay, and rollback contract

Unsafe paths, escaping the source root, hash mismatch, unresolved pointer, invalid payload schema,
invalid candidate envelope, or write failure reject the batch. No candidate is written until every
entry validates. Repeated dry runs do not write files; repeated staging produces identical bytes.

## Implementation sequence

1. Implement bounded path/pointer/hash/schema validation and canonical staging.
2. Demonstrate deterministic dry-run and staged output with the neutral fixture.
3. Leave license/collision batch-policy rejection to 5C.

## Acceptance matrix

| Case | Expected result |
| --- | --- |
| Valid pinned source + payload schema | deterministic candidate plan |
| Hash mismatch | whole batch rejected |
| Missing record/payload pointer | whole batch rejected |
| Invalid payload schema | whole batch rejected |
| Repeated staging | byte-identical candidate files |

## Verification commands

`pwsh -NoProfile -File ruleset/dnd2024/adoption/transformation/tools/Test-ContentTransformation.ps1 -Stage 5B`

## Completion receipt and exit gate

Write a 5B receipt after focused tests. Stop before batch policy rejection and any catalog import.
