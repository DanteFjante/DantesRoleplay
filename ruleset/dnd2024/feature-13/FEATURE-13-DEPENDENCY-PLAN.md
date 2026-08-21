# Feature 13 dependency plan — SRD conditions and their D20 Test effects

Status: **Verified in repository mode — all six slices and the full acceptance suite passed**
Last updated: 2026-08-20

## Execution rule

This plan governs repository implementation under `AGENTS.md` and the active
`procedure.system.create-feature`. Repository catalog files are the development authority. Each
implementation pass completes one lowest slice, validates the catalog in a fresh disposable
database, records objective evidence, and stops for review. A persistent catalog import belongs
only to an explicit integration-play or release boundary. All six verified Slice contracts,
components, mechanics, revisions, and focused tests have been authored; later slices remain blocked
until separately reviewed.

## Target capability

A GM can attach and clear SRD conditions on a creature while retaining any relevant creature source,
then see their effective condition state; every D20 Test that creature makes or receives
automatically carries the Advantage, Disadvantage, or automatic failure the SRD prescribes, without
the GM having to remember it or type it in.

The second half of that sentence is the point. Storing conditions is easy and nearly worthless;
what makes conditions real is that the rules engine stops asking the caller for a circumstance it
can derive from state.

### Included

- One creature-owned condition component covering the fourteen non-Exhaustion SRD conditions and
  preserving the optional entity source of each condition instance.
- One administrative writer that records, applies, and clears condition instances with strict
  vocabulary, source, ordering, and compatibility control.
- One shared, effect-free resolver that turns a creature's stored conditions into derived D20 Test
  circumstances, flat modifiers, and automatic-outcome verdicts.
- Consumption of that resolver by every existing D20 Test owner: ability checks, saving throws,
  weapon attacks (both the attacker's and the defender's conditions), and Initiative rolls.
- Enforcement of `Incapacitated` and the Speed-0 conditions in Feature 12's action economy.

### Excluded

- **Exhaustion.** It is a condition in the SRD, but it carries a level and a flat numeric penalty
  rather than a circumstance. Feature 14 owns it by *revising this feature's component and
  resolver*, not by creating a second condition store. This is a boundary, not an omission.
- Every condition effect that requires position, distance, line of sight, or a named other
  creature: Prone's "within 5 feet" attacker split, Frightened's line-of-sight qualifier and
  no-approach rule, Grappled's "targets other than the grappler", and Charmed's restriction on
  attacking the charmer. These are named per-condition in the derivation table below with their
  owning feature (20, 21, or 22).
- Blinded's and Deafened's automatic failure of "checks requiring sight/hearing". Nothing in the
  system marks a check as requiring a sense, and inventing that flag here would be a second,
  unsourced vocabulary. Feature 34 owns senses.
- Petrified's Resistance to all damage, and Paralyzed's and Unconscious's automatic Critical Hits
  from within 5 feet. Damage mitigation is Feature 15; automatic criticals are positional.
- Concentration being broken by `Incapacitated` (Feature 18 — there is nothing to break yet).
- Condition duration, timed expiry, saves to end a condition, homebrew conditions, and any stacking
  rule beyond the SRD's. This feature retains entity-source identity so later causes can end their
  own condition instance without erasing an independent source.
- How a condition is *caused*. Feature 13 never applies a condition as a consequence of damage, a
  spell, or a failed save; Feature 17 owns Unconscious-from-0-HP, and every later cause owns its
  own application call.

## Official source basis

`source.dnd2024.srd-5.2.1`, *System Reference Document 5.2.1* (2025-05-01, CC-BY-4.0). Locators:

- `Rules Glossary > Condition` (PDF p. 179) — a condition normally does not stack with itself;
  Exhaustion is the exception.
- `Rules Glossary > Blinded` (p. 177), `Charmed` (p. 178), `Exhaustion` (p. 181), `Frightened` and
  `Grappled` (p. 182), `Incapacitated` and `Invisible` (p. 184), `Paralyzed`, `Petrified`,
  `Poisoned`, and `Prone` (p. 186), `Restrained` (p. 187), `Stunned` (p. 189), and `Unconscious`
  (p. 191) — the fourteen non-Exhaustion entries in scope and their implications.
- `Playing the Game > D20 Tests > Advantage/Disadvantage` (PDF pp. 6–7) — already the basis of the
  `rollCircumstances` convention established by `mechanic.dnd2024.check.ability` v3/v4.
- `Playing the Game > Bonus Actions` and `> Reactions` (PDF p. 10) plus `Rules Glossary >
  Incapacitated` (p. 184) — the action, Bonus Action, and Reaction prohibitions.

The fourteen conditions in scope: `blinded`, `charmed`, `deafened`, `frightened`, `grappled`,
`incapacitated`, `invisible`, `paralyzed`, `petrified`, `poisoned`, `prone`, `restrained`,
`stunned`, `unconscious`.

## Confirmed schema boundary

The permanent `dnd2024.conditions` meaning and its companion contract and mechanic IDs are bounded
by the following deliberately narrow source-instance rule:

- an entry has a required condition id and an optional entity source derived from the mechanic's
  `source` role, never from caller input;
- one source may apply a condition instance once, while several sources can keep the same
  non-stacking condition effective;
- Charmed, Frightened, and Grappled require a non-self entity source; and
- clearing with a source removes only that source's instance, while clearing without one removes
  all instances of the named condition.

This preserves the source data later rules need without introducing unverified effect IDs, generic
duration records, or a second condition store.

## Planning inventory and overlap result

