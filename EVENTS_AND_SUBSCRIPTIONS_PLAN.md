# Events and subscriptions

Status: **Slices 1–4 verified (322/322); Slice 5 in progress — 5a and 5b written, awaiting a build**
Last updated: 2026-08-19

Slice 1 delivered the versioned event-type tables, migration, JSON Schema syntax validation,
file-first `catalog/event-types/` import/export, the nine reserved `world.*` structural schemas,
and `query(kind: "event-types")` / `commit(kind: "event-type")`. It intentionally does not
emit, persist, route, subscribe to, chain, or notify on events. Evidence: catalog import created
10 records and `verify` reported 84 unchanged records; the full test suite passed 304/304.

## Goal

Enable reliable reactive play: registered guard middleware can veto a proposed world change before
commit, an accepted world change emits a named event, eligible reaction subscriptions can run a
stored reactive mechanic, and any resulting effects can emit further events. The entire decision
and reaction chain remains deterministic, auditable, bounded, and transactional.

Examples include a condition expiring, a location trigger firing when an entity enters it, or a
quest state advancing after a required relationship is created.

## Core model

- **Event type:** a registered, versioned identifier with a description and JSON payload schema.
  Event names are lowercase dot paths, such as `world.component.changed` or
  `ruleset.dnd2024.condition.expired`. There are no ad-hoc event strings.
- **Event:** immutable ledger record with ID, type/version, scope, payload, timestamp, root
  correlation ID, causation ID, depth, sequence, root operation ID, producer evidence, and affected
  entity IDs.
- **Subscription:** immutable, versioned registration with mode `guard` or `reaction`, ID,
  event-type ID, tracked-entity scope, exact-match payload filters, target event-mechanic ID,
  deterministic order, per-chain execution limit, status, and change note. Its mechanic must
  already be active and declare the same event mode and context it needs.
- **Guard middleware:** a matching `guard` subscription evaluates a proposed event while its
  effects exist only inside the root transaction. It must return `allow` or `deny`, cannot rewrite
  the proposal or produce effects/events/notifications, and any denial rolls back the whole root.
  No matching guards means allow.
- **Event mechanic:** an ordinary stored mechanic with explicit `guard` or `reaction` event
  requirements. A guard receives a frozen proposal and returns only a decision; a reaction receives
  a frozen accepted event and may return normal effects, narration, optional registered derived
  events, and optional in-system notifications. Unknown event types are rejected atomically.
- **Subscription execution:** immutable evidence linking one proposed or accepted event context and
  exact subscription/mechanic versions to mode, derived seed, structured decision/result, outputs,
  and status. A denied proposal's durable evidence lives in the failed root audit because its
  transaction and proposed event are rolled back.
- **Notification:** immutable transactional output with topic, subject, body, tracked entity IDs,
  source event/execution IDs, created time, and mutable read/archive state. It is not world state
  and is not an external webhook.

## Processing and failure semantics

```text
Root action/effects transaction
  → complete effect batch validates and applies inside the uncommitted transaction
  → host creates immutable proposed event envelope(s) from effect receipts
  → matching active guard subscriptions run by order then subscription ID
  → any deny rolls back the root; all allow accepts the proposed event(s)
  → accepted event rows are inserted
  → matching active reaction subscriptions run by order then subscription ID
  → each reaction mechanic executes against its frozen accepted-event projection
  → returned effects, registered derived events, and notifications are validated
  → effects apply and may produce structural events
  → derived and structural events continue the queue in stable order
  → event, execution, notification, world state, and root audit commit together
```

- Root actions initially emit only a closed mapping of successfully applied structural effects.
  Failed actions emit nothing because their transaction is rolled back. Action-lifecycle event
  types are deferred until their transactional meaning is separately specified.
- Reactive mechanics may propose an `events` array, but every type must be registered and every
  payload must pass that type's schema. There is no free-form `event.emit` effect and ordinary
  root action mechanics do not emit custom events in the first release.
- Every event chain shares one correlation ID. Each child records its direct causation ID.
- A guard denial, reactive failure, invalid proposed effect, unknown event type, or chain-limit breach aborts the whole
  root transaction: no partial world effects, subscriptions, or ledger records remain committed.
- Process events by monotonically increasing chain sequence and matching subscriptions by ascending
  explicit order then stable lexical ID. Derive each event-mechanic seed from the root seed, event
  sequence/proposal ordinal, subscription ID, mode, and execution ordinal;
  record it with the execution so replay is independent of timestamps and database-generated IDs.
- A proposed root batch is guarded completely before any accepted event is routed to reactions.
  This prevents a reaction to effect 0 from running before a guard vetoes effect 1. Child effect
  batches and explicit derived events follow the same guard-before-acceptance rule.
- Guard evaluation is read-only. A guard returning effects, events, notifications, an unknown
  decision, or no decision fails with a stable contract error. Guards do not mutate, suppress,
  replace, or patch proposals: their only authority is allow or deny.
- Guard each root action with defaults of maximum depth **8**, maximum emitted events **100**, and
  maximum executions of one subscription **1**. A subscription may explicitly raise its own
  per-chain limit up to **8**. Exceeding any limit aborts the root transaction with a named,
  auditable error.

## Conditions, random occurrences, and tracked items

Conditions have two layers with one owner each:

1. The host applies cheap declarative filters before execution: exact event type, optional
   intersection with `trackedEntityIds`, and optional AND-combined equality checks over top-level
   scalar payload fields. Missing fields do not match. No JSONPath, expressions, or executable
   filter language belongs in the kernel.
2. The event mechanic owns every richer condition. A `reaction` mechanic reads the frozen accepted
   event and returns no effects/events/notifications when its condition is false. A `guard`
   mechanic reads the frozen proposed event and returns `allow` when its blocking condition is
   false or `deny` when true. Guard chance uses only its derived seeded random source.

Random occurrences are supported when an event provides the opportunity: for example,
`ruleset.clock.day.advanced` can trigger a mechanic that uses `ctx.randomInt(1, 6)` and emits a
registered encounter event on a 1. The derived seed is deterministic and recorded. This design
supports event → random decision → event → notification chains without `Math.random()`.

Tracked-item notifications use the same `trackedEntityIds` index for matching and store their own
entity links for querying. A notification can therefore answer “show unread notices involving
this quest, location, or item” without scanning payload JSON. Reading or archiving a notification
does not emit another event in the initial release, preventing acknowledgement loops.

## Ownership and persistence decisions

- Event type and subscription identities are stable; authored content is append-only versioned.
- Event, execution, guard-denial evidence, and notification rows are immutable evidence except
  notification read/archive state. Proposed and accepted event payloads and output data are
  canonical JSON.
- Event-to-entity and notification-to-entity join rows provide indexed tracked-item queries.
- The event ledger records type version, sequence, depth, correlation, causation, root operation,
  producer mechanic/version where applicable, and affected entity IDs.
- Subscription versions own mode, order, filters, mechanic target, limit, status, and change note.
  Executions record the exact subscription and mechanic versions actually used. A denied root's
  failure audit records the proposal plus denying guard/subscription/mechanic versions, derived
  seed, stable denial code, and reason outside the rolled-back transaction.
