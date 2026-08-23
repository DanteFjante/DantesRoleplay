# Web Interface Feature 1 dependency tree — trusted dynamic HTML pages

Status: **Slice 1 verified**
Ruleset alignment: **ruleset-neutral**
Source: **not applicable**

## Outcome and non-goals

A local browser can upload and retrieve a versioned HTML page and can request an entity or one of
its dynamically typed JSON components by ID. SSE, non-HTML assets, game-state writes, security
hardening, and remote hosting are non-goals.

## Existing owners and evidence

| Concern | Owner | State | Evidence |
| --- | --- | --- | --- |
| Dynamic entities/components | `state` system component and `IWorldStore` | verified | `src/system/state/component.json`, `IWorldStore`, and `WorldStoreTests` |
| SQLite process ownership | Existing ASP.NET host and data-access registration | verified | `DantesRoleplay.MCPServer/Program.cs` and `DataAccessServiceCollectionExtensions` |
| Application composition | `dantes-roleplay-host` | verified | `src/applications/dantes-roleplay-host/component.json` |
| Web subsystem direction | Web interface roadmap | verified | Confirmed user requirements recorded in `WEB-INTERFACE-ROADMAP.md` |
| Page revision persistence | New `DantesRoleplay.Web` project | ready | Closed two-table model and focused tests specified by Slice 1 |
| HTTP page/data surface | New `DantesRoleplay.Web` project | ready | Closed routes and failure behavior specified by Slice 1 |

## Dependency tree

```text
Trusted dynamic HTML page                                      [verified]
├─ Existing entity/component reads through IWorldStore         [verified]
├─ Existing application host composition                       [verified]
├─ Versioned HTML page persistence                             [verified]
└─ Closed HTTP mapping for page and dynamic data reads          [verified]
```

## Conflicts and decisions

- The superseded plan prohibited database-authored executable HTML. The user explicitly replaced
  that direction and accepted trusted uploaded HTML with security deferred.
- `type` does not name a database table. It is either reserved `entity` or a component-definition
  ID, preserving the generic state owner and avoiding a raw database API.
- Page writes use the web project's own transaction and tables. They do not join a game-state root
  transaction because they do not change game state.

## Ordered leaves

| Order | Leaf | Depends on | Exit gate |
| --- | --- | --- | --- |
| 1 | Page persistence and read service | SQLite hosting | Revision and rollback-safe active pointer tests pass. |
| 2 | Dynamic state reader | `IWorldStore` | Entity, component, missing-type, and missing-entity tests pass. |
| 3 | Host endpoints | Leaves 1–2 | Focused tests, build, and protocol compatibility pass. |

## Lowest ready leaf

Delivered and verified by [the Slice 1 receipt](WEB-INTERFACE-SLICE-1-RECEIPT.md).

## Confirmation gates

The user confirmed the new project, executable database-authored HTML, versioned page storage,
dynamic type-plus-ID reads, and deferred security on 2026-08-23. This confirms the project name,
tables, route family, and public surface named by Slice 1.

## Planning receipt

- Runtime artifacts created: none.
