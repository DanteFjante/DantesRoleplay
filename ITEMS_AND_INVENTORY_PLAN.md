# Items and inventory mechanics roadmap

Status: **Feature 23 Slice 1 verified; item-definition and inventory-state slices remain unselected**
Last updated: 2026-08-20

## Purpose

Build a coherent item system around the repository's existing entity/component/containment model,
then layer D&D 2024 equipment rules onto it without copying item facts onto characters or adding
game vocabulary to the C# kernel.

The first useful release should let a created character receive a canonical weapon instance,
carry it directly or inside a nested container, equip it, and use that exact possessed instance in
the existing weapon-attack and weapon-damage path. Later slices add quantities, weight, creature
size, carrying capacity, currency, consumables, armor, ammunition, magic items, and economies.

This document expands D&D roadmap Feature 23. Features 24, 25, 29, and 30 remain separate owners
of armor, weapon properties/mastery, magic items/attunement, and guided character creation.

## Execution rule

Use [GAME_SYSTEM_MASTER_PLAN.md](GAME_SYSTEM_MASTER_PLAN.md) for cross-subsystem ownership,
[ruleset/dnd2024/ROADMAP.md](ruleset/dnd2024/ROADMAP.md) for numbered D&D dependencies,
[ruleset/dnd2024/TERRA-FEATURE-PLANNING-GUIDE.md](ruleset/dnd2024/TERRA-FEATURE-PLANNING-GUIDE.md)
for plan quality, and a populated
[SUBSYSTEM_IMPLEMENTATION_HANDOFF.md](SUBSYSTEM_IMPLEMENTATION_HANDOFF.md) for an active
assignment. Implement one reviewed slice, meet its exit gate, record evidence, and stop.

All permanent IDs below are candidate owner names, not authorised IDs. Slice 0 must search the
authored catalog, ratify exact IDs and schemas, and update this plan before any runtime artifact is
created. Repository files are the development authority; reviewed catalog files are imported only
at an explicit synchronization boundary.

## Recommended decisions

1. **An inventory is a projection, not a component.** Physical custody comes from containment.
   Never add an `inventory` array, `ownerId`, or copied item statistics to an actor.
2. **Use definitions and instances.** A versioned definition owns immutable rules/content facts;
   a campaign-owned instance owns quantity and other mutable state.
3. **Nested containers use existing containment.** Moving a backpack moves its subtree without
   rewriting every descendant. One physical parent and cycle rejection already exist.
4. **Use weight for D&D carrying, not generic inventory slots.** SRD 5.2.1 gives item weights,
   container limits, creature size, and Strength-based carrying capacity. A universal slot count
   would be a house rule and would compete with those facts.
5. **Do not give ordinary items creature-size categories.** Creature size is required because it
   changes carrying capacity and movement. Item fit should initially use weight, specific content
   constraints, and only source-backed volume; do not invent dimensions for every item.
6. **Equipment positions are typed claims, not a universal body-slot grid.** D&D cares whether an
   item is held, worn, donned, or wielded and later limits some same-kind magic items. A rigid
   head/chest/hands/feet slot model would be both too strict and too weak.
7. **Stacks represent fungible units only.** One stack entity may represent identical arrows,
   rations, coins, or similar units. A named, damaged, charged, attuned, identified, customized,
   or otherwise distinct object must be its own instance.
8. **Currency should reuse stacks.** Use one stack per denomination/state/container, not one entity
   per coin and not an unrelated actor balance. A wallet view derives value and coin weight.
9. **Derived totals are never stored as authority.** Stack weight, container load, recursive carried
   burden, remaining capacity, wealth value, and carrying limits are computed from source facts.
10. **Every gameplay mutation is semantic and atomic.** Transfer, split, merge, equip, consume,
    grant, and currency payment validate their whole consequence and return ordinary effects. Raw
    structural effects remain administrative tools governed by existing world contracts.

## Official D&D 2024 source basis

The registered source is `source.dnd2024.srd-5.2.1`, the official SRD 5.2.1 published 2025-05-01
under CC-BY-4.0. Exact content records must retain heading-plus-PDF-page locators.

