# Feature 33 dependency plan — rests, Hit Dice, recovery, and expiry

Status: **Planned; Slice 1 is an immutable, source-cited standard-rest policy catalog and is the next and only authorised implementation pass.**
Last updated: 2026-08-21

## Execution rule

This is a planning artifact. It creates no runtime procedure, component, entity, mechanic, fixture,
migration, action, event, subscription, or game state. An implementation pass re-reads the current
core-world clock, event/subscription, healing, exhaustion, class-progression, spellcasting, and
attunement contracts; confirms permanent IDs; validates a disposable catalog import; writes a
receipt; and stops after one accepted slice. It must not import the persistent database merely to
develop this feature.

## Target capability

One eligible creature can begin, interrupt, resume where the rules allow, and finish a source-backed
standard Short or Long Rest against the one authoritative world clock. Completion routes each
consequence to its existing state owner: Hit Dice, HP recovery, temporary-HP expiry, Exhaustion,
and supported rest-recharged resources. A rest itself never directly rewrites HP, slots, conditions,
class resources, effects, or the clock.

### Included

- Immutable standard-rest policy data: duration, start condition, permitted activity, interruption
  categories, and the source-defined completion consequences.
- Later creature-owned rest episode state, clock-bound elapsed-time reconciliation, and explicit
  interruption/resume/completion records.
- Short-Rest Hit-Die spending and sequential healing, when class membership/Hit-Dice facts and the
  healing composition seam are ready.
- Long-Rest recovery routed to the authoritative HP, temporary-HP, Exhaustion, Hit-Dice, ability-
  reduction, maximum-reduction, and resource owners.
- One narrow focused-Short-Rest completion handoff for Feature 29 attunement after its instance and
  physical-contact prerequisites exist.

### Excluded

- A second clock, scheduler, wall-clock timer, date system, route, itinerary, location, travel
  pace, sleeping accommodation, party-rest record, or automatic encounter simulation.
- Direct HP/maximum/ability/condition/resource/slot/effect writes; healing, expiry, Exhaustion,
  class, spell, and effect owners retain those transitions.
- Dying, resurrection, stable recovery, environmental Exhaustion causes, spell resolution, and
  concentration; they remain Features 17, 34, 32, and 18 respectively.
- Optional rest variants (including Gritty Realism), camp activities, watch assignment, random
  encounters, item charges/next dawn, and a generic "recover everything" endpoint.

## Official source basis

