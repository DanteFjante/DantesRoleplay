# Local development knowledge access

This is a temporary local-only bridge until real authentication exists. It is disabled by default
and represents exactly one fixed seat for the whole MCP host. Do not bind this host to a LAN or the
internet while it is enabled.

Set these environment variables before starting `DantesRoleplay.MCPServer`:

```powershell
$env:DANTESROLEPLAY_DEVELOPMENT_KNOWLEDGE_AUDIENCE = "true"
$env:DANTESROLEPLAY_DEVELOPMENT_CAMPAIGN = "campaign.your-campaign-id"
$env:DANTESROLEPLAY_DEVELOPMENT_ROLE = "gm"
# For an actor view instead:
# $env:DANTESROLEPLAY_DEVELOPMENT_ROLE = "actor"
# $env:DANTESROLEPLAY_DEVELOPMENT_ACTOR = "actor.your-actor-id"
$env:DANTESROLEPLAY_OLLAMA_COMPLETION = "true"
```

The host refuses an explicit non-loopback `ASPNETCORE_URLS` value and rejects non-loopback MCP
requests while this mode is on. Start it normally, then ask through MCP:

```text
query(kind: "knowledge-answer", campaignId: "campaign.your-campaign-id", question: "What is known about the old toll?")
```

The actor view uses the existing effective knowledge-state rules. A GM seat reads campaign-scoped
canonical records; an actor seat receives only perspective-safe statements. Neither request can
supply a different actor, role, world, visibility override, or include-hidden flag.

The first query rebuilds the local derived lexical index from canonical world knowledge, so no
separate indexing command is required while this bridge is active.

To turn the bridge off, remove `DANTESROLEPLAY_DEVELOPMENT_KNOWLEDGE_AUDIENCE` and restart. Replace
this fixed development policy with real authentication before any shared or published deployment.
