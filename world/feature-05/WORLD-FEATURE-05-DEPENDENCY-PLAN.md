# World Feature 5 dependency plan — explicit world time

Status: **Feature 5 verified**  
Last updated: 2026-08-20

## Target capability

The existing world-root entity carries one closed clock component. A trusted GM can advance that
clock by a declared positive number of in-world minutes through a deterministic action. The action
atomically updates the current minute and revision, and the action/structural-event ledger supplies
the durable evidence of the advance.

Time is an authored world coordinate, never wall-clock time, a scheduler, a travel calculation, or
a campaign-owned duplicate.

### Included

- One `game.core.world.clock` component attached directly to an active world-root entity.
- One explicit calendar identity, monotonic minute count, and revision.
- One `mechanic.game.core.world.clock.advance` action with a closed `minutes` input.
- A governed procedure, fixture clock state, action/replay/overflow/no-change coverage, and use of
  the existing `world.component.replaced` event/audit record.

### Excluded

- Calendars with dates, weekdays, seasons, time zones, real-time synchronization, schedules,
  timers, durations, background advancement, random encounters, route costs, or travel time.
- Campaign, quest, faction-front, condition, or opportunity reactions. Those may consume accepted
  clock changes only in later separately planned features.
- New event type/subscription, MCP tool/kind, C# game concept, migration, or copied time fields on
  campaigns, locations, travellers, routes, or events.

## Source and contract basis

| Authority | Evidence | Decision supplied |
| --- | --- | --- |
| Feature workflow | `AGENTS.md`; `procedure.system.create-feature`; `procedure.system.verify` | Repository mode, confirmation boundary, catalog validation, and focused/full acceptance evidence. |
| World state | `procedure.world.model`; `procedure.world.change`; `procedure.world.naming` | A clock is a component, not a kernel table or entity-ID field; complete replacement is atomic. |
| Existing topology | `procedure.game.core.world.location` and [Feature 1 receipt](../feature-01/WORLD-FEATURE-01-RECEIPT.md) | The root's existing component has exactly status/summary/visibility and must not be widened with time fields. |
| Action/mechanic runtime | `procedure.action.run`; `procedure.mechanic.write`; `procedure.mechanic.projection` | Frozen root/clock projection, closed action input, deterministic proposed effect, and replay. |
| Audit/event runtime | `ActionRunner`, `EventLedgerTests`, and `world.component.replaced` | Accepted component replacement is correlated to the root action operation. JavaScript receives no operation ID, so state must not pretend to record one. |
| World ownership | [World/lore plan](../../WORLD_AND_LORE_PLAN.md), Time and Slice 5 | The clock is world-owned; campaigns consume rather than copy it. |

No component, procedure, mechanic, event type, or fixture currently owns the proposed clock
identifier. Feature 2 expressly excluded time, and Features 3–4 own factions/motives and
knowledge, not temporal state.

## Ownership and confirmed vocabulary

The following permanent IDs and data meanings were confirmed by the user on 2026-08-20:

| Artifact | Meaning |
| --- | --- |
| `game.core.world.clock` | Closed clock state attached directly to the one world-root entity. It is the sole source of current in-world time for this first world. |
| `procedure.game.core.world.time` | Governs clock recording, correction, calendar identity, input limits, and the audit/event boundary. |
| `mechanic.game.core.world.clock.advance` | Active deterministic rule that advances one root clock by a supplied number of minutes. Its category is `game.core.world.time`. |

### Confirmed closed component and input

```text
game.core.world.clock
{
  calendarId: trimmed text, 1–100 Unicode scalar values,
  currentMinute: integer, 0–1,000,000,000 inclusive,
  revision: integer, 0–2,147,483,647 inclusive
}

clock.advance input
{
  minutes: integer, 1–1,440 inclusive
}
```

The initial fixture uses one confirmed calendar ID, `currentMinute: 0`, and `revision: 0`.
`calendarId` identifies the convention a future display/calendar feature may interpret; it is not a
date format, an external clock, or a mutable display label. `currentMinute` is elapsed in-world
minutes from the fixture calendar's authored epoch. `revision` increments once per accepted
advance and lets a reader distinguish the initial state from a later equal-looking display.