- World facts remain entities/components. Events describe committed changes; notifications report
  them. Neither duplicates authoritative game state.
- Developer-authored event types and subscriptions require file-first catalog formats plus
  import/export/manifest support. MCP-authored records must export back to those formats.

## MCP surface and contracts

Keep the three MCP tools. Add closed semantic kinds only after following
`procedure.mcp.add-tool`:

- `query(kind: "event-types")`, `query(kind: "events")`, `query(kind: "subscriptions")`, and
  `query(kind: "notifications")` for discovery, chain inspection, tracked-item history, and inbox
  reads.
- `commit(kind: "event-type")` to define or revise a registered event type.
- `commit(kind: "subscription")` to create, revise, enable, disable, or archive a subscription.
- `commit(kind: "notification")` only marks one notification read/unread or archived; mechanics
  create notifications transactionally through their structured result, not this administrative
  kind.

Create each governing contract in the same slice as the capability it governs:

- `procedure.event.define`
- `procedure.subscription.create`
- `procedure.subscription.modify`
- `procedure.event.inspect`
- `procedure.event.guard`
- `procedure.event.chain-limits`
- `procedure.notification.inspect`

The contracts must state source, scope, phase/mode, payload schema, required mechanic projection,
ordering, allow/deny semantics, failure behavior, test cases, and the exact recovery call for
every rejected write.

## Exact authored-record contracts

### Event type

- ID: permanent lowercase dotted path, 3–200 characters, with segments matching
  `[a-z][a-z0-9-]*`; `world.*` is reserved for kernel structural types.
- Identity row: `Id`, `Category`, `Scope`, `Status`, `CurrentVersion`, `CreatedAt`, `UpdatedAt`.
- Append-only version: `EventTypeId`, `Version`, `Name`, `Description`, `PayloadSchema`,
  `ChangeNote`, `CreatedBy`, `SourceHash`, `CreatedAt`.
- Status vocabulary: `draft`, `active`, `deprecated`, `archived`. Only active types can receive new
  events or subscriptions. Existing ledger rows retain the exact type version they used.
- `PayloadSchema` is a JSON Schema Draft 2020-12 object. Add `JsonSchema.Net` to
  `DantesRoleplay.DataAccess` (not the package-free core project), pin its selected version, compile the
  schema on write, and validate every emitted payload. Invalid schemas and invalid payloads fail
  with stable codes and JSON-pointer locations. Require an object root and explicit
  `additionalProperties`; the nine built-in structural schemas use `additionalProperties:false`.
- Catalog path: `catalog/event-types/<id>.json`; the schema is the exact-byte sibling
  `<id>.schema.json`, following component-schema precedent. Manifest kind: `eventType`.
- `commit(kind: "event-type")` supports dry run. Revising appends a version; changing category,
  scope, status, name, description, or schema requires a nonempty change note.

### Subscription

- ID: permanent lowercase dotted path under `subscription.*`.
- Identity row: `Id`, `Category`, `Scope`, `Status`, `CurrentVersion`, `CreatedAt`, `UpdatedAt`.
- Append-only version: `SubscriptionId`, `Version`, `EventTypeId`, `EventMechanicId`,
  `FixedRoleEntityIdsJson`, `TrackedEntityIdsJson`, `PayloadEqualsJson`,
  `Mode`, `Order`, `MaxExecutionsPerChain`, `ChangeNote`, `CreatedBy`, `SourceHash`, `CreatedAt`.
- Status vocabulary: `draft`, `active`, `disabled`, `archived`. Only active subscriptions route.
- Mode vocabulary: `guard`, `reaction`. It is required and immutable after version 1 because
  changing a veto into a reaction under one permanent ID destroys audit meaning; create a new ID.
- `order`: integer -1000–1000, default 0. Matching subscriptions run by ascending order, then
  ordinal ID. Equal order is valid. The stored order is part of the content hash and exact version
  evidence; no database row/insertion order may affect execution.
- `trackedEntityIds`: absent or 1–100 existing permanent entity IDs, trimmed, distinct, sorted.
  A match requires at least one intersection with the event's indexed entity IDs.
- `payloadEquals`: absent or an object of at most 32 top-level keys whose values are JSON scalar
  string/number/boolean/null. All entries must match by JSON value and type; missing keys fail.
- `fixedRoleEntityIds`: absent or an object mapping every required ordinary mechanic role to one
  existing entity ID; optional roles may be omitted and extra role names fail. Dynamic affected
  entities are available through `ctx.eventEntities`, not role-name inference.
- `maxExecutionsPerChain`: integer 1–8, default 1. It can narrow or explicitly raise the repeated
  subscriber limit, but cannot change global depth/event limits.
- The target mechanic must be active and declare an event requirement containing the subscribed
  event type with the same mode. At runtime an unavailable target aborts the chain; it is never
  silently skipped.
- Catalog path: `catalog/subscriptions/<id>.json`. Manifest kind: `subscription`.
- `commit(kind: "subscription")` supports dry run. Every content/status change appends a version.

### Reactive mechanic declaration and output

Extend `MechanicRequirements` with optional `event`:

```json
{
  "event": {
    "mode": "reaction",
    "types": ["world.component.replaced"],
    "components": ["dnd2024.hit-points"],
    "includeContents": false
  }
}
```

`mode` is exactly `guard` or `reaction`. `types` is a nonempty sorted distinct list of active
registered event IDs. `components` is a sorted
distinct list of existing component definitions projected for every live affected entity.
Reactive mechanics may also declare ordinary fixed roles, but a subscription must provide exact
fixed role bindings for them; event entity IDs are never guessed into role names. Child mechanic
composition is excluded from the initial event-mechanic release and must be rejected in an event
target declaration.

The sandbox receives immutable `ctx.event` and `ctx.eventEntities` in addition to existing fields.
For a reaction, `ctx.event` contains ID, type ID/version, mode=`reaction`, scope, canonical payload,
entity IDs, correlation, causation, depth, and sequence. For a guard, it contains no event ID or
sequence because none exists yet; it contains mode=`guard`, type ID/version, scope, canonical
payload, entity IDs, correlation, causation, depth, and `proposalOrdinal`. Scripts must branch on
their declared mode, never null-test their way into supporting both. `ctx.eventEntities` is keyed
by affected entity ID and contains only declared components plus normal containment metadata.
Deleted entities are absent there; the event payload/receipt carries their frozen before state.

Reaction mechanics use the existing output plus bounded proposal lists in their owning slices
(`events` in Slice 5, `notifications` in Slice 6):

```json
{
  "effects": [],
  "events": [{"type":"ruleset.example.follow-up","payload":{},"entityIds":["..."]}],
  "notifications": [{"topic":"...","subject":"...","body":"...","entityIds":["..."]}],
  "data": {},
  "narration": "..."
}
```

