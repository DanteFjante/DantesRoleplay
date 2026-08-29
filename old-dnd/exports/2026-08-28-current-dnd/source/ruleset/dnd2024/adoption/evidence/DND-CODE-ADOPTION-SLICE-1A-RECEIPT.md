# D&D code-adoption Slice 1A corrective receipt — capability inventory

Status: **accepted after corrective review 2026-08-25**
Implementation: [Slice 1A](../../DND-CODE-ADOPTION-SLICE-1A-IMPLEMENTATION.md)
Matrix: [coverage-matrix-1a.json](coverage-matrix-1a.json)

The corrected generator uses the current and pre-archive manifests as capability owners rather than
counting files. It produced 271 unique `{kind,id,version}` rows: 127 exact active/archive content-hash
matches and 144 archive-only candidates. Related schemas/JavaScript are grouped on their owner row.
Exact-ID scanning found historical tests for 170 rows, dependencies for 250 rows, and archived SRD
locator evidence for 35 rows; none of those locators is marked officially verified.

Input SHA-256: `F28DCAE3848BC2620ACE7B876C0596F13EAB6C7A6864B04B3ADD62AF66A62125`.
Matrix SHA-256: `7BE91888393705B8D36FF5E165AA09C245CE0D9561A83B08378FC542D0300AB8`.
Two consecutive generations were identical and the matrix passed the accepted Draft 2020-12 schema.
No runtime, catalog, database, public operation, or archive file changed.
