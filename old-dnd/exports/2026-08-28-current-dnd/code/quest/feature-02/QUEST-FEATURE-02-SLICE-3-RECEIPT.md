# Quest Feature 2 — Slice 3 receipt: reconciliation and terminal correction

Status: **Verified — Quest Feature 2 accepted**  
Completed: 2026-08-20

## Accepted boundary

Q2.3 completes the existing `procedure.quest.modify` and `commit(kind: "quest")` lifecycle union.
It adds the following closed root operations:

```json
{"operation":"reconcile|fail|reopen-quest|archive","questId":"quest.*","expectedQuestStatus":"active|completed|failed|offered","reason":"trimmed factual text"}
```

It also adds `reopen-objective` using the Q2.2 closed objective shape without `targetStatus`.

`reconcile` is explicit: a failed required objective wins; otherwise all required objectives
completed makes the parent completed; otherwise it returns `NO_RECONCILIATION_CHANGE` with no
structural effect. `fail`, `reopen-quest`, and `archive` each change only the root. An objective
can reopen only from completed/failed, with completed prerequisites and no completed direct or
transitive dependant. Every success is one atomic derived structural batch; no operation changes a
campaign, world, reward, evidence, notification, item, character, time, or authorization record.

## Q2 completion

The complete manual quest lifecycle is now accepted:

1. Q2.1 — offer a draft and accept an offer, activating eligible initial objectives.
2. Q2.2 — complete/block/fail/unblock owned objectives, activating newly eligible dependants.
3. Q2.3 — explicitly reconcile parent state and apply narrowly guarded terminal correction.

Q2 adds no new database schema, quest table, relationship, mechanic, event/subscription, query or
commit kind, reward, automation, or persistent-database import. Q3 owns the next history/read
projection boundary.

## Verification

| Check | Result |
| --- | --- |
| `dotnet test ... --filter FullyQualifiedName~QuestFeature2Tests` | Passed 8/8. |
| Focused quest/surface/guard/protocol selection | Passed 30/30. |
| `roleplay.cmd validate catalog` | Validated 231 records with 0 warnings; no live data touched. |
| Serialized full suite | Passed 503/503. |
| `git diff --check` | Passed (existing line-ending notices only). |

## Next gate

Quest Feature 2 is complete. Q3, bounded trusted-host quest/evidence/history projection, requires
its own dependency plan and acceptance boundary. It must consume the Q2 owner rather than creating
another mutable quest state store.
