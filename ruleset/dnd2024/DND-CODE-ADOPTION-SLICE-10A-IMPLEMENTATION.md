# D&D code-adoption Slice 10A implementation — SRD currency definitions

Status: **implemented; acceptance pending confirmation**  
Parent: [Slice 10 static-content design](DND-CODE-ADOPTION-SLICE-10-DESIGN.md), leaf 10A  
Ruleset alignment: `dnd2024-owned`  
Source: `source.dnd2024.srd-5.2.1`, `Equipment > Coins > Coin Values` (PDF p. 89)  
Effort: 4 EP  
Model assignment: `gpt-5.6-luna` medium for the mechanical cohort; `gpt-5.6-terra` for transform review

## Outcome and boundary

Recover the five archived currency definition IDs as immutable activated application content:
Copper, Silver, Electrum, Gold, and Platinum Pieces. Preserve the existing exact rational mass and
copper-value representation consumed by the accepted currency-value and burden readers.

This leaf adds no component or mechanic ID, formula, effect, migration, public operation, live-state
write, donor runtime, or archive mutation. It stops before commerce, treasure generation, exchange
actions, wallets, automatic state-space installation, and every non-currency content family.

## Deterministic mapping

The transform accepts only the five hash-locked archived entity records. It reconstructs a closed
entity envelope, fixes the historical locator `Equipment > Currency` to the official SRD heading
`Equipment > Coins > Coin Values (PDF p. 89)`, preserves the existing IDs/names/denominations,
requires `1/50` pound per coin, and derives copper values of `1`, `10`, `50`, `100`, and `1000` from
the official GP-relative table. Any source, ID, shape, value, or target collision drift fails.

## Dependencies and state

Each entity owns one `dnd2024.item-definition` component. The component is fungible currency with a
positive exact quantity supplied only by physical item stacks. Definition entities are immutable
source records; campaign stacks reference their exact entity ID. Derived currency value and burden
remain effect-free and are never stored as authority.

## Failure and acceptance

Malformed or stale source hashes, unexpected archive fields, wrong denominations/values, duplicate
IDs/paths, missing attribution, schema-invalid payloads, transform drift, or activation omission
fails without changing live state. Acceptance requires deterministic transform check, all five
schema validations, source preview/activation retention, representative runtime consumption by the
existing currency and burden mechanics, catalog validation, focused/full tests, and final user
confirmation.
