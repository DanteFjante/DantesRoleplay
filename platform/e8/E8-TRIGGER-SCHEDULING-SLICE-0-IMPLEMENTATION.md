# E8 trigger scheduling Slice 0 implementation — semantic and security ratification

Status: **accepted**
Owner/roadmap: `trigger-scheduling`; [platform enabling features roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)
Dependency tree/leaf: [durable scheduling and external triggers, Slice 0](E8-TRIGGER-SCHEDULING-DEPENDENCY-PLAN.md)
Ruleset alignment: **ruleset-neutral**
Source ID and locator: **not applicable**
Outcome: close the ownership, observation, endpoint, time, retry, retention, notification, and
authorization decisions required before pure contracts can be implemented.
Exclusions: runtime contracts, C# components, catalog records, schemas, routes, migrations,
database rows, workers, schedules, adapters, phone code, MCP kinds, and state-changing fire targets.
Allowed files/areas: this implementation document, its receipt, the owning dependency tree, the E8
index, and the platform roadmap status/link.
Stop point: Slice 1 is ready to author; no runtime artifact exists.

## Confirmed decisions

The user confirmed this ratification boundary by requesting implementation of Slice 0 on
2026-08-25 after reviewing the dependency plan. The following choices are closed for the first
release. A later change to one of these choices is a new semantic/public gate.

### Ownership and dependency direction

1. `trigger-scheduling` is a new ruleset-neutral system owner. It owns source registrations,
   observation structures and observations, trigger definitions, due calculation, leases, fire
   attempts/receipts, retry/misfire state, and trigger-status projections.
2. An observation or due occurrence is evidence **before** an event. It cannot write an event,
   effect, ECS value, world clock, or action result directly.
3. E8 remains the owner of routing only after an accepted event exists. This feature is downstream
   of E8 and does not add polling or time semantics to subscriptions.
4. `web-interface` owns HTTP mapping and private-host transport enforcement; `authorization` owns
   trusted principal evidence; `schema-validation` owns the bounded JSON Schema profile;
   `events-and-notifications` owns immutable notification content; and `operations-and-audit` owns
   audit evidence. None of those owners are copied into `trigger-scheduling`.
5. Notification-only is the sole initial fire target. State-changing targets remain blocked until
   a separately accepted durable delegated-authorization contract exists.

### Permanent names reserved for later slices

| Concern | Confirmed name/shape |
| --- | --- |
| System component | `trigger-scheduling` |
| HTTP route | `POST /api/applications/{applicationId}/observations` |
| Private-operator capability audit name | `trigger.observation.submit` |
| Observation ID | `observation.` plus 32 lowercase hexadecimal characters |
| Fire receipt ID | `trigger-fire.` plus 32 lowercase hexadecimal characters |
| Request ID | `observation-request.` plus 32 lowercase hexadecimal characters |
| Application-scoped authored IDs | 3–200 characters matching `^[a-z][a-z0-9-]*(\.[a-z][a-z0-9-]*)+$` |

The application-scoped ID pattern applies to source, structure, and trigger IDs; the record kind
and application scope disambiguate them. Slice 0 reserves names but creates no registry entry.

## Prerequisite evidence

| Dependency | Evidence used | Decision consequence |
| --- | --- | --- |
| E8 event routing | [Slice 1 receipt](E8-SLICE-1-RECEIPT.md) and [Slice 2 receipt](E8-SLICE-2-RECEIPT.md) | Accepted event routing is reused; observations never become an alternate event insert path. |
| Application identity | `ApplicationIdentifier` and `IApplicationRegistry` in `src/system/application-registry` | The path application is parsed and must resolve; body data cannot override it. |
| JSON Schema | `SystemJsonSchemaProfile` and `IBoundedJsonSchemaValidator` in `src/system/schema-validation` | Structure schemas use the existing offline Draft 2020-12 profile and content hashes. |
| Private web identity | `WebAccessPolicy`, `WebPrivateOperatorGuard`, and `PrivateOperatorAuthorizationPolicy` | Slice 3 initially supports loopback and configured Tailscale Serve principals only. |
| Notification evidence | `Notification`, `DeclaredNotification`, and `INotificationStore` | Content remains immutable; only delivery state keeps its existing mutable lifecycle. |
| Audit/replay | `operations-and-audit` component and existing request-token conventions | Every administration, observation acceptance, and fire terminal outcome has bounded audit evidence. |