| Rule area | Source locator | Roadmap consequence |
| --- | --- | --- |
| Coins and equipment tables | *Equipment > Coins/Weapons/Armor/Adventuring Gear*, PDF pages 89–100 | Definitions need cost and weight; fifty coins weigh one pound; denomination value is exact. |
| Containers | *Equipment > Adventuring Gear*, PDF pages 96–99 | Backpack, basket, barrel, pouch, quiver, and similar records have weight, weight/volume, or item-type limits. |
| Creature size | *Playing the Game > Combat > Movement and Position > Creature Size*, PDF page 14 | Creature size is shared actor state, not item metadata. |
| Carrying Capacity | *Rules Glossary > Carrying Capacity*, PDF page 178 | Carry and drag/lift/push limits derive from Strength and size. Above the carry limit, drag/lift/push caps Speed at 5 feet. |
| Equip/unequip weapons | *Rules Glossary > Attack [Action]*, PDF page 177 | Drawing, picking up, sheathing, stowing, or dropping a weapon interacts with the Attack action and later action economy. |
| Armor and shields | *Equipment > Armor*, PDF pages 92–93 | Don/doff timing, training, Strength, Stealth, and AC derivation belong to Feature 24. |
| Ammunition and weapon properties | *Equipment > Weapons > Properties*, PDF pages 89–90 | Ammunition expenditure/recovery, loading, hands, range, Light, Heavy, thrown, and mastery belong to Feature 25. |
| Magic items | *Equipment > Magic Items*, PDF pages 102–103 | Identification, attunement, wearing/wielding, same-kind limits, charges, and special containers belong to Feature 29. |

SRD 5.2.1 defines a carrying maximum but does not include the older thresholded Variant
Encumbrance table. Feature 23 should therefore implement carrying capacity and drag/lift/push. It
must not invent `Encumbered` or `Heavily Encumbered` states unless a later campaign option names an
approved source and owns the resulting speed consequences.

## Fit with the existing contracts

### What already exists

- `procedure.world.model` says an item is an entity with components; an inventory is containment
  plus data, not a new database concept.
- `Containment` already enforces at most one physical parent. `WorldStore.MoveAsync` rejects direct
  and indirect cycles.
- `containment.move` is an existing atomic effect and produces the existing structural
  `world.containment.moved` event.
- Effects validate and commit as one transaction with events, guards, reactions, audit, and
  rollback.
- A mechanic receives frozen declared projections and never reads the store during execution.
- Feature 7 already owns canonical Dagger, Shortbow, and Battleaxe profile entities. Feature 8
  accepts a canonical weapon-profile role, and Feature 9 consumes the same profile for damage.
- The generic graph reader can return selected components over a containment graph, currently to a
  maximum containment depth of two and bounded node counts.

### Gaps that must be resolved rather than worked around

1. **Definition versus instance.** Existing weapon entities are canonical profile definitions,
   not physical objects. An actor must possess a campaign instance that references one exact
   canonical entity/version.
2. **Action projection for nested state.** `includeContents` exposes direct child identity and slot
   but not the child's components or arbitrary descendants. A transfer/carrying mechanic cannot
   honestly derive recursive weight or validate a nested destination from that shape.
3. **Definition version identity.** World entities/components have revisions, while item instances
   need a stable reference to the exact immutable content revision they were created from. Slice 0
   must align this with the catalog/versioning contract rather than inventing “latest.”
4. **Item deletion.** `entity.delete` soft-deletes an entity. Empty-stack removal must first detach
   its containment edge in the same effect list. A container with descendants may not be deleted
   until its contents are explicitly moved or removed.
5. **Equipment validation.** `Containment.Slot` is deliberately free text. It is useful for display
   or physical placement but is not sufficient authority for D&D held/worn state without a closed
   semantic mechanic and validated state owner.
6. **Integrity across all writes.** A correct transfer mechanic is not enough if starting grants,
   consumption, or administrative changes can create over-capacity, invalid-stack, or
   equipped-but-unpossessed state. Slice 0 must decide which invariants need reusable guards and
   which are guaranteed by closed root mechanics.

Do not bypass these gaps with caller-supplied child totals, an actor inventory array, copied
weapon data, per-item C# classes, or lazy database reads from JavaScript.

## Ownership model

### Item definition

An item definition is immutable, source-cited, versioned content. It owns facts shared by every
copy:

- stable definition identity, display name, item family/category, source/version, and description;
- unit mass and cost when supplied by the source;
- whether units are stackable and any canonical denomination or unit;
- container capability: allowed content kind, count/weight/volume limits, and compartments when
  the source actually defines them;
