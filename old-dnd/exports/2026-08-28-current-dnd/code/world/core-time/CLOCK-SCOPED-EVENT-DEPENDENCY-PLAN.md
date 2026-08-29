# Core-world-time dependency plan — scoped clock-advance event

> **D&D implementation reference:** When this work includes D&D 2024 mechanics, inspect [Foundry VTT dnd5e](https://github.com/foundryvtt/dnd5e) before designing or coding. Use it as a licensed engineering reference—not a direct dependency or rules authority—while retaining the exact `source.dnd2024.srd-5.2.1` locator and all applicable MIT, CC-BY, and asset-license notices for any reused material.

Status: **Accepted**  
Owner: `procedure.game.core.world.time`  
Ruleset alignment: **ruleset-neutral**  
Source: Not applicable; generic world-time/event infrastructure.

## Outcome and non-goals

After an accepted mutation of the one root `game.core.world.clock`, the same root transaction
records one semantic event scoped to that world. It enables bounded E8 subscriptions to react to a
particular world’s elapsed-time evidence without treating structural component replacement as a
global scheduler. It preserves existing `world.component.replaced` evidence and adds no clock,
background process, polling, wall-clock time, date system, generic query, campaign duplicate, or
D&D/rest behavior.

## Existing owners and evidence

| Concern | Owner | State | Evidence |
| --- | --- | --- | --- |
| Root clock authority | `procedure.game.core.world.time` | verified | One root component; only governed actions change minute/revision. |
| Event transaction | E1 ledger / `EffectApplier` | verified | Events and reactions share the root transaction. |
| Scoped selection | E8 Slice 2 | accepted | Fan-out requires nonempty exact `EventDetail.Scope`. |
| Current clock evidence | `world.component.replaced` | conflicting | Its event scope is empty, so it cannot meet E8’s scope rule. |
| First consumer | Feature 33 Slice 2 | blocked | It must not infer a world scope from component JSON. |

## Dependency tree

```text
scoped semantic clock-advance event                                  [accepted]
├─ root clock and monotonic mutations                                 [verified]
├─ scoped event type/schema                                           [accepted]
├─ direct advance, route, ground- and aerial-conveyance producers     [accepted]
├─ E1 root transaction                                                [verified]
└─ E8 scoped payload binding / fan-out                                [accepted]
    └─ Feature 33 rest episode                                       [ready successor]
```

## Proposed semantic contract

Create `game.core.world.clock.advanced`, scoped to and naming exactly the root world entity. Its
closed payload is:

```json
{"worldId":"world.example","calendarId":"lantern-compact-epoch","beforeMinute":120,"afterMinute":180,"beforeRevision":4,"afterRevision":5}
```

`worldId` is declared as an E8 entity-payload field. A producer emits this only after successfully
replacing that exact valid clock, with unchanged calendar, positive monotonic minutes, and exactly
one incremented revision. It contains no route, actor, campaign, rest, schedule, consumer, or
caller-selected duration. The structural replacement event remains first; this scoped semantic
event follows in the same accepted root batch.

The initial producer set is direct clock advance, route travel, ground-conveyance travel, and
aerial-conveyance travel. Every later governed clock producer must explicitly opt in; consumers may
never synthesize this event from a structural replacement.

## Ordered leaves

| Order | Leaf | Exit gate |
| ---: | --- | --- |
| 1 | Confirm this event/schema, scope/entity identity, ordering, and producer set | Permanent and cross-owner semantics approved. |
| 2 | Declare event/schema and revise each producer | Every clock mutation emits one valid scoped event atomically. |
| 3 | Prove E8 routing | Only matching-world receivers run; global structural subscriptions remain compatible. |
| 4 | Unblock Feature 33 | Feature 33 independently implements its episode; this plan adds no rest state. |

## Confirmation gates

Confirm the exact proposed event ID/schema, `worldId` scope/entity identity, structural-then-semantic
ordering, and all four initial producers. After confirmation, create one active core-world-time
implementation slice only.

## Planning receipt

- Accepted implementation: `CLOCK-SCOPED-EVENT-RECEIPT.md`.
- Feature 33 Slice 2 may now use this scoped event; it remains a separate, unstarted slice.
