# Development workflow

Use this guide for code, tests, schemas, and catalog changes. The default reading set is [AGENTS.md](../../AGENTS.md), [README.md](README.md), this guide, and the exact implementation files involved.

## Before editing

1. Search for the existing owner in code, `catalog/`, and focused tests.
2. Decide whether the behavior is generic host behavior or game-specific behavior.
3. Inspect the smallest relevant contract: interface/schema, implementation, registration, and focused tests.
4. Check the working tree and preserve unrelated user changes.

Do not create a dependency tree, implementation plan, handoff, receipt, or status diary as a prerequisite. Keep a temporary plan in the task or issue unless the user asks for a durable document.

## Placement

Put generic storage, validation, sandboxing, typed-effect application, transaction, audit, retrieval, or protocol behavior in C#.

Put ruleset vocabulary, IDs, formulas, eligibility, choices, and outcome branching in catalog data or JavaScript. D&D-specific material belongs under `catalog/applications/dnd2024/` and must not leak into the generic C# kernel.

Schemas define stored component state. Procedures define how a capability is invoked and what context it receives. JavaScript mechanics calculate game-specific results. Tests should assert the boundary as well as the result.

Application-facing read models follow the same ownership rule. Register a closed query contract
under the application `queries/` tree and point `mechanic-projection` at one exact active,
effect-free JavaScript mechanic. The generic host resolves installed component identities, runs the
sandbox, validates the returned data against the registered output schema, and binds the response
to state-space, resolution, result, and source-revision fingerprints. A website must consume that
read model instead of reproducing ruleset calculations or scanning raw ECS components. Host-bound
audience policy selects which entity may be projected; the request or model never selects its own
audience.

## Validation

Run checks in proportion to the change:

| Change | Minimum checks |
| --- | --- |
| C# implementation | Build plus focused tests |
| Catalog records, schemas, procedures, or mechanics | Focused tests plus `.\roleplay.cmd validate catalog` |
| Persistence or transaction behavior | Focused persistence tests and affected integration tests |
| MCP surface or dependency registration | Focused tests plus a protocol walk |
| Feature acceptance or broad refactor | Full solution build and full test suite |

Common commands:

```powershell
dotnet build DantesRoleplay.slnx
dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj
.\roleplay.cmd validate catalog
```

Catalog validation uses a fresh disposable database and does not change the live database.

## Visual media

Visual media belongs to its ECS entity through `game.core.media.visual`; maps use the same blob
identity through `game.core.world.map.visual`. SQLite owns the attachment metadata and visibility,
while immutable PNG, JPEG, or WebP bytes live in the adjacent content-addressed `blobs/` store.
Website and AI callers discover media through the owning entity. They must not turn a raw digest
or a repository path into a public URL, because doing so would bypass Player/DM visibility.
The host binds `system_entity_media`, `system_current_location_media`, and
`system_current_location_map` to its trusted seat and returns structured attachments; models and
browsers never choose an audience. Runtime item media overrides the same role inherited from its
definition, while unoverridden illustration and icon roles remain inherited.

The play conversation resolves its persisted current situation's exact location through the same
owner-bound media endpoint. It prefers a setting or scene card, re-resolves it after location
changes and page refreshes, and silently omits unavailable media. Conversation records therefore
retain authoritative entity and situation identities rather than copying blob paths or visibility
metadata into transcript text.

Import reviewed files with the production verification path before changing an entity reference:

```powershell
.\roleplay.cmd import-media <image> [<image> ...] --database <database>
```

The command verifies length, media signature, and SHA-256 during upload, then reopens and rehashes
the finalized bytes. It does not change ECS associations or delete sources. Backups and restores of
a runtime database must include both the SQLite file and its adjacent `blobs/` directory.

## Published web bundles

Build an application page bundle from its maintained browser source, then publish it through the
registered ECS page identity. The operator route is
`PUT /api/control/web/applications/{applicationId}/pages/{entityId}/bundle` with an
`application/zip` body containing root `index.html` and assets below `assets/`. The route resolves
the versioned content owner from `system.web.page`; it never accepts a raw content-page ID. Bundle
validation, immutable revision creation, activation, authorization, and same-origin checks all run
before the new revision becomes visible.

## Changes needing confirmation

Pause for confirmation before introducing permanent IDs, changing schema meaning, adding a migration, changing a public surface, crossing an ownership boundary semantically, or performing a destructive operation that the user has not already authorized.

At completion, report what changed, relevant check results, and any deliberate exclusions. Update current documentation only if a durable rule or operating procedure changed.
