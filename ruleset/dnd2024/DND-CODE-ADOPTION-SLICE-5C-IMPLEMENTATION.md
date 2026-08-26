# D&D code adoption Slice 5C implementation — dry-run collision and license rejection

Status: **accepted**
Owner/roadmap: [D&D 2024 application roadmap](ROADMAP.md)
Dependency tree/leaf: [D&D code-adoption dependency tree](DND-CODE-ADOPTION-DEPENDENCY-PLAN.md), D1 / Slice 5C
Ruleset alignment: **ruleset-neutral**
Source ID and locator: not applicable; this is generic staging policy, not a game rule.
Outcome: Make the content transformer reject an entire batch on unreviewed/non-permitted licensing,
duplicate candidate identities/paths, or collisions with an operator-supplied existing target root;
prove dry-run reporting remains stable and non-mutating.
Exclusions: No automatic license normalization, overwrite/merge, production catalog import, ID
reservation, rule decision, migration, or public endpoint.
Allowed files/areas: `ruleset/dnd2024/adoption/transformation/**`, Slice 5 documents/evidence, and
the D&D adoption plan/roadmap status lines.
Stop point: A valid fixture has deterministic dry-run/staged output; invalid license and collision
fixtures reject atomically before staged candidates are written.

## Confirmed decisions

- Only explicitly reviewed `first-party-recovery`, `approved-mit-software`, and
  `approved-cc-by-srd-content` entries may reach staging.
- Every manifest target ID, candidate key, and path must be unique within the batch.
- Existing target paths are conflicts, never overwrite instructions.
- Dry run writes only its replaceable report and is deterministic; it cannot create staged candidates.

## External implementation reference

No Foundry review applies because no rules or Foundry-dependent behavior is implemented.

## Prerequisite evidence

- Slice 5A provides the review/source/license manifest.
- Slice 5B provides hash/pointer/schema validation and staging-only candidate generation.

## Runtime artifacts

- The existing transformer gains batch-level collision and permitted-license rejection.
- `Test-ContentTransformation.ps1` verifies contracts, dry-run, staging, and rejection behavior.
- `review/SOL-SLICE-5-REVIEW.md` packages the semantic and licensing decisions for Sol.

## Authoritative state and closed input

The manifest is the only declaration of candidate identity/source/license. An existing target root is
read-only collision evidence. The tool may not infer a license or choose a new target identity.

## Behavior, result, and typed effects

Preflight every entry before writing staged candidates. The report is `ready` only when every entry
passes source, payload, candidate, license, and collision checks. Otherwise report `rejected`, exit
nonzero, and write no candidate. No effects or database transaction occur.

## Failure, replay, and rollback contract

Any rejected entry rejects the full batch. Duplicate keys/IDs/paths, stale hash, pointer/schema
failure, unreviewed/blocked license, or existing target collision are errors. Dry run cannot mutate
the staging directory. Staging never overwrites an existing candidate.

## Implementation sequence

1. Add focused harness with schema and semantic-negative cases.
2. Verify dry-run byte stability and staging output identity.
3. Verify full-batch rejection leaves a new staging directory empty.
4. Produce Sol review package and receipt; do not import content.

## Acceptance matrix

| Case | Expected result |
| --- | --- |
| Valid batch, repeated dry run | identical ready reports; no staged files |
| Valid batch, repeated staging | identical candidate bytes |
| Blocked/unknown license | rejected; no staged files |
| Duplicate target path | rejected; no staged files |
| Existing target path | rejected; no staged files |
| One hash/pointer/schema error | rejected; no staged files |

## Verification commands

`pwsh -NoProfile -File ruleset/dnd2024/adoption/transformation/tools/Test-ContentTransformation.ps1 -Stage 5C`

## Completion receipt and exit gate

Write the Slice 5C receipt and Sol review packet after focused, catalog, and repository checks.
Do not treat the review packet as automatic semantic or license acceptance for future D&D content.