- supported equipment mode/profile references;
- references to ruleset-specific weapon, armor, tool, spell-focus, activity, or magic-item data;
- optional tags used for closed compatibility checks, never prose interpreted at runtime.

The existing Dagger, Shortbow, and Battleaxe entities remain authoritative for their verified
weapon facts. Slice 1 should either extend those entities with generic item-definition facts or
create a generic definition that references each profile. It must not duplicate category, damage,
proficiency, or attack rules.

### Item instance

An item instance is a campaign-owned entity referring to one exact definition identity/version.
It owns mutable facts only:

- quantity for a stackable definition;
- optional custom label and creation/grant provenance;
- later condition/durability only if a dedicated feature is approved;
- later uses/charges, identification, attunement, or curse state only under their owning feature;
- closed equipment state when the item is actually equipped.

Non-stackable instances have an implicit quantity of one; storing `quantity: 1` on them creates two
representations and should be rejected. A stack quantity is a positive safe integer. Zero is a
transition to detach-and-delete, never durable stack state.

### Possession, custody, and ownership

Containment is authoritative for physical location and custody:

- an actor, location, vehicle, or container contains an item instance;
- a container item may contain other item instances;
- recursive ancestry determines what an actor carries;
- moving a container moves the entire subtree;
- the same physical instance has one parent.

Legal ownership is different from custody. If stolen goods, loans, or property claims matter, use
a separate relationship such as an approved `owns`/`claims` contract. Do not overload containment
or store both custody and owner IDs on the instance.

### Stack identity

Two instances can merge only when a derived stack key is identical. At minimum it includes:

- exact definition identity and version;
- every mutable state field that changes interchangeability;
- denomination/unit for money or ammunition kind;
- no custom name, attunement, charge variance, damage, identification difference, or unique
  provenance that the owning rule says must be preserved.

Merge/split rules conserve total quantity. Merge keeps one deterministic surviving ID and detaches
then deletes the emptied instance. Split requires a caller-proposed permanent ID because entity IDs
cannot be reused, creates one instance with the same stack key, and conserves quantity atomically.
Partial transfer composes split plus containment move in the same root transaction.

### Equipment state and “slots”

Possession is not equipment. Use a closed item equipment-state owner rather than treating any
carried weapon as ready.

- The definition owns supported modes and physical claims, such as held, worn, donned armor, or
  shield. Feature 25 may add hand requirements that depend on the attempted attack rather than
  permanent state.
- The instance owns only its current equipment mode. The actor is derived through containment and
  is not copied into the component.
- Initially, an equipped item must be directly contained by the actor, not buried in a backpack.
- Equipping validates definition support, possession, conflicts, and later timing; unequipping
  moves to a valid stowed/carried state or drops it.
- UI groups such as “hands,” “worn,” and “backpack” are projections. They are not generic numeric
  inventory slots.

Feature 29 may later add source-backed wear groups for footwear, gloves/gauntlets, bracers, armor,
headwear, and cloaks. Those are same-kind limits with GM exceptions, not a universal equipment
grid.

### Physical measures, item “size,” and capacity

Store source-backed physical measures on definitions in exact canonical integer units; do not use
binary floating point. Slice 0 must choose the unit convention and prove every SRD fraction needed
by seed content. A workable direction is thousandths of a pound for mass and thousandths of a
cubic inch for explicit volume.

Ordinary recursive carried burden is:

```text
instance burden = definition unit mass × stack quantity
container subtree burden = container instance burden + sum(descendant burdens)
actor carried burden = sum(burdens of every item in the actor's containment subtree)
```

Only descendants carrying the ratified item-instance marker participate in this formula. A creature
or another non-item entity in the containment graph is not silently treated as a zero-weight item;
carrying creatures or other world objects needs an explicit compatible physical rule.

Container limits and creature carrying limits are separate:

- a backpack can be below an actor's carry limit while exceeding its own 30-pound capacity;
- the backpack's own weight counts in actor burden;
- a quiver's source-backed limit is 20 arrows, not twenty arbitrary slots;
- a barrel's liquid/dry-volume capacity should be enforced only when the contents have compatible,
  source-backed units;
- because the SRD does not provide volume for every ordinary item, volume must not be guessed to
  reject otherwise legal transfers;
- extradimensional containers and fixed effective carried weight are Feature 29 exceptions, not
  generic container booleans.

