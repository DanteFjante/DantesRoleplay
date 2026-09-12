---
id: procedure.system.inner-worker.wait
category: system
name: Wait for a focused inner worker
governs: System capability system.inner-worker.wait
status: active
createdBy: "system"
changeNote: "Documents bounded selected-application worker waiting."
---

## Description
Wait performs bounded asynchronous readback for one exact retained focused-worker handle in the selected application and state space. The closed input is `stateSpaceId`, `taskId`, `commandId`, and `waitMilliseconds` from 0 through 25,000. The host retains one bounded wait deadline and rechecks current state and `ReadTask` authority without holding database or execution resources between reads.

The response is the actual full invocation envelope returned by durable readback. A timeout returns the latest `pending` result; it does not create completion evidence. Terminal completion, failure, cancellation, revocation, and recovery evidence are returned unchanged.

```json
{"stateSpaceId":"example-space","taskId":"task.0123456789abcdef0123456789abcdef","commandId":"command.0123456789abcdef0123456789abcdef","waitMilliseconds":25000}
```

## Matches
wait for focused worker
await delegated task
long poll inner worker

## Instructions
1. Reuse the exact state-space, task, and command identities returned by submission or an authorized list.
2. Choose a wait no longer than 25 seconds and invoke `system.inner-worker.wait` through the same selected application.
3. Treat `pending` as a timeout or unfinished task and retain the same handle. Preserve every terminal result and its evidence as returned.

## Constraints
- Waiting creates no task, receipt, checkpoint, execution, or additional AI allowance.
- Waiting stops when current authorization is revoked, the task is cancelled, the bounded wait expires, or the host deadline is reached.
- The host may refresh the current state revision, but it cannot replace the retained task scope or reset the wait budget.
