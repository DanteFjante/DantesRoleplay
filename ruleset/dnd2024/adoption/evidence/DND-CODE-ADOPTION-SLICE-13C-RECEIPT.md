# D&D code-adoption Slice 13C receipt — clean build and retained recovery

Date: 2026-08-27  
Status: **accepted**

## Verification

- Archive aggregate before and after:
  `E1AAFB069019CA45201AB92D06568840B9A7EC92EA52153CDBE3CB186AA073FF`; unchanged.
- Active D&D JavaScript syntax: **56/56** passed.
- Adoption contracts, conformance, transformation cohorts, mapping, effect allowlist, and
  impact/replay/rollback proofs: all passed, including negative cases.
- Every one of the **43** retained transformation sources matched its declared SHA-256 and produced
  the accepted target evidence.
- Release build: **0 warnings, 0 errors**.
- Disposable catalog: **144 valid records**, same **21 advisories**, no live data touched.
- Shared suite: **1,117 passed, 0 failed**; Local AI: **21 passed, 0 failed**.
- Additional real JSON-RPC walk: **6 passed, 0 failed, 2 deliberately skipped retired paths**.
- Machine report: `adoption/evidence/slice13-retained-acceptance-2026-08-27.json`.

## Boundary

The build has zero archive project/catalog/production-source consumers. The tests and transformation
tools deliberately consume retained provenance/recovery bytes. No archive, runtime, catalog,
database, migration, donor lock, or public operation changed.
