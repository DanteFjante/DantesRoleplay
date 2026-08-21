# Quest Feature 3 — Slice 1 validation: bounded trusted-host summary

Status: **Implemented; global acceptance pending unrelated repository failure**  
Completed implementation: 2026-08-21

## Delivered boundary

Q3.1 adds the fixed, trusted-host `query(kind: "quest-summary", id: "quest.*")` read governed
by `procedure.quest.inspect`.

It returns only an active, valid Q1–Q2 quest: current root fields; exactly three display-ordered
objectives; each objective's bounded target-id/role/audience evidence metadata; at most twelve
verified lifecycle component replacements; and the fixed statement that visibility is descriptive,
not authorization. The reader fails closed for invalid current graph/context and omits malformed
historical records. It neither writes state nor reads target content or operation prose.

The public query is closed: it requires one lowercase `quest.*` id and rejects every other query
filter with `INVALID_QUEST_SUMMARY_QUERY` and a literal recovery call.

## Verification evidence

| Check | Result |
| --- | --- |
| Q3 quest/surface/guard/protocol selection | Passed 33/33. |
| Catalog validation | Validated 238 records with 4 existing/unrelated near-duplicate warnings; no live data touched. |
| Whitespace validation | Passed; Git reported existing line-ending notices only. |
| Full suite immediately before the closed-input correction | Passed 506/506. |
| Full suite after the correction | 504/506; failures are `CatalogValidationTests.Repository_catalog_validates_without_changing_its_files` and `CatalogFeature12Tests.Starting_and_advancing_turns_restore_only_the_newly_active_participant_budget`. |
| Re-run of the two full-suite failures | Catalog validation test passed; the Catalog Feature 12 turn-budget test still failed in isolation. |

The persistent failure is outside Q3.1's quest read/query/procedure boundary. Do not alter the
turn-budget system as part of this slice. Re-run the full suite once that owning worktree change is
resolved; only then replace this validation note with an accepted Q3.1 receipt.

## Next gate

Q3.2 remains blocked by the missing S1 storytelling procedure. It must not add another query,
state record, audience policy, recap generation, or lifecycle mutation.
