# D&D code adoption Slice 4C implementation — conformance runner and difference gate

Status: **accepted**
Owner/roadmap: [D&D 2024 application roadmap](ROADMAP.md)
Dependency tree/leaf: [D&D code-adoption dependency tree](DND-CODE-ADOPTION-DEPENDENCY-PLAN.md), Slice 4C
Ruleset alignment: **ruleset-neutral**
Source ID and locator: not applicable; this slice compares declared values and cannot decide rule truth.
Outcome: Compare a reference and candidate normalized observation against a neutral scenario suite,
produce a deterministic report, and block every mismatch—including a declared intentional difference—
until an adoption owner supplies separate confirmation evidence.
Exclusions: No auto-acceptance of changed behavior, no game-specific equivalence rules, no catalog
activation, no state writes, and no external source execution.
Allowed files/areas: `ruleset/dnd2024/adoption/conformance/**`, Slice 4 evidence, and adoption
planning status lines.
Stop point: Positive conformance, blocked mismatch, and blocked declared-difference behavior are
verified. Applying a confirmed semantic difference belongs to a later D&D-owned adoption slice.

## Confirmed decisions

- Equality is deep JSON equality of only the scenario-declared fields.
- A declared difference supplies review context, not authorization. The runner returns nonzero and
  reports `requires-confirmation`; a separate confirmed adoption decision is required before use.
- The runner is generic: it compares strings/numbers/objects/arrays without rule IDs or formulas.

## External implementation reference

No Foundry review applies: this is generic comparison and gating infrastructure.

## Prerequisite evidence

- Slice 4A supplies the scenario contract; Slice 4B emits normal observations with source
  provenance.
- Slice 3C supplies the actual ability-check parity evidence; this slice does not claim a new rule
  result from its generic samples.

## Runtime artifacts

- `intentional-difference.schema.json` declares review-only mismatch metadata.
- `Invoke-Conformance.ps1` validates declared suites/cases/fields and writes a deterministic report.
- Test fixtures exercise equal, unexpected-difference, and declared-but-unconfirmed paths.
- No runtime endpoint, catalog record, permanent ID, or schema owner changes.

## Authoritative state and closed input

The scenario declares case IDs and field names. Each observation declares a source and exactly one
value map per case. The runner rejects unknown/missing cases or fields rather than ignoring them.
The optional difference manifest must use the same suite/case/field and may only describe an
observed mismatch.

## Behavior, result, and typed effects

Compare reference/candidate values in scenario/case/field order. Equal values produce `passed`.
Unequal values produce `blocked`; an exact matching review declaration changes its reason to
`requires-confirmation`, never to passed. Output ordering is stable. No effects or transactions.

## Failure, replay, and rollback contract

Wrong format, mismatched suite, duplicate or missing case IDs, missing/extra fields, invalid
difference documents, and malformed JSON all fail. Every unresolved difference exits nonzero. No
state changes occur other than a replaceable report file.

## Implementation sequence

1. Add review-only difference contract and adapted sample.
2. Implement deterministic comparison and strict validation/gating.
3. Verify positive, negative, declared-difference, and repeatability behavior.

## Acceptance matrix

| Case | Expected result |
| --- | --- |
| Same normalized values | exit 0 / passed report |
| One unequal field | nonzero / blocked report |
| Matching declared difference | nonzero / requires-confirmation report |
| Declaration without a mismatch | rejected |
| Missing observation field | nonzero |
| Repeat same inputs | byte-identical report |

## Verification commands

`pwsh -File ruleset/dnd2024/adoption/conformance/tools/Test-ConformanceTooling.ps1 -Stage 4C`

## Completion receipt and exit gate

Write `adoption/conformance/evidence/DND-CODE-ADOPTION-SLICE-4C-RECEIPT.md`, then update the
parent dependency plan and roadmap. Stop before applying an intentional rule difference.
