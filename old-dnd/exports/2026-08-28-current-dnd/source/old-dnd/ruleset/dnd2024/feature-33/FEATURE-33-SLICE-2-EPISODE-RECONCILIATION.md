# Feature 33 Slice 2 reconciliation — one clock-scoped rest episode

Status: **Confirmed; implementation is authorised.**  
Owner: `ruleset/dnd2024/feature-33/FEATURE-33-DEPENDENCY-PLAN.md`, Slice 2  
Ruleset alignment: **dnd2024-owned**  
Source: `source.dnd2024.srd-5.2.1`, *Rules Glossary > Short Rest* (PDF p. 186) and *Long Rest*
(PDF p. 184).

## Outcome and exclusions

One creature may record one active Standard Short or Long Rest episode against one existing world
clock and policy entity. An accepted scoped advance of that exact root clock updates the episode’s
derived progress through Platform E8’s bounded selector. Reaching the declared duration records
that the episode is ready for a later completion owner; it grants no recovery.

This slice does not implement interruptions, resumption, completion benefits, Hit Dice, healing,
resource recovery, exhaustion, temporary-HP expiry, party rests, a scheduler, a second clock, or a
caller-supplied elapsed-time value.

## Proposed permanent contract

| Artifact | Proposed ID / shape | Owner and reason |
| --- | --- | --- |
| Episode state | `dnd2024.rest-episode` component on the resting creature | Feature 33 owns a bounded active episode, not recovery state. |
| Begin action | `mechanic.dnd2024.rest.begin` | Catalog JavaScript validates source-backed rest eligibility and emits the episode/relationship effects. |
| Clock reaction | `mechanic.dnd2024.rest.clock-reconcile` | Catalog JavaScript receives one selected episode holder from an accepted scoped root-clock advance. |
| Reaction registration | `subscription.dnd2024.rest.clock-reconcile` | Versioned catalog subscription using E8's scoped fan-out selector; no custom router behavior. |
| World membership fact | `dnd2024.rest.world` relationship, `world -> creature` | The existing directed relationship proves that an episode holder belongs to the reacting world scope. |

The episode component is closed:

```json
{
  "policyEntityId": "content.dnd2024.rest-policy.standard.v1",
  "kind": "short",
  "worldId": "world.example",
  "startedAtMinute": 120,
  "requiredMinutes": 60,
  "status": "active",
  "sourceRef": {
    "sourceId": "source.dnd2024.srd-5.2.1",
    "locator": "Rules Glossary > Short Rest, PDF page 186"
  }
}
```

`kind` is exactly `short` or `long`; `status` is exactly `active` or `ready`. `requiredMinutes`
must exactly equal the cited immutable policy’s 60 or 480-minute duration. The component stores its
start coordinate only; it never copies current clock minute, clock revision, elapsed time, an
interruption, activity, recovery, roll, resource, or outcome.

## Closed behavior

1. `rest.begin` receives exactly `{ "kind": "short" | "long" }`; the creature and world are
   resolved roles, never arbitrary IDs in input. It reads the active world’s clock and the standard
   immutable policy, verifies the creature has current HP greater than zero through the existing HP
   owner, and rejects a second episode or missing/corrupt scope membership.
2. It emits only `component.add(dnd2024.rest-episode)` and
   `relationship.create(dnd2024.rest.world, world -> creature)`, atomically. It derives
   `startedAtMinute` and `requiredMinutes`; callers cannot supply either.
3. The accepted `game.core.world.clock.advanced` event names and scopes the exact world through
   its declared `worldId` payload field. The subscription uses that nonempty scope and E8 Slice 2
   to select creatures with `dnd2024.rest-episode` through `dnd2024.rest.world`.
4. For each selected creature, `rest.clock-reconcile` verifies the same world/policy/source and
   accepts only a monotonic root-clock advance. If `afterMinute - startedAtMinute` is
   below `requiredMinutes`, it returns no effects. At or above it, it replaces only the episode
   `status` with `ready`. It never applies a benefit or removes the episode.
5. E8 supplies canonical candidate order, receiver binding, preflight, chain budgets, and atomic
   rollback. The reaction source contains the D&D duration comparison; C# remains generic.

## Confirmation record

The requested confirmation approves all of the following before an active implementation document,
catalog schema, permanent records, or migration is created:

1. The five permanent IDs and the directed `world -> creature` relationship above are the accepted
   state/action/event vocabulary.
2. The episode keeps only its immutable policy reference, kind, world ID, start minute, required
   duration, status, and source reference; `ready` is the only Slice 2 terminal state and grants
   no recovery.
3. Slice 2 uses accepted `game.core.world.clock.advanced` plus E8’s bounded fan-out—no new
   scheduler, clock query, or structural-event inference.
4. The initial admission check is current HP > 0; the 16-hour Long-Rest restart gate and all
   interruption/resumption rules remain later slices.

## Acceptance evidence after confirmation

- Valid Short and Long episode starts derive policy duration and root-clock start minute with no
  caller authority; duplicate, zero-HP, absent policy/clock/world membership, corrupt policy, and
  wrong scope reject unchanged.
- One clock replacement below duration keeps the episode active; exactly duration and over-duration
  make it ready once; repeated/replayed replacements produce no duplicate transition.
- Two worlds and multiple creatures prove scope isolation, ordinal fan-out order, empty selection,
  eight-member limit, and one-over-bound root rollback through E8.
- Source/membership/policy/clock/projection/mechanic/effect/audit failure leaves no partial episode,
  relationship, event execution, or success evidence.
- Catalog validation, fresh import, focused tests, full suite, and protocol walk (new subscription
  registration) pass. A receipt records this slice and stops before interruption or recovery.
