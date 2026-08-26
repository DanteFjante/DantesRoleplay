# E8 trigger scheduling Slice 10 implementation — web/MCP management and final acceptance

Status: **accepted**
Owner/roadmap: [Platform enabling features roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)
Dependency tree/leaf: [E8 trigger scheduling, I. web/MCP management and acceptance](E8-TRIGGER-SCHEDULING-DEPENDENCY-PLAN.md)
Ruleset alignment: **ruleset-neutral**
Source ID and locator: **not applicable**
Outcome: Expose the accepted trigger-scheduling registrations, statuses, evidence, and phone pairing
through one authenticated administration contract shared by the control-center web UI and the
existing MCP three-verb protocol, then complete feature-wide compatibility/security acceptance.
Exclusions: New trigger semantics, raw GPS, forwarded phone notifications, outbound polling or
destinations, secret recovery/rotation, push delivery, automatic catalog/live synchronization,
arbitrary predicates/code, direct event insertion, state-changing targets, delegated action
authority, retention/redaction execution, or a phone application.
Allowed files/areas: `src/system/trigger-scheduling/{domain,persistence,hosting,tests}`; existing
authorization capability contracts/tests; web control security/endpoints/services/tests and the
control-center example; MCP query/commit catalogs, adapters, registration, protocol tests; the
governing system procedure; component/catalog coverage only if required; this implementation,
receipt, dependency plan, and owning roadmap.
Stop point: An authenticated private operator can inspect the whole bounded trigger system,
preview and execute supported registrations/revisions/pairing/revocation through web or MCP, and
see dynamic schedule/notification outcomes in the control center. Stop before every exclusion
above and close the E8 downstream trigger plan.

## Confirmed decisions

- The existing three MCP tools remain `orient`, `query`, and `commit`. Slice 10 adds one query kind
  and one commit kind, both named `system.trigger-scheduling`; it does not add an MCP tool.
- The web owner remains the private control boundary. New routes live only below
  `/api/control/triggers`, use the closed trigger-administration capability, same-origin checks for
  writes, no-store responses, and existing read/upload limits.
- Both adapters use the same domain administration service and exact command envelope:
  `{requestToken, operation, applicationId, value}`. The 32-lowercase-hex request token is server
  replay/audit identity; application scope is never derived from `value`.
- Closed operations are `structure.register`, `source.register`, `one-time.register`,
  `recurring.register`, `conditional.register`, `observation-trigger.register`, `phone.register`,
  and `phone.revoke`. A lifecycle change is a normal immutable next definition revision, never an
  in-place update. Observations, fires, work, receipts, and notifications remain read-only evidence.
- Structure registration is the explicit runtime synchronization boundary for an already reviewed
  application-authored schema. It does not edit or export catalog files and does not make website
  content catalog authority.
- The query resources are `overview`, `structures`, `sources`, `devices`, `one-time`, `recurring`,
  `conditional`, `observation-triggers`, `observations`, `fires`, and `phone-principal`. Omitted
  application scope returns bounded registered-application summaries. Results omit credentials,
  verifiers, raw headers, raw data JSON, lease tokens, and unnecessary work internals.
- MCP mutation requires an exact successful dry run of the same canonical request token, operation,
  application, and value. Web mutation has distinct preview/apply routes and the UI previews before
  enabling apply. Commit success and its operation/audit record share one database transaction.
- `phone.register` returns its credential only on the first successful commit response. Replay
  returns safe device status without a credential; get/list/query never recover it.
- The existing notification store remains the content/delivery authority. Trigger status projects
  notification IDs and outcomes dynamically; management never edits notification content/state.
- These public IDs and route family are confirmed by the explicit request to finish the planned
  Slice 10 public-management boundary. No other permanent kind or route is introduced.

No D&D source or Foundry reference applies because this slice is generic private-host
administration and contains no game rule.

## Prerequisite evidence

