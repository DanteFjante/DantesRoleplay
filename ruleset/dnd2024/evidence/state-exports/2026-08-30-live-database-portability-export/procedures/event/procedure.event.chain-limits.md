---
id: procedure.event.chain-limits
category: event
name: Chain limits
governs: how far a reaction chain may run before it is stopped
status: active
createdBy: "seed"
changeNote: "Seeded from bootstrap file."
---

## Description
What bounds a reactive chain, what happens when a bound is reached, and how to read the result.

## Instructions
1. Expect four bounds on one committed change: depth 8, one hundred events, one hundred guard and
   reaction executions, and each subscription's own `maxExecutionsPerChain` of 1 to 8.
2. Read the failure code rather than the message. `EVENT_DEPTH_LIMIT`, `EVENT_COUNT_LIMIT`,
   `EXECUTION_COUNT_LIMIT` and `SUBSCRIPTION_EXECUTION_LIMIT` each name a different cause and a
   different fix.
3. On `EVENT_DEPTH_LIMIT`, look for two rules reacting to each other. Read the failed operation in
   `query(kind: "history")`, then the chain it names with
   `query(kind: "events", correlationId: ...)`.
4. On `SUBSCRIPTION_EXECUTION_LIMIT`, decide whether the subscription should genuinely run that
   often. Raising its limit is right for a rule that legitimately handles many events in one change;
   it is the wrong answer for a rule that is triggering itself.
5. Set `maxExecutionsPerChain` deliberately when registering a reaction. The default of 1 is right
   for a rule that answers one event; raise it only for one that answers several.

## Constraints
- A chain that reaches any limit fails the WHOLE change. Nothing is left behind: no world state, no
  events, no executions, and no success audit. A chain cut off half way would leave the world in a
  state no rule intended and no reader could explain.
- Proposed events count towards the event limit, not only accepted ones. A chain whose events are
  mostly vetoed still spends the budget, because proposing and guarding them is the work being
  bounded.
- Limits are checked BEFORE a mechanic runs. A limit enforced afterwards has already paid the cost
  it exists to bound.
- The bounds are fixed in the kernel and are not configurable per campaign. They exist because a
  chain terminates only if the rules somebody wrote happen to terminate, which is not a property
  anything can check in advance.
- Raising a subscription's own limit cannot exceed 8, and never raises the chain-wide bounds.
