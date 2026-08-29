# Web interface

The existing ASP.NET host serves trusted HTML pages stored in SQLite. Uploading a page creates and
activates an immutable revision; it does not require a rebuild or restart. The web routes accept
direct requests only from the same computer, with optional private access through Tailscale.

## Upload and open a page

After the `home` page has been uploaded, open `http://localhost:6217/` for the site home page.
It links to the control center; `http://localhost:6217/ui/control-center/index.html` remains the
direct control-center page URL.

With the host running, upload a complete self-contained HTML file:

```powershell
Invoke-RestMethod `
  -Uri 'http://localhost:6217/api/pages/character-sheet' `
  -Method Put `
  -ContentType 'text/html; charset=utf-8' `
  -InFile '.\character-sheet.html'
```

Open `http://localhost:6217/ui/character-sheet`. Uploading the same ID again appends and activates
the next revision.

## Upload a page with assets

Put a root `index.html` and its files in one directory. Relative references should begin with
`assets/`, for example `<link rel="stylesheet" href="assets/site.css">`. Create and upload the
bundle:

```powershell
Compress-Archive -Path '.\character-sheet\*' -DestinationPath '.\character-sheet.zip'

Invoke-RestMethod `
  -Uri 'http://localhost:6217/api/pages/character-sheet/bundle' `
  -Method Put `
  -ContentType 'application/zip' `
  -InFile '.\character-sheet.zip'
```

Open `http://localhost:6217/ui/character-sheet/index.html`. The HTML and every asset activate as
one immutable revision; uploading a later bundle never exposes assets from the older revision.

## Read dynamic data from a page

Read a complete entity with every attached component:

```javascript
const character = await fetch("/api/data/entity/creature.orban").then(response => response.json());
```

Read one component by using its component-definition ID as the data type:

```javascript
const inventory = await fetch("/api/data/inventory/creature.orban").then(response => response.json());
```

The component endpoint returns the stored JSON object directly. The web project has no list of
game-specific types and does not translate unknown fields.

## Refresh when committed data changes

Use the browser's built-in `EventSource` to refetch current data after a committed change:

```javascript
const changes = new EventSource("/api/changes?page=character-sheet");

changes.addEventListener("invalidate", async () => {
  const character = await fetch("/api/data/entity/creature.orban").then(response => response.json());
  renderCharacter(character);
});

changes.addEventListener("page-revision", event => {
  const update = JSON.parse(event.data);
  console.log(`Page ${update.pageId} activated revision ${update.pageRevision}`);
});
```

The first `invalidate` arrives immediately, including after an automatic reconnect. Later
invalidation events are coarse refetch hints: several commits may be combined, and a refetch may
occasionally find unchanged data. The optional `page` query adds page-revision notices.

## Current boundary

HTML, bounded ZIP bundles, dynamic reads, and local SSE invalidation are supported. Pages are
trusted operator-authored code: inline scripts and styles can call the same-origin read/SSE routes.
The server rejects non-loopback web clients, applies restrictive browser headers, limits direct
HTML to 1 MiB, limits uploads to 10 per minute, limits reads to 240 per minute, and permits four
concurrent SSE streams.

## Reserved control API boundary

Future operator controls use only the `/api/control/*` route family. Slice 0 reserves mapping
helpers and five server-selected capabilities: control reads, page writes, setting writes, local-AI
messages, and Codex approvals. It does not map a control endpoint or add a control-center page by
itself.

Control reads use GET. Control changes use POST or PUT with `application/json`; their browser
`Origin` must exactly match the approved loopback Host or the configured Tailscale HTTPS Host.
Capability names in headers, query strings, or request bodies are ignored. Existing page uploads
retain their earlier interface.

## Control-center shell