No D&D source or Foundry implementation applies because this slice contains no game rule.

## Observation structure and source authority

- Observation **structure definitions** are application-authored, versioned records using the
  existing `system-json-schema-2020-12/v2` profile. Development fixtures belong in the authored
  catalog; an activated/running application uses the reviewed version imported into SQLite at an
  explicit synchronization boundary.
- A structure has an application ID, qualified structure ID, monotonically increasing version,
  normalized schema, schema hash, semantic description, status, and authored provenance.
- Every structure schema has an object root and `additionalProperties: false` at that root. An
  observation cannot provide an inline schema or select a different profile.
- Source registrations are runtime administrative records in SQLite because they bind real
  producers. Each immutable version names one application, source ID, transport/trust kind,
  allowed exact structure ID/versions, permitted principal or device references, replay window,
  rate limit, and enabled state. Credentials remain in a future secrets owner; only opaque secret
  references may appear here.
- Source, structure, and trigger revisions are append-only. Changing a structure or source makes a
  dependent trigger stale until explicitly reviewed against the new version.

## Closed HTTP input and response

The first ingestion contract is exactly:

```json
{
  "requestId": "observation-request.0123456789abcdef0123456789abcdef",
  "source": {
    "id": "phone.dante",
    "instanceId": "android-primary",
    "occurrenceId": "geofence-home-enter.20260825T171530Z.1"
  },
  "structure": {
    "id": "device.geofence.transition",
    "version": 1
  },
  "observedAt": "2026-08-25T17:15:30Z",
  "data": {
    "geofence": "home",
    "transition": "entered",
    "confidence": "system-geofence"
  }
}
```

All objects reject duplicate or unknown properties. `data` is required and must be a JSON object;
it is never a string containing JSON. `source.id` and `structure.id` use the application-scoped ID
pattern. `instanceId` and `occurrenceId` are 1–128 and 1–200 printable ASCII characters
respectively, limited to letters, digits, `.`, `_`, `:`, and `-`. Structure version is a positive
32-bit integer. `observedAt` is RFC 3339 UTC with `Z`; offsets and timezone-less values are rejected.

The server derives application revision, source version/trust, principal/device evidence,
structure profile/hash, source-to-structure permission, canonical data and request hashes,
`receivedAt`, observation ID, replay disposition, trigger matches, handlers, authorization,
effects/events, and safe audit projection. Supplying any server-owned field is an unknown-property
failure.

Accepted new and exact-replay submissions both return HTTP `202`:

```json
{
  "observationId": "observation.0123456789abcdef0123456789abcdef",
  "accepted": true,
  "duplicate": false,
  "status": "recorded"
}
```

An exact replay returns the same observation ID with `duplicate: true`. `status` remains
`recorded`; the endpoint never claims that matching, notification, or action execution completed.
Error bodies use the existing bounded `{ "error": "CODE", "message": "Safe text" }` shape and do
not echo source data, credentials, headers, schema bodies, or principal identifiers.

### HTTP status contract

| Status | Meaning |
| ---: | --- |
| `202` | New observation durably recorded, or exact replay returned. |
| `400` | Malformed JSON, wrong root/kind, unknown/duplicate fields, invalid ID/time, or invalid structure version. |
| `403` | Private-host authentication, capability, source-principal binding, or device authorization failed. |
| `404` | Authenticated request names an unknown application, source, or structure. |
| `409` | Request ID or producer occurrence identity was reused with a different canonical request. |
| `413` | Request or JSON resource bound exceeded. |
| `422` | `data` is well-formed but invalid against the exact registered structure schema. |
| `429` | Effective principal/source rate or concurrency bound exceeded. |
| `503` | Durable recording is temporarily unavailable; no observation was accepted. |

## Bounds, canonicalization, and replay

- Content type is `application/json`; total request UTF-8 size is at most 65,536 bytes.
- Parsed JSON depth is at most 16, total nodes at most 512, total object properties at most 256,
  array items at most 256, and any individual string's UTF-8 size at most 16,384 bytes.
- The registered structure must also pass the existing schema-profile limits. The observation
  endpoint's smaller value bounds win when the two limits differ.
