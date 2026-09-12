---
id: procedure.system.inner-worker.submit
category: system
name: Submit a focused inner worker
governs: System capability system.inner-worker.submit
status: active
createdBy: "system"
changeNote: "Documents the selected-application durable worker submission contract."
---

## Description
Submit starts one bounded durable focused-worker assignment for the currently selected application and state space. The caller supplies only the exact procedure, instruction, result schema, and prior task handles. The host selects the workflow profile, worker model and provider, tools, current grant, operation budget, and deadline. A current state-space `Execute` standing grant must authorize the exact selected procedure.

The closed input is `stateSpaceId`; `procedure` with `definitionId`, positive `revision`, and 64-character uppercase `contentFingerprint`; `instruction`; `resultSchema`; and `dependencyHandles`. The instruction is 1–8,000 characters. `resultSchema` is a JSON string containing a bounded closed JSON Schema and is at most 16,000 bytes after canonicalization. `dependencyHandles` contains at most 16 durable handles with distinct task IDs, each carrying `taskId` and `commandId`.

The result is the full invocation envelope: `tag`, `code`, `message`, `dataJson`, `readEvidence`, `receipt`, `proposal`, `pending`, `completionEvidenceReference`, `previousCommits`, and `recoveryIdentity`. Successful submission is `pending`; retain the task and command identity carried by `pending`. Submission does not mean the worker completed or that its output is valid.

## Matches
delegate a bounded task
start a focused inner worker
submit background work

## Instructions
1. Discover the application procedure and pin its exact current definition ID, revision, and content fingerprint. Its authored `Governs` value must use the supported explicit form `execute <qualified-id>` or `query(kind: "<qualified-id>")`; never derive tools from prose.
2. Write one complete result schema and a bounded instruction. Pass only retained dependency handles from earlier durable tasks; use an empty array when there are none.
3. Invoke `system.inner-worker.submit` through the selected-application gateway with one stable idempotency key. Do not put a model, provider, profile, tools, grant, budget, or deadline in the input.
4. Preserve the returned task ID and command ID. Reconnect with `system.inner-worker.read`; use `system.inner-worker.cancel` only when cancellation is intended.

## Constraints
- The selected application and state space must still match current activation and standing-grant evidence.
- Do not report `pending` as completion. Use only the exact retained handle with read, list, or bounded wait.
- Reusing an idempotency key for different input is a conflict.
