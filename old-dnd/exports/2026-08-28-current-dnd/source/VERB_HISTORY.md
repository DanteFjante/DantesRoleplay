# MCP verb history

This document records the MCP surface that existed immediately before the three-verb migration.
It is historical documentation, not the current operating contract. The current public surface is
described by `orient`, `query`, and `commit`.

Archived on 2026-08-18.

## The twelve-verb surface

The old server exposed these twelve tools:

| Verb | Purpose | Implementation area |
| --- | --- | --- |
| `orient` | Describe the system, current counts, capabilities, and next steps. | `OrientTool` |
| `find_procedures` | List or search procedure contracts. | `ProcedureTools` |
| `get_procedure` | Read one procedure, optionally at a historical version. | `ProcedureTools` |
| `write_procedure` | Dry-run, create, or append a procedure revision. | `ProcedureTools` |
| `describe_world` | Read component definitions and example entities. | `WorldTools` |
| `get_entities` | Read entities by id or search criteria. | `WorldTools` |
| `define_component` | Add a component definition to the world model. | `WorldTools` |
| `apply_effects` | Validate and apply a structural effect list atomically. | `WorldTools` |
| `find_mechanics` | Search mechanics or read one in full. | `MechanicTools` |
| `write_mechanic` | Dry-run, create, or append a mechanic revision. | `MechanicTools` |
| `run_action` | Select and execute a mechanic through projection, sandbox, effects, and audit. | `ActionTools` / action runner |
| `history` | Read recent operation audit records. | `HistoryTool` |

## Why this is retained

The old names remain meaningful when reading historical audit records, coldwalk notes, and earlier
client transcripts. They also provide the implementation mapping for the three-verb adapter:

| Historical family | New call form |
| --- | --- |
| `find_procedures`, `get_procedure` | `query(kind: "procedures", ...)` |
| `describe_world` | `query(kind: "world", ...)` |
| `get_entities` | `query(kind: "entities", ...)` |
| `find_mechanics` | `query(kind: "mechanics", ...)` |
| `history` | `query(kind: "history", ...)` |
| `write_procedure` | `commit(kind: "procedure", payload: {...})` |
| `define_component` | `commit(kind: "component", payload: {...})` |
| `apply_effects` | `commit(kind: "effects", payload: {...})` |
| `write_mechanic` | `commit(kind: "mechanic", payload: {...})` |
| `run_action` | `commit(kind: "action", payload: {...})` |

This migration does not add a new world model, a new storage model, or special system-commit
semantics. Those decisions remain future work and must be governed by their own procedure before
being implemented.
