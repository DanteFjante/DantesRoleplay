# Quest Feature 2 — Slice 1 receipt: offer and accept

Status: **Verified**  
Completed: 2026-08-20

## Accepted boundary

Q2.1 adds `procedure.quest.modify` and extends only the existing
`commit(kind: "quest")` closed union. It accepts exactly:

```json
{"operation":"offer|accept","questId":"quest.*","expectedQuestStatus":"draft|offered","reason":"trimmed factual text"}
```

`offer` changes only a draft root to `offered`. `accept` changes an offered root to `active`, then
activates every dormant objective whose direct prerequisites are already completed, in display-order
then ID order. The Q1 fixture therefore produces one offer event and two accept events: root plus
Trace the Missing Margin. No Q2.2 objective transition, parent reconciliation, terminal state,
reward, notification, event subscription, query kind, schema, migration, or persistent-database
import was added.

## Implemented owner and evidence

- `QuestLifecycleRunner` validates the stored Q1 graph and C3 context inside one transaction,
  dry-validates derived `component.set` effects, applies them under one operation ID, and records
  the provided reason in the immutable successful audit summary.
- `CommitTool`, `QuestTools`, `VerbSurface`, and DI expose only the closed offer/accept union; extra
  fields or unsupported lifecycle operations reject before the runner. Creation stays with
  `procedure.quest.create`.
- `QuestFeature2Tests` prove successful ordering/events/audit, stale and invalid rejection with no
  structural events, and the public closed-payload boundary.

## Verification

| Check | Result |
| --- | --- |
| `dotnet test ... --filter FullyQualifiedName~QuestFeature2Tests` | Passed 3/3. |
| Focused quest/surface/guard/protocol selection | Passed 25/25. |
| `roleplay.cmd validate catalog` | Validated 222 records; no live data touched; five existing non-blocking near-duplicate warnings. |
| Serialized full suite | Passed 491/491. |
| `git diff --check` | Passed. |

One earlier full-suite attempt had a transient catalog-hash snapshot failure while the same isolated
test passed immediately afterward; the serialized rerun above is the acceptance result.

## Next gate

Q2.2 is the next and only allowed slice. Before writing, re-read this receipt, the Q2 dependency
plan, and the three governing system/MCP/quest procedures. It may add owned objective progression
only; reconciliation and terminal operations remain Q2.3.
