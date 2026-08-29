# D&D 2024 MVP audience-context dependency tree

Status: lowest leaf ready
Ruleset alignment: dnd2024-compatible
Source: not applicable; this is ruleset-neutral host context plumbing.

## Outcome and non-goals

The chat DM can read one host-authorized player context before it plans an action: application,
state space, campaign, and actor. The player never supplies or selects those identities.

This does not add a chat endpoint, a fourth MCP verb, a new D&D component, a player-seat record,
a conversation database, or any rule calculation.

## Existing owners and evidence

| Concern | Owner | State | Evidence |
| --- | --- | --- | --- |
| Fixed local player seat | private host configuration and `LocalKnowledgeAudiencePolicy` | verified | `DantesRoleplay.MCPServer/LocalKnowledgeAudience.cs` |
| Application/campaign vocabulary | activated application metadata | verified | `catalog/applications/dnd2024/metadata/authorized-knowledge.json` |
| Active character participation | generic knowledge participation verifier | verified | `ApplicationKnowledgeActorParticipationVerifier` |
| Durable game state/replay | application ECS, action runner, and audit | verified | `procedure.action.run` and accepted D&D mechanics |
| MCP read surface | `query` dispatcher and `VerbSurface` | verified | `DantesRoleplay.MCPServer/Tools/QueryTool.cs` |

## Dependency tree

```text
DM receives an authoritative player context [ready]
└─ current fixed local seat is authorized [verified]
   ├─ exact active application/campaign binding resolves [verified]
   └─ configured actor has one active participation [verified]
```

## Confirmation gates

The user explicitly requested implementation while retaining the `orient` / `query` / `commit`
surface. That confirms one new permanent **query kind**, `system.audience-context`; no new tool or
endpoint is created.

## Lowest ready leaf

Add the query kind and a small generic dispatcher adapter. It must return only verified bindings,
must accept no caller identity or scope, and must perform no write.

## Planning receipt

- Runtime artifacts created: none.
