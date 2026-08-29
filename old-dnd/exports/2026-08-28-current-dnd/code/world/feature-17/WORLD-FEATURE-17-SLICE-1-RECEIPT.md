# World Feature 17 Slice 1 receipt — effect-free small-world composer

Status: **Verified scoped slice; stop before C10 Campaign composition.**
Date: 2026-08-21

## Delivered

- Added `SmallWorldBlueprint`, child result/evidence types, and `ISmallWorldCompositionPlanner`.
- Added `SmallWorldCompositionPlanner`, which derives the R3 namespace/key mapping and uses only
  `IStagedWorldComposer.StartAsync`/`AppendAsync` to dry-run the fixed World graph.
- Added focused regression coverage for valid canonical output, no-write determinism/invalid input,
  and derived-ID collision behavior.

The valid result contains exactly 58 World effects: 14 entity creates, 20 component additions,
four containment moves, and 20 relationship creates. It exposes the staged virtual World only to a
later internal C10 child, and never applies effects, starts a transaction, records an audit, emits
an event, or registers an MCP route.

## Evidence

- Focused suite: `WorldFeature17SmallWorldCompositionTests` — **3 passed, 0 failed**.
- The valid case asserts ordered local keys, root identity, 14/20/4/20 counts, effect-type/order
  boundaries, fixed adjacency, injected secret classification/visibility, and unchanged durable
  entity/component/relationship/event/operation counts.
- Invalid typed content and malformed namespace return ordered W17 problems with no effects or
  durable changes. An existing derived root ID returns `WORLD_ID_CONFLICT` and reserves no sibling
  ID.
- `git diff --check` passed for every W17/C10 artifact in this slice.

## Repository gate note

The full suite was not claimed as a fresh W17 result because a separate shared solution-wide test
run was already active and held the normal test process. The focused suite used isolated build
outputs to avoid the running MCP server's locked binaries. The previously observed full-suite
failure in `CatalogFeature10Tests.Imported_catalog_replays_the_feature_10_vertical_session_in_two_fresh_databases`
is the unrelated Feature 20 fixture delta, not W17 behavior.

No catalog artifact changed, so `roleplay validate catalog` was not required. No persistent
database import occurred. R5, the Campaign-only effect-free adapter, remains the next slice.
