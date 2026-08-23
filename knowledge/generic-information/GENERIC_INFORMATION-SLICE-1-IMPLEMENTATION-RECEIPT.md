# Generic information Slice 1 receipt

Status: verified with pre-existing unrelated suite failures

## Delivered boundary

- Neutral source, record, and action-contract persistence with one migration.
- Hierarchical information namespaces (`game.worldname.*`) and a fixed local development policy.
- Grounded `information-answer` and explicit `information-actions` discovery.
- Declared `information-action-contract` execution. The initial `kernel.mechanic-action` adapter
  delegates to the existing atomic, catalog-JavaScript action runner; no campaign is required.
- Catalog procedures and three-verb MCP surface support for sources, records, contracts, and execution.

## Evidence

- `dotnet test --filter FullyQualifiedName~InformationTests`: 3 passed, including public
  query/commit verb dispatch for a scoped action contract.
- `dotnet test --filter FullyQualifiedName~GuardTests`: 9 passed.
- `dotnet test --filter FullyQualifiedName~ProtocolWalkTests`: 7 passed.
- `dotnet test --filter FullyQualifiedName~BootstrapContractTests`: 11 passed.
- `dotnet test --filter FullyQualifiedName~CatalogCoverageTests`: 3 passed.
- `roleplay validate catalog`: valid, 426 records and 94 advisory warnings.
- Full suite: 809/811 passed. The remaining failures are pre-existing
  `CatalogFeature20Tests` movement/Speed cases, outside this generic-information boundary.

## Exclusions

- No campaign data migration or automatic campaign/world exposure.
- No external ingestion, identity provider, or arbitrary tool/code executor.
- Action contracts run only a host-registered executor after namespace and JSON Schema validation.