| Inquiry | Evidence and conclusion |
| --- | --- |
| Existing condition owner | `game.core.world.condition` exists, but its active contract owns one scheduled route-closure entity with clock/route relationships. It is distinct from creature-owned SRD conditions. Searches for `prone`, `poisoned`, `blinded`, `unconscious`, `restrained`, and `incapacitated` otherwise find only exclusion prose in adjacent D&D contracts, such as `procedure.mechanic.dnd2024.saving-throw` reserving persistent conditions for a separate contract. `dnd2024.conditions` remains unowned. |
| D20 circumstance convention | `procedure.mechanic.dnd2024.check.ability` defines `rollCircumstances` as an array of unique `{kind, source}` objects; `kind` is `advantage` or `disadvantage`; same-kind entries never stack; any mixture cancels. `mechanic.dnd2024.check.ability` is at version 4 in `catalog/manifest.json`. |
| D20 consumers | Four mechanics roll a d20 and would need condition awareness: `mechanic.dnd2024.check.ability`, `mechanic.dnd2024.saving-throw`, `mechanic.dnd2024.weapon-attack`, `mechanic.dnd2024.initiative.roll`. Each validates `rollCircumstances` independently today. |
| Composition model | `procedure.mechanic.projection` supports `requirements.children` with `roleBindings`, deterministic child seeds, and frozen serialised child results; a child proposes effects but never applies them, so an effect-free child is the safe shape. `mechanic.dnd2024.weapon-damage.apply` is the worked example. |
| Child input sources — **decisive constraint** | Exactly three, per `MechanicComposer.ResolveInput` (~L223–260) and `ChildMechanicRequirement` (`MechanicModels.cs` ~L190–220): inherit the parent's validated input; a **static literal** object; or `inputFromParentProperty`, a top-level key of the parent input **whose value is an object**. A child's input can never be templated from a validated scalar, a projected component value, or a sibling child's result — all children resolve before the parent source runs. This rules out passing `ability`, `resource`, or an `against` flag into the resolver per invocation, and shapes decision 2 below. |
| Role and component semantics | `RoleRequirement.Optional` is a **role-level** flag (`MechanicModels.cs` ~L178–183). A declared component the entity lacks is simply absent from the projection (`ProjectionResolver.cs` ~L184–186) and never fails it. There is no "optional component"; a role stays required and the mechanic branches on absence explicitly. |
| Event model | E1 is verified. `procedure.event.guard` and `procedure.event.react` are active; notifications are explicitly unavailable. |
| Action economy | Feature 12's `mechanic.dnd2024.turn-budget.spend` is the single spend path this feature extends; Feature 12's plan records that Feature 13 revises that mechanic rather than adding a parallel rule. |
| Selection safety | E2 is verified. Condition names are extremely common English words; every new phrase needs a routing test against all four D20 owners and the Feature 12 spend mechanic. |

## Verified existing dependencies

| Dependency | Evidence |
| --- | --- |
| Source registry | `catalog/world/entities/source.dnd2024.srd-5.2.1.json`: version 5.2.1, publisher, canonical and PDF URLs, CC-BY-4.0 attribution, heading-plus-page locator format. |
| Shared circumstance convention | `mechanic.dnd2024.check.ability` v4 and `mechanic.dnd2024.saving-throw` both implement the identical validated `rollCircumstances` rules; Feature 3's and Feature 4's exit gates covered both modes, same-kind non-stacking, and cancellation. |
| Closed-writer pattern | Features 6 and 7 (`hit-points.write`, `armor-class.write`, `weapon-proficiencies.write`) prove record/correct modes, closed input, fixed `sourceRef`, and rejection-before-effect. |
| Atomic actions and replay | `procedure.mechanic.run`: one transaction, validated-then-applied effect list, recorded seed, version, and projection; failures commit nothing and still record an audit row. |
| Effect exactness | `STATUS.md`: adding a present component, removing an absent one, and reusing a deleted id are faults, not no-ops; every fault in a batch is reported at once with its position. |
| Action economy | Feature 12 Slice 3 (blocked, planned) — `mechanic.dnd2024.turn-budget.spend` with roles `subject` and `encounter`. |

## Recursive dependency analysis

```text
Feature 13: SRD conditions and their D20 Test effects
├─ SRD condition definitions                                       [implemented source basis]
├─ Advantage/Disadvantage circumstance convention                  [implemented: Feature 3]
├─ ability check / saving throw / attack / Initiative resolvers    [implemented: Features 1-5, 8]
├─ mechanic composition with frozen child results                  [implemented kernel]
├─ atomic effects, audit, replay                                   [implemented kernel]
├─ turn budget and its single spend path                           [BLOCKED: Feature 12, Slice 3]
└─ conditions as enforced state                                    [blocked parent]
   ├─ closed condition component + its apply/clear writer          [missing leaf: Slice 1]
   ├─ shared state-to-D20-effect resolver + first consumer         [blocked: Slice 2]
   ├─ saving-throw consumption incl. automatic failure             [blocked: Slice 3]
   ├─ attack consumption, attacker and defender                    [blocked: Slice 4]
   ├─ Initiative consumption                                       [blocked: Slice 5]
   └─ action-economy prohibition                                   [blocked: Slice 6]
```

Every leaf below Slice 1 is a *consumer* of the same resolver. That is deliberate: the alternative
shape — each D20 owner deriving condition effects itself — puts the same fourteen-row table in five
mechanics and is the "duplicated rule logic" the guide's red-flag list forbids.

## Dependency and ownership decisions

1. **One shared resolver, five consumers.** `mechanic.dnd2024.d20-test.state-effects` is an
   effect-free child that reads a creature's condition state and returns derived circumstances,
   flat modifiers, and automatic-outcome verdicts. The four D20 owners and the Feature 12 spend
   mechanic compose it. The alternative — each owner reading `dnd2024.conditions` and applying its
   own table — was rejected because a rule that appears in five places has no owner.

2. **The resolver takes no input and reports the whole derivation; the consumer selects.** This is
   forced by the kernel and is not a style choice. A child's input can only be inherited, a static
   literal, or a top-level *object* property of the parent input — so a consumer cannot pass
   `{"test": "saving-throw", "ability": "dex"}` built from its own validated `ability` string.
   The resolver therefore takes a **static `{}`** and returns a complete `byTest` report:
   circumstances for ability checks, per-ability circumstances and automatic-failure verdicts for
   saving throws, circumstances for attack rolls made by this creature and for attack rolls against
   it, circumstances for Initiative, and the resource `prohibitions`. Each consumer reads the one
   branch it needs out of the frozen child result.

   The "made by" versus "against" distinction is expressed by **which role the child is bound to**,
   not by an input flag: the attack mechanic composes the same child twice, once bound to `subject`
   and once to `target`, and reads `byTest.attackRoll` from the first and `byTest.attackAgainst`
   from the second. Two invocations of one child under different role bindings is exactly what
   `procedure.mechanic.projection` step 6 supports.

   The cost is that the resolver computes branches a given consumer ignores. It is effect-free,
   consumes no randomness, and reads one small component, so the cost is arithmetic rather than
   correctness — and the benefit is that the derivation table exists exactly once.

