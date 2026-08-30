---
id: procedure.mechanic.dnd2024.currency-value
category: ruleset.dnd2024.core.data.currency-value
name: Read derived D&D 2024 currency value
governs: mechanic.dnd2024.currency-value.read
status: active
---

## Description

Derives physical coin count, copper-piece value, and denomination breakdown below one custody root.

## Instructions

Read only positive canonical coin stacks. Denomination identity comes from each immutable definition
link; copper conversion ratios are derived by the mechanic from the SRD coin table and are never
stored on the runtime item.

## Constraints

This creates no wallet, balance, exchange, spending, pricing, transfer, or cached value. The view is
bounded and effect-free; incompatible visible currency fails closed.
