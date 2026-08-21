# Connecting a client to this server

Verified 2026-08-18 against Anthropic's current client behaviour. The server is
`DantesRoleplay.MCPServer`, it speaks **streamable HTTP**, and it lives on
**`http://127.0.0.1:6217/mcp`** (`Properties/launchSettings.json`; the path comes from
`ServerConfiguration.McpEndpoint`).

Start it first, always:

```
dotnet run --project DantesRoleplay.MCPServer
```

First run creates the SQLite file and seeds the manual and the two starter rules. Watch the console
for migration or seeding errors — a failed seed means an empty manual, and any client will then
correctly report the system as broken.

---

## Which client, and what each needs

| Client | Reaches this server? | How |
| --- | --- | --- |
| **Claude Code CLI** | yes, natively | one command, no bridge |
| **Claude Desktop** | yes, via a local bridge | `connect-claude-desktop.ps1` |
| **Codex CLI** | yes, natively | `config.toml`, see `COLDWALK.md` |
| Claude Desktop → Settings → Connectors | **no** | that URL is dialled from Anthropic's cloud, which cannot see your loopback |
| A Cowork session | **no** | sandboxed, in the cloud or in a network-isolated local VM. Neither can reach `localhost` |

That last row is worth stating plainly because it is the thing people lose an hour to: the assistant
helping you build this cannot itself call it.

---

## Claude Code CLI — one line

It speaks streamable HTTP directly, so there is no bridge and no Node package:

```bash
claude mcp add --transport http dantesroleplay http://127.0.0.1:6217/mcp
```

Then `claude`, and the three tools are there. `claude mcp list` shows the connection state.

---

## Claude Desktop — run the script

```powershell
powershell -ExecutionPolicy Bypass -File .\connect-claude-desktop.ps1
```

Then **fully quit** Claude Desktop and reopen it. Closing the window is not quitting: right-click
the system-tray icon and choose Quit, or the config is not reloaded.

### Why a bridge at all

Claude Desktop supports exactly two transports, and neither takes a local URL directly:

| Path | Reaches `127.0.0.1`? | Why |
| --- | --- | --- |
| `claude_desktop_config.json` | yes, through a bridge | The schema validates **stdio only** — it requires a `command`. Adding `url`, `type` or `transport` fails validation, and newer builds rewrite the file on the next save and drop the whole `mcpServers` block. |
| Settings → Connectors → *Add custom connector* | **no** | Streamable HTTP, but the URL is resolved from Anthropic's infrastructure rather than from your machine. |

So the supported arrangement is `mcp-remote`: a small Node process Claude Desktop launches as a
stdio child, which forwards to the local server. Nothing leaves the machine.

```
Claude Desktop  --stdio-->  npx mcp-remote  --streamable HTTP-->  DantesRoleplay.MCPServer :6217
```

Keeping it on loopback is the point, not an inconvenience: this prototype has no authentication and
`commit` writes.

### The entry the script writes

```json
{
  "mcpServers": {
    "dantesroleplay": {
      "command": "npx.cmd",
      "args": ["-y", "mcp-remote", "http://127.0.0.1:6217/mcp",
               "--transport", "http-only", "--allow-http"]
    }
  }
}
```

Four details are load-bearing, each a real failure mode:

- **`npx.cmd`, not `npx`.** Claude Desktop spawns the process directly rather than through a shell,
  so the bare name does not resolve to the `.cmd` shim. This is the most common cause of a Windows
  server that silently never starts.
- **`127.0.0.1`, not `localhost`.** Node resolves `localhost` to `::1` first on some Windows
  configurations, and a server listening only on IPv4 is then unreachable through the bridge while
  working perfectly from a browser.
- **`--transport http-only`.** This server registers streamable HTTP and nothing else. Without the
  flag, `mcp-remote` tries deprecated SSE first and wastes a failed round trip.
