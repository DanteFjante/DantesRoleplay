# D&D code adoption Slice 4B implementation — source-vector conversion and provenance

Status: **accepted**
Owner/roadmap: [D&D 2024 application roadmap](ROADMAP.md)
Dependency tree/leaf: [D&D code-adoption dependency tree](DND-CODE-ADOPTION-DEPENDENCY-PLAN.md), Slice 4B
Ruleset alignment: **ruleset-neutral**
Source ID and locator: not applicable; this slice transfers supplied JSON values only.
Outcome: Convert a closed archive, donor, native, or adapted source-vector document into the neutral
scenario and normalized-observation contracts without calculating or interpreting the values.
Exclusions: No donor checkout/runtime dependency, rule translation, schema transformation, catalog
registration, source activation, or state mutation.
Allowed files/areas: `ruleset/dnd2024/adoption/conformance/**`, Slice 4 evidence, and adoption
planning status lines.
Stop point: Archive and donor examples convert deterministically with retained identity/revision/path
provenance. Comparison is reserved for 4C.

## Confirmed decisions

- Source vectors are immutable evidence inputs; their declared provenance travels into the output.
- The converter may select values with declared JSON Pointers only. It must not contain game IDs,
  formulas, branches, or outcome classifications.
- The donor fixture identifies the already locked `dnd-srd-engine` commit and blob; it does not
  download, execute, or package donor code.

## External implementation reference

No Foundry review applies because this is generic data conversion, not D&D rule behavior.

## Prerequisite evidence

- Slice 4A supplies the closed portable scenario contract.
- `adoption/donor-lock.json` and `adoption/evidence/slice-1b-source-inventory.json` provide the
  existing donor commit and exact source-file evidence used by the fixture.

## Runtime artifacts

- `source-vector.schema.json` and `conformance-observation.schema.json` define closed input/output.
- `Convert-ConformanceVectors.ps1` emits a scenario and one normalized observation.
- Archive and donor frozen vector fixtures demonstrate distinct source provenance.
- No permanent runtime or public-surface artifact is introduced.

## Authoritative state and closed input

The converter receives a source-vector file only. `result` values are opaque. Its mapping controls
every read, and the converter rejects missing pointers, duplicate IDs/names, or incompatible suite
metadata. It never resolves campaign state or accepts a raw donor state model.

## Behavior, result, and typed effects

For each raw case, copy `context`, `input`, and `seed` into a normalized scenario. For each declared
comparison pointer, copy the pointed result value into the observation under its declared name.
Write both documents in a fixed property order and UTF-8 without BOM. No effects or transactions.

## Failure, replay, and rollback contract

Malformed JSON, unsupported source format, empty/missing mapping values, duplicate case IDs,
duplicate field names, or unresolved result pointers fail before output. Repeated input produces
byte-identical output. Existing output paths are only overwritten after successful conversion.

## Implementation sequence

1. Add closed source-vector and observation schemas.
2. Add deterministic generic conversion tool and frozen archive/donor samples.
3. Verify repeated conversions and retained provenance.

## Acceptance matrix

| Case | Expected result |
| --- | --- |
| Archive source vector | scenario and observation emitted |
| Pinned donor source vector | revision/path provenance retained |
| Missing result pointer | conversion rejected |
| Duplicate comparison name | conversion rejected |
| Same input twice | byte-identical output |

## Verification commands

`pwsh -File ruleset/dnd2024/adoption/conformance/tools/Test-ConformanceTooling.ps1 -Stage 4B`

## Completion receipt and exit gate

Write `adoption/conformance/evidence/DND-CODE-ADOPTION-SLICE-4B-RECEIPT.md` after deterministic
converter verification. Stop before comparison/gating logic.
