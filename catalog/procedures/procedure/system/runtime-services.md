---
id: procedure.system.runtime-services
category: system
name: Invoke application JavaScript and declared services
governs: permissioned application queries, Atomic actions, JavaScript service declarations, prepared execution, durable job handoff, receipts, and recovery
status: active
createdBy: "system"
changeNote: "Documents the implemented runtime-service boundary."
---

## Description
Use this contract after discovery selects an exact active query, mechanic, or procedure. JavaScript
runs in a fresh constrained engine for every invocation against one exact application generation.
Prepared programs may be reused process-locally, but no engine, JavaScript value, input, random state,
or durable heap is shared between calls.

## Matches
run an application action
call a declared JavaScript service
read application data from JavaScript
submit durable work from a mechanic
recover a partial workflow result

## Instructions
### Select the exact callable contract
1. Start from `orient()`, then use `system.feature-search` for an intent or `system.catalog.record` for
   a known qualified ID. Read the exact mechanic/procedure Markdown and current capability schema.
2. Pin application and state-space identity, definition revision and fingerprint, role bindings,
   input, and a stable command/idempotency identity. The host supplies principal, grant, deadline,
   execution profile, and operation ledger.
3. Use `application.action.execute` only when the exact Atomic mechanic and bindings are already
   known. Use `system.interaction-plan` when selection or binding is ambiguous; its proposal remains
   inert until confirmed through `system.interaction-execute`.

```json
{"idempotencyKey":"application-action.example.1","applicationId":"example","stateSpaceId":"example-space","qualifiedMechanicId":"example.mechanic.refresh","mechanicVersion":1,"contentFingerprint":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA","roleEntityIds":{"record":"entity.record"},"input":{}}
```

### JavaScript service boundary
A workflow mechanic may call only aliases retained in its exact `requirements.service` contract:

- declared reads receive bounded JSON and remain read-only;
- `ctx.services.action(alias, input)` reauthorizes the exact action and commits its typed effects in
  that action owner's transaction;
- `ctx.services.job(alias, input)` transfers the remaining root operation allowance to the declared
  procedure and returns a durable Pending handle;
- `ctx.services.job.status(handle)` treats the handle as untrusted and performs current `ReadTask`
  authorization before returning bounded inert status.

Service calls execute on the engine thread through captured JSON functions. CLR services and trusted
authority objects never enter JavaScript. A first declared action can be dry-run during candidate
validation, and a first declared procedure job can be validated without creating a task. This does
not prove arbitrary service graphs or persistent JavaScript continuation.

### Results and partial commits
Read the common invocation envelope by `tag` and `code`. A completed computation may carry bounded
`dataJson` and read evidence. A committed action carries an authoritative receipt. A durable handoff
is `pending` with a task handle. Preserve `previousCommits`, completion evidence, and recovery identity
on failure or cancellation: a later workflow failure never rolls back an earlier committed child
action.

Equal command replay returns the recorded receipt or task handle. A changed payload under the same
identity conflicts. On stale inputs, read fresh state and create a new planned attempt. On an uncertain
commit, reconcile the original operation receipt before any retry. On a durable Pending result, release
the JavaScript invocation and use the INNER read/list/wait/cancel contracts; do not keep calling services
from authored exception handling.

### Limits and recovery
Each service exchange is bounded JSON; the exact descriptor and mechanic contract provide current
limits. The registered runtime caps service calls, child reads, progress frames, source size,
preparation work, statements, recursion, memory checks, operations, and deadline. These cooperative
limits are not an operating-system process memory guarantee.

If prepared execution is suspected, the host can disable its process-local prepared-program cache
before constructing the engine and retry from retained source. A stale or unavailable selected
generation never falls back to an older executable. Resumable `ctx.services.wait`, JavaScript-stack
checkpoints, arbitrary durable JavaScript heaps, and in-process AI callbacks are unsupported.

## Constraints
- Read-only profiles reject action and job service calls.
- The root Atomic adapter does not accept parented requests; independently committed service actions
  retain their own receipts.
- Authored input cannot select authority, grants, provider/model/tool policy, budgets, or deadlines.
- Model text, progress, and proposed effects are not commit evidence.
