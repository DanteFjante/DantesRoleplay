# MCP Server

This README was created using the C# MCP server project template.
It demonstrates how you can easily create an MCP server using C# and run it as an ASP.NET Core web application.

The MCP server is built as a self-contained application and does not require the .NET runtime to be installed on the target machine.
However, since it is self-contained, it must be built for each target platform separately.
By default, the template is configured to build for:
* `win-x64`
* `win-arm64`
* `osx-arm64`
* `linux-x64`
* `linux-arm64`
* `linux-musl-x64`

If you require more platforms to be supported, update the list of runtime identifiers in the project's `<RuntimeIdentifiers />` element.

## Developing locally

To test this MCP server from source code (locally), you can configure your IDE to connect to the server using localhost.

```json
{
  "servers": {
    "DantesRoleplay.MCPServer": {
      "type": "http",
      "url": "http://localhost:6217/mcp"
    }
  }
}
```

Refer to the VS Code or Visual Studio documentation for more information on configuring and using MCP servers:

- [Use MCP servers in VS Code](https://code.visualstudio.com/docs/copilot/chat/mcp-servers)
- [Use MCP servers in Visual Studio](https://learn.microsoft.com/visualstudio/ide/mcp-servers)

## Testing the MCP Server

Once configured, call `orient()` and then `query(kind: "capabilities")`. The server intentionally
publishes only the three tools `orient`, `query`, and `commit`; the capability result lists every
supported kind and its closed payload or parameter shape.

## Configuring application source roots

Application source registrations contain an opaque allowed-root ID and a relative path or glob.
The corresponding canonical host path must be configured by the operator and is never accepted in
an MCP call. ASP.NET Core configuration maps environment variables with double underscores, for
example:

```powershell
$env:Sources__AllowedRoots__workspace = 'C:\source\my-application'
```

An authenticated local client can then register a source with `allowedRootId: "workspace"` and a
relative specification such as `catalog/**/*.json`. After registration,
`query(kind: "system.application-preview", applicationId: "...")` performs a bounded disposable
scan and returns only relative logical paths, hashes, winners, shadows, and closed problems. It does
not persist or activate the candidate.

`query(kind: "system.dependencies", applicationId: "...")` returns a deterministic inventory of
the application's declared component-field and projection dependencies. Use a returned canonical
node ID with `id` and `transitive` to review downstream impact. The response explicitly lists
consumer kinds that are not yet indexed; it does not inspect JavaScript or require an AI model.

After an exact dry run, `commit(kind: "system.application.activate", ...)` can atomically retain
and select that preview's redacted source-overlay evidence. The activation appears under the
application's `Active` field in `system.applications`. This does not import or execute the files;
dependency coverage remains explicit in the activation summary.

## Connecting a conversational game model

The chat model should treat player text as intent, not as a rule result. For a D&D 2024 application,
load `dnd2024.procedure.play.mini-game`, then resolve the message through the existing interaction
planner. The planner returns either a clarification/unsupported response or an inert proposal:

```text
query(kind: "system.interaction-plan", applicationId: "dnd2024",
  request: "{\"operation\":\"resolve\",\"stateSpaceId\":\"party\",\"sessionContextId\":\"chat-1\",\"intent\":{\"idempotencyKey\":\"turn-1\",\"intentText\":\"I try to open the iron gate\",\"maximumPlanSteps\":1}}")
```

Only after the player confirms the proposal should the model submit the exact returned proposal via
`commit(kind: "system.interaction-execute", ...)`. The generic action runner remains the rules
authority; the model supplies intent, role references, and player choices, never derived modifiers,
DCs, effects, or fabricated outcomes. The DM then narrates the verified result returned by execution.

## Configuring approval-gated Codex web conversations

The private operator web interface can use the local Codex CLI through `codex app-server --stdio`.
The bridge is pinned to `codex-cli 0.149.1`, fixes every turn to this repository, and sends
approval policy `on-request` with a read-only, no-network baseline. A bounded request can be accepted
once, declined, or used to cancel the turn in the control center; there are no session-wide approvals
or browser-controlled sandbox settings. It does not store Codex credentials; the CLI continues to
use the operator's existing Codex authentication and configuration.

By default the host resolves `codex` from its process path and finds the nearest repository ancestor
containing `.git` and `AGENTS.md`. Override either host-owned value when necessary:

```powershell
$env:Codex__ExecutablePath = 'C:\Tools\codex.exe'
$env:Codex__RepositoryRoot = 'C:\source\DantesRoleplay'
$env:Codex__PinnedVersion = '0.149.1'
$env:Codex__Model = 'gpt-5.6-luna'
```

The configured model is sent only when the bridge starts a new Codex thread. Resumed threads retain
the model recorded by Codex for that existing thread. The browser reports the selected host model but
cannot supply a model identifier.

The interaction planner is a separate no-tools integration and never reuses this repository-capable
Codex bridge. Its remote adapter is disabled by default. For development verification, enable the
host-owned adapter and supply the credential through the environment rather than a checked-in
settings file:

```powershell
$env:InteractionPlanning__Remote__Enabled = 'true'
$env:OPENAI_API_KEY = '<credential>'
```

The adapter fixes inner planning to `gpt-5.6-luna` with low reasoning and outer planning to the same
model with high reasoning. It sends strict schema-only Responses requests with no tools and no
stored provider response. Slice 12E registers only an internal planning service; a public interaction
route remains intentionally unavailable until Slice 12F.

Some packaged desktop-app binaries under `WindowsApps` cannot be launched by an ordinary child
process. In that case install an independently accessible Codex CLI or point
`Codex__ExecutablePath` at an accessible executable. The web status panel reports availability and
version mismatch without exposing authentication/configuration content.

After activation, an exact dry run followed by
`commit(kind: "system.state-space.create", ...)` creates one empty isolated runtime state space
bound to the supplied current activation fingerprint. Exact `system.applications` results include
the application's bounded state-space bindings for confirmation. Creation cannot adopt an existing
ID, create entities/components, upgrade a state space, or migrate legacy data.

`commit(kind: "system.state-space.upgrade", ...)` can advance an existing state space to the exact
current activation only when the space contains zero entities and zero components. It requires the
current binding fingerprint, records an immutable binding revision and compatibility receipt, and
preserves historical create/upgrade replay. Non-empty spaces fail with `MIGRATION_REQUIRED`; the
protocol accepts no migration script, data, or compatibility override.

## Known issues

1. When using VS Code, connecting to `https://localhost:5144` fails.
  * This is related to using a self-signed developer certificate, even when the certificate is trusted by the system.
  * Connecting with `http://localhost:6217` succeeds.
  * See [Cannot connect to MCP server via SSE using trusted developer certificate (microsoft/vscode#248170)](https://github.com/microsoft/vscode/issues/248170) for more information.

## More information

ASP.NET Core MCP servers use the [ModelContextProtocol.AspNetCore](https://www.nuget.org/packages/ModelContextProtocol.AspNetCore) package from the MCP C# SDK. For more information about MCP:

- [Official Documentation](https://modelcontextprotocol.io/)
- [Protocol Specification](https://spec.modelcontextprotocol.io/)
- [GitHub Organization](https://github.com/modelcontextprotocol)
- [MCP C# SDK](https://csharp.sdk.modelcontextprotocol.io/)
