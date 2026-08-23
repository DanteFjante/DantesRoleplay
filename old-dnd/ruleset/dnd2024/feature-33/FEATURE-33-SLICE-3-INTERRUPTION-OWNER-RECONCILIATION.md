# Feature 33 Slice 3 dependency tree — authenticated rest interruption and resumption

> **D&D implementation reference:** Inspect [Foundry VTT dnd5e](https://github.com/foundryvtt/dnd5e) before designing or coding this mechanic. Use it as a licensed engineering reference—not a direct dependency or rules authority—while retaining the exact `source.dnd2024.srd-5.2.1` locator and all applicable MIT, CC-BY, and asset-license notices for any reused material.

Status: **Planning only; blocked by named event-owner contracts and confirmation.**  
Owner: `ruleset/dnd2024/feature-33/FEATURE-33-DEPENDENCY-PLAN.md`, Slice 3  
Ruleset alignment: **dnd2024-owned**  
Source: `source.dnd2024.srd-5.2.1`, *Rules Glossary > Short Rest* (PDF p. 186) and *Long Rest*
(PDF p. 184).

## Outcome and non-goals

One active Standard Rest episode can react only to authoritative evidence that its creature rolled
Initiative, cast a non-Cantrip spell, took damage, or accumulated one hour of walking or physical
exertion. A Short Rest is ended without benefits. A Long Rest preserves only the source-defined
interruption/resumption facts; recovery, Hit Dice, resource recharge, party rests, scheduling,
caller-supplied interruption facts, and generic activity logging remain outside this slice.

## Existing owners and evidence

| Concern | Owner | State | Evidence |
| --- | --- | --- | --- |
| Active/ready episode and world membership | Feature 33 Slice 2 | Verified | `FEATURE-33-SLICE-2-RECEIPT.md`; `dnd2024.rest-episode` and `dnd2024.rest.world` contain no interruption state. |
| Selected receiver routing | Platform E8 Slices 1–2 | Verified | Accepted payload-role binding and bounded scoped fan-out; consumer behavior remains absent. |
| Damage to a named target | Feature 9 weapon-damage application | Verified, usable input | `mechanic.dnd2024.weapon-damage.apply` emits schema-validated `dnd2024.damage.dealt` with authoritative `targetId`. |
| Rolling Initiative | Feature 11 encounter Initiative/turn lifecycle | Missing event contract | Initiative order and turn lifecycle write state but declare no event naming the creature that rolled Initiative. |
| Non-Cantrip spell cast | Feature 32 spell resolution | Missing cast capability/event | The current spell-resolution profile is immutable content only; it cannot prove a caster, cast spell, or whether the spell is a Cantrip. |
| Walking/physical exertion | Core world time/travel | Missing actor-bound exertion evidence | Route travel advances the scoped clock and relocates a traveller, but `game.core.world.clock.advanced` names only the world and cannot prove the rest creature walked or how much qualifying exertion it accumulated. |

## Dependency tree

```text
Feature 33 Slice 3: authenticated interruption and resumption       [blocked parent]
├─ existing rest episode and scoped clock evidence                    [verified: Feature 33 Slice 2]
├─ damage interruption                                                 [ready input: dnd2024.damage.dealt.targetId]
│  └─ one Feature 33 payload-bound reaction and rest-state transition [blocked by Slice 3 state confirmation]
├─ Initiative interruption                                             [missing Feature 11 event leaf]
│  └─ exact creature/encounter evidence emitted only on a real Initiative roll
├─ non-Cantrip spell interruption                                     [missing Feature 32 cast-event leaf]
│  └─ caster and derived spell kind/Cantrip evidence; no caller flag
├─ one-hour walking/physical-exertion interruption                    [missing time/activity evidence leaf]
│  └─ actor-bound, source-action duration; not inferred from world-clock advance
└─ rest interruption/resumption state transition                      [awaiting semantic confirmation]
   ├─ Short Rest termination and later fresh-start rule
   ├─ Long Rest interruption counter, added-duration, and immediate-resume rule
   └─ event ordering, replay, rollback, and unrelated-rest isolation
```

## Decisions and conflicts

1. `dnd2024.damage.dealt` is the sole currently usable interruption input. Its `targetId` is
   authoritative and can use E8's accepted payload-role binding; Feature 33 must not infer damage
   from an HP component replacement or accept a target in caller input.
2. Starting an encounter, advancing a turn, or observing a turn budget is not equivalent to a
   creature rolling Initiative. Feature 11 must own a distinct authoritative Initiative evidence
   event before it can interrupt a rest.
3. A spell profile or spell identity is not a cast. Feature 32 must own the cast root and derive
   the Cantrip/non-Cantrip distinction from trusted spell content before it can emit interruption
   evidence.
4. A root-clock advance is not proof that any particular creature walked or exerted itself.
   Treating every clock advance as exertion would incorrectly interrupt unrelated rests. The time
   owner must expose actor-bound qualifying duration from the action that caused it; Feature 33
   alone applies the SRD one-hour threshold to its rest episode.
5. Slice 2 intentionally supports only `active` and `ready`. Slice 3 therefore requires a
   confirmed `dnd2024.rest-episode` schema/lifecycle revision and must not add an undocumented
   Boolean, free-text reason, caller duration, second clock, or generic scheduler.

## Ordered leaves

| Order | Leaf | Depends on | Exit gate |
| ---: | --- | --- | --- |
| 1 | Interruption envelope and episode-transition confirmation | Existing Slice 2 state and E8 evidence | Confirm the exact event identities/payload roles, Short-Rest termination, Long-Rest resumption state, and actor-bound exertion accumulation boundary. |
| 2 | Feature 11 Initiative evidence | Row 1 | A completed Initiative roll emits one schema-validated creature/encounter event with atomic rollback evidence. |
| 3 | Feature 32 non-Cantrip cast evidence | Row 1 and a real casting root | A completed cast emits one schema-validated caster/spell event whose Cantrip classification is derived from content. |
| 4 | Core actor-bound exertion evidence | Row 1 | Approved walking/exertion actions emit trusted creature/world/minutes evidence without turning clock advancement into a scheduler. |
| 5 | Feature 33 Slice 3 implementation | Rows 2–4 and an active implementation document | Every named source interrupts only its resting creature; resumption is source-correct; replay/failure rolls back atomically. |

## Confirmation gates

Before any event type, component revision, subscription, mechanic, procedure, migration, or
fixture is authored, confirm:

1. the exact event IDs and closed payload fields owned by Features 11, 32, and core activity;
2. the rest-episode lifecycle/schema change, including the Short-Rest terminal action and the
   Long-Rest interruption/resume representation;
3. whether Feature 33 records qualifying exertion minutes from authenticated activity events or
   consumes an already-thresholded owner event; and
4. the causal ordering when an action both advances time and produces interruption evidence.

## Planning receipt

- Runtime artifacts created: none.
- Catalog/runtime behavior changed: none.
- Lowest implementation leaf: none; the shared event/lifecycle semantic gate remains unconfirmed.