| Concern | Existing evidence | Slice 10 use |
| --- | --- | --- |
| Trigger runtime and statuses | Slices 0–8 receipts and current focused tests | Reuse immutable definitions, workers, evidence, current status readers, and notification-only behavior without new semantics. |
| Phone identity/privacy | [Slice 9 receipt](E8-TRIGGER-SCHEDULING-SLICE-9-RECEIPT.md) | Reuse internal registry, one-time credential, exact permission, revocation, and privacy defaults. |
| Private web administration | `WebControlRequestGuard` and `/api/control` conventions | Reuse loopback/Tailscale identity, same-origin mutation defense, safe headers, and rate limits. |
| MCP administration | Generic verb catalog, private operator authorizer, request-token/dry-run patterns | Add kinds under existing tools and durable operation audit rather than a fourth tool. |
| Governing procedures | `procedure.system.use` and `procedure.system.inspect` | Query/commit capability descriptions and operator instructions stay generated and explicit. |

## Runtime artifacts

| Artifact | Purpose |
| --- | --- |
| `TriggerSchedulingAdministrationCommand/Context/Result` | Closed shared request, authorization/audit evidence, preview/commit result, and replay identity. |
| `ITriggerSchedulingAdministrationService` | Single transaction owner for preview validation, commit, replay, operation audit, and bounded queries. |
| `TriggerSchedulingAdministrationView` | Safe bounded application/structure/source/device/status/observation/fire projection. |
| `trigger.admin.read` / `trigger.admin.write` | Closed capabilities selected by server route/tool mapping, never caller input. |
| `/api/control/triggers/applications` | Bounded application summaries. |
| `/api/control/triggers/applications/{applicationId}` | Safe resource/id/limit query and aggregate overview. |
| `/api/control/triggers/applications/{applicationId}/phone-principal/{deviceId}` | Authorized deterministic pairing-principal projection. |
| `/api/control/triggers/commands/preview` and `/commands` | Same-envelope preview and mutation routes. |
| MCP `system.trigger-scheduling` query/commit kinds | Existing three-verb access to the same query/command service. |
| `trigger-scheduling-panel` | Non-technical status/pairing/reminder view inside the persistent control-center shell. |

## Authoritative state and closed input

SQLite remains authoritative for live trigger registrations, observations, work, receipts, phone
status, and notification links. Catalog files remain authoritative for authored structures before
explicit reviewed synchronization. The administration service never writes catalog files.

The server derives canonical request fingerprints, operation subjects, recorded times, current
versions/statuses, structure hashes where the contract derives them, source/structure staleness,
device principal/verifier/status, fire/work/notification identity, replay outcome, and audit guard
evidence. Callers may not supply credentials except the newly returned pairing secret, stored
verifiers, current pointers, status evidence, observations/fires/work/receipts, notification IDs,
effects, events, handlers, code, paths, destinations, or authorization.

## Behavior, result, and transaction ownership

1. The adapter authenticates a private operator with the fixed read/write capability before query
   parameters or command bodies reach the owner.
2. Query validates application/resource/id/limit and returns only bounded safe projections. An
   overview combines current definitions/statuses, devices, recent observations, and recent fires;
   notification IDs update as accepted workers commit them.
3. Preview validates the exact closed envelope and selected operation, executes all contract/scope
   checks inside a rolled-back transaction, then records bounded preview evidence keyed by command
   fingerprint. It returns no generated phone credential.
4. Commit starts one root SQLite transaction, verifies the exact preview and request token, invokes
   the existing owner store/registry without a nested transaction, records the successful operation
   using the request token and authorization evidence, and commits once.
5. Exact request-token replay verifies the prior operation subject and current immutable resource,
   then returns `replay`. Conflicting token reuse fails. Phone replay omits the secret.
6. The control-center panel uses query/preview/commit routes, presents applications and current
   schedule/condition/observation/device/notification status, provides a simple one-time reminder
   and phone pairing workflow, and keeps an advanced shared-command form for the other exact
   operations. It never evaluates rules or infers missing IDs/revisions.

## Failure, replay, and rollback contract

