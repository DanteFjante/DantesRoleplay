# Feature 21 dependency plan — cover and ranged attacks

Status: **Planned; Slice 1 (authoritative ranged-weapon range data) is the next and only authorised implementation pass. Cover, sight, and tactical ranged resolution remain blocked.**
Last updated: 2026-08-21

## Execution rule

This is a planning-only artifact under `AGENTS.md` and the Terra planning guide. It creates no runtime procedure, component, mechanic, event, subscription, fixture, migration, or game state.

The live `procedure.system.create-feature` read and runtime inventory were unavailable, so this plan records repository/catalog evidence only. An implementation pass must re-read current live contracts, resolve catalog/database drift, perform one reviewed lowest slice, validate a disposable import, record evidence, and stop.

## Target capability

Within a tactical encounter, a ranged weapon attack can use authoritative normal/long range, geometric cover, and the relevant close-combat circumstance without copying attack arithmetic or turning transient cover into permanent Armor Class.

### Included

- Normal and long range as versioned static weapon facts.
- Half, Three-Quarters, and Total Cover as a transient attack/target context.
- A bounded tactical geometry result: cover degree and physical line-of-effect, distinct from a creature's ability to see.
- The SRD ranged-attack long-range Disadvantage, beyond-long-range refusal, and close-combat Disadvantage conditions.
- Reuse of Feature 8's seeded weapon-attack resolution only through a confirmed trusted context-composition path.

### Excluded

- Senses, darkness, light, hidden creatures, Invisible's “cannot be seen,” blindsight, and the player-facing determination that one creature can see another. Feature 34 owns those facts.
- Arbitrary vector geometry, elevation, projectile simulation, ballistics, area effects, spell targeting, and cover for every unusual object shape.
- Thrown weapon ranges, ammunition, loading, weapon Reach, Finesse, mastery, and weapon ownership/equipping. Feature 25 owns those properties; this plan initially supports the existing ranged weapon profile only.
- Damage, Action expenditure, target selection UI, attacks made by spells, and firing a ranged weapon from a mount.
- A “firing into melee” penalty. The SRD 5.2.1 rule is instead attacker-side close combat: an enemy within 5 feet who can see the attacker and is not Incapacitated.

## Official source basis