Only reaction executions may return `events` or `notifications` initially. Root action mechanics
that return either are rejected with `REACTIVE_OUTPUT_NOT_ALLOWED`. Proposal arrays have a host
limit of 100 each; IDs must be existing permanent entities, event types active, payloads valid,
and notification strings trimmed with topic ≤100, subject ≤200, and body ≤4000 characters.

Guard mechanics have a separate closed output contract:

```json
{
  "decision": "deny",
  "code": "TARGET_WARDED",
  "reason": "The target is protected from this change.",
  "data": {},
  "narration": "The ward rejects the effect."
}
```

`decision` is required and exactly `allow` or `deny`. `deny` requires a code matching
`[A-Z][A-Z0-9_]{2,63}` and a trimmed reason of 1–500 characters. `allow` may include explanatory
data/narration but must not include a denial code. Any `effects`, `events`, or `notifications`
member is invalid even when empty, so the host never accidentally grants guards side effects.
The root error code is `EVENT_BLOCKED`; structured error/audit detail carries the guard's code and
reason plus the exact proposal, subscription/version, mechanic/version, seed, and proposal ordinal.

## Exact structural event mapping

The event producer consumes immutable effect receipts, not raw effects. Refactor the applier to
return one receipt per successfully applied effect with effect index/type, canonical before and
after JSON where applicable, and ordered affected IDs. The root effect batch is fully validated
and applied inside the transaction first; its receipts become proposed event envelopes. Guards
observe the final uncommitted result of the whole batch, never a half-applied intermediate state.
Only after every root proposal is allowed are accepted ledger events inserted and reactions
eligible to run. A denial rolls the applied batch back, so “applied inside the transaction” never
means visible or committed.

| Effect | Registered event type | Required payload |
| --- | --- | --- |
| `entity.create` | `world.entity.created` | effect index, entity ID, after entity snapshot |
| `entity.delete` | `world.entity.deleted` | effect index, entity ID, before entity snapshot |
| `component.add` | `world.component.added` | effect index, entity/definition IDs, `before:null`, canonical after data |
| `component.set` | `world.component.replaced` | effect index, entity/definition IDs, canonical before and after data |
| `component.merge` | `world.component.merged` | effect index, entity/definition IDs, canonical before, patch, and after data |
| `component.remove` | `world.component.removed` | effect index, entity/definition IDs, canonical before, `after:null` |
| `containment.move` | `world.containment.moved` | effect index, entity ID, before/after container ID and slot |
| `relationship.create` | `world.relationship.created` | effect index, from/to IDs, kind, canonical after data |
| `relationship.remove` | `world.relationship.removed` | effect index, from/to IDs, kind, canonical before data |

The nine schemas and event-type files land in Slice 1. Catalog import/export, seeding, migrations,
and administrative store writes do not emit gameplay events. Only successful
`commit(kind: "effects")` and `commit(kind: "action")` mutations enter the event pipeline.

## Transaction and queue algorithm

1. Allocate the root operation ID and root seed before opening the transaction. The correlation ID
   equals the root operation ID in the initial release. An action uses its existing optional seed;
   add optional `seed` to the `effects` commit payload and generate a cryptographic seed when either
   caller omits one. Record the resolved seed on the root operation. Allow
   `OperationLog.RecordAsync` to accept that ID; keep generation centralized and collision-tested.
2. Open one transaction in the action or direct-world-change runner. Validate the complete root
   effect batch, apply it, capture receipts, and materialise immutable proposed event envelopes in
   effect-index order. A proposed envelope has type/version, canonical payload, scope, ordered
   entity IDs, depth, proposed ordinal, correlation/root operation, and nullable causation, but no
   event ID, sequence, or timestamp yet.
3. Freeze the active matching `guard` registrations and their exact versions. For every proposal
   in ordinal order, apply type/scope/entity/payload filters, then execute matching guards by
   ascending order and ordinal ID. Derive each guard seed from SHA-256 over an unambiguous binary
   encoding of root seed, proposal ordinal, subscription ID, mode, and execution ordinal.
4. If any guard denies, stop evaluating immediately, roll back, clear tracking, and record one
   failure operation outside the transaction with `EVENT_BLOCKED` and the complete denial evidence.
   No event, reaction execution, notification, success audit, or world delta remains. Invalid guard
   output or unavailable guard dependency follows the same rollback path with its own stable code.
5. After every proposal in the batch is allowed, insert accepted structural events in proposal
   order with one correlation ID, increasing sequence, depth 0, null causation, root scope, and the
   preallocated root operation ID. Direct effects use empty/shared scope. Derived events inherit
   their root scope.
6. Dequeue the lowest accepted-event sequence. Select active matching `reaction` subscriptions by
   ascending order then ordinal ID. Apply type, scope, tracked-entity, and payload-equality filters
   before projection/execution.
7. Derive each reaction seed from SHA-256 over an unambiguous binary encoding of root seed, event
   sequence, subscription ID, mode, and execution ordinal; take the first 64 bits as signed
   little-endian. Record all derivation inputs and the result.
8. Resolve and freeze `ctx.event`, `ctx.eventEntities`, and any declared fixed roles. Run the exact
   reaction mechanic version, validate its whole output, apply its complete effect batch, and
   capture receipts. Materialise receipt proposals first in effect order, then explicit derived
   event proposals in output order.
9. Guard the whole child proposal batch before accepting any child. Each child has the current
   accepted event as causation and depth +1. If allowed, assign the next sequences and queue them;
   proposed entity ID lists must be distinct and retain declared order in ordinal join rows.
10. Validate and insert notifications from the same output in declared order. They do not create
   events in the initial release.
11. Continue until the queue is empty. Enforce depth 8, accepted+proposed event count 100, total
   guard+reaction executions 100, and each subscription's per-chain limit before execution. Counting
   proposals prevents a veto-heavy chain from bypassing limits.
12. Insert the successful root operation inside the same transaction and commit. Extend tool
    plumbing so a handler that already recorded its operation returns that ID without a duplicate
    audit row. On any failure, roll back, clear tracking, then record one failure operation outside
    the transaction using the same ID; no world/event/execution/notification success row remains.

## Slice order and stop gates

| Slice | End-to-end capability | Exit gate |
| --- | --- | --- |
| 1 | Registered event types: contract, model/version, schema validation, migration, store, catalog, query/commit kinds, structural type fixtures | Event types can be authored file-first or through MCP, validated, versioned, imported/exported, and discovered. Stop. |
| 2 | Registered event middleware: subscription contracts/model/version with `guard` or `reaction` mode, explicit order, filters, mechanic event requirements, migration, store, catalog, query/commit kinds | Guard and reaction registrations can be authored and validated but cannot execute yet; mode/output/reference/filter errors fail before writing. Stop. |
| 3 | Transactional pre-commit guards: effect receipts/proposals, frozen guard projections, deterministic guard execution, allow/deny contract, denial evidence, direct-effects/action integration | Registered guards deterministically allow or veto root changes; denial leaves no world/event/success state and returns exact middleware evidence. No event ledger or reactions yet. Stop. |
| 4 | Transactional structural event ledger: inspect contract, accepted-event/entity links, atomic audit, query kind, guard-to-event evidence linkage | Guard-approved structural effects produce exact immutable events; failures leave world, ledger, and success audit absent. Reactions still do not execute. Stop. |
| 5 | Reactive event chains: executions, deterministic reaction seeds, filters, derived-event guard stage, effects, ordering, limits, rollback, replay | Multi-hop guarded conditional/random chains replay identically across fresh databases and all denial/failure/limit paths roll back. Stop. |
| 6 | Transactional tracked-item notifications: contract, output validation, store/entity links, query/commit kinds, full protocol/catalog acceptance | Event chains create queryable notices atomically; unread/read/archive and tracked-item queries work without world mutation. Stop. |

