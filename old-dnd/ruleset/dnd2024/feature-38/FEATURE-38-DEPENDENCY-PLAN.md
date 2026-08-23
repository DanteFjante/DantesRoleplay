# Feature 38 dependency plan — social attitude and Influence

> **D&D implementation reference:** When this work includes D&D 2024 mechanics, inspect [Foundry VTT dnd5e](https://github.com/foundryvtt/dnd5e) before designing or coding. Use it as a licensed engineering reference—not a direct dependency or rules authority—while retaining the exact `source.dnd2024.srd-5.2.1` locator and all applicable MIT, CC-BY, and asset-license notices for any reused material.

Status: **Planned; Slice 1 is an immutable, source-cited social-interaction policy catalog and is
the next and only authorised implementation pass.**
Last updated: 2026-08-21

## Execution rule

This is planning only. It creates no runtime procedure, component, entity, mechanic, fixture,
migration, action, event, subscription, campaign state, attitude, cooldown, or check result. A
later implementation pass re-reads the current Feature 3 ability-check, Feature 12 action-budget,
Feature 13 conditions, world-clock, campaign/world actor, and authorization contracts; confirms
every permanent ID and source vocabulary; validates a disposable catalog import; records a receipt;
and stops after one accepted slice.

## Target capability

For a specific creature's attitude toward a specific player character, a trusted GM can record the
source-defined attitude and adjudicate an Influence attempt as willing, unwilling, or hesitant;
the hesitant path later resolves the correct source-defined check, DC, advantage/disadvantage, and
24-hour same-approach lockout without turning NPC narrative into a personality simulator.

### Included

- Immutable source policy for attitudes, influence approaches, willingness branches, default DC,
  attitude circumstances, and post-failure cooldown vocabulary.
- Later target-owned, player-specific social-attitude records and an administrative GM-adjudication
  route that does not copy NPC personality, faction, secret, quest, or campaign state.
- Later effect-free Influence admission/check evidence that derives the permitted ability/skill,
  target Intelligence DC, attitude/condition circumstance, and action requirement from trusted
  state and policy.
- Later atomic outcome handling: source-authorized completion evidence and an active cooldown that
  derives its expiry from the existing root clock.

### Excluded

- LLM or rules-engine generation of dialogue, personality, motives, ideals, bonds, fears, lies,
  secrets, consent, emotional state, relationships, romance, persuasion narrative, or GM
  willingness judgement.
- A generic NPC/monster/character identity, faction allegiance, hostility-in-combat, quest result,
  campaign reference, world fact, access-control system, or a duplicate character/creature model.
- A caller-supplied check total, modifier, DC, target Intelligence, success, attitude effect,
  cooldown expiry, time, action spend, condition effect, or raw structural effect.
- Combat-side hostility, Charm spell resolution, language comprehension/telepathy, animal
  classification, movement/range/visibility, Help, group checks, opposed checks, social combat,
  bargaining/economy, and a player-facing dialogue UI.
- Directly forcing a creature to act, changing a quest/fact, sharing a secret, granting a reward,
  creating an encounter, or narrating an NPC response. Those outcomes remain trusted GM/campaign
  decisions after a social result.

## Official source basis

The fixed source is `source.dnd2024.srd-5.2.1`, *System Reference Document 5.2.1* (Wizards of the
Coast LLC, 2025-05-01, CC-BY-4.0): [Playing the Game > Social Interaction and Actions, PDF
pp. 9–10](https://media.dndbeyond.com/compendium-images/srd/5.2/SRD_CC_v5.2.pdf) and [Rules
Glossary > Attitude, Friendly, Hostile, Indifferent, and Influence, PDF pp. 176, 181–183](https://media.dndbeyond.com/compendium-images/srd/5.2/SRD_CC_v5.2.pdf).

- A creature has a starting attitude **toward a player character**: Friendly, Indifferent, or
  Hostile. Friendly predisposes it to help, Indifferent neither helps nor hinders, and Hostile
  views the character unfavorably. Indifferent is the default attitude of a monster.
- The GM decides whether a described/roleplayed urging is willing, unwilling, or hesitant. Willing
  and unwilling branches require no check; only hesitant uses an ability check.
- The permitted hesitant approaches are Charisma (Deception, Intimidation, Performance, or
  Persuasion), and Wisdom (Animal Handling) for gently coaxing a Beast or Monstrosity. The default
  DC is the greater of 15 and the target's Intelligence score. Friendly gives Advantage and Hostile
  gives Disadvantage on the influence check.
- On a hesitant success the creature does as urged; on failure the influencer waits 24 hours, or
  a GM-set duration, before urging it in the same way. This plan supports only the fixed source
  24-hour branch until a separately governed GM-duration policy exists.

## Planning inventory and overlap result

| Inquiry | Repository evidence and decision |
| --- | --- |
| Ability checks | `mechanic.dnd2024.check.ability` is the sole seeded fixed-DC raw/named-skill D20 owner. It derives character skill proficiency from `dnd2024.character-level` and `dnd2024.skill-proficiencies`; F38 must compose it, not copy its roll, modifier, or skill logic. |
| Target Intelligence | `dnd2024.abilities` is creature-owned and derives ability modifiers, but the current ability-check child cannot receive a parent-derived scalar DC. F38 needs a ratified derived-result-to-child-input seam or a Feature-3-approved social context extension. |
| Action economy | Feature 12 owns the combat Action allowance and deliberately does not identify actions. The source says Influence is an Action, but noncombat/campaign action timing and atomic check-to-spend composition are not yet an owner. |
| Conditions | Feature 13 owns Charmed instances and explicitly defers the charmer's influence advantage to Feature 38. F38 may read source-bound Charmed evidence; it cannot apply/clear Charmed or duplicate its condition list. |
| Attitude scope | The SRD is directional and player-character-specific. An attitude cannot live as one global `friendly`/`hostile` flag on an NPC, monster, encounter side, faction, or player character. |
| World/campaign NPC facts | World actors/motives, campaign references, and story facts are narrative/source owners. They are context for GM roleplay, never a condition from which F38 infers willingness, an attitude, or an outcome. |
| Time | `game.core.world.clock` is the only elapsed-time coordinate. It can advance directly or through declared journeys, but current subscriptions cannot fan arbitrary clock changes into dynamic social records. A cooldown must therefore be read as active from its recorded start/expiry against the root clock, not copied to another scheduler. |
| Authorization | No accepted campaign-host/social-adjudication authority is found. A later attitude write or Influence disposition must not pretend that an arbitrary player is the GM; its trusted-host route remains a confirmation dependency. |
| Existing social owner | Searches find no D&D attitude, influence, persuasion-result, social cooldown, or NPC social-state owner. Existing generic world/campaign relationships do not implement this ruleset-specific directional mechanic. |

## Recursive dependency analysis

~~~text
Feature 38: social attitude and Influence                                      [blocked parent]
├─ SRD social vocabulary and source policy                                     [implemented basis]
├─ creature abilities and seeded ability-check owner                           [implemented: Features 2–3]
├─ conditions and source-bound Charmed state                                   [implemented: Feature 13]
├─ root world clock                                                            [implemented: core world time]
├─ combat Action allowance                                                     [implemented: Feature 12]
├─ GM/campaign/world narrative records                                         [implemented but deliberately not social mechanics]
├─ immutable social-interaction policy                                         [missing Slice 1 leaf]
├─ directional social-attitude state and administrative route                  [blocked: policy + trusted-host authority]
├─ social context reader (attitude/Charmed/approach/target facts)              [blocked: state + language/type semantics]
├─ Influence admission and effect-free check evidence                          [blocked: context + Feature-3 derived-input composition]
├─ atomic Action/check/outcome composition                                     [blocked: action timing + dynamic child-result binding]
├─ source-fixed failed-attempt cooldown                                        [blocked: outcome + clock-scoped actor linkage]
├─ attitude-change/GM outcome handoff                                          [blocked: trusted-host authority]
└─ expanded social exceptions and consumers                                    [blocked: accepted core lifecycle]
~~~

The only independent leaf is immutable social-interaction policy. It establishes source vocabulary
without deciding a creature's attitude, reading a motive, rolling, spending an Action, or creating
a social relationship.

## Dependency and ownership decisions

1. **Attitude is directed, not a creature label.** A later `dnd2024.social-attitudes` record is
   target-owned and contains canonical entries keyed by exactly one `towardEntityId`: the player
   character toward whom the target has Friendly, Indifferent, or Hostile attitude. The target
   does not receive a global social alignment; the player character never stores a mirrored copy.
   Missing entry means no recorded attitude and fails a mechanical Influence attempt. Explicit
   Indifferent is a real recorded source decision, not an inferred default.
2. **Roleplay judgement remains with the trusted GM.** A later administrative attitude/disposition
   route records only an already-adjudicated closed attitude or willingness branch plus provenance.
   It accepts no narrative transcript, free-text motive, hidden knowledge, outcome, check total,
   or effect list. It cannot determine what an NPC wants, whether a request aligns with it, or how
   it fulfills a request; those are roleplay/campaign decisions outside a rules mechanic.
3. **Policy distinguishes source fact from GM discretion.** The immutable policy owns the closed
   three-attitude vocabulary, five influence approaches, willing/unwilling/hesitant branch names,
   fixed 15 floor, friendly/hostile circumstance declarations, and source 1,440-minute cooldown.
   It contains no actor, target, attitude entry, prompt, decision, DC, Intelligence, time, or
   outcome. A later extension for a GM-set duration needs an explicit time/authority policy rather
   than accepting a caller's arbitrary duration.
4. **One social context reader owns the cross-actor derivation.** It reads the influencer, target,
   directional attitude entry, source-bound Charmed instance, immutable policy, and any confirmed
   creature-type/language fact. It returns only a closed trusted context: allowed approach,
   selected ability/skill, target Intelligence-derived default DC, attitude state, derived
   circumstances, and any inapplicability reason. It stores none of those derived values and does
   not roll, spend, or mutate attitude.
5. **Influence reuses the ability-check owner exactly once.** F38 supplies a Feature-3-approved
   trusted context to `mechanic.dnd2024.check.ability`; it never recreates skill proficiency, D20
   selection, conditions, advantage arithmetic, modifier lists, or replay envelope. Current child
   composition cannot turn target Intelligence/context into the child's scalar `dc`, so this is a
   genuine platform/Feature-3 confirmation boundary—not permission to pass caller DC or copy the
   check.
6. **Friendly/Hostile and Charmed advantage are state-derived.** A later context reader produces
   reserved, auditable social circumstance evidence. It must merge with Feature 13's own
   condition resolver through the approved Feature-3 path, preserving advantage/disadvantage
   cancellation. The caller can never claim Friendly, Hostile, or `charmed-by-target` advantage.
7. **Cooldown is a keyed failure fact, not a timer.** A future target-owned cooldown entry is keyed
   by `(towardEntityId, approach)` and records only a validated world-clock minute/revision and the
   source-fixed expiry. An Influence admission reads the one root clock and rejects an unexpired
   same-approach entry. No scheduler, duplicate clock, background subscription, or blanket
   “cannot talk to this NPC” state is created; other approaches and other characters remain
   distinct.
8. **Result is not campaign consequence.** A willing/unwilling decision or successful check is
   auditable influence evidence. Only a trusted GM/campaign owner decides whether to change
   attitude, reveal information, transfer an item, accept a quest, avoid combat, or take any
   other story/world effect.

## Confirmation boundaries

| Decision | Required confirmation before implementation |
| --- | --- |
| Policy | Exact component/procedure/entity IDs, attitude/approach/branch vocabulary, source locators, 24-hour representation, and immutable revision rule. |
| Attitude record | Target/influencer role eligibility, component shape, canonical ordering, absent/explicit-Indifferent semantics, state record/correct/retire path, provenance, and cross-world/campaign policy. |
| Trusted adjudication | GM/host identity and authority verification, disposition/attitude update route, audit evidence, stale-state guard, and no-free-text/narrative-data boundary. |
| Social context | Target Intelligence requirement, creature type/language applicability, Charmed source matching, circumstance namespace, attitude ordering, and diagnostic result form. |
| Check composition | Feature-3 extension or platform result-binding interface, exact child role/input path, circumstance merge/cancellation, seed ownership, and one-roll proof. |
| Action/timing | Whether social Influence spends a combat Action only in an encounter, a noncombat action abstraction, active-turn/target scope, and atomic check/spend failure order. |
| Cooldown | Root world/campaign linkage, key/expiry representation, clock revision/staleness rules, retry/replay behavior, cleanup, and later GM-duration extension. |
| Outcomes | What a success/willing/unwilling result permits a GM to record, how outcome/attitude changes stay separate, and which campaign/world owners consume it. |

## Slice order and stop gates

| Order | Slice | Starts only when | Exit gate |
| ---: | --- | --- | --- |
| 1 | Immutable social-interaction policy | Permanent vocabulary and source locators confirmed. | Policy reads deterministically with zero actor, attitude, time, action, roll, condition, campaign, or event effects. |
| 2 | Directional attitude state and administrative reader/writer | Slice 1 and trusted-host authority confirmed. | One target records an explicit attitude toward one eligible character without creating a mirrored/global attitude or story outcome. |
| 3 | Effect-free social context reader | Slices 1–2, source-bound Charmed semantics, and target eligibility/type/language decision. | Reader returns an auditable closed applicability/DC/circumstance context with zero random calls or effects. |
| 4 | Influence admission and check evidence | Slice 3 and ratified Feature-3 derived-input composition. | Hesitant attempt calls the existing ability-check owner exactly once; willing/unwilling branches make no roll. |
| 5 | Action and fixed-cooldown lifecycle | Slice 4, Feature-12/noncombat action timing, root clock, and atomic composition. | Failed hesitant attempt records one keyed 1,440-minute lockout and never partially spends/rolls/writes. |
| 6 | Trusted outcome/attitude handoff | Slices 2–5 and campaign/GM authority decision. | A qualified host can record an allowed separate social/campaign outcome with no automatic narrative consequence. |
| 7 | Source exceptions and consumer expansion | Slices 1–6 plus each owner review. | Animal Handling, language/telepathy, Charm spells, GM duration, and later social consumers reuse the stable interfaces. |

## Slice 1 — immutable social-interaction policy

### Runtime artifacts

- A confirmed immutable `dnd2024.social-interaction-policy` component/schema and governing
  static-definition procedure.
- One versioned `content.dnd2024.social-interaction-policy.standard.v1` entity with fixed
  `source.dnd2024.srd-5.2.1` provenance.
- Focused catalog validation/tests only. No actor relationship, attitude, GM disposition, social
  context, ability check, Action spend, cooldown, condition write, world-clock read/write,
  campaign/world consequence, event, or routing phrase.

### Governing contracts and source locator

Immediately before implementation, re-read `procedure.system.create-feature`, the source registry
and static-content conventions, `procedure.mechanic.dnd2024.check.ability`,
`procedure.mechanic.dnd2024.turn-budget`, Feature 13's Charmed boundary, and
`procedure.game.core.world.time`. Use the fixed SRD source and the Social Interaction/Influence
locators above. Confirm permanent component/procedure/entity IDs and every canonical token at the
semantic boundary; no future actor-state shape is implied by this catalog policy.

### Data/input contract and required state

The policy is closed and immutable. It declares stable key/version/source reference; canonical
attitude order `friendly`, `indifferent`, `hostile`; canonical approach declarations mapping
`deception`, `intimidation`, `performance`, `persuasion`, and `animal-handling` to their
source-defined ability/skill and applicability category; willingness branch vocabulary; default
DC floor 15; friendly/hostile circumstance declarations; and a fixed `1440`-minute
same-approach cooldown declaration.

It contains no actor/entity ID, target, player character, NPC, creature type, language, alignment,
attitude record, willingness result, narrative text, target Intelligence, final DC, skill
proficiency, roll/circumstance outcome, source condition, Action budget, clock/current minute,
cooldown entry, campaign, quest, item, secret, effect, duration override, or code. Exact ordering
and declared compatibility are part of validation; a later GM-duration variant is a successor
policy, not an optional free-form field.

### Recording behavior, result, and effects

Static validation/readback returns canonical policy/entity ID, key, version, source reference,
and closed vocabulary with zero effects. It cannot mark a creature Friendly/Indifferent/Hostile,
choose whether an NPC is willing, call a check, grant social advantage, alter a condition, consume
an Action, read/advance a clock, impose a lockout, or change a campaign/world record.

### Invariants, failure behavior, and non-goals

- Entity key/version and component key/version agree; correction creates a successor entity and
  never mutates a policy future attitude records or receipts may cite.
- Wrong source/key/version, unknown/duplicate/reordered attitude or approach token, wrong
  ability/skill/category mapping, malformed floor/circumstance/cooldown declaration, missing
  required category, or extra field rejects unchanged.
- Reads inspect no creature, conditions, character-level, skills, action budget, world clock,
  campaign, world actor, quest, or encounter state; they are deterministic and make no random
  call.

### Slice 1 implementation sequence

1. Re-read governing contracts and search the catalog for social, attitude, influence, Friendly,
   Hostile, Indifferent, persuasion, cooldown, and policy owners before creating any permanent ID.
2. Stop at the permanent-ID/source-vocabulary confirmation boundary. Confirm entity/component/
   procedure IDs, canonical ordering, exact five approach declarations, source locators,
   1,440-minute value, and immutable revision policy.
3. Author schema, procedure, standard policy entity, read/validation path, and focused tests
   together. Store no copied SRD prose, actor/target state, executable predicate, or future
   outcome payload.
4. Prove valid readback; invalid key/version/source/token/order/mapping/floor/cooldown/extra data;
   immutability; replay; and zero-effect isolation.
5. Run focused tests, `roleplay validate catalog`, the full suite, and `git diff --check`; write a
   receipt and stop. Do not begin attitude state or Influence resolution.

### Slice 1 acceptance matrix

| Case | Exact assertion |
| --- | --- |
| Source policy | One active standard policy returns exact source provenance, three attitudes, five approach mappings, three willingness branches, floor 15, attitude circumstances, and 1,440-minute cooldown declaration. |
| Closed/immutable data | Wrong key/version/source, unknown/duplicate/out-of-order attitude/approach/branch, wrong ability-skill/applicability mapping, malformed floor/circumstance/cooldown, missing/extra field, in-place rewrite, or duplicate same version rejects unchanged. |
| Separation | Attitudes, GM adjudication, checks, Charmed, Actions, clocks, cooldowns, creature types/languages, and campaigns are policy vocabulary only; none creates mutable game state. |
| Isolation | Reads leave creatures, actor relationships, character levels/skills, conditions, budgets, clocks, encounters, campaign/world records, events, and audits byte-identical. |
| Determinism | Equivalent reads are byte-identical, make no random call, select no player-facing phrase, and expose no narrative content. |
| Repository | Focused tests, disposable catalog validation, full suite, diff check, and catalog query-back pass; no persistent import occurs. |

### Slice 1 exit gate

Slice 1 is verified only after the immutable policy has closed source-cited data,
rejection/immutability/isolation evidence, catalog validation, repository checks, and a receipt.
Stop before attitude state, trusted adjudication, a D20 check, Action spending, cooldown state, or
campaign outcome.

## Social resolution and consumer map

~~~text
social-interaction policy
├─ directional target → player-character attitude state ────────> Feature 38
├─ trusted GM willingness/attitude adjudication ────────────────> Feature 38; campaign authority
├─ social context reader
│  ├─ target Intelligence ──────────────────────────────────────> Feature 2 abilities
│  ├─ Charmed source matching ──────────────────────────────────> Feature 13
│  ├─ type/language applicability ──────────────────────────────> confirmed creature/language owners
│  └─ derived Friendly/Hostile/Charmed circumstances ───────────> Feature 3 approved context seam
├─ existing ability-check D20 envelope ─────────────────────────> Feature 3
├─ Action admission/spend ──────────────────────────────────────> Feature 12 or approved noncombat action owner
├─ failed-attempt cooldown ─────────────────────────────────────> Feature 38 + core root clock
└─ success/outcome handoff ─────────────────────────────────────> trusted GM/campaign/world owner
~~~

## Plan-quality audit

- One capability—directional attitude plus source-bounded Influence—with explicit narrative and
  campaign non-goals: yes.
- Attitude state, GM judgement, source policy, D20 derivation, Action spending, cooldown time,
  conditions, and campaign outcome have distinct owners: yes.
- The graph expands all missing parents to one independent Slice-1 leaf: yes.
- Slice 1 has closed data, source, versioning, failure/isolation behavior, implementation
  sequence, acceptance matrix, and an all-or-nothing exit gate: yes.
- No runtime game artifact was created during this planning pass: yes.

## Plan-change rule

Revise before implementation if a campaign-host authorization owner is accepted, Feature 3 gains a
general derived-context input interface, creature type/language ownership changes, Feature 13
changes Charmed source semantics, or a compatible directional relationship-data model is already
confirmed. Do not store a global NPC attitude, infer willingness from personality/faction/alignment,
accept caller DC/outcome/cooldown/time/effects, use attitude as combat hostility, or turn a
successful check into an automatic quest, secret, reward, or compelled action.
