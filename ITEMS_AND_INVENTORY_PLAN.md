# Items and inventory implementation plan

Status: **Draft — planning only; no item/inventory slice is authorised**  
Last updated: 2026-08-20

## Execution rule

Use [GAME_SYSTEM_MASTER_PLAN.md](GAME_SYSTEM_MASTER_PLAN.md) for cross-subsystem ownership,
[TERRA-FEATURE-PLANNING-GUIDE.md](ruleset/dnd2024/TERRA-FEATURE-PLANNING-GUIDE.md) for plan quality,
and a populated [SUBSYSTEM_IMPLEMENTATION_HANDOFF.md](SUBSYSTEM_IMPLEMENTATION_HANDOFF.md) for the active assignment. Implement one reviewed delivery
slice, meet its exit gate, record evidence, and stop.

## Goal

Represent reusable item definitions separately from campaign-owned item instances, then support
possession, nested containers, quantities, equipped state, transfer, consumption, and later item
activities without copying rules into characters or adding item vocabulary to the C# kernel.

The first release must let one created character possess and equip an existing canonical weapon,
move it between valid containers, and use its referenced weapon profile in an existing attack.

## Ownership model

### Item definition

An item definition is immutable, versioned content. It owns stable identity, name, item kind,
source/version, description summary, stackability, container capability, and references to
ruleset-specific profiles or activities.

Existing canonical Dagger, Shortbow, and Battleaxe entities remain the weapon-profile authority.
The generic item definition references the appropriate profile; it does not duplicate attack,
damage, proficiency, or mastery rules.

### Item instance

A campaign-owned item instance is an entity referencing one exact item-definition identity/version.
It owns mutable instance state only: quantity when stackable, charges when a later feature defines
them, condition/custom label, attunement/equipped markers where supported, and creation provenance.

Two characters may own separate instances of the same definition. Revising a definition never
silently changes the version referenced by an existing instance.

### Possession and containers

Containment is authoritative for possession/location:

- a character or container contains an item instance;
- a container item may contain other item instances;
- the same instance has one physical parent;
- containment cycles and invalid parents are rejected by existing structural rules.

Do not also store ownerId, locationId, or inventory arrays on the character/item. Read models derive
inventory and ownership from containment.

### Equipped state

Equipped state is not the same as possession. Proposed component: item.equipped.

It records the owning actor, equipment slot/category, status, and revision only when the actor
actually possesses the instance. The exact slot vocabulary belongs to the ruleset/equipment
feature. Equipping never copies weapon/armor statistics onto the actor.

Armor Class derivation from worn armor/shields is a later D&D feature. Initial equipment supports a
weapon being marked equipped and discovered by the existing attack mechanic through an explicit
role/reference.

### Currency, quantities, and consumables

Currency starts as a dedicated resource component or closed currency-purse component on an actor,
not thousands of coin entities. Exact ownership is decided in the D&D equipment slice.

Quantity exists only on stackable item instances and follows closed merge/split rules. Consumable
use must be a mechanic that validates possession/quantity/activity, applies the consequence, and
decrements/removes the instance in one transaction. It is not a generic “delete item” button.

## Proposed components and relationships

- item.definition: immutable generic identity/source/display/container/stack metadata;
- item.instance: definition/version reference, quantity, provenance, mutable instance status;
- item.equipped: actor/slot/status reference with possession invariant;
- item.activity-reference: references a versioned ruleset activity/mechanic definition;
- item.property: deferred ruleset-specific property references;
- containment: physical possession/nesting;
- item.related-to: optional provenance/quest/faction/world relationship, never ownership.

Exact IDs, status values, definition-vs-instance entity kinds, and source-version rules are ratified
in Slice 0.

## Mechanics and procedures

Proposed versioned mechanics:

- mechanic.item.instance.create creates one validated campaign instance from an eligible definition;
- mechanic.item.transfer validates source/destination and proposes containment.move;
- mechanic.item.stack.merge and mechanic.item.stack.split own quantity transitions;
- mechanic.item.equip and mechanic.item.unequip own equipped state;
- mechanic.item.consume validates and applies a declared item activity plus quantity change;
- mechanic.item.correct is administrative and separately governed.

Required procedures:

- procedure.item.define
- procedure.item.create-instance
- procedure.item.transfer
- procedure.item.equip
- procedure.item.consume
- procedure.item.inspect
- procedure.item.correct

Game-specific weapon, armor, class-grant, crafting, shop, loot, and magic-item rules remain in their
own ruleset plans.

## Delivery slices

### Slice 0 — ratify definition/instance boundary

Inventory existing weapon/profile entities and decide whether they are definitions directly or are
referenced by a new generic item definition. Fix exact component IDs, source/version semantics,
quantity rules, container capability, and missing-versus-empty behavior. Choose one character,
weapon, and container fixture.