Slice 2 is the only authorized implementation slice. Each slice is deliberately large enough to
land a usable vertical kernel capability with its own contract, persistence, file-first catalog
path, semantic surface, and acceptance tests. No slice leaves a model that cannot be authored,
read back, or verified.

Every slice must re-read `procedure.system.modify`, `procedure.mcp.add-tool` when it adds kinds,
and all event contracts already completed. A newly discovered dependency causes plan revision and
a stop; it is not mocked or bundled.

## Slice 1 — event-type registry end to end

Status: **implemented and verified**. Dependency: current versioned-store, catalog, migration,
three-verb, and operation-audit infrastructure. Evidence: 10 catalog records created, 84 catalog
records unchanged after import, and the full repository suite passed 304/304.

Runtime artifacts:

- `DantesRoleplay.Events`: event-type status, identity/version records, write/check/query requests,
  summaries/details, `IEventTypeStore`, and stable validation codes.
- `DantesRoleplay.DataAccess`: EF mapping, forward-only `EventTypes` migration, schema validator,
  append-only store, registration, hashes, and database initialisation coverage.
- Catalog: event-type file/parser/layout/reader/writer/import/export/manifest support and the nine
  active `world.*` structural event-type/schema files.
- MCP: `query(kind: "event-types")`, `commit(kind: "event-type")`, VerbSurface capability entry,
  thin handler, dry run, standard fixes, and dispatch guard updates.
- Contract: `catalog/procedures/event/procedure.event.define.md`.

Important behavior:

- A write validates ID reservation, closed status, schema syntax, scope/category/name lengths,
  duplicate/near-duplicate identity, and required change note before opening a write transaction.
- Dry run performs the same checks and hash computation but writes no version, operation, or
  manifest state. A real revision appends exactly one version and changes the identity pointer.
- Built-in `world.*` files are ordinary catalog records but their IDs are reserved from MCP calls;
  only file-first kernel development can revise them.
- `query` supports id/version, query, category, scope, includeInactive, and limit. Full reads return
  exact schema text and version metadata; lists omit schema bodies.

Acceptance matrix:

- Happy/versioning: create a custom draft type, activate it with a change note, read both versions,
  and prove hashes differ only when authored content differs.
- Closed input: reject uppercase/bad IDs, reserved `world.*`, missing name/schema, unknown status,
  invalid JSON, invalid Draft 2020-12 schema, extra payload fields, and revision without note.
- Catalog: fresh import, unchanged re-import, edit classification, conflict refusal, export, exact
  schema-byte round trip, manifest hash, and rules-only catalog behavior.
- Migration: pending-model check, empty migration, and upgrade from the previous latest migration.
- Protocol: capability listing, dispatch symmetry, dry-run recovery call, readback, and no `orient`
  claim that event routing exists.
- Repository: focused tests, full suite, catalog verify, and `git diff --check`.

Exit gate: event types are safely authorable and discoverable through files and MCP, all nine
structural types exist, and no event row, subscription, routing, or notification capability exists.
Record counts/test totals in this plan and stop.

## Slice 2 — event middleware registry end to end

Status: **implemented and verified**. Evidence: migration `20260819170537_Subscriptions`, catalog
import created two contracts and revised the entry contract with no conflicts, `verify` reported 86
unchanged records, and the full suite passed 308/308. Slice 1 review/evidence remains complete.

Runtime artifacts:

- `DantesRoleplay.Events`: subscription status/mode, identity/version records, deterministic order,
  filter contracts, check/write/query requests and `ISubscriptionStore`.
- Extend `MechanicRequirements` with the exact event declaration above and update mechanic write
  checks, detail/readback, content hashing, and catalog round trips.
- `DantesRoleplay.DataAccess`: subscription/version tables and indexes for event type, status,
  scope, and mechanic; append-only store and validation. Canonical JSON fields are stored compact.
- Catalog: `catalog/subscriptions/<id>.json`, manifest/import/export/conflict support.
- MCP: `query(kind: "subscriptions")`, `commit(kind: "subscription")`, capabilities, handlers,
  dry run, and dispatch guards.
- Contracts: `procedure.subscription.create` and `procedure.subscription.modify` as file-first
  catalog procedures.

Important behavior:

- Validate referenced event type active, target mechanic active, event declaration includes that
  type and the same mode, event component IDs exist, no event-mechanic children, fixed roles exactly
  satisfy ordinary role requirements, tracked entities exist, filters are closed/canonical, and
  order/limits are in range.
- Mode is required on new subscriptions. `guard` targets must satisfy the closed guard-output
  declaration and `reaction` targets the reaction-output declaration. A version cannot change mode;
  dry run returns `MODE_IMMUTABLE` with a create-new-ID recovery call.
- A later event type/mechanic deactivation does not mutate subscriptions. Query reports dependency
  health; future execution will fail loudly rather than silently skip an active broken declaration.
- Scope matching is exact subscription scope against root action scope, with empty meaning shared.
  It is separate from tracked-entity matching.
- Registration query/detail returns dependency health, mode, order, and the exact versioned target;
  list ordering is category then ID, while runtime execution order is order then ID.

Acceptance matrix:

- Happy/versioning: create disabled guards and reactions, enable active, revise filters/order/limit,
  reject mode revision, read every version, and
  prove sorted canonical IDs/keys and stable hashes.
- Invalid references: missing/inactive type or mechanic, missing event declaration, type mismatch,
  undeclared/extra/missing fixed role, missing component/entity, children, bad status/limit.
- Filters: absent versus empty, scalar type sensitivity, missing payload key, AND behavior,
  tracked-entity intersection/nonintersection, duplicate/unsorted input canonicalisation.
- Ordering: -1000/0/1000 boundaries, equal-order lexical tie break, out-of-range rejection, and
  content-hash sensitivity to order.
- Existing mechanics without `event` continue to import, hash, route, and execute byte-identically.
- Catalog, migration, protocol, dry-run, readback, conflict, full-suite, verify, and diff gates match
  Slice 1 quality.

Exit gate: guard/reaction subscriptions are authorable, inspectable declarations and event-mechanic
requirements are enforceable, but no middleware executes and no event ledger exists. Stop.

## Slice 3 — transactional pre-commit guard middleware

