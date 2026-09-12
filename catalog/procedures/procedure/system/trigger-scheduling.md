---
id: procedure.system.trigger-scheduling
category: system
name: Operate schedules and observers
governs: query and commit system.trigger-scheduling, one-time and recurring schedules, conditional observers, observation triggers, workflow targets, fires, and recovery
status: active
createdBy: "system"
changeNote: "Documents the implemented trigger scheduling and durable workflow boundary."
---

## Description
The trigger owner stores versioned schedules and observers and stages due work durably. Notification
targets preserve their existing behavior. Procedure-workflow targets enqueue through the durable task
owner after current authority and the retained binding are checked.

## Matches
schedule a procedure workflow
register a recurring job
observe a component change
run JavaScript when a relationship changes
inspect trigger fires

## Instructions
### Inspect before changing
Use `query(kind: "system.trigger-scheduling")`. Omit `applicationId` for bounded application
summaries. With an application selected, choose one closed `resource`: `overview`, `structures`,
`sources`, `devices`, `one-time`, `recurring`, `conditional`, `observation-triggers`, `observations`,
`fires`, or `phone-principal`. Supply an exact `id` when inspecting one retained registration and a
bounded `limit` when listing.

These reads intentionally omit credentials, stored verifiers, raw observation JSON, transport
headers, leases, and retained authority. A fire record is evidence to reconcile with its task or
notification owner; it is not proof of successful workflow completion.

### Closed administration input
The existing commit kind is `system.trigger-scheduling`. Its envelope is exactly
`{requestToken, operation, applicationId, value}`. Operations are `structure.register`,
`source.register`, `one-time.register`, `recurring.register`, `conditional.register`,
`observation-trigger.register`, `phone.register`, and `phone.revoke`. Always read the current
capability schema for the selected operation and preview the identical command first.

```json
{"requestToken":"0123456789abcdef0123456789abcdef","operation":"one-time.register","applicationId":"example","value":{"id":"example.reminder","version":1,"dueAtUtc":"2026-10-01T12:00:00Z","misfirePolicy":"fire-once","lifecycle":"active","notification":{"topic":"scheduled.reminder","subject":"Review","body":"Review the queued work.","stateSpaceId":null,"entityIds":[]}}}
```

Caller input cannot contain effects, events, actions, code, destinations, authorization, current
pointers, receipts, or observations. A structure registration synchronizes an already reviewed
catalog-authored schema into SQLite; it does not edit the catalog.

### Workflow targets and observers
A workflow binding pins the selected procedure revision/fingerprint, principal, application and state
revision, grant reference, canonical assignment and result schema, runtime window, and operation
allowance. Each occurrence derives a stable command. Duplicate delivery converges; recurring
occurrences remain distinct.

A conditional workflow observer uses a retained catalog JavaScript predicate over only its admitted
event, component, and directed-relationship captures. The queued match keeps the immutable observer
revision. One fire shares a causal ledger of 64 operations and may admit 1–16 workflow targets within
that ledger. Predicates cannot make service calls while matching. Unrelated state changes do not run
the target.

### Results and recovery
A successful registration returns its administration receipt; query back the exact registration and
later inspect `fires`. Workflow completion is read from the durable task owner with the retained task
handle. Preserve trigger evidence, task result, previous commits, and recovery identity separately.

Misfire policy is part of the retained schedule. A denied current grant, stale/corrupt binding, failed
AI enrollment, lost lease, or superseded revision leaves no executable task and no success receipt.
Replace or disable an observer with a new version; queued work keeps its captured revision. Reconcile
an uncertain action commit by its original receipt before retrying.

The preserving observer migration is part of the source tree. Do not claim it is installed in a live
database until the normal deployment boundary applies it and readiness confirms the owner.

## Constraints
- Never put network or AI work inside the SQLite writer transaction.
- Never serialize a live JavaScript heap. Durable work resumes from explicit task state and journals.
- A trigger registration does not create execution authority; current grants are checked again.
- Phone integration is a separate existing target and is not required for website, Codex, or runtime
  JavaScript workflows.
