# Feature 15 dependency plan — damage types, Resistance, Immunity and Vulnerability

Status: **Verified — all four slices are implemented and accepted. Stop before Feature 16.**
Last updated: 2026-08-21

## Execution rule

Planning-only artifact under `AGENTS.md` and the active `procedure.system.create-feature`. Repository
catalog files are the development authority. Each implementation pass completes one lowest slice,
validates the catalog in a fresh disposable database, records objective evidence, and stops for
review. A persistent catalog import belongs only to an explicit integration-play or release
boundary. This plan creates no procedure, component, mechanic, fixture, or game state.

**Roadmap correction requested.** `ROADMAP.md` lists Feature 15's dependency as "Feature 9". This
plan establishes that it is **Feature 9 and Feature 13** — Petrified grants Resistance to all
damage, and a mitigation resolver that cannot see conditions would be wrong from the day it
shipped. Update the roadmap row when this plan is accepted.

## Target capability

When a creature takes damage, the amount that actually reaches its Hit Points reflects what that
creature is made of: a fire-immune creature takes none, a resistant one takes half, a vulnerable
one takes double — computed by the system, in the SRD's order, from stored facts rather than from a
number the GM worked out beforehand.

### Included

- One canonical thirteen-value damage-type vocabulary shared by every rule that deals damage.
- One creature-owned mitigation component listing Resistances, Immunities, and Vulnerabilities.
- One shared, effect-free resolver reporting a creature's full mitigation profile, including
  Petrified's Resistance to all damage, plus the contract-specified arithmetic that turns a raw
  amount and type into a final amount by applying Immunity, then Resistance, then Vulnerability.
- Revision of `mechanic.dnd2024.weapon-damage.apply` so that the only existing damage-to-Hit-Points
  path composes the resolver instead of applying a raw amount.
- A registered `dnd2024.damage.dealt` event carrying everything a later rule needs to react to
  damage, including the overkill that the Hit Point component clamps away.

### Excluded

