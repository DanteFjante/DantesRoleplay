# D&D code adoption Slice 5A implementation — content provenance and transformation manifest

Status: **accepted**
Owner/roadmap: [D&D 2024 application roadmap](ROADMAP.md)
Dependency tree/leaf: [D&D code-adoption dependency tree](DND-CODE-ADOPTION-DEPENDENCY-PLAN.md), D1 / Slice 5A
Ruleset alignment: **ruleset-neutral**
Source ID and locator: not applicable; this slice defines staging metadata and does not encode a rule.
Outcome: Define a closed batch manifest which identifies exact source bytes, selected records, target
candidate paths, payload schemas, license review facts, and transformation provenance.
Exclusions: No content activation, catalog import, rule behavior, persistent ID registration, donor
runtime, or automatic license approval.
Allowed files/areas: `ruleset/dnd2024/adoption/transformation/**`, Slice 5 documents/evidence, and
the D&D adoption plan/roadmap status lines.
Stop point: The contract and fixtures prove a manifest can express the full candidate boundary;
actual transformation is reserved for 5B.

## Confirmed decisions

- A manifest is a review/staging artifact, never catalog authority.
- Every entry freezes its source-file SHA-256 and record/payload JSON Pointers before conversion.
- Any permitted license disposition must also have explicit review evidence. Unknown, mixed,
  premium, asset, and non-SRD source material are representable only as blocked/rejected entries.
- Target IDs and paths are candidates until the owning application/catalog import boundary accepts
  them. This avoids reserving a production ID during a dry run.

## External implementation reference

No Foundry review applies because no D&D-owned rule or Foundry implementation is involved.

## Prerequisite evidence

- Slice 0B's provenance ledger fixes the donor/notice and license vocabulary.
- Slice 4B's source-vector conversion proves the same general principle: copies retain source
  provenance and tools do not interpret game values.

## Runtime artifacts

- New development contract: `transformation/contracts/content-transform-manifest.schema.json`.
- New staged-candidate envelope contract: `transformation/contracts/staged-content-candidate.schema.json`.
- No runtime catalog schema, permanent catalog ID, public kind, or migration is added.

## Authoritative state and closed input

The manifest receives only a source-relative file, expected source hash, declared record and payload
pointers, candidate target identity/path, target payload schema, and license provenance. The source
file and referenced schema remain authoritative inputs; callers cannot supply transformed values.

## Behavior, result, and typed effects

The contract has no executable behavior and emits no effects. A later transformer will copy the
selected payload exactly, retain its manifest provenance, validate it with the declared target
schema, and stage—not activate—the resulting candidate.

## Failure, replay, and rollback contract

Unknown properties, unsafe relative paths, missing hashes, unsupported license states, invalid
pointers, or missing source-review evidence are invalid. There is no transaction or state mutation.

## Implementation sequence

1. Add closed manifest and staged-candidate contracts plus valid/negative fixtures.
2. Validate the semantic license/source combinations and record Sol review questions.
3. Leave payload conversion to 5B and collision/dry-run enforcement to 5C.

## Acceptance matrix

| Case | Expected result |
| --- | --- |
| Reviewed, source-hashed neutral entry | accepted |
| Unknown or mixed license as candidate | rejected |
| Missing review evidence | rejected |
| Unsafe source/target/schema path | rejected |
| Unpinned source hash | rejected |

## Verification commands

`pwsh -NoProfile -File ruleset/dnd2024/adoption/transformation/tools/Test-ContentTransformation.ps1 -Stage 5A`

## Completion receipt and exit gate

Write a 5A receipt and a focused Sol review packet. Stop before transformation code or a catalog
write.
