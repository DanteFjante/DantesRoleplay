# D&D code adoption Slice 4C receipt — conformance runner and difference gate

Status: **accepted**

## Delivered boundary

- Added a deterministic reference/candidate conformance runner over only scenario-declared fields.
- Added the review-only intentional-difference contract.
- An actual mismatch exits nonzero and reports `blocked`. A matching intentional-difference record
  still exits nonzero and reports `requires-confirmation`; it never authorizes a changed rule.

## Evidence

`pwsh -NoProfile -File ruleset/dnd2024/adoption/conformance/tools/Test-ConformanceTooling.ps1 -Stage 4C`
passed: four schema compilations; three repeatable conversions; deterministic equal comparison;
unexpected mismatch blocked; declared mismatch requires confirmation and remains nonzero; an
intentional-difference declaration without an observed mismatch is rejected.

## Defects corrected while implementing

- Cross-schema references initially used filesystem-relative paths despite the schemas' canonical
  IDs. They now resolve through the canonical IDs and compile as a unit.
- The test harness initially used PowerShell's automatic `$input` name for a fixture path. It now
  uses `fixturePath`, preventing an empty argument from reaching the converter.
- The runner now rejects a declared difference that does not correspond to an actual mismatch.

## Deliberate exclusions

No D&D rule change, acceptance of an intentional semantic difference, catalog activation, runtime
host change, state write, source import, or MCP-surface change is included.