### Creature size and carrying

Creature size is shared D&D actor state and should be introduced by a small ruleset owner consumed
by both Feature 20 movement and Feature 23 carrying. It uses the closed SRD categories Tiny,
Small, Medium, Large, Huge, and Gargantuan. Missing is unknown and fails carrying derivation;
explicit size is never inferred from display name.

Carrying limits are derived, never stored:

| Creature size | Carry | Drag/lift/push |
| --- | ---: | ---: |
| Tiny | Strength × 7.5 lb. | Strength × 15 lb. |
| Small or Medium | Strength × 15 lb. | Strength × 30 lb. |
| Large | Strength × 30 lb. | Strength × 60 lb. |
| Huge | Strength × 60 lb. | Strength × 120 lb. |
| Gargantuan | Strength × 120 lb. | Strength × 240 lb. |

Traits such as Beast of Burden should modify the effective size used for this derivation through a
named trait/modifier owner. They must not overwrite actual creature size. Drag/lift/push is an
action circumstance and does not mean the object becomes contained in the actor's inventory.

### Currency

Represent CP, SP, EP, GP, and PP as stackable item definitions with exact denomination value and
coin mass. One stack entity represents many identical coins. Payment/change is a semantic
transaction over denomination stacks; total value and total coin mass are derived. This supports
coins in a pouch, chest, cart, location, or actor with the same containment and carrying rules.

Do not use a single floating-point GP balance. If a campaign wants abstract wealth with no
physical coins, that is an explicit campaign option with a separate authority and must not be
silently mixed with physical coin stacks.

### Item activities and resources

An item definition may reference one or more versioned activities owned by other rules:

- drink/administer a potion;
- expend a healer's-kit use;
- light/extinguish a torch or lamp;
- apply poison or oil;
- fire ammunition;
- activate a charged magic item.

The activity mechanic validates possession, accessibility, target/range/timing, remaining
quantity/uses/charges, and the activity's rule. Consequence plus decrement/removal commits in one
transaction. A generic “delete item” or caller-supplied effect list is not consumption.

Quantity, uses, charges, and duration are different concepts and must not share one vague
`resource` number. Each owning feature defines recovery, expiry, and zero behavior.

## Capability map

### Feature 23 — core inventory owner

| Capability | Include | Notes |
| --- | --- | --- |
| Definition discovery | Yes | Find exact versioned mundane definitions and referenced profiles. |
| Instance creation/grant | Yes | Closed campaign-scoped creation from an eligible definition. |
| Inventory projection | Yes | Derived from bounded containment; one-level display by default, bounded expansion on request. |
| Nested containers | Yes | One parent, no cycles, subtree movement, capacity/access validation. |
| Stack split/merge | Yes | Positive integer quantities and conservation. |
| Transfer/drop/pick up/give/stow | Yes | One semantic transfer family with explicit source/destination and partial quantity support. |
| Equip/unequip | Yes | Minimal held/worn state; timing integration waits for Feature 12 where needed. |
| Item weight and container limits | Yes | Source-backed mass plus specific capacity rules; no guessed volume. |
| Creature size and carrying capacity | Yes, as shared dependency | Actual size state is shared with Feature 20; carry limits are derived. |
| Currency denominations and payment | Yes | Physical denomination stacks and exact value/weight. |
| Minimal mundane consumption | Yes, after core state | One representative activity proves atomic consequence plus depletion. |
| Starting equipment grants | Integration | One root transaction with character creation; one subsystem owns the root. |

### Adjacent owners that must be on the roadmap

| Capability | Owner | Why it is separate |
| --- | --- | --- |
| Armor, shields, don/doff, training, worn-derived AC | Feature 24 | Replaces Feature 6's final AC input with equipment-derived formulas and timing. |
| Weapon properties, range, ammunition, loading, hands, mastery | Feature 25 | Alters attack legality, action economy, and damage behavior. |
| Item-specific adventuring-gear activities | Individual activity families | Caltrops, kits, oil, poison, rope, and tools need their own target/check/effect rules. |
| Identification, attunement, magic item effects/charges, special containers | Feature 29 | Depends on rests, timed distance, exceptions, and item-granted effects. |
| Guided starting equipment | Feature 30 / Character CH5 integration | Must be atomic with legal character creation and choice validation. |
| Mount/vehicle cargo | Feature 23 plus world travel/conveyance | Reuses burden/capacity; pulling formulas and travel consequences belong to movement/travel. |
| Loot tables and random treasure | Future loot feature | Needs deterministic candidate pools, seed/audit, and encounter/quest provenance. |
| Buying, selling, services, and markets | Future economy feature | Prices and availability are world/economy state, not inventory mutation. |
| Crafting | Future crafting feature | Needs recipes, tools, proficiency, materials, time, progress, and outputs. |
| Durability, repair, and object damage | Future object/durability feature | D&D does not give one universal item-durability path; object HP is separate. |
| Trade, lending, theft, and legal ownership | Future social/economy feature | Custody through containment is not title or consent. |
| Inventory-slot house rule | Optional campaign rule | Alternative to weight, not hidden inside D&D 2024 core. |

