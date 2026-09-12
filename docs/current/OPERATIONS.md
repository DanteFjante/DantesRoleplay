# Operations

Use this guide to run the local service, connect an MCP client, and perform a compact verification.

## Run the service

For the saved live release, use the ordinary launcher. The development launch profile below
does not select a frozen release and is not a production restart workflow.

```powershell
.\run-mcp-server.ps1
.\run-mcp-server.ps1 -Restart
```

For deliberate source development only: `dotnet run --project DantesRoleplay.MCPServer`.

The development MCP endpoint is:

```text
http://127.0.0.1:6217/mcp
```

The default development database is `DantesRoleplay.MCPServer/data/dantesroleplay.db`. It is runtime state and is not the authored catalog.

The HTTP launch profile, direct executable configuration, and `run-mcp-server.ps1` listen on
all IPv4 interfaces at port 6217. Router forwarding maps external TCP 80 to this device's TCP
6217; internet visitors then open `http://<public-ip>/` without a port suffix. Allow inbound TCP
6217 in Windows Firewall by running `enable-public-web-firewall.ps1` from an administrator
PowerShell window. This rule also applies when the Ethernet connection uses the Public profile.

Anonymous public website access is explicitly enabled in this checkout through
`WebInterface:RemoteAccess:AllowAnonymousPublicAccess`. Direct network visitors receive only
Player-safe website routes and the current party's authorized Player projection; they cannot open
DM, raw entity/component/catalog/blob, control-center, write, or private event routes. Their audit
identity is `anonymous-public-web`, distinct from the local operator. Public Player knowledge is the
union of admitted knowledge for active party members plus eligible public facts. Missing admissions
remain empty or unavailable and never fall back to DM truth.
Public pages cannot call entity-addressed media discovery or content routes. Images are available only
when an authorized read model issues an opaque link that is revalidated when the bytes are opened.

The local owner on a direct localhost/loopback host and an allow-listed Tailscale owner receive the
configured operator seat. When that seat is GameMaster, the website reports
`X-Website-Access: shared` and offers server-authorized DM and Player presentations. A public or
unprivileged bootstrap reports `X-Website-Access: public`, returns the non-actor `player-group` role, and offers Player only. An arbitrary public
Host that reaches the process through a loopback reverse proxy is still public; Host, Origin, query,
body, forwarded headers, saved preferences, and the selector cannot create owner authority.
`Knowledge:LocalPlayer:ApplicationId` and `CampaignId` seed the initial workspace. Owner browser
mutations retain same-origin checks against the external request host and port; public mutations are
rejected before their handlers run.
Set the option to `false` and
restart to reject anonymous network visitors; the option defaults to false when omitted.
Private MCP operations retain their existing loopback-only authorization.

Startup distinguishes service unavailability, connection failure, and actual admission denial.
“Retry application” reloads bootstrap and its resource owners; a failed deployment does not claim
that private views are locked or that a Rules load succeeded. Plain HTTP browsers use the shared
SHA-256/request-ID helper, retaining exact fingerprints and cryptographically random IDs without
requiring secure-context-only browser APIs.

Before a catalog activation, state migration, import, or page release, create a consistent database
backup without combining preservation with a catalog import:

```powershell
.\roleplay.cmd backup
```

The command uses SQLite's online backup API, verifies the copy with `integrity_check`, and never
migrates or writes the source database. Use `--output <path>` when a reviewed release needs a named
recovery point; an existing file is never overwritten.

The ordinary launcher reads `DantesRoleplay.MCPServer/data/runtime-launch.json`. That local
administration profile pins the host directory and every host file, the source directory and every
source file, the live database/blob paths, one listener, and readiness/audience evidence for each
exact probe origin. The source directory must contain `catalog/`. Normal startup never guesses a
release from the editable checkout or the newest folder. It verifies byte lengths and SHA-256
hashes, including line endings, before starting or replacing a process.

Before cleanup, resolve the saved launch profile and the executable path of every running server.
Their host/source directories are required runtime files, even when ignored by Git or located under
`data/releases/`, `bin/`, or a worktree. Do not delete them as backups or build caches. After cleanup,
run the launcher's `-Check` and verify application readiness through the configured website origins.

```powershell
.\run-mcp-server.ps1 -Check
# Explicit development/recovery selection, not an argument required for normal use:
.\run-mcp-server.ps1 -Profile D:\releases\dantes-roleplay\reviewed-runtime-launch.json
```

`-Restart` stops only the process whose executable, PID and start time match this launcher's saved
receipt. Untracked listeners, split IPv4/IPv6 listeners, and reused PIDs fail with a specific
diagnostic instead of stopping every server or accepting another process's readiness. New starts
retain a timestamped startup log next to the profile; the launcher reports actual bound addresses
and checks both the direct local listener and every configured public origin without redirects.
It pins both `URLS` and `ASPNETCORE_URLS` so application settings cannot redirect a rehearsal onto
the live port. The caller's environment is restored after launch.

At an explicit release-selection boundary, dot-source
`src/system/web-interface/scripts/RuntimeLaunch.ps1` and call `Save-RuntimeLaunchProfile` with
`-Path`, `-HostRoot`, `-SourceRoot`, `-Database`, `-BlobRoot`, `-ApplicationId`, `-ListenUrl`,
`-Targets`, and `-Environment`. Each target is `{ origin, expected: { checks, audience } }`:
`checks` pins each readiness owner's code/revision/fingerprint; `audience` pins the bound context
returned by that exact origin. Review these against an isolated restored copy and the actual
activation/page before selection. Do not self-certify a failed response or reuse localhost's
policy fingerprint for a public request. Explicit `-Replace` retains the previous profile;
ordinary startup never rewrites its selection. Copy only deployable host files (including shared
BrowserComponents), not an output directory's incidental `data/` tree. No catalog import, state
migration, page publication, or database restoration is performed by the launcher.

Keep the preceding release directory and its matching database backup until the new release passes
readiness and browser verification. Registrations retain the stable `repository` allowed-root ID
and relative catalog paths; only the host-owned resolved root changes between releases.
Backups include the matching content-addressed blobs: verify their recorded lengths and SHA-256
values on readback. Rehearse against a separate database copy, never the retained recovery point.
Reverting the host/source selection does not rewind gameplay. Restore a prior database only after
reconciling later writes, and restore its matching blobs with it.

Public plain-HTTP release verification is opt-in per signed target: set `expectedRuntime.origin`
to the exact public origin in the signed version-2 release manifest. `release:verify-live` rejects
another origin, port, redirects, wrong audience/runtime evidence, or changed assets; it never
substitutes localhost. Public browser evidence must name that same origin. Omitting the origin
retains the previous HTTPS/loopback-only verifier policy. Startup health and asset identity are
not complete feature/browser acceptance; retain the separate release checks.

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
