---
id: procedure.system.inner-worker.read
category: system
name: Read a focused inner worker
governs: System capability system.inner-worker.read
status: active
createdBy: "system"
changeNote: "Documents the selected-application durable worker read contract."
---

## Description
Read reconnects to one retained focused-worker task for the currently selected application and state space. The closed input is exactly `stateSpaceId`, `taskId`, and `commandId`. The host reconstructs current workflow authority; a current state-space `ReadTask` standing grant is required. No idempotency key or write confirmation is required.

The response is the full invocation envelope: `tag`, `code`, `message`, nullable `dataJson`, `readEvidence`, `receipt`, `proposal`, `pending`, nullable `completionEvidenceReference`, up to 64 `previousCommits`, and `recoveryIdentity`. Interpret the envelope by `tag` and `code`: `pending` means retain the same handle and read again later; `completed` carries the validated bounded result in `dataJson` and completion evidence; `failed`, `cancelled`, and `unavailable` are distinct terminal or recovery states. Commit and recovery evidence must remain attached to the result that supplied it.

```json
{"stateSpaceId":"example-space","taskId":"task.0123456789abcdef0123456789abcdef","commandId":"command.0123456789abcdef0123456789abcdef"}
```

## Matches
check focused worker progress
read a delegated task
reconnect to inner worker

## Instructions
1. Reuse the exact `stateSpaceId`, `taskId`, and `commandId` returned by submission. Do not substitute a current or guessed handle.
2. Invoke `system.inner-worker.read` through the same selected application. Keep provider, model, tools, grants, budgets, and worker lifecycle fields out of the input.
3. If the result is `pending`, retain the handle and retry later. If it is `completed`, validate `dataJson` against the submitted result schema and preserve completion and prior-commit evidence.
4. Report failed, cancelled, unavailable, and recovery-required outcomes as returned. Use cancel only through its separate capability.

## Constraints
- Reading one handle does not grant execution, cancellation, task listing, or access to another task.
- Listing and bounded waiting use their separate selected-application capabilities and the same current `ReadTask` authority.
- A stale, foreign, revoked, or mismatched handle remains denied or unavailable.