Missing/extra/malformed fields, invalid resource/operation/token/application/ID/version/enum/time,
unreviewed adapter, invalid schema/configuration, stale source/structure/hash/current pointer,
wrong state-space/entity scope, omitted source principal, non-minimized phone structure, duplicate
or conflicting definition, missing exact preview, conflicting request token, unauthorized/direct
remote/cross-origin request, over-limit query/body, injected store/audit/database failure, or
concurrent commit leaves no definition, pointer, device, status, operation receipt, observation,
fire, notification, event, effect, action, or state change. Exact definition/command replay returns
the prior safe resource. Unknown/revoked/wrong-scope phone credentials remain non-enumerating.

## Implementation sequence

1. Add pure public administration/query contracts and authorization capability tests.
2. Add the shared SQLite administration/query service and make existing store transactions safely
   participate in one outer transaction.
3. Add web control routes/readers and focused origin/auth/replay/rollback tests.
4. Add MCP query/commit kinds, capability catalog/procedure wording, and protocol tests.
5. Add the control-center trigger panel with overview, reminder, pairing, preview/apply, safe secret
   display, retry, and mobile navigation behavior.
6. Run security/final acceptance, inspect all artifacts, write the receipt, and close owning plans.

## Acceptance matrix

| Area | Required proof |
| --- | --- |
| Query/status | Web and MCP return matching bounded safe application/resource views; notification IDs/status update after worker delivery. |
| Registration | Every closed definition operation previews and commits through current contracts; next revisions change lifecycle without rewriting history. |
| Phone | Principal derivation, pairing, one-time secret display, status, revocation, and secret-free replay/readback pass. |
| Authorization | Direct remote, unauthenticated, device-credential-only, wrong capability, cross-origin, and missing dry-run writes fail before owner mutation. |
| Replay/concurrency | Exact request token/definition replay is stable; conflicting reuse and concurrent submissions create at most one revision/audit result. |
| Rollback/security | Injected store/audit/database failure and migrated SQLite tampering leave no partial public administration result. |
| Privacy | No query/UI/MCP/audit/error exposes credential verifier, replayed credential, raw header, raw observation JSON, lease secret, raw GPS, or phone notification content. |
| UI | Persistent navigation opens trigger management; applications, statuses, notification outcome, reminder preview/apply, and pairing/revocation are usable on desktop/mobile. |
| Compatibility | Existing observation route, workers, E8 routing, actions/effects/events, notifications, settings/pages/assistants, catalog authority, and exactly three MCP tools remain unchanged. |
| Final acceptance | Focused/full tests, build, migration/model drift, catalog validation, protocol walk, public catalog consistency, and diff checks pass or an unrelated reproducible issue is receipted. |

## Verification commands

```text
dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~TriggerSchedulingAdministration|FullyQualifiedName~PhoneCompanion|FullyQualifiedName~WebInterfaceTests|FullyQualifiedName~SystemTriggerScheduling"
dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~TriggerScheduling
dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~PrivateOperatorAuthorization|FullyQualifiedName~CatalogCoverage|FullyQualifiedName~MigrationDrift"
dotnet ef migrations has-pending-model-changes --project DantesRoleplay.DataAccess --startup-project DantesRoleplay.DataAccess --configuration Release --no-build
dotnet build DantesRoleplay.slnx -c Release --no-restore
dotnet test DantesRoleplay.slnx -c Release --no-restore
dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj -c Release --no-restore -p:IncludeProtocolWalkTests=true --filter FullyQualifiedName~ProtocolWalkTests
dotnet run --project DantesRoleplay.Tools/DantesRoleplay.Tools.csproj -c Release --no-build -- validate catalog
git diff --check
```

## Completion receipt and exit gate

Accepted evidence will be recorded in
`platform/e8/E8-TRIGGER-SCHEDULING-SLICE-10-RECEIPT.md`. Completion requires every accepted
trigger-scheduling capability to be operable or inspectable through the shared public management
boundary, the control-center panel to show current outcomes, and all deliberate exclusions to
remain absent. The E8 downstream trigger dependency plan then becomes complete through Slice 10.