## Proposed mechanic and procedure families

Exact IDs are ratified in Slice 0. Prefer one mechanic per reusable transformation, not one per
item.

### Read/definition procedures

- define or revise an immutable item definition through the catalog workflow;
- inspect one definition/version and its referenced ruleset profiles;
- inspect an item instance with resolved definition and physical ancestry;
- project one actor/container inventory with bounded depth, totals, truncation, and corrupt-state
  reporting.

### State-changing mechanics

- create/grant one instance or one closed grant bundle;
- split and merge compatible stacks;
- transfer all or part of an instance among actor, container, location, or vehicle;
- equip and unequip an eligible possessed item;
- exchange/pay exact currency amounts, with deterministic change policy;
- consume/use one item activity and atomically apply its consequence plus resource change;
- correct/migrate an item instance administratively under a separate contract.

### Derived resolvers

- resolve an instance to its exact definition/profile version;
- calculate one stack's burden, one container's direct load, one containment subtree's burden, and
  one actor's recursive carried burden;
- derive carrying and drag/lift/push limits from Strength and effective carrying size;
- derive remaining container capacity and total wallet value/coin mass;
- resolve inventory ancestry and the nearest custody root without storing it.

## Recursive dependency graph

```text
Feature 23: usable D&D equipment and inventory
├─ five-structure world model, atomic effects/events/audit          [implemented]
├─ single-parent, cycle-safe containment                             [implemented]
├─ canonical weapon profiles and attack/damage consumers            [implemented: Features 7–9]
├─ generic bounded inventory action projection                      [Feature 23 Slice 1 verified]
│  ├─ declared descendant component projection
│  ├─ declared ancestor chain or derived custody-root projection
│  ├─ deterministic ordering, depth/node limits, and truncation failure
│  └─ frozen/audited projection with no lazy store access
├─ immutable item-definition/version boundary                       [missing]
├─ campaign item instances and instance lifecycle                   [missing]
├─ quantity and stack conservation                                  [missing]
├─ physical measures and container-capacity definitions             [missing]
├─ recursive burden/capacity derivation                             [blocked by projection]
├─ creature size state                                              [missing shared leaf]
├─ Strength/size carrying derivation                                [blocked by creature size]
├─ atomic transfer and nested-container validation                  [blocked by all above]
├─ minimal equipment state and weapon-instance integration          [blocked by transfer]
├─ physical currency stacks and payments                            [blocked by stacking/carrying]
├─ one mundane item activity                                        [blocked by instance lifecycle]
└─ starting-equipment root integration                              [blocked by Items + CH5]
   ├─ Feature 24 armor                                               [downstream]
   ├─ Feature 25 weapon properties/ammunition                        [downstream]
   ├─ Feature 29 magic items/attunement                              [downstream]
   └─ Feature 30 guided character creation                           [downstream]
```

## Delivery roadmap and stop gates

### Slice 0 — ratify ownership, IDs, versioning, and projection

Inventory the catalog and current live/imported state. Decide whether canonical weapon-profile
entities are item definitions directly or are referenced by generic definitions. Ratify exact
definition/instance/equipment/physical/size IDs; missing-versus-empty semantics; source/version
identity; fixed measurement units; custody vocabulary; stack-key inputs; maximum supported nesting;
and the closed root transaction for starting grants.

Specify the smallest generic action-projection extension that can expose declared descendant
components and required ancestry within fixed depth/node bounds. Truncation must fail a mechanic;
it may not produce a falsely low burden. This is a public mechanics-projection contract decision
and requires its own reviewed implementation slice before item mechanics consume it.

