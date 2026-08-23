# Feature 33 Slice 2 implementation — clock-scoped rest episode

Status: **Accepted**  
Owner/roadmap: `ruleset/dnd2024/ROADMAP.md`, Feature 33  
Dependency tree/leaf: `FEATURE-33-DEPENDENCY-PLAN.md`, Slice 2  
Ruleset alignment: **dnd2024-owned**  
Source ID and locator: `source.dnd2024.srd-5.2.1`, *Rules Glossary > Short Rest* (PDF p. 186) and
*Long Rest* (PDF p. 184)  
Contract: `FEATURE-33-SLICE-2-EPISODE-RECONCILIATION.md`

## Outcome and boundary

Implement one source-backed active Short or Long Rest episode on one creature in one world. Begin
derives its start coordinate and required duration from authoritative policy and clock state. An
accepted scoped clock-advance event reaches scoped episode holders through E8 and marks an elapsed
episode `ready`; no recovery is granted.

Allowed areas: the confirmed Feature 33 catalog component/procedures/mechanics/event subscription
and fixtures; focused tests; catalog manifest; generic E8 only when a test exposes a platform bug.
All game eligibility, duration, and state branching belongs in catalog JavaScript. C# may not gain
Feature 33 IDs or rule logic.

Stop point: a ready episode is evidence only. Do not implement interruption, resumption, completion,
Hit Dice, healing, temporary-HP expiry, Exhaustion, resource/slot recovery, or an additional clock.

## Confirmed decisions

- IDs, component shape, relationship direction, admission rule, status vocabulary, and event bridge
  are exactly those in the confirmed reconciliation.
- `game.core.world.clock.advanced` and the accepted E8 payload/fan-out contracts are the dispatch
  mechanism. The root world clock stays its sole time owner.

## D&D 5e 2024 alignment

| Rule concern | Source meaning | Existing owner | Consequence |
| --- | --- | --- | --- |
| Rest duration | Short Rest is one hour; Long Rest is at least eight hours | immutable `dnd2024.rest-policy` | JavaScript checks policy-derived 60/480 only. |
| Start eligibility | A rest starts only at 1+ HP | existing HP owner | JavaScript reads current HP; caller cannot assert eligibility. |
| Time | Rest progresses against elapsed world time | `game.core.world.clock` | episode stores start minute only; clock reaction derives elapsed time. |
| Benefits | Source recovery follows completion | later Feature 33 and consequence owners | Slice 2 merely records `ready`. |

## Prerequisite evidence

- Feature 33 Slice 1 immutable policy receipt.
- Platform E8 Slice 1/2 accepted receipts and subscription contracts.
- `procedure.game.core.world.time` and the accepted [scoped clock-event receipt](../../../world/core-time/CLOCK-SCOPED-EVENT-RECEIPT.md).

## Resolved prerequisite evidence

`game.core.world.clock.advanced` now declares `worldId` as an E8 entity-payload field and records
the exact root world as both event scope and entity identity. Its accepted receipt proves all four
initial clock producers record it after structural changes in the same root batch. E8 therefore can
apply its exact scope match without interpreting a structural payload, adding a global selector, or
polling a clock.

## Runtime artifacts

Create only confirmed catalog artifacts: `dnd2024.rest-episode`,
`mechanic.dnd2024.rest.begin`, `mechanic.dnd2024.rest.clock-reconcile`,
`subscription.dnd2024.rest.clock-reconcile`, and relationship kind `dnd2024.rest.world`.

## Authoritative state and closed input

`rest.begin` accepts only `{ "kind": "short" | "long" }`; world and creature arrive as declared
roles, and the caller may name only the canonical policy entity role whose immutable data the
mechanic validates. Clock, current HP, scope membership, start minute, duration, source reference,
and receiver identity are derived from projections/event bindings. The reconciliation’s closed
episode shape is authoritative.

## Behavior, failure, replay, and rollback

Begin atomically adds episode state and world membership. The E8-routed reaction sees the accepted
scoped clock-advance event and marks an active episode ready exactly once when elapsed minutes reach its
policy duration. Any malformed, absent, stale, wrong-scope, replayed, corrupt, projection, or
mechanic failure aborts unchanged under the existing root transaction.

## Implementation sequence and acceptance

1. Add catalog schemas/procedures/mechanics/subscription and only required fixture records.
2. Add focused fresh-import and JavaScript behavior tests, including source/scope/duplicate/zero-HP,
   threshold/replay, E8 fan-out, and rollback assertions.
3. Run focused tests, catalog validation, full suite, and protocol walk; write receipt and stop.

## Verification and exit gate

Use focused Feature 33/E8 tests, `roleplay validate catalog`, full suite, and protocol walk because
the catalog subscription changes the MCP-visible catalog surface. Record results in
`FEATURE-33-SLICE-2-RECEIPT.md`, set the dependency/roadmap status once, and stop before Slice 3.

**Accepted; see `FEATURE-33-SLICE-2-RECEIPT.md`.**
