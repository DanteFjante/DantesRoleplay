# Executable workflow plan

Status: **Draft — design plan only; no implementation is authorised by this document**  
Last updated: 2026-08-20

## Goal

Allow a caller to invoke a registered, versioned workflow through one MCP call. A workflow runs a small, validated sequence of semantic commands in one root transaction. Later steps can consume named outputs from earlier steps and query or act on state those steps have changed. Success commits one coherent change; any step failure rolls the whole root change back.

    caller -> commit(kind: "workflow", payload: { id, input })
           -> resolve active workflow definition
           -> run validated steps against one ambient transaction
           -> validate/apply/route events where each command normally does
           -> commit or rollback once
           -> return one root result with per-step receipts

A workflow is not an arbitrary list of caller-supplied commit calls, a way to execute SQL, JavaScript, HTTP calls, or tool handlers recursively, a replacement for event reactions or mechanics, or permission for individual steps to commit independently.

The existing procedure contracts remain instructions for agents. This feature deliberately uses the separate term **workflow** for executable, stored data.

## User-facing examples

### Travel: move, then discover

    workflow: travel.move-and-discover
    input: partyId, destinationId

    1. Validate the requested journey for this party and destination.
    2. Apply the move.
    3. Read the party's now-current location.
    4. Run a discovery action against that location.
    5. Return movement and discovery results.

Step 4 observes the new location produced by step 2, even though the database transaction has not committed yet. If discovery fails, the party did not move.

### Later map interaction

A future map may send one intent such as “travel to Ashwood”, not a sequence of browser calls. The corresponding workflow validates reachability, changes location, advances time, invokes any relevant rule, and returns an updated map resource. The map must never translate a click directly into raw world effects.

## Governing invariants

1. **One root transaction.** The workflow runner owns begin, commit, and rollback. Every participating command shares its ambient unit of work and must not independently commit.
2. **Semantic commands only.** A step names an approved in-process command capability, never an MCP endpoint, database table, or service method selected by reflection.
3. **Definitions are data.** Workflow identity is stable; authored definitions are append-only versions with status, scope, source hash, author, and change note.
4. **Closed, linear control flow first.** Version 1 supports a finite ordered list of steps. No branches, loops, retries, recursion, nested workflow calls, or dynamic step creation.
5. **Explicit bindings, no expressions.** A step may take a literal JSON value, a root-input JSON Pointer, or a named prior-step result JSON Pointer. There is no interpolation, code, template language, or access to arbitrary state.
6. **Declared I/O.** A workflow declares an input JSON schema and output/result schema. Each command capability declares its own request and result contract. Validate before any world change where possible.
7. **One operation, inspectable steps.** The root audit identifies workflow id/version, root input, correlation, outcome, and receipts for every completed step. It is observable evidence, not chain-of-thought.
8. **Event safety carries through.** Effects, guards, accepted events, reactions, and notifications remain part of the same root transaction and share the root operation/correlation ID.
9. **Bounded execution.** Start with a maximum of 12 steps, one execution depth, a total timeout, and size limits for input and stored step receipts.
10. **Discoverable and governed.** A workflow has procedure contracts describing creation, revision, execution, inspection, and recovery from each rejection.

## Proposed stored model

### Workflow identity

- Id: permanent lowercase dotted identifier, for example travel.move-and-discover.
- Scope: shared or campaign/ruleset scope, following the mechanic-scope policy.
- Status: draft, active, deprecated, or archived.
- Current version, timestamps, and optional display metadata.

### Workflow version

Each version stores:

- name, description, change note, source hash, author, and created time;
- input JSON Schema and declared output schema;
- ordered step definitions;
- the explicitly allowed command identifiers;
- a required-procedure list and any capability/version compatibility data.

A workflow revision never changes old execution evidence. A running execution resolves one exact active version before it starts.

### Workflow execution evidence

Successful executions are committed with the root operation and retain:

- workflow id/version, input, output, root operation/correlation id, timestamps, and status;
- one ordered receipt per completed step: step id, command id/version, bound input, output summary, affected entity IDs, relevant event IDs, elapsed time, and deterministic seed if one was used.