Status: **implemented and verified.** Evidence: migration `20260819172055_GuardEvidence`, direct
deny rollback and rollback-only dry-run coverage, structured root-audit evidence, catalog verify
with 87 unchanged records, and the full suite passed 311/311. Do not begin Slice 4 without review
and explicit authorization.

Runtime artifacts:

- `EffectReceipt` and immutable `ProposedEvent` contracts for all nine structural effects. Extend
  the owning applier API rather than adding a parallel effect interpreter. Proposed envelopes are
  schema-validated but are not ledger rows and have no event ID/timestamp/sequence.
- One transaction-owning world-change runner used by `commit(kind: "effects")`; `ActionRunner`
  keeps its current ownership and invokes the same proposal/guard service in its ambient
  transaction. The complete effect batch applies before guards, but remains uncommitted.
- Frozen `ctx.event`/`ctx.eventEntities` projection for guard mechanics, Jint input/output support,
  deterministic seed derivation, timeout/cancellation, and a `GuardRouter` that executes only
  matching active `guard` registrations by order then ID.
- Structured denial evidence in the failed root operation audit: proposed type/version/payload/
  scope/entity IDs/ordinal, subscription/version/order, mechanic/version, derived seed, decision
  code/reason, correlation/root operation ID. Allowed evaluations remain transaction-local until
  Slice 4 gives accepted events a durable execution link.
- Contract `procedure.event.guard`, file-first and imported with the slice. Existing event-type and
  subscription contracts are revised only if their live wording conflicts with guard semantics.

Important behavior:

- No matching guard means allow. Every matching guard must explicitly return `allow`; the first
  deterministic `deny` vetoes the root and short-circuits later guards. Short-circuit position is
  evidence, not an unspecified optimisation.
- Guards observe final uncommitted state for the whole effect batch plus one immutable proposal.
  They cannot see half-applied batch state, mutate context, call child mechanics, or return
  effects/events/notifications. They cannot rewrite an effect or “deny only the event while keeping
  the world change”; event acceptance and its world change are one atomic decision.
- All root proposals are guarded before any is accepted/routed. The same service can later guard
  child receipt proposals and explicit derived-event proposals without a second semantics path.
- `commit(kind: "effects", dryRun:true)` validates effects and evaluates guards in a rollback-only
  transaction, returning proposed events and guard decisions without world/event/execution rows.
  The existing operation audit still records that a dry run occurred. The real commit must rerun
  guards because state may have changed. `action` gains no dry-run claim in this slice.
- Denial returns top-level `EVENT_BLOCKED`; invalid/missing decision, forbidden output, unavailable
  target, schema/projection failure, timeout, and cancellation use distinct stable codes. Every
  path rolls back and records exactly one failed root audit outside the transaction.
- Guards initially cover proposed registered structural events and, once Slice 5 exists, explicit
  derived events. They do not guard procedure/event-type/subscription/notification administration,
  catalog import, migrations, or action intent before its mechanic proposes effects. Action-level
  lifecycle guards require separately registered action-attempt types and are deferred.

Acceptance matrix:

- Allow: no guards and one/many allowing guards commit exactly the original effects; evaluation
  order is explicit order then lexical ID and changing insertion order changes nothing.
- Deny: entity/component/containment/relationship examples each return exact guard code/reason and
  leave byte-equivalent world state, zero event rows, zero success audit, and one failure audit.
- Whole batch: guard on effect 1 denies after effects 0–N were applied in-transaction; rollback
  restores every row/revision and no guard/reaction for effect 0 escapes early.
- Conditions/random: tracked/payload/scope filters avoid execution; rich false condition allows;
  seeded chance proves allow and deny branches and replays identically in two fresh databases.
- Output contract: missing/unknown decision, deny without code/reason, allow with code, and any
  effects/events/notifications key fail closed and preserve state.
- Dependency/failure: inactive type/subscription/mechanic, mode mismatch, corrupt filter, projection
  failure, throw, timeout, cancellation, and failure-audit error have named expected outcomes.
- Dry run proves proposal/decision readback and zero domain-state mutation (apart from its normal
  operation audit); real commit re-evaluates.
  Migration/protocol/full-suite/catalog/diff gates pass and existing no-guard actions preserve
  exact outputs apart from additive proposal/decision summaries.

Exit gate: registered middleware can deterministically veto real direct effects and actions with
complete rollback and actionable audit evidence. No accepted event ledger or reaction execution
exists. Stop.

## Slice 4 — transactional structural event ledger

Status: **VERIFIED 2026-08-19 — build clean, 322/322.** Authorized and completed in one pass. Part
of it was already present when that pass started; what follows records what was finished and what
was found wrong.

### Found while closing the gate: a fresh install could not change the world

The nine `world.*` event types existed **only as catalog files**, and `InitialiseDantesRoleplayAsync`
seeds contracts and rules and nothing else. So on any database where nobody had happened to run
`roleplay import catalog`, every `commit(kind: "effects")` failed — the ledger has no registered type
to record the change against. Nothing declared the dependency and no test covered it; the protocol
walk found it only once the ledger started naming the missing type instead of failing obscurely
after the commit.

They are kernel contracts, not content — `EventTypeTools` already refuses to let an LLM write one —
so they now ship as embedded resources under `DantesRoleplay/EventTypes/` and seed like the
bootstrap contracts, **before** the rules. `EventTypeSeeder` reuses the catalog's own `EventTypeFile`
parser rather than adding a second reader of the format. The catalog copy is untouched and still
round-trips.

### Also corrected: two Slice 3 defects that blocked Slice 5

- **`ctx.event` was the bare payload, not the envelope this plan specifies.** It is now built in one
  place (`EventEnvelope`) carrying mode, type id/version, scope, payload as JSON, entity ids,
  correlation, causation, depth, and `proposalOrdinal` for guards. A guard's envelope deliberately
  omits `id` and `sequence` rather than nulling them, so a guard reading `ctx.event.id` fails while
  it is being written. This required `ProposedEvent` to carry correlation, depth and causation —
  which §"Transaction and queue algorithm" step 2 already specified, and which Slice 5's children
  need. The ledger now records depth and causation from the proposal instead of assuming the root.
- **The guard output check contradicted the contract.** It rejected any narration or data as
  `GUARD_FORBIDDEN_OUTPUT`, while this plan says `allow` may include explanatory data and narration.
  Effects are still forbidden outright; denials now also validate the code against
  `[A-Z][A-Z0-9_]{2,63}` and cap the reason at 500 characters.

**Migration note for later slices:** migrations are generated by tooling that is not available to the
authoring session. Adding an entity means writing the model and the `DbContext` mapping, then
running `dotnet ef migrations add <Name> --project DantesRoleplay.DataAccess` locally.
`MigrationDriftTests` fails until that is done, which is the intended signal.

### What was found wrong

- **Every payload violated its own registered schema.** The producer built payloads by serialising
  the effect object, giving PascalCase keys plus five extra properties — so for a
  `component.set`, `world.component.replaced` was handed a payload missing all three of its
  required properties and carrying eight forbidden ones. Nothing validates payloads at write time,
  so nothing said so. The live ledger happened to be empty, so this was latent rather than already
  written: it would have violated on the first event ever recorded.
