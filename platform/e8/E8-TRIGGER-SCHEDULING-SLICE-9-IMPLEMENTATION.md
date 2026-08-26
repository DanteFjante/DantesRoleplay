# E8 trigger scheduling Slice 9 implementation — phone companion identity and privacy-minimized observations

Status: **accepted**
Owner/roadmap: [Platform enabling features roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)
Dependency tree/leaf: [E8 trigger scheduling, G. phone device/geofence source](E8-TRIGGER-SCHEDULING-DEPENDENCY-PLAN.md)
Ruleset alignment: **ruleset-neutral**
Source ID and locator: **not applicable**
Outcome: Add durable revocable phone-device identity whose one-time credential can authenticate the
existing private observation route, bind submissions to one exact source/device/structure scope,
and admit only structures explicitly classified as privacy-minimized signals.
Exclusions: A registration/revocation web or MCP route, phone application code, push delivery,
continuous/raw location, background location permissions, forwarded phone-notification content,
outbound polling, third-party destinations, credential recovery/rotation, state-changing targets,
events/actions/effects, and live import.
Allowed files/areas: `src/system/trigger-scheduling/{domain,persistence,hosting,tests}`;
the existing web observation security/endpoint registration and tests; `DantesRoleplayDbContext`,
an additive EF migration/snapshot, catalog coverage, the trigger component manifest, this
implementation/receipt, and owning roadmap rows.
Stop point: A pre-authorized internal caller can register/revoke one device, and the device can use
its credential on the existing observation endpoint for privacy-minimized exact-source evidence.
Stop before Slice 10 public management UI/web/MCP registration and final acceptance surfaces.

## Confirmed decisions

- The existing `POST /api/applications/{applicationId}/observations` envelope and response do not
  change. Phone companions are another trusted transport identity for that route, not a new event,
  trigger, or device-controlled schema/handler surface.
- A phone installation supplies an opaque `phone-device.<32 lowercase hex>` ID during the internal
  authorized pairing workflow. The application/device tuple deterministically derives its opaque
  `principal.<64 lowercase hex>` before source registration, so an authorized source revision can
  explicitly permit that principal without the device service mutating source policy.
- Registration binds one application, device ID, exact current enabled source revision, derived
  principal, permission profile, and 1–8 exact current active source-allowed structure
  revisions/hashes. Version changes make the registration stale; they never silently widen it.
- The only Slice 9 permission profile is `privacy-minimized-signals`. A structure has an immutable
  data classification: `general`, `privacy-minimized-signal`, `raw-location`, or
  `third-party-notification-content`. Phone registration accepts only
  `privacy-minimized-signal`; other classes require future separately reviewed profiles.
- Registration creates a 256-bit random `phone-credential.<64 lowercase hex>` returned once. SQLite
  stores only a domain-separated SHA-256 verifier. Reads/status never expose the credential or its
  verifier. Re-registration conflicts; a revoked device cannot be reactivated or recover its secret.
- Revocation appends immutable status revision 2 and advances a guarded current pointer. Request
  authentication revalidates current active status for every call before body parsing.
- Device authentication additionally requires route application, exact source ID/current version,
  `source.instanceId == deviceId`, and exact allowed structure revision/hash. Operator-authenticated
  submissions retain existing behavior.
- Existing observed-time/replay-window, occurrence identity, schema validation, rate limiting,
  observation matching, and async delivery remain authoritative. Offline evidence is accepted only
  inside the source replay window; exact duplicate transitions replay rather than duplicate.
- Tailscale remains transport privacy, not device identity. This slice adds no outbound client,
  secret provider, uploaded code, raw OS permission, or network destination.

No D&D source or Foundry reference applies because this slice is generic private-host
infrastructure.

## Prerequisite evidence

| Concern | Existing evidence | Slice 9 use |
| --- | --- | --- |
| Authenticated bounded observation route | Trigger scheduling Slices 2A–3 | Reuse exact envelope, authenticate-before-parse filter, source permissions, schema validation, rate bounds, safe response, and immutable evidence. |
| Exact observation matching | [Slice 8 receipt](E8-TRIGGER-SCHEDULING-SLICE-8-RECEIPT.md) | Reuse source/structure revision staleness, deterministic replay, bounded match work, and notification-only delivery. |
| Private/Tailscale transport | E9 private web boundary | Reuse transport access controls; device credential is an additional identity check and does not trust Tailscale alone. |

## Runtime artifacts

| Artifact | Purpose |
| --- | --- |
| `ObservationDataClassification` | Immutable generic privacy classification on every observation structure. |
| `PhoneCompanionDeviceId` / `PhoneCompanionIdentity` | Closed opaque device ID and deterministic principal derivation. |
| `PhoneCompanionRegistrationRequest/Result/View` | Internal pairing contract; result is the only one-time credential projection. |
| `IPhoneCompanionRegistry` | Internal register, revoke, get, and list boundary; no public route in Slice 9. |
| `IPhoneCompanionCredentialGenerator` | Production CSPRNG seam and deterministic test seam. |
| `IPhoneCompanionAuthenticator` | Credential-to-current-device authentication without exposing stored verifier. |
| `IObservationIngestionPolicy` | Post-parse exact binding check; device policy is inert for operator principals. |
| Device registration/structure/status/current tables | Durable exact scope, append-only revocation evidence, and guarded current status. |
| Existing observation filter | Selects device credential authentication when the dedicated header is present; otherwise preserves private-operator authorization. |
| Additive migration | Structure classification plus device tables, constraints, FKs, indexes, and immutability/transition/scope guards. |

