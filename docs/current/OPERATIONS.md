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
