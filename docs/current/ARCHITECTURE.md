# Architecture

Last reviewed: 2026-08-31.

DantesRoleplay is a generic C# host for data-authored roleplaying games. The host knows how to execute and audit declared behavior; it must not know the rules or vocabulary of D&D or any other game.

## Sources of authority

| Concern | Authority |
| --- | --- |
| Generic execution, persistence, transactions, effects, retrieval, and protocol hosting | C# projects |
| Game rules, eligibility, calculations, and outcomes | Catalog JavaScript mechanics |
| Persistent state shape | Catalog component JSON Schemas |
| Authored development records | `catalog/` |
| A running game's campaigns, state, events, history, and MCP-authored records | SQLite |
| Finalized blob metadata and upload state | SQLite |
| Immutable runtime blob bytes | Content-addressed `blobs/` storage beside the database, or `BlobStorage:Root` |
| Contributor guidance | `docs/current/` |
| World-building working material, artwork, and maps | `docs/world/`, read only for the relevant world task |
| External rules sources | `docs/pdfs/`, used only as references while authoring catalog content |

Documentation, UI models, and plans are never runtime authority.

## Runtime flow

1. A client calls the MCP surface.
2. The host resolves the declared procedure and materializes only its declared context.
3. If rule behavior is needed, the host runs the selected catalog mechanic in the sandbox.
4. The mechanic returns a generic result and typed effects.
5. The C# host validates the envelope and effects, applies them transactionally, and records the operation.
6. Retrieval exposes authorized projections of the resulting state.

The host owns safety and consistency. The catalog owns meaning.

## The C# boundary

C# may:

- identify, store, version, and retrieve generic records;
- validate schemas, envelopes, capabilities, limits, and effect types;
- materialize declared inputs and execute JavaScript in a constrained sandbox;
- apply generic typed effects transactionally;
- audit operations and expose protocol-neutral results.

C# must not:

- special-case a ruleset, campaign, spell, class, species, condition, or event type;
- contain formulas or branching whose result can vary by game rules;
- infer gameplay semantics from a record ID;
- duplicate a catalog mechanic as a supposedly convenient host shortcut.

Generic security, resource, and transaction invariants remain host responsibilities.

## Project map

- `DantesRoleplay/` contains the domain model, ECS concepts, and generic kernel contracts.
- `DantesRoleplay.DataAccess/` contains SQLite persistence, retrieval, registrations, catalog access, and `Mechanics/JintMechanicEngine.cs`, the JavaScript sandbox implementation.
- `DantesRoleplay.MCPServer/` hosts the MCP endpoint and composes runtime dependencies.
- `DantesRoleplay.Tools/` implements catalog maintenance commands.
- `DantesRoleplay.Tests/` contains kernel, persistence, protocol, catalog, and feature tests.
- `DantesRoleplay.LocalAI/` and its tests contain local-model integration.
- `DantesRoleplay.Web/` is a client of the runtime, not an authority for game state.
- `catalog/applications/dnd2024/` is the D&D 2024 catalog application. D&D-specific content belongs there or in shared catalog mechanics it explicitly uses.

## Invariants

- Stable record IDs are contracts. Add or change them deliberately.
- Component schemas own stored state shape; mechanics do not invent undeclared state.
- Procedures declare the context and capabilities mechanics may use.
- Effects are validated before mutation and applied within the owning transaction.
- Live database records are exported and reviewed before corresponding authored files are changed.
- Web features read and write through supported runtime operations; they do not become a second game-state store.
