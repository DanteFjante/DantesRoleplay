# Events and subscriptions — receipt

Completed 2026-08-20. Six slices, verified at **365/365 tests, 0 errors**.

This receipt is the durable design and acceptance summary. Runtime contracts, code, migrations, and
tests own the executable detail; completed slice-planning prose has been removed.

---

## What it does

A rule can now answer a change to the world, and a change to the world leaves a record that says
what it replaced.

Concretely: `commit(kind: "effects")` or `commit(kind: "action")` applies a batch inside one
transaction. Each applied effect produces a **receipt** — what it touched, and the state it
displaced — and each receipt becomes a proposed structural event. Registered **guards** see those
proposals and may veto, which rolls the whole change back. Surviving proposals are written to an
append-only **ledger**. Registered **reactions** then run on the accepted events; their effects go
back through the same applier, proposing their own events and facing the same guards one level
deeper. A reaction may also **declare an event** of its own, for facts the world does not show, and
**raise a notification** for a person to read. All of it commits together or none of it does.

| Slice | What landed |
| --- | --- |
| 1 | Versioned event types with JSON Schema payloads; the nine reserved `world.*` structural types; `query`/`commit` for event types |
| 2 | The mode-aware, versioned subscription registry — guard or reaction, immutable mode, append-only revisions |
| 3 | Pre-commit guards. A denial rolls back the root and records structured evidence in the failed operation |
| 4 | The append-only structural event ledger, correlation and causation, `query(kind: "events")` with nine filters |
| 5a–5b | Reaction dispatch, chain budgets, the `ctx.event` envelope, and `ctx.eventEntities` as a keyed projection of declared components |
| 5c | Event receipts: `before` and `after` in every payload |
| 5d | Derived events — a rule declares one, further rules answer it |
| 6 | Notifications, and the final surface sweep |

## Evidence

- **365/365 tests, 0 errors.** 61 of them are this feature's, across eight classes:
  `EventLedgerTests` (14), `EventRouterTests` (13), `NotificationTests` (10), `DerivedEventTests`
  (7), `EventChainTests` (6), `ChainDeterminismTests` (4), `SubscriptionStoreTests` (4),
  `GuardRouterTests` (3).
- Every one of the nine structural payloads is checked against its registered schema, read from
  `catalog/event-types/` rather than restated in the test.
- A chain replays byte-identically into a genuinely fresh database — two fixtures, because one
  connection is one database and replaying into the first run's rows would prove nothing.
- Migrations: `EventTypes`, `Subscriptions`, `GuardEvidence`, `EventLedger`, `EventExecutions`,
  `DerivedEventProducer`, `Notifications`.

## The surface it added

Two kinds, and no fourth tool. Per `procedure.mcp.add-tool`, the unit of extension is a kind, and
each was added to `VerbSurface` and the verb's dispatch switch in the same change — a guard test
compares the two lists in both directions.

- `query(kind: "events")` — id, correlationId, causationId, rootOperationId, type, entityId,
  afterSequence, from, to, limit
- `query(kind: "notifications")` — id, state, topic, entityId, correlationId, from, to, limit
- `commit(kind: "event-type")`, `commit(kind: "subscription")`, `commit(kind: "notification")`

Seven contracts, authored file-first and seeded from the kernel: `procedure.event.define`,
`.guard`, `.react`, `.inspect`, `.chain-limits`, `procedure.subscription.create`/`.modify`, and
`procedure.notification.inspect`.

## Decisions that are load-bearing

**Everything commits together or nothing does.** A reaction is not a follow-up that happens after a
change — it is part of the change. "The change committed but its consequence did not" is the state
the whole design exists to make unreachable, which is why every failure anywhere in a chain rolls
back the complete root, including work that had already succeeded.

**Prior state is captured before it is overwritten, not reconstructed after.** A ledger recording
only the new value cannot answer "what did this rule actually do?" — four vigour is a scratch or
nearly fatal depending entirely on what it replaced. Two extra reads per effect is the price.

