# Application kernel Slice 11H implementation — activated action-catalog materialization and publication

Status: **accepted 2026-08-24**  
Owner/roadmap: [Platform enabling features roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)  
Dependency tree/leaf: [Application-kernel I / described catalog directory nodes](APPLICATION-KERNEL-DEPENDENCY-PLAN.md)  
Ruleset alignment: **dnd2024-compatible legacy catalog migration through ruleset-neutral adapters**  
Source ID and locator: **not applicable** — no D&D rule, formula, data shape, or outcome is interpreted.  
Outcome: Materialize the activated procedure/mechanic documents of an explicitly published
application into the existing immutable catalog navigator, then prove the ratified `dnd2024`
action catalog is traversable, searchable, and inspectable through existing `system.catalog.*`
queries without vectors or local AI.  
Exclusions: New MCP kinds/request fields; implicit publication; private/record-level authorization;
component/event/subscription/world catalog adapters; aliases; execution; projections; state or
state-space migration; database schema changes; cache/history retention; vectors; and AI
orchestration.  
Allowed files/areas: `src/system/catalog-navigation/{domain,persistence,hosting,tests}/` and its
component manifest; generic composition in `DantesRoleplay.DataAccess`; MCP host configuration and
the existing fresh-host protocol test; this plan/receipt and concise dependency/roadmap status
links. No authored procedure, mechanic, component, fixture, or synchronization record may change.  
Stop point: Stop after a fresh disposable host explicitly publishes exactly the 20 ratified
procedures and 14 ratified mechanics from its exact active winner set, exercises list/browse/search/
inspect over the live three-verb protocol, and proves deny-by-default, drift, and isolation behavior.

## Confirmed decisions

- Slice 0 and Slice 9 already confirm the permanent collection/node/record contracts, missing-
  description status for migrated nodes, deterministic ranking, bounds, and cursor semantics.
- Slice 10A already confirms the four public `system.catalog.*` query kinds and requires an
  authorization-filtered provider whose default reveals nothing.
- Publication is therefore an explicit host allowlist of application IDs. Registration,
  activation, trusted sources, loopback, or source trust alone never publishes content.
- One migrated collection uses the application ID as its collection ID and the immutable
  application display name/description as its authored root metadata. This creates no additional
  permanent ID.
- Procedure and mechanic node paths are `procedures/<authored category path>` and
  `mechanics/<authored category path>`. Category segments are preserved verbatim as legacy titles;
  all non-root migrated nodes use `descriptionStatus: missing`, so the kernel invents no prose.
- Record summaries use application-qualified navigation identities, version 1 migration identity,
  existing authored names/descriptions/status, active source provenance, and exact serialized
  contract content. These are discovery locators, not executable aliases or action authority.
- The user's continuation after being told the next slice would perform actual catalog-node
  materialization and publication confirms this bounded configuration/publication behavior.

## D&D 5e 2024 alignment

| Rule concern | Source meaning | Existing owner | Consequence |
| --- | --- | --- | --- |
| Action contracts | Existing procedure/mechanic metadata and JavaScript remain authoritative. | `catalog/procedures/`, `catalog/mechanics/` | Parse and serialize; do not reinterpret or execute. |
| Directory meaning | Existing authored category paths are navigation metadata. | Procedure/mechanic front matter | Preserve segments and mark migrated node descriptions missing. |
| D&D rules | None selected, calculated, or changed. | Application catalog content | No SRD locator or Foundry comparison applies. |

## External implementation reference

No Foundry dnd5e review applies. This slice is provenance-checked catalog materialization and public
navigation plumbing, not ruleset behavior or a D&D data model.

## Prerequisite evidence

- [Slice 11G receipt](receipts/APPLICATION-KERNEL-SLICE-11G-RECEIPT.md) proves exactly 20 ratified
  procedures and 14 mechanics have complete authored record metadata and lossless categories.
- [Slice 10F receipt](receipts/APPLICATION-KERNEL-SLICE-10F-RECEIPT.md) proves active revisions
  retain exact winner paths, hashes, lengths, media types, source IDs, and activation fingerprints.
- [Slice 10A](APPLICATION-KERNEL-SLICE-10A-IMPLEMENTATION.md) owns the unchanged public query and
  deny-by-default provider boundary.

## Runtime artifacts

