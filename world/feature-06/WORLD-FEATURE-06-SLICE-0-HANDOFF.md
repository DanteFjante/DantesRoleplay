# World Feature 6 — Slice 0 implementation handoff

**Assignment ID:** world-feature-06-slice-0-catalog-fixture-binding  
**Status:** Complete and awaiting review  
**Owning plan:** [World Feature 6 dependency plan](WORLD-FEATURE-06-DEPENDENCY-PLAN.md)  
**Exact slice:** Catalog fixture-bound subscription import  
**Requested outcome:** A fresh catalog import registers the Feature 6 fixed clue role with the
unquoted catalog entity ID and preserves atomic rejection of a genuinely missing fixed entity.  
**Excluded work:** Feature 6 reaction mechanics, procedures, subscriptions, gameplay behavior,
migrations, public-surface changes, and persistent catalog import.  
**Stop point:** Record the Slice 0 receipt after its focused/import validation; do not begin Slice
1.

## Required reads and verified baseline

| Source | Evidence | Required conclusion |
| --- | --- | --- |
| `AGENTS.md` | Development loop and quality gates | One reviewed slice; catalog validation and focused tests are mandatory. |
| `WORLD-FEATURE-06-DEPENDENCY-PLAN.md` | Slice 0 | Fixed roles remain required; repair the generic import/validation path only. |
| `SubscriptionStore.CheckAsync` | Failing focused test | `fixedRoleEntityIdsJson` values are stored as JSON raw text and queried as entity IDs. |
| `CatalogImporter.ApplyAsync` | Failing focused test | The entity is materialised before subscription registration in one transaction. |
| `CatalogWorldFeature6Tests` | Baseline test failure | Fresh import fails with `Missing entities: "clue.feature-04.oren-letter"`. |

## Closed behavior contract

- `fixedRoleEntityIdsJson` is a JSON object whose values are nonempty strings containing entity
  IDs. The canonical persisted representation remains a JSON object.
- Payload-equality values remain JSON scalars and keep their raw JSON representation; this slice
  must not change their matching semantics.
- A fixed role with a missing entity still fails validation. Import rollback leaves no subscription
  row or version behind.
- A real fixed entity, including `clue.feature-04.oren-letter`, validates through the production
  importer without test-only pre-seeding.

## Allowed changes and verification

**Allowed files:** `DantesRoleplay.DataAccess/SubscriptionStore.cs`,
`DantesRoleplay.Tests/CatalogWorldFeature6Tests.cs`, this handoff, and the Slice 0 receipt.

**Required checks:**

1. The focused Feature 6 test proves the fresh-import bind and the missing-target rollback case.
2. `roleplay validate catalog` succeeds against its disposable database.
3. Existing subscription-store tests and the full suite pass.
4. `git diff --check` reports no whitespace errors.

**Result:** The focused import regression, catalog validation, subscription-store coverage, and
full repository suite passed. The minimal repair was fixed-role entity-ID decoding in
`SubscriptionStore`; no importer-order change was needed.

**Escalate instead of widening:** a need to change importer ordering, any subscription routing
semantics beyond fixed-role ID decoding, a migration, or a new catalog/runtime contract.
