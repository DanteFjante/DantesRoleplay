---
id: procedure.system.inner-worker.cancel
category: system
name: Cancel a focused inner worker
governs: System capability system.inner-worker.cancel
status: active
createdBy: "system"
changeNote: "Documents the selected-application durable worker cancellation contract."
---

## Description
Cancel requests cancellation of one retained focused-worker task for the currently selected application and state space. The closed input is exactly `stateSpaceId`, `taskId`, and `commandId`. A current state-space `CancelTask` standing grant is required, and the selected-application gateway requires a stable idempotency key.

The response is the full invocation envelope: `tag`, `code`, `message`, `dataJson`, `readEvidence`, `receipt`, `proposal`, `pending`, `completionEvidenceReference`, `previousCommits`, and `recoveryIdentity`. A cancellation request may race with completion, so preserve the returned envelope and reconcile the same handle with `system.inner-worker.read`. Cancellation is not permission to remove retained task history or committed effects.

```json
{"stateSpaceId":"example-space","taskId":"task.0123456789abcdef0123456789abcdef","commandId":"command.0123456789abcdef0123456789abcdef"}
```

## Matches
cancel a focused inner worker
stop a delegated task
cancel background work

## Instructions
1. Reuse the exact `stateSpaceId`, `taskId`, and `commandId` from the retained handle.
2. Invoke `system.inner-worker.cancel` with a stable idempotency key. Do not send a model, provider, profile, tools, grant, budget, deadline, or replacement task identity.
3. Preserve cancellation and recovery evidence. Read the same handle afterward when the result requires reconciliation.
4. If work must be resubmitted, use a new logical submission and its own stable idempotency key; do not rewrite the cancelled handle.

## Constraints
- Cancellation requires current `CancelTask` authority and never implies `Execute` or `ReadTask` authority.
- Listing and bounded server-side waiting remain separate read capabilities and require current
  `ReadTask` authority.
- Already completed, foreign, stale, or revoked tasks remain governed by their returned result and retained evidence.
