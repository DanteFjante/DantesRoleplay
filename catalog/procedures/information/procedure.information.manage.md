---
id: procedure.information.manage
category: information
name: Manage generic information sources and records
governs: commit(kind: "information-source"), commit(kind: "information-record")
status: active
---

## Description

Create bounded user-defined information outside game, campaign, world, and ruleset state.

## Instructions

1. Create a source with a stable id, generic scope id, name, optional description, and JSON-object metadata schema.
2. Add or revise text records by stable id and source id. Record metadata is an opaque JSON object.
3. Reuse an id only to retry identical content or deliberately revise that record.

## Constraints

- Sources and records contain no game semantics or access grants.
- A source scope is a concrete information namespace, not a campaign requirement. Use a namespace
  selector such as `game.worldname.*` only when reading or authorizing a family of scopes.
- Do not place credentials, secrets, or instructions for the model in metadata or content.