**Acceptance:** every field belongs to definition, instance, containment, equipped state, or
derived projection exactly once; no actor stores copied item statistics or an inventory array.

### Slice 1 — item definitions and read discovery

Add the generic definition contract/component only where existing artifacts cannot already own it.
Connect the three verified weapon profiles without changing their attack/damage ownership. Add
bounded query/catalog discovery.

**Acceptance:** a caller can discover one definition and its exact weapon profile/source version;
invalid/missing source and duplicate identity are rejected.

### Slice 2 — instances and possession

Add item.instance, creation mechanic, and containment-based possession. Create one weapon instance
owned by the fixture character and one container instance capable of holding it.

**Acceptance:** query derives inventory from containment; invalid definition, non-stack quantity,
wrong scope, duplicate instance ID, and invalid/cyclic parent fail atomically.

### Slice 3 — transfer and nested containers

Add the transfer mechanic and bounded nested inventory projection. Define permitted destinations,
container capability, self/cycle protection, actor/location transitions, and event behavior.

**Acceptance:** moving the weapon character -> container -> character changes one containment edge
per operation, emits existing structural events, and never changes its definition/profile data.

### Slice 4 — equipped weapon state

Add equipped/unequipped state and possession invariant. Integrate one existing weapon attack so
the caller references the possessed/equipped instance and the mechanic resolves its immutable
profile/proficiency data without duplication.

**Acceptance:** the equipped weapon supports the existing attack/damage path; unowned, contained,
wrong-category, duplicate-slot, and corrupt reference cases fail unchanged.

### Slice 5 — stack quantities and consumption

Add stackable definition metadata, instance quantity, merge/split mechanics, and one minimal
consumable activity. Define integer ranges, canonical split IDs, zero-removal semantics, and
transaction order.

**Acceptance:** merge/split conserve total quantity; consumption applies consequence plus decrement
atomically; failure/guard/reaction rollback restores exact quantities and state.

### Slice 6 — starting equipment grants

Integrate item definitions/instances with CHARACTER_CREATION_PLAN.md grants. A background/class
grant resolves approved definitions and creates/contains instances in the character-creation root
transaction.

**Acceptance:** the sample character receives exactly its selected starting items; duplicate,
ineligible, wrong-version, or partial grant failure creates no character or loose items.

### Slice 7 — armor, currency, and advanced item rules

Plan separately before adding armor-derived AC, shields, ammunition/loading/range, weapon mastery,
currency/encumbrance, attunement, magic items, charges, loot tables, shops, crafting, or durability.
Each feature owns one authoritative state path and ruleset source.

**Acceptance:** no advanced feature enters through an unreviewed generic field.

### Slice 8 — read-only inventory UI

Add server-rendered character/container inventory views and SSE refresh after committed
containment/component changes. Later drag/drop transfer or equipment controls require authenticated
semantic commands and conflict/revision handling.

**Acceptance:** the read-only UI reflects derived containment/equipped state and remains correct
after refresh without direct browser mutation.

## Acceptance matrix

- definition/instance/version/source ownership and catalog round-trip;
- stackable versus non-stackable quantity, min/max/zero behavior, merge/split conservation;
- possession readback, nested containers, cycle/self/invalid parent rejection;
- transfer success, guard/reaction failure, rollback, and structural event evidence;
- equip/unequip ownership, slots, duplicates, missing/corrupt definition/profile state;
- existing weapon attack/damage compatibility without copied fields;
- starting equipment grants in character-creation transaction;
- consumable consequence and quantity update atomicity;
- source revision preservation and explicit migration/correction;
- fresh-session inventory reconstruction and fixture restoration;
- read-only website projection and no authority in browser state.

## Non-goals

No shopping economy, prices/markets, crafting, durability, procedural loot, full SRD equipment
catalog, magic items, attunement, armor formulas, encumbrance, ammunition accounting, drag/drop
writes, or item-specific C# classes are included in the first release.

## Dependencies and handoff

The generic world model, containment/effects/events/audit, and existing weapon profile/attack/damage
features are prerequisites. Character creation consumes Slice 1–4 and later starting grants.
Before Item Slice 6 or Character Creation Slice 5 is assigned, Terra High must ratify one owner for
the atomic character-plus-starting-equipment root transaction. The non-owning slice exposes a
closed grant capability and must not create a second transaction/audit root. Campaign/quest/world
features may link items but do not own their possession or mechanical profile.

Each implementation handoff names one slice and fills SUBSYSTEM_IMPLEMENTATION_HANDOFF.md. Terra
High handles Slice 0 and any ruleset ownership/source decision. A lower model may implement a
ratified catalog/component/test slice only with exact IDs, shapes, expected state, and cleanup.
