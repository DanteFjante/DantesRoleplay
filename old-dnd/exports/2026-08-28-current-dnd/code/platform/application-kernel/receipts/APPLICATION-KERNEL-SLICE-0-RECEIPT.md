# Application kernel Slice 0 receipt — semantic contract ratification

Status: **accepted**  
Completed: 2026-08-23  
Accepted contract: [Slice 0 semantic contract](../APPLICATION-KERNEL-SLICE-0-IMPLEMENTATION.md)

## Delivered

The user accepted S0.1–S0.10 as one ruleset-neutral package:

- `system` is reserved, `dnd2024` is the first opaque application ID, and non-system records require
  an explicit application owner.
- Immutable application activation is separate from explicit state-space upgrade.
- ECS values may use every bounded JSON kind; merge remains shallow and object-only, null remains
  distinct from removal, and schemas are exact immutable versioned contracts.
- Derived projections declare exact role, component-version, RFC 6901 path, projection, and output
  dependencies. Generic mapping is structural only, the graph is acyclic and reversible for impact
  analysis, and derived caches never become state authority.
- Registered sources use allowed roots and relative path/glob specifications. Trust and explicit
  precedence resolve one effective winner before import, search, vectors, or AI; remote directory
  creation and unrestricted filesystem browsing are excluded.
- Application catalogs use authored described logical nodes, deterministic traversal and lexical
  ranking, exact inspection, bounded pages, and authenticated manifest-bound cursors without
  depending on vectors or local AI.
- The proposed `system.*` query and commit names are reserved while their serialized contracts and
  remote exposure remain owned by Slice 10 and gated on E9 authorization.
- Compatibility is additive and no-change on failure. Models remain downstream consumers and never
  become registration, validation, authorization, migration, or execution authority.

## Evidence

- User confirmation: “Continue” in direct response to the request to approve S0.1–S0.10 as a
  package.
- Local Markdown targets in the Slice 0 document were resolved successfully.
- `git diff --check -- platform/application-kernel` passed; Git emitted only line-ending notices for
  pre-existing modified files.
- Inspection confirmed that Slice 0 added documentation and owner links only.

## Deliberate exclusions

No runtime code, permanent schema, table, migration, catalog record, application/source/state-space
registration, protocol registration, compatibility alias, game content, or AI integration was
created. Numeric safety defaults, serialized contracts, persistence layout, legacy ownership,
backfill, authorization implementation, and migration details remain assigned to their later
slices and confirmation gates.

## Exit and next gate

Dependency leaf A is accepted. Slice 1 may now be authored as a separate read-only inventory slice.
It must classify existing namespaces, schemas, values, sources, and public kinds without mutating
the database or catalog, and must escalate genuinely ambiguous ownership such as `game.core.*`
rather than guessing.