- Add an immutable configured public-application policy whose empty default denies every app.
- Add an activated catalog materializer that resolves only retained winner documents through their
  registered allowed roots, rechecks registration fingerprints, canonical containment, byte
  length, and SHA-256, and adapts canonical procedure/mechanic Markdown plus same-source JavaScript
  sidecars through the existing parsers.
- Add a scoped activated public provider and one process-lifetime random cursor-signing key.
- Add an optional host `Catalogs:PublishedApplications` configuration list; it is application data,
  not a system-code branch. Development configuration may explicitly list `dnd2024`.
- Extend the existing disposable `dnd2024` activation proof through live catalog list, paginated
  browse, lexical search, and exact inspection. Add focused generic denial/drift/materialization
  tests as needed.
- Add no database table/migration, catalog file format, public kind, or application-specific C#.

## Authoritative state and closed input

SQLite owns the immutable application registration, source registrations, and current activation
evidence. Configured allowed roots own canonical filesystem resolution. The retained activation
winner set and current file bytes must agree exactly. Existing catalog parsers own authored record
shape. The host allowlist owns publication authorization.

Callers may select only the existing bounded catalog query fields. They cannot supply a path,
source, hash, version, publication decision, manifest fingerprint, node metadata, search rank, or
cursor key.

## Behavior, result, and typed effects

- An unlisted, unknown, inactive, empty, malformed, drifted, out-of-root, or incomplete application
  yields no public navigator.
- Materialization selects only active procedure Markdown and mechanic Markdown/JavaScript pairs.
  Other active document classes remain unexposed until their own adapters are accepted.
- The navigation manifest fingerprint binds the activation fingerprint and materializer version.
  Search/browse ordering and cursors use the accepted Slice 9 implementation.
- `dnd2024` yields one collection, an authored root, migrated missing-description directory nodes,
  and exactly 34 application-qualified records. Inspection returns exact contract JSON and redacted
  active source provenance.
- Queries are read/audit only. Typed effects and state transactions: none.

## Failure, replay, and rollback contract

Publication failure is fail-closed as `PUBLIC_CATALOG_UNAVAILABLE` without source/path details.
Direct materializer tests retain specific internal failure codes for missing activation/metadata,
unavailable root, containment, registration drift, file drift, malformed contract, missing or
cross-source mechanic sidecar, duplicate identity, and bounds. Failed queries write only their
ordinary read audit. Activation replay and state remain unchanged; no rollback transaction exists.

## Implementation sequence

1. Add generic policy/materializer/provider contracts and focused deny/drift/parity tests.
2. Wire scoped provider, process cursor key, and optional host allowlist without changing query
   contracts or the empty default.
3. Extend the fresh `dnd2024` protocol proof through all four existing catalog queries and exact
   34-record traversal/search/inspection evidence.
4. Run focused protocol/materializer/security tests, catalog validation, full shared/local-AI
   suites, warning-free isolated solution build, protocol walk, and `git diff --check`; receipt.

## Acceptance matrix

| Concern | Required evidence |
| --- | --- |
| Publication | Explicitly listed active app is available; unlisted/inactive app is unavailable. |
| Provenance | Registration/root/path/length/hash/sidecar drift fails closed. |
| Parity | Exactly 20 procedures and 14 mechanics materialize once with existing metadata. |
| Nodes | Root metadata is authored; legacy category nodes preserve paths and report missing descriptions. |
| Navigation | Existing list/browse/search/inspect and cursor pagination work over the activated view. |
| Isolation | No system procedure, structural event, component, fixture, or unrelated app appears. |
| Ruleset neutrality | Generic code contains no `dnd2024`, game ID, D&D vocabulary, or rule branch. |
| Repository | Focused/full/local-AI/catalog/build/protocol/diff checks pass. |

## Verification commands

- Focused catalog-navigation materializer and fresh-host MCP protocol tests.
- `roleplay validate catalog`; full shared/local-AI suites; warning-free isolated solution build;
  the live protocol walk; `git diff --check`.

## Completion receipt and exit gate

Record acceptance in `receipts/APPLICATION-KERNEL-SLICE-11H-RECEIPT.md`, mark this document
accepted, update Slice 11 status links, and stop before any other record adapter, projection, state
migration, alias, execution, vector, or AI integration.
