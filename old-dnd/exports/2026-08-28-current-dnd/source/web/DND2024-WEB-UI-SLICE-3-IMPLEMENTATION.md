# D&D 2024 web UI Slice 3 implementation — generic prepared application actions

Status: **accepted 2026-08-27**
Owner/roadmap: [Web interface roadmap](WEB-INTERFACE-ROADMAP.md), Feature 5
Dependency tree/leaf: [D&D 2024 web UI dependency plan](DND2024-WEB-UI-DEPENDENCY-PLAN.md),
Order 3 / D1–D3
Ruleset alignment: **ruleset-neutral**
Source ID and locator: **not applicable**; this slice transports exact active application mechanics
without adding, interpreting, or changing any D&D rule.
Outcome: expose one exact current mechanic descriptor and a private prepare/confirm/execute adapter
that reuses the existing interaction verifier, authorization, execution coordinator, action runner,
transaction, idempotency, and durable receipt owners.
Exclusions: generic browser custom elements, D&D action controls, +/- controls, nested inventory,
input-schema authoring, effect-free auto-execution, model planning, route learning, multi-step/result-
binding proposals, D&D rule changes, migrations, MCP changes, page activation, and live state setup.
Allowed files/areas: interaction role/profile and submitted-proposal guard, ruleset-neutral web action
adapter/registration/routes/remote-path policy, interaction/web focused tests, this document/receipt,
the D&D web dependency plan, and the web roadmap.
Stop point: a verified private user can inspect one exact direct mechanic, prepare one inert server-
built proposal, explicitly confirm it, and receive the existing typed outcome and durable receipt;
no game-specific action control is added.

Model assignment: `gpt-5.6-sol`, xhigh reasoning, as assigned by Order 3 because this slice crosses
public routes, authorization, proposal identity, idempotency, current-state revalidation, effects,
transactions, replay, rollback, and receipts.

## Confirmed decisions

The user's 2026-08-27 instruction to “implement slice 3” confirms Order 3 and its public-route
boundary. The exact private routes are:

- `GET /api/applications/{applicationId}/state-spaces/{stateSpaceId}/mechanics/{qualifiedMechanicId}`;
- `POST .../mechanics/{qualifiedMechanicId}/prepare`; and
- `POST .../mechanics/{qualifiedMechanicId}/execute`.

The current private-host policy remains the only visibility/authorization owner: a verified private
operator may act only inside the requested application's bound state space. This slice introduces
no separate player/GM privilege claim. Every direct web action requires explicit confirmation,
including effect-free mechanics, because `procedure.system.use` requires confirmation of the exact
receipt, proposal fingerprint, full proposal, application, and state-space scope before execution.

Direct server-built proposals use a distinct generic `direct` interaction role profile. They never
invoke a planning model, cannot enter route learning, and are accepted only when the submitted
proposal path is used. The user's request confirms this cross-owner identity addition.

## D&D 5e 2024 alignment

No D&D rule meaning is implemented. Stable mechanic IDs, role names, requirements, source inputs,
JavaScript results, effects, and narration remain application-authored. The C# adapter treats each
role and component ID as opaque and never branches on `dnd2024` vocabulary.

## External implementation reference

No Foundry dnd5e review is applicable because this slice adds no D&D calculation, rule behavior,
eligibility, transition, or game-specific presentation. No external code, data, or asset is adopted.

## Prerequisite evidence

- [Slice 1 receipt](DND2024-WEB-UI-SLICE-1-RECEIPT.md) accepts exact private application/state-
  space isolation and the reviewed browser asset boundary.
- `procedure.system.use` requires an inert verified proposal followed by explicit confirmation of
  its exact receipt/fingerprint/body/scope and a distinct bounded execution idempotency key.
- `InteractionGateway` already accepts a caller-built proposal only through the common current
  inspection/verifier and writes a durable resolution receipt.
- `InteractionExecutionCoordinator` already rehydrates current authorization, activation,
  application/state revision, verifies the exact proposal, derives per-step seed/operation identity,
  enforces at-most-once execution, delegates to `IApplicationActionRunner`, and writes the execution
  receipt.
- `ApplicationActionRunner` already resolves declared projection, evaluates catalog JavaScript,
  translates typed effects, and commits them atomically through the application ECS effect owner.

## Runtime artifacts

- Add `InteractionAiRole.Direct` and its fixed non-model profile. Direct planning without a
  submitted proposal fails closed; execution rehydration recognizes only its exact stable key.
- Add a ruleset-neutral web service that resolves one trusted current mechanic through
  `IInteractionGateway`, verifies its application/state/activation scope, parses generic
  `MechanicRequirements`, and projects exact current component schemas for declared roles.
- The descriptor exposes authored ID/name/description, exact qualified ID/version/content
  fingerprint, required/optional role declarations, containment/relationship projection flags,
  and resolved component contract identity/version/profile/schema/hash. It never exposes mechanic
  JavaScript source.
- Input truth is explicitly `json-object` / `mechanic-validated`. The repository has no authored
  per-mechanic input-schema owner, so this slice does not infer one from JavaScript or filenames.
- Prepare accepts only a bounded idempotency key, role-to-entity IDs, and one JSON object. The
  server adds current version/fingerprint/state revision and builds a one-step inert action proposal.