The control-center shell is an uploadable page bundle. It reads status, committed-effect history,
the host's closed local-completion setting definitions, and the read-only
application/ECS/public-catalog structure endpoints. Its site editor manages existing
database-authored pages. A persistent sidebar selects one main workspace through closed hash routes,
so settings, effects, assistants, applications, and the editor remain available without a page
reload. The assistant panel provides operator-scoped, durable local advisory
conversations when the configured Ollama provider is ready, plus streamed Codex conversations when
the pinned local app-server executable is available. Codex starts with the repository as its
server-selected working directory and a read-only, no-network baseline. A visible, bounded command,
file-change, network, or permission request can be accepted once, declined, or used to cancel the
turn; no session-wide approval or browser-selected sandbox is available. An active turn can also be
cancelled directly. These conversations are not interaction plans or execution authority; the separate
interaction-orchestration plan owns future intent resolution, verified proposals, and execution.

```powershell
Compress-Archive `
  -Path '.\src\system\web-interface\examples\control-center\*' `
  -DestinationPath '.\control-center.zip'

Invoke-RestMethod `
  -Uri 'http://localhost:6217/api/pages/control-center/bundle' `
  -Method Put `
  -ContentType 'application/zip' `
  -InFile '.\control-center.zip'
```

Then open `http://localhost:6217/ui/control-center/index.html`. The upload is an ordinary immutable
page revision and can be replaced through the existing bundle upload path; the host does not seed it
automatically.

The Applications workspace starts with registered applications. Selecting one updates the URL to a
`#/applications/{applicationId}` deep link and opens its structure inside the main workspace while
the control navigation remains visible. It lazily opens state spaces, live entities, component
values, and the exact immutable schema version referenced by each component. Catalog collections,
browse, search, and record detail appear only when the host supplies
an explicitly public catalog navigator. The default host deliberately reports `unavailable`; it
does not infer that catalog files or database records are public.

### Change startup settings

The Server settings panel lists exactly seven local-completion startup settings: enabled state,
loopback Ollama endpoint, model, profile, output-token limit, timeout, and concurrency. Each item
shows whether its applied value came from configuration, a default, or a durable override, plus its
sensitivity, mutability, restart/disruption state, exact JSON Schema, and revision history. Changes,
resets, and rollbacks append audited revisions and responses are never cached.

The current host does not register the local completion provider, so the panel truthfully labels
these as resolved startup values rather than effective model settings. Every change is staged: the
running process remains untouched, the panel shows a pending value, and a normal host restart
validates and applies it. The web UI cannot restart the host. It does not expose arbitrary
configuration, configuration paths, environment-variable names, user secrets, or
database/listen/MCP/Tailscale settings.

### Edit an existing page

Open the Site editor panel and choose an existing page and immutable revision. Editing the HTML and
choosing **Save inactive draft** creates a new revision while leaving the visible page unchanged.
Preview opens that exact revision in a script-capable but opaque sandbox that cannot connect to the
control API or external services. Publishing and rollback are separate confirmed actions guarded by
the page's active/latest revision tokens. An exact revision can also be downloaded as a ZIP.

The editor copies the selected revision's complete asset set; this first release edits HTML only and
does not create new pages or change individual assets. Keep a local copy of `control-center`. If a
published editor revision is broken, restore it through the direct HTML or bundle upload commands
above.

## Optional private access

If Tailscale is installed and signed in, start the host and a temporary private HTTPS route with:

```powershell
& '.\src\system\web-interface\scripts\Start-PrivateWeb.ps1'
```

The command prints the private `https://...ts.net` address. It derives the current Tailscale
hostname and login at runtime, allows only that login, keeps ASP.NET bound to IPv4 loopback, and
removes the Tailscale Serve mapping when the process exits. It refuses to replace an existing Serve
configuration. Pages can call `/api/session` to distinguish `local` from `tailscale` access.

Only `/ui`, `/api/pages`, `/api/data`, `/api/changes`, `/api/session`, and the reserved
`/api/control` family are available through the private hostname. `/mcp` remains separate and is not
exposed by this route. Tailscale Funnel is not used, so this does not publish the server to the
public internet.

There is deliberately no account database, public hosting, or MCP identity change. ChatGPT
continues to use the MCP surface separately. Individual asset mutation, partial bundle updates,
durable change replay, hostile-content sandboxing, shared remote administration, and game-state
write endpoints remain outside this interface.
