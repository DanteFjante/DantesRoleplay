---
id: procedure.information.action
category: information
name: Use scoped information action contracts
governs: query(kind: "information-actions") and the host-registered contracts behind it
status: active
createdBy: "seed"
changeNote: "Seeded from bootstrap file."
---

## Description
List, define, and execute explicit action contracts in a generic information namespace. A contract
links rule records to one host-enabled executor; it does not grant access or allow arbitrary code.

## Matches

## Instructions
1. Store rule text as information records in a concrete namespace such as `game.worldname.rules`.
2. Define an action contract in the same namespace family. Give it the explicit executor id,
   JSON Schema for its input object, and the record ids that define its rules.
3. Query `information-actions` with an authorized namespace selector such as `game.worldname.*`.
4. Execute only the returned contract by id. The host rechecks namespace authority and validates
   the input against the stored schema before dispatching it.

## Constraints
- `query(kind: "information-actions")` is the only protocol verb this contract has. There is no
  commit kind for declaring or invoking an action; a host registers both.
- A namespace selector is either one concrete scope, a terminal `.*` prefix selector, or `*` when
  the host policy explicitly grants all scopes.
- Contracts are declarations, not executable model text. Only a host-registered executor can run.
- `kernel.mechanic-action` invokes the existing generic action runner; D&D and other rules remain
  authored catalog JavaScript and are never reimplemented by this information layer.
- An unavailable executor, unauthorized namespace, missing contract, or invalid input changes no
  state.
