---
id: procedure.system.create-feature
category: system
name: Add a feature
governs: adding a capability that does not exist yet
status: active
createdBy: "seed"
changeNote: "Re-seeded: the bootstrap file changed."
---

## Description
How to add one capability safely without making repository development imitate a live MCP game
session. Repository files are authoritative while developing; SQLite remains authoritative for a
running game's state and for content authored by an agent that has no filesystem.

## Instructions
1. Define one target capability, its boundary and explicit non-goals. If it can be expressed as
   data, a contract or JavaScript, it does not go in C#.
2. Search for an existing owner before creating a permanent id. Extend or revise that owner when
   its meaning fits; create only when the responsibility is genuinely new.
3. Choose the authoring mode before writing:
   - **Repository mode:** edit canonical files and use the developer validation loop below.
   - **MCP-only mode:** the database is the available authoring surface; follow
     `procedure.system.use` and the relevant write contract, including dry-run and query-back.
   Do not mix the modes for the same records without an export or import boundary.
4. Plan proportionally. Cross-subsystem or multi-slice work needs a dependency plan ordered to
   verified leaves. A small change needs only a stated boundary, its tests and an objective exit
   condition. Do not reproduce the common verification workflow inside every feature plan.
5. Select one coherent, reviewable slice whose dependencies are already implemented. It may
   include the contract, schema, mechanic and tests required to make that slice real; do not split
   those inseparable artifacts merely to create more approval stops.
6. In repository mode, read `procedure.system.modify` for C# changes and only the domain contracts
   relevant to the slice, directly from `catalog/procedures`. Filesystem edits do not require MCP
   orientation, contract citations, dry-run commits, query-back calls or operation ids.
7. Author procedures, mechanics, component definitions, event types, subscriptions and ruleset
   fixtures under `catalog/`. A mechanic's `.md` metadata and `.js` source land together. Core
   contracts are embedded from these same files; there is no second `Bootstrap/` copy.
8. Add a focused test that would fail without the change. Run focused tests while iterating and
   `.\roleplay validate catalog` after catalog edits. Validation imports a disposable copy into a
   fresh migrated database, applies the production write checks and proves a clean round trip; it
   never changes the live database.
9. At feature acceptance, run the full suite once. Run guard tests when the kernel or MCP surface
   changed, and the protocol walk only when the MCP surface or dependency registration changed.
   Record the commands and outcome in a short receipt when durable evidence is needed; operation
   ids are evidence for live operations, not filesystem edits.
10. Import into the persistent database only for integration play or release. First inspect
    `.\roleplay import catalog --dry-run`; resolve live/file drift or export live work, then run
    `.\roleplay import catalog` and `.\roleplay verify catalog`. Never force a side merely to make
    the report clean.
11. Stop for human confirmation at semantic boundaries: a new permanent id, a changed schema
    meaning, a migration, a public surface change, a destructive operation or completed feature.
    Routine edits inside an approved boundary do not require confirmation after every file or
    dependency leaf.
12. If implementation reveals an unresolved dependency or changes the feature boundary, revise
    the plan and descend to it instead of bypassing it with caller-supplied data or duplicated
    logic.

## Constraints
- Repository files are authoritative for developer-authored code and catalog content. SQLite is
  authoritative for live world state, history and unsynchronized MCP-authored content.
- One coherent slice and the contracts and tests that make it usable land together.
- A slice is either verified against its exit gate or remains pending. "Mostly complete" does not
  authorize work on a dependent slice.
- Never bypass a dependency with caller-supplied derived values, duplicated state, placeholder
  data or a second copy of an existing rule. Temporary scaffolding must have explicit removal
  criteria.
- Evidence for a repository dependency names a test or inspected artifact. Evidence for a live
  operation names its query result or operation id. Do not demand both forms for the same fact.
- Do not treat the persistent database as a required intermediate output of ordinary development.
  A fresh validation database is the development gate; catalog/live agreement is the integration
  and release gate.
- If the feature needs a new query kind or commit kind, follow `procedure.mcp.add-tool` instead.
  There are three tools and there will not be a fourth, and a new kind is nearly always the wrong
  answer too—check first whether it fits behind an existing kind or is a contract rather than a
  capability.

