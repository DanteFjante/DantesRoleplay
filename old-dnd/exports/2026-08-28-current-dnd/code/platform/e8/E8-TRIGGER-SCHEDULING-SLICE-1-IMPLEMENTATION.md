# E8 trigger scheduling Slice 1 implementation — pure contracts and one-time evaluation

Status: **accepted 2026-08-25**
Owner/roadmap: `trigger-scheduling`; [platform enabling features roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)
Dependency tree/leaf: [durable scheduling and external triggers, Slice 1](E8-TRIGGER-SCHEDULING-DEPENDENCY-PLAN.md)
Ruleset alignment: **ruleset-neutral**
Source ID and locator: **not applicable**

## Outcome and boundary

Create the pure, persistence-free contracts that later slices use to validate an observation,
canonicalize its object data, fingerprint it deterministically, represent one versioned source and
structure, and decide whether a single UTC one-time notification-only trigger is pending, due, or
missed. Add an injectable fake UTC clock for deterministic tests.

Included: the `trigger-scheduling` component declaration, domain contracts/validators, canonical
JSON/data bounds, schema-hash checks, source/structure compatibility, observation admission checks,
one-time trigger/misfire evaluation, stable fire IDs, and focused tests.

Excluded: SQLite/EF models or migrations; source/structure/trigger persistence; HTTP/MCP routes;
authentication implementations; rate-limit registration; hosted worker/leases/retries; schema
compilation; recurrence/timezone/DST execution; notification writes/status projections; adapters;
phone code; actions/effects/events; and any state mutation.

Allowed files/areas: `src/system/trigger-scheduling/component.json`, its `domain/` and `tests/`
files, this implementation document, the dependency tree, and the Slice 1 receipt. No existing
component contract changes are allowed.

Stop point: pure values and tests compile in the shared core/test assemblies. Stop before
persistence or public-surface registration.

## Confirmed decisions used

Slice 0 ratified the application-scoped dotted ID pattern, object-root data, 64 KiB/16-depth/512
node/256-property/256-array-item/16 KiB-string bounds, exact source/structure version matching,
UTC observed-time replay window, and one-time `skip`/`fire-once` behavior. This slice consumes those
decisions; it neither revises them nor creates the endpoint that will enforce transport limits.

## Prerequisite evidence

| Concern | Existing owner/evidence | Slice 1 use |
| --- | --- | --- |
| Application identity | `DantesRoleplay.Applications.ApplicationIdentifier` | Application scope is an opaque parsed value. |
| Bounded schema profile | `DantesRoleplay.SchemaValidation.SystemJsonSchemaProfile` | Structures retain the accepted profile ID/schema/hash; this slice does not compile schemas. |
| E8 event ownership | E8 Slices 1–2 receipts | Contracts contain no event insertion capability. |
| Notification ownership | `DantesRoleplay.Notifications.Notification` | Trigger target is a closed `NotificationOnly` marker; no notification is created. |

No D&D source or Foundry reference applies.

## Runtime artifacts

| Artifact | Status in this slice |
| --- | --- |
| Component `trigger-scheduling` | New declaration; pure domain owner only. |
| `TriggerSchedulingContracts.cs` | New domain contract, validation, canonicalization, fingerprint, and evaluation seam. |
| `TriggerSchedulingContractsTests.cs` | New focused contract/evaluation tests. |
| Persistence, endpoint, worker, notification writer | Explicitly absent. |

## Authoritative state and closed input

`ObservationSubmission` accepts only a Slice-0-shaped request expressed as typed values:
request ID; source ID/instance/occurrence; structure ID/version; UTC observed time; and canonical
object data. It does not accept application revision, source revision, authorization evidence,
schema content/hash, receive time, observation ID, trigger selection, or effects.

`ObservationStructureDefinition` carries a validated normalized object-root schema, its existing
profile ID, and SHA-256 hash. `ObservationSourceDefinition` carries one application, source version,
enabled status, exact allowed structure versions, a bounded replay window, and a rate ceiling. The
admission evaluator receives these trusted definitions and an injectable current UTC time; it never
queries storage or accepts caller-supplied authorization.

`OneTimeTriggerDefinition` accepts only a UTC due instant, immutable version, closed
`skip` or `fire-once` misfire policy, and the closed `NotificationOnly` target. It does not accept a
handler, action payload, event type, entity selection, or rule mechanic.

## Behavior, result, and typed effects

Canonicalization rejects non-object data, duplicate JSON property names, invalid JSON, out-of-bound
resource use, non-finite/unrepresentable numbers, and disallowed strings. It sorts object keys by
ordinal name, preserves array order, writes stable JSON, and supplies a SHA-256 fingerprint.

Admission checks application/source scope, enabled state, exact structure allowlist/version,
structure profile/hash shape, observed-time future skew and replay window. It returns a trusted
canonical observation projection plus a stable request fingerprint; replay conflict and database
idempotency remain Slice 2 ownership.

One-time evaluation returns `pending`, `due`, or `missed` and a deterministic fire ID derived from
the trigger ID, version, and scheduled UTC instant. `skip` marks any late occurrence missed;
`fire-once` is due through the confirmed 24-hour lateness window and missed after it. The fake clock
only supplies UTC time. No method returns an effect, action, event, notification, or database write.

## Failure, replay, and rollback contract

All malformed IDs, invalid versions/times, duplicate keys, invalid data bounds, bad schema hash,
source/structure mismatch, disabled sources, stale/future observations, and unsupported target or
misfire kind throw a closed `TriggerSchedulingContractException` code before producing a result.
This pure slice has no transaction and no durable state. The same inputs always canonicalize,
fingerprint, and evaluate identically; exact persistence replay belongs to Slice 2.

## Implementation sequence

1. Declare the component and pure domain values/helpers.
2. Add focused tests for bounds, canonicalization, source/structure admission, fingerprints, fake
   clock, one-time due/missed cases, and forbidden non-notification targets.
3. Run focused tests and shared build. Update the dependency tree and write a receipt only after
   the evidence passes.

## Acceptance matrix

| Area | Required proof |
| --- | --- |
| IDs/contracts | Invalid IDs, versions, profile/hash, scope, enabled state, and source allowlist fail with stable codes. |
| Data | Key-order variants canonicalize/fingerprint identically; duplicate, scalar root, deep, large, property/array/string, and unrepresentable numeric values fail. |
| Time | UTC only, 5-minute future boundary, source replay-window boundary, fake-clock advance, and exact instant behavior are deterministic. |
| One-time | Pending, exact due, `skip`, in-window `fire-once`, late `fire-once`, version change, and deterministic fire ID are asserted. |
| Isolation | No EF/database, HTTP, hosted-service, event, action/effect, notification-store, or catalog fixture dependency exists. |
| Compatibility | Existing system component manifests, schemas, actions, E8 routing, notifications, and MCP surface are unchanged. |

## Verification commands

```text
dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore --filter FullyQualifiedName~TriggerScheduling
dotnet build DantesRoleplay.slnx --no-restore
git diff --check
```

## Completion receipt and exit gate

Completion is recorded in [Slice 1 receipt](E8-TRIGGER-SCHEDULING-SLICE-1-RECEIPT.md). Slice 2 is
next but remains at its migration-confirmation gate. Stop before any persistence or public-surface
work.