3. **The resolver is named for what it will be, not for what Slice 2 needs.** It is
   `d20-test.state-effects` rather than `d20-test.circumstances` because Feature 14 will add a flat
   numeric penalty (−2 × Exhaustion level) that is not a circumstance, and Features 24, 26, and 27
   will add further stateful modifiers. Ids are permanent; naming it narrowly now would force
   either a misleading name or a second owner later.

4. **Derived circumstances and caller circumstances are different things and must stay separable.**
   A GM ruling ("attacking from higher ground") is a legitimate transient input and stays in
   `rollCircumstances`. A condition is state and must never be typed in. Each consumer therefore
   merges two lists and reports them separately in its result: `rollCircumstances` (caller) and
   `derivedCircumstances` (resolver). **Every derived circumstance carries a reserved `source`
   string of the exact form `condition:<id>`, and every consumer rejects a caller-supplied
   `source` matching `^condition:` before rolling.** Without that reservation a caller could forge
   "condition:poisoned" and the audit trail would no longer distinguish a rule from a claim.

5. **Merging follows the existing convention unchanged, and this is the whole point.** The merged
   list obeys `mechanic.dnd2024.check.ability` v4's rules exactly: same-kind entries do not stack,
   any mixture of both kinds cancels to normal. A Poisoned creature with GM-granted Advantage rolls
   normally. Feature 13 introduces no new resolution arithmetic; it introduces new *inputs* to
   arithmetic that is already verified.

6. **A condition is non-stacking, but its causes are not discarded.** `dnd2024.conditions` holds
   canonically ordered *condition instances*, unique by `(condition, sourceEntityId)`, where
   `sourceEntityId` may be absent for an unattributed instance. The resolver derives a condition as
   effective when at least one instance exists, so the SRD's non-stacking rule still produces one
   set of mechanical effects. Retaining separate sources is necessary: removing one fear, charmer,
   or grappler must not silently remove another. Exact duplicate instances are faults. Exhaustion,
   which has a numeric level rather than separate sources, remains Feature 14's extension of this
   same component.

7. **Missing and empty are different, and both are legal.** An absent `dnd2024.conditions` means
   the creature has never been admitted to the condition system; a consumer treats it as a hard
   failure only where a condition is required, and otherwise as "unknown". A present component with
   `entries: []` means "known to have no conditions". The writer's `record` mode creates the empty
   component; `apply` requires it to exist. This preserves the missing-vs-empty distinction that
   `procedure.mechanic.dnd2024.check.ability` already enforces for skill state.

   **Consequence to plan for:** every existing test creature must gain an empty condition component,
   or every D20 consumer must tolerate absence. Slice 2 chooses *tolerate absence as no
   conditions*, but only because the resolver reports `conditionsKnown: false` in its result, so the
   distinction survives into the audit record rather than being flattened. Any consumer that hides
   `conditionsKnown` is defective.

   Note the precise mechanism, since "optional component" is not a kernel concept: the resolver's
   `subject` role is **required** and declares `dnd2024.conditions`; a subject that lacks the
   component simply has it absent from the projection, which does not fail resolution. The branch is
   the mechanic's, not the projection layer's.

8. **Petrified owns its condition immunity at the condition boundary.** `petrified` excludes every
   stored `poisoned` instance. Applying Poisoned to an effective Petrified creature fails;
   applying Petrified removes Poisoned instances in the same complete replacement. Clearing
   Petrified never revives a previously removed Poisoned instance. This is condition-state
   compatibility, not Feature 15's separate damage-type immunity/resistance work.

9. **`Incapacitated` is enforced where the resource is spent, not where the rule runs.** Feature 12
   Slice 3's spend mechanic is the single chokepoint; Feature 13 revises it. Adding an
   `Incapacitated` check to each resolver would be both incomplete (resolvers are effect-free and
   nothing forces a caller through them) and duplicated.

10. **Speed-0 conditions block movement spending, not movement restoration.** Grappled, Paralyzed,
   Petrified, Restrained, Stunned, and Unconscious set Speed to 0. Feature 12 restores
   `movementRemainingFeet` from the recorded maximum at turn start regardless; Feature 13 makes the
   *spend* fail. Rewriting the restore to zero would destroy the recorded maximum when the
   condition ended.

11. **Feature 13 never causes a condition.** It provides `apply`; the cause always calls it. This
   keeps Feature 17's dying rules, Feature 32's spells, and Feature 22's grapples as the owners of
   their own consequences.

## The derivation table

This is the testable core of Slices 2–6. Each row is one condition, what it derives, and — where a
rule is deferred — the feature that owns the missing precondition.