**Exit:** every field belongs to one owner; the recursive dependency graph has no assumed leaf;
exact IDs are reviewed; no runtime artifact has been created; exactly one lowest implementation
slice is named next.

### Slice 1 — generic item definitions and canonical seed content

Add the generic immutable definition contract only where existing artifacts cannot already own the
fact. Connect Dagger, Shortbow, and Battleaxe without changing Feature 7's profile authority. Add a
small representative seed set: one non-stackable weapon, one stackable mundane unit, one ordinary
container, and later one denomination definition. Include source-backed unit mass, stack policy,
and container capability needed by later slices.

**Exit:** callers can discover one definition and its exact profile/source/version; invalid or
duplicate source identity, floating/negative measurements, unsupported categories, and copied
weapon facts are rejected; catalog round-trip is exact.

### Slice 2 — item instances, lifecycle, and direct possession reads

Add campaign-owned instances referencing exact definitions. Implement closed instance creation for
approved definitions, direct containment-based possession, provenance, and leaf detach-delete.
Provide a direct one-level inventory projection using existing read capabilities while the generic
nested action projection is implemented separately.

**Exit:** two actors can own distinct instances of the same definition; moving/revising the
definition does not rewrite instance references; invalid scope/version/quantity/provenance and
deleting a contained or nonempty container fail atomically.

### Slice 3 — quantities, split, merge, and zero removal

Add positive quantities only for stackable definitions. Implement deterministic merge/split,
explicit new split IDs, stack-key validation, conservation, and detach-delete when a stack is
exhausted. Keep unique items unstackable.

**Exit:** split and merge conserve quantity at all boundaries; incompatible or distinct-state
instances cannot merge; duplicate IDs, zero/negative/fractional/unsafe quantities, and partial
failures leave exact state unchanged.

### Slice 4 — bounded nested inventory projection and burden derivation

Implement the ratified generic projection prerequisite, then derive ancestry, custody root,
recursive burden, direct container load, and remaining capacity from definitions, instances,
quantities, and containment. One-level display remains the default; deeper reads are bounded and
explicit.

**Exit:** a character -> backpack -> pouch -> coin/arrow tree produces exact deterministic totals;
moving the backpack changes ancestry without rewriting descendants; corrupt/missing definitions,
cycles, excessive depth/nodes, and truncation fail rather than undercount.

### Slice 5 — creature size and carrying capacity

Add the shared source-cited creature-size state owner and safe record/correction path. Implement an
effect-free carrying resolver that reads Strength, actual size, approved effective-size modifiers,
and recursive burden. Keep drag/lift/push as separately reported limits and do not create an
encumbrance condition.

**Exit:** every size/Strength boundary produces the SRD formula exactly; Small and Medium match;
Tiny halves and each larger category doubles; missing/corrupt size or Strength and caller-supplied
totals fail without effects.

### Slice 6 — atomic transfer and nested-container capacity

Implement one transfer family for pick up, drop, give, stow, retrieve, and whole/partial stack
movement. Validate source ancestry, destination capability, stack split, container limits, actor
carry limit, campaign scope, cycle/self movement, and accessibility. Apply all quantity,
containment, and deletion effects in one transaction.

Initial containers are explicitly accessible/open unless a later container-state contract is
implemented. Locked/closed/sealed access must not be guessed from prose. Direct world effects remain
administrative; normal play uses this mechanic.

**Exit:** character -> backpack -> pouch -> character and actor-to-actor transfer work; every
capacity boundary is exact; overweight/wrong-content/inaccessible/cyclic/cross-campaign/partial
failure changes nothing; accepted movement emits the existing structural events once.

### Slice 7 — minimal equip/unequip and weapon-instance integration

Add closed held/worn equipment state and possession/conflict invariants. Update or compose the
existing weapon action so the caller names the campaign item instance; the rule resolves its exact
definition/profile and verifies it is possessed and appropriately equipped. It must not copy the
weapon profile onto the actor or instance.

Integrate Attack-action draw/stow timing only after Feature 12's action economy can own the cost.
Until then, equip/unequip is a standalone semantic action and does not pretend to consume a turn
resource.

**Exit:** one equipped Dagger instance follows the existing attack/damage path; canonical profile
entities can no longer be passed as if physically held; unowned, nested/stowed, wrong-category,
conflicting, unsupported-mode, missing/corrupt reference, and duplicate equipment cases fail.

