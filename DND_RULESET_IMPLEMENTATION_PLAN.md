# D&D SRD 5.2.1 ruleset implementation plan

Last updated: 2026-08-18

## Purpose

Build a playable, inspectable D&D-compatible ruleset using **only SRD v5.2.1** material under the
existing `dnd2024-srd-5.2.1` scope.  This is an implementation plan, not permission to bulk-create
rules.  Each numbered component still needs its own active procedure contract before it is added.

The target is a ruleset that a game-master model can use reliably: it can discover the relevant
rule, collect a declared action, make transparent checks, apply only defined state changes, and
tell the story around the result.

## Research findings

Established virtual-tabletop implementations tend to separate four concerns:

1. **Actors** are campaign-owned state: player characters, creatures, parties, and encounters.
2. **Content records** are reusable definitions: equipment, spells, class features, monsters, and
   similar SRD records.  They are not mutated when one character uses them.
3. **Activities** are the typed things content can do.  Foundry's 5e system, for example, puts
   flexible, multiple activities on an item instead of making an item have exactly one hard-wired
   action.  Its types cover attack, check, save, cast, damage, healing, summoning, transformation,
   and utility work.
4. **Effects and advancement** alter an actor through defined changes, instead of embedding every
   exception inside a roll formula.  Advancement is treated as ordered grants, choices, traits,
   scale values, and hit-point changes.

This is a useful model for DantesRoleplay, but it is not a requirement to copy Foundry's data
format.  The server should use its own small, source-cited component contracts and stay within its
three-verb MCP surface.

## Design principles

### 1. Separate definitions from play state

Keep SRD content definitions immutable and source-cited.  A character, monster instance, encounter,
or active condition is campaign state that references those definitions by stable ID.  A later
correction to an SRD content record must be an explicit versioned revision; it must not silently
rewrite a campaign character.

### 2. Make activities the bridge between fiction and mechanics

Every resolvable player or GM declaration should eventually select one typed activity:

```text
declared intent -> activity -> inputs/context -> roll or choice -> typed effects -> updated state
```

An activity records only what the rule needs: eligibility, activation cost and timing, targets,
range, roll or save instruction, consumption, duration, and effect references.  It never contains
free-form code or a hidden narration outcome.

The initial activity family should be deliberately small:

| Activity | Initial use |
| --- | --- |
| `check` | Ability checks against fixed DCs or another actor |
| `save` | A creature resists a defined hazard or effect |
| `attack` | Attack roll, hit decision, then separately-defined damage |
| `utility` | A non-roll, rules-bearing action such as interacting with an object |

`cast`, `heal`, `damage`, `summon`, and `transform` come only when their supporting data,
targeting, and effect contracts exist.  An activity can delegate to a smaller activity, but that
link must be explicit and acyclic.

### 3. Use typed effects, not prose mutations

The result of a successful activity is a list of closed, inspectable effect instructions, for
example `adjust-hit-points`, `consume-resource`, `apply-condition`, `move-actor`, or
`grant-temporary-data`.  The narrative layer explains the result; it does not invent a state
change.  Events and subscriptions, when implemented, subscribe to these committed effects rather
than to unstructured text.

### 4. Treat derived values as a projection

Ability modifiers, proficiency bonuses, Armor Class, DCs, resource limits, and roll modifiers are
derived from cited base state and active effects.  Store their inputs and the calculation/version
that produced an action audit; do not make every derived number independently mutable.  Cache only
when a later performance need proves it necessary.

### 5. Preserve rules provenance and licensing

Every rules-bearing definition needs a `sourceRef` containing at least the SRD version, a stable
section or page reference, source URL, attribution text/key, and a short structured explanation
when it makes a contract easier to use.  Store facts, parameters, and citations—not copied
non-SRD book text.  Content outside the SRD must be rejected or live in a separately licensed
pack, never mixed invisibly with the SRD scope.

### 6. Make character growth a controlled workflow

Character creation and level advancement are not ad-hoc component edits.  They are a sequence of
typed grants and player choices evaluated against eligibility rules, then committed atomically.
Every choice records the source definition, owning actor, level/prerequisite context, and the
resolved grant.  This makes corrections, auditing, and future migration possible.

### 7. Prefer small composable rules over a universal mega-schema

An ability score component must not pre-design spell slots.  A weapon component must not pre-design
every condition.  Contracts define stable extension points and explicit dependencies, while each
new component adds one responsibility only.  Categories follow the existing dot-path taxonomy,
such as `ruleset.dnd2024.data.abilities` and
`ruleset.dnd2024.combat.attack.roll`.

