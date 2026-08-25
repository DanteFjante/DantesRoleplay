# Web Interface Feature 1 dependency tree — trusted dynamic HTML pages

Status: **Slices 1–5 verified; selected outcome complete**
Ruleset alignment: **ruleset-neutral**
Source: **not applicable**

## Outcome and non-goals

A local browser can upload and retrieve a versioned HTML page, request an entity or one of its
dynamically typed JSON components by ID, upload a bounded ZIP whose HTML and static assets activate
as one immutable page revision, receive coarse live invalidation, use the complete surface locally,
and optionally reach only that web surface through private Tailscale identity. Game-state writes,
public hosting, MCP exposure changes, and local-AI provider replacement are non-goals.

## Existing owners and evidence

| Concern | Owner | State | Evidence |
| --- | --- | --- | --- |
| Dynamic entities/components | `state` system component and `IWorldStore` | verified | `src/system/state/component.json`, `IWorldStore`, and `WorldStoreTests` |
| SQLite process ownership | Existing ASP.NET host and data-access registration | verified | `DantesRoleplay.MCPServer/Program.cs` and `DataAccessServiceCollectionExtensions` |
| Application composition | `dantes-roleplay-host` | verified | `src/applications/dantes-roleplay-host/component.json` |
| Web subsystem direction | Web interface roadmap | verified | Confirmed user requirements recorded in `WEB-INTERFACE-ROADMAP.md` |
| Page revision persistence | `DantesRoleplay.Web` project | verified | Slice 1 receipt and focused tests |
| HTTP page/data surface | `DantesRoleplay.Web` project | verified | Slice 1 receipt and focused tests |

## Dependency tree

```text
Trusted dynamic HTML page                                      [verified]
├─ Existing entity/component reads through IWorldStore         [verified]
├─ Existing application host composition                       [verified]
├─ Versioned HTML page persistence                             [verified]
└─ Closed HTTP mapping for page and dynamic data reads          [verified]
Versioned page bundle                                           [verified]
├─ Existing page revision transaction                           [verified]
├─ Bounded ZIP validation and materialization                   [verified]
├─ Revision-owned asset persistence                             [verified]
└─ Active-revision asset HTTP reads                             [verified]
Live browser invalidation                                       [verified]
├─ Existing shared SQLite commit boundary                       [verified]
├─ Transient commit-token observation                           [verified]
├─ Optional active page-revision observation                    [verified]
└─ Bounded SSE response lifecycle                               [verified]
Local single-user hardening                                     [verified]
├─ Existing closed web route owner                              [verified]
├─ Fail-closed loopback access boundary                         [verified]
├─ Trusted-content browser policy                               [verified]
├─ Direct HTML input ceiling                                    [verified]
└─ Read/upload/stream quotas                                    [verified]
Private remote access                                           [verified]
├─ Existing loopback-only ASP.NET host                         [verified]
├─ Tailnet-private HTTPS and proxy identity                    [verified]
├─ Exact host and login allowlist                              [verified]
├─ Remote web-only route boundary                              [verified]
└─ Foreground lifecycle without stored identity                [verified]
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
| 4 | Versioned ZIP page bundle | Existing page revision owner | ZIP boundary, atomic activation, active-only asset, migration, and compatibility tests pass. |
| 5 | Live SSE invalidation | Shared SQLite commit boundary | Initial/refetch, commit, page revision, rollback, cancellation, and compatibility tests pass. |
| 6 | Local single-user hardening | Closed web route owner | Access, CSP, upload ceiling, quota, no-write, and compatibility tests pass. |
| 7 | Private remote access | Leaf 6 and selected Tailscale provider | Identity, exact-host, web-only isolation, lifecycle, and compatibility tests pass. |

## Lowest ready leaf

[Leaf 6](WEB-INTERFACE-SLICE-4-IMPLEMENTATION.md) is delivered and verified by
[its receipt](WEB-INTERFACE-SLICE-4-RECEIPT.md). Leaf 7 is delivered and verified by the
[Slice 5 receipt](WEB-INTERFACE-SLICE-5-RECEIPT.md), with Tailscale Serve as the selected provider.

## Confirmation gates

The user confirmed the new project, executable database-authored HTML, versioned page storage,
dynamic type-plus-ID reads, and deferred security on 2026-08-23. This confirms the project name,
tables, route family, and public surface named by Slice 1.

The user's 2026-08-24 request to implement the web dependency plan confirms Slice 2's web-owned
asset migration and bundle/asset routes as closed by its active implementation document.

The user's 2026-08-24 instruction to continue after Slice 2 acceptance confirms Slice 3's
ruleset-neutral SSE route and transient SQLite commit observation as closed by its active
implementation document.

On 2026-08-24 the user selected the simplest local single-user completion boundary, with ChatGPT
using MCP separately. This confirms Slice 4's loopback access policy, trusted executable HTML,
browser hardening, and fixed quotas; remote identity/deployment is deferred.

On 2026-08-24 the user then explicitly requested Slice 5. This confirms private remote web access
using the simplest selected provider boundary, while keeping the host local and MCP separate.

## Planning receipt

- Dependency-plan authoring created no runtime artifact.
- Leaf 4 runtime artifacts and verification are recorded in the
  [Slice 2 receipt](WEB-INTERFACE-SLICE-2-RECEIPT.md).
- Leaf 5 runtime artifacts and verification are recorded in the
  [Slice 3 receipt](WEB-INTERFACE-SLICE-3-RECEIPT.md).
- Leaf 6 runtime artifacts and verification are recorded in the
  [Slice 4 receipt](WEB-INTERFACE-SLICE-4-RECEIPT.md).
- Leaf 7 runtime artifacts and verification are recorded in the
  [Slice 5 receipt](WEB-INTERFACE-SLICE-5-RECEIPT.md).