- Canonical JSON sorts object keys by ordinal UTF-8 name, preserves array order, uses invariant
  JSON number formatting, and emits no insignificant whitespace. Duplicate keys, non-finite
  numbers, and values that cannot be represented by `System.Text.Json` are rejected.
- `requestId` identifies one canonical request. `(application, source version, instanceId,
  occurrenceId)` independently identifies the producer occurrence. An exact repeat through either
  key returns the original row; conflicting reuse returns `409` and writes nothing.
- The source default replay window is 24 hours and may be configured from 1 second through 7 days.
  `observedAt` may be at most 5 minutes ahead of server `receivedAt`. The caller timestamp never
  controls a lease, retry, or due decision.
- Initial HTTP throttling uses the existing private-web upload ceiling of 10 requests per minute
  with no queue, plus a maximum of 2 concurrent requests per authenticated principal. A source
  registration may lower the per-minute limit but cannot raise it in Slice 3.

## Authorization and exposure

- Slice 3 adds the closed `trigger.observation.submit` capability; it does not reuse generic
  `modify` as durable source authority.
- The initial adapter accepts only an already authenticated local-loopback or configured Tailscale
  Serve principal and then verifies that the exact source version permits that principal.
- The exact observation route is added to the private remote path allowlist. No other
  `/api/applications` path is opened by this decision.
- Tailscale transport and login headers are accepted only through the existing loopback Serve
  boundary. A direct remote client cannot self-assert those headers.
- Phone/device identity is not inferred from Tailscale identity. Slice 9 must add revocable device
  registration and proof; until then a phone can submit only as an explicitly permitted private
  operator through the existing private host.
- Source submission authority cannot create/revise sources, structures, triggers, or schedules.
  Administration surfaces require separately confirmed control capabilities in Slice 10.

## Time, timezone, recurrence, misfire, lease, and retry

- Host decisions use an injectable UTC clock returning `DateTimeOffset`; persisted instants are
  normalized UTC. SQLite ordering may use UTC `DateTime`, following existing persistence practice.
- Human schedule zones use IANA timezone identifiers, with `Europe/Stockholm` as the first
  acceptance fixture. A one-time local time in a DST gap is rejected; an ambiguous local time must
  explicitly choose `earlier` or `later` occurrence.
- Recurrence is a closed calendar form, never free cron: `daily`, `weekly`, or `monthly`; interval
  1–365; local time; IANA zone; optional start/end; weekdays only for weekly; day 1–31 only for
  monthly. A nonexistent monthly day is skipped. DST gap policy is `skip` or `next-valid` (default
  `skip`); overlap policy is `earlier` or `later` (default `earlier`).
- Initial misfire kinds are `skip` and `fire-once`. One-time schedules default to `fire-once`;
  recurring schedules default to `skip`. A `fire-once` occurrence more than 24 hours late becomes
  `missed`; multiple missed recurrence occurrences collapse to the most recent single fire.
- A deterministic occurrence key uses trigger ID, trigger version, and scheduled UTC occurrence.
  UTC clock rollback cannot repeat a terminal occurrence. A forward jump uses the misfire policy.
- Initial leases last 60 seconds. A worker may reclaim only an expired lease and must finish by
  comparing the persisted trigger/version/occurrence identity.
- Retry is fixed initially to three total attempts: immediate, then after 5 and 30 seconds. Only
  explicitly classified transient database/handler-unavailable failures retry. Malformed,
  unauthorized, stale, cancelled, schema, policy, or permanent handler failures become terminal.

These decisions describe later contract/worker slices; Slice 0 creates no clock or worker.

## Persistence, retention, and privacy

- The main generic SQLite database is the persistence owner. A separate database would make atomic
  notification/fire evidence harder without providing a first-release benefit.
- Source, structure, and trigger versions plus fire receipts and their safe audit evidence are
  retained indefinitely. Operational lease state may be compacted only after a terminal fire
  receipt exists, under a future explicit retention operation.
- Canonical observation data is retained for 90 days by default. After that it is eligible for an
  explicit audited redaction operation that preserves observation ID, canonical hash, source and
  structure versions, observed/received times, replay identity hash, match/fire links, and redaction
  time. No automatic purge is introduced before that operation is implemented and confirmed.
