---
id: procedure.mechanic.dnd2024.item-definition
category: ruleset.dnd2024.core.data.item-definition
name: Define immutable D&D 2024 items
governs: catalog authoring of dnd2024.item-definition and the immutable definition references consumed by future campaign item instances
status: active
---

## Description

Defines the source-cited immutable item-definition vocabulary used by D&D 2024 inventory features.
An item definition is a catalog entity, never a campaign possession. Its versioned entity id is its
durable identity; later physical instances reference that exact id and do not copy its static facts.

## Instructions

1. Read the source entity named by `sourceRef` and use a stable heading locator plus page where
   useful. This initial set uses `source.dnd2024.srd-5.2.1`.
2. Create `dnd2024.item-definition` as the closed schema. Static measures use positive-or-zero
   rational pounds/feet so 50 coins per pound is exact rather than a floating-point approximation.
3. Give every definition a permanent id ending in `.v1` and `definitionVersion: 1`. A correction
   that would change a static fact creates a new entity id ending in the next version; never revise
   a referenced definition in place.
4. Keep weapon combat statistics in Feature 7's canonical weapon-profile entity. A weapon item
   definition may name that profile through `weaponProfileId`, but may not duplicate its category,
   abilities, or damage.
5. Seed only the representative ordinary definitions in this slice: backpack, pouch, quiver,
   hempen rope (50 feet), dagger, and the five coin denominations.

## Constraints

- `definitionVersion`, kind, future stack policy, exact mass, optional ordinary capacity, optional
  currency metadata, optional weapon-profile reference, and source attribution are static data.
- Do not attach this component to a creature, a campaign item instance, an encounter, or a
  container in play. Slice 3 owns instances and custody; Slice 4 owns quantity operations; Slice 5
  owns burden calculation; Slice 7 owns transfer admission; Feature 9 owns currency transactions.
- Do not add price, owner, equipped state, quantity, derived totals, magical exceptions, attunement,
  actions, or arbitrary item scripting.
- This slice has no action mechanic: definitions are reviewed catalog data. Runtime mutation of a
  published definition requires a separately governed migration, not `record` or `correct` mode.

## Verification

- Fresh-import the catalog and prove all ten definition entities have the closed component with
  fixed source refs and exact rational measures.
- Prove the Dagger definition references `weapon.dnd2024.dagger` while that existing entity remains
  the sole owner of the weapon profile.
- Reject malformed definition data through the component schema coverage test; no campaign instance
  or inventory mutation may be introduced.
