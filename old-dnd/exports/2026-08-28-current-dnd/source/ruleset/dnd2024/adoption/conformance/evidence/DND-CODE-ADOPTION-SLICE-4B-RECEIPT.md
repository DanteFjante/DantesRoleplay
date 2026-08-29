# D&D code adoption Slice 4B receipt — source-vector conversion and provenance

Status: **accepted**

## Delivered boundary

- Added closed source-vector and normalized-observation contracts.
- Added a deterministic PowerShell converter which copies declared JSON Pointer values only.
- Added archive, pinned-donor, and adapted frozen vector fixtures. The donor fixture retains its
  locked commit and exact donor source path without downloading or executing donor code.

## Evidence

`pwsh -NoProfile -File ruleset/dnd2024/adoption/conformance/tools/Test-ConformanceTooling.ps1 -Stage 4B`
passed: three schema compilations; three valid source conversions; byte-identical repeat outputs;
four rejected scenario negatives; rejected unresolved result pointer; retained donor provenance.

## Deliberate exclusions

The converter does not calculate rules, accept donor state, execute external code, activate archive
records, or compare source behavior.