All records are closed objects. Missing, `null`, non-integers, negative values, unsafe numeric
shapes, extra keys, whitespace-only/untrimmed calendar ID, arrays, and scalar/non-object data
reject. A direct correction is administrative and replaces the complete clock component after
inspection; it must not claim to be elapsed play.

## Audit and event policy

The advance mechanic cannot know the action's operation ID, because the runner allocates it only
after the mechanism returns effects. The rule therefore does not carry `lastAdvanceOperationId` or
any guessed audit field. On success, the action result, operation row, and existing
`world.component.replaced` event share the root-operation correlation. That is the authoritative
trace from new clock state to the action/mechanic version that produced it.

No `world.time.advanced` event is created: the state change and structural event already capture
the only outcome in this first slice. A semantic event is a later dependency only if a reaction
needs a stable clock-specific meaning that a guarded `world.component.replaced` subscription cannot
safely express.

## Recursive dependency analysis

```text
World Feature 5: one explicit monotonic world clock
├─ Feature 1 active root fixture and closed root component           [verified]
├─ generic component/effect transaction                              [implemented]
├─ action runner, deterministic replay, operation correlation       [implemented]
├─ structural component-replacement event                            [implemented]
├─ Features 3 and 4 story-first fixture boundary                    [verified]
├─ confirmed clock vocabulary, calendar, limits, fixture identity   [implemented: Slice 1]
│  └─ component, procedure, root fixture, catalog tests             [implemented]
└─ one manual clock advance                                          [implemented: Slice 2]
   ├─ closed minute input and root/clock projection                  [implemented]
   ├─ deterministic component replacement                            [implemented]
   └─ action/replay/audit/event/no-change tests                      [implemented]

Dates, schedules, travel time, reactions, campaigns, quests, UI [excluded]
```

The clock has no technical dependency on faction or knowledge data. They remain sequencing gates
because this plan follows the current world delivery order and the final compact fixture must be
reviewed as one coherent setting.

## Slice order and stop gates

| Order | Slice | Starts only when | Exit gate |
| --- | --- | --- | --- |
| 1 | Root clock foundation | Features 3–4 are verified and the clock vocabulary, numeric bounds, and calendar identity above are confirmed. | **Verified:** fresh catalog import has exactly one valid clock on the world root; invalid data and wrong placement are rejected. See the [Slice 1 receipt](WORLD-FEATURE-05-SLICE-1-RECEIPT.md). |
| 2 | Deterministic manual advance | Slice 1 is verified. | **Verified:** a real action advances exactly one clock by a valid minute value; overflow, reversal-shaped input, corrupt state, and replay preserve state. See the [Slice 2 receipt](WORLD-FEATURE-05-SLICE-2-RECEIPT.md). |

## Slice 1 — root clock foundation

### Runtime artifacts

| Artifact | Change |
| --- | --- |
| Component definition/schema | Add `game.core.world.clock` with the closed data contract above and a description pointing to the time procedure. |
| Governing procedure | Add `procedure.game.core.world.time`. It declares root-only placement, complete-record correction, calendar identity policy, and the distinction between administrative correction and action advance. |
| Catalog fixture | Attach one clock component to the verified `world.feature-01.fixture` root. It does not alter the root component, containment topology, relationships, Feature 2 traveller state, or Features 3–4 content. |
| Focused test | Add `CatalogWorldFeature5Tests` or the closest catalog-world test owner for fresh-import/readback and disposable invalid-fixture cases. |

The fixture uses an additional component on the root rather than a clock entity, containment edge,
or `worldId` relation. There is exactly one clock per world root by this feature convention. A
generic direct component write cannot universally enforce this game rule, so fixture validation and
the governed procedure make it explicit instead of claiming hidden generic enforcement.

### Slice 1 acceptance matrix