- **`RootOperationId` was never set.** `AttachRootOperationAsync` existed and had no callers, so
  every row would have carried `""` and the operation linkage was absent.
- **The unguarded path committed before writing its events**, so a failure between the two would
  have left a committed world change with no record of it.
- **Three near-duplicate write paths**, each minting its own correlation id.
- `query(kind: "events")` and `procedure.event.inspect` did not exist, so the exit gate's word
  *queryable* was unmet.
- **`orient()` still told every session "There are no events, no subscriptions"** — false since
  Slice 1, and `procedure.system.use` instructs sessions to believe orient over anything else.

### What was done

- Payloads are now built per event type against the shipped schemas. Verified by reimplementing the
  producer in Python and checking all nine effect types against `catalog/event-types/*.schema.json`:
  **9 of 9 conform**, including `containment.move` to nowhere, which relies on that schema allowing
  a null `toEntityId`.
- **The correlation id is the root operation id**, per this plan's step 1. `Operation.NewId()`
  centralises minting, `IOperationLog.RecordAsync` accepts a pre-allocated id, and
  `IEffectApplier.ApplyAsync` takes `rootOperationId`. `AttachRootOperationAsync` is deleted — the
  link exists the moment the row does.
- The three write paths are one. Events are written **before** the commit, so an event and the
  change it describes are one atomic fact.
- `ApplyOneAsync` returns the id it actually touched, so `entity.create` with no id records the id
  the store minted rather than an empty string. This is the first receipt in the sense §"Exact
  structural event mapping" means; the full before/after receipt pipeline is NOT built (see below).
- `query(kind: "events")` with all nine filters, total and stable ordering, exclusive
  `afterSequence` paging, and a clamped limit. Listings omit payloads; `id` returns one in full.
- `procedure.event.inspect`, and `procedure.system.use` and `orient()` corrected.
- `EventLedgerTests`: 10 tests, including schema conformance for all nine types read from the
  catalog rather than restated.

### Deliberately NOT done — the gap Slice 4 still carries

This plan's mapping table says every payload should carry an effect index plus `before` and `after`
snapshots. **The schemas Slice 1 shipped have none of that** — `world.component.replaced` declares
only `entityId`, `definitionId`, `data`. So the ledger records what a change set, not what it
changed *from*. Closing that is a v2 of all nine event types plus the receipt pipeline, and it is
its own slice; the payloads here conform to the schemas that actually exist. Decide which of the
two is wrong before Slice 5 depends on it.

Runtime artifacts:

- Event identity and `EventEntity` join models/tables with indexes on correlation+sequence,
  causation, root operation, type+timestamp, and entity+timestamp. Event rows are append-only.
  `RootOperationId` is indexed but not an immediate foreign key because the success operation is
  inserted after the chain; the shared transaction and an orphan-integrity test enforce linkage.
- Reuse Slice 3's `EffectReceipt`/`ProposedEvent` pipeline for all nine effects; do not re-read raw
  effects or create a second producer.
- One transaction-owning world-change runner used by `commit(kind: "effects")`; ActionRunner keeps
  its existing ownership but calls the same receipt/event producer inside its ambient transaction.
- Caller-supplied operation IDs and tool plumbing for exactly one atomic success audit row.
- `query(kind: "events")` and `procedure.event.inspect`. Filters: id, correlationId, causationId,
  rootOperationId, type, entityId, afterSequence, from/to UTC, and bounded limit.

Important behavior:

- Event data fields: ID, event type ID/version, scope, canonical payload, UTC timestamp,
  correlation ID, nullable causation ID, depth, sequence, root operation ID, producer
  mechanic/version when applicable, producer subscription/version for derived events, and ordered
  entity links. IDs/timestamps are evidence but excluded from replay equality.
- Direct dry-run effects produce no receipt/event/audit mutation. Catalog import, administrative
  writes, migrations, and direct store tests do not emit events.
- Empty effect actions remain valid and produce no structural events. No action lifecycle event is
  inferred.
- Only guard-approved proposals become event rows. Allowed guard evidence links to the accepted
  event/root operation; denied proposals remain represented only by the failed operation audit.

Acceptance matrix:

- One focused case per effect type asserts exact event type, schema-valid payload, before/after,
  affected IDs, type version, root mechanic version, depth 0, sequence, and operation linkage.
- A multi-effect batch proves events follow effect index, while subscriber-observable projection
  would see the final whole-batch state.
- Invalid root effect, exception during apply, cancellation, and audit failure each leave no world
  delta, event row, or success audit; the failure audit uses the allocated root ID.
- Existing action output/effect counts stay unchanged; event summaries are additive fields only.
- Event queries prove every filter, stable ordering/pagination, entity-index use, and no payload scan
  for tracked entity lookup.
- Migration upgrade, protocol, full suite, catalog verify, and diff checks pass.

Exit gate: guard-approved committed action/direct effects atomically create a queryable structural
ledger, with no reaction executions yet. Stop.

## Slice 5 — deterministic reactive chains

Status: **in progress.** Slice 4 was reviewed and closed; this slice is being taken in three passes,
recorded under "Progress" below. Its exit gate is NOT met — derived events (5c) are outstanding.

Runtime artifacts:

- `EventExecution` model/table recording event, subscription/version, mechanic/version, derived
  seed, ordinal, frozen projection JSON, output JSON, effects/events counts, narration/log, and
  elapsed/limit evidence. Only committed successful executions remain in this table.
- Event router/queue service implementing the exact algorithm and limits above, reusing Slice 3's
  guard service for every child receipt/derived proposal before acceptance.
- Projection resolver path for `ctx.event`/`ctx.eventEntities`; Jint payload/harness support;
  `MechanicOutput.Events` and registered-derived-event validation.
- Action/direct-world runners invoke the router after structural events and include event/execution
  summaries in their result envelopes without changing existing result fields.
- Contracts `procedure.event.chain-limits` and any required `procedure.event.guard` revision as
  file-first catalog procedures.

Important behavior:

- An active reaction subscription whose dependency became unavailable aborts with
  `SUBSCRIBER_UNAVAILABLE`. Mechanic error, timeout, invalid effect/event, schema failure, projection
  failure, guard denial, or guard contract failure also aborts the root transaction and names the
  event/proposal, subscription, and mechanic.
- A condition that returns empty effects/events is still a successful execution record: this is
  evidence that the condition was evaluated. It creates no child event.
- Derived events are validated against the exact active type version at emission and store that
  version. Causation always names the event being handled; correlation/root operation stay fixed.
- Same-ID subscription repeats use the version fixed on first selection for the chain, so a store
  revision cannot change semantics mid-run (normally impossible inside one transaction, explicitly
  guaranteed anyway).

Acceptance matrix:

- A→B→C chain proves causation, depth, sequence, ascending-order/lexical-ID subscriber order, type/subscription/
  mechanic versions, exact effects, and final world state.
