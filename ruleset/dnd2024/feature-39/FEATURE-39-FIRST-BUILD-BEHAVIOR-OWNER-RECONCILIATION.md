# First-build behavior owner reconciliation — Human, Alert, and Savage Attacker

Status: **Planning-only; no runtime slice is authorised**  
Reviewed: 2026-08-21

## Purpose and boundary

This reconciliation executes the next-pass stop gate from
`character/feature-00/CHARACTER-FEATURE-00-OWNER-MAP-RECONCILIATION.md` for the ratified Human
Soldier Fighter. It determines whether the existing D&D 2024 owners can make the remaining Human
and Origin-feat benefits playable before CH3/CH4/CH5 are assigned.

It creates no catalog record, component, schema, mechanic, procedure, actor state, item, event,
MCP surface, or C# rule. D&D rule calculations, choices, timing, and outcomes remain catalog
JavaScript work; C# remains only the generic host.

## Source and fixture scope

| Selected result | Source ID and exact locator | Ratified fixture meaning |
| --- | --- | --- |
| Human Resourceful | `source.dnd2024.srd-5.2.1` — `Character Origins > Character Species > Human, PDF page 86` | A Human gains Heroic Inspiration after finishing a Long Rest. |
| Human Versatile / Alert | `source.dnd2024.srd-5.2.1` — `Character Origins > Character Species > Human, PDF page 86`; `Feats > Origin Feats > Alert, PDF page 87` | The selected Human Origin Feat is Alert. |
| Soldier Savage Attacker | `source.dnd2024.srd-5.2.1` — `Character Origins > Character Backgrounds > Soldier, PDF page 83`; `Feats > Origin Feats > Savage Attacker, PDF page 87` | The Soldier background supplies Savage Attacker. |

The scope is only the already-ratified Human Soldier Fighter. It does not make any other Human
choice, Origin Feat, background, class feature, reroll, rest policy, or character creation path
supported.

## Existing owners and evidence

| Concern | Current owner/evidence | State | Consequence |
| --- | --- | --- | --- |
| Player-character eligibility | CH1 `dnd2024.character.profile` | Verified | Heroic Inspiration may be held only by a valid profiled actor. |
| Selected Human identity | Feature 26 Slice 2 `dnd2024.selected-species` | Verified reference only | It records a trusted species definition; it does not grant a trait. |
| Heroic Inspiration held state | Feature 39 Slice 1 `dnd2024.heroic-inspiration` and `.grant` | Verified | One empty presence component may be added once. It neither proves Resourceful nor consumes a die. |
| Rest source/policy | Feature 33 Slice 1 `dnd2024.rest-policy` | Verified static data only | There is no active rest episode, completed-rest event, or recovery dispatch. |
| Alert and Savage Attacker identities | Feature 28 Slice 4 content definitions plus `dnd2024.feat-profile` | Verified static data only | The actor has no feat entitlement/receipt or executable benefit. |
| Initiative roll/order | Features 5 and 10 `mechanic.dnd2024.initiative.roll` and `mechanic.dnd2024.encounter-initiative-order` | Verified | Initiative accepts only caller-supplied Advantage/Disadvantage circumstances; its only derived modifier is current condition/exhaustion state. The resulting order is immutable once recorded. |
| Combat turns and sides | Feature 11 turn lifecycle; `dnd2024.encounter-sides` | Verified | Turns identify an active participant; sides do not represent a willing ally or authorise an Initiative swap. |
| Weapon damage | Feature 9 `mechanic.dnd2024.weapon-damage.roll` / `.apply` | Verified | It rolls one normal/critical weapon-damage expression. It has no feat proof, turn binding, optional reroll choice, or attack-result binding. |

## Reconciled dependency tree

```text
Playable remaining first-build behavior                              [blocked parent]
├─ Human Resourceful -> Heroic Inspiration after a completed Long Rest
│  ├─ one held instance                                               [verified: Feature 39 Slice 1]
│  ├─ selected Human evidence                                         [verified reference: Feature 26 Slice 2]
│  └─ authenticated completed Long Rest                               [blocked: Feature 33]
│     └─ active-rest dispatch / root-clock fan-out decision           [missing semantic gate]
├─ Alert
│  ├─ actor feat entitlement with immutable source provenance         [blocked: CH3 origin grant]
│  ├─ trusted Initiative proficiency contribution                     [missing owner/protocol]
│  └─ post-roll swap with one willing ally in the same combat         [blocked: ally-consent + provisional-order owner]
└─ Savage Attacker
   ├─ actor feat entitlement with immutable source provenance         [blocked: CH3 origin grant]
   ├─ confirmed weapon-hit/damage composition                         [missing parent action]
   └─ once-per-turn feature-use lifecycle                             [missing owner]
```

## Findings and ownership decisions

1. **Heroic Inspiration already has a state owner.** The earlier CH0 wording that it has no
   owner is now stale: Feature 39 Slice 1 provides its one-instance JavaScript grant recorder.
   That recorder is deliberately not a Resourceful trigger and cannot invent rest completion.