- **`--allow-http`.** Redundant for loopback, which is already exempt, but keeps the entry working
  if the URL is ever pointed at a LAN address.

### "I don't have that folder" — the Microsoft Store build

This machine runs the **MSIX (Microsoft Store) build** of Claude Desktop, which changes where the
config lives:

| Build | Config directory |
| --- | --- |
| Standard installer | `%APPDATA%\Claude\` |
| **Microsoft Store (MSIX)** | `%LOCALAPPDATA%\Packages\Claude_<hash>\LocalCache\Roaming\Claude\` |

MSIX filesystem virtualization redirects Electron's `app.getPath("userData")` into the package
container, so the app reads the `LocalCache` path while the documented one stays empty.

**The app's own Settings → Developer → Edit Config does not help here.** On MSIX it calls
`shell.openPath()`, which is *not* virtualized, so it opens the non-existent `%APPDATA%` copy — you
edit a file the app never loads, with no error either way. Anthropic tracks this as
[#25579](https://github.com/anthropics/claude-code/issues/25579) and
[#26073](https://github.com/anthropics/claude-code/issues/26073).

The script detects the build by globbing `%LOCALAPPDATA%\Packages\Claude_*` rather than hardcoding
the hash, prefers a package that already holds a real config, and falls back to `%APPDATA%\Claude`.
On the Store build that file already exists and holds app preferences, which is why the script
merges and backs up rather than overwriting.

### Switches

```powershell
.\connect-claude-desktop.ps1 -Diagnose     # report paths and state, change nothing
.\connect-claude-desktop.ps1 -WhatIfOnly   # print the merged config, write nothing
.\connect-claude-desktop.ps1 -Remove       # unregister again
.\connect-claude-desktop.ps1 -ConfigDir 'C:\some\other\Claude'
.\connect-claude-desktop.ps1 -McpUrl http://127.0.0.1:5000/mcp
```

---

## What to say once it is connected

**As little as possible.** That is not a shortcut — it is the acceptance criterion. The whole design
premise is that a session with no context can call one tool and work out the rest, so a long system
prompt does not help the model, it hides whether the surface works:

> Use the `dantesroleplay` connection. Call `orient` first and follow what it tells you.

If that is not enough, the gap is a defect in `orient`, in `query(kind: "capabilities")`, or in
`procedure.system.use` — write down the sentence you wanted to add, because that sentence is the
finding. This is the same discipline as `COLDWALK.md`, and it applies to ordinary use too.

For play specifically, the guidance lives inside the system rather than in your prompt: the GM
reads `procedure.play.storytelling` through `query`. (That contract is drafted at `storytelling.md`
and lands in the internal proof ledger in `STORY_FIRST_ROADMAP.md`.)

**For a cold walk, the client session must have no access to this repository.** A Claude Desktop
chat with no project attached qualifies; a Claude Code session started inside the repo does not,
because it can read `ARCHITECTURE.md` and the run then proves nothing.

---

## Checking the server without any client

`DantesRoleplay.MCPServer.http` sits next to the project and walks the whole surface by hand —
`tools/list`, `orient`, the catalog, a component, a dry run, a commit, an action, the history. Send
requests from the gutter in Visual Studio. It is the fastest way to tell "the server is broken" from
"the client is not connected".

---

## When it does not appear

1. **Is the server running?** `.\connect-claude-desktop.ps1 -Diagnose` probes the URL and reports.
   A bare GET on `/mcp` returning 4xx is correct — it proves something is listening.
2. **Did you fully quit Claude Desktop?** Tray icon → Quit, not just the window.
3. **Logs.** `%APPDATA%\Claude\logs\mcp.log` and `mcp-server-dantesroleplay.log`. On the Store build
   the logs are under the package path, alongside the config.
4. **First run is slow.** `npx` downloads `mcp-remote` on first use; allow ~20 seconds.
5. **Are you in a Cowork task rather than a normal chat?** See the table at the top.