| Condition | Own D20 Tests | Tests against this creature | Deferred part and owner |
| --- | --- | --- | --- |
| Blinded | Attack rolls: disadvantage | Attack rolls: advantage | Auto-fail sight-based checks → Feature 34. Both halves are required: a Blinded attacker striking a Blinded defender must cancel, which Slice 4 tests. |
| Charmed | — | — | Cannot attack/target the charmer; charmer's social advantage → Feature 22 / Feature 38 (needs a named other creature) |
| Deafened | — | — | Auto-fail hearing-based checks → Feature 34 |
| Frightened | — | — | Ability checks and attacks at disadvantage *while the source is in line of sight*; cannot move closer → Feature 20/34 (needs line of sight and a named source) |
| Grappled | — | — | Attacks at disadvantage against targets other than the grappler → Feature 22 (needs the grappler's identity); Speed 0 → Slice 6 |
| Incapacitated | Initiative roll: disadvantage | — | Concentration broken → Feature 18; no Action/Bonus Action/Reaction → Slice 6 |
| Invisible | Attack rolls: advantage; Initiative roll: advantage | Attack rolls: disadvantage | Cannot be seen; Hide interaction → Feature 34 |
| Paralyzed | Str and Dex saves: automatic failure | Attack rolls: advantage | Automatic Critical Hit from within 5 feet → Feature 20/21; Speed 0 → Slice 6; also Incapacitated |
| Petrified | Str and Dex saves: automatic failure | Attack rolls: advantage | Resistance to all damage → Feature 15; Poisoned incompatibility → Slice 1; Speed 0 → Slice 6; also Incapacitated |
| Poisoned | Attack rolls: disadvantage; ability checks: disadvantage | — | — |
| Prone | Attack rolls: disadvantage | — | Advantage within 5 feet / disadvantage beyond → Feature 20/21; crawling movement cost → Feature 20 |
| Restrained | Attack rolls: disadvantage; Dex saves: disadvantage | Attack rolls: advantage | Speed 0 → Slice 6 |
| Stunned | Str and Dex saves: automatic failure | Attack rolls: advantage | Speed 0 → Slice 6; also Incapacitated |
| Unconscious | Str and Dex saves: automatic failure | Attack rolls: advantage | Automatic Critical Hit from within 5 feet → Feature 20/21; Speed 0 → Slice 6; drops held items → Feature 23; also **Incapacitated and Prone** |

**Conditions that imply other conditions.** Paralyzed, Petrified, and Stunned each include
Incapacitated in the SRD, and **Unconscious includes both Incapacitated and Prone**. Two shapes were
considered: storing the implied condition alongside the stated one, or deriving it. **Decision:
derive it.** The writer stores only what was applied; the resolver expands implications and reports
both `entries` (stored) and `effectiveConditions` (stored plus implied) in its result. Storing
implications would mean clearing Stunned had to know whether Incapacitated was also applied
independently — two sources of truth for one fact, and an unclearable residue. The implication map
is fixed, closed, non-recursive by construction, and lives in the resolver's contract. Confirm each
implication against the Rules Glossary during the pre-write re-read; Unconscious's Prone implication
in particular is easy to miss and changes what a defender derives. An implied condition inherits the
source identities of the stored condition that implies it. The resolver therefore also returns a
canonical `sourcesByCondition` map, allowing later position/target rules to compare the relevant
actor with a recorded charmer, fear source, or grappler without inventing a second source store.

## Slice order and stop gates

| Order | Slice | Starts only when | Exit gate |
| --- | --- | --- | --- |
| 1 | Source-aware condition component and its apply/clear writer | Feature 12 verified; provenance schema confirmed | A creature's condition instances can be recorded, applied, and cleared through one closed writer; duplicate source instances, unknown ids, ordering, incompatible Petrified/Poisoned state, missing, and corrupt cases all reject without state change. |
| 2 | Shared state-effects resolver and ability-check consumption | Slice 1 verified | An ability check by a Poisoned creature rolls with disadvantage that nobody typed in, reports the derived circumstance separately from caller circumstances, and rejects a forged `condition:` source. |
| 3 | Saving-throw consumption and automatic failure | Slice 2 verified | A Restrained creature's Dex save rolls with disadvantage; a Paralyzed creature's Str and Dex saves fail automatically without consuming randomness; every other ability's save is unaffected. |
| 4 | Weapon-attack consumption, attacker and defender | Slice 3 verified | An attack derives circumstances from both creatures' conditions, cancels correctly when they oppose, and leaves Feature 8's hit/critical arithmetic byte-identical. |
| 5 | Initiative-roll consumption | Slice 4 verified | An Incapacitated creature rolls Initiative with disadvantage and an Invisible one with advantage; the resulting order and Feature 5's tie policy are otherwise unchanged. |
| 6 | Action-economy prohibition | Slice 5 verified | An Incapacitated creature cannot spend an Action, Bonus Action, or Reaction, and a Speed-0 creature cannot spend movement, each failing with a distinct reason and zero effects. |

## Slice 1 — condition component and its apply/clear writer

### Runtime artifacts

| Artifact | Proposed ID / category | Change |
| --- | --- | --- |
| Governing contract | `procedure.mechanic.dnd2024.conditions` in `ruleset.dnd2024.core.state.conditions` | New. Governs the vocabulary, the component, and the writer. |
| Component definition and schema | `dnd2024.conditions` | New closed creature-owned component. |
| Writer | `mechanic.dnd2024.conditions.write` in `ruleset.dnd2024.core.state.conditions`, scope `dnd2024-srd-5.2.1` | New deterministic writer with `record`, `apply`, and `clear` modes plus an optional `source` role. |
| Regression coverage | `CatalogFeature13Tests` | New fresh-import coverage. |

### Governing contracts and source locator

Before writing, re-read `procedure.system.create-feature`,
`procedure.mechanic.dnd2024.skill-proficiencies` (the closest stable-list component),
`procedure.mechanic.dnd2024.hit-points` (closed-writer discipline), `procedure.mechanic.run`,
`procedure.mechanic.projection`, `procedure.world.change`, and
`procedure.game.core.world.condition` (the distinct route-closure owner). Re-search `condition`,
each of the fourteen condition ids, `apply condition`, `clear condition`, and `remove condition`
against the authored catalog and execute routing tests against both condition namespaces.

`sourceRef` is fixed to
`{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Rules Glossary"}` — the component covers the
whole condition vocabulary, so the per-condition heading belongs in the contract's prose, not in a
component field that would then have to be an array of locators.

### Data/input contract and required state

- The component is a closed object with exactly `entries` and `sourceRef`.
- `entries` is a bounded array of at most 100 closed condition-instance objects. Each has
  `condition: <stable id>` and optional `sourceEntityId`; source identity is an entity id derived
  only from the optional `source` role, never an input string. The writer verifies a supplied source
  role resolves to an entity when it adds an instance; a persisted id is historical provenance and
  is not dynamically re-queried. Entries are unique by the pair
  `(condition, sourceEntityId)`, sorted by the fixed condition order and then by source identity
  with an absent source first. Feature 14 extends the Exhaustion instance with its `level`; it does
  not create another condition store.
- The vocabulary enum contains exactly the fourteen ids. `exhaustion` is **not** in it; Feature 14
  adds it.
- Writer input is exactly `{"mode": ..., ...}` where:
  - `record` takes no further field, requires absence, and applies one `component.add` of
    `{"entries": [], "sourceRef": ...}`.
  - `apply` takes `"conditions"`, a nonempty unique array of 1–14 known ids, requires a valid
    existing component, and adds instances using the optional `source` role's entity id when it is
    bound. `charmed`, `frightened`, and `grappled` require that non-self source role. An exact
    existing `(condition, sourceEntityId)` pair is rejected, while an independent source is legal.
  - `clear` takes `"conditions"` with the same shape. With a bound `source` role it removes only
    matching source instances; without it, it explicitly clears every source instance of each named
    condition. A requested condition with no matching instance fails.
- `apply` and `clear` each apply exactly one `component.set` carrying the complete re-sorted list.
  Applying Petrified also removes every Poisoned instance in that same effect; applying Poisoned to
  an effective Petrified creature fails.
- Rejected before any effect: unknown ids, wrong case, duplicates within the input array, an
  exact duplicate source instance under `apply`, an absent matching instance under `clear`, an empty
  array under `apply` or `clear`, a non-array, a bare string, a source id in input, and every
  caller-supplied `sourceRef`, `entries`, `effectiveConditions`, `level`, duration,
  source-of-condition, `effects`, or extra key.

### Recording behavior

1. Validate closed input, optional-role identity, then — for every mode but `record` — the complete
   existing component, its `sourceRef`, vocabulary membership, instance uniqueness, source binding,
   Petrified/Poisoned compatibility, and canonical ordering.
2. Reject before constructing an effect. No randomness is consumed.
3. Compute the new instance list and re-sort it into canonical order regardless of input order, so
   two databases reaching the same condition state hold byte-identical data.
4. Propose exactly one effect and return mode, bound source id/null, before/after instances, added
   instances, removed instances, and any Poisoned instances removed by Petrified immunity.

### Invariants, failure behavior, and non-goals

- One condition component per creature; `record` never overwrites, the other modes never create.
- A corrupt stored list — unknown id, duplicate instance, malformed source identity, wrong
  order, incompatible Petrified/Poisoned pair, wrong `sourceRef`, or malformed JSON — is rejected,
  never silently repaired or re-sorted into validity.
- The writer applies no condition as a consequence of anything, changes no Hit Points, no budget,
  and no other entity, and emits no event.
- There is deliberately no unrestricted `replace`: it would allow a caller to forge several source
  identities in one input. A later migration/correction owner must define a separately reviewed
  source-safe repair boundary.

### Slice 1 implementation sequence

1. Confirm Feature 12 is verified, confirm the source-instance schema, and record clean focused-test
   and `roleplay validate catalog` baselines.
2. Re-read the listed contracts; repeat overlap and routing searches for all fourteen condition
   words and the distinct world-condition namespace.
3. Author contract, component definition and schema, mechanic `.md`/`.js` pair, manifest entries,
   and the focused fresh-import test as catalog files first.
4. Run `roleplay validate catalog`; resolve every schema, write-side, or routing failure in the
   disposable validation database. Do not import into the persistent database in this slice.
5. In fresh disposable test databases, exercise the full acceptance matrix. Do not alter catalog
   fixtures or persistent game state.
6. Run focused tests, the full suite, `roleplay validate catalog`, and `git diff --check`; record
   evidence in the slice receipt; mark only Slice 1 verified; stop for review.

### Slice 1 acceptance matrix

| Class | Required assertion |
| --- | --- |
| Happy path | `record` creates `entries: []`; unattributed `apply ["poisoned"]` adds one source-absent instance; applying `charmed` with a source role records that entity id; applying a second `charmed` source preserves both instances in canonical order. |
| Boundaries | Applying every individually valid source-free condition succeeds, and a mutually compatible set is stored in canonical order; clearing all instances returns `entries: []` rather than removing the component. A source-specific clear removes only that source's instance. |
| Differential | Two creatures differing only in one condition instance or source id have components differing in exactly that instance. |
| Closed input | Unknown id, wrong case (`"Poisoned"`), `exhaustion`, duplicate within the array, empty array under `apply`/`clear`, bare string, non-array, non-object root, extra key, and supplied `sourceEntityId`/`sourceRef`/`level`/`duration`/`effects` each fail with zero effects. |
| Missing state | `apply` and `clear` against an absent component each fail with a distinct reason. |
| Existing state | `record` against a present component fails atomically; original bytes unchanged. |
| Duplicate/absent | Re-applying the exact `(condition, source)` pair and clearing an absent matching instance each fail with distinct reasons and byte-identical state; the same condition from an independent source succeeds. |
| Source requirements | Charmed, Frightened, and Grappled without a source role, with a self source, or with a missing source entity fail. Source-free conditions do not require the role. |
| Condition immunity | Applying Poisoned while Petrified fails. Applying Petrified to a Poisoned creature removes Poisoned in the same single replacement and reports it; later clearing Petrified does not restore Poisoned. |
| Corrupt state | Stored unknown id, duplicate source instance, malformed source identity, wrong order, incompatible Petrified/Poisoned state, wrong `sourceRef`, extra field, and malformed JSON are each rejected by every non-`record` mode before any effect. A source entity is verified when its instance is added; a later-deleted source remains clearable historical provenance. |
| Determinism | Equivalent databases, equivalent input in different array orders, byte-identical stored result; no `ctx.randomInt` call. |
| Routing | `apply the poisoned condition` and `clear the prone condition` select only this writer. Critically, `poison damage`, `prone target`, `blinded by the flash`, `make a saving throw`, `attack the target`, and `spend my action` must **not** select it — condition names are ordinary English and this is the highest collision risk in the ruleset so far. |
| Effects | Exactly one effect of the expected type per success; zero on every rejection. |
| State integrity | Before/after byte comparison on the subject and on one untouched sibling for every rejection. |
| Readback | Contract, definition, schema, and mechanic are read from the fresh test database at intended version and scope. |
| Restoration | Disposing the fresh test database removes fixtures; Feature 10 and 11 catalog baselines remain untouched. |
| Repository | `roleplay validate catalog`, focused tests, the full suite, and `git diff --check` pass; no persistent import occurs. |

### Slice 1 exit gate

Every row passes with recorded selected mechanic id and version, parsed result fields, exact effect
counts, before/after bytes, disposable-database readback, and repository checks. Slice 2 remains
blocked until a new review authorizes it.

## Slice 2 — shared state-effects resolver and ability-check consumption

### Status and prerequisite

Blocked until Slice 1 is verified. Adds `procedure.mechanic.dnd2024.d20-test.state-effects` and
`mechanic.dnd2024.d20-test.state-effects`; revises `procedure.mechanic.dnd2024.check.ability` and
`mechanic.dnd2024.check.ability` (to v5).

### Data/state and resolution contract

- The resolver has one **required** role `subject` declaring `dnd2024.conditions`, and closed input
  of exactly `{}` — a static literal, per decision 2. It takes no `test`, no `ability`, and no
  `against` flag, because the kernel cannot template any of them into a child's input.
- It returns `conditionsKnown` (false when the component is absent), stored `entries`,
  `effectiveConditions` (stored plus implied), `sourcesByCondition` (canonical source-id arrays,
  including inherited sources for implied conditions), a `byTest` report, `derivedModifiers` (empty
  in this slice; Feature 14 fills it), `prohibitions` (empty in this slice; Slice 6 fills it), and
  the source reference. It returns `effects: []` and consumes no randomness.
- `byTest` is a closed object with exactly these branches, each a list of unique
  `{kind, source:"condition:<id>"}` entries in canonical order by condition id then kind:
  - `abilityCheck` — circumstances on ability checks this creature makes.
  - `attackRoll` — circumstances on attack rolls this creature makes.
  - `attackAgainst` — circumstances on attack rolls made against this creature.
  - `initiative` — circumstances on this creature's Initiative roll.
  - `savingThrow` — an object keyed by the six ability ids, each carrying its own circumstance list
    and an `automaticFailure` reason or `null`. Populated in Slice 3; every key present and empty in
    Slice 2, so the shape never changes under a consumer.
- The resolver never rolls, never merges with caller circumstances, and never decides an outcome.
  It reports; the consumer composes.
- `mechanic.dnd2024.check.ability` v5 declares the resolver as a child bound to its own `subject`
  role with `inheritInput: false` and a static `{}` input, reads `byTest.abilityCheck` from the
  frozen child result, merges those entries into the validated `rollCircumstances`, and derives the
  roll mode from the merged list using the **unchanged** v4 rules.
- v5 rejects any caller `rollCircumstances` entry whose `source` matches `^condition:` before
  rolling, with a distinct reason.
- v5's result adds `derivedCircumstances`, `mergedCircumstances`, and `conditionsKnown` while
  keeping every existing v4 field with identical semantics.

### Acceptance and exit gate

Prove: a Poisoned creature's check rolls disadvantage with no caller input and reports the derived
source as `condition:poisoned`; a Charmed creature exposes its charmer only in
`sourcesByCondition` and creates no premature attack restriction; a Poisoned creature with one caller Advantage rolls normal
(cancellation), and the same creature with a caller Disadvantage still rolls disadvantage
(non-stacking); a creature with no conditions rolls exactly as v4 did for the same seed, ability,
DC, skill, and state — **a byte-for-byte differential against a recorded v4 result is the strongest
assertion available here and is required**; an absent conditions component reports
`conditionsKnown: false` and rolls normally; a forged `condition:` source is rejected with zero
effects; every v4 acceptance row still passes; the child result is present, frozen, and reported
with its own mechanic id, version, and seed; replay is exact; routing is unchanged; and both new
and revised artifacts are loaded from the fresh validation database while v4 remains readable.
Run the full suite, `roleplay validate catalog`, and `git diff --check`; no persistent import
occurs. Slice 3 stays blocked.

## Slice 3 — saving-throw consumption and automatic failure

### Status and prerequisite

Blocked until Slice 2 is verified. Revises the resolver's contract and mechanic (to v2), and
`procedure.mechanic.dnd2024.saving-throw` and `mechanic.dnd2024.saving-throw`.

### Data/state and resolution contract

- The resolver v2 populates `byTest.savingThrow`. Under the `str` and `dex` keys it sets
  `automaticFailure` to `"condition:<id>"` when any of `paralyzed`, `petrified`, `stunned`, or
  `unconscious` is effective, naming the canonically first where several apply; the other four
  ability keys keep `automaticFailure: null`.
- It adds Restrained's disadvantage as a circumstance under the `dex` key only.
- The saving-throw mechanic composes the resolver as a child bound to `subject` with
  `inheritInput: false` and a static `{}` input, then selects `byTest.savingThrow[<validated ability>]`
  from the frozen child result. Selecting by ability happens in the **parent's** source, after
  validation, which is what makes a static child input sufficient.
  **When the selected `automaticFailure` is non-null it returns `succeeded: false` without calling
  `ctx.randomInt`**, with `rollMode`, `roll`, and `total` null and `rolls` empty — exactly the
  shape the existing `voluntaryFailure: true` path already produces, reusing a verified result
  contract rather than inventing a second "no roll" shape.
- Interaction with `voluntaryFailure`: an automatic condition failure and a voluntary failure are
  both failures and are not an error together; the result reports both `automaticFailure` and
  `voluntaryFailure` so the audit says which applied. Order of reporting is fixed by the contract.

### Acceptance and exit gate

Prove: Restrained gives disadvantage on a Dex save and nothing on a Wis save; Paralyzed
auto-fails Str and Dex saves at DC 0 and leaves Con, Int, Wis, and Cha saves entirely unaffected
including their proficiency bonus arithmetic; an auto-failed save consumes no randomness, verified
by seed-advance comparison rather than by inspection; Petrified, Stunned, and Unconscious each
auto-fail identically and the reported reason is the canonically first when several apply; an
unconditioned creature's save is byte-identical to the pre-revision result for the same seed;
`voluntaryFailure` combined with an automatic failure reports both; every Feature 4 acceptance row
still passes; replay, routing, zero effects, and readback all hold. Full suite, verify, diff-check.
Slice 4 stays blocked.

## Slice 4 — weapon-attack consumption, attacker and defender

### Status and prerequisite

Implemented and verified after Slice 3. Revises the resolver (to v3) and
`procedure.mechanic.dnd2024.weapon-attack` and `mechanic.dnd2024.weapon-attack`.

### Data/state and resolution contract

- The attack mechanic composes the resolver **twice**, under two result keys: once bound to
  `subject` and once bound to `target`, both with `inheritInput: false` and a static `{}` input. It
  reads `byTest.attackRoll` from the `subject` invocation and `byTest.attackAgainst` from the
  `target` invocation. Two child invocations of one mechanic under different role bindings is
  exactly what `procedure.mechanic.projection` step 6 supports, and the role binding — not an input
  flag — is what makes the two perspectives distinct. This is the concrete reason decision 2's
  no-input shape works.
- The attack's `target` role declares `dnd2024.conditions` alongside its existing
  `dnd2024.armor-class`. The role stays required; a target lacking the component simply has it
  absent, and the child reports `conditionsKnown: false`.
- Both derived lists and the caller's list merge into one, and the roll mode comes from the merged
  list under the unchanged convention. Blinded attacker (disadvantage) attacking a Blinded
  defender (advantage) cancels — that is the correct SRD outcome and is a required test.
- Feature 8's hit determination, natural-20/1 classification, Proficiency Bonus arithmetic, and
  effect-free contract are untouched. The result gains `attackerDerivedCircumstances`,
  `targetDerivedCircumstances`, `mergedCircumstances`, and both `conditionsKnown` flags.
- `byTest.savingThrow` is ignored by this consumer; there is no SRD automatic attack outcome that
  does not depend on distance.

### Acceptance and exit gate

Prove: each of Blinded, Invisible, Poisoned, Prone, and Restrained on the attacker produces exactly
the tabled circumstance; each of Blinded, Invisible, Paralyzed, Petrified, Restrained, Stunned, and
Unconscious on the defender produces exactly the tabled circumstance; opposing pairs cancel;
same-kind pairs do not stack into a third die; an unconditioned pair is byte-identical to the
pre-revision result for the same seed; two child results are present, frozen, and separately
reported with their own seeds; a caller-forged `condition:` source is rejected; every Feature 8
acceptance row including natural-20 critical classification still passes; the attack still applies
zero effects; replay, routing, and readback hold. Focused regression coverage and catalog
validation passed; Slice 5 is now the next separately reviewed implementation pass.

## Slice 5 — Initiative-roll consumption

### Status and prerequisite

Implemented and verified after Slice 4. Revises the resolver (to v4) and
`procedure.mechanic.dnd2024.initiative` / `mechanic.dnd2024.initiative.roll`.

### Data/state and resolution contract

- The resolver returns a disadvantage circumstance `condition:incapacitated` for
  `test: "initiative"` when `incapacitated` is effective — including when it is implied by
  Paralyzed, Petrified, Stunned, or Unconscious rather than applied directly. That implication path
  is why the derived-not-stored implication decision must be proven here specifically.
- It returns an **advantage** circumstance `condition:invisible` for `test: "initiative"` when
  `invisible` is effective. Initiative is the one test where two conditions push in opposite
  directions, so an Invisible *and* Incapacitated participant rolls normally by cancellation — a
  required test, and the reason both rules land in the same slice.
- The Initiative roll composes the resolver as a child bound to `subject` with a static `{}` input
  and merges `byTest.initiative` as before.
- `mechanic.dnd2024.encounter-initiative-order` is **not** revised. Ordering, ties, and the
  snapshot stay exactly as Feature 5 verified them; only the per-participant roll changes.

### Acceptance and exit gate

Prove: an Incapacitated participant rolls Initiative at disadvantage; a Stunned participant does
too, by implication, with the reported source still `condition:incapacitated`; an Invisible
participant rolls at advantage; a participant that is both rolls normally by cancellation, with both
derived circumstances still reported; an unconditioned
participant's roll is byte-identical to the pre-revision result for the same seed; the encounter
order mechanic's composed results, tie policy, and snapshot bytes are unchanged; Feature 5's
arbitrary-roster matrix still passes in full; replay, routing, and readback hold. Focused
regression coverage and catalog validation passed; Slice 6 is now the next separately reviewed
implementation pass.

## Slice 6 — action-economy prohibition

### Status and prerequisite

Implemented and focused/catalog-verified after Slice 5. Revises `procedure.mechanic.dnd2024.turn-budget` and
`mechanic.dnd2024.turn-budget.spend` (Feature 12 Slice 3), and the resolver's contract to add the
prohibition query. Adds no new mechanic.

### Data/state and resolution contract

- The spend mechanic's `subject` role declares `dnd2024.conditions` (required role, absence legal
  and reported), and it composes the resolver as a child bound to `subject` with a static `{}`
  input.