- Conditions: declarative type/scope/entity/payload exclusions produce no execution; rich false
  condition produces one empty execution; true condition produces exact outputs.
- Random: a reactive mechanic uses `ctx.randomInt`; chosen seeds prove both branches, replay in two
  fresh databases matches parsed output/event order/final state, and unrelated subscriber order
  changes do not alter a given derivation input.
- Limits: depth 9, event 101, execution 101, and per-subscription overflow each return distinct
  stable codes and roll back all world/event/execution rows.
- Child veto: a guard denies a receipt event and an explicit derived event in separate tests; each
  rolls back the complete root including parent reaction effects/events/executions.
- Failure matrix covers inactive dependency, throw, timeout, bad event type/payload/entity,
  invalid child effect, cancellation, and root audit failure with exact no-state comparisons.
- No-subscription actions preserve behavior and stay within a recorded proportional performance
  threshold rather than an absolute machine-dependent duration.

Exit gate: bounded conditional, random, effect-producing, and event-producing chains are atomic,
auditable, and reproducible. Notifications are still rejected as unavailable. Stop.

### Progress

This slice is large enough that taking it whole would mean a single unreviewable change, so it is
being delivered in three passes. Each is a step towards ONE exit gate; none of them is a slice in
its own right, and the gate stays shut until 5c lands.

**5a — reaction dispatch.** `EventExecution` model, table and migration; `EventRouter` mirroring
`GuardRouter`'s selection, declarative filters and ordering; `ChainBudget` with the four bounds and
their four distinct codes; `EffectApplier.RunChainAsync` as the queue loop that applies a reaction's
effects through the same doorway as any other change. Two Slice 3 defects were fixed on the way in:
the unguarded commit path wrote its events after committing, and the guard router rejected narration
and data on an allow — which made a guard that explained itself a failing guard.

**5b — what a middleware sees.** `EventEnvelope` builds `ctx.event` once for both modes, differing
only where they genuinely differ: a guard gets `proposalOrdinal` and no `id` or `sequence`, because
it is being asked about something that may never exist. `ctx.eventEntities` became a keyed
projection — entity id to that entity, carrying only the components the mechanic declared — rather
than a bare list of ids. Nine `world.*` event types now seed from embedded files, because a fresh
install could not otherwise change the world at all.

Contracts landed with the code, as `procedure.system.create-feature` step 6 requires:

- `procedure.event.react` — new. Slice 5's central capability had no contract at all.
- `procedure.event.guard` — revised: the keyed projection, the full envelope, narration and data on
  an allow, the denial code shape, and that guards run on every proposal in a chain.
- `procedure.subscription.create` / `.modify` — the "does not execute yet" constraints are gone.
- `procedure.event.chain-limits` and `procedure.event.inspect` — authored as bootstrap files; they
  reach `catalog/` through `roleplay export catalog`.
- `VerbSurface` and `orient()` no longer say registrations do not execute. This is the second time
  that denial has had to be narrowed; a session is told to believe it over anything else it reads.

**5c — derived events.** Not started. `MechanicOutput.Events`, validation against the exact active
type version at emission, and a child guard veto rolling back the complete root. `EventExecution.EventCount`
is hard-coded to zero until it lands, and both the reaction contract and `orient()` say so.

### Evidence

322/322 was the last green run, taken before 5a. Everything since — 5a, 5b and the contract work —
is **written and unbuilt**: there is no .NET SDK reachable from the authoring environment, so the
compiler has not seen it. Nothing here is verified until `dotnet build` and `dotnet test` run clean
and `roleplay verify catalog` agrees. Do not read the paragraphs above as evidence; they are a
description of what to check.

Reimplementation-in-Python checks that did run, against a copy of the live database: all nine event
payloads conform to their registered schemas, and the content-hash backfill appends zero spurious
versions. Neither of those covers reaction dispatch, which has no equivalent shortcut — it needs the
suite.

### Still open before 5c

The `before`/`after` payload decision recorded as Slice 4's deliberate gap, and as KNOWN_ISSUES #5,
is still open. 5c adds a second producer of events, so it is the last comfortable moment to settle
whether the plan's mapping table or the shipped schemas is the thing that is wrong.

## Slice 6 — tracked-item notifications and final surface

Status: blocked on Slice 5 review.

Runtime artifacts:

- Notification and `NotificationEntity` tables. Immutable fields: ID, topic, subject, body,
  correlation/event/execution/root-operation IDs, ordinal, created UTC. Mutable delivery state:
  `unread`, `read`, or `archived`, plus nullable read/archived UTC timestamps.
- `MechanicOutput.Notifications`, Jint parsing/limits, transactional validation/insertion in the
  router, and notification summaries in action/direct-effect results.
- `query(kind: "notifications")` filters id, state, topic, entityId, correlationId, from/to, limit;
  `commit(kind: "notification")` accepts exactly id+state (`unread`, `read`, `archived`) and is
  idempotent for the current state. Neither call emits events.
- Contract `procedure.notification.inspect`; complete capabilities, orient, protocol walk, catalog
  coverage map, and README/status descriptions only after the code exists.

Important behavior:

- Notification content and links are created only by a successful reaction execution and commit
  with its entire root chain. Administrative state transitions cannot edit content or links.
- Marking unread clears `ReadAt`; read sets it once; archived sets `ArchivedAt` and retains prior
  `ReadAt`. Archived can return to unread/read only if the contract explicitly allows it; initial
  release rejects unarchive to keep lifecycle one-way.
- External delivery, push, webhook, email, polling jobs, and scheduler semantics remain absent and
  unadvertised.

Acceptance matrix:

- Conditional deterministic-random chain creates zero notices on one seed and one exact notice on
  another; two fresh databases match content, links, chain evidence, and world state excluding IDs/time.
- Multiple tracked IDs query through join indexes; unread/topic/correlation/time filters and stable
  ordering work; payload text is not used for entity filtering.
- Invalid output strings/entity IDs/count, duplicate malformed proposal, subscriber failure, or
  notification insert failure rolls back the complete chain.
- Read/unread/archive transitions, repeated idempotent calls, unknown ID/state, unarchive rejection,
  and no-event/no-world-state guarantees are asserted.
- Full protocol walk proves every advertised kind dispatches and every dispatcher is advertised;
  orient accurately describes events/subscriptions/notifications and still denies scheduling.
- Complete fresh/upgrade migration, catalog round trip/conflict, full suite, verify, diff, and
  regression-count evidence pass.

Exit gate: E1 is complete for event-driven chains, deterministic occurrences, and in-system
tracked-item notifications. Stop before any scheduler, external delivery, or D&D consumer feature.

## Reliability features and deliberate deferrals

The guard review exposed several event-system features that are easy to imply and expensive to
retrofit. The initial release makes the following decisions explicit:

- **Deterministic middleware order is included.** Every guard and reaction has versioned integer
  `order`; execution is ascending order then ordinal ID. There is no implicit insertion order.
