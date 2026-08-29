# D&D code-adoption Slice 12B receipt — full validation and protocol evidence

Date: 2026-08-27  
Status: **accepted**

## Accepted boundary

- `Invoke-Slice12Acceptance.ps1` now provides one fail-fast same-worktree acceptance command.
- It verifies active D&D JavaScript, every accepted adoption contract/tooling family, release
  compilation, disposable catalog validity, shared and Local AI regression, and the opt-in real
  JSON-RPC protocol walk.
- A zero-match test filter is a failure even when the test runner returns exit code zero. This
  prevented the first protocol attempt from being accepted and is covered by the corrected run.
- The machine-readable result is
  `adoption/evidence/slice12-acceptance-2026-08-27.json`.

## Successful consolidated run

- Active D&D JavaScript: **56/56** syntax checks passed.
- Adoption contracts, conformance, transformation/cohort, mapping, effect-allowlist, and
  impact/replay/rollback tooling: all passed, including their negative cases.
- Release build: **0 warnings, 0 errors**.
- Catalog: **144 valid records**, the same **21 advisory warnings**, no live data touched.
- Shared suite: **1,117 passed, 0 failed, 0 skipped**.
- Local AI: **21 passed, 0 failed, 0 skipped**.
- Real JSON-RPC protocol walk: **6 passed, 0 failed, 2 deliberately skipped**. The active tests
  verify the public three-verb surface and end-to-end orient/query/commit behavior. The two skipped
  tests document retired authored-procedure paths and are not hidden by the runner.

## Deliberate exclusions

No donor checkout or upstream network comparison ran, no donor lock or runtime artifact changed,
and no live database was opened. Attribution/upstream maintenance remains Slice 12C.
