---
id: procedure.mechanic.dnd2024.currency-value
category: ruleset.dnd2024.core.data.currency-value
name: Read derived D&D 2024 currency value
governs: mechanic.dnd2024.currency-value.read and the read-only derivation of physical currency stacks
status: active
---

## Description

Derives the value of physical D&D 2024 currency stacks inside one selected custody root. Currency
is still an ordinary fungible item stack: this procedure creates neither a wallet nor a balance
that can be edited independently of those stacks.

## Instructions

1. Use `mechanic.dnd2024.currency-value.read` with the creature, container, or other explicit
   custody root whose bounded nested contents are being inspected.
2. Read only an item instance's exact immutable definition and its compatible positive quantity.
   A qualifying definition has `kind: "currency"`, `stackPolicy: "fungible"`, and its closed
   `currency` metadata.
3. Return the exact derived copper-piece total, physical coin count, and a deterministic
   denomination breakdown. The result is a read model and proposes no effects.

## Constraints

- The five denomination definitions remain the sole source of conversion data. A stack's value is
  its quantity multiplied by that definition's `copperValue`; do not persist a wallet total,
  normalize stacks, make change, spend, exchange, price, or transfer currency here.
- A fungible currency item without a compatible positive `dnd2024.item-quantity` is corrupt for
  this reader and fails closed. Non-currency physical items below the root contribute no value.
- `coinsPerPound: 50` is a definition invariant already consumed by burden; this reader neither
  duplicates mass calculation nor changes physical quantity.
- The bounded containment projection is an inspection limit, not an assertion that omitted deeper
  currency is absent. Consumers must surface the result as bounded if they need a complete wallet.

## Verification

- Prove mixed nested denomination stacks derive their exact copper value and count without effects.
- Prove the reader rejects an incompatible currency stack rather than reporting a false total.
- Prove non-currency items do not create wallet authority or a parallel balance.