- The resolver populates `prohibitions`: an ordered unique list of
  `{"resource": ..., "reason": "condition:<id>"}` covering **all** resources, not just a requested
  one — the parent selects the entry matching its validated `resource` after validation.
  `incapacitated` (stated or implied) prohibits `action`, `bonusAction`, and `reaction`. Each of
  `grappled`, `paralyzed`, `petrified`, `restrained`, `stunned`, and `unconscious` prohibits
  `movement`.
- A prohibited spend fails with the condition-specific reason and applies zero effects. It is a
  distinct failure from "already spent", and the two must be separately assertable.
- `freeInteraction` is not prohibited by `Incapacitated` in this slice. The SRD permits the one free
  interaction during movement or an Action; Incapacitated removes Actions but not movement on its
  own. A Speed-0 condition can make a particular interaction impossible in a later positional
  feature, but Feature 13 does not invent that additional requirement.

### Acceptance and exit gate

Prove: an Incapacitated creature fails to spend Action, Bonus Action, and Reaction, each with the
condition reason and byte-identical state; a Stunned creature fails the same three by implication;
each of the six Speed-0 conditions fails a movement spend; a prohibited spend on an already-spent
resource reports the prohibition reason, not the exhaustion reason, and the precedence is fixed by
the contract and tested; an unconditioned creature's spends are byte-identical to the Feature 12
Slice 3 results; clearing the condition restores the ability to spend within the same turn; every
Feature 12 Slice 3 acceptance row still passes; replay, routing, effect-exactness, disposable
readback, and cleanup all hold. Full suite, `roleplay validate catalog`, `git diff --check`.

