# Catalog and live database

The repository catalog and a running SQLite database have different authority. Treat synchronization as an explicit, reviewed operation.

## Authority

- `catalog/` owns authored development procedures, schemas, fixtures, applications, and JavaScript mechanics.
- SQLite owns a running game's campaigns, world state, events, notifications, operation history, and records authored through MCP.
- Stable IDs connect the two. File location alone does not change a record's authority.

## Namespace registry and file layout

Namespaces are database records, not conventions inferred from whatever folder a file happened to
use. Each namespace declares its owner, description, aliases, allowed record kinds, enabled state,
and review state with a reason. Child namespaces must be registered after their parent, keep the
same owner, and cannot become reviewed before their parent.

Catalog schema version 2 maps the namespace portion of a qualified ID directly to directories. For
example, `dnd2024.character.ability-score` is written as
`components/dnd2024/character/ability-score.json`. Namespace declarations live beside that shape at
`namespaces/dnd2024/character/_namespace.json`. Version-2 import refuses records in a non-canonical
directory, manifest entries with noncanonical paths, misplaced or orphan mechanic/schema sidecars,
unknown namespaces, wrong record kinds, or owner-inconsistent namespace trees. It validates the
complete managed-file inventory under the catalog roots, so an old flat file cannot be silently
ignored beside its namespace-directory replacement. Version-1 catalogs remain readable so an
existing catalog can be exported deliberately instead of breaking in place; every new export uses
version 2. The authoritative repository catalog has completed this version-2 cutover, so new
authored records must use its canonical namespace layout.

An older database with no namespace rows is in adoption mode. Its first export derives namespace
declarations from its stored qualified IDs without modifying the database. Importing the reviewed
export registers those declarations. Once any namespace is registered, the database save boundary
rejects every new authored identity whose namespace is unknown, disabled, unreviewed, or does not
allow that record kind, even when a caller bypasses the normal catalog importer. An inferred export
starts as `needs-review`; inference proves directory coverage, not correct ownership.

The current repository review approves the active namespace tree. Every authored record now belongs to an
enabled, reviewed namespace, and `roleplay validate catalog` reports zero namespace-review
warnings. The former inverted D&D roots and unqualified legacy root remain as disabled migration
markers; ordinary namespace lookup and writes cannot reuse them. Their records were moved to
`dnd2024.*` and `fixture.legacy.*` identities.

Use `roleplay migrate-identities <catalog> <plan.json>` to validate a batch of reviewed identity
renames without changing files. Add `--apply` only after the dry run succeeds. The operation stages
the whole catalog, rewrites exact references, round-trips it through a fresh disposable database,
and requires warning-free validation before committing the files. It never opens the live game
database. `--references-only` completes stale-reference repair after an interrupted rename and
refuses while any source identity still exists.

Live ECS identities use a separate, backup-first boundary. Review a bounded JSON plan, run
`roleplay migrate-ecs-identities <plan.json>` to inspect exact component and relationship counts,
then repeat the identical command with `--apply`. The apply path creates a consistent SQLite backup
and delegates every rename, value rewrite, collision check, revision change, and source retirement
to the transactional ECS lifecycle store. Do not rename ECS rows with direct SQL. Schema-changing
component migrations must provide exact state-space, entity, and expected-revision-bound rewritten
values for every incompatible live component.

Applications can declare reviewed base applications. A state space remains bound to the exact
application revision and activation fingerprint recorded when it was created or last upgraded;
application discovery must not reinterpret an older binding through the latest revision. After a
base or activation change, preview and commit an explicit state-space upgrade using its current
binding fingerprint. System-owned kernel components remain valid in every application state space;
application-owned components must belong to the exact application revision or one of its bases.

Use `roleplay import catalog --namespaces-only --database <path>` to synchronize reviewed namespace
metadata without applying unrelated catalog-record drift. Add `--dry-run` to inspect its exact
created/updated counts first.

Namespace descriptions and aliases are searchable. Catalog lexical search ranks qualified IDs,
names, aliases, match phrases, descriptions, and logical paths. `system.catalog.search` and
`system.feature-search` accept `namespaceId`; the web catalog-search endpoint accepts the same value
as a query parameter. The filter includes the selected namespace and all descendants, is bound into
catalog pagination cursors, and normally omits disabled namespaces.

Extension precedence is activation metadata. Each immutable extension registration targets one
application and declares its source membership, contributed namespace roots, dependencies,
conflicts, and explicit precedence over the base application or other extensions. Preview and
activation reject missing dependencies, active conflicts, cycles, and any precedence set that does
not produce one deterministic order. The active manifest retains the selected registrations and a
resolution fingerprint; later search never consults mutable installation presets.

