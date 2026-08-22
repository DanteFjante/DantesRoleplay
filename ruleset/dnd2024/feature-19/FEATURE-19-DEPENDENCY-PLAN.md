# Feature 19 dependency plan — reactions in play

> **D&D implementation reference:** When this work includes D&D 2024 mechanics, inspect [Foundry VTT dnd5e](https://github.com/foundryvtt/dnd5e) before designing or coding. Use it as a licensed engineering reference—not a direct dependency or rules authority—while retaining the exact `source.dnd2024.srd-5.2.1` locator and all applicable MIT, CC-BY, and asset-license notices for any reused material.

Status: **Planned; no Feature 19 runtime slice is authorised. Feature 20's spatial-movement contract and a reaction-composition decision are the next missing dependencies.**
Last updated: 2026-08-20

## Execution rule

This is a planning-only artifact under `AGENTS.md`, `procedure.system.create-feature`, and the Terra planning guide. It creates no runtime artifact: no procedure, component, mechanic, event type, subscription, fixture, migration, or live game state.

The planning guide calls for a live inventory and current `procedure.system.create-feature` read. The MCP runtime was unavailable for this pass, so the inventory below is repository/catalog evidence only. An implementation pass must repeat those live reads, resolve catalog/database drift, and complete exactly one verified lowest slice before it stops. It must not import the persistent catalog/database except at an explicit integration or release boundary.

## Target capability

When a creature voluntarily leaves another creature's reach during an encounter, the threatened creature can spend its available Reaction to make one melee weapon or Unarmed Strike immediately before the departure; no other movement or reaction rule is accidentally treated as an opportunity attack.

### Included

- The common, universally available Opportunity Attack reaction only.
- One closed, per-reactor semantic trigger produced before a mover leaves reach.
- The existing Reaction budget as the sole availability/consumption authority.
- Reuse of the existing effect-free weapon-attack resolver for a legal melee weapon attack once a platform composition path is confirmed.
- Exact transaction, timing, seed, event-chain, and audit evidence.
- Explicit refusal of teleportation, forced movement, Disengage, unavailable reaction, unseen mover, no-longer-eligible reach transition, and invalid/corrupt spatial state.

### Excluded

- Position, speed, movement allowance, difficult terrain, reach derivation, Disengage state, teleportation, and forced-movement classification. Feature 20 owns their authoritative model and the movement event. Feature 21 owns obstacle/cover geometry; Feature 34 owns the SRD visibility condition.
- Unarmed Strike resolution: Feature 22 owns it. The first Feature 19 vertical slice may support melee weapons only.
- Weapon ownership, equipping, range, properties, damage, Hit Point changes, conditions, and effects of a hit. Features 8, 9, 15–17, 21–24 own those boundaries.
- Ready, Counterspell, spell reactions, held actions, multiple reaction choices/prompts, feat/class/item-triggered reactions, and reaction replacement. They need a later generic trigger-choice protocol and are not hidden inside the opportunity-attack subscription.
- Automatic NPC choice or a player prompt. A creature may decline a legal opportunity attack; choice ownership remains a later interaction feature.

## Official source basis

The registered source is `source.dnd2024.srd-5.2.1`: *System Reference Document 5.2.1* (Wizards of the Coast LLC, 2025-05-01, CC-BY-4.0), [Playing the Game > Combat > Opportunity Attacks, PDF p. 14](https://media.dndbeyond.com/compendium-images/srd/5.2/SRD_CC_v5.2.pdf), and [Rules Glossary > Opportunity Attacks, Reaction, Ready, PDF pp. 184–186](https://media.dndbeyond.com/compendium-images/srd/5.2/SRD_CC_v5.2.pdf).

- A Reaction answers its defined trigger, can occur on another creature's turn, and is unavailable again until the reacting creature's next turn starts.
- The universal opportunity trigger is a creature that the reactor can see leaving the reactor's reach using its action, Bonus Action, Reaction, or one of its speeds.
- Spending the Reaction permits exactly one melee weapon attack or Unarmed Strike against the provoking creature, immediately before it leaves reach.
- Teleportation and movement that does not use the mover's movement, action, Bonus Action, or Reaction do not provoke. The separate Disengage action also prevents provocation.

“Immediately before” makes this a structural timing boundary, not an after-the-fact notification. The pre-departure snapshot, eligibility decision, reaction, and subsequent movement must share one atomic root chain.

## Planning inventory and overlap result

| Inquiry | Evidence and conclusion |
| --- | --- |
| Reaction allowance | Feature 12 is verified. `dnd2024.turn-budget` has a single Boolean reaction allowance, refreshes it at the participant's own turn start, and `mechanic.dnd2024.turn-budget.spend` permits an off-turn reaction for a validated encounter participant. It deliberately decides neither trigger nor action cost. |
| Turn timing | Feature 11's encounter lifecycle derives the active participant from the initiative order. It has no spatial positions, movement step, pre-departure hook, or interrupted-action representation. |
| Attack resolution | `mechanic.dnd2024.weapon-attack` is the single effect-free resolver for an attack against final AC. It intentionally excludes turns, target legality, range, ownership, damage, and persistence. Feature 19 must not duplicate its D20, proficiency, or natural-roll logic. |
| Movement/reach owner | Feature 20 is planned in full and owns speed, distance, and reach as attack preconditions. Searches find world travel/containment movement but no D&D encounter position, reach, departure, teleport, forced-movement, or Disengage model. |
| Visibility owner | The SRD requires the reactor to see the mover. Feature 21 supplies obstacle geometry, while Feature 34 owns light, senses, hidden/Invisible state, and the resulting “can see” fact. |
| Event reactions | E1 is verified. `procedure.event.react` exposes accepted event payload and declared projections atomically, but `procedure.subscription.create` currently requires an event mechanic to declare no child mechanics. |
| Dynamic event roles | Reactions can inspect `ctx.event.entityIds` and `ctx.eventEntities`, but a subscription's ordinary roles are fixed at registration. No existing contract binds a child attacker's/target's role or child input from a per-event payload. |
| Existing subscriptions | Catalog subscriptions are world reactions; no D&D opportunity-attack event/subscription or general triggered-ability selection protocol exists. |

## Verified existing dependencies

| Dependency | Current evidence |
| --- | --- |
| Reaction refresh and spend | Feature 12's verified `dnd2024.turn-budget` component and `mechanic.dnd2024.turn-budget.spend` support exactly one off-turn Reaction for a valid encounter participant. |
| Encounter turn identity | Feature 11 provides initiative-order lifecycle and Feature 12 validates roster membership, but neither represents spatial movement. |
| Melee attack arithmetic | Feature 8's `mechanic.dnd2024.weapon-attack` provides seeded, effect-free attack evidence against final Armor Class. |
| Atomic event chains | E1's `procedure.event.react` provides accepted-event ordering, deterministic derived seeds, rollback, and event ledger causation. |
| Subscription limitation | `procedure.subscription.create` rejects reaction mechanics that declare children, preventing a reaction from composing the Feature 8 attack resolver. |
| Official rule source | `source.dnd2024.srd-5.2.1` identifies the current SRD and canonical source locators. |

## Recursive dependency analysis

```text
Feature 19: opportunity attacks in play
├─ SRD reaction/opportunity-attack timing and exclusions               [implemented source basis]
├─ encounter membership + Reaction refresh/spend                        [implemented: Features 11–12]
├─ seeded effect-free weapon attack                                     [implemented: Feature 8]
├─ atomic event chain/audit                                             [implemented: E1]
├─ legal melee-attack selection                                         [blocked: Feature 20/22/equipment boundaries]
├─ encounter position, reach, sight, and movement classification       [missing owner: Feature 20 plan]
│  ├─ pre-departure position transition                                 [missing Feature 20 leaf]
│  ├─ per-reactor leave-reach eligibility event                         [blocked: spatial model]
│  ├─ Disengage/teleport/forced-movement exclusion facts                [blocked: Feature 20 and later action owners]
│  └─ seeing mover at the trigger                                       [blocked: Features 21 and 34]
├─ reaction composition with dynamic event bindings                     [missing platform decision]
│  ├─ effect-free child eligibility in event middleware                 [missing platform leaf]
│  ├─ event payload -> child roles/input binding                         [missing platform leaf]
│  └─ deterministic nested execution/audit semantics                    [blocked parent]
└─ opportunity-attack lifecycle                                         [blocked parent]
   ├─ eligible-trigger record/decline policy                             [blocked: spatial trigger + choice policy]
   ├─ consume Reaction and resolve one melee attack                      [blocked: composition + legal attack selection]
   └─ resume/depart atomically                                          [blocked: pre-departure movement boundary]
```

The lowest newly discovered work is not an opportunity-attack mechanic. It is (1) a Feature 20 dependency plan that defines the authoritative spatial state and pre-departure event, and (2) a platform-owner decision on bounded child composition in event reactions. Neither belongs to Feature 19, and neither may be bypassed with caller-supplied coordinates, distances, target ids, or a copied attack formula.

## Dependency and ownership decisions

1. **Features 20, 21, and 34 own eligibility facts.** Feature 20 owns position, reach, movement mode, origin, destination, and voluntary movement; Feature 21 supplies obstacle geometry; Feature 34 supplies the final visibility fact. Feature 19 consumes closed inputs and must not infer sight, forced movement, or teleportation.
2. **A trigger is per possible reactor, not a generic “moved” event.** One movement can leave multiple creatures' reaches. The Feature 20 producer must emit one ordered opportunity-trigger candidate per eligible reactor, carrying closed `moverId`, `reactorId`, source pre-departure position/reach snapshot or opaque transition identity, and eligibility/exclusion provenance. A generic event with a list of reactors lets a subscription choose an arbitrary party and cannot model one Reaction each.
3. **Timing belongs to movement, not the reaction.** Feature 20 must keep the mover at its origin while trigger candidates and their accepted reactions execute, then apply the position change only when the chain succeeds. A post-move reaction cannot meet the “right before” rule and must not repair this with a second position effect.
4. **The Reaction budget remains sole consumer.** Feature 19 invokes or composes the Feature 12 spend path exactly once only after the trigger is still valid. It cannot set `reaction: false` directly, make its own allowance component, or refresh by round.
5. **Feature 8 remains sole attack resolver.** The opportunity path supplies only a legal melee selection and reactor/target identities through a confirmed binding protocol. It cannot reproduce D20 selection, ability/proficiency arithmetic, AC, natural-roll handling, or damage.
6. **A legal opportunity attack is not automatic attack choice.** The first vertical slice needs a closed decision policy. Pending interaction support, it should create a single, bounded resolution request whose accept/decline is authenticated to the reactor and expires at the pre-departure chain boundary. A reaction that always attacks changes the player-facing rule; one that accepts arbitrary target/weapon input enables forgery. This choice protocol needs its own confirmed owner before a player-facing slice is authorised.
7. **No Ready or Counterspell generalisation.** Ready retains an action-time declaration, a future trigger, optional movement, spell resource expenditure, and concentration; Counterspell needs spell casting/levels and timing. Feature 19's opportunity path may establish reusable platform primitives only after their contracts are confirmed, but it must not claim those rules.

## Confirmation boundary

Before any Feature 19 runtime work, ratify these with the owning plans:

| Decision | Required confirmation |
| --- | --- |
| Spatial contract | Feature 20's encounter-position and speed/reach model, entity inclusion, missing/corrupt semantics, and normal write/move path. |
| Visibility input | Feature 21's geometry plus Feature 34's senses/light/hidden-state contract supply a stable “reactor can see mover” fact at the pre-departure snapshot. |
| Movement timing | A pre-departure movement transaction that can emit ordered per-reactor candidate events before the position effect becomes visible. |
| Exclusion facts | Feature 20/action-owner vocabulary for voluntary movement, speed/action/Bonus Action/Reaction movement, teleportation, forced movement, and Disengage. |
| Trigger payload | Exact versioned opportunity-trigger schema, fixed participant ids, transition identity, source locator/provenance, and ordering when several reactors qualify. |
| Dynamic reaction bindings | Whether event middleware may compose an explicitly effect-free child; how fixed event fields bind child roles and closed input; deterministic seed, error, rollback, chain-limit, and audit behavior. |
| Choice/decline | The owner of an authenticated, bounded “take or decline this reaction now” decision, including expiry and simultaneous eligible reactors. |
| Melee selection | Feature 20/22/23's authoritative definition of a legal melee weapon or Unarmed Strike and the first-slice restriction if Unarmed Strike remains unavailable. |

No permanent Feature 19 id, event type, subscription, component, procedure revision, mechanic, fixture, or public action is authorised before this boundary is reviewed.

## Slice order and stop gates

| Order | Slice | Starts only when | Exit gate |
| --- | --- | --- | --- |
| 0 | Spatial/event and platform confirmation | Feature 20 plan and platform-owner plan exist. | Pre-departure per-reactor event and safe dynamic child binding have closed contracts; no Feature 19 runtime artifact. |
| 1 | Opportunity trigger candidate | Slice 0 and Feature 20's movement foundation are verified. | A voluntary, visible leave-reach transition produces ordered, auditable candidate state/events before departure and excludes all non-provoking movement. Stop before attack resolution. |
| 2 | Accepted/declined melee weapon opportunity attack | Slice 1, choice protocol, Feature 12 spend reuse, dynamic bindings, and legal melee selection are verified. | One accepted candidate spends one reaction and resolves one Feature-8 melee weapon attack before movement; decline preserves the reaction and permits movement. |
| 3 | Multi-reactor ordering and downstream proof | Slice 2 plus Feature 9 damage application and condition/death owners as applicable. | Several reactors resolve/decline in canonical order with rollback and no duplicated attacks; attack damage remains separately owned. |
| 4 | Deferred reaction families | Separate feature plans. | Ready, Counterspell, spells, Unarmed Strike, feat/class/item reactions remain explicitly outside this feature. |

## Slice 1 — opportunity trigger candidate

### Runtime artifacts

Subject to the confirmation boundary, Feature 20—not Feature 19—revises its movement contract and creates the closed semantic trigger event. Feature 19 may add a read-only eligibility consumer only if Feature 20 assigns it that responsibility. Exact ids, schema, categories, and subscriptions remain unproposed until the spatial contract is accepted.

### Governing contracts and source locator

Re-read `procedure.mechanic.dnd2024.turn-budget`, `procedure.event.react`, `procedure.subscription.create`, the accepted Feature 20 movement/reach contract, and the source locators above immediately before any write.

### Data/input contract and required state

Movement receives no caller-supplied reactor list, reach, visibility answer, provocation flag, reaction availability, target, attack total, or outcome. The spatial owner validates complete encounter membership, origin/destination, actor-authorised movement classification, effective reach/sight, and Disengage state before it emits a candidate.

Candidate state/event is missing when no reactor qualifies. It contains fixed mover/reactor identities and an immutable transition identity. It never embeds mutable actor components, free-form reasons, a weapon id, an attack DC/total, dice, or a “must attack” Boolean.

### Resolution/recording behavior

For every qualifying reactor in the confirmed canonical order, Feature 20 emits a candidate while the mover is still at the origin. A candidate represents eligibility only; it neither consumes Reaction nor selects/executes an attack. Teleportation, forced movement, moving inside reach, leaving a creature not seen by the reactor, and active Disengage emit no candidate. Failure of a candidate/schema/eligibility event aborts the root movement instead of committing an un-auditable partial spatial change.

### Result, invariants, and non-goals

The movement result identifies the transition and its candidate ids/count without reporting untrusted derived attack data. The position effect happens after the reaction window, exactly once. This slice does not offer a player an attack, spend a Reaction, or resolve a hit.

### Implementation sequence and acceptance matrix

Perform fresh live inventory/searches and contract reads; dry-run and commit identical catalog writes; query each artifact; then use disposable encounter fixtures. Prove:

| Case | Exact assertion |
| --- | --- |
| One qualifying departure | One candidate names the exact mover/reactor and is ledgered before the final position transition. |
| Multiple reactors | One candidate per eligible reactor in fixed documented order; no merged list or arbitrary subscription choice. |
| Boundary | Leaving 5-foot and extended reach on exactly the first out-of-reach step qualifies; movement within reach does not. |
| Exclusions | Teleport, forced move, Disengage, unseen mover, different encounter, and invalid movement source emit none. |
| State failure | Missing/corrupt positions, reach, sight, budget, encounter, or movement classification rejects before position mutation. |
| Atomicity/replay | Fixed seed/input reproduce event order and positions; malformed candidate/event or subscriber failure leaves origin position and all state unchanged. |
| Routing/cleanup | Movement phrases select the movement owner, not attack/spend; fixtures are returned to baseline or removed through normal audited paths. |

### Exit gate

Feature 20 demonstrates an atomic pre-departure, ordered opportunity-candidate boundary against real encounter spatial state. Stop immediately; no attack or Reaction-spend behavior is added in this slice.

## Slice 2 — accepted or declined melee weapon opportunity attack

### Runtime artifacts

Subject to the confirmed platform/choice contracts: one Feature 19 opportunity-resolution mechanic, an event subscription to the Feature-20 candidate event, and a bounded decision record/procedure owned by the interaction platform. The resolver composes only `mechanic.dnd2024.turn-budget.spend` and `mechanic.dnd2024.weapon-attack` if the platform explicitly permits their required effect-free/one-effect roles. Exact ids remain proposed, not authorised.

### Governing contracts and source locator

Re-read Feature 20's candidate event, `procedure.event.react`, `procedure.subscription.create`, Feature 12's spend contract, Feature 8's weapon-attack contract, the approved dynamic-binding contract, and the decision-owner contract.

### Data/input contract and required state

Only the authenticated reactor can accept or decline its own unexpired candidate. Candidate identity is opaque and single-use. The caller cannot submit mover id, reactor id, coordinates, reach, Disengage, weapon id unless equipment owns and validates selection, ability, modifiers, AC, reaction Boolean, dice, total, hit, or effects.

The resolver validates that the candidate, movement transition, encounter membership, spatial snapshot, and reaction budget remain valid. A decline produces zero budget/attack effects. An acceptance has exactly one legal melee weapon selection under the accepted equipment/reach rules; no legal selection means an explicit decline/failure according to the choice contract, never a fabricated unarmed attack.

### Resolution/recording behavior

On accepted candidate, consume exactly one Feature-12 Reaction then invoke the existing Feature-8 resolver once with fixed reactor/mover roles and the validated melee selection. The composed root records parent/child seeds and structured attack evidence. The resolver does not apply damage, move either creature, or modify a turn state. Any child, subscription, event, or position transition failure rolls back the whole movement chain.

### Result, invariants, and non-goals

Return candidate id, decision, reactor/mover ids, Reaction-spend result, and frozen Feature-8 attack result. Never claim damage or an HP change. A declined, invalid, expired, unavailable, or already-resolved candidate has exact documented no-effect behavior and cannot be replayed to obtain an additional attack.

### Implementation sequence and acceptance matrix

| Case | Exact assertion |
| --- | --- |
| Accepted legal attack | Exactly one Reaction spend and one Feature-8 weapon-attack child occur before final departure; no direct turn/position/damage effect is proposed by Feature 19. |
| Decline | Exactly zero attack/spend effects; departure proceeds once. |
| Spent reaction | Candidate cannot resolve an attack; origin/departure behavior follows the confirmed movement policy with no state mutation beyond the legitimate move. |
| Closed input | Foreign/stale/duplicate candidate, wrong actor, extra fields, forged role/target/weapon/reach/roll/outcome, and unknown decision all reject unchanged. |
| Attack boundaries | Feature-8 AC/proficiency/natural/replay evidence is preserved through the composed reaction; Feature 19 adds no competing math. |
| Ordering/atomicity | Candidate reaction precedes position effect; child failure or invalid event rolls back spend, attack evidence, and move together. |
| Routing/cleanup | Opportunity-specific phrases do not capture normal weapon attacks, movement, or budget spending; disposable fixtures are restored/removed. |

### Exit gate

One accepted or declined, melee-weapon opportunity attack is reproducible and atomic at the Feature-20 departure boundary, uses the existing budget and attack owners, and proves no duplicated state or algorithm. Stop before multi-reactor policy, Unarmed Strike, damage integration, or other reaction families.

## Slice 3 — multi-reactor ordering and downstream proof

This slice starts only after Slice 2 and the damage/condition owners needed by the selected fixture are verified. It proves the canonical candidate order, each reactor's independent one-Reaction limit, decline/accept mix, and root rollback. It may compose Feature 9's existing damage parent only through a separately confirmed action/choice path; Feature 19 itself still does not own damage. It must also prove that an attack that changes later eligibility follows the confirmed snapshot/order policy rather than silently recomputing arbitrary reactors. Stop before generic triggered abilities.

## Plan-quality audit

- One player-facing capability and explicit boundaries: yes.
- Official source/version/locators: yes; official SRD PDF pp. 14 and 184–186.
- Existing owners/overlaps searched: yes; turn budget, event middleware, subscriptions, weapon attack, and movement were inspected.
- Missing dependencies expanded: yes; Feature 20 spatial timing, dynamic reaction composition, choice/decline, and legal melee selection are explicit.
- One lowest next assignment: yes — plan Feature 20's spatial-movement contract and obtain the platform composition decision; no Feature 19 implementation is authorised.
- Closed input, timing, atomicity, ordering, negative cases, replay, routing, and cleanup: specified for each dependent slice.
- Runtime payload/source duplication: none; this is a behavioral plan only.

## Plan-change rule

Stop and revise before implementation if Feature 20 selects a different spatial model, if the platform cannot bind dynamic event roles/input into a bounded child, if a reaction decision cannot be represented atomically, or if legal melee selection depends on an unplanned equipment/Unarmed Strike owner. Do not substitute caller-supplied geometry or copy a weapon-attack algorithm to make the plan appear unblocked.
