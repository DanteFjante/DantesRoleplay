# Feature 23 Slice 2 receipt — immutable item definitions

Reconciled: 2026-08-20

## Outcome

Implemented the catalog-owned, immutable definition boundary for Feature 23. A definition is a
versioned catalog entity, identified by its permanent `.v1` entity id. Future campaign instances
will reference that exact id rather than copying static item facts. A material correction requires a
new versioned definition entity; the catalog data is not a campaign item record.

The original overlapping draft split these facts across three components. It was consolidated into
the single closed component required by the governing procedure, so one definition is the sole
source for its static mass, ordinary capacity, currency conversion, optional weapon-profile link,
and source citation.

## New permanent vocabulary

- `procedure.mechanic.dnd2024.item-definition`
- `dnd2024.item-definition`
- `item.dnd2024.backpack.v1`, `item.dnd2024.pouch.v1`, `item.dnd2024.quiver.v1`
- `item.dnd2024.hempen-rope-50-foot.v1`, `item.dnd2024.dagger.v1`
- `currency.dnd2024.copper-piece.v1`, `.silver-piece.v1`, `.electrum-piece.v1`, `.gold-piece.v1`, `.platinum-piece.v1`

The closed component stores static kind, stack policy, exact rational mass, optional ordinary
capacity/length, optional currency conversion, optional weapon-profile reference, and a fixed SRD
source reference. It stores no instance, custody, quantity, equip, burden, price, magic, or action
state.

The Dagger definition references `weapon.dnd2024.dagger`; Feature 7 remains the sole owner of the
weapon profile's combat data.

## Verification

- `CatalogFeature23Tests` passed. It fresh-imports the catalog, validates all ten definitions
  against the published JSON schema, checks exact capacities/masses/currency values, and rejects a
  weapon definition without its required profile reference or a currency definition with an invalid
  stack policy.
- `roleplay validate catalog` passed: 193 records and two pre-existing procedure overlap advisories;
  no live data was touched.
- `git diff --check` passed for the Slice 2 files.

`CatalogValidationTests.Repository_catalog_validates_without_changing_its_files` was not used as
acceptance evidence: it failed its before/after snapshot because the shared worktree's catalog
manifest and unrelated catalog records changed concurrently. That is external drift, not a Slice 2
validation failure; the focused import/schema test and the disposable catalog validator both pass.

Slice 3 is separately verified. It introduces the campaign item-instance lifecycle and direct
custody without duplicating definition facts or inventing an inventory array.