Focused Slice 6 coverage and catalog validation passed. The isolated serial full-suite acceptance
run passed after the shared catalog stabilized. Feature 13 is verified; proceed only to Feature 14.

## Forward dependencies this plan deliberately leaves open

| Concern | Owner | Note |
| --- | --- | --- |
| Exhaustion | Feature 14 | Revises `dnd2024.conditions` (adds the id and a `level` field) and the resolver (fills `derivedModifiers`). It must preserve the source-instance entries and use its own explicit Exhaustion level semantics; it must not create a second condition store or pretend the existing no-input resolver accepts per-test fields. |
| Petrified's Resistance to all damage | Feature 15 | Feature 15's mitigation resolver reads conditions; Feature 13 does not model damage. |
| Source-specific condition endings | Features 17, 22, and 32 | A cause that ends only its own effect binds the same `source` role to `clear`; a cure that removes every source deliberately calls clear without it. No later owner may replace the condition list with caller-authored source ids. |
| Unconscious from 0 Hit Points | Feature 17 | Feature 17 applies it from an event reaction. **A subscribed mechanic may not declare child mechanics** (`procedure.subscription.create`), so that reaction cannot compose this feature's writer. Its condition-integrity guard must validate the source-instance shape, ordering, source existence, and Petrified/Poisoned compatibility before a reaction writes it. |
| Concentration broken by Incapacitated | Feature 18 | Nothing to break yet. |
| Every positional condition effect | Features 20–22 | Enumerated per-condition in the derivation table. |
| Sense-based automatic failure | Feature 34 | Blinded and Deafened. |

