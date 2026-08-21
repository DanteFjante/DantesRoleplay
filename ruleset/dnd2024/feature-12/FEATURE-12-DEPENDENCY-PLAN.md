# Feature 12 dependency plan — turn action economy

Status: **Verified in repository mode**  
Last updated: 2026-08-20

## Target capability

During an encounter, a GM can see which of a participant's turn resources — its Action, Bonus
Action, Reaction, free object interaction, and movement allowance — remain, spend one through an
audited action that refuses to overspend, and have the whole allowance restored automatically when
that participant's next turn begins.

The budget is state, not permission. Feature 12 tracks and spends resources; it does not decide what
a given rule costs, and it does not make any existing resolver require one. A GM who never calls the
spend action sees no change in behavior anywhere else.

### Included

- One participant-owned turn-budget component holding only what the SRD makes consumable between
  defined turn-based refresh points.
- Restoration of the full allowance at the start of the owning participant's turn, inside the
  Feature 11 transition that already decides whose turn began.
- One explicit spend transition per resource, rejecting a spend of an exhausted resource without
  changing state.
- Movement allowance as a remaining distance in feet, decremented by a declared amount.
- Deterministic, effect-exact, replayable transitions with no randomness.

### Excluded

- **Which action a given rule costs.** Feature 8's and Feature 9's resolvers are effect-free by
  contract; nothing observes them. Making an attack cost an Action changes their transaction shape
  and their exit gates, and is a separate reviewed change.
- The twelve named SRD actions as distinct behaviors. Feature 12 spends an Action; it does not know
  what the Action was used for.
- Speed as a derived fact, distance, difficult terrain, reach, and every positional legality
  (Feature 20).
