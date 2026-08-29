# Bound-role planning and web audience-context implementation

Status: active
Owner/roadmap: `ruleset/dnd2024/ROADMAP.md`
Dependency tree/leaf: `DND2024-MVP-BOUND-ROLE-PLANNING-DEPENDENCY-TREE.md` / lowest ready leaf
Ruleset alignment: ruleset-neutral
Source ID and locator: not applicable; no D&D rule meaning changes.

## Outcome and boundary

Protect a host-bound role during interaction planning and expose that same already-verified ambient
binding to the local web companion. If the authoritative intent supplies `roleHints[role]`, a
proposal may use that role only with the same entity reference; it cannot replace it through a
static or query-result role binding. `GET /api/audience-context` returns no caller-selected
identity and exists so the prototype's server route can start from the existing campaign rather
than a fixture.

Allowed areas: generic interaction-proposal verification, the existing host audience resolver, one
generic local-web read route, the prototype's server-only connection adapter, its initial
campaign-root/character-record display, tests, and this implementation evidence. Excluded: new MCP
tools/kinds, D&D IDs or rule logic in C#, campaign/state mutation, fixture import, broad
world/campaign projection, creation-flow changes, and automatic filling of omitted optional roles.

Stop after the verifier rejects substitutions, the local web route returns the same verified
binding without request parameters, and the prototype reads only the returned campaign, actor,
campaign-root component, and character-record component.

## Confirmed decisions

- The user confirmed continued protected-player work on 2026-08-29.
- Existing `roleHints` retain their closed input shape. At proposal verification they are exact
  host-authorized constraints only for roles the proposal elects to bind.
- The user confirmed on 2026-08-29 that the prototype must use the existing server campaign, not
  import the Eldervale fixture. The generic local web route may therefore return only the existing
  host-selected application, state space, campaign, actor, and creation-needed state.

## Prerequisite evidence

- `InteractionIntent` records canonical, bounded role hints in the authorized envelope.
- `InteractionProposalVerifier` verifies both generated and submitted proposals before producing a
  resolved plan or resolution receipt.
- `system.audience-context` supplies the active player as the `actor` hint without caller input.
- `SystemAudienceContextTools.ResolveAsync` already validates the host configuration, ambient
  audience policy, active application binding, and campaign participation in that order.

## Runtime artifacts

One public generic local-web read route is added: `GET /api/audience-context`. It accepts no
parameters and returns the same already-authorized binding shape as the existing MCP query. No
new D&D ID, schema, migration, effect, or transaction root is added. The prototype adapter is
server-only and has one configuration value: the credential-free C# server origin.

## Authoritative state and closed input

The authorized interaction envelope owns the role-hint map. The proposal supplies its own static
role bindings and result bindings. The verifier compares them; callers and planners cannot replace
a matching hinted role with another entity. The existing contract still decides whether a role is
required.

The host configuration and existing campaign participation own the audience context. The web route
cannot accept an application ID, campaign ID, actor ID, or role. It invokes the same resolver;
only a loopback request that satisfies the existing policy may receive a bound or
character-creation-required response.

The prototype's server route reads `GET /api/audience-context` first. Only after a `bound` result
does it request the exact returned campaign and actor entities and their known campaign-root and
temporary-character-record components through the existing generic application-state API. It never
reads an entity ID from browser input and has no Eldervale fallback.

## Behavior, failure, replay, and rollback contract

For each draft step, before normal role completeness is evaluated:

- static binding equal to its intent hint is accepted;
- static binding different from its intent hint returns an unsafe non-resolution;
- a result binding targeting a hinted role returns an unsafe non-resolution;
- roles absent from the intent hint map keep existing behavior.

No effect or transaction occurs. Since a rejected proposal has no proposal fingerprint, it cannot
be executed or replayed. Valid proposal fingerprints incorporate the already-authorized envelope.
The audience read has no effects, transaction, cacheable state, or replay token.

## Acceptance matrix

| Case | Expected result |
| --- | --- |
| Matching hinted actor | resolved normally |
| AI-proposed different actor | unsafe before a proposal is created |
| Submitted different actor | unsafe before a proposal is created |
| Query-result replacement of hinted role | unsafe |
| Unhinted target result binding | existing accepted behavior |
| Local web request with active player | exact existing application/state-space/campaign/actor binding |
| Web request with missing character | character-creation-required and exact reserved actor ID |
| Web request with invalid/inactive binding | no binding fields; denial response |
| Request-supplied application/campaign/actor | ignored because the route accepts none |
| Prototype server adapter | reads exactly the returned campaign/actor plus their declared initial display components, or renders an explicit unavailable/denied/creation-needed state |

## Verification commands

```text
dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --filter "FullyQualifiedName~InteractionPlanningTests|FullyQualifiedName~InteractionQueryTests|FullyQualifiedName~InteractionOrchestrationAcceptanceTests|FullyQualifiedName~SystemAudienceContextToolsTests|FullyQualifiedName~GuardTests"
dotnet build DantesRoleplay.MCPServer/DantesRoleplay.MCPServer.csproj --no-restore
roleplay validate catalog
node --test test/game-server-context.test.js
npm run build
```

## Completion receipt and exit gate

Record evidence in `evidence/DND2024-MVP-BOUND-ROLE-PLANNING-RECEIPT.md`. The next capability is
server-reserved character-creation input binding; it is deliberately outside this slice.
