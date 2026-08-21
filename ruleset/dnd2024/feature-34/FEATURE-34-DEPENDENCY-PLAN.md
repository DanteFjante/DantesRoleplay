# Feature 34 dependency plan — vision, light, hiding, passive Perception, and surprise

Status: **Planned; Slice 1 is an immutable, source-cited observation-policy catalog and is the next and only authorised implementation pass.**
Last updated: 2026-08-21

## Execution rule

This is planning only. It creates no runtime procedure, component, entity, mechanic, fixture,
migration, action, event, subscription, or game state. A later implementation pass re-reads the
current Feature 13 conditions, Feature 20 map/placement, Feature 21 geometry/sides, ability-check,
initiative, action-economy, item, species, and spell/effect contracts; confirms permanent IDs;
validates a disposable catalog import; records a receipt; and stops after one accepted slice.

## Target capability

For two placed creatures in a bounded encounter, the game can derive whether an observer can see
a subject from authoritative illumination, physical occlusion, senses, and conditions; resolve a
legal Hide attempt and its discoverability; derive a passive Perception score; and provide a trusted
surprise context to encounter Initiative without copying position, geometry, D20, condition, action,
or Initiative ownership.

### Included

- Immutable source policy for ordinary illumination, obscurement, special-sense semantics, Hide,
  passive Perception, and surprise vocabulary.
- Later encounter-scoped illumination/obscurement state, source-backed creature senses, and an
  effect-free observer-to-subject visibility result.
- A Hide root that validates sight/cover/action prerequisites, calls the D20 owner once, and
  creates/ends a paired Feature-34 hidden record plus Feature-13 Invisible condition atomically.
- Passive Perception and active find-hidden resolution through the existing Wisdom (Perception)
  check owner.
- A pre-Initiative surprise classifier that returns only trusted per-participant Initiative
  circumstances to the existing encounter-order owner.

### Excluded

- A duplicate map, grid, position, footprint, range, cover, line-of-effect, hostile-side, world
  light clock, item custody, spell effect, condition store, action budget, D20 roller, or Initiative
  order. Features 20, 21, 23, 32, 13, 12, 3, and 5 respectively retain them.
- Dynamic lighting UI, radiosity, arbitrary vector geometry, weather, sound propagation,
  secret-door/trap content, marching order, search UI, perception-based quest revelation, and
  player-data visibility/authorisation.
- Species, monster, item, feat, or spell grants of Darkvision/Blindsight/Tremorsense/Truesight;
  Features 26, 29, 31–32, and 35 declare grants through the accepted Feature-34 sense model.
- A generic "hidden" Boolean, caller-supplied visibility/cover/sense/result/Surprise flag, or
  permanent "surprised" condition.

## Official source basis

