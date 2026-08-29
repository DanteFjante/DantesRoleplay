# D&D code adoption Slice 4A receipt — neutral conformance scenario contract

Status: **accepted**

## Delivered boundary

- Added the closed Draft 2020-12 portable scenario schema and valid/negative fixtures under
  `adoption/conformance/`.
- The contract carries only opaque context/input/seed values, declared output pointers, and source
  provenance. It performs no game calculation or state transition.

## Evidence

`pwsh -NoProfile -File ruleset/dnd2024/adoption/conformance/tools/Test-ConformanceTooling.ps1 -Stage 4A`
passed: one schema compilation, one positive document, and four rejected negative documents.

## Deliberate exclusions

No source conversion, comparison, catalog registration, public endpoint, donor execution, or game
rule behavior was added in this leaf.