**A payload has two halves, and the split is a rule.** The top level identifies what changed and
holds only filterable scalars — which is exactly what a subscription's `payloadEquals` matches on.
`before` and `after` carry state and are never filterable. Without that line, every new field would
be an argument about which half it belongs in.

**A rule cannot declare a `world.*` event.** Those are the kernel's own record of what it did. A
rule able to forge one could claim a component was replaced that never was, in the one place whose
entire value is that it can be believed.

**Notification content is written once and is never editable.** A notice is evidence that a rule at
a version decided something was worth saying. Text that could be revised later would look like
evidence without being it. The only mutable thing is the delivery state, and archiving is one-way:
"I have dealt with this" must not be something a later mistake quietly undoes.

**A chain is reproducible or it is not auditable.** Guard and reaction seeds derive from the root
seed and execution position, subscriber order is declared-order-then-id, and the id tiebreak is not
decoration — two subscriptions at the same order would otherwise run in whatever sequence the
database returned.

**Four bounds, four distinct codes.** Depth 8, 100 events, 100 executions, and each subscription's
own limit of 1–8. Checked BEFORE a mechanic runs, because a limit enforced afterwards has already
paid the cost it exists to bound. Proposed events count, not only accepted ones.

## Deliberately absent

- **Delivery of any kind.** No push, no mail, no webhook, no polling, no scheduler. A notification
  is a row that waits until somebody asks.
- **The passage of time.** Nothing in this system moves unless a verb is called.
- **A commit kind for events.** An event that could be written directly would be a claim about a
  change that never happened. Asserted by a test, not merely by omission.
- **Unarchiving.** One-way in this release.
- **Backwards compatibility for the widened payloads.** The nine schemas were revised in place, so
  events recorded before that keep their narrower payload and their older `typeVersion`.
  `procedure.event.inspect` says to read that version before concluding an old row means nothing
  was replaced.

## What the suite caught that reading would not have

Recorded because the pattern matters more than the bugs.

- The unguarded commit path wrote its events **after** committing, so a failure between the two
  would have left a committed world change with no record of it. It hid for three slices in a dense
  one-liner among three near-identical lines.
- Guards rejected narration and data on an *allow*, which made a guard that explained itself a
  failing guard.
- A fresh install could not change the world at all: the nine `world.*` types shipped as catalog
  files only, so nothing seeded them.
- `Evaluate` in JsonSchema.Net 9 takes a `JsonElement`, not a `JsonNode`.
- `EvaluationResults.Details` is null on a leaf, so reading the detail of a *failed* validation
  threw — turning a clean rejection into an unhandled exception, on the one path where a payload
  actually failed. The verdict is now decided before the detail is read.

Two of these were only reachable by running the code. Simulating the logic in Python against a copy
of the live database — the technique that carried the catalog work — found none of them.

## Left to do

Operator side, once:

1. Stop the MCP server and start it once. It has been holding a DLL lock, and it needs a clean
   start to reseed: the nine `world.*` types go to v2 with the widened schemas, and the seven
   revised or new contracts append versions. Until then the running server serves the old manual.
2. `.\roleplay export catalog`, then commit the catalog, its manifest and the database
   together. `.\roleplay verify catalog` reports drift until you do. **Done 2026-08-20:**
   92 records exported, verify reports 91 unchanged and the two sides agreeing.

Both migrations already exist; `dotnet ef migrations add Notifications` fails now because it
succeeded the first time.

## Follow-on

Nothing here blocks anything. The guard and reaction routers now share one root-seed derivation;
the guard predicts the sequence the ledger will assign before the event is accepted. This changes
existing guard draws, intentionally, and the regression tests pin both the root and continuation
sequence behavior.

Two entries closed the same day this feature did. `orient()` no longer denies mechanic composition —
that line had been false for a whole feature, and every session is told to believe the list over
anything else it reads. And the pinned regression baseline is gone, having been wrong twice.

Not covered by the acceptance matrix, and recorded rather than claimed: cancellation mid-chain, a
root audit failure, and the proportional performance threshold for the no-subscription path.
