---
id: procedure.system.inner-worker.list
category: system
name: List focused inner workers
governs: System capability system.inner-worker.list
status: active
createdBy: "system"
changeNote: "Documents the bounded selected-application worker list contract."
---

## Description
List returns at most sixteen authorized focused-worker handles and their full current invocation envelopes for the selected application, principal, and state space. A current state-space `ReadTask` standing grant is required for every returned task. The closed input is `stateSpaceId`, `pageSize` from 1 through 16, and an optional opaque `cursor` from the preceding page.

The completed list computation carries `items` and nullable `nextCursor` in `dataJson`. Each item contains the exact durable handle and its full nested invocation result, including completion evidence, previous commits, and recovery identity when present. A list computation is not evidence that any listed worker completed.

## Matches
list focused workers
show delegated task progress
page through inner workers

## Instructions
1. Select the same application and state space used for submission.
2. Invoke `system.inner-worker.list` with a page size no greater than sixteen. Reuse only the opaque cursor returned by the previous page.
3. Interpret every nested result independently by its `tag` and `code`, preserving its evidence and recovery fields.
4. Continue only while `nextCursor` is present.

## Constraints
- The list omits tasks that do not pass a fresh current `ReadTask` authorization check.
- A cursor grants no authority and must not be edited or used with another scope.
- Listing does not execute, resume, cancel, or alter a task.
