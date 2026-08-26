# D&D code adoption Slice 0A receipt — pinned donor baseline

Status: **accepted**
Completed: 2026-08-25
Implementation: [Slice 0A](../../DND-CODE-ADOPTION-SLICE-0A-IMPLEMENTATION.md)
Evidence: [Pinned baseline](donor-baseline-2026-08-25.json)

## Delivered

- Locked `dnd-srd-engine` at commit `ead852b19b9e45f54f43e193caf4f10aad91a91b`
  and Foundry dnd5e at 6.0.x commit `275bed0be4ccfa15e6b3347acccb8da8784726d9`.
- Added a development-only verifier that fetches exact commits into a uniquely named operating-system
  temporary directory, verifies commit/tree/file hashes, runs only the standalone donor, and safely
  removes its exact checkout by default.
- Reproduced two stable runs. `npm ci` and `npm run build` passed. The full donor suite reproduced
  **4,610 passed, 4 failed, and 173 skipped** twice. The four failures are recorded exactly and remain
  donor limitations; they are not accepted rule differences.
- Fingerprinted Foundry without installing, building, executing, or importing it.
- Recorded the donor install's 9 reported dependency vulnerabilities. No npm dependency was added to
  DantesRoleplay and the donor remains prohibited as a production runtime package.

## Verification

- PowerShell parser and donor-lock JSON checks: passed.
- Exact commits, trees, submodule commit, and 11 required file fingerprints: stable across both runs.
- Stable result comparison: passed; timestamps/durations were excluded.
- Cleanup: both temporary roots deleted; zero matching roots remained.
- Negative unsafe-parent check: rejected before checkout/evidence creation; zero matching roots
  remained.

## Deliberate exclusions

No license permission was inferred, no provenance/coverage schema was finalized, and no D&D rule,
catalog record, application source, runtime dependency, database, public operation, or archived file
was changed. Slice 0B owns the adoption policy and ledger contracts.

