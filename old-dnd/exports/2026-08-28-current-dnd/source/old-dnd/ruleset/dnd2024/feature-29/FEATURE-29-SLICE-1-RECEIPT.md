# Feature 29 — Slice 1 receipt

Date: 2026-08-21

## Delivered boundary

- Added the closed `dnd2024.magic-item-profile` component and its governing static-profile
  procedure.
- Added immutable, source-cited profiles for the Potion of Healing, Boots of Elvenkind, and Amulet
  of Health. Together they represent a consumable, a non-attuned worn item, and an attuned worn
  item.
- Each profile declares only its version, key, source, category, rarity, attunement requirement,
  physical-use mode, activation family, consumable/no-charge facts, and effect-family key.

## Explicitly not delivered

No physical item definition or instance, custody or equipment state, attunement list or limit,
Short-Rest action, charge state or recharge, activation operation, mechanical effect, event,
subscription, or campaign state was added.

## Verification evidence

- `CatalogFeature29Tests` passed: 2/2 tests. The tests prove that all three profiles import with
  their exact static facts and that the schema rejects attunement state, a false attunement flag,
  and encoded healing mechanics.
- `roleplay validate catalog` passed with the repository-wide near-duplicate warnings. The
  disposable validation did not touch live data.

## Next boundary

Slice 2 remains blocked on the Feature 23 physical-definition bridge. It must establish the
ownership boundary between an ordinary item definition and an instanced magic item before any
possession, equipment, or attunement work begins.
