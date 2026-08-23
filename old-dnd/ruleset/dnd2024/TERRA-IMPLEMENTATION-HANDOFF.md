# D&D 5e 2024 implementation handoff adapter

Status: Adapter template — no active assignment

This file adds D&D 5e 2024 constraints to the shared handoff template. It never selects a feature
or authorizes implementation by itself. Completed work belongs in receipts, not here.

## Required read order

1. `../../docs/IMPLEMENTATION_DOCUMENT_READING.md`
2. one active dependency tree and its selected ready leaf
3. one active feature implementation document
4. `../../SUBSYSTEM_IMPLEMENTATION_HANDOFF.md`
5. `TERRA-FEATURE-PLANNING-GUIDE.md`
6. only the contracts and owners named by that feature document

Stop if there is no single active feature document, the selected leaf is not ready, or the files
disagree about scope or ownership.

## Assignment fields

A populated handoff must name:

- feature id and one bounded slice;
- dependency-tree path and selected ready leaf;
- alignment class: `dnd2024-owned`, `dnd2024-compatible`, or `ruleset-neutral`;
- canonical source id and exact locator for every owned D&D rule;
- existing catalog owners to reuse;
- intended catalog, JavaScript, C#, schema, fixture, and test changes;
- exclusions, failure behavior, acceptance checks, and stop conditions.

## D&D 5e 2024 gates

- Use revision scope `dnd2024` and canonical source `source.dnd2024.srd-5.2.1`.
- Cite an exact SRD 5.2.1 locator before implementing a `dnd2024-owned` rule.
- Do not fill gaps from remembered 2014 rules, model knowledge, or unofficial sources.
- Label compatibility behavior, optional rules, and house rules explicitly.
- Reuse existing capability, mechanic, rule, condition, property, action, and event owners.
- Treat caller-supplied derived values as untrusted; derive them from authoritative state.
- Put D&D-specific mechanics in authored catalog JavaScript. Keep C# ruleset-neutral unless an
  approved public boundary explicitly requires otherwise.
- Keep content definitions, campaign state, and mechanical behavior separate.

## MCP payload facts

When a feature changes the MCP surface or is verified through MCP:

- call `orient` before contract-dependent operations;
- pass nested payload fields as JSON strings when the tool contract requires them;
- encode `matches`, `requirements`, and `source` fields according to the active schema;
- ensure stored JavaScript source has a valid final `return` when the contract requires one;
- encode action input as JSON text and `roleEntityIds` as an object;
- include at least one expected failure case so rejection behavior is proven.

These facts supplement the active contracts; they do not replace reading them.

## Completion

Run focused tests, `roleplay validate catalog` after catalog edits, and the full suite at feature
acceptance. Run the protocol walk only when the MCP surface or dependency registration changed.
Record durable proof in a short receipt, then return this adapter to its no-assignment state.