The registered source is `source.dnd2024.srd-5.2.1`: *System Reference Document 5.2.1* (Wizards of the Coast LLC, 2025-05-01, CC-BY-4.0), [Playing the Game > Cover and Ranged Attacks, PDF p. 14](https://media.dndbeyond.com/compendium-images/srd/5.2/SRD_CC_v5.2.pdf), [Rules Glossary > Cover, PDF p. 178](https://media.dndbeyond.com/compendium-images/srd/5.2/SRD_CC_v5.2.pdf), and [Equipment > Weapons, PDF p. 90](https://media.dndbeyond.com/compendium-images/srd/5.2/SRD_CC_v5.2.pdf).

- Half Cover grants +2 AC and Dexterity saves; Three-Quarters Cover grants +5; Total Cover prevents direct targeting. Multiple cover sources use only the most protective degree.
- A ranged attack beyond normal range and within long range has Disadvantage; a target beyond long range cannot be attacked.
- A ranged attack has Disadvantage when the attacker is within 5 feet of an enemy that can see it and is not Incapacitated.
- The currently seeded Shortbow's source range is 80/320 feet. The existing Dagger range is a Thrown property and is therefore Feature 25 work.

## Planning inventory and overlap result

| Inquiry | Evidence and conclusion |
| --- | --- |
| Weapon profile owner | Feature 7 owns `dnd2024.weapon-profile`, currently limited to category, `kind`, abilities, damage, and source. No normal/long range exists. Range must revise this existing static owner, not create a parallel range component. |
| Attack resolver | Feature 8's `mechanic.dnd2024.weapon-attack` consumes final Armor Class and produces effect-free D20 evidence. It has no range, cover, map, or visibility input. |
| Permanent AC | Feature 6's `dnd2024.armor-class` is final persistent AC. Cover is directional and transient; it must not set or cache a target's AC. |
| Position/terrain | Feature 20 is planned. It owns grid position/placement and its sparse blocked/difficult map, but no cover-profile geometry has been confirmed. |
| Conditions | Feature 13 owns stored/effective creature conditions. Its plan explicitly defers positional condition rules; a close-combat enemy must be effectively not Incapacitated through that owner, not a copied condition list. |
| Sight | Feature 34 owns vision/light/hiding/senses. Grid positions and physical line-of-effect are insufficient to conclude that a creature can see another. |
| Combat enemy relation | Features 5 and 11 explicitly exclude encounter sides. No D&D combat-side/hostility owner exists; world factions are campaign narrative state, not an encounter enemy predicate. |
| Composition | Children resolve before parent source and accept only inherited/static/caller top-level objects. The attack resolver cannot safely receive a parent-derived cover bonus, range result, or close-combat circumstance today. |

## Recursive dependency analysis

~~~text
Feature 21: cover and ranged attacks
├─ SRD cover/range/close-combat rules                            [implemented source basis]
├─ static weapon-profile owner                                   [implemented: Feature 7]
├─ seeded effect-free attack arithmetic                          [implemented: Feature 8]
├─ persistent final AC                                            [implemented: Feature 6]
├─ range data on existing ranged weapon profiles                 [missing leaf: Slice 1]
├─ tactical map/positions/distance                               [blocked: Feature 20 Slice 2]
├─ bounded cover geometry / GM adjudication policy               [missing design leaf]
├─ encounter enemy relation                                      [missing state leaf]
├─ effective Incapacitated input                                 [blocked: Feature 13 consumer state]
├─ can-see input                                                  [blocked: Feature 34]
├─ trusted derived attack context -> Feature 8                   [missing platform leaf]
└─ tactical ranged-attack parent                                 [blocked parent]
   ├─ range outcome + long-range Disadvantage                    [blocked: range data + map + composition]
   ├─ cover outcome                                               [blocked: geometry + composition]
   └─ close-combat outcome                                        [blocked: sides + sight + conditions + composition]
~~~

The lowest next slice is the Feature-7-owned static range-data migration. It supplies the authoritative fact later slices need without pretending that a range-capable tactical attack already exists.

## Dependency and ownership decisions

1. **Range is static weapon data.** Extend `dnd2024.weapon-profile` under its existing Feature-7 procedure with a required closed `rangeFeet` object for `kind: "ranged"` profiles: `normal` and `long` are positive five-foot multiples, and `normal <= long`. Melee profiles have no range field. The first fixture migration records Shortbow 80/320. Thrown modes wait for Feature 25.
2. **Cover is attack context, never Armor Class state.** A cover reader returns a closed degree and derived AC bonus/targetability for one attacker-target-map snapshot. It does not mutate `dnd2024.armor-class` or retain result state beyond the action/audit.
3. **Geometry must be explicit before it is automatic.** The SRD defines cover outcomes but not a universal grid algorithm for whether an irregular object covers half or three quarters. A separate map-cover policy must decide whether the system uses bounded directional cell edges, authored obstruction silhouettes, or a GM-recorded geometry result. This feature must not infer degrees from a caller label or a simplistic “one creature equals half cover” rule.
4. **Line of effect is not sight.** Cover/obstacle geometry can report whether direct targeting is physically blocked. Feature 34 decides visibility from illumination, senses, hidden/Invisible state, and later dynamic light. Feature 21 consumes that result for close combat; it does not substitute clear geometry for “can see.”
5. **Enemy is encounter state, not a faction guess.** The close-combat rule needs a stable encounter-scoped enemy predicate. A new small side/hostility contract is required; it must not infer hostility from names, world factions, containment order, or Initiative position.
6. **Feature 8 remains the dice owner.** The tactical parent can supply a trusted, closed derived context only after platform support distinguishes host-bound values from caller input. It must not duplicate AC math, D20 selection, Advantage cancellation, or natural-roll behavior.
7. **The roadmap wording is corrected to the SRD.** “Firing into melee” is not implemented as a target-side penalty. The supported rule is renamed “ranged attacks in close combat.”

## Confirmation boundary

| Decision | Required confirmation |
| --- | --- |
| Range schema | Exact `rangeFeet` field shape, profile migration, source locators, and Feature 25's future Thrown/property extension seam. |
| Cover policy | Bounded terrain representation and deterministic directional/line algorithm, or a normal audited GM-adjudication writer. |
| Enemy state | Authoritative encounter-scoped side/hostility model, normal writer, missing/neutral semantics, and fixture baseline. |
| Sight input | Feature 34's stable per-observer/subject “can see” result and ordering with conditions/hidden state. |
| Condition input | Feature 13 effective-Incapacitated report used by close-combat evaluation. |
| Trusted context | Platform contract for passing derived cover/range/circumstance data to Feature 8 without exposing forged fields on its direct public action. |
| Attack parent | Player-facing intent/routing boundary between diagnostic Feature 8 resolution and complete tactical melee/ranged parents. |

## Slice order and stop gates

| Order | Slice | Starts only when | Exit gate |
| --- | --- | --- | --- |
| 1 | Ranged weapon range data | Feature 7 owner and source rows re-read. | Every ranged profile has validated normal/long range; existing fixtures/schema/contracts migrate without changing attack results. |
| 2 | Combat-side foundation | Slice 1 and ids confirmed. | Encounter participants have an explicit, validated side/hostility fact with no faction inference. |
| 3 | Cover geometry reader | Feature 20 placement/map plus cover policy confirmed. | Effect-free reader returns deterministic cover/line-of-effect evidence, with Total Cover refusing direct targetability. |
| 4 | Range reader | Feature 20 distance and Slice 1. | Effect-free reader returns in-normal/in-long/out-of-range from positions and weapon data. |
| 5 | Close-combat reader | Slices 2–4, Feature 13, and Feature 34. | It identifies only the SRD attacker-side Disadvantage trigger from enemy, distance, sight, and effective Incapacitated inputs. |
| 6 | Tactical ranged attack | Slices 3–5 and trusted context composition. | A normal/long/blocked/close-combat ranged attack composes Feature 8 once with exact context and unchanged state. |

## Slice 1 — ranged weapon range data

### Runtime artifacts

Revise the existing Feature-7 `dnd2024.weapon-profile` schema, writer, governing procedure, and ranged weapon fixtures; add focused migration/import tests. No new Feature-21 component or action is needed.

### Data/input and behavior

For a `kind: "ranged"` profile, `rangeFeet: { normal, long }` is required and has positive five-foot integer values with `normal <= long`. It is absent—not empty or null—on `kind: "melee"` profiles. The profile writer fixes source provenance and rejects caller-supplied damage/range derivations outside the complete static profile it owns. Existing Shortbow migrates to 80/320 from the SRD weapon table; melee Dagger and Battleaxe retain their current shape.

This slice changes no action selection, D20 resolver, current AC, equipment state, position, or damage. It is a content/schema migration only.

### Acceptance matrix and exit gate

| Case | Exact assertion |
| --- | --- |
| Source fixture | Shortbow records normal 80 and long 320; queried profile preserves canonical field order/source attribution. |
| Closed shape | Ranged profile missing/null/empty/non-object range, zero/negative/fractional/non-five-foot values, long below normal, extra fields, or wrong-case keys fails unchanged. |
| Melee differential | Dagger/Battleaxe reject any `rangeFeet` field and retain valid unchanged profiles. |
| Migration | All current catalog profiles and fixture imports validate; Feature-8 direct result for the same seed/input remains byte-identical. |
| Routing/state | Profile administrative phrases remain with Feature 7; no new ranged attack intent is activated. Rejections produce zero effects. |

Stop when the existing static owner carries exact range data and repository/catalog checks pass. Do not add a tactical attack parent in this slice.

## Slice 2 — combat-side foundation

Create one encounter-scoped participant side/hostility record with a normal record/correct path. A value must be closed, stable within the encounter, and deliberately distinguish enemy, ally, and neutral/unknown semantics. It must validate roster membership and cannot read world factions, Initiative rank, character name, or callers' target lists. The reader reports the exact relation for two members and has no effects.

Test distinct/same/neutral sides, missing/corrupt/stale encounter state, duplicate membership, roster changes, query/readback, replay, routing, and fixture cleanup. Stop before sight, cover, or attacks.

## Slice 3 — cover geometry reader

After Feature 20's map/positions exist and cover policy is ratified, add an effect-free reader for attacker, target, and encounter. It consumes only map geometry plus both exact footprints and returns one closed cover degree, AC bonus (0/2/5), direct-targetable Boolean, and geometry provenance. Multiple sources select the highest degree; Total Cover sets direct-targetable false. It never alters AC or applies an attack condition.

The acceptance matrix includes all degrees, multiple-cover maximum, both direction reversals, blocked/missing/corrupt/overlapping positions, map bounds, large/tiny footprints, deterministic same geometry, zero effects, and no caller cover field. Stop before range or sight.

## Slice 4 — range reader

After Feature 20 distance and Slice 1, an effect-free reader consumes attacker/target placements and ranged weapon profile. It reports measured distance, normal/long limits, and exactly one disposition: `normal`, `long`, or `out-of-range`. No path obstruction, cover, targetability, or D20 is decided here. Test equality and adjacency around normal/long boundaries, various footprints, wrong weapon kind, corrupted range, distinct map/encounter, replay, zero effects, and routing.

## Slice 5 — close-combat reader

After Feature 34 sight, Feature 13 effective conditions, and the side foundation, an effect-free reader enumerates encounter participants in canonical order and returns the qualifying enemy ids within five feet of the attacker that can see it and are not effectively Incapacitated. Any nonempty set yields exactly one derived Disadvantage circumstance with source `ranged-close-combat`. It never gives a “firing into melee” penalty based on the target's neighbours.

Test each factor independently, multiple qualifying enemies, same-side/neutral entities, invisible/blinded/hidden/sight variations supplied by Feature 34, effective-condition implications, and zero effects. Stop before resolving an attack.

## Slice 6 — tactical ranged attack

After the three readers and trusted context composition are verified, create one player-facing ranged weapon parent. It validates the weapon kind, targetability, range disposition, and close-combat reader; out-of-range or Total Cover reject before randomness. Normal range uses no added circumstance; long range and close combat each add one derived Disadvantage source, which cancel with any derived Advantage under the established Feature-3 convention. The parent supplies closed trusted context to Feature 8, returns frozen attack evidence, and never writes AC, position, damage, Action budget, or equipment state.

Prove context combinations/cancellation, natural 20/1 precedence, no D20 on invalid targetability/range, same-seed replay, no caller-forged cover/range/side/sight fields, exact zero effects, intent routing, and fixture restoration.

## Plan-quality audit

- One player capability with explicit boundaries: yes.
- Official source/version/locators: yes; SRD 5.2.1 pp. 14, 178, and 90.
- Existing owners and overlaps searched: yes; profile, attack, AC, positions, conditions, sight, sides, and composition are classified.
- Every missing dependency expanded: yes; range data, cover policy, enemy relation, Feature 34 sight, and trusted context are separate leaves.
- One next slice: yes — static range data under the existing Feature-7 owner.
- Closed state/input, formula boundaries, deterministic evidence, routing, and cleanup: specified.
- No runtime artifact is created by this planning pass.

## Plan-change rule

Stop and revise if the Feature-7 profile has independently gained a range schema, Feature 20 selects incompatible coordinates, Feature 34 exposes a different sight contract, or trusted composition cannot prevent caller-forged context. Do not store cover in Armor Class, infer enemy from faction/order, use target-side “firing into melee,” or copy Feature-8 D20 logic.
