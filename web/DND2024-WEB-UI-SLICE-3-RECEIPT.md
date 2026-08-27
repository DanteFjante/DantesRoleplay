# D&D 2024 web UI Slice 3 completion receipt

Status: **accepted 2026-08-27**
Implementation: [Slice 3](DND2024-WEB-UI-SLICE-3-IMPLEMENTATION.md)
Dependency leaf: [D&D 2024 web UI plan](DND2024-WEB-UI-DEPENDENCY-PLAN.md), Order 3 / D1–D3
Ruleset alignment: **ruleset-neutral**; no D&D rule, catalog record, schema, mechanic, procedure,
authored content, application registration, or live game state changed.

## Delivered boundary

- Added the fixed non-model `direct:none:none` interaction role. It is valid only with an exact
  submitted proposal, never invokes a planning model, and rehydrates through the existing execution
  authority after confirmation.
- Added a private ruleset-neutral mechanic descriptor for one exact trusted mechanic active in the
  requested application's current activation. It returns authored identity and description,
  version/fingerprint, generic role requirements, projection flags, and exact registered component
  schema identities. Mechanic JavaScript source is never returned.
- Kept input authority honest: the repository has no authored per-mechanic input JSON Schema, so
  the descriptor reports a bounded JSON-object shape with mechanic-owned validation and
  `schemaStatus: not-authored` instead of inventing fields from source code.
- Added inert prepare and explicit execute operations. Prepare accepts only an idempotency key,
  role/entity bindings, and one JSON object; the server adds current contract identity and persists
  the existing resolution receipt without evaluating the mechanic or changing ECS state.
- Execute accepts the exact returned proposal, proposal fingerprint, resolution receipt, and a
  distinct idempotency key. It rejects multi-step, dependency, result-binding, and route-mechanic
  substitutions before delegating to the existing coordinator, action runner, effect transaction,
  replay, rollback, and receipt owners. Learning is fixed off.
- Added three exact private routes: descriptor `GET`, preparation `POST`, and execution `POST`.
  Action bodies are closed, canonical, UTF-8, object-only, and bounded to 64 KiB even without a
  content length. All responses are `no-store`; the existing verified private-operator guard and
  read/upload rate limits remain authoritative.

## Authority and transaction review

- Route application/state/activation scope is checked before lookup or planning. Mechanic identity
  is proven by exact trusted lookup in the active application snapshot, including legitimate
  activated base-application mechanics rather than trusting a namespace prefix.
- Required and unknown roles, current mechanic version/fingerprint, and exact proposal shape are
  revalidated by the shared proposal verifier. The browser cannot supply revisions, activation
  truth, authorization, seed, operation identity, effects, result, narration, or receipt status.
- The web adapter owns no game rule and no transaction. The execution coordinator rehydrates the
  persisted resolution authority, detects stale scope or proposal evidence, derives execution
  identity, and delegates the one action to `IApplicationActionRunner`. Existing action/ECS tests
  prove at-most-once application and atomic rollback.
- Equal resolution/execution retries and conflicting key reuse remain owned by the durable
  interaction/action stores. The new direct role is covered through prepare, confirmed execution,
  and execution-authority rehydration without calling a planner.

## Acceptance evidence

- `dotnet build DantesRoleplay.slnx --no-restore` — passed with 0 warnings and 0 errors.
- Focused Slice 3 interaction/web suite — 10 passed, covering the direct profile and no-planner
  guard, confirmed execution rehydration, exact descriptor/schema projection, inert preparation,
  execution delegation, event/stale/tamper failures, exact route/rate-limit inventory, and remote
  path closure.
- `ApplicationMechanicWebServiceTests` — 8 passed, including unknown-member, duplicate-property,
  and casing-alias rejection, non-object input, chunked-body limits, and proof that rejected bodies
  never plan.
- `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-build` — 1,232 passed.
- `dotnet test src/system/local-ai/DantesRoleplay.LocalAI.Tests/DantesRoleplay.LocalAI.Tests.csproj
  --no-restore` — 21 passed.
- A disposable private host exercised the real descriptor route without the live database. The
  unknown disposable state space returned typed `STATE_SPACE_UNKNOWN` HTTP 404, `Cache-Control:
  no-store`, JSON content, and `X-Content-Type-Options: nosniff`; the host was then stopped.
- `git diff --check` passed with no whitespace error in the delivered Slice 3 files.

Catalog validation and the MCP protocol walk were not run because this slice changes no catalog
artifact, MCP operation, or MCP dependency registration.

## Deliberate exclusions and stop

No generic browser entity picker, action button, form, D&D dice/check/save control, +/- mutation,
HP/inventory/equipment action, encounter mutation, nested inventory traversal, page activation,
application-to-page association, model planning, route learning, or live-state setup was added.
Slice 4 owns the interactive game-styled control layer that will consume this seam; ordinary
character-sheet editing may use forms where appropriate, but their mutations remain explicit
buttons/steppers with a separate confirmation boundary rather than raw authority-bearing fields.