If any step fails, the root transaction rolls back. Record a durable failure operation outside the rolled-back transaction, following the existing action-failure pattern, with workflow/version, failing step, stable error, and safe diagnostic evidence. Do not leave a misleading partial-success history.

## Workflow definition grammar

Use JSON as the authored transport, but keep its vocabulary closed and independently validated.

    {
      "id": "travel.move-and-discover",
      "operation": "define",
      "inputSchema": { "type": "object", "required": ["partyId", "destinationId"] },
      "steps": [
        {
          "id": "move",
          "command": "world.travel.move",
          "input": {
            "partyId": { "$from": "/input/partyId" },
            "destinationId": { "$from": "/input/destinationId" }
          }
        },
        {
          "id": "discover",
          "command": "action.run",
          "input": {
            "intent": "discover current location",
            "roleEntityIds": {
              "actor": { "$from": "/input/partyId" },
              "location": { "$from": "/steps/move/output/currentLocationId" }
            }
          }
        }
      ],
      "result": {
        "movement": { "$from": "/steps/move/output" },
        "discovery": { "$from": "/steps/discover/output" }
      }
    }

The example uses future semantic command names, not current implementation promises. Step definitions validate all referenced steps and JSON Pointers before execution starts. A binding may select a JSON value; it cannot concatenate strings, call functions, inspect the database, or suppress errors.

## Command capability boundary

The current MCP commit handlers are transport adapters, and several own their own logging or transaction behaviour. The workflow runner must not invoke those handlers. First extract in-process command services with contracts like:

    command id -> validated request -> execute in WorkflowExecutionContext -> typed result/receipt

WorkflowExecutionContext provides the current database context/transaction, root operation and correlation IDs, cancellation/deadline, procedure evidence, and bounded execution state. A command service uses the caller-owned transaction and returns an ordinary typed result.

Start with a small allowlist of commands whose transaction and error semantics are proven. The first vertical workflow uses game/world commands only. Do not allow workflow steps to create procedures, component definitions, mechanics, event types, subscriptions, or other workflows: authoring the system and playing the game have different review and safety needs.

An action-running command needs an internal entry point that accepts the ambient context instead of always beginning and committing its own transaction. Existing normal MCP action execution keeps its public behaviour by invoking that shared entry point inside a one-step root execution.

## MCP surface

Keep the three verbs.

- query(kind: "workflows") lists discoverable workflow summaries; with an id it returns the current or requested version and its input contract.
- commit(kind: "workflow") has an explicit operation field:
  - define creates or appends a version after validation/dry-run;
  - run resolves an active version and executes it with supplied input;
  - lifecycle operations can be added later only when their contract and audit semantics are specified.
- query(kind: "history") and workflow detail expose root execution evidence and step receipts without exposing internal code or unbounded payloads.

The public envelope includes root operation ID, workflow/version, summary, final output, affected entity IDs, and completed-step summaries. On failure it identifies the failed step and offers the next governed recovery call. It never claims earlier steps succeeded after their writes rolled back.

## Delivery slices

### Slice 0 — ratify boundaries and first vertical example

Choose one real, future game workflow that contains at least two dependent world commands. Write procedure contracts, command capability catalog, definition schema, error envelope, ownership model, limits, and audit/privacy policy. Decide workflow scope and catalog import/export format.

**Acceptance:** the travel example can be expressed without an escape hatch, and every proposed command is classified as allowed or forbidden with a reason.

### Slice 1 — make transaction ownership composable

Refactor action/effect/event paths so they can execute under an explicit ambient root transaction. Preserve current one-call MCP behaviour as a wrapper around the same shared path. Ensure no nested step commits and no post-commit notification is published prematurely.

**Acceptance:** a two-command in-process test sees the first command's uncommitted change; an injected second-command failure leaves no world, event, notification, or success-audit change.

### Slice 2 — workflow persistence and discovery