## Authoritative state and closed input

The registration owns application, device ID, exact source revision, derived principal, credential
verifier, permission profile, exact allowed structures/hashes, created time, and append-only status
history. The server derives principal, verifier, timestamps, and status revision. Only the initial
result contains the generated credential.

The phone still supplies only the accepted observation envelope. It cannot supply device status,
principal, credential verifier, source revision, structure hash/classification, replay verdict,
trigger selection, target, notification content, action/effect/event claims, or retention policy.
The credential travels only in the dedicated request header and is never copied into observation,
audit, response, or status records.

## Behavior, result, and transaction ownership

1. An authorized internal caller derives the principal for its chosen opaque device ID, registers a
   source revision permitting that principal, and calls registration with exact allowed structures.
2. Registration validates application/current source/current structures/source permissions/privacy
   class, generates the credential, and atomically writes immutable registration, structure links,
   active status revision 1, and current pointer. Collision retries are bounded and no partial
   record or credential result escapes.
3. Revocation atomically appends status revision 2 `revoked` and advances the current pointer.
   Exact replay returns the revoked view; illegal reactivation or further revision is absent.
4. On the existing observation route, presence of the device credential header selects device
   authentication. The verifier lookup and current active/application checks occur before request
   body parsing. Invalid/revoked/wrong-application credentials return one generic denial.
5. After parsing, the device ingestion policy validates exact source ID/current source version,
   device instance ID, and registered structure version/hash/classification before schema/rate/store
   work. Operator submissions bypass only this device-specific binding and retain source principal
   authorization.
6. The existing ingestion transaction stores offline observed time, canonical data, device-derived
   principal evidence, and deterministic request/occurrence identity. Existing Slice 8 matching may
   later create a notification; no phone call directly creates downstream authority.

## Failure, replay, and rollback contract

Malformed device/credential IDs, credential collision, duplicate registration, unknown application,
stale/disabled/wrong source, source principal omission, missing/retired/stale/wrong-class/forbidden
structure, empty/duplicate/oversized allowlist, revoked/unknown credential, route mismatch,
instance/source/structure mismatch, expired/future evidence, duplicate conflict, rate denial,
schema failure, injected database failure, concurrent register/revoke/authenticate, or direct
database tampering creates no partial registration, status transition, observation, work, receipt,
notification, event, action, effect, or state change. Exact observation replay returns prior
evidence; exact revocation replay returns current revoked status. Credentials/verifiers are absent
from ordinary views and errors are non-enumerating.

## Implementation sequence

1. Add privacy/device/credential pure contracts and tests.
2. Extend structure persistence with immutable data classification and add guarded device records.
3. Implement internal registry, revocation, authentication, exact ingestion policy, and DI wiring.
4. Extend only the existing observation filter to select device authentication; add no route.
5. Add migration, catalog classification, focused security/concurrency/rollback/compatibility tests.
6. Run verification, inspect every artifact, write the receipt, and advance the roadmap once.

## Acceptance matrix

| Area | Required proof |
| --- | --- |
| Registration | One exact privacy-minimized source/device/structure registration returns one credential and safe readback; duplicate/collision/cross-scope inputs leave no partial rows. |
| Authentication/revocation | Current credential authenticates before parse; unknown, wrong-route, and revoked credentials share a generic denial; revocation is immediate and replay-safe. |
| Exact permission | Wrong source, source revision, instance ID, structure revision/hash, or source permission cannot submit or widen device access. |
| Offline/replay | In-window delayed evidence preserves observed time and delivers normally; expired evidence is rejected; exact occurrence/request replay creates no duplicate work/notification. |
| Privacy | Only privacy-minimized structure classification can register; raw-location/general/third-party-content defaults deny; no credential/verifier/raw transport header appears in observation/status/error. |
| Bounds/concurrency | 1–8 structures, rate/replay limits, credential collision retry, simultaneous registration/revocation/authentication, and exact identity remain bounded. |
| Rollback/security | Injected failure and EF/direct SQLite tampering cannot rewrite registration/status/provenance or partially accept evidence. |
| Compatibility | Operator observation submissions, Slice 8 matching, schedules/conditions, web routes, MCP verbs, and event authority remain unchanged. |

## Verification commands

```text
dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~PhoneCompanion|FullyQualifiedName~ObservationIngestion|FullyQualifiedName~ObservationTrigger"
dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~TriggerScheduling
dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~WebInterfaceTests|FullyQualifiedName~MigrationDriftTests|FullyQualifiedName~CatalogCoverageTests"
dotnet ef migrations has-pending-model-changes --project DantesRoleplay.DataAccess --configuration Release --no-build
dotnet build DantesRoleplay.slnx -c Release --no-restore
dotnet test DantesRoleplay.slnx -c Release --no-restore
dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj -c Release --no-restore -p:IncludeProtocolWalkTests=true --filter FullyQualifiedName~ProtocolWalkTests
dotnet run --project DantesRoleplay.Tools/DantesRoleplay.Tools.csproj -c Release --no-build -- validate catalog
git diff --check
```

## Completion receipt and exit gate

Accepted evidence will be recorded in
`platform/e8/E8-TRIGGER-SCHEDULING-SLICE-9-RECEIPT.md`. Stop after revocable phone identity can
submit privacy-minimized evidence through the existing route. Public device/source/structure
management and final web/MCP acceptance remain Slice 10.