The fixed source is `source.dnd2024.srd-5.2.1`, *System Reference Document 5.2.1* (Wizards of the
Coast LLC, 2025-05-01, CC-BY-4.0), [Rules Glossary — Long Rest and Short Rest, PDF pp. 184 and
186](https://media.dndbeyond.com/compendium-images/srd/5.2/SRD_CC_v5.2.pdf).

- A Short Rest is one hour of downtime, starts only at 1+ HP, permits only light activity, and is
  stopped by Initiative, casting a non-Cantrip spell, or damage. An interrupted Short Rest grants
  no benefits. On completion, a creature may spend one Hit Point Die at a time, rolling that die
  plus Constitution modifier with a minimum recovery of 1; it chooses whether to spend another
  after each roll. Features recharge only as their own descriptions say.
- A Long Rest is at least eight hours, including at least six hours asleep and no more than two
  hours of light activity; it starts at 1+ HP and cannot be started until 16 hours after the prior
  finished Long Rest. Completion restores lost HP and spent Hit Point Dice, normalises reduced HP
  maximum and reduced ability scores, reduces Exhaustion by one, and invokes the source-specific
  recharge rules. It ends temporary HP through that rule's owner.
- Initiative, a non-Cantrip spell, damage, or one hour of walking/physical exertion interrupts a
  Long Rest. After at least one hour of rest the creature gets Short-Rest benefits; it may resume
  immediately, adding one hour for each interruption.

The policy stores these source facts, not copied rule prose or a creature's outcome.

## Planning inventory and ownership result

| Inquiry | Evidence and decision |
| --- | --- |
| World time | `procedure.game.core.world.time` owns one root `game.core.world.clock`; its governed actions advance a monotonic minute/revision. Feature 33 consumes it and never stores a clock copy or advances it by a caller-selected amount. |
| Clock event evidence | A root-clock replacement emits `world.component.replaced`; its `before` and `after` are evidence, not a general rest scheduler. Existing subscriptions bind only fixed roles and tracked entities. |
| Healing and temporary HP | Feature 16 owns `mechanic.dnd2024.healing.apply` and `temporary-hit-points.write`/`expire`. Healing currently takes a positive caller amount, so it cannot safely express "restore all lost HP" without an owner-approved full-recovery child transition. |
| Exhaustion | Feature 14 owns its condition state and recovery. Feature 33 invokes `recover` with the fixed one-level result; it never edits the condition entry. |
| Hit Dice and class resources | Feature 27 owns immutable class Hit-Die facts and class entitlement, but CH4 class membership and an actor Hit-Die/resource model are not implemented. Feature 33 owns spend/recovery timing, not class identity or feature semantics. |
| Spell resources | Feature 31 will own slots/prepared state. Its recovery/preparation transition is intentionally blocked on this feature; Feature 33 supplies only completed-rest authority. |
| Ability/HP-maximum reductions | No approved generic modification/reversal owner exists. Feature 33 must not silently erase unknown fields; it requires a named owner before claiming the complete Long-Rest benefit. |
| Interruption events | Initiative/turn mechanics, spell resolution, and damage are separate owners. Their current event shapes cannot dynamically bind an arbitrary resting creature to a reaction. No caller may assert an interruption. |
| Attunement | Feature 29 needs an interruption-aware, focused physical-contact Short-Rest handoff. Feature 33 does not create item instances, custody, or attunement state. |
| Party coordination | No party/rest-group ownership is verified. The first supported rest is one creature; coordinated rests are an explicit later extension. |

## Recursive dependency analysis

```text
Feature 33: source-backed rests and recovery                              [blocked parent]
├─ immutable standard-rest policy                                         [missing Slice 1 leaf]
├─ core world root clock                                                  [implemented]
├─ rest episode / clock evidence model                                    [blocked: dynamic rest-to-event binding]
│  ├─ generic clock-replacement fan-out or indexed active-rest reader     [missing platform/core decision]
│  ├─ start / elapsed / completion / resume state                          [blocked after binding decision]
│  └─ interruption evidence from Initiative, casting, damage, exertion    [blocked: source event contracts]
├─ Short-Rest benefits                                                     [blocked parent]
│  ├─ actor class membership and Hit-Die pool                              [blocked: CH4 + Feature 27]
│  ├─ Constitution modifier and seeded individual rolls                    [Feature 2 + roll/receipt seam]
│  ├─ healing transition composition                                       [Feature 16]
│  └─ feature-resource registry and reset contract                         [blocked: class/resource owners]
├─ Long-Rest benefits                                                      [blocked parent]
│  ├─ full HP recovery child transition                                    [blocked: Feature 16 extension]
│  ├─ temporary-HP expiry                                                  [Feature 16]
│  ├─ one-level Exhaustion recovery                                        [Feature 14]
│  ├─ Hit-Die/resource/slot recovery                                       [Feature 27 / Feature 31]
│  └─ reduced maximum/ability restoration                                  [missing named modification owner]
├─ focused Short-Rest attunement handoff                                   [blocked: Feature 29 + instance/custody]
└─ travel/duration consumers                                               [successors: Features 37 and 32]
```

The only lowest independent leaf is the immutable standard-rest policy. It gives all later owners
one source-cited vocabulary without pretending a creature has rested or recovered.

## Dependency and ownership decisions

1. **The root world clock remains the only time coordinate.** A rest episode may retain bounded
   progress/remaining-duration and interruption count, but never a second calendar, clock, or
   caller-selected elapsed-minute field. It accepts only authenticated root-clock evidence.
2. **A rest is an episode, not immediate recovery.** Starting a rest records no recovery. A
   completion transition is the sole source of recovery authority, and all recipient transitions
   occur atomically or the rest remains unfinished.
3. **Feature 33 owns timing and policy, not consequences.** It delegates exact HP, temp HP,
   Exhaustion, Hit Dice, class resource, spell resource, ability, and maximum transitions to their
   established owners. A source-specific feature opts in through a typed rest-recharge contract;
   no global scan/reset is permitted.
4. **Interruption must be authenticated.** Initiative, non-Cantrip casting, damage, and exertion
   are observed from their authoritative events/actions. An input such as `interrupted: true`, a
   caller-supplied cause, elapsed duration, recovery amount, roll, resource id, or final state is
   invalid.
5. **Long-Rest partial credit is a composition, not an exception.** An authenticated interruption
   after at least 60 minutes invokes the same Short-Rest completion path once, then records the
   Long-Rest interruption/resumption state. It never duplicates Hit-Die or resource logic.
6. **Hit Dice are spent sequentially.** A completed Short Rest opens one source-backed spend at a
   time; each roll/heal commits before the next choice. A batch amount, caller result, negative
   Constitution modifier arithmetic, or unused-die reset does not bypass this sequence.
7. **Long-Rest full healing needs its HP owner.** Feature 16 must expose an authenticated,
   zero-caller-amount `restore-to-maximum` transition (or an equally narrow approved equivalent)
   that reads current/maximum itself. Feature 33 must not compute and pass `maximum - current`,
   because the current composition model cannot bind that derived component value into a child
   safely.
8. **Static policy is immutable and separate from actor state.** A policy revision uses a new
   source-cited content entity/version; an actor never copies its duration, conditions, or benefit
   list. The standard policy is not campaign house rules or optional variants.

## Confirmation boundaries

| Decision | Required confirmation before its implementation |
| --- | --- |
| Static policy | Exact component/procedure/entity IDs, policy key/version convention, source locator format, and canonical interruption/benefit vocabulary. |
| Active-rest fan-out | The owner and transaction semantics for locating every rest affected by an accepted root-clock replacement, including bounded ordering and rollback. Existing fixed-role subscriptions are insufficient. |
| Rest episode | Component shape, creature/world scope relationship, start/resume/cancel/complete vocabulary, unique-active-rest rule, and no-clock-copy invariants. |
| Interruption protocol | Exact authoritative event IDs/payloads for Initiative, spell kind, damage target, and one-hour exertion; actor identity matching and event ordering. |
| Hit Dice | CH4 membership, Feature 27 class facts, actor pool shape, Constitution projection, deterministic roll/audit owner, and sequential choice persistence. |
| Long-Rest recovery | Feature 16 full-HP transition; named owners for reduced maximum/ability restoration; Feature 14/resource/slot child contracts and atomic order. |
| Attunement/travel | Feature 29 physical-contact assertion and Feature 37 elapsed-time consumer contract; neither may turn Feature 33 into a second scheduler. |

## Slice order and stop gates

| Order | Slice | Starts only when | Exit gate |
| ---: | --- | --- | --- |
| 1 | Immutable standard-rest policy | Permanent vocabulary and source locators confirmed. | A source-cited standard Short/Long policy reads deterministically with zero actor, clock, recovery, or event effects. |
| 2 | Active-rest platform and episode contract | Slice 1 and ratified dynamic clock/reaction binding. | One creature can have at most one validated, clock-scoped active episode; accepted root-clock evidence reaches the right episode without a duplicate clock or scheduler. |
| 3 | Authenticated interruption and resumption | Slice 2 and named Initiative/casting/damage/exertion contracts. | Each source interruption stops/rejects/resumes exactly as policy declares; no caller can forge it and no unrelated creature is affected. |
| 4 | Short-Rest Hit Dice and selected resource recharge | Slice 3, CH4/Feature 27 pool, Feature 16 healing, and first resource owner. | One die at a time is spent/rolled/healed through its owners; exact eligible resource recharges once on completion. |
| 5 | Long-Rest recovery composition | Slice 4, Feature 16 full recovery/expiry, Feature 14, named maximum/ability owner, and first slot/resource owner. | Every supported benefit delegates once in one atomic completion; unsupported benefit families make the policy capability explicitly bounded. |
| 6 | Attunement and elapsed-time consumers | Slice 5, Feature 29, Feature 31, and Feature 37 agreements. | A focused eligible Short Rest and a validated world-clock advance compose without duplicate recovery, clock, or effect expiry ownership. |
| 7 | Expansion | Slice 6 and source/owner review per addition. | Add a rest variant, class resource, species/item recovery, or group model only by reviewed policy and owner amendment. |

## Slice 1 — immutable standard-rest policy

### Runtime artifacts

- A confirmed immutable `dnd2024.rest-policy` component/schema and static-definition procedure.
- One versioned `content.dnd2024.rest-policy.standard.v1` entity with fixed provenance.
- Focused catalog validation/tests only. No rest episode, creature state, timer, clock action,
  interruption event, Hit Die, healing/resource transition, public "rest" action, or fixture.

### Data contract and required state

The policy is closed and immutable. It declares a stable key/version, the standard source
reference, exactly two canonical kinds (`short`, `long`), fixed minimum durations (60 and 480
minutes), 1-HP start requirement, the canonical interruption category set for each kind, and a
declarative ordered benefit vocabulary. Long Rest additionally declares the six-hours-sleep/two-
hours-light-activity constraint, 16-hour restart wait, partial-Short-Rest threshold, and one-hour-
per-interruption extension rule.

The policy contains no actor/world/campaign ID, current minute, elapsed/remaining time, event ID,
activity log, rest status, chosen resource, Hit-Die count/roll, ability score, HP/maximum, slot,
effect, item, attunement, route, or outcome. Exact benefit tokens are confirmed at the permanent-
ID boundary and name owner interfaces rather than source prose.

### Recording behaviour, result, and effects

Static validation/readback returns canonical policy/entity ID, key, version, source reference,
and the closed Short/Long declarations with zero effects. A valid policy does not authorise anyone
to rest, advance time, spend a Hit Die, regain HP, end temporary HP, reduce Exhaustion, recover a
slot/resource, or change preparation.

### Invariants, failure behaviour, and non-goals

- Entity key/version and component key/version agree exactly; a correction is a distinct reviewed
  successor, never an in-place mutation of a policy possibly cited by a future rest receipt.
- Unknown/duplicate kind, missing/extra/malformed rule field, wrong source, noncanonical ordering,
  incompatible duration, or extra data rejects unchanged.
- Reads are deterministic and effect-free; they do not inspect creature, class, campaign, inventory,
  clock, events, actions, HP, conditions, or resources.

### Slice 1 implementation sequence

1. Re-read source registry/content-definition conventions, core clock, Features 14/16/27/29/31,
   and existing catalog ID vocabulary; confirm whether an existing immutable policy owner can be
   extended rather than introduced.
2. Pause at the permanent-ID/source-vocabulary confirmation boundary. Confirm the exact entity ID,
   component/procedure IDs, source locators, benefit tokens, and policy revision rule.
3. Author the schema, procedure, standard policy entity, reader/validation path, and focused tests
   together. Store no copied SRD prose or runnable consequence payload.
4. Prove valid readback, source/key/version mismatch, bad duration/constraint/ordering, extra data,
   immutability, replay, zero-effect isolation, and catalog query-back.
5. Run focused tests, `roleplay validate catalog`, the full suite, and `git diff --check`; write a
   receipt and stop. Do not begin active rests or recovery in this slice.

### Slice 1 acceptance matrix

| Case | Exact assertion |
| --- | --- |
| Source policy | One active standard policy reports Short 60-minute and Long 480-minute declarations with exact SRD provenance. |
| Distinct kinds | Short and Long retain their distinct interruption/benefit/activity/restart declarations; neither is inferred from the other. |
| Closed/immutable data | Wrong key/version/source, bad duration, duplicate/unknown kind, missing or extra field, reordered canonical lists, in-place mutation, or duplicate same version rejects unchanged. |
| Isolation | Reads leave all actors, root clock, HP, temporary HP, conditions, resources, items, class state, events, and campaign data byte-identical. |
| Determinism | Equivalent reads return byte-identical policy data, make no random call, and select no player-facing rest route. |
| Repository | Focused tests, disposable catalog validation, full suite, diff check, and query-backs pass; no persistent import occurs. |

### Slice 1 exit gate

Slice 1 is verified only after the immutable standard policy has closed source-cited data,
rejection/immutability/isolation evidence, catalog validation, repository checks, and a receipt.
Stop before adding a rest state, a clock reaction, a rest action, or any recovery consequence.

## Later recovery and consumer map

```text
standard rest policy
├─ clock-bound rest episode ───────────────────────────────> Feature 33
│  ├─ Initiative / spell / damage / exertion evidence ─────> authoritative source owners
│  └─ focused Short-Rest evidence ─────────────────────────> Feature 29 attunement
├─ Short-Rest completion
│  ├─ Hit-Die pool / class facts ──────────────────────────> CH4 + Feature 27
│  ├─ Constitution modifier / one roll ────────────────────> Feature 2 + roll receipt owner
│  ├─ HP increase ─────────────────────────────────────────> Feature 16 healing
│  └─ explicit feature-resource recharge ──────────────────> class/species/item owner
├─ Long-Rest completion
│  ├─ full HP and temporary-HP expiry ─────────────────────> Feature 16
│  ├─ Exhaustion -1 ───────────────────────────────────────> Feature 14
│  ├─ spent Hit Dice and feature/slot recovery ────────────> Feature 33 + 27/31 owners
│  └─ reduced ability/HP maximum restoration ──────────────> future modification owner
└─ accepted elapsed-time consequence ──────────────────────> Feature 37 / Feature 32
```

## Plan-quality audit

- One rest/recovery capability with source, clock, interruption, and consequence boundaries: yes.
- Core-world time, event-subscription limits, healing, Exhaustion, class, spell, and attunement
  ownership were inspected: yes.
- Every unresolved operational requirement expands to a named leaf or blocked parent: yes.
- Static policy, active episode, elapsed-time evidence, interruption, individual recovery owners,
  and downstream consumers remain distinct: yes.
- One lowest implementation slice exists: **Slice 1 immutable standard-rest policy**.
- No runtime game artifact was created by this planning pass: yes.

## Plan-change rule

Revise before implementation if the core clock changes its event evidence, the platform gains a
generic active-state fan-out contract, Initiative/casting/damage event shapes change, CH4/Feature
27 chooses a different Hit-Die model, Feature 16 supplies a different full-recovery interface, or
an ability/maximum modification owner is introduced. Do not work around any such change with a
second clock, polling loop, caller-supplied time/interruption/recovery/roll, direct state write,
global resource reset, copied class rule, or generic scheduler.