Add workflow identity/version/execution models, database mappings/migration, store interfaces, catalog import/export/manifest support, version/source-hash behaviour, and read/query contracts. Add procedure.workflow.define, procedure.workflow.run, and procedure.workflow.inspect.

**Acceptance:** definitions are append-only, catalog round-trip is exact, and an inactive or incompatible definition cannot execute.

### Slice 3 — definition validator and bindings

Implement the closed JSON grammar, input/output schema validation, duplicate-step detection, forward-reference rejection, command allowlist checks, JSON-Pointer binding validation, payload size limits, and deterministic validation errors. Add a dry-run that reports named checks and does not alter state.

**Acceptance:** invalid definitions fail before execution; valid bindings resolve only root input or completed prior-step results; arbitrary expressions and undeclared command IDs are rejected.

### Slice 4 — workflow runner and audit evidence

Implement the runner, ambient execution context, sequential binding/result flow, root operation allocation, success receipts, failure audit, timeout/step limits, and result envelope. Wire the first approved semantic commands through the command capability interface.

**Acceptance:** a successful workflow yields one root operation and complete step receipts; a failure at each step position rolls back all writes and has a precise durable failure record.

### Slice 5 — MCP integration and contracts

Add the closed query(kind: "workflows") and commit(kind: "workflow") routes, capability descriptions, procedure contracts, bootstrap/catalog definitions, protocol-walk coverage, and examples. Ensure workflows are discoverable but not silently selected by intent routing.

**Acceptance:** a fresh MCP session can discover, validate, execute, inspect, and recover from one workflow using only orient, query, and commit.

### Slice 6 — events, subscriptions, and live UI integration

Run workflows through the completed event/subscription runtime. Add workflow identity/version and step identity to relevant event/operation evidence. Browser SSE observes only committed root outcomes, then refreshes normal resources.

**Acceptance:** an event reaction caused by a workflow is indistinguishable in atomicity and audit quality from one caused by a single action; a rolled-back workflow creates no SSE invalidation.

### Slice 7 — prove with a map-facing workflow

Once map and travel rules have their own approved plans, add one read/write workflow behind a single map interaction plus accessible non-map controls. Handle revision/concurrency failures by reloading the current map state and reporting a recoverable domain error.

**Acceptance:** one map intent produces one workflow execution, an auditable world change, related events, and an updated read model; a stale or invalid move changes nothing.

## Test matrix

- definition schema, status/scope, compatibility, and catalog round-trips;
- allowed/forbidden command matrix and no invocation of MCP transport handlers;
- root-input and prior-result binding, all invalid pointer cases, and output shaping;
- same-transaction read-after-write;
- rollback on validation, command, effect, guard, reaction, cancellation, timeout, and persistence failure;
- exact root/causation/correlation IDs across effects, events, notifications, workflow, and audit;
- no leaked successful step execution after root rollback;
- concurrent workflow isolation and optimistic-revision conflict behaviour;
- protocol walk from a fresh MCP client;
- replay/determinism where an action step uses random mechanics;
- post-commit SSE ordering once the website event bridge exists.

## Non-goals and revisit triggers

Do not add branching, conditions, loops, retries, arbitrary result transforms, nested workflows, parallel steps, caller-defined pipelines, remote workflows, or cross-database transactions in the first release. Each would change determinism, auditability, or transaction semantics enough to deserve a separate design.

Revisit branching only after several registered workflows demonstrate the same bounded conditional need. Revisit composition of mechanics separately: workflow composition coordinates semantic commands, whereas mechanic composition reuses pure rule logic.

## Dependencies and ordering

The event/subscription work currently in progress is a prerequisite for the full integration slice, because workflows must share its root transaction, chain limits, and durable event evidence. The feature may begin design and transaction-boundary refactoring earlier, but should not expose an executable workflow publicly until events/subscriptions are verified.

This plan intentionally changes the wording in NEXT_STEPS.md that rules out arbitrary multi-commit pipelines: registered, typed, bounded, transaction-owned workflows are the safe alternative. Update that roadmap entry only when Slice 0 is ratified.