## Plan-quality audit

1. Yes — one capability: conditions as enforced state, with explicitly enumerated non-goals and a
   per-condition deferral column rather than silent omission.
2. Yes — source identity, headings, and verified PDF pages are concrete.
3. Yes — all fourteen condition words plus `condition`, `apply`, and `clear` were searched; the
   distinct world route-closure condition owner and adjacent D&D exclusions were read and separated.
4. Partly — kernel, Feature 3/4/5/8, and E1/E2 rows cite artifacts and named tests; the Feature 12
   rows cite an unimplemented plan, which is why this whole feature is blocked.
5. Yes — Slice 1 is a standalone leaf; Slices 2–6 are consumers of one resolver, each independently
   testable and each leaving a valid system.
6. Yes — the permanent source-instance semantics, including the three source-required conditions
   and source-specific clearing, are explicit before a schema is authored.
7. Yes — Slice 1 creates the component with its only safe write path; every later slice revises an
   existing owner rather than adding a sibling.
8. Yes — Slice 1 alone is named as next, and only once Feature 12 is verified.
9. Yes — missing versus empty is explicit and is preserved into the result as `conditionsKnown`.
10. Yes — the derivation table, the implication map, the merge rules, effect counts, and result
    fields are all testable without guessing.
11. Yes — every slice's gate requires a byte-identical differential against the pre-revision
    behavior, which is the assertion most likely to catch a regression in five verified mechanics.
