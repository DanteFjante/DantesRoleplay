# Quest Feature 2 — Slice 2 receipt: owned objective progression

Status: **Verified**  
Completed: 2026-08-20

## Accepted boundary

Q2.2 extends the existing `procedure.quest.modify` and `commit(kind: "quest")` lifecycle union.
It adds only these closed requests:

```json
{"operation":"set-objective","questId":"quest.*","expectedQuestStatus":"active","objectiveId":"quest.*","expectedObjectiveStatus":"active","targetStatus":"completed|blocked|failed","reason":"trimmed factual text"}
```

```json
{"operation":"unblock-objective","questId":"quest.*","expectedQuestStatus":"active","objectiveId":"quest.*","expectedObjectiveStatus":"blocked","reason":"trimmed factual text"}
```

Completion writes the owned objective first, then every newly eligible dormant dependant in
display-order then ID order. Blocking, failing, and unblocking write only the owned objective.
All calls require the active parent quest, an owned objective, exact expected state, and a factual
reason. The parent stays active: Q2.2 does not reconcile or otherwise change it.

No new ID, schema, relationship, mechanic, event/subscription, query/commit kind, migration,
reward, notification, or persistent-database import was added.

## Evidence

- `QuestObjectiveTransitionRequest`, `QuestLifecycleRunner`, `QuestTools`, and `CommitTool` retain
  one lifecycle owner and reject extra or unsupported public fields before running.
- `QuestFeature2Tests` prove Trace completion produces three ordered effects (Trace complete, then
  Test and Read active), optional completion leaves the quest active, block/unblock are one-effect
  operations, and stale/foreign/illegal requests leave no structural events.
- The procedure and capability description now state the exact Q2.2 operations and retain Q2.3 as
  the owner of reconciliation and terminal state.

## Verification

| Check | Result |
| --- | --- |
| `dotnet test ... --filter FullyQualifiedName~QuestFeature2Tests` | Passed 5/5. |
| Focused quest/surface/guard/protocol selection | Passed 27/27. |
| `roleplay.cmd validate catalog` | Validated 228 records with 0 warnings; no live data touched. |
| Serialized full suite | Passed 498/498. |
| `git diff --check` | Passed (existing line-ending notices only). |

## Next gate

Q2.3 is the next and only allowed slice. It may add explicit reconciliation and terminal
correction only; it must not introduce history projection, event automation, rewards, campaign
mutation, or generic correction.
