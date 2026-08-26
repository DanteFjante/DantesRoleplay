# D&D code adoption Slice 5B receipt — deterministic staged content transformer

Status: **accepted**

## Delivered boundary

- Added a deterministic transformer that verifies source SHA-256, resolves only declared JSON
  Pointers, validates the selected payload against its target schema, and preserves provenance and
  mapping details in a staged candidate envelope.
- Apply mode is bounded to an explicit staging root; the tool has no catalog, SQLite, runtime, or
  source-fetching path.

## Evidence

`Test-ContentTransformation.ps1 -Stage 5B` passed manifest/candidate schema compilation,
deterministic repeated dry-run reports, two byte-identical staged candidates, candidate validation,
and whole-batch stale-hash rejection with no candidate output.

The corrected transformer also verifies the target-schema SHA-256, canonical mapping SHA-256, and
the actual executing transformer SHA-256 before staging.

## Deliberate exclusions

No license decision, collision policy acceptance, catalog import, or D&D semantics is added here.