- Safe views never expose source credentials, raw transport headers, full principal/device IDs, or
  raw request bodies. Instance and occurrence IDs are returned only to authorized administration
  views and otherwise projected as hashes.
- Phone geofence sources default to semantic zone/transition data and never raw coordinates. Raw
  GPS and third-party notification contents require separate source profiles, retention, redaction,
  and permission confirmation.

## Notification contract and transaction ownership

- A trigger definition may contain a versioned notification-only target: bounded topic, subject,
  body, and declared application/entity links. External `data` cannot select or rewrite these
  fields at ingestion or fire time.
- `events-and-notifications` will expose a narrow internal immutable-notification writer in Slice 5.
  It is not a public arbitrary notification API and does not add an event.
- The trigger fire transaction owns the notification-only root: it validates current trigger and
  links, writes exactly one immutable notification through that port, writes the terminal fire
  receipt/audit evidence, and commits them atomically. Failure leaves neither row.
- Existing notification topic/subject/body/correlation/event/execution evidence remains immutable;
  existing unread/read/archive delivery state is unchanged.
- `trigger-scheduling` separately projects `scheduled`, `due`, `completed`, `cancelled`, `missed`,
  or `superseded` from authoritative trigger/fire state. It never changes historical notification
  content. A material update creates a new notification instead of rewriting an old one.

## Failure, replay, and rollback contract

Malformed, oversized, unauthorized, wrong-application, unknown/disabled/stale source, unknown/stale
structure, schema-invalid, late/future, rate-limited, conflicting replay, cancelled trigger, expired
lease, stale application revision, and injected persistence failures produce no partial trigger,
action, event, notification, or success receipt. Denials may write only the existing bounded safe
authorization/operation evidence when that owner permits it. Exact observation and fire replay
returns prior evidence without repeating matching or delivery.

## Implementation sequence

1. Slice 1: author only pure contracts, ID/bound/time validators, canonicalization, fingerprints,
   fake clock, and deterministic one-time evaluation.
2. Slice 2: add reviewed SQLite persistence and migrations after its separate confirmation.
3. Slice 3: add the exact private observation endpoint and security tests.
4. Slice 4: add the one-time worker/lease/retry/misfire runtime.
5. Slice 5: add the atomic notification-only target and status projection.
6. Later slices add recurrence, world/state conditions, adapters, phone identity, and management.

No later slice may be folded into Slice 1.

## Acceptance matrix

| Area | Slice 0 proof |
| --- | --- |
| Ownership | Every state/effect/time/authorization/notification concern has one owner and dependency direction. |
| API | Exact route, request, response, status, source/structure/data, bounds, replay, and server-derived fields are closed. |
| Security | Private-host capability, source-principal binding, rate/concurrency limits, phone identity deferral, and no ambient action authority are explicit. |
| Time | UTC, IANA zones, DST, recurrence, misfire, lease, retry, restart, and clock-jump semantics are closed. |
| Evidence | Main-database ownership, append-only versions/receipts, 90-day payload-redaction eligibility, and safe projections are explicit. |
| Notifications | Immutable content, derived trigger status, atomic notification/fire transaction, and no direct event are explicit. |
| Compatibility | E8, application actions/effects/events, world-clock ownership, current notification reads, three MCP verbs, and disabled-feature behavior are unchanged. |
| Stop | Repository contains no runtime artifact from this slice; Slice 1 is independently authorable. |

## Verification commands

Slice 0 requires document-only verification:

```text
git diff --check
rg -n "Slice 0|Slice 1|POST /api/applications/.*/observations|trigger.observation.submit" platform/e8 platform/PLATFORM-ENABLING-FEATURES-ROADMAP.md
```

No build, catalog validation, migration test, full suite, or protocol walk is claimed because no
runtime, catalog, database, MCP, or dependency-registration artifact changes.

## Completion receipt and exit gate

Completion is recorded in
[E8-TRIGGER-SCHEDULING-SLICE-0-RECEIPT.md](E8-TRIGGER-SCHEDULING-SLICE-0-RECEIPT.md).
The exit gate is met when the dependency tree marks Slice 0 accepted, all confirmation gates map to
the decisions above, Slice 1 is the lowest ready leaf, and document diff checks pass. Stop before
creating `src/system/trigger-scheduling`, a route, schema, migration, or database row.
