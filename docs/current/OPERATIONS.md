# Operations

Use this guide to run the local service, connect an MCP client, and perform a compact verification.

## Run the service

```powershell
dotnet run --project DantesRoleplay.MCPServer
```

The development MCP endpoint is:

```text
http://127.0.0.1:6217/mcp
```

The default development database is `DantesRoleplay.MCPServer/data/dantesroleplay.db`. It is runtime state and is not the authored catalog.

Before a catalog activation, state migration, import, or page release, create a consistent database
backup without combining preservation with a catalog import:

```powershell
.\roleplay.cmd backup
```

The command uses SQLite's online backup API, verifies the copy with `integrity_check`, and never
migrates or writes the source database. Use `--output <path>` when a reviewed release needs a named
recovery point; an existing file is never overwritten.

Run a live release from a frozen source directory instead of the development checkout. The release
directory must contain `catalog/` at its root:

```powershell
.\run-mcp-server.ps1 -SourceRoot D:\releases\dantes-roleplay\<release-id>
```

Keep the preceding release directory and its matching database backup until the new release passes
readiness and browser verification. Registrations retain the stable `repository` allowed-root ID
and relative catalog paths; only the host-owned resolved root changes between releases.

After an application preview is reviewed and before its activation, check that the runtime has the
exact component-type versions referenced by D&D application objects:

```powershell
cd src/system/web-interface/dnd2024
npm run release:reconcile-object-types
```

The check derives requirements from the object contracts and exits non-zero when registration is
required. Run the same command with `-- --apply` only at the reviewed synchronization boundary. It
dry-runs every missing contiguous version through the authenticated local MCP endpoint, verifies the
derived version and schema hash against the object contract, then commits that exact request. It
does not import state or register unrelated component schemas.

After activating a changed application resolution, readiness now fails with
`AUDIENCE_STATE_SPACE_STALE` until the server-selected state space is current. Use the
`system.state-space.upgrade` capability's dry-run and exact commit boundary; it validates every
persisted component against its retained contract before rebinding populated state. Then require
both application readiness and a real audience-bound read model to pass before publishing the page.

## Host-owned configuration

ASP.NET Core maps nested environment configuration with double underscores. Keep canonical paths,
credentials, provider choices and other host policy out of MCP payloads and checked-in settings.

Source registrations use an opaque allowed-root ID plus a relative path or glob. Configure the
canonical host path separately, for example:

```powershell
$env:Sources__AllowedRoots__workspace = 'C:\source\my-application'
```

An authenticated local client may then use `allowedRootId: "workspace"`; preview results expose
only relative logical paths and content evidence, never the canonical host path.

The private operator interface can run the pinned Codex CLI through `codex app-server --stdio`.
Override host-owned bridge settings only when needed:

```powershell
$env:Codex__ExecutablePath = 'C:\Tools\codex.exe'
$env:Codex__RepositoryRoot = 'C:\source\DantesRoleplay'
$env:Codex__PinnedVersion = '<required version>'
$env:Codex__Model = '<supported model>'
```

The bridge fixes turns to the configured repository and does not store Codex credentials. Each turn
uses request-scoped approval with a read-only, no-network baseline; the browser cannot grant a
session-wide approval or choose its sandbox. If the configured executable is inaccessible
(including some packaged desktop-app binaries), install an independently accessible CLI or point
`Codex__ExecutablePath` to one.

The separate no-tools remote interaction-planning adapter is disabled by default and never reuses
the repository-capable Codex bridge. Development verification must enable it explicitly and supply
its credential through host configuration:

```powershell
$env:InteractionPlanning__Remote__Enabled = 'true'
$env:OPENAI_API_KEY = '<credential>'
```

Use `query(kind: "capabilities")` and the catalog procedures for current preview, activation,
state-space and interaction payloads rather than copying protocol shapes into operational docs.

## Publish the server

The server project publishes as a self-contained single-file application. Its current
`RuntimeIdentifiers` list is the authority for supported publish targets; pass one of those runtime
identifiers to `dotnet publish`. Adding a target is a project-file change, not a runtime setting.

## Page identity upgrade

Application navigation is registered ECS publication state. Versioned HTML and assets remain in
the web-content tables and are linked by `system.web.page`; the application landing entity also has
`system.web.index-page`. There is no page-name convention or raw page-ID upload route.

When upgrading a database that predates publication identity:

1. Start the service so the database and web-content migrations complete.
2. Open **Control center → Site editor** and inspect the legacy page review.
3. Classify every unlinked content identity explicitly as an application page or retained
   unclassified content. Keep home and control center system-owned.
4. Apply the reviewed migration and require `contentVerified: true` in its report.
5. Confirm navigation through `/api/web/applications`; do not infer routes from content IDs.

The reviewed migration is transactional for ECS identities and compares every page revision and
asset fingerprint before and after the change. Unclassified content is retained, never deleted.
Page administration can then create or edit metadata, transfer the index marker, append and
activate revisions, change visibility or order, disable and re-enable identities, and permanently
remove only a disabled unreferenced identity. Permanent identity removal still preserves its
versioned content history.

## Protocol model

The MCP surface is intentionally small:

- `orient` discovers available capabilities and the context needed to use them.
- `query` retrieves authorized information without committing game-state changes.
- `commit` invokes declared operations and applies validated effects transactionally.

Binary images use the same three-tool surface. `commit(kind: "system.blob-upload.begin")` returns a
15-minute upload path and one-use secret for a declared SHA-256, MIME type, and byte length. PUT the
raw bytes with the returned `X-DantesRoleplay-Upload-Token` header, then call
`commit(kind: "system.blob-upload.finalize")`. Read finalized metadata with
`query(kind: "system.blobs", id: "<sha256>")`; its result supplies both the MCP resource URI and
private HTTP download path. The default byte store is the `blobs` directory beside the kernel
database and may be overridden with `BlobStorage:Root`.

Clients should discover capabilities rather than hard-code game-specific procedure IDs or state shapes.

## Verification

Before diagnosing client configuration, verify the repository and server separately:

```powershell
dotnet build DantesRoleplay.slnx
.\roleplay.cmd validate catalog
dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj
```

Then start the server and confirm the MCP endpoint is reachable. When the MCP surface or dependency registration changes, exercise an orient/query/commit walk against a disposable campaign or test database.

The repository's `connect-claude-desktop.ps1` helper may be used for Claude Desktop configuration. Other clients should be pointed at the endpoint above using their supported HTTP MCP configuration.