- Execute accepts the exact resolution receipt ID, proposal fingerprint, one distinct execution
  idempotency key, and the inert proposal returned by prepare. It rejects any proposal that is not
  exactly one direct action for the route mechanic, then delegates unchanged to the existing
  coordinator.
- Add no table, migration, catalog record, mechanic, procedure, schema, effect, application
  registration, state-space record, custom element, or browser storage.

## Authoritative state and closed input

Application registry, active manifest, state-space binding, trusted public application snapshot,
mechanic record, generic requirements, component registry, private principal, interaction
authorization, resolution authority, execution coordinator, action runner, and receipt store remain
authoritative.

The prepare caller supplies only `{idempotencyKey, roleEntityIds, input}`. The execute caller supplies
only the exact inert preparation evidence plus a distinct `idempotencyKey`. Callers cannot supply or
override application/state revisions, activation/catalog truth, contract version/fingerprint,
required roles, component mappings/schemas, authorization, confirmation status, seed, projection,
effects, operation ID, result, narration, receipt status, learning, or transaction behavior.

## Behavior, result, and typed effects

- Descriptor lookup requires one trusted active mechanic belonging to the route application and a
  state space bound to its current activation. Event middleware and invalid requirement graphs are
  not direct actions.
- Role/component contracts are ordered ordinally and fully resolved against the application plus
  declared base applications. Missing or ambiguous current component ownership fails closed.
- Prepare canonicalizes the input object through existing interaction parsing, verifies exact role
  names and required roles through the common proposal verifier, and persists the inert resolution
  receipt. Missing required roles return the verifier's typed non-resolution without execution.
- Prepare never evaluates JavaScript, proposes effects, or writes game state.
- Execute requires a separate explicit call. The existing coordinator rechecks current authority,
  derives the seed/operation identity, evaluates the exact mechanic, and returns action result(s)
  plus the execution receipt. Zero-effect narration is a legitimate successful confirmed action.
- Effectful actions commit only through the existing application ECS root transaction. The page
  never sees or supplies the effect list.

## Failure, replay, and rollback contract

Malformed/oversize bodies, unknown or cross-application state/mechanic IDs, unpublished/untrusted/
inactive/event mechanics, stale activation/contracts, invalid requirements/component mappings,
unknown/duplicate/missing roles, non-object input, tampered proposal identity/body, mismatched
receipt/scope, unauthorized principal, reused conflicting idempotency keys, stale projections, and
rejected effects return typed safe failures without an unauthorized write.

Equal prepare retries replay the resolution receipt; conflicting reuse fails. Equal execute retries
replay the execution receipt/operation; changed reuse conflicts. A stale or rejected single-step
action commits no ECS effect. Existing action/effect transaction rollback remains the root owner;
the web adapter owns no transaction and performs no compensating write.

## Implementation sequence

1. Add the fail-closed direct interaction profile and tests without changing model planning.
2. Add the generic descriptor/prepare/execute service over existing owners and focused service
   tests for positive, stale, cross-scope, missing-role, tamper, replay, and no-change cases.
3. Add the three exact private routes, rate limits, strict body bounds/error mapping, exact remote
   path shapes, and route inventory/security tests.
4. Run focused interaction/web tests, build, full suites, inspect one disposable local route,
   record the receipt, update Order 3 once, and stop.

## Acceptance matrix

| Case | Evidence |
| --- | --- |
| Descriptor | Exact trusted mechanic and role/component schema contracts project without JavaScript source. |
| Prepare | One current server-built proposal and durable resolution receipt; no action runner call or ECS write. |
| Required input | Missing/unknown roles, invalid JSON root, and oversize/malformed input fail safely. |
| Confirmation | No prepare response can execute; only the separate exact execute request reaches the coordinator. |
| Execution | Confirmed effect-free and effectful fixtures return typed action results and execution receipts. |
| Authority | Unknown/wrong app/state, stale activation/fingerprint/revision, untrusted/event mechanic, and unverified principal fail closed. |
| Tamper | Changed route mechanic, proposal body/fingerprint, receipt, scope, or server-owned values cannot execute. |
| Replay | Equal prepare/execute keys replay; conflicting reuse returns typed conflict. |
| Rollback | Rejected/stale action leaves component/containment revisions unchanged. |
| Compatibility | Application conversation/MCP interaction paths and existing web/system controls remain green. |
| Surface | Three exact private routes only; remote matcher rejects extras, slashes, and malformed IDs. |

## Verification commands

- Focused interaction contract/gateway/execution tests for the direct profile and submitted-proposal
  guard.
- Focused web action service and `WebInterfaceTests` for descriptor, prepare/execute, route/rate-
  limit inventory, remote boundary, body limits, isolation, replay/tamper/no-change behavior.
- `dotnet build DantesRoleplay.slnx --no-restore`
- Full core/web and local-AI test suites.
- `git diff --check` plus trailing-whitespace checks over Slice 3 files.

Catalog validation and the MCP protocol walk are not required because this slice changes no catalog
artifact, MCP operation, or dependency registration.

## Completion receipt and exit gate

Write `DND2024-WEB-UI-SLICE-3-RECEIPT.md`, mark Order 3 accepted only after all D1–D3 evidence
passes, and stop. Generic entity picker/button/form, D&D dice/check/save controls, +/- mutations,
nested inventory, page activation, and live application state remain outside this slice.