- `Incapacitated` and the Speed-0 conditions as prohibitions (Feature 13, which revises this
  feature's spend mechanic rather than adding a parallel rule).
- Legendary and lair actions, multiattack routines, Ready, held actions, and delaying.
- Opportunity attacks and triggered abilities as behaviors (Feature 19); Feature 12 owns only the
  Reaction *allowance* they will consume.
- Turns outside an encounter, and any budget for an entity absent from the Initiative order.

## Official rule source

`source.dnd2024.srd-5.2.1`, *System Reference Document 5.2.1* (Wizards of the Coast, 2025-05-01,
CC-BY-4.0). The canonical and PDF URLs are on the live source entity.

| Locator | PDF | Rule this feature takes from it |
| --- | --- | --- |
| `Playing the Game > Combat > Your Turn` | p. 13 | One action per turn plus movement up to your Speed; move and act in either order, movement splittable. |
| `Playing the Game > Bonus Actions` | p. 10 | Only when a feature allows it, and only one per turn. |
| `Playing the Game > Reactions` | p. 10 | After taking a Reaction, another cannot be taken until the start of the creature's next turn. |
| `Playing the Game > Actions` | p. 9 | The closed action list and the general Action boundary. |
| `Playing the Game > Interacting with Objects > Time-Limited Object Interactions` | p. 12 | When time is short, including combat, a creature receives one free object interaction per turn; additional interactions require the Utilize action. |

The SRD gives no machine-readable rule for what a feature costs, so a cost is never inferred; a
spend is always explicit and audited. The source re-read confirms `freeInteraction` is a
once-per-turn allowance, so it remains in the component.

## Source and contract basis

| Authority | Evidence | Decision supplied |
| --- | --- | --- |
| Repository workflow | `AGENTS.md`; `procedure.system.create-feature`; `procedure.system.verify` | Repository-mode authoring, proportional planning, risk-based verification, and the confirmation boundary below. |
| Projection and composition | `procedure.mechanic.projection` | Contents carry identity and slot only; a role's declared components are projected onto the role entity. Reaching a contained entity's components requires a child with `forEachContentsOf` and a `$item` role binding. Child input is inherited, a static literal, or an object-valued top-level key of the parent input — never a sibling result or a projected value. |
| Fan-out precedent | `mechanic.dnd2024.encounter-initiative-order` | The live worked example of `forEachContentsOf` over encounter contents, including its per-`$item` binding. |
| Turn lifecycle | [Feature 11 plan](../feature-11/FEATURE-11-DEPENDENCY-PLAN.md) | Owns `dnd2024.encounter-turn-state`, the start/advance/end transitions, roster-equality validation, and the derived active participant. **Verified.** |
| Encounter roster and order | [Feature 5 plan](../feature-05/FEATURE-5-DEPENDENCY-PLAN.md); `CatalogFeature5Tests` | Containment is the roster; `dnd2024.encounter-initiative-order` is the sole ordered snapshot, bounded at 100 participants. |
| Closed-writer pattern | `procedure.mechanic.dnd2024.hit-points`; [Feature 6 plan](../feature-06/FEATURE-6-DEPENDENCY-PLAN.md) | `record`/`correct` modes, closed input, fixed `sourceRef`, rejection before effect, corrupt records rejected rather than repaired. |
| Effect semantics | `procedure.world.change`; `EffectApplier` | `component.add` faults on a present pair, `component.remove` on an absent one; `component.set` is an upsert and faults on neither. |
| Selection safety | E2; `MechanicStoreTests.Player_match_phrases_exclude_rules_that_only_share_generic_description_words` | Authored match phrases outrank incidental tokens; exact phrase collisions remain an authoring risk needing routing tests. |

No existing owner was found. Searches over `catalog/` for action, bonus action, reaction, movement
allowance, turn budget, action economy, and spend return no component, procedure, or mechanic —
`catalog/components/` holds only abilities, armor-class, character-level,
encounter-initiative-order, hit-points, saving-throw-proficiencies, skill-proficiencies, source,
weapon-proficiencies, and weapon-profile.

## Ownership and confirmation boundary

`procedure.mechanic.dnd2024.turn-budget` becomes the owner of per-turn resource state and its
transitions. Feature 11 remains the sole owner of `round`, `turnIndex`, `status`, and the derived
active participant; the budget stores none of them.

**The Slice 1 permanent ids and meanings were confirmed through implementation.** Later-slice ids
remain separate public-surface boundaries.

| Artifact | Proposed meaning |
| --- | --- |
| `procedure.mechanic.dnd2024.turn-budget` | Governing contract for per-turn resource state, its restoration point, and its spend rules. Category `ruleset.dnd2024.core.combat.economy`. |
| `dnd2024.turn-budget` | Closed participant-owned component: four availability Booleans, remaining and maximum movement feet, and a fixed `sourceRef`. |
| `mechanic.dnd2024.turn-budget.write` | Administrative `record`/`correct` admission and correction path. Not the restore path and not the spend path. |
| `mechanic.dnd2024.turn-budget.read` | Effect-free per-participant reader designed for fan-out composition. Direct selection is harmless and returns the same diagnostic result. |
| `mechanic.dnd2024.turn-budget.spend` | The single normal consumer path; one resource per call. |
| `mechanic.dnd2024.encounter-turn.start` / `.advance` revisions | Feature 11 transitions gain the fan-out child and the restoration effect. A second effect joins each transition. |
| `creature.dnd2024.feature-10.hero`, `creature.dnd2024.feature-10.training-target` | Fixture revision: both gain a valid budget in Slice 1, one slice before Slice 2 makes an absent budget a hard failure. |

**Three ownership decisions worth confirming with the ids**, because each rules out a design that
looks correct:

1. **The budget belongs to the participant, not the encounter.** A creature's remaining Reaction is
   a fact about the creature. Five budgets inside one encounter component would make that component
   a second roster and would lose the budget when the encounter ended.

2. **Restoration happens inside the turn transition, reached by a fan-out child.** Two other routes
   were ruled out on evidence. A *reaction* to the turn-state change cannot work: the accepted event
   touches the encounter alone, so the newly active participant is not in `ctx.eventEntities`, and
   `fixedRoleEntityIdsJson` binds at registration while a roster is per encounter. The transition's
   *own projection* cannot work either: contents carry identity and slot only, so adding
   `dnd2024.turn-budget` to the `encounter` role would project it onto the encounter. What remains is
   a declared child with `forEachContentsOf: "encounter"` and `roleBindings: {"participant": "$item"}`
   — the shape `mechanic.dnd2024.encounter-initiative-order` already uses over the same contents. The
   cost is honest: the child runs once per participant though one result is used. It is effect-free
   and consumes no randomness, so the cost is projection work, not correctness.

3. **A Reaction is exempt from the acting-participant check, not from roster membership.** A
   Reaction is taken in response to a trigger, normally on another creature's turn — which is why
   the SRD refreshes it at the start of *your* next turn. Gating all five resources on "only on your
   turn" would make the Reaction allowance unspendable and would contradict Feature 19's dependency
   on it. The Boolean permits one Reaction between refresh points; it does not claim a literal
   one-per-round limit. The subject must still be a distinct member of the encounter's validated
   containment roster and Initiative order.

## Closed component and input contracts

~~~text
dnd2024.turn-budget
{
  action: boolean,                  // still available this turn
  bonusAction: boolean,
  reaction: boolean,
  freeInteraction: boolean,
  movementRemainingFeet: integer,   // 0 <= remaining <= maximum
  movementMaximumFeet: integer,     // multiple of 5, 0..1000
  sourceRef: { sourceId: "source.dnd2024.srd-5.2.1",
               locator: "Playing the Game > Actions; Bonus Actions; Reactions; Interacting with Objects; Combat > Your Turn" }
}
~~~

Field order is canonical and fixed, so two exports of one database are byte-identical. The
cross-field bound and the multiple-of-5 rule are enforced by the writer, because JSON Schema draft
2020-12 has no portable cross-property comparison — the same reason recorded in
`dnd2024.hit-points.schema.json`.

~~~text
mechanic.dnd2024.turn-budget.write   { mode: "record" | "correct",
                                       action, bonusAction, reaction, freeInteraction,
                                       movementRemainingFeet, movementMaximumFeet }

mechanic.dnd2024.turn-budget.read    {}          // role: participant

mechanic.dnd2024.turn-budget.spend   { resource: "action" | "bonusAction" | "reaction"
                                                 | "freeInteraction" | "movement",
                                       feet? }   // required iff resource is "movement"
~~~

Every input is closed. No caller supplies a `sourceRef`, participant id, encounter id, round,
turnIndex, resource history, derived value, or `effects`. `feet` is a positive multiple of 5, at
most 1000, and is rejected with any resource other than `movement`.

**Missing and empty differ.** An absent `dnd2024.turn-budget` means the creature has not been
admitted to the action economy; every spend fails with a distinct reason. A present budget with
`action: false` means the Action has been spent this turn. No path treats absence as "everything
available".

**`movementMaximumFeet` is declared scaffolding.** Nothing stores a creature's Speed. Rather than
descend into a Speed component that Feature 20 owns properly, the maximum is recorded state.
Removal criterion: Feature 20 replaces it with a value derived from Speed and revises this contract.

## Dependency order and slices

~~~text
Feature 12: turn action economy
├─ SRD turn/bonus/reaction/interaction/move budget rules             [source basis]
├─ encounter roster and Initiative order                             [verified: Feature 5]
├─ atomic actions, closed input, audit, replay                       [verified: kernel]
├─ fan-out child over contained participants                         [verified: Feature 5 mechanic]
├─ phrase-aware mechanic selection                                   [verified: E2]
├─ whose turn it is, and when a turn begins                          [BLOCKED: Feature 11, all slices]
└─ per-participant turn budget                                       [parent]
   ├─ Slice 1: budget component, writer, fixtures                    [missing leaf]
   ├─ Slice 2: fan-out reader and restoration at turn start          [blocked: Slice 1]
   └─ Slice 3: validated spend of one resource                       [blocked: Slice 2]
~~~

| Order | Slice | Starts only when | Exit gate |
| --- | --- | --- | --- |
| 1 | Budget component, administrative writer, fixtures | Feature 11 verified; ids confirmed | A participant is admitted with and can have corrected a complete closed budget through its only normal write path; every rejection leaves state byte-identical; both Feature 10 fixtures carry one and every unaffected Feature 10/11 assertion still passes. |
| 2 | Fan-out reader and restoration at turn start | Slice 1 verified | Starting preflights every roster member and restores the first participant; advancing restores exactly the newly active participant in the same atomic action. Feature 11 lifecycle invariants remain intact while its exact-effect assertions are deliberately revised from one effect to two. |
| 3 | Validated spend of one resource | Slice 2 verified | Each resource spends once per turn and then fails; an off-turn Reaction succeeds while the other four fail; a restored turn makes a spent resource available again. |

## Slice 1 — budget component and administrative writer

| Artifact | Change |
| --- | --- |
| `procedure.mechanic.dnd2024.turn-budget` | New. Governs the component and the writer only; later slices revise it as they add transitions. |
| `dnd2024.turn-budget` definition and schema | New closed participant component. |
| `mechanic.dnd2024.turn-budget.write` | New `.md`/`.js` pair, scope `dnd2024-srd-5.2.1`, role `subject` declaring the component. Absence is legal and branched on — a role-level `Optional` flag is not used. |
| Feature 10 creature fixtures | Revised to carry a valid budget. |
| `CatalogFeature12Tests` | New focused fresh-import coverage. |

Behavior: validate closed input, then the existing component for `correct`; reject before
constructing an effect; consume no randomness; propose exactly one `component.add` (`record`) or
`component.set` (`correct`) carrying the complete seven-field object in canonical order; return
mode, the six values, the previous object (`null` for `record`), and the fixed `sourceRef`.
`record` never overwrites, `correct` never creates, and a corrupt existing record is rejected rather
than repaired. The writer touches no other component and no other entity.

### Slice 1 acceptance matrix

| Case | Exact expected result |
| --- | --- |
| Record and correct | `record` with a full allowance and `movementMaximumFeet: 30` applies one `component.add`; the queried component has exactly seven fields in canonical order with the fixed `sourceRef`. `correct` applies one `component.set`. |
| Boundaries | Maximums of 0, 5 and 1000 record; remaining equal to and one below maximum record; remaining one above it fails. |
| Differential | Two records differing only in `movementMaximumFeet` produce components differing in exactly that field. |
| Closed input | Missing, null, non-object root, wrong-case or unknown `mode`, string-typed Booleans, fractional/non-finite/negative feet, non-multiple-of-5 maximum, one extra key, and any supplied `sourceRef`/participant/encounter/round/turnIndex/effects each fail with zero effects. |
| Missing and existing state | `correct` against an absent component and `record` against a present one each fail with a distinct reason; original bytes unchanged. |
| Corrupt state | A stored missing field, extra field, wrong `sourceRef`, `remaining > maximum`, or malformed JSON is rejected by `correct` before any effect and is not repaired. |
| Determinism | Equivalent input produces byte-identical component data; no `ctx.randomInt` call. |
| State integrity | Before/after byte comparison on the subject and on one untouched sibling participant for every rejection. |
| Routing | `record turn budget` and `correct turn budget` select only this writer. `start encounter turns`, `advance the turn`, `spend my action`, `record hit points`, and `record armor class` neither select it nor are captured by it. |
| Fixture migration | Both Feature 10 fixtures carry a valid budget; every Feature 10 and Feature 11 assertion still passes; `roleplay validate catalog` reports the intended fixture revisions and nothing else. |
| Slice verification | Focused tests and `roleplay validate catalog` pass. No persistent import occurs. |

## Slice 2 — fan-out reader and restoration at turn start

| Artifact | Change |
| --- | --- |
| `mechanic.dnd2024.turn-budget.read` | New. Effect-free, static `{}` input, role `participant` declaring the budget; always returns `{participantId, present, valid, problem, budget}` for domain state, using `budget: null` when absent or corrupt. Composition/host failures still abort normally. Administrative/diagnostic match phrases only. |
| `mechanic.dnd2024.encounter-turn.start` / `.advance` | Revised: add the child `{"budgets": {"mechanicId": "mechanic.dnd2024.turn-budget.read", "roleBindings": {"participant": "$item"}, "forEachContentsOf": "encounter", "inheritInput": false, "input": "{}"}}`, then restore the acting participant's budget. |
| `procedure.mechanic.dnd2024.turn-budget`, `procedure.mechanic.dnd2024.encounter-turn-lifecycle` | Revised to describe the restoration point and the two-effect transition. |

Restoration sets the four Booleans to `true` and `movementRemainingFeet` to that participant's own
recorded `movementMaximumFeet`, as one full seven-field `component.set` carrying `movementMaximumFeet`
and `sourceRef` through unchanged. Each transition's effect count rises from one to exactly two, in
a fixed order, in one transaction. No other participant changes — a half-spent allowance is left as
it was until that participant's own next turn.

The reader never returns a failed child result for an absent or malformed budget, because
`MechanicComposer` aborts a parent on any failed child. It instead returns `present: false` or
`valid: false` plus a closed diagnostic `problem`, allowing the parent to make the lifecycle
decision. Projection/host/composition failures remain real failures and abort the parent action.

**Start is an all-roster admission boundary.** Before creating lifecycle state, the parent checks
that the fan-out produced exactly one reader result for every validated roster identity and that
every result is present and valid. Any missing, corrupt, duplicate, or unmatched budget rejects the
whole start action. This prevents an encounter from starting in a known state that must later jam
when an unadmitted participant becomes active.

**Advance validates the newly active participant.** An absent or corrupt budget on that participant
rejects the whole transition, including Feature 11's turn-state effect. A corrupt budget introduced
later on a non-acting participant is reported but does not block the current advance; the correction
path can repair it before that participant's turn.

**Reaction restoration follows the SRD refresh boundary.** Restoring `reaction: true` at the start
of each participant's turn permits one Reaction until the start of that participant's next turn.
No round counter is stored or inferred.

### Slice 2 acceptance matrix

| Case | Exact expected result |
| --- | --- |
| Start and advance | Start requires every roster participant to have one valid budget, then restores the round-1 index-0 participant only; advance restores the newly active participant only and leaves the outgoing participant's spent budget byte-identical. |
| Round wrap | A wrap restores correctly with no extra effect; a one-participant encounter restores on every advance. |
| Already full | A full budget is still rewritten to an identical value as exactly one effect, asserted rather than optimised away. |
| Admission and acting-participant failure | Start rejects an absent or corrupt budget anywhere in the roster. Advance rejects one on the newly active participant. In both cases the turn state remains byte-identical. |
| Non-acting participant after start | If a non-acting participant's valid budget is corrupted after start, the current advance is not blocked unless that participant is newly active. |
| Reader semantics | Missing and each corrupt-domain case return a successful child result with `present`/`valid`/`problem`/`budget` set consistently; a simulated host/composition failure still aborts the parent. |
| Feature 11 intact | Feature 11 lifecycle, roster, routing, replay, and historical-state assertions remain intact. Its start/advance exact-effect expectations are revised from one effect to two and explicitly re-accepted. |
| Determinism and routing | Replay is exact; no randomness; the reader's phrases capture no player phrase and neither transition's routing changes. |
| Slice verification | Focused tests and `roleplay validate catalog` pass. No persistent import occurs. |

## Slice 3 — validated spend of one resource

| Artifact | Change |
| --- | --- |
| `mechanic.dnd2024.turn-budget.spend` | New. Roles `subject` (budget) and `encounter` (`dnd2024.encounter-initiative-order`, `dnd2024.encounter-turn-state`, `includeContents: true`). |
| `procedure.mechanic.dnd2024.turn-budget` | Revised to describe spending, the acting-participant rule and its Reaction exemption. |

Validate before any effect: the complete budget and its `sourceRef`; the complete turn state;
`status` is `active`; roster and snapshot identities still match; and `subject` is a distinct member
of both. For `action`, `bonusAction`, `freeInteraction` and `movement`, `subject` must be exactly
`order[turnIndex].participantId` — derived, never supplied. `reaction` is exempt only from this
acting-participant equality check, per ownership decision 3.

A Boolean already `false`, or `feet` greater than `movementRemainingFeet`, fails with a distinct
reason and zero effects; partial movement is never silently truncated. Success proposes exactly one
`component.set` on `subject` with exactly one field changed, and reports the resource, its before
and after values, and — for movement — feet spent and remaining. No randomness, no other entity, no
condition, damage, attack, position, or event.

### Slice 3 acceptance matrix

| Case | Exact expected result |
| --- | --- |
| Spend once | Each of the five resources spends exactly once and then fails; the second attempt leaves bytes unchanged. |
| Movement bounds | Movement spends down to exactly zero; one foot-multiple beyond fails; `feet` of 0, a non-multiple of 5, a negative and a fractional each fail; `feet` with a Boolean resource fails, and its omission with `movement` fails. |
| Off-turn Reaction | A non-acting roster participant's Action, Bonus Action, free interaction and movement each fail and change nothing, **while its Reaction succeeds**. A second Reaction before its own next turn begins fails; that turn starting makes it available again. A subject outside the roster/order fails for every resource, including Reaction. |
| Invalid encounter state | A spend against an `ended` encounter fails; roster drift fails; an absent or corrupt budget or turn state fails. |
| Full cycle | Spend, restore, spend again across a turn cycle is deterministic and replays exactly. |
| Closed input and integrity | Every rejection applies zero effects and leaves the subject and one sibling byte-identical. |
| Routing | "spend my action", "use my bonus action", "use my reaction" and "move 15 feet" select the spend mechanic; Feature 11's lifecycle phrases and the Slice 1 writer's administrative phrases are unaffected. |
| Feature acceptance | Focused tests, `roleplay validate catalog`, the full suite once, and `git diff --check`. No guard tests or protocol walk: this feature changes no kernel code and no MCP surface. |

## Forward dependencies

| Concern | Owner | Handshake |
| --- | --- | --- |
| Making a rule cost a resource | A later reviewed consumer change | Feature 8/9 resolvers are effect-free by contract; changing that changes their exit gates. |
| `Incapacitated` and Speed-0 prohibitions | Feature 13 | Revises `mechanic.dnd2024.turn-budget.spend`; must not add a parallel spend rule. |
| Exhaustion's movement reduction | Feature 14 | Revises `mechanic.dnd2024.turn-budget.read` to also report the level, and the restore arithmetic to `max(0, maximum − 5 × level)`. One fan-out, one child. |
| Speed replacing `movementMaximumFeet` | Feature 20 | The declared removal criterion above. |
| Reaction triggers | Feature 19 | Consumes the allowance this feature creates; does not re-model it. |

## Completion boundary

Feature 12 is complete when a participant's allowance refreshes on its own turn, each resource can
be spent exactly once between its applicable refresh points, and an off-turn Reaction is the only
spend that can occur outside the acting participant's turn.
Record each slice's evidence in `FEATURE-12-SLICE-N-RECEIPT.md` and feature acceptance in
`FEATURE-12-IMPLEMENTATION-RECEIPT.md`. Stop before making any rule cost a resource; that needs its
own boundary and its own reviewed change.

## Plan-change rule

Stop and revise, rather than adapting in flight, if:

- Feature 11 ships a start or advance mechanic without `includeContents: true` on its `encounter`
  role. Ownership decision 2's fan-out has no other route to the acting participant.
- Feature 11 stores the active participant id rather than deriving it, making decision 1's
  separation a duplication instead.
- `procedure.mechanic.projection` or `MechanicComposer` changes either the successful structured
  child-result shape or the rule that genuine host/composition failures abort the parent action.
- The SRD source changes the confirmed once-per-turn free object interaction or Reaction refresh
  boundary.
- A repository search finds an existing turn-resource owner.