- Healing, temporary Hit Points, and the order in which temporary Hit Points absorb damage
  (Feature 16, which revises this feature's resolver and event rather than adding a second one).
- Every consequence of reaching 0 Hit Points: unconsciousness, death saves, instant death, and
  massive-damage death (Feature 17, which subscribes to this feature's event).
- Damage transfer, shared damage, damage absorption, damage thresholds, and half-damage-on-a-save.
  The last is a real SRD rule (`Saving Throws and Damage`) but it belongs to the feature that owns
  the save-based effect, which is Feature 32.
- Sources of Resistance beyond stored state and Petrified: class features, spells, magic items, and
  species traits (Features 26, 27, 29, 31, 32). Each grants by writing the component this feature
  creates.
- Non-weapon damage sources. Feature 15 makes the resolver reusable and proves it through the one
  existing damage path; a spell that deals damage arrives with Feature 32.
- Any change to how damage dice are rolled. `mechanic.dnd2024.weapon-damage.roll` is untouched.

## Official source basis

`source.dnd2024.srd-5.2.1`, *System Reference Document 5.2.1* (2025-05-01, CC-BY-4.0). Locators,
all within `Playing the Game > Damage and Healing` (PDF p. 17):

- `> Damage Types`: each instance of damage has a type; the types are listed in the Rules Glossary.
- `> Resistance and Vulnerability`: Resistance halves damage of that type against you, rounding
  down; Vulnerability doubles it. Both are applied **only after all other modifiers to damage** and
  only once per instance however many sources grant them; when both apply, Resistance is applied
  first and Vulnerability second.
- `> Immunity`: Immunity to a damage type means you take no damage of that type.
- `Rules Glossary > Damage Types` — the enumerated list.

The thirteen types: `acid`, `bludgeoning`, `cold`, `fire`, `force`, `lightning`, `necrotic`,
`piercing`, `poison`, `psychic`, `radiant`, `slashing`, `thunder`.

The adjustment order is settled: after any outside modifiers owned by the damage cause, apply
Immunity; then Resistance (halve and round down); then Vulnerability (double). For example, if a
prior modifier reduces 28 damage by 5, then Resistance and Vulnerability apply in sequence,
`23 → 11 → 22`; they do not cancel.

## Planning inventory and overlap result

| Inquiry | Evidence and conclusion |
| --- | --- |
| Existing mitigation owner | Nothing in `catalog/` implements Resistance, Immunity, or Vulnerability. Three existing contracts explicitly disclaim it: `procedure.mechanic.dnd2024.hit-points` ("resistance, immunity, vulnerability … out of scope"), `procedure.mechanic.dnd2024.weapon-damage.roll` ("Resistance, Vulnerability, Immunity … are later owners"), and `procedure.mechanic.dnd2024.weapon-damage.apply` ("No Resistance, Vulnerability, Immunity … is created"). Ownership is unclaimed and was deliberately reserved. |
| Existing damage-type vocabulary | `catalog/components/dnd2024.weapon-profile.schema.json` constrains `damage.type` to exactly `["bludgeoning","piercing","slashing"]`. That is the only damage-type enum in the system, and it is a subset of what this feature needs. |
| Damage application path | `catalog/mechanics/.../mechanic.dnd2024.weapon-damage.apply.md` declares roles `subject` (`dnd2024.abilities`), `target` (`dnd2024.hit-points`), `weapon` (`dnd2024.weapon-profile`), and one child `damage` bound to `mechanic.dnd2024.weapon-damage.roll` with `inheritInput: true`. It applies exactly one `component.set` on the target. It is the single existing writer of damage-caused Hit Point loss. |
| Composition model | `procedure.mechanic.projection`: children may be bound to declared parent roles, get deterministic derived seeds, and return frozen serialised results. Composition depth is bounded at eight; this feature reaches depth 2. |
| Child input sources — **decisive constraint** | Exactly three, per `MechanicComposer.ResolveInput` (~L223–260): inherit the parent's validated input; a static literal object; or `inputFromParentProperty`, a top-level key of the parent input whose value is an object. **All children resolve before the parent's source runs**, so a child's input can be built neither from a sibling child's result nor from a projected component value. A mitigation child therefore cannot be handed the rolled damage amount or the weapon's damage type — which is what shapes decision 4 below. |
| Role and component semantics | `RoleRequirement.Optional` is a **role-level** flag; a declared component the entity lacks is simply absent from the projection and never fails it. There is no "optional component" — a role stays required and the mechanic branches on absence and reports it. |
| Event model | E1 verified. `procedure.event.define` registers versioned payload schemas; `procedure.event.react` permits a rule to declare any non-`world.*` type, validates the payload against the version active at emission, and fails the whole root change on an invalid one. Revising a type never invalidates an already-recorded event. |
| Conditions | Feature 13 **Slice 1** (blocked, planned) supplies `dnd2024.conditions`. Only the stored `entries` are needed: nothing in the SRD implies Petrified, so the implication expansion Feature 13 Slice 2 adds is not a dependency here. |

## Verified existing dependencies

| Dependency | Evidence |
| --- | --- |
| Source registry | `catalog/world/entities/source.dnd2024.srd-5.2.1.json` — version, publisher, URLs, CC-BY attribution, locator format. |
| Damage dice and criticals | `mechanic.dnd2024.weapon-damage.roll` is verified (Feature 9 Slice 1): it returns base count and faces, actual dice, ordered rolls, dice subtotal, ability modifier, and a nonnegative damage total, with `effects: []`. |
| Transactional Hit Point application | `mechanic.dnd2024.weapon-damage.apply` is verified (Feature 9 Slice 2): one `component.set`, overkill clamped at zero, maximum and `sourceRef` preserved, atomic dry-run/apply, and Feature 9's focused integration tests passed 3/3 with the full suite at 302/302. |
| Hit Point state shape | `dnd2024.hit-points.schema.json`: closed `{current, maximum, sourceRef}`, `current` an integer `0..maximum`, `maximum` a positive safe integer, `sourceRef.locator` a const. |
| Closed-writer pattern | Features 6 and 7's `write` mechanics. |
| Event infrastructure | E1's six verified slices and the `world.*` type/schema pairs under `catalog/event-types/`. |

## Recursive dependency analysis

```text
Feature 15: damage types, Resistance, Immunity and Vulnerability
├─ SRD damage type and mitigation rules                            [implemented source basis]
├─ seeded damage dice and critical doubling                        [implemented: Feature 9 Slice 1]
├─ transactional Hit Point application                             [implemented: Feature 9 Slice 2]
├─ mechanic composition with frozen child results                  [implemented kernel]
├─ event type registration and declared events                     [implemented: E1]
├─ stored condition list (Petrified needs no implication expansion) [BLOCKED: Feature 13, Slice 1]
└─ mitigation applied between the roll and the Hit Points          [blocked parent]
   ├─ one canonical damage-type vocabulary                          [missing leaf: Slice 1]
   ├─ mitigation state + its administrative writer                  [blocked: Slice 2]
   ├─ effect-free mitigation resolver                               [blocked: Slice 3]
   └─ the existing damage path composes it, and announces it        [blocked: Slice 4]
```

The vocabulary is a leaf on its own because two later slices and one existing component all have to
agree on the same thirteen strings, and a vocabulary settled inside the component that first needs
it is a vocabulary the next component copies.

## Dependency and ownership decisions

1. **The vocabulary is contract-owned, and each schema declares its applicable subset.** There is no
   component that holds "the list of damage types" — a vocabulary is not world state. It lives in
   `procedure.mechanic.dnd2024.damage-types`. The mitigation schema enumerates all thirteen ids;
   a domain schema may name a verified subset when its SRD domain requires one. Slice 1's tests
   compare each enum to its contract-defined full set or declared subset, never to a hand-waved list.

2. **Weapon damage remains restricted to the three physical types.**
   `dnd2024.weapon-profile.damage.type` and its writer retain `bludgeoning`, `piercing`, and
   `slashing`. Widening that component schema would let a generic component write create an invalid
   radiant weapon profile even though the domain writer later rejects it. The shared thirteen-value
   vocabulary belongs in the new mitigation schema and future non-weapon damage owners, not in a
   weapon-only fact.

3. **Mitigation is one component with three lists, not three components.** `dnd2024.damage-mitigation`
   holds `resistances`, `immunities`, and `vulnerabilities`, each a unique array of type ids in
   canonical order. They are read together on every instance of damage and are meaningless apart.
   Three components would mean three reads, three absence questions, and three chances to be
   half-written.

4. **The resolver reports a mitigation *profile*; the parent does the arithmetic. The kernel forces
   this, and the cost is written down rather than hidden.**

   The natural design — a child that takes `{amount, type}` and returns the mitigated number —
   cannot be built. Child inputs resolve before the parent's source runs and can only be inherited,
   static, or an object property of the parent's own input, so the rolled amount (a sibling child's
   result) and the weapon's damage type (a projected component value) can reach a child by no route
   at all.

   So `mechanic.dnd2024.damage.resolve` is effect-free, takes a static `{}`, declares one role
   `defender`, and returns that creature's **profile**: which of the thirteen types it is immune,
   resistant, and vulnerable to, whether Petrified is in effect, and the two `known` flags. The
   parent applies the SRD mitigation sequence to its own rolled amount and type.

   The consequence, stated plainly: **the arithmetic is specified in
   `procedure.mechanic.dnd2024.damage-mitigation` and implemented in the consumer.** Today there is
   exactly one consumer, so there is no duplication — but Feature 32's spell damage will be the
   second, and that is the moment the rule would start existing twice. Two exits are acceptable and
   a third is not: either the contract's arithmetic is extracted into the **shared JavaScript
   prelude** already recorded as follow-on work in this project's notes, or `MechanicComposer` gains
   the ability to template a child input from a parent value and the resolver becomes the computing
   child this decision wanted. **Feature 32 must not simply reimplement it.** That is a removal
   criterion, and Slice 3's contract carries it.

   `procedure.mechanic.projection` also states that a child proposes effects but never applies them
   and that only the top-level parent returns effects to the applier, so an effect-free child is the
   right shape regardless — the same shape `weapon-damage.roll` already uses.

5. **`weapon-damage.apply` is revised, not replaced.** It stays the owner of "damage reaches a
   creature's Hit Points" and gains a second child. Introducing a parallel
   `mechanic.dnd2024.damage.apply` would create two writers of the same component — the overlap the
   guide's red-flag list forbids. When Feature 32 needs to deal spell damage it composes
   `damage.resolve` and writes Hit Points from its own parent; the *arithmetic* is shared, the
   *effect* belongs to whoever caused it.

6. **The event is declared here, and its payload carries overkill.** This is the decision with the
   longest reach. Feature 17 must implement instant death, whose rule is "the remaining damage
   equals or exceeds the creature's Hit Point maximum". After application, `current` is clamped at
   0, so the overkill is **gone from the world**. A reaction to `world.component.replaced` can see
   `payload.before` and `payload.after` and still cannot recover it. Therefore
   `mechanic.dnd2024.weapon-damage.apply` declares `dnd2024.damage.dealt` carrying the raw amount,
   the type, the full mitigation breakdown, the applied amount, before and after `current`, the
   `maximum`, the `overkill`, and whether the hit was critical.

   `procedure.event.react` says it plainly: declare an event "when the fact matters and the world
   does not show it". Overkill is precisely that fact. Declaring it now costs one event-type
   registration; discovering it in Feature 17 would cost a revision of a verified damage parent and
   a re-verification of Feature 9's exit gate.

7. **Immunity, then Resistance, then Vulnerability, and each once.** Immunity short-circuits to
   zero. After any modifier owned by the damage cause, Resistance halves and rounds down, then
   Vulnerability doubles the rounded result. Multiple sources of one effect count once, which is why
   the component stores sets. Resistance and Vulnerability on the same type compound in this order;
   they never cancel.

8. **Petrified is read from the stored condition list, not copied into the mitigation component.** A
   Petrified creature has Resistance to all damage. Writing that into `dnd2024.damage-mitigation`
   when the condition is applied would leave a residue when it was cleared, and would make "why is
   this creature resistant?" have two answers. The resolver reads the stored `entries` of
   `dnd2024.conditions` — note that this needs only Feature 13 **Slice 1**, since nothing in the SRD
   implies Petrified and no implication expansion is required — and reports `petrified` so the
   consumer can name the reason for the mitigation it applied.

9. **The resolver reports its reasoning, not just its answer.** Its profile carries the three lists,
   `petrified`, and their reasons. The consuming parent reports `rawAmount`, `type`, `immune`,
   `resistanceApplied`, `vulnerabilityApplied`, ordered reasons, and `finalAmount`. A mitigated
   amount with no explanation is unreviewable months later.

## Slice order and stop gates

| Order | Slice | Starts only when | Exit gate |
| --- | --- | --- | --- |
| 1 | Canonical damage-type vocabulary | Feature 9 verified; plan reviewed; clean `roleplay validate catalog` | **Verified** — the contract lists exactly thirteen types, the weapon-profile physical subset is unchanged and documented, and every Feature 7–9 acceptance row still passes. See `FEATURE-15-SLICE-1-RECEIPT.md`. |
| 2 | Mitigation state and its administrative writer | Slice 1 verified | **Verified** — a creature's Resistances, Immunities, and Vulnerabilities can be recorded and corrected through one closed writer, with unknown types, duplicates, ordering, missing, and corrupt cases rejected without state change. See `FEATURE-15-SLICE-2-RECEIPT.md`. |
| 3 | Effect-free mitigation-profile resolver, and the contract's arithmetic | Slice 2 and Feature 13 Slice 1 verified | **Verified** — the resolver reports a defender's exact mitigation profile with zero effects, and the contract-specified arithmetic produces the SRD-correct final amount for every combination of the three lists and Petrified. See `FEATURE-15-SLICE-3-RECEIPT.md`. |
| 4 | The damage path composes and announces | Slice 3 verified | **Verified** — one composed parent applies the SRD mitigation sequence, preserves overkill, and records one schema-valid `dnd2024.damage.dealt` event. See `FEATURE-15-SLICE-4-RECEIPT.md`. |

## Slice 1 — canonical damage-type vocabulary

### Runtime artifacts

| Artifact | Proposed ID / category | Change |
| --- | --- | --- |
| Governing contract | `procedure.mechanic.dnd2024.damage-types` in `ruleset.dnd2024.core.gameplay.damage` | New. Owns the thirteen ids, their canonical order, and the rule that every damage-typed schema enumerates exactly this list. |
| Regression coverage | `CatalogFeature15Tests` | New fresh-import coverage. |

### Governing contracts and source locator

Before writing, re-read `procedure.system.create-feature`,
`procedure.mechanic.dnd2024.weapon-profile`, `procedure.mechanic.dnd2024.weapon-damage.roll`,
`procedure.mechanic.dnd2024.weapon-damage.apply`, and `procedure.contract.create`. Reconfirm the
thirteen types and the Resistance-then-Vulnerability order against SRD PDF p. 17.
Re-search `damage type`, `resistance`, `immunity`, `vulnerability`, and each of the ten
non-physical type names against the authored catalog.

Locator: `source.dnd2024.srd-5.2.1`, `Playing the Game > Damage and Healing > Damage Types`.

### Data/input contract and required state

- The contract enumerates the thirteen ids and fixes their canonical order as the alphabetical
  order given above. Canonical order is what makes two exports of one database byte-identical, per
  `CATALOG_PORTABILITY_PLAN.md`.
- The weapon-profile enum and writer remain the verified physical subset
  `bludgeoning|piercing|slashing`; the new contract records that relationship explicitly.
- No component schema or mechanic changes in this slice, and no world state changes. This is a
  vocabulary-contract slice.

### Invariants, failure behavior, and non-goals

- Every existing stored `dnd2024.weapon-profile` component and its physical-type schema remain
  byte-identical. The writer continues to reject `radiant` and every non-physical type.
- No component stores the vocabulary; no mechanic hard-codes a fourteenth type.

### Slice 1 implementation sequence

1. Confirm Feature 9 is verified; record clean focused-test and `roleplay validate catalog`
   baselines.
2. Re-read the listed contracts; repeat overlap and routing searches.
3. Author the new contract, manifest entry, and focused validation test as catalog files first.
4. Run `roleplay validate catalog`; resolve every catalog or routing failure in its disposable
   validation database. Do not import into the persistent database.
5. In fresh disposable test databases, verify the contract's exact full vocabulary and the existing
   weapon schema's declared physical subset; do not alter fixtures or persistent game state.
6. Run focused tests, the full suite, `roleplay validate catalog`, and `git diff --check`; record
   evidence; mark only Slice 1 verified; stop for review.

### Slice 1 acceptance matrix

| Class | Required assertion |
| --- | --- |
| Happy path | The contract lists exactly thirteen ids in canonical order and records the weapon-profile physical subset. |
| Differential | The contract's full list and physical subset are compared element-by-element with the mitigation schema (when Slice 2 is added) and weapon schema respectively, not by eye. |
| Regression safety | Dagger, Shortbow, and Battleaxe profiles remain byte-identical; the schema is unchanged. |
| Closed vocabulary | A profile write attempting `radiant`, `Fire` (wrong case), `""`, or an unlisted string still fails; the weapon writer's physical restriction is unchanged. |
| Repository | Every Feature 7, 8, and 9 acceptance row, `roleplay validate catalog`, the full suite, and `git diff --check` pass; no persistent import occurs. |

### Slice 1 exit gate

Every row passes with recorded artifact version, byte comparisons of existing profiles, disposable
database readback, and repository checks. Slice 2 stays blocked until a new review authorizes it.

## Slice 2 — mitigation state and its administrative writer

### Status and prerequisite

Authorized by the Slice 1 dependency gate, but awaits confirmation of its new permanent ids. Adds
`procedure.mechanic.dnd2024.damage-mitigation`, the `dnd2024.damage-mitigation` component and schema, and
`mechanic.dnd2024.damage-mitigation.write`.

### Data/state and resolution contract

- The component is closed with exactly `resistances`, `immunities`, `vulnerabilities`, and
  `sourceRef`. Each list is an array of 0–13 unique type ids in canonical order.
- `sourceRef` is fixed to
  `{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Playing the Game > Damage and Healing"}`.
- Writer input is exactly `{"mode":"record"|"correct","resistances":[...],"immunities":[...],"vulnerabilities":[...]}`.
  All three arrays are required in both modes and may be empty; there is no per-list add or remove
  in this slice, because the whole-object write is what keeps the three lists consistent.
- `record` requires absence and applies one `component.add`; `correct` requires a valid existing
  component and applies one `component.set`. A corrupt record is rejected, never repaired.
- **A type may appear in more than one list, and this is legal.** Resistance and Vulnerability to
  the same type is a state the SRD anticipates and resolves at application time; forbidding it in
  storage would make a legitimate world unrepresentable. The resolver, not the writer, decides what
  it means.
- Rejected before any effect: unknown type ids, wrong case, duplicates within a list, non-array,
  missing list, wrong order, non-object root, extra key, and every caller-supplied `sourceRef`,
  `effects`, condition, or amount field.
- Lists are re-sorted into canonical order regardless of input order so two databases reaching the
  same state hold byte-identical data.

### Acceptance and exit gate

Prove: `record` with three empty lists creates the component; each of the thirteen types records in
each of the three lists; all thirteen in all three lists records and re-sorts correctly; a type in
two lists records; input in non-canonical order stores canonically; unknown id, wrong case,
duplicate, non-array, missing list, extra key, and supplied `sourceRef`/`effects` each fail with
zero effects; `record` against a present component and `correct` against an absent one each fail
with distinct reasons; corrupt stored data — unknown id, duplicate, wrong order, wrong `sourceRef`,
malformed JSON — is rejected by `correct` before any effect; routing selects this writer for
"record damage resistances" and not for "apply confirmed weapon damage", "record hit points", or
"apply the poisoned condition"; determinism, effect-exactness, state integrity, readback, cleanup,
and repository checks all hold. Slice 3 stays blocked.

## Slice 3 — the effect-free mitigation resolver

### Status and prerequisite

Authorized by the Slice 2 dependency gate, but awaits confirmation of its new permanent ids. Adds
`procedure.mechanic.dnd2024.damage.resolve` and `mechanic.dnd2024.damage.resolve`. Revises no
existing mechanic.

### Data/state and resolution contract

- One **required** role `defender`, declaring `dnd2024.damage-mitigation` and `dnd2024.conditions`.
  A defender lacking either simply has it absent from the projection; the result reports
  `mitigationKnown` and `conditionsKnown` rather than defaulting silently.
- Input is exactly `{}` — a static literal, per decision 4. The resolver reports state; it is handed
  no amount and no type, because the kernel cannot give it either.
- It reads the stored `entries` of `dnd2024.conditions` directly. Petrified implies Incapacitated
  but nothing implies Petrified, so the stored list is sufficient and `effectiveConditions` — which
  is a *result field of Feature 13's resolver*, not component data — is neither needed nor
  reachable here.
- Returns `mitigationKnown`, `conditionsKnown`, `immunities`, `resistances`, `vulnerabilities` (each
  the stored canonical list, or empty when unknown), `petrified` (Boolean), and the source
  reference. Always `effects: []`. Consumes no randomness. Corrupt mitigation or condition state
  fails here, before the parent computes anything.
- **The arithmetic the consumer must implement**, specified once in this contract and evaluated
  exactly once each:
  1. **Immunity.** If the instance's `type` is in `immunities`, the final amount is 0 and the
     remaining steps are skipped and reported as not applied.
  2. **Resistance.** Applies if `type` is in `resistances`, **or** if `petrified` is true. Halves,
     rounding down.
  3. **Vulnerability.** Applies if `type` is in `vulnerabilities`. Doubles the already-resisted
     amount, when Resistance also applies.

  Multiple sources of one effect count once — which is why Petrified plus a stored Resistance to the
  same type halves once, and why the component stores sets rather than counts.
- A raw amount of 0 resolves to 0 through every path without error; Feature 9 already permits a
  zero-damage result.
- The consumer reports `rawAmount`, `type`, `immune`, `resistanceApplied`,
  `vulnerabilityApplied`, ordered `reasons` (each `{effect, reason}` where reason is `"component"`
  or `"condition:petrified"`), and `finalAmount`, alongside the frozen child result it derived
  from. It fails before effects or an event when Vulnerability would exceed the safe integer.

### Acceptance and exit gate

The resolver reports state, so its own matrix proves reporting: each of the three lists is returned
exactly as stored and canonically ordered; `petrified` is true only when the stored entry is
present; an absent mitigation component reports `mitigationKnown: false` with three empty lists; an
absent conditions component reports `conditionsKnown: false` with `petrified: false`; corrupt
mitigation or condition state fails before returning; input other than `{}` fails; `effects: []` on
every success; no `ctx.randomInt` call; determinism; routing does not capture "apply confirmed
weapon damage" or "roll weapon damage"; readback and cleanup hold.

**The arithmetic is proven in this slice too, against the contract, using a disposable test
consumer** — it must not wait for Slice 4, or Slice 4 would be proving two new things at once.
Prove: each of the thirteen types unmitigated; Immunity yields 0 for any raw amount including the
safe-integer maximum; Resistance halves 1→0, 2→1, 3→1, and 7→3; Vulnerability doubles and the
doubled value at the safe-integer boundary fails rather than overflowing; Immunity outranks both;
Resistance plus Vulnerability follows the required sequence (including `23 → 11 → 22`); Petrified
alone halves; Petrified plus a stored Resistance to the same type halves **once**, with both reasons
reported — the multiple-sources-count-once row is required; Petrified plus Vulnerability halves then
doubles; a raw amount of 0 resolves to 0 through every path. The disposable consumer is discarded
with its test database. Run the full suite, `roleplay validate catalog`, and `git diff --check`; no
persistent import occurs. Slice 4 stays blocked.

## Slice 4 — the damage path composes the resolver and announces the result

### Status and prerequisite

Authorized and completed after the Slice 3 dependency gate. It revises `procedure.mechanic.dnd2024.weapon-damage.apply` and
`mechanic.dnd2024.weapon-damage.apply`, and adds the event type `dnd2024.damage.dealt` and its
schema. It adds no new mechanic.

### Data/state and resolution contract

- The apply parent gains a second declared child, `mitigation`, bound to
  `mechanic.dnd2024.damage.resolve` with role binding `defender: target`, `inheritInput: false`, and
  a static `{}` input. It cannot inherit the parent input, whose `{ability, critical}` shape is
  meaningless to the resolver, and it cannot be handed the rolled amount — see decision 4.
- The `target` role declares `dnd2024.damage-mitigation` and `dnd2024.conditions` alongside its
  existing `dnd2024.hit-points`. The role stays required; the two new components may be absent and
  the child reports that.
- Execution order is fixed: both children resolve before the parent source, in their declared
  deterministic composition order — the damage child returns the rolled amount and the mitigation
  child returns the defender's profile. The parent then applies the contract's arithmetic to the
  rolled amount and the weapon's damage type, computes the after-state, proposes one
  `component.set`, and declares one event. Composition depth reaches 2, well inside the eight-level
  bound.
- `afterCurrent = max(0, beforeCurrent - finalAmount)`. `overkill = max(0, finalAmount - beforeCurrent)`.
  `maximum` and `sourceRef` are unchanged. The effect count stays exactly one.
- The declared event `dnd2024.damage.dealt` names the target in `entityIds` and carries a closed
  payload: `targetId`, `sourceId` (the attacking creature), `rawAmount`, `type`, `finalAmount`,
  `immune`, `resistanceApplied`, `vulnerabilityApplied`, `beforeCurrent`, `afterCurrent`,
  `maximum`, `overkill`, `critical`, and `sourceRef`.
- The event is declared on **every** successful application, including zero-damage and
  already-at-zero cases. A rule that fires on "took damage" must be able to distinguish "took 0"
  from "did not happen", and only a consistently declared event allows that.
- The parent still never rolls, never recomputes damage, never trusts a caller-supplied amount,
  never changes the subject or weapon, and never applies a condition or death consequence.

### Acceptance and exit gate

Prove: an unmitigated hit produces exactly the Feature 9 result, byte-identical for the same seed —
the strongest available regression assertion and a required row; a resistant target loses the halved
amount; an immune target loses nothing and still receives exactly one `component.set` and one event;
a vulnerable target loses double; combined Resistance and Vulnerability applies in that order; a
Petrified target loses half; a critical hit against a resistant target halves the doubled dice result
and not the other way round, proving Resistance is applied after all other modifiers; an unsafe
Vulnerability double fails before an effect or event; overkill is reported correctly when damage
exceeds current Hit Points and is 0 otherwise; a target already at 0 takes an event with `overkill`
equal to `finalAmount`; the event validates against its registered schema, appears exactly once per
application, names the target, and has disposable-database causation evidence; a failed application
declares no event and changes nothing; two frozen child results are separately reported
with their own mechanic ids, versions, and seeds; replay from the same seed is exact; every Feature
9 Slice 2 acceptance row still passes; routing is unchanged; revised artifacts are loaded from a
fresh validation database while prior versions remain readable. Run the full suite, `roleplay
validate catalog`, and `git diff --check`; no persistent import occurs.

Feature 15 is verified. Stop before Feature 16.

## Forward dependencies this plan deliberately leaves open

| Concern | Owner | Note |
| --- | --- | --- |
| Temporary Hit Points absorbing damage first | Feature 16 | Revises this feature's apply parent and the `dnd2024.damage.dealt` payload to add `temporaryAbsorbed`. Revising an event type never invalidates a recorded event, so the ledger stays readable. |
| 0 Hit Points, unconsciousness, death saves, instant death | Feature 17 | Subscribes to `dnd2024.damage.dealt`. It must ignore an event whose `finalAmount` is 0; the `overkill` field exists from Slice 4 so Feature 17 need not revise a verified damage parent. |
| Half damage on a successful save | Feature 32 | Belongs to the effect that offers the save, not to the mitigation resolver. |
| Granted Resistances from class, species, item, or spell | Features 26, 27, 29, 31, 32 | Each writes `dnd2024.damage-mitigation`; none adds a second mitigation source. |
| Non-weapon damage | Feature 32 | Composes `mechanic.dnd2024.damage.resolve` and owns its own Hit Point effect. **It becomes the second implementer of decision 4's arithmetic and must not reimplement it** — extract the shared prelude, or add parent-value child input, first. |

## Plan-quality audit

1. Yes — one capability, mitigation between the damage roll and the Hit Points, with healing,
   dying, and every granting source explicitly excluded and assigned.
2. Yes — the source entity, three headings, PDF page 17, adjustment order, and rounding rule are
   concrete.
3. Yes — damage type, resistance, immunity, vulnerability, and each non-physical type name were
   searched; three existing contracts were found to have disclaimed ownership in writing.
4. Partly — Feature 9's rows cite verified exit-gate evidence; the Feature 13 rows cite an
   unimplemented plan, which is why this feature is blocked and why the roadmap dependency row needs
   correcting.
5. Yes — the vocabulary and mitigation state are independent leaves; resolver and consumption are
   ordered consumers of Feature 13's conditions, each independently testable.
6. Yes — vocabulary, stored mitigation, condition-derived mitigation, transient amount and type,
   the arithmetic, the effect, and every downstream consequence have single named owners.
7. Yes — Slice 2 lands the component with its only safe write path; Slice 4 revises the existing
   owner rather than adding a second Hit Point writer.
8. Yes — Slice 1 alone is named as next and has only verified Feature 9 as a prerequisite.
9. Yes — absent versus empty mitigation, a type in two lists, zero damage, and corrupt state are all
   explicit.
10. Yes — the three mitigation adjustments, the rounding rule, the overkill formula, the effect count, the event
    payload, and every result field are testable without guessing.
11. Yes — the matrix covers happy, boundary (including safe-integer overflow on doubling),
    differential, closed-input, missing, corrupt, replay, routing, effect-exactness, event validity,
    state integrity, readback, cleanup, and repository classes. The **random-selection class does
    not apply** to Slices 1–3, which consume no randomness; Slice 4 inherits Feature 9's dice child
    and asserts its replay rather than re-testing it.
12. Yes — repository-mode disposable catalog validation, event-payload evidence, and persistent
    import boundaries are stated.
13. Yes — disposable fixture deletion and baseline preservation are explicit.
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

- The SRD re-read contradicts the recorded sequence — immunity, then Resistance rounded down, then
  Vulnerability — or the rounding rule. Slice 3 cannot be written on a guess about mitigation
  arithmetic.
- Feature 13 ships a condition component whose stored source-aware `entries` cannot be validated
  and read directly for any Petrified instance, which would make decision 8's Slice-1-only
  dependency wrong.
- `MechanicComposer` gains the ability to template a child input from a parent value or a sibling
  result. That does not break this plan, but it makes decision 4's compromise unnecessary: revisit
  it deliberately and move the arithmetic into the resolver before Feature 32 arrives, rather than
  leaving the removal criterion outstanding.
- A repository search finds any existing mitigation owner or a second damage-type enum.

Descend to a new dependency rather than adding a second Hit Point writer, storing Petrified's
Resistance as component data, accepting a caller-supplied final amount, or omitting the event and
leaving Feature 17 to re-plumb this feature.
