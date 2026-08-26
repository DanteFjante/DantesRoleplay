# D&D code adoption Slice 5C receipt — rejection policy and Sol handoff

Status: **accepted**

## Delivered boundary

- The transformer now rejects an entire batch before staging on blocked/unreviewed licensing,
  duplicate target paths, source-hash mismatch, and collisions at an existing target root.
- Dry run produces deterministic reports and writes no staged candidates. Staging refuses overwrite
  and rolls back files it wrote if a later staging write fails.
- Added the focused [Sol review packet](../review/SOL-SLICE-5-REVIEW.md).

## Evidence

`pwsh -NoProfile -File ruleset/dnd2024/adoption/transformation/tools/Test-ContentTransformation.ps1 -Stage 5C`
passed: two schema compilations, six rejected contract negatives, deterministic dry-run, two
byte-identical candidates, and rejected source/schema/mapping/tool hashes, blocked-license,
duplicate-target/normalized-alias, reparse-point, and existing-target cases.

`roleplay.cmd validate catalog` passed: 144 records valid with 21 advisory warnings; no live data
was touched. Local-AI tests passed 20/20.

The shared main test project currently has 3 unrelated trigger-scheduling failures (902/905 passed):
an expected immutable-trigger count and new recurring-trigger tables/columns not yet classified for
catalog coverage. Slice 5 neither touches those migrations nor their tests.

## Sol review

Sol approved the license/staging and D&D-owned source-verification semantics after corrective
review and an independent focused test run. This receipt is not catalog/content activation.

## Deliberate exclusions

No catalog import, source activation, D&D content/rule, runtime host change, public endpoint,
migration, or permanent catalog ID was added.
