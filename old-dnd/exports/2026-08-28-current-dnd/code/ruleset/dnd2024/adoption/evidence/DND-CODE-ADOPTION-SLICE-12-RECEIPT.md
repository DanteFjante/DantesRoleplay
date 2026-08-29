# D&D code-adoption Parent Slice 12 receipt — acceptance and maintenance

Date: 2026-08-27  
Status: **accepted**

## Delivered boundary

- 12A: one core-only fresh-host encounter/combat/healing flow plus replay, no-change rejection, and
  the existing injected-failure rollback proof.
- 12B: one fail-fast acceptance runner for 56 D&D scripts, all adoption tooling, release build,
  disposable catalog validation, the complete shared/Local AI suites, and opt-in protocol walks.
- 12C: exact-pin/attribution auditing plus a review-only upstream comparison with deterministic
  offline tests and safe temporary cleanup.
- 12D: parent closure with no unresolved Slice 12 implementation row.

## Consolidated evidence

- D&D fresh-host suite: **92 passed, 0 failed**.
- Release build: **0 warnings, 0 errors**.
- Catalog: **144 valid**, same **21 advisories**, no live data.
- Shared suite: **1,117 passed, 0 failed**; Local AI: **21 passed, 0 failed**.
- Real JSON-RPC protocol walks: **6 passed, 0 failed, 2 deliberately skipped retired paths**.
- Attribution/offline upstream workflow: passed; exact donor-lock SHA-256
  `43A3980EC299D57501135B48DEDB70B7B8A77FEC7716DE20E8A566A14CB9F468` retained.
- Current upstream report: primary donor unchanged; Foundry reference branch changed by 42 files and
  is explicitly `review-required`; no automatic activation, lock change, or runtime write.

## Stop boundary

Slice 13 remains planned. Retained `old-dnd/` content is not removed or superseded without the
separate destructive confirmation already required by the adoption plan.