| Test class | Input/setup | Exact expected result |
| --- | --- | --- |
| Fresh import | Disposable catalog import | The verified root has exactly one clock with confirmed calendar ID, minute zero, and revision zero. |
| Placement/count | Clock on non-root, absent root clock, multiple root clocks | Focused convention validation rejects the fixture; no location/campaign/traveller carries clock data. |
| Closed data | Missing/extra/non-object fields, invalid strings/integers/bounds | Schema/fixture validation identifies the invalid field; valid root and topology data remain unchanged. |
| Isolation | Existing Feature 1–4 fixture state | Existing component bytes, containment, relationships, and mechanics are unchanged except the root's new clock component. |
| Readback | World-root query/store read | The clock is attached to the root and contains no operation ID, date string, duration, route, or scheduler field. |
| Repository | Focused test, `roleplay validate catalog`, full suite at slice acceptance, `git diff --check` | All pass without persistent import. |

### Slice 1 exit gate

**Verified.** The approved component/procedure vocabulary, root-only fixture placement, numeric
semantics, and fresh-import evidence agree. See the [Slice 1 receipt](WORLD-FEATURE-05-SLICE-1-RECEIPT.md).
Stop before the advance mechanic.

## Slice 2 — deterministic clock advance

Add the mechanic `.md`/`.js` pair and the action path to `procedure.game.core.world.time`.

The mechanic declares exactly one required `world` role, carrying
`game.core.world.root` and `game.core.world.clock`. Input must be exactly the one-key object
`{"minutes": n}`. It accepts only an active root, a valid closed clock component, a calendar ID,
and integer `minutes` in the confirmed 1–1,440 range. It computes
`currentMinute + minutes` and rejects when the result exceeds 1,000,000,000 or the revision cannot
increment. It makes no random call.

On success it returns exactly one complete `component.set` effect for `game.core.world.clock`:
the same `calendarId`, the computed `currentMinute`, and `revision + 1`. It changes no root data,
location, containment, relationship, faction, motive, knowledge, campaign, quest, or traveller
state. The accepted effect generates one existing `world.component.replaced` event and action/audit
evidence under one root operation.

### Slice 2 acceptance matrix

| Test class | Input/setup | Exact expected result/state assertion |
| --- | --- | --- |
| Happy path | Active root at minute 0/revision 0; `{"minutes":60}` | One action selects the clock rule and changes only the clock to minute 60/revision 1. |
| Boundary | Inputs 1 and 1,440; current minute near maximum | Exact legal result at each boundary; overflow/revision limit rejects atomically. |
| Closed call | Missing/extra/wrong role; missing/extra/invalid input key or non-object input | Rejected with zero effects and byte-identical root clock. |
| Invalid root/clock | Inactive root; missing/malformed/negative/out-of-range clock fields | Deterministic rejection; caller input never repairs stored state. |
| Replay/determinism | Same fresh fixture, role/input/seed; repeat after success | Fresh output/effect match; repeat advances only from its new stored time, not from a stale replay claim. |
| State/event/audit | Success and rejection | Success has one changed clock component, one structural replacement event, and one correlated action operation; rejection has no replacement. |
| Repository | Focused action test, catalog validation, full suite, diff check | All pass; no protocol walk unless public MCP surface/dependency registration changes. |

### Slice 2 exit gate

**Verified.** Fresh-import action coverage proves legal advance, boundaries, stored-state
validation, replay, operation/event correlation, and no-change behavior. Catalog validation and
the full suite pass. See the [Slice 2 receipt](WORLD-FEATURE-05-SLICE-2-RECEIPT.md). Stop before
reactive time consumers, travel costs, schedules, or calendar display.

## Required confirmation and plan-change rule

Confirm the permanent IDs, root-only placement, calendar identity, minute epoch, numeric maxima,
advance range, and the deliberate omission of a mutable last-operation field before implementation.
Revise this plan rather than widening it if time must have dates/seasons, a clock belongs somewhere
other than the root, an advance needs a cause or travel calculation, a consumer needs a semantic
event, or the product requires actual scheduled advancement. Each changes ownership or needs a
separate dependency plan.