## Target component model

| Layer | Owns | Does not own |
| --- | --- | --- |
| Source registry | SRD identity, page/section references, attribution | campaign state or rules execution |
| Content definition | immutable, versioned SRD record and source reference | a particular character's choices/resources |
| Actor state | a campaign entity's base data, acquired definitions, resources, active effects | the canonical SRD definition |
| Activity definition | inputs, eligibility, timing, targeting, resolution instruction, effects | free-form mutations or narration outcomes |
| Resolution mechanic | deterministic validation, dice math, and result envelope | cataloguing every spell/item/monster |
| Effect mechanism | validated state changes and audit entries | selecting a rule from natural-language intent |
| Play/host contracts | who declares intent, who selects rules, who narrates | changing rules without a contract |

Relationships use immutable IDs, never display names.  A content definition may be superseded, but
existing references retain the exact definition version until an explicit migration contract maps
them forward.

## Contract standard for this track

The existing main contract, `procedure.mechanic.dnd2024.ruleset`, remains the governing contract.
Every planned item below must first get its own active procedure using this shape:

```text
procedure.mechanic.dnd2024.<component-id>
```

In addition to the established contract fields, D&D contracts should state:

- The layer(s) it owns and the layers it may read.
- Exact `sourceRef` requirement and a concise SRD explanation where useful.
- Whether it defines an actor field, content definition, activity, resolution mechanic, or effect.
- Stable IDs and any allowed version/migration behavior.
- The category leaf under `ruleset.dnd2024`.
- Inputs, output envelope, failure behavior, and deterministic fixtures.
- Forbidden future assumptions, so an early component cannot accidentally authorize a later one.

Only one component contract is created, reviewed, and implemented at a time.  Each implementation
uses the normal search -> dry-run -> verified commit -> query-back -> recorded operation-ID loop,
then stops for review.

## Ordered implementation roadmap

### Stage 0 — foundation and play boundaries

1. **Source registry** — create its contract, then register SRD v5.2.1 with its CC-BY attribution,
   canonical download URL, and page/section reference format.
2. **Host contract** — create `procedure.mechanic.dnd2024.host`, paired with the existing player
   contract.  The host describes scenes, decides when a rule applies, chooses a cited mechanic,
   asks only supported follow-up questions, commits the result, and narrates without inventing
   mechanical state.
3. **D20 resolution envelope** — define the common auditable result shape before any check,
   attack, or save mechanics: dice specification, rolls, modifiers by source, total, target/DC,
   outcome, and affected actor IDs.  It is a data/result component, not yet a complete roll rule.

**Exit:** a source-cited host can receive a declared action and identify either a governed next
component or a missing rule—without improvising one.

### Stage 1 — minimum viable ability play

4. **Abilities** — six ability-score fields, modifiers, invariants, and source references.
5. **Proficiencies** — proficiency states and a stable association to abilities/skills; no class
   automation yet.
6. **Ability-check activities** — fixed-DC first, then contested checks, using the common result
   envelope and transparent modifier sources.
7. **Advantage/disadvantage** — one bounded modifier-selection component, shared by checks before
   it is reused elsewhere.
8. **Saving throws** — save proficiencies and a save activity, without spell-specific content.

**Exit:** a player can attempt a simple uncertain action; the system records why the relevant
check was selected, every modifier, the dice, and the authoritative outcome.

### Stage 2 — actor survival and basic encounters

9. **Actor vital statistics** — hit points, temporary hit points, armor class inputs, speed, size,
   senses, and resource fields with strict invariants.
10. **Encounter and turn state** — encounter membership, initiative, active turn, round, and a
    bounded turn transition.  This is state management, not attack implementation.
11. **Combat action economy and movement** — activation timing, movement allowance, targets, and
    range.  Map/grid support is explicitly deferred.
12. **Attack, hit, and damage** — separate contracts for attack-roll resolution, hit evaluation,
    damage expression, resistance/immunity/vulnerability, and hit-point effects.
13. **Conditions and death** — one condition/effect registry plus specific condition contracts;
    unconsciousness, death saves, and stabilization only after their prerequisites are tested.

**Exit:** a two-creature, round-based encounter can run deterministically and resume from saved
state, with no narration-only damage or untracked resource use.

### Stage 3 — authored character choices

14. **Content-definition base** — versioned SRD records and campaign-owned references for items,
    features, spells, backgrounds, species, and classes.