Catalog browse, catalog search, and feature search automatically use that active application resolution. Records sharing a
kind and the same dotted suffix beneath their owning base or extension namespace form one logical
resolution key. Ordinary search returns only the highest-priority active record. Operator catalog
diagnostics may pass `includeShadowed=true` to include the lower-priority records and
`resolutionDiagnostics`; this option and the resolution fingerprint are bound into catalog cursors.
Exact `system.catalog.record` inspection remains exact, so a qualified shadowed ID can still be
inspected deliberately. Installation presets are not a search input: catalog, web, MCP, and
local-AI callers supply only the application, whose active manifest selects the effective
extensions automatically.

Extension package schema version 2 uses the same fields as runtime extension registration:
runtime-safe extension ID, display name, description, classification, sources, contributed
namespaces, dependencies, conflicts, precedence edges, and base precedence. Extension records live
under an owned namespace such as `dnd2024.extension.caldris`. Matching a base record's suffix and
kind creates an override candidate; a unique suffix is additive content.

During installation, an operator may preview and activate an explicit reviewed `sourceIds` base
set together with `extensionIds`. The extension registry, not the base-source list, owns extension
source membership; a source already owned by an extension is rejected if supplied as a base source.
This lets an installation exclude retired source registrations without exposing either selector to
ordinary catalog, web, or AI reads. Omitting `sourceIds` retains the convenient behavior of using
all registered non-extension sources for a newly configured application.

`GET /api/applications/{applicationId}/content` exposes the effective public application content.
It returns the resolution fingerprint, active extension provenance, resolved winners, additive
extension records, source classifications, and generic presentation roles. It accepts only bounded
pagination, never extension or overlay IDs. The D&D website uses this endpoint for its Installed
Content view and labels sources as Core, Homebrew, Compatibility, or Third-party.

Readable game guidance is an explicit catalog presentation contract, not a directory convention.
An entity carrying `game.core.rules.readable` declares its section identity, label and ordering;
readable blocks and examples; related rules and citations; authority links to mechanics or
procedures; audience; and publication status. Published rules must link to at least one
authoritative mechanic or procedure. The component explains a rule but never owns its outcome:
game-specific JavaScript mechanics remain authoritative.

`GET /api/applications/{applicationId}/rules` returns the server-selected Public or DM projection
grouped by those declared sections. It applies the active extension resolution automatically, so
extensions can add sections or rules and can replace the base winner by using the same resolution
key. Results include Core, Homebrew, Compatibility, or Third-party provenance and are bound to both
the application resolution fingerprint and a rules-projection fingerprint. Web callers do not
supply extension, overlay, state-space, or audience IDs. The D&D rules page consumes only this
resolved API; it does not infer sections from catalog paths, require one particular source record,
or use a generated static rules fallback.

The resolution fingerprint also follows the active context into state-space bindings, retrieval
and vector generations, learned recipes, and scheduled local-AI tasks. Scheduled tasks fail closed
when their retained fingerprint no longer matches the active application instead of running against
a changed extension set.

Never reconstruct live changes from memory. Export them before editing the same records in the authored catalog.

## Safe commands

```powershell
.\roleplay.cmd validate catalog
.\roleplay.cmd verify catalog
.\roleplay.cmd help export
.\roleplay.cmd help import
.\roleplay.cmd help migrate-identities
```

- `validate catalog` loads the authored catalog into a disposable database and validates it without touching the live database.
- `verify catalog` compares file and database records and exits unsuccessfully when drift exists.
- Use `roleplay help <command>` for help. Appending `--help` after an export target is not the documented help form.

## Export workflow

Export the live database to a disposable, ignored review directory, not directly over `catalog/`:

```powershell
.\roleplay.cmd export <review-directory> --database <database-path>
```

Then compare records by stable ID and content hash. Merge database-only or database-newer records deliberately while preserving intentional file-only changes. Validate the reviewed catalog before removing the temporary export.

Do not move a version-2 record merely to reorganize it. Change or register its qualified namespace,
then let export produce the canonical path.

Export overwrites matching files in its destination but does not delete extra destination files or modify the source database. That makes a clean review directory important.

## Import workflow

Preview first:

```powershell
.\roleplay.cmd import catalog --database <database-path> --dry-run
```

Import only at an explicit synchronization boundary. Resolve conflicts rather than selecting `--force-files` or `--force-db` casually. Back up or otherwise protect a material live database before a persistent import.

## Catalog content boundary

Procedures describe callable capabilities, component schemas define stored state, fixtures define authored records, and JavaScript mechanics implement game-specific rules. C# hosts and validates those records; it does not duplicate their game semantics.

Component schemas may use the root-only `x-dantes-entity-roles` and
`x-dantes-role-constraints` annotations. Constraints select entities by semantic role or exact
component type, choose either runtime or application-publication scope, set enabled-entity bounds,
declare required selectors, and define uniqueness keys from component JSON pointers. Keep domain
roles and policies in catalog schemas; the C# validator remains generic.