2. **Human Resourceful cannot be implemented next.** Feature 33 currently supplies immutable rest
   policy only. Its next planned slice is an active rest episode, which is blocked on a generic
   decision for how an accepted root-clock change finds every affected active rest safely and
   atomically. A Human-specific timer, polling loop, caller-provided completion flag, or direct
   Heroic-Inspiration grant would duplicate or bypass the rest owner.
3. **Static feat identity is not actor entitlement.** `dnd2024.feat-profile` attaches only to
   immutable content definitions. CH3 must own the selected background/species grant and immutable
   creation receipt before either feat behavior can infer that an actor has it. No generic feat
   array, Boolean, or copied rules payload is authorised by this reconciliation.
4. **Alert needs two later seams.** The Initiative JavaScript resolver has no trusted feature
   modifier input: it accepts only Advantage/Disadvantage and rejects caller-provided bonuses.
   Its order parent persists a final order, so Alert's voluntary same-combat swap must occur while
   the order is still provisional. Current encounter sides identify sides, not willing allies.
5. **Savage Attacker needs an atomic combat parent, not a damage patch.** The current weapon-damage
   JavaScript rolls exactly the normal/critical base expression, while the application parent
   accepts caller-confirmed hit/critical evidence. Neither proves an actor's feat, binds the
   action to an active turn, nor preserves the unused/used decision required by once per turn.
   Adding a reroll field to either mechanism would make Feature 9 own feat rules and would permit
   unauthorised rerolls.

## Ordered next work

| Order | Owner slice | Readiness | Exact exit gate |
| ---: | --- | --- | --- |
| 1 | **Platform E8 Slice 1 metadata confirmation** | Awaiting confirmation | The generic event-type and subscription metadata contract is fixed with no D&D behavior. |
| 2 | Platform E8 Slice 1 — exact payload role binding | Blocked by row 1 | One reaction receives one event-named entity through a declared role; mismatches roll back the root. |
| 3 | Platform E8 Slice 2 — bounded indexed fan-out | Blocked by Slice 1 | One scoped event selects a canonically ordered bounded active set and every reaction joins/rolls back atomically. |
| 4 | Feature 33 Slice 2 — active rest episode | Blocked by E8 Slice 2 | A single creature can have one validated clock-scoped episode with no duplicate clock or scheduler. |
| 5 | Feature 33 completion/recovery slices | Blocked by Slice 2 and named consequence owners | A completed Long Rest can produce authenticated, source-specific recovery evidence. |
| 6 | Feature 39 Slice 2 — Human Resourceful source grant | Blocked by row 5 and CH3 source selection | A verified selected Human receives one lawful Heroic Inspiration grant from completed-rest evidence; duplicate handling is explicitly owned. |
| 7 | CH3 origin entitlement/receipt | Blocked by all selected origin behavior owners | An actor can prove its selected Alert and Savage Attacker grants without copied behavior. |
| 8 | Alert Initiative contribution and provisional-swap tree | Blocked by row 7 | Proficiency and willing-ally swap each have one safe Initiative-order composition owner. |
| 9 | Savage Attacker combat/damage tree | Blocked by row 7 | A once-per-turn choice composes with one confirmed attack/damage parent and never changes Feature 9's generic damage contract. |

## The only immediate next pass

**Assignment:** Platform E8 Slice 1 metadata confirmation.

The next pass is planning only. Platform E8—not Feature 33—owns dynamic event role binding and
bounded indexed fan-out. Its Slice 1 must first fix how the event type declares eligible payload
entity fields and how a subscription persists one role-to-field mapping. It must not add a rest
component, rest subscription, scheduler, Human trait, Heroic Inspiration transition, or D&D rule.

Required reads for that pass:

1. `AGENTS.md`, `STATUS.md`, `KNOWN_ISSUES.md`, this reconciliation, and
   `ruleset/dnd2024/ROADMAP.md`.
2. `platform/e8/E8-DEPENDENCY-PLAN.md` and `platform/PLATFORM-ENABLING-FEATURES-ROADMAP.md`.
3. The event type, subscription, projection, event-router, effect-chain, and catalog import/export
   contracts reached by E8.
4. `ruleset/dnd2024/feature-33/FEATURE-33-DEPENDENCY-PLAN.md`,
   `docs/DEPENDENCY_TREE_AUTHORING.md`, `docs/FEATURE_IMPLEMENTATION_AUTHORING.md`, and
   `ruleset/dnd2024/TERRA-FEATURE-PLANNING-GUIDE.md`.

## Stop gate

Stop for confirmation after the E8 metadata contract is fully specified. It changes generic
subscription/event public semantics and is therefore a semantic boundary. Do not begin E8 Slice 1,
Feature 33 Slice 2, Resourceful, CH3, Alert, Savage Attacker, or any character-creation runtime
work until that decision has an accepted implementation document.

## Planning receipt

- Runtime artifacts created: none.
- Catalog/runtime files changed: none.
- Next runtime candidate: E8 Slice 1 after its metadata semantic gate is confirmed.
