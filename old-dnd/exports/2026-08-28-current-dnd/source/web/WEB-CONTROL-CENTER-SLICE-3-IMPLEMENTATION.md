# Web Interface Feature 2 Slice 3 implementation — ECS and contract explorer

Status: **accepted**  
Owner/roadmap: [Web interface roadmap](WEB-INTERFACE-ROADMAP.md)  
Dependency tree/leaf: [Control-center dependency plan](WEB-CONTROL-CENTER-DEPENDENCY-PLAN.md), ECS and contract explorer  
Ruleset alignment: **ruleset-neutral**  
Source ID and locator: **not applicable**  
Outcome: Let an authorized operator browse registered applications, their state spaces, live entities, component instances, exact component schemas, and explicitly public catalog contracts.  
Exclusions: ECS/component writes, catalog activation/import, filesystem/database browsing, private or unclassified catalog content, caller-selected authoritative hashes, D&D interpretation, migrations, MCP changes, and all other control-center panels.  
Allowed files/areas: application-registry and ECS read contracts/persistence/tests; catalog-provider use without semantic changes; web explorer projection/routes/tests/source bundle; Feature 2 documents.  
Stop point: `<ecs-explorer>` provides the confirmed read-only hierarchy and the remaining future panels stay unchanged.

## Confirmed decisions

- The user's **continue** after Slice 2 on 2026-08-24 authorizes this Sol gate and its named read-only public contract additions.
- Owner pages use ordinal ID order and accept `limit` 1–100, default 25. Owner contracts receive a validated plain `after` key; the web wire uses an opaque base64url token containing `kind`, `scope`, `pageSize`, and `lastKey`.
- A cursor is invalid when malformed/over 1024 characters and stale when kind, scope, page size, or existing scoped last row differs. The response tells the browser to restart the affected list.
- Application list order is application ID. State spaces are application-scoped and ordered by state-space ID. Component types are application-scoped, ordered by qualified ID, and list only the latest immutable version. Entities are live-only and ordered by entity ID. Components are live-entity-scoped and ordered by qualified type ID.
- Exact schema inspection names qualified type ID and immutable version. Component instances carry that pair and schema hash, so the UI follows recorded references rather than supplying an authoritative hash.
- The existing `IPublicApplicationCatalogProvider` is the sole catalog publication boundary. `TryGet == false` means `unavailable`; a navigator returning zero collections means `empty`; otherwise the existing signed browse/search cursor and exact-record semantics pass through a bounded web projection.
- The production host's current empty provider remains deliberately unavailable. This slice does not infer publicness from catalog files, SQLite, application preview, or source registration and does not activate a candidate manifest.

## Prerequisite evidence

- [Slice 0 receipt](WEB-CONTROL-CENTER-SLICE-0-RECEIPT.md) verifies the `control.read` boundary.
- [Slice 1 receipt](WEB-CONTROL-CENTER-SLICE-1-RECEIPT.md) verifies the browser-native panel shell.
- `IApplicationRegistry` owns immutable application identity/description/revision and already has a capped list.
- `IStateSpaceRegistry`, `IEntityComponentStore`, and `IApplicationComponentTypeRegistry` own exact application-scoped ECS reads and immutable schemas.
- `IPublicApplicationCatalogProvider` and `ICatalogNavigator` already own the public-only boundary, signed catalog cursors, breadcrumbs, search, and exact record inspection.

## Runtime artifacts

- New owner page records and reads: `ApplicationDiscoveryPage` / `IApplicationRegistry.ListPage`; `StateSpaceDiscoveryPage` / `IStateSpaceRegistry.ListPage`; `ComponentTypeDiscoveryPage` / `IApplicationComponentTypeRegistry.ListLatestPage`; `EcsEntityDiscoveryPage` and `EcsComponentDiscoveryPage` / matching `IEntityComponentStore` async reads.
- Web-only `ControlStructureExplorer` maps owner records to bounded JSON and owns only wire cursor encoding/validation.
- Control reads under `/api/control/structure/*` for applications, application detail/state spaces/component types/catalog, state-space entities, entity detail/components, and exact component-type schema.
- Existing catalog navigator requests keep their signed cursor unchanged; no second catalog cursor or manifest is created.

## Authoritative state and closed input

The registries/stores derive application revision, manifest fingerprint, component version/schema hash, entity/component revision, timestamps, and catalog publication. Browser input is limited to bounded IDs, page size, opaque cursor, catalog collection/branch/query/filter values already validated by `ICatalogNavigator`, and historical schema version. The browser cannot provide application ownership, state-space binding, schema hash, component value, source path, authorization scope, or catalog manifest.

## Behavior, result, and typed effects

The explorer starts with applications, then lazily loads the selected application's state spaces and latest component types. Selecting a state space loads live entities; selecting an entity loads its current components; selecting a component loads the exact schema version referenced by it. Catalog collections/browse/search/record calls occur only through an available public navigator and retain its breadcrumbs and cursors. Every request is read-only and uses `Cache-Control: no-store`; no transaction or typed effect is created.

## Failure, replay, and rollback contract

- Invalid IDs/limits/cursors/filters return stable 400 results; stale cursors return 409 with restart guidance.
- Unknown application/state space/entity/component type/catalog node or record returns 404 without disclosing adjacent records.
- A registered application with no public navigator returns a successful catalog status of `unavailable`; an available navigator with no collections returns `empty`.
- Oversized schema/value/catalog content remains bounded by the existing schema/catalog owners and is returned only by exact detail reads.
- Repeating reads is idempotent. All failure paths leave applications, state spaces, ECS data, schemas, catalog state, files, settings, pages, events, and operations unchanged.

## Implementation sequence

1. Add and test bounded owner discovery reads without changing existing exact/write semantics.
2. Add the web projection/cursor, route family, stable errors, and public-catalog adapter use.
3. Update only `<ecs-explorer>` with lazy hierarchy/detail loading and isolated empty/unavailable/error states.
4. Run focused owner/web tests, solution build, full suite, browser walk, and `git diff --check`; write receipt and stop.

## Acceptance matrix

| Case | Evidence |
| --- | --- |
| Empty/multiple applications and state spaces | owner and web projection tests |
| Application-scoped latest types and exact historical schema | registry tests |
| Live entity/component scope isolation and paging | ECS persistence tests |
| Invalid/stale cursors, IDs, limits, unknown records | stable 400/404/409 tests |
| Catalog unavailable/empty/available, browse/search/detail | fake public-provider web tests plus existing catalog tests |
| Wrong identity and GET-only routes | existing control guard plus route metadata tests |
| No writes | before/after row counts and repeated-read tests |
| Browser isolation | disposable local browser walk |

## Verification commands

- `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore --filter "FullyQualifiedName~ApplicationRegistryPersistenceTests|FullyQualifiedName~ApplicationScopedEcsTests|FullyQualifiedName~ComponentTypeRegistryTests|FullyQualifiedName~CatalogNavigationTests|FullyQualifiedName~WebInterfaceTests"`
- `dotnet build DantesRoleplay.slnx --no-restore`
- `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore`
- `git diff --check`

## Completion receipt and exit gate

Record evidence in `web/WEB-CONTROL-CENTER-SLICE-3-RECEIPT.md`, update Feature 2 status once, and stop before site editing, settings, conversations, local-model, or Codex work.