The fixed source is `source.dnd2024.srd-5.2.1`, *System Reference Document 5.2.1* (Wizards of the
Coast LLC, 2025-05-01, CC-BY-4.0): [Vision and Light, Hiding, and Combat > Initiative, PDF
pp. 11–13](https://media.dndbeyond.com/compendium-images/srd/5.2/SRD_CC_v5.2.pdf), [Character
Creation > Passive Perception, PDF p. 21](https://media.dndbeyond.com/compendium-images/srd/5.2/SRD_CC_v5.2.pdf),
and [Rules Glossary > Blinded, Blindsight, Hide, Invisible, Tremorsense, and Truesight, PDF
pp. 177, 183, and 189](https://media.dndbeyond.com/compendium-images/srd/5.2/SRD_CC_v5.2.pdf).

- Bright Light permits normal sight. Dim Light is Lightly Obscured and imposes Disadvantage on
  sight-reliant Wisdom (Perception) checks. Darkness is Heavily Obscured and makes an observer
  Blinded when trying to see into it. The system distinguishes illumination from fog or foliage.
- Blindsight works in range without physical sight, despite Darkness, Blinded, or Invisible, but
  not through Total Cover. Tremorsense pinpoints eligible contacts but is not sight. Truesight
  pierces Darkness and Invisibility within range. Specific sense grants and ranges remain their
  source owners' facts.
- Hide requires a DC 15 Dexterity (Stealth) check while Heavily Obscured or behind
  Three-Quarters/Total Cover and outside every enemy's line of sight. Success gives Invisible while
  hidden; its check total is the Wisdom (Perception) DC to find the creature. Hiding ends on a
  louder-than-whisper sound, finding, an attack roll, or a spell with a Verbal component.
- Passive Perception is `10 + Wisdom (Perception) check modifier`, including every applicable
  modifier, and represents general awareness when not actively looking. Surprise is not a durable
  condition: a combatant caught unaware at combat start has Disadvantage on its Initiative roll.

## Planning inventory and ownership result

| Inquiry | Evidence and decision |
| --- | --- |
| Conditions | Feature 13 stores effective Blinded, Deafened, and Invisible conditions and assigns sense-based auto-failure, Invisible visibility, and Hide interaction to Feature 34. It never stores a hidden DC/duration/cause. |
| D20 and skills | `mechanic.dnd2024.check.ability` owns seeded Dexterity (Stealth) and Wisdom (Perception) checks, abilities, skill proficiency, and circumstance merging. Feature 34 supplies trusted context only. |
| Initiative | The encounter-order parent fans out Initiative children from caller-provided participant input. It stores no surprise state and has no child-result-to-child-input path, so a derived surprise result cannot currently reach Initiative safely. |
| Action economy | Feature 12 owns the Action allowance but does not identify what an action does. Hide must compose one Action spend only after a source-specific action-composition contract is confirmed. |
| Position and map | Feature 20 owns tactical map, placement, and distance. Its Slice 2 is not implemented; Feature 34 cannot invent an origin, distance, room, or line. |
| Cover and sides | Feature 21 owns encounter side/hostility and physical cover/line-of-effect. Its cover reader must establish the obstruction result Feature 34 consumes; clear line of effect alone is not sight. |
| Items/light sources | Feature 23 owns item definitions/instances/custody, but no illumination emission/activity exists. A torch, lantern, or spell may delegate an emission to Feature 34 only after its own use/effect owner exists. |
| Senses and grants | Feature 26 defers Darkvision/Tremorsense/Hide to Feature 34; Feature 35 will do the same for stat-block senses. Feature 34 defines generic state/readers, never a species/monster grant. |
| Hidden lifecycle | Feature 13's Invisible instance cannot carry the Hide check DC. Feature 34 needs a distinct hidden record paired with exactly one condition instance, plus authenticated end signals. Fixed-role subscriptions cannot maintain arbitrary hidden creatures. |
| Sound/casting/damage | Attack, spell, sound, and damage sources do not expose all required authenticated ending events. Hide may not accept claimed sound, attack, spell component, or discovery input. |

## Recursive dependency analysis

```text
Feature 34: observation, hiding, passive Perception, and surprise              [blocked parent]
├─ immutable observation policy                                                 [missing Slice 1 leaf]
├─ abilities, skill proficiency, seeded check                                   [implemented: Features 2–3]
├─ condition storage/effective implications                                     [implemented: Feature 13]
├─ action allowance                                                             [implemented: Feature 12]
├─ map, placement, distance                                                     [blocked: Feature 20 Slice 2]
├─ sides and physical cover/line-of-effect                                      [blocked: Feature 21 Slices 2–3]
├─ illumination/obscurement and source emission                                 [blocked after map/geometry]
├─ creature sense state and grants                                              [blocked: policy + Features 26/35]
├─ can-see / can-detect reader                                                  [blocked: map + geometry + illumination + senses]
├─ passive Perception reader                                                    [blocked: applicable-modifier contract]
├─ Hide lifecycle                                                               [blocked parent]
│  ├─ trusted sight/cover/enemy preconditions                                   [blocked: can-see + Feature 21]
│  ├─ Action spend plus Stealth result composition                              [missing dynamic composition seam]
│  ├─ hidden record + paired Invisible application                              [missing condition-fragment transaction seam]
│  └─ attack/spell/sound/find ending evidence                                   [blocked: source events + dynamic active-state binding]
└─ surprise-to-Initiative bridge                                                [blocked: can-see/hidden + dynamic child-result binding]
```

The only independent leaf is immutable observation policy. It establishes one source-cited
vocabulary without declaring a creature sees, hides, notices, emits light, or is surprised.

## Dependency and ownership decisions

1. **Geometry is physical; sight is perceptual.** Feature 21 determines cover/Total Cover and
   Feature 20 determines placement. Feature 34 combines their trusted result with illumination,
   special senses, and conditions to answer observer-to-subject `canSee`; it never returns a
   geometry verdict that Feature 21 could consume.
2. **Illumination belongs to encounter/environment state.** A later Feature-34 encounter-scoped
   light/obscurement model records only canonical areas and source-backed emitters. It does not
   embed a world map, item inventory, weather, calendar, or copied creature positions.
3. **A sense is source-backed actor capability, never a species label.** Proposed sense state names
   only canonical sense kind/range and provenance. It contains no creature type, class, item,
   spell, target, illumination, position, active duration, or hidden list. Source systems grant or
   revoke it through reviewed transitions.
4. **Hidden and Invisible are paired but not identical.** Feature 34 owns a short hidden record
   with the successful Stealth DC and action evidence; Feature 13 owns the condition instance.
   Every successful Hide creates both or neither, and every authenticated end clears both or neither.
   An independently granted Invisible condition never invents a hidden DC.
5. **Passive Perception is derived, not stored.** Its reader uses authoritative Wisdom and
   Perception proficiency plus only modifiers proved applicable to passive observation. It stores no
   cached score and does not turn a passive observation into an automatic discovery write.
6. **Finding is a check, not condition deletion.** Feature 34 invokes the Perception resolver
   against the hidden record's DC. On success, its lifecycle root clears hidden state and requests
   paired condition clear; a caller cannot name another creature's hidden record or final result.
7. **Surprise is an Initiative context, not a condition.** A pre-order reader yields canonical
   `disadvantage: surprised` only for combatants unaware at combat start. It never writes condition
   state or stores a Surprise Boolean; Feature 5 still rolls and records Initiative once.
8. **Dynamic consequences need an authenticated composition seam.** Current children cannot feed a
   sight/Stealth/surprise result into Action spend, condition writer, or Initiative child. This is a
   platform/owner decision, not a reason to copy their logic in Feature 34.

## Confirmation boundaries

| Decision | Required confirmation before implementation |
| --- | --- |
| Observation policy | Exact component/procedure/entity IDs, policy key/version, source locator, canonical illumination/obscurement/sense/hide/surprise tokens, and immutable revision rule. |
| Encounter illumination | Area model, overlap/precedence, source-emitter reference/lifecycle, relationship to Feature-20 cells and Feature-21 occlusion, and no-world-clock-copy rule. |
| Senses | Component shape, allowed kinds/ranges, provenance/grant/revoke interface, source-specific range policy, and treatment of ordinary sight, Blinded, Invisible, and Total Cover. |
| Visibility result | Closed observer/subject/encounter roles, physical-line dependency, conditions/sense ordering, `canSee` versus non-sight detection result, and no leakage of hidden data. |
| Passive modifier set | Which Feature-13/other derived modifiers apply to a non-D20 passive score, its audit output, and how future feature modifiers compose without cached state. |
| Hide composition | One Action spend + one Stealth child + hidden record + condition fragment transaction; result binding, seed/audit ownership, rollback, and independent-Invisible behavior. |
| Hidden endings | Exact attack-roll, verbal-spell, sound, and successful-find event/action evidence; dynamic indexed/fan-out handling, ordering, and idempotency. |
| Surprise bridge | Moment-of-combat snapshot, participant scope, awareness policy, per-participant Initiative context binding, and no caller-forged `surprised` source. |

## Slice order and stop gates

| Order | Slice | Starts only when | Exit gate |
| ---: | --- | --- | --- |
| 1 | Immutable observation policy | Permanent vocabulary and source locators confirmed. | A source-cited policy reads deterministically with zero encounter, creature, light, condition, check, or Initiative effects. |
| 2 | Passive Perception reader | Slice 1 and applicable-modifier decision. | An effect-free reader derives one creature's uncached score from authoritative Perception facts only. |
| 3 | Encounter illumination/obscurement and generic sense state | Slice 1, Feature 20 placement/map, Feature 21 geometry, and source-emitter convention. | Closed state/readers establish illumination/obscurement and source-backed sense capability without Hide or attack. |
| 4 | Can-see / detect reader | Slice 3, Feature 13, and Feature 21 physical-line result. | A deterministic observer-subject result distinguishes sight from non-sight detection and never exposes raw hidden state. |
| 5 | Hide and find-hidden lifecycle | Slice 4, action/check/condition composition, and authenticated ending events. | A legal Hide creates paired hidden/Invisible state; only correct source events or a successful finding end it atomically. |
| 6 | Surprise-to-Initiative bridge | Slice 5 and ratified dynamic context binding to Feature 5. | One encounter start derives surprise contexts and rolls Initiative once per member without persisted Surprise. |
| 7 | Consumers and source expansion | Slices 2–6 and each owner review. | Feature 21 close combat, species/monster/item/spell senses, and special exceptions consume stable readers by amendment. |

## Slice 1 — immutable observation policy

### Runtime artifacts

- A confirmed immutable `dnd2024.observation-policy` component/schema and static-definition
  procedure.
- One versioned `content.dnd2024.observation-policy.standard.v1` entity with fixed provenance.
- Focused catalog validation/tests only. No creature senses, map light, hide state, condition
  application, D20 check, Action spend, Initiative context, event, subscription, or fixture.

### Data contract and required state

The policy is closed and immutable. It declares stable key/version and source reference; canonical
illumination levels (`bright`, `dim`, `darkness`); ordinary obscurement outcomes; canonical
special-sense vocabulary (`blindsight`, `darkvision`, `tremorsense`, `truesight`); Hide
precondition/end category tokens; the passive-Perception base constant/formula declaration; and the
surprise-to-Initiative circumstance token.

It contains no actor, enemy, encounter, position, path, map cell, line, cover degree, light source,
sense range, check DC/result, seed, condition, hidden record, Initiative count, action budget,
effect, duration, party/campaign, or outcome. Exact canonical token ordering and policy references
are confirmed at the permanent-ID boundary rather than guessed into runtime code.

### Recording behaviour, result, and effects

Static validation/readback returns canonical policy/entity ID, key, version, source reference, and
the closed declarations with zero effects. A valid policy cannot make an area bright/dim/dark,
grant Darkvision, decide sight, apply Invisible, make a Stealth/Perception check, mark someone
surprised, or alter Initiative.

### Invariants, failure behaviour, and non-goals

- Entity key/version and component key/version agree; correction requires a successor entity and
  never mutates a policy that future actions/receipts may cite.
- Wrong source, unknown/duplicate token, omitted required category, malformed passive formula
  declaration, noncanonical ordering, extra field, or duplicate same version rejects unchanged.
- Reads inspect no creature, condition, map, item, action, event, Initiative, campaign, or world
  state; they are deterministic and make no random call.

### Slice 1 implementation sequence

1. Re-read source registry/content conventions, Features 13/20/21/26/35, D20/Initiative
   contracts, and current catalog vocabulary. Confirm no compatible immutable rules-policy owner
   already exists.
2. Pause at the permanent-ID/source-vocabulary confirmation boundary. Confirm exact entity,
   component/procedure, token, source-locator, and revision IDs.
3. Author schema, procedure, standard policy entity, read/validation path, and focused tests
   together. Store no copied SRD prose or executable area/sense/Hide payload.
4. Prove valid readback, bad key/version/source/token/formula/ordering, extra data, immutability,
   replay, zero-effect isolation, and catalog query-back.
5. Run focused tests, `roleplay validate catalog`, the full suite, and `git diff --check`; write a
   receipt and stop. Do not begin passive scores, map illumination, senses, or hiding.

### Slice 1 acceptance matrix

| Case | Exact assertion |
| --- | --- |
| Source policy | One active standard policy returns exact source provenance and canonical observation vocabulary. |
| Separation | Illumination, senses, Hide, passive Perception, and surprise tokens are policy only; none creates creature or encounter state. |
| Closed/immutable data | Wrong key/version/source, unknown/duplicate/reordered token, malformed formula declaration, missing/extra field, in-place rewrite, or duplicate same version rejects unchanged. |
| Isolation | Reads leave creatures, positions, maps, light sources, items, conditions, actions, checks, Initiative, events, and campaign state byte-identical. |
| Determinism | Equivalent reads are byte-identical, make no random call, and select no player-facing phrase. |
| Repository | Focused tests, disposable catalog validation, full suite, diff check, and query-backs pass; no persistent import occurs. |

### Slice 1 exit gate

Slice 1 is verified only after the immutable policy has closed source-cited data,
rejection/immutability/isolation evidence, catalog validation, repository checks, and a receipt.
Stop before creating observation state, light source, visibility result, passive score, Hide action,
or surprise bridge.

## Later owner and consumer map

```text
observation policy
├─ encounter illumination / obscurement ────────────────────────> Feature 34
├─ source-backed creature sense capability ─────────────────────> Feature 34; grants by 26/35/29/32
├─ physical map, placement, line/cover, and enemy predicate ───> Features 20/21
├─ observer-to-subject can-see / detect result ─────────────────> Feature 34
│  ├─ Blinded/Deafened/Invisible state ─────────────────────────> Feature 13
│  ├─ Feature 21 close-combat reader ───────────────────────────> Feature 21
│  └─ Hide/surprise decision ───────────────────────────────────> Feature 34
├─ passive or active Perception ────────────────────────────────> Feature 34 + Feature 3
├─ Hidden record + paired Invisible condition ──────────────────> Feature 34 + Feature 13
└─ surprise context ────────────────────────────────────────────> Feature 5 Initiative order
```

## Plan-quality audit

- One observation capability with source, state, calculation, action, and Initiative boundaries:
  yes.
- Existing condition, D20, action, Initiative, map, geometry, item, species, and monster owners
  were inspected: yes.
- Every unresolved operational requirement is a named leaf or blocked parent: yes.
- Static policy, illumination, senses, can-see, passive score, hidden state, D20 result, condition,
  and surprise context retain separate owners: yes.
- One lowest implementation slice exists: **Slice 1 immutable observation policy**.
- No runtime game artifact was created by this planning pass: yes.

## Plan-change rule

Revise before implementation if Feature 20 changes tactical placement, Feature 21 selects a
different physical-line contract, Feature 13 changes condition provenance, the composition platform
gains safe dynamic child-result binding, or a source owner establishes a generic light/sense
capability. Do not work around those changes with a duplicate grid/light map, caller-supplied
visibility/cover/surprise/check outcome, stored passive score, direct condition/action/Initiative
write, generic sound listener, or automatic exposure of hidden state.
