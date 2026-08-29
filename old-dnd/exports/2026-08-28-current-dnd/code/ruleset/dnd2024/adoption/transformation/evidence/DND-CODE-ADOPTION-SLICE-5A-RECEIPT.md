# D&D code adoption Slice 5A receipt — provenance and transformation manifest

Status: **accepted**

## Delivered boundary

- Added a closed batch manifest for source-hashed content candidates, source key/revision/path,
  record/payload selection, candidate target, target schema, license review facts, and ruleset
  source verification.
- D&D-owned manifest rows require the SRD source ID, exact locator, and explicit official
  verification. The neutral fixture remains candidate-only test data.

## Evidence

`Test-ContentTransformation.ps1 -Stage 5A` passed one schema compilation, one positive fixture,
and six rejected negative cases, including unsafe paths, unreviewed source, blocked license, missing
hash, missing MIT notice, and missing official verification for a D&D-owned candidate.

Sol reviewed and approved the corrected provenance boundary, including independent D&D-owned commit,
lock, source-review, and official-verification contract failures.

## Deliberate exclusions

No content conversion, catalog import, runtime registration, permanent ID, or D&D rule behavior was
added in this leaf.
