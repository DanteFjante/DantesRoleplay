# D&D 2024 MVP audience-context implementation

Status: accepted 2026-08-28
Owner/roadmap: `ruleset/dnd2024/ROADMAP.md`
Dependency tree/leaf: `DND2024-MVP-AUDIENCE-CONTEXT-DEPENDENCY-TREE.md` / lowest ready leaf
Ruleset alignment: dnd2024-compatible
Source ID and locator: not applicable; no D&D rule is implemented.

## Outcome and boundary

Expose `query(kind: "system.audience-context")` through the existing MCP `query` verb. It gives
the DM the current host-authorized application, state space, campaign, actor, and role hint before
the normal `system.interaction-plan` then `system.interaction-execute` flow.

Allowed areas: the generic MCP query catalog/dispatcher, a focused adapter, focused tests, and this
implementation evidence. Excluded: web companion work, new endpoints/tools, schema/state changes,
conversation persistence, D&D rule logic, and caller-selected identities.

## Confirmed decisions

- The existing `orient`, `query`, and `commit` tools remain the complete MCP surface.
- `system.audience-context` is the one new read-only query kind.
- Existing host configuration and active campaign-participation verification are authoritative;
  no duplicate player-seat entity is created.

## Prerequisite evidence

- `LocalKnowledgeAudiencePolicy` resolves only a loopback, enabled, exact configured actor seat.
- `ActivatedKnowledgeApplicationBindingResolver` resolves one exact active application/campaign
  binding.
- `ApplicationKnowledgeActorParticipationVerifier` proves active campaign-to-actor membership.

## Runtime artifacts

- `system.audience-context`: no-input read-only query kind.
- `SystemAudienceContextTools`: generic adapter that composes existing authorization and binding
  owners without D&D identifiers or rules.

## Authoritative state and closed input

There is no caller payload. The current server-side local seat selects the campaign and actor. The
adapter rechecks policy, binding, and active participation for every call.

## Behavior, result, and failure contract

On success, return application ID, state-space ID, campaign ID, actor ID, actor role hint, and the
policy/binding/participation revisions. On disabled, remote, stale, malformed, mismatched, or
inactive state, return a generic denial with no binding details. The operation is read-only and
cannot be replayed into a write.

## Acceptance matrix

| Case | Evidence |
| --- | --- |
| Valid fixed seat | exact context and `roleHints.actor` returned |
| Policy denial | no binding or participation lookup |
| Invalid binding or participation | generic denial and no write |
| MCP protocol | kind is advertised, dispatched, and tool descriptions name it |
| Surface | exactly three MCP tools remain |

## Verification commands

```text
dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --filter "FullyQualifiedName~SystemAudienceContextToolsTests|FullyQualifiedName~GuardTests|FullyQualifiedName~LocalKnowledgeAudienceTests"
roleplay validate catalog
```

## Completion receipt and exit gate

Recorded in `evidence/DND2024-MVP-AUDIENCE-CONTEXT-RECEIPT.md`. A later chat client may use this
context; it must not invent an alternate execution path.