### Slice 8 — physical currency and exact payments

Add five denomination definitions and stacks, exact value conversion, coin mass, wallet projection,
and payment/transfer mechanics. Define deterministic payment and change behavior; do not silently
mint change unless an explicit counterparty or exchange capability supplies it.

**Exit:** denomination conversion and fifty-coins-per-pound burden are exact; split/merge/payment
conserve value and coin count except for explicitly evidenced change; insufficient funds,
unavailable change, over-capacity destination, and overflow fail atomically.

### Slice 9 — one mundane consumable/use activity

Choose one representative source-cited item whose downstream rule already exists or can be one
small independent leaf. A healer's-kit activity once stabilization exists, or a potion once
healing exists, is preferable to inventing a generic consequence. Validate possession/access/timing/target,
compose the owning consequence, and decrement uses or quantity atomically.

**Exit:** consequence plus depletion succeeds or rolls back as one root; consuming the last unit
detaches and deletes a leaf instance; invalid/missing/corrupt resource or rejected consequence
restores exact item and target state.

### Slice 10 — starting equipment grants

Integrate item definitions/instances with character creation. A background/class choice resolves
approved definition versions and creates/contains the exact instances and currency stacks in the
character-creation root transaction.

Before Item Slice 10 or Character CH5 is assigned, the reviewer must ratify one owner for the
atomic character-plus-starting-equipment root transaction. The non-owning slice exposes a closed
grant planner/capability and must not create a second transaction or audit root.

**Exit:** the sample character receives exactly the selected starting items; duplicate,
ineligible, wrong-version, overweight, invalid-container, or partial grant failure creates no
character, loose item, currency stack, event, or misleading success audit.

### Slice 11 — read-only inventory UI

Add server-rendered actor/container views and SSE refresh after committed containment/component
changes. Default to one level: nested containers link to their own screen instead of rendering an
unbounded recursive wall. Show definition, quantity, location/equipment state, direct and recursive
burden, capacity warnings, and source/version where useful.

Drag/drop transfer, equip buttons, and payments wait for authenticated semantic commands,
conflict/revision handling, and the relevant mechanics.

**Exit:** the UI derives everything from authoritative state, displays truncation/errors honestly,
survives refresh, and has no mutation authority in browser state.

## Cross-cutting invariants

- One physical item instance has exactly one parent or is explicitly uncontained; never both an
  actor owner field and a containment owner.
- Definitions are immutable/versioned; instances refer to an exact version; “latest” never
  silently changes an existing instance.
- Every normal item instance resolves to one valid definition; missing/corrupt references fail.
- Non-stackable means implicit quantity one. Stackable means one positive safe-integer quantity.
- Split/merge/transfer/consume conserve quantity except for one declared consumption output.
- Derived totals use canonical integer units with checked arithmetic; no binary floating-point or
  caller-provided totals.
- Recursive totals include descendant contents exactly once and the ordinary container's own
  weight once.
- A capacity/carrying check never accepts a truncated graph or treats missing state as zero.
- Equipped implies direct valid possession and a supported mode; possession does not imply
  equipped.
- An empty-stack deletion detaches first in the same transaction. A nonempty container cannot be
  deleted implicitly.
- Physical currency conserves denomination counts and exact value; abstractions cannot be mixed
  silently.
- All item mutations are campaign-scoped, deterministic where no roll is required, transactionally
  atomic, evented, and auditable.
- Existing generic structural events remain the base evidence. Add semantic item events only when
  a downstream subscriber requires a stable domain fact that structural events cannot express.

## Acceptance matrix

### Definitions, instances, and versions

- catalog import/export preserves definition identity, version, source, measurement units,
  stack/container policy, and referenced profiles;
- revising a definition creates or selects an explicit version and does not mutate existing
  instance references;
- missing, unknown, wrong-scope, future, deprecated, and corrupt definition references fail;
- a canonical weapon profile cannot be used directly as a possessed campaign item.

### Containment and nesting

- direct, two-level, and maximum-supported-depth inventories read deterministically;
- one instance cannot have two parents; self/ancestor cycles fail;
- moving a container moves custody of the subtree without descendant rewrite effects;
- deletion of a contained item detaches it; deletion of a nonempty container fails;
- exceeding depth/node limits or truncating required data fails derived mechanics unchanged.

### Quantity and stacking