15. **Character origins** — background/species records, choice lists, and granted traits.  Build
    each record family as a separate component; do not import all content in one commit.
16. **Class and level** — class membership, level, hit-die inputs, class features, and prerequisites.
17. **Advancement workflow** — structured grants, selections, ability-score improvements, scale
    values, and validation of choices.  Each advancement type receives its own contract.
18. **Equipment and inventory** — possession, equipped state, capacity only if the SRD rule is
    being implemented, and activity references.  Start with mundane equipment.

**Exit:** a small, SRD-only sample character can be constructed and advanced through explicit,
auditable choices without hand-editing derived values.

### Stage 4 — magic, rest, and richer actions

19. **Spellcasting base** — spellcasting ability, prepared/known state, slots or other cited
    resource model, and spell DC/attack derivation.
20. **Spell activities** — casting timing, components/costs as supported by the current scope,
    targets, duration, concentration, and effects.  Add individual spells only after the generic
    activity component is verified.
21. **Rest and recovery** — short/long-rest activities, recovery rules, and expiry of limited
    effects.
22. **Special activity families** — healing, summoning, transformation, and area effects; each
    remains deferred until targeting and effect rules can express it without exceptions.

**Exit:** a limited SRD spell can be selected, validated, resolved, consume the right resource,
apply effects, and be cleanly reversed/expired where the rule requires it.

### Stage 5 — content packs and operations

23. **Creatures and NPCs** — SRD creature definitions, separate campaign instances, and reusable
    activities.  Do not make a monster stat block a special execution path.
24. **Content-pack lifecycle** — import manifest, source/version validation, conflict reporting,
    preview, atomic apply, and explicit deprecation/migration.  This is the only point where bulk
    SRD-content import becomes appropriate.
25. **Compatibility and regression suite** — frozen deterministic scenarios for each released
    component, version-crossing migration tests, and replay of saved action audits.
26. **Game master experience** — use intent routing only after the existing local-intent-routing
    plan's safety contract is implemented; the router may recommend a rule but never silently
    alter campaign state.

**Exit:** an SRD content pack can be validated and installed without bypassing contracts, and a
fresh host model can run a small game entirely from retrieved contracts, components, and audits.

## Verification strategy

Every component contract supplies fixtures based on a small source-cited scenario, not a copied
block of book text.  The complete ruleset adds these cross-cutting checks:

- **Schema checks:** reject missing source references, unknown IDs, invalid category paths, and
  impossible actor state.
- **Resolution checks:** the same inputs and recorded dice produce the same result; every modifier
  names its source; ties and natural-roll rules are specified by the applicable component.
- **Boundary checks:** actions cannot spend unavailable resources, target illegal actors, apply
  unknown conditions, or mutate content definitions through campaign play.
- **Composition checks:** check -> effect, attack -> damage -> hit-point change, and advancement ->
  grant chains work through public MCP operations only.
- **Audit/replay checks:** query history exposes the selected contract/mechanic, source reference,
  inputs, result, effects, and operation ID needed to explain a ruling.
- **Migration checks:** an explicitly migrated actor keeps a readable original version and either
  lands in a valid new state or fails with a repair instruction.

## Deliberate non-goals until later

- Non-SRD or branded D&D content, copied rulebook prose, and unlicensed compendia.
- A universal rule-expression language or arbitrary scripts inside content records.
- Map/grid, lighting, vision geometry, and automation that requires them.
- Full character-builder UX, broad content import, or every spell/class/monster before the small
  vertical slices prove the model.
- Autonomous rule changes, chained writes, or narrative text that changes state outside a typed,
  audited effect.

## Source and design references

- Wizards of the Coast, *System Reference Document v5.2.1*, CC-BY-4.0.  The official SRD page
  identifies the v5.2.1 release, its 2024-revised (5.5e) rules content, and the attribution-based
  license: <https://www.dndbeyond.com/srd>.
- Foundry VTT's open D&D 5e system is a practical reference for separating actors, items,
  mechanics, and compendium content: <https://github.com/foundryvtt/dnd5e>.
- Its activity model is a useful reference for typed, reusable action definitions, their timing,
  consumption, targeting, and applied effects: <https://github.com/foundryvtt/dnd5e/wiki/Activities>.
- Its advancement model illustrates ordered, typed grants and choices rather than hand-editing a
  character at level-up: <https://github.com/foundryvtt/dnd5e/wiki/Advancement>.
