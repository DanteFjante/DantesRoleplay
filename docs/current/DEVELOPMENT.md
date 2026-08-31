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

## Changes needing confirmation

Pause for confirmation before introducing permanent IDs, changing schema meaning, adding a migration, changing a public surface, crossing an ownership boundary semantically, or performing a destructive operation that the user has not already authorized.

At completion, report what changed, relevant check results, and any deliberate exclusions. Update current documentation only if a durable rule or operating procedure changed.