- min/max quantities, split one, split all rejection, merge, partial transfer, and last-unit
  consumption conserve exact counts;
- only identical derived stack keys merge;
- custom/charged/damaged/attuned/differently-versioned instances remain distinct;
- zero, negative, fractional, unsafe integer, duplicate ID, and extra derived input fail with no
  state change.

### Weight, capacity, size, and carrying

- unit mass × quantity, nested subtree totals, container self-weight, and actor burden are exact;
- backpack 30-pound, pouch 6-pound, quiver 20-arrow, and representative liquid/dry boundaries are
  tested only from source-backed facts;
- Strength/size formulas cover every transition and a trait that changes effective carrying size
  without changing actual size;
- adding/removing one unit changes burden by exactly one unit mass;
- over-capacity container and actor destinations fail; drag/lift/push reports the separate maximum
  and 5-foot speed consequence without pretending the object is carried;
- absent/corrupt mass, quantity, size, Strength, modifier, or incompatible capacity units fail
  rather than defaulting.

### Equipment and existing combat

- equip/unequip validates direct possession, definition support, conflicts, and exact state;
- a possessed/equipped weapon instance resolves the unchanged Feature 7 profile and produces the
  existing Feature 8/9 attack/damage behavior;
- stowed/nested/unowned/deleted/wrong-version/unsupported instances fail before attack randomness;
- equipment changes never modify actor ability, proficiency, AC, HP, or canonical weapon bytes;
- Feature 24/25 integration later proves armor, hands, ammunition, and action-economy behavior.

### Currency, grants, and consumption

- denomination values, wallet sums, coin counts, and fifty-coins-per-pound burden are exact;
- payments conserve value and respect physical location/capacity;
- starting grants create the exact item tree and no loose artifacts on failure;
- consumption consequence and quantity/use change are one transaction; guard/reaction failure
  restores both;
- same seed/input/state/version replays identically for any item activity that rolls.

### Routing, effects, audit, and restoration

- player phrases route among inspect, transfer, split/merge, equip, pay, and consume without
  selecting administrative correction;
- every accepted change has the exact expected ordinary effects, structural events, revisions,
  operation ID, and frozen projection/version evidence;
- rejection leaves exact entity/component/containment bytes unchanged and emits no accepted event;
- fresh-session inventory reconstruction needs only database state, not transcript memory;
- shared actors are restored and disposable fixtures are detached/deleted through governed paths;
- focused tests, full suite at feature acceptance, catalog validation after catalog changes, and
  `git diff --check` pass.

## Deliberate non-goals for the first release

- universal inventory-slot capacity or Diablo-style item grids;
- guessed item dimensions or item use of creature-size categories;
- older Variant Encumbrance states without an approved source;
- complete SRD equipment, tool, armor, weapon, or magic-item content packs;
- action-economy draw/stow integration before Feature 12;
- armor-derived AC, shields, ammunition, loading, range, Light/Heavy/Two-Handed, weapon mastery;
- identification, attunement, curses, magic charges, extradimensional containers;
- shops, price simulation, availability, procedural loot, crafting, durability, repair, or theft;
- arbitrary item scripts, caller-supplied effects/totals, direct browser writes, or item-specific
  C# domain classes.

## Plan-quality and change gate

This roadmap was converted into the formal [Feature 23 dependency plan](ruleset/dnd2024/feature-23/FEATURE-23-DEPENDENCY-PLAN.md)
on 2026-08-20; it remains planning rather than implementation. The plan must
re-read the live/imported `procedure.system.create-feature` contract, verify catalog/database
agreement at the required synchronization boundary, search owners and aliases, inspect the exact
official source pages, and convert Slice 0 plus the first missing implementation leaf into the full
Terra slice format.

Stop and revise this roadmap if any of the following occurs:

- another component/mechanic already owns a proposed fact or transformation;
- definition versioning cannot name an immutable revision without a broader catalog decision;
- recursive projection cannot provide complete bounded state without a public kernel change;
- a new item rule requires guessed volume, duplicated owner/location, stored derived totals, or
  caller-supplied canonical facts;
- a downstream Feature 24/25/29 rule would force a generic field to change meaning;
- starting equipment would create a second transaction/audit root;
- a supposedly generic “slot,” “resource,” “property,” or “size” field conflates distinct D&D
  mechanics.

No runtime game artifact is authorised merely by approving this roadmap.
