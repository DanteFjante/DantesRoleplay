# E6 Slice 1 receipt — typed dependent mechanic composition

Status: **Accepted 2026-08-21.**

## Delivered

- Added the closed child declaration
  `inputFromChildData: { resultKey: "<sibling key>" }`.
- Validates mutually exclusive input sources, sibling existence, single-invocation producers,
  self-reference, and dependency cycles before execution.
- Executes dependencies first with lexical result-key tiebreaks; legacy declarations retain their
  existing lexical order and seed derivation.
- Gives the dependent child a deep JSON copy only when the producer's `output.data` is one object.
  Scalar, array, null, malformed, missing, or multi-result producer data fails composition before
  the dependent child or parent source runs.
- Documents the boundary in the mechanic-projection contract and adds generic, effect-free tests.

## Not delivered

No effect-order compatibility work, consumer migration, virtual/staged actor state, event routing,
query capability, or game-specific mechanic was added. Those remain outside Slice 1.

## Verification

| Check | Result |
| --- | --- |
| E6 focused composition tests | Passed: 6 tests |
| Existing action-runner and mechanic-store tests | Passed: 41 tests |
| `roleplay.cmd validate catalog` | Validated 313 records with 39 advisory overlap warnings; no live data touched |
| Full repository suite | Passed: 642 tests, 0 failures (the runner's TRX report confirmed all tests completed successfully; its host required termination only during post-run cleanup) |

No persistent catalog import or live campaign change was performed.