12. Yes — repository-mode disposable catalog validation and persistent-import boundaries are stated.
13. Yes — disposable database cleanup and baseline preservation are explicit.
14. Yes — each slice has an objective all-or-nothing exit gate.
15. Yes — no JavaScript, commit payload, or duplicate JSON Schema is embedded.
16. Yes — this planning pass stops before implementation.

## Kernel constraints these plans were checked against

Verified by reading the kernel source during the planning pass, not assumed. Every plan in the
Features 12–17 block depends on all four:

1. **Contents carry no components.** `ProjectionResolver.cs` (~L196–200) materialises each contained
   entity as `new ContainedProjection(Id, Name, Slot)` and nothing more. A role's declared components
   are projected onto the role entity alone. The only way to see a contained entity's components is a
   declared child with `forEachContentsOf` and `roleBindings: {"<role>": "$item"}`, whose own role
   requirements decide what is projected. `mechanic.dnd2024.encounter-initiative-order` is the live
   worked example.
2. **A child's input has exactly three sources.** `MechanicComposer.ResolveInput` (~L223–260):
   inherit the parent's validated input, a static literal object, or `inputFromParentProperty` — a
   top-level key of the parent input whose value is an object. All children resolve before the parent
   source runs, so no child input can be templated from a sibling child's result, a projected
   component value, or a validated scalar.
3. **`component.set` is an upsert.** `EffectApplier.cs` (~L198–220) faults `ComponentAdd` on a
   present pair and `ComponentRemove` on an absent one. `ComponentSet` does neither; it emits
   `world.component.replaced` with `before: null`. Choosing between add and set is therefore a real
   decision with a silent failure mode, not a formality.
4. **There is no "optional component".** `RoleRequirement.Optional` (`MechanicModels.cs` ~L178–183)
   is a role-level flag. A declared component the entity lacks is simply absent from the projection
   and never fails it. Roles stay required; mechanics branch on absence and report it.

## Plan-change rule

Stop and revise before implementation if:

- The SRD re-read shows a condition effect, implication, condition immunity, or free-interaction
  boundary this table has misstated.
- Feature 12 ships a spend mechanic without an `encounter` role or with a different chokepoint,
  which would invalidate decision 9.
- The composition layer turns out not to support two invocations of one child mechanic under
  different role bindings within one parent, which would invalidate Slice 4's shape and, with it,
  decision 2. Descend into that kernel question rather than splitting the resolver into attacker and
  defender variants.
- `MechanicComposer` gains the ability to template a child's input from a validated parent value.
  That would make a per-test resolver input legal and decision 2's whole-report shape optional
  rather than forced — worth revisiting, but only deliberately.
- A repository search finds a D&D condition owner or a non-entity source form needed by a later
  condition cause. In the latter case, descend to an explicit source-reference contract before
  widening this schema.

Descend to a new dependency rather than duplicating the derivation table into a consumer, storing
implied conditions, accepting caller-supplied condition circumstances, or bundling Feature 14's
Exhaustion into this feature.