- **Transactional once-only execution is included.** Add database uniqueness constraints for
  event `(CorrelationId, Sequence)`, accepted execution `(EventId, SubscriptionId,
  SubscriptionVersion, ExecutionOrdinal)`, and proposal guard evaluation `(RootOperationId,
  ProposalOrdinal, SubscriptionId, SubscriptionVersion, ExecutionOrdinal)`. A synchronous chain
  either commits each accepted event/execution once or commits none. Restart before commit rolls
  the transaction back; there is no pending in-memory queue to recover.
- **Decision and matching explainability is included at the decision boundary.** Subscription
  dry-run/detail reports dependency health, normalized filters, mode, and order. Executions expose
  the exact matched registration; failures expose the exact guard denial or reaction failure and
  filter inputs. Persisting every nonmatching registration for every event is deliberately omitted
  because it multiplies ledger size; historical “why did this inactive/changed subscription not
  match?” needs a separate routing-snapshot design. Sensitive free-form payload search is not
  introduced.
- **Event mutation is excluded.** Middleware cannot rewrite, replace, defer, or partially accept a
  proposed event/effect. Supporting transformations would require conflict resolution and a second
  validation pass; author the desired state transition in the root/reaction mechanic instead.
- **Wildcard event types are excluded.** Registrations name exact type IDs. Prefix/glob matching
  makes schema and projection requirements ambiguous and can silently attach new event types to an
  old subscriber. A future wildcard design needs explicit schema compatibility rules.
- **Best-effort listeners, retries, and dead-letter queues are excluded.** All initial reactions are
  required and transactional; a failure aborts the root. Optional/asynchronous listeners need a
  durable outbox, retry identity/backoff, poison-message/dead-letter policy, and independent audit.
- **Root-command idempotency keys are a separate kernel feature.** Transaction uniqueness prevents
  duplicate work inside one chain but cannot tell whether two separately received MCP commits are
  client retries. A future optional request/idempotency key must define result caching, conflict
  behavior when payloads differ, retention, and failed-request retry semantics before being added.
- **Ledger retention/compaction is deferred but acknowledged.** The initial ledger is append-only.
  Production retention needs archive/export integrity, correlation-chain atomicity, notification
  references, legal/audit policy, and query behavior over archived chains; no ad-hoc row deletion.
- **Action-attempt guards are deferred.** Initial guards veto proposed structural/derived events
  after a mechanic has produced effects. Blocking an intent before mechanic execution requires
  registered action-attempt event types and a stable action proposal schema; it must not be faked
  by parsing intent text.
- **Authorization and external delivery remain outside E1.** When actors/accounts exist, event-type,
  registration, notification-state, and guard-administration permissions need a separate security
  design. Email/webhook/push scheduling remains governed by the separate outbox/scheduler work.

These exclusions are not silent gaps. If an implementation needs one to satisfy a slice, stop and
revise the architecture rather than adding a local flag or background task.

## Plan-quality audit

| Question | Result and location |
| --- | --- |
| One target with explicit boundary? | Yes: Goal plus Scope boundaries separate event-driven behavior from scheduling/external delivery. |
| Existing owners respected? | Yes: effects remain with `IEffectApplier`, actions with `ActionRunner`, rules with mechanics, audit with `OperationLog`, and authored records with catalog/version stores. |
| Every missing dependency expanded? | Yes: schema validation/event types → mode-aware registrations → receipts/proposals/guards → accepted ledger → reaction router/replay → notifications. |
| Slices independently usable and large enough? | Yes: each slice lands contract, model, migration, store, catalog, MCP/readback, tests, and an explicit absence boundary. |
| Exactly one next slice? | Yes: Slice 4 transactional structural event ledger is the sole next candidate; it is not yet authorized. |
| Inputs and state closed? | Yes: exact IDs/statuses/limits/filter shapes/role bindings/output fields and missing/empty semantics are specified. |
| Transaction ownership unambiguous? | Yes: root runners own one transaction; applier produces receipts; router/audit participate; failure audit is outside rollback. |
| Event order and replay unambiguous? | Yes: proposal/receipt order, guard and reaction order+ID, queue order, causation/depth/sequence, seed derivation, and replay exclusions are explicit. |
| Guard ownership clear? | Yes: effects apply only inside the root transaction; guards read frozen proposals/final uncommitted state, return allow/deny only, and denial rolls back the root. |
| Conditional/random ownership clear? | Yes: host owns indexed equality filters; event JS owns rich conditions and seeded chance, with allow/deny for guards and empty output for false reactions. |
| Notification ownership clear? | Yes: reactive outputs propose immutable notices; the host validates/persists them; administrative state changes never mutate world state. |
| Catalog/runtime authority preserved? | Yes: event types/subscriptions are file-first with import/export/manifest; events/executions/notifications are runtime ledger data. |
| Adversarial tests sufficient? | Yes: every slice covers happy/versioning, closed input, missing/inactive/corrupt references, rollback, migration, protocol, catalog, and repository gates; chain slices add limits/replay. |

All audit answers are yes. If implementation requires a predicate DSL, background worker,
free-form event effect, event rewriting, best-effort reaction, dynamic role inference, partial
chain commit, or another source of random state, the plan is no longer valid: stop and revise
rather than introducing it inside a slice.

## Acceptance tests

- Unknown event types and subscriptions targeting missing/inactive mechanics fail before writing.
- A valid effect emits the expected ledger event and runs exactly the matching subscriptions in
  stable order.
- Scope and metadata filters exclude nonmatching subscriptions.
- Matching guards run by explicit order then ID; every guard must allow, and one denial returns
  exact versioned middleware evidence while preserving the complete pre-action state.
- Rich mechanic conditions can return no outputs without creating ledger children or notifications.
- The same root seed/state reproduces random decisions, derived event order, execution seeds, and
  notifications across fresh databases.
- Registered event A can produce registered event B, which can produce C, with correct causation,
  depth, sequence, and stable explicit-order/ID subscriber order.
- A failing subscriber or invalid child effect leaves both world state and event ledger unchanged.
- A guard can veto a root structural proposal, a reaction-produced structural proposal, or an
  explicit derived-event proposal; each veto rolls back the complete root correlation.
- Loop depth, event-count, and repeated-subscriber limits abort safely and identify the offending
  event/subscription in the returned error and audit record.
- A recorded chain can be queried by correlation ID and replayed with the same seed to reproduce
  its event order, mechanic versions, effects, and narration.
- Existing actions with no subscriptions retain their current behavior and performance.
- Notification queries filter by unread state and tracked entity without payload scans; marking a
  notice read/archive changes no world state and emits no event.

## Scope boundaries

This subsystem includes pre-commit guard middleware, chained accepted events, conditional reaction
mechanics, deterministic chance on proposed or accepted events, and transactional in-system
notifications for tracked items. It does not add wall-clock timers, background jobs, external
push/webhooks/email, server-held sessions, wildcard subscribers, best-effort listeners, or action-
intent interception.

Real-time or delayed random occurrences need a separate scheduled-events design covering durable
jobs, clock authority, restart/catch-up behavior, cancellation, concurrency, and delivery. Until
that is approved, time advances through explicit actions that can emit a registered clock event.
