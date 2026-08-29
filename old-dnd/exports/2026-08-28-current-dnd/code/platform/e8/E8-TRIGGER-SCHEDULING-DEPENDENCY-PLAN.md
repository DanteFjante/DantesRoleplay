# E8 downstream dependency tree — durable scheduling and external triggers

Status: **Slices 0–10 accepted; downstream notification-only trigger plan complete**
Ruleset alignment: **ruleset-neutral**
Source: **not applicable**
Owning roadmap: [Platform enabling features roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)
Upstream event owner: [E8 dynamic event roles and fan-out](E8-DEPENDENCY-PLAN.md)
Latest receipt: [Slice 10 web/MCP management and final acceptance](E8-TRIGGER-SCHEDULING-SLICE-10-RECEIPT.md)

## Outcome and non-goals

Deliver a generic trigger service in which applications can register bounded schedules and
conditions, authenticated sources can submit schema-valid observations, and a durable worker can
turn one due trigger into an authorized application request with idempotent evidence. The normal
action/effect/event pipeline remains the only owner of world mutation and event-ledger truth.

The intended outcomes include:

1. one-time and recurring real-time schedules with explicit timezone and missed-fire policy;
2. application world-clock thresholds without automatically advancing the world clock;
3. declared state conditions evaluated only when their exact dependencies change;
4. registered polling, webhook, device, or coded source adapters producing typed observations;
5. a private HTTP observation endpoint accepting a source, registered data-structure reference,
   observation time, stable occurrence identity, and bounded object-root data;
6. notification-only reminders as the first useful fire target;
7. later application actions only through an explicit durable delegated-authorization boundary;
8. append-only observations and fire receipts with deterministic replay/idempotency behavior; and
9. phone companion sources that can submit privacy-minimized signals such as `home.entered`
   without uploading continuous location history.

This plan does **not**:

- let a scheduler, listener, phone, feed, or model insert an event-ledger row directly;
- let external data supply effects, mechanic results, authorization, action validity, SQL, JSON
  paths, code, shell commands, network destinations, or catalog identifiers to execute;
- run arbitrary code uploaded from the website or contained in an observation;
- continuously scan all ECS JSON or evaluate an unrestricted predicate/query language;
- make E8 subscriptions into a background scheduler or change their atomic routing semantics;
- automatically advance an application-owned world clock from wall time;
- make notifications editable evidence or promise push delivery in the first slice;
- guarantee exact real-time firing while the local host is stopped, asleep, or disconnected;
- store raw phone GPS or third-party notification contents by default; or
- authorize runtime implementation, permanent IDs, migrations, routes, or public kinds from this
  dependency plan alone.

## Existing owners and evidence

| Concern | Owner | State | Evidence/constraint |
| --- | --- | --- | --- |
| Accepted internal events, guards, reactions, chains | `events-and-notifications` | verified | E8 Slices 1–2 receipts and current router/chain tests; routing begins only after a proposal exists. |
| Dynamic event role and bounded fan-out | E8 | verified | [Slice 1 receipt](E8-SLICE-1-RECEIPT.md) and [Slice 2 receipt](E8-SLICE-2-RECEIPT.md). |
| World mutation and structural event production | effects/actions | verified | Effects are validated, guarded, recorded, reacted to, and committed in one transaction. |
| World clock meaning/advance | application catalog mechanic | verified application owner | `mechanic.game.core.world.clock.advance` advances one application-owned clock; the platform must not advance it implicitly. |
| Durable notifications | events-and-notifications | verified, immutable/pull-only | Reaction-created notifications are immutable except delivery state and wait for queries. |
| Application scope/schema registry | application kernel | verified foundation | Registered applications and versioned component schemas exist; observation structures require their own owner rather than becoming component types accidentally. |
| Trusted private web identity | E9/private web adapter | verified local profile | Local and allowlisted Tailscale browser access exists; device-source identity and durable delegated action authority do not. |
| Background scheduler, sources, observations, notification/condition consumers | `trigger-scheduling` | accepted through Slice 10 | Slices 2–10 provide immutable registrations/evidence, private ingestion, durable time/application-state/observation work, reviewed closed matchers, revocable phone identity, privacy-minimized permissions, leases/retries, exact dependencies, atomic notification consumers, current status projections, and shared web/MCP administration. |
| External network/feed credentials | none | missing | Secrets and outbound allowlists must be selected before a poller slice becomes active. |
| Phone companion protocol | `trigger-scheduling` | accepted through Slice 10 | Revocable device registration, exact privacy-minimized permissions, offline replay, credential authentication, and private operator management are implemented. |

## Ownership boundary

The confirmed generic component label is `trigger-scheduling`.

```text
trigger-scheduling
  owns: source registrations, observation structures, observation ledger,
        trigger definitions/notification targets, due-time calculation, leases, fire receipts,
        retry/misfire state, scheduled-notification provenance, current trigger status
  depends on: application registry, authorization port, clock abstraction,
              operations/audit, notification/application-request ports
  does not own: ECS state, world clocks, mechanics, events, subscriptions,
                notification content mutation, external secrets, or phone OS APIs

events-and-notifications
  owns: accepted event ledger, guards, reactions, chain budget, reaction notifications
  does not poll clocks, feeds, devices, or trigger tables

application adapter
  owns: meaning of `soft-session-ending`, `home.entered`, feed fields,
        state conditions, handler selection, and any game-specific prompt/narration
```

An external or scheduled occurrence is first an **observation** or **due fire**, not an event. The
trigger service records it and asks a declared application handler to act. Only the handler's
accepted effects and reactions can produce event-ledger rows.

## Closed terminology

- **Source registration** — versioned host record describing one allowed producer, application,
  transport/adapter kind, trust class, structure allowlist, rate limits, and enabled state.
- **Observation structure** — versioned object-root JSON Schema and semantic description for one
  source data shape. It validates evidence; it grants no behavior.
- **Observation** — immutable authenticated input with source, structure/version/hash, occurrence
  identity, observed/received times, canonical data, and provenance.
- **Trigger definition** — versioned closed match and fire policy owned by one application.
- **Schedule** — a trigger whose eligibility is derived from real time or application world time.
- **Condition trigger** — a trigger evaluated from declared indexed dependencies when those
  dependencies change, never by free database polling.
- **Fire attempt** — one leased, idempotent attempt to handle an eligible trigger occurrence.
- **Fire receipt** — append-only evidence of eligibility, authorization, handler request, outcome,
  retry/misfire decision, and linked operation/event/notification IDs.
- **Misfire** — a due time that passed while no worker could process it.

## Confirmed external observation API contract

The confirmed initial route is:

```text
POST /api/applications/{applicationId}/observations
```

The route and envelope are ratified by
[Slice 0](E8-TRIGGER-SCHEDULING-SLICE-0-IMPLEMENTATION.md). It is private-host/Tailscale-only
initially and is not added to the MCP three-verb surface automatically.

### Request

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

`data` is an object, not a string. `structure` identifies a pre-registered versioned schema; the
ingestion call does not accept a new inline schema. Structure registration/revision is a separate
authorized administrative operation so one observation can always be replayed against the exact
schema that originally accepted it.

### Server-derived values

The server derives and callers cannot supply:

- application authorization and effective application revision;
- canonical source registration/version/trust class and enabled state;
- authenticated principal/device evidence and transport identity;
- structure content hash and source-to-structure permission;
- canonical payload hash, receive timestamp, observation ID, and replay verdict;
- trigger matches, handler/action selection, authorization, effects, event claims, and outcome; and
- safe/redacted audit projection.

### Validation and limits

- Require `application/json`, an object root, exact known properties, and no duplicate JSON keys.
- Bound the request to 64 KiB, depth 16, 512 JSON nodes, 256 object properties, 256 array items,
  and 16 KiB per string as confirmed by Slice 0. Existing schema-profile bounds also apply and the
  smaller endpoint limit wins.
- `requestId` is caller idempotency for one intended submission. `(source registration,
  instanceId, occurrenceId)` is the producer occurrence identity. Exact replay returns the prior
  observation; conflicting reuse returns `409` and creates no row.
- `observedAt` must be valid UTC and inside the source's configured replay window. The server keeps
  its own `receivedAt`; a caller timestamp never controls leases or due-time calculations.
- Validate `data` against the exact active structure version and persist canonical data plus the
  accepted schema hash.
- Authenticate before parsing expensive payload/schema work. Apply per-source and per-principal
  rate/concurrency limits.
- Unknown/disabled source, wrong application/structure, invalid signature/device, stale schema,
  malformed data, future/expired observation, duplicate conflict, or authorization failure makes
  no trigger, action, event, or notification change.

### Response

The confirmed accepted response is `202` with:

```json
{
  "observationId": "observation.0123456789abcdef0123456789abcdef",
  "accepted": true,
  "duplicate": false,
  "status": "recorded"
}
```

An exact replay returns the same ID with `duplicate: true`. The response does not claim a trigger
fired or an action succeeded; those have separate durable receipts and may occur asynchronously.

## Trigger kinds

### Real-time schedule

- One-time local wall time plus an explicit timezone, normalized to an unambiguous UTC instant.
- Later recurrence uses a closed calendar shape, not free cron text initially.
- Explicit DST gap/overlap policy, start/end bounds, and next-fire derivation.
- Misfire policy is one of `skip`, `fire-once`, or bounded `catch-up`; first delivery recommends
  only `skip` and `fire-once`.

### Application world-clock threshold

- References one registered application/state space and one declared clock projection.
- Evaluated when an accepted application action changes that clock, never from wall-time polling.
- Does not advance the clock and cannot reinterpret its units.

### Declared state condition

- Names exact application projection/component dependencies and a closed comparison owned by the
  application adapter.
- Evaluated only after a committed dependency change.
- Supports edge semantics (`false -> true`) separately from level semantics (`while true`), with
  explicit re-arm behavior.
- No arbitrary JSONPath, JavaScript, SQL, cross-application query, or unbounded entity scan.

### External observation

- Matches one registered source and structure, then bounded exact scalar fields declared by the
  trigger version.
- Raw `data` cannot choose the handler or supply an action payload outside the trigger's declared
  typed slot mapping.
- Source/structure revision changes make dependent trigger definitions stale until reviewed.

## Fire targets and authorization

The first useful target is **notification-only**. A schedule such as “soft ending at 23:00” can
produce an immutable reminder linked to the application/session/trigger/fire receipt without
changing world state.

State-changing targets remain blocked until the system has a durable delegated-authorization
contract that answers:

- whose authority is exercised after the creator is offline;
- which exact application capability, scope, handler, and parameter slots were granted;
- expiry, revocation, policy revision, and current-authorization revalidation;
- whether a model-authored schedule requires explicit human confirmation; and
- how a fire that becomes unauthorized is recorded without mutation.

No trigger stores a generic bearer token or replays a browser session. At fire time the handler,
current contracts, application revision, source/structure revision, trigger version, authorization,
and target state are revalidated.

## Notification update model

Existing notification content remains immutable. “Updating a notification” is split into:

1. an immutable notification linked to its trigger/fire and subject entities;
2. a current derived status projection such as `scheduled`, `due`, `completed`, `cancelled`,
   `missed`, or `superseded`; and
3. an optional new notification when a material change should alert the person again.

The UI may render current linked state beside the original text. It must not rewrite the original
subject/body or make a historical reminder appear to have said something else.

## Worker, leases, retries, and time

- Use an injectable UTC clock for all host due-time decisions and deterministic tests.
- Persist trigger next-fire state and fire attempts in the main generic SQLite database.
- One bounded hosted worker claims due rows through expiring leases. Restart may recover an expired
  lease; simultaneous workers cannot execute the same fire twice.
- A deterministic fire idempotency key binds trigger ID/version and occurrence. Retry returns or
  completes the same fire receipt.
- Retry only classified transient failures with bounded attempts/backoff. Validation,
  authorization, stale-contract, and permanent handler failures do not spin.
- Server shutdown, sleep, clock rollback/forward, timezone revision, DST transitions, and database
  lock contention have explicit tests and misfire evidence.
- If the local host is stopped, exact real-time delivery is impossible. `fire-once` may catch up on
  restart; a companion app may separately schedule a local device notification.

## External adapters and phone companion

- A coded listener is a reviewed host adapter registered at startup, not source text uploaded at
  runtime. Polling adapters use explicit outbound destinations, credentials from a selected secret
  owner, timeouts, response-size limits, and source-specific schemas.
- Webhook/device adapters call the observation endpoint. Tailscale is transport privacy, not the
  complete device identity or replay defense.
- A phone source has a revocable device registration and source-specific occurrence IDs. Offline
  observations preserve their device-observed time but are matched only if within the replay
  window.
- Prefer phone-side geofence evaluation and a small `home.entered` observation over continuous raw
  GPS upload. Raw location requires a separate explicit data-retention and visibility decision.
- Reading other applications' phone notifications is platform/permission dependent and must use a
  source-specific allowlist and redaction policy. No generic “forward every notification” source.

## Dependency tree

```text
Durable scheduling and external triggers                                  [Slices 0–10 accepted]
├─ A. Accepted event/action/notification foundations                       [verified]
│  ├─ E8 dynamic role binding and bounded fan-out                          [accepted]
│  ├─ action/effect transaction and structural events                      [verified]
│  └─ immutable pull notification store                                    [verified]
├─ B. Threat model and semantic ratification                               [accepted Slice 0]
│  ├─ trigger-scheduling owner/dependency direction                        [confirmed]
│  ├─ observation versus event boundary                                    [confirmed]
│  ├─ time/timezone/misfire/retry semantics                                [confirmed]
│  ├─ notification status projection                                       [confirmed]
│  └─ delegated action authority                                           [missing; notification-only first]
├─ C. Pure contracts and deterministic evaluator                           [accepted Slice 1]
│  ├─ sources, structures, observations, triggers, fires                   [implemented]
│  ├─ canonical fingerprints/idempotency                                   [implemented]
│  └─ fake clock and pure next-fire evaluation                             [implemented]
├─ D. Persistence and worker                                                [Slices 2–4 accepted]
│  ├─ append-only observation/fire records                                 [accepted and hardened Slice 2A]
│  ├─ trigger/source/structure versions and current pointers               [accepted Slice 2A]
│  ├─ lease/retry/misfire state                                             [accepted Slice 4]
│  └─ bounded due worker and restart recovery                              [accepted Slice 4]
├─ E. Private observation ingestion                                        [accepted Slice 3]
│  ├─ authenticated application/source endpoint                            [implemented]
│  ├─ schema validation, replay, and rate bounds                           [implemented]
│  └─ safe receipt/readback                                                 [implemented]
├─ F. Trigger consumers                                                     [Slice 5 accepted; later consumers planned]
│  ├─ one-time reminder notification                                       [accepted Slice 5]
│  ├─ recurring real-time schedules                                        [accepted Slice 6]
│  ├─ world-clock threshold                                                 [accepted Slice 7]
│  ├─ declared state transition                                             [accepted Slice 7]
│  └─ external observation match                                            [accepted Slice 8]
├─ G. External adapters and companion protocol                              [depends on E/F]
│  ├─ reviewed coded adapter interface                                      [accepted Slice 8; no outbound ports]
│  ├─ outbound poller/webhook security                                      [secret/network gate]
│  └─ phone device/geofence source                                          [accepted Slice 9 internal boundary]
├─ H. Authorized application actions                                        [blocked]
│  └─ durable scoped delegated authority                                    [missing owner]
└─ I. Web/MCP management and acceptance                                     [accepted Slice 10]
   ├─ schedule/source/structure/observation/fire administration              [implemented]
   ├─ dynamic notification status display                                   [implemented]
   └─ restart/replay/security/full-suite/protocol evidence                   [accepted]
```

## Ordered slices and model routing

| Order | Slice | Default implementation model | Exit gate |
| ---: | --- | --- | --- |
| 0 | [Ratify owner, observation/event boundary, IDs, time/timezone/DST, misfire, retry, retention, notification projection, endpoint/authentication, and notification-only first target](E8-TRIGGER-SCHEDULING-SLICE-0-IMPLEMENTATION.md) | **Accepted** ([receipt](E8-TRIGGER-SCHEDULING-SLICE-0-RECEIPT.md)) | Decisions recorded; no runtime changes. |
| 1 | [Pure source/structure/observation/trigger/fire contracts, limits, canonical fingerprints, fake clock, and deterministic once-at evaluation](E8-TRIGGER-SCHEDULING-SLICE-1-IMPLEMENTATION.md) | **Accepted** ([receipt](E8-TRIGGER-SCHEDULING-SLICE-1-RECEIPT.md)) | Pure tests cover bounds, time, replay admission, stale revisions, and forbidden fields; no persistence/public route. |
| 2 | [Versioned source/structure/trigger persistence plus append-only observations/fire receipts](E8-TRIGGER-SCHEDULING-SLICE-2-IMPLEMENTATION.md) | **Accepted** ([receipt](E8-TRIGGER-SCHEDULING-SLICE-2-RECEIPT.md)) | Migration/store tests prove immutable revision history, exact historical references, idempotency, conflict, and rollback. |
| 2A | [Persistence security hardening](E8-TRIGGER-SCHEDULING-SLICE-2A-IMPLEMENTATION.md) | **Accepted** ([receipt](E8-TRIGGER-SCHEDULING-SLICE-2A-RECEIPT.md)) | Trusted-clock recomputation, current-revision revocation, atomic replay handling, database immutability, permission FK, and exact time precision are proven. |
| 3 | [Private application-scoped observation HTTP endpoint](E8-TRIGGER-SCHEDULING-SLICE-3-IMPLEMENTATION.md) | **Accepted** ([receipt](E8-TRIGGER-SCHEDULING-SLICE-3-RECEIPT.md)) | Exact requests authenticate before parsing, validate, deduplicate, rate-limit, persist principal-bound evidence, and return safe receipts; hostile/invalid requests make no downstream change. |
| 4 | [Durable one-time scheduler, leases, retry classification, `skip`/`fire-once`, and restart recovery](E8-TRIGGER-SCHEDULING-SLICE-4-IMPLEMENTATION.md) | **Accepted** ([receipt](E8-TRIGGER-SCHEDULING-SLICE-4-RECEIPT.md)) | Fake-clock and multi-worker tests prove one fire, no double handling, bounded retries, sleep/clock-jump behavior, transactional participant rollback, and deterministic receipts. |
| 5 | [Notification-only reminder target and current status projection](E8-TRIGGER-SCHEDULING-SLICE-5-IMPLEMENTATION.md) | **Accepted** ([receipt](E8-TRIGGER-SCHEDULING-SLICE-5-RECEIPT.md)) | A 23:00 session reminder appears once, original content remains immutable, all six statuses derive correctly, and cancel/reschedule/replay are tested. |
| 6 | [Closed recurrence with timezone/DST policies](E8-TRIGGER-SCHEDULING-SLICE-6-IMPLEMENTATION.md) | **Accepted** ([receipt](E8-TRIGGER-SCHEDULING-SLICE-6-RECEIPT.md)) | Daily/weekly/monthly bounds, exact DST resolution, deterministic next/latest fire, pause/resume/cancel, collapsed catch-up, retries, concurrency, and tamper guards pass. |
| 7 | [World-clock threshold and declared state-transition triggers](E8-TRIGGER-SCHEDULING-SLICE-7-IMPLEMENTATION.md) | **Accepted** ([receipt](E8-TRIGGER-SCHEDULING-SLICE-7-RECEIPT.md)) | Only declared changed dependencies evaluate; no automatic clock advance, JSON scan, cross-scope leak, or repeated level fire. |
| 8 | [External observation matching and reviewed coded-adapter interface](E8-TRIGGER-SCHEDULING-SLICE-8-IMPLEMENTATION.md) | **Accepted** ([receipt](E8-TRIGGER-SCHEDULING-SLICE-8-RECEIPT.md)) | Exact source/structure revision matching, closed scalars, bounded durable work, adapter failure, network/secret boundary, injection, rollback, and concurrency tests pass. |
| 9 | [Phone companion registration and privacy-minimized observations](E8-TRIGGER-SCHEDULING-SLICE-9-IMPLEMENTATION.md) | **Accepted** ([receipt](E8-TRIGGER-SCHEDULING-SLICE-9-RECEIPT.md)) | Revocation, offline replay window, duplicate evidence, exact permission denial, credential collision rollback, migration tamper guards, and no-raw-GPS default pass. |
| 10 | [Web/MCP management and final acceptance](E8-TRIGGER-SCHEDULING-SLICE-10-IMPLEMENTATION.md) | **Accepted** ([receipt](E8-TRIGGER-SCHEDULING-SLICE-10-RECEIPT.md)) | Shared private web/MCP administration, safe projections, exact preview/commit/replay, one-time pairing secret, control-center UI, and compatibility/security evidence are accepted. |
| Later | State-changing scheduled/external actions | **Blocked pending durable delegated authorization** | Current authorization is revalidated at fire time; expiry/revocation/scope/replay tests prove no ambient authority. |

Slices 0–5 form the smallest useful release; Slice 6 adds durable closed-calendar recurrence,
Slice 7 adds exact application-state conditions, Slice 8 adds exact external-observation matching,
and Slice 9 adds revocable privacy-minimized phone identity without outbound authority.
Later slices do not block one-time or recurring notification reminders.
Only one slice becomes active at a time and receives its own implementation document and receipt.

## Acceptance matrix

| Class | Required evidence |
| --- | --- |
| Positive | One exact observation and one due schedule each produce one receipt; the accepted reminder produces one notification. |
| Negative/no-change | Malformed, unauthorized, stale, late, future, unknown-source/structure, schema-invalid, disabled, cancelled, and rate-limited input changes no trigger/action/event/notification state. |
| Event authority | No API, scheduler, adapter, or observation writes an event directly; only accepted application actions/effects do. |
| Determinism | Canonical data/fingerprints, next-fire calculation, matches, leases, recurrence, and status projections are stable. |
| Replay | Exact request/occurrence/fire replay returns prior evidence; conflicting reuse is rejected; no duplicate notification/action. |
| Time | UTC, timezone, DST gap/overlap, clock jumps, sleep, restart, and misfires have explicit outcomes. |
| Concurrency | Two workers and repeated submissions produce one accepted observation/fire and bounded safe retries. |
| Privacy | Safe projections omit credentials, raw transport headers, unnecessary device identity, raw GPS, and unapproved phone notification content. |
| Authorization | Source submission and schedule administration are distinct; a source cannot create triggers; notification-only cannot mutate world state. |
| Compatibility | Existing direct effects/actions, E8 subscriptions/chains, notification queries, web routes, and three MCP verbs remain unchanged when trigger scheduling is disabled. |

## Confirmation gates

| Gate | State after Slice 0 |
| --- | --- |
| Separate `trigger-scheduling` owner downstream of E8 | **Confirmed** |
| Permanent ID shapes, exact observation route/envelope/response/bounds/rates | **Confirmed for Slices 1–3** |
| Catalog-authored structure plus activated SQLite version; runtime source registration | **Confirmed** |
| Main-database ownership and retention/redaction semantics | **Confirmed; Slice 2 migration accepted; retention/redaction operation remains future work** |
| UTC/IANA timezone, DST, recurrence, retry, lease, and misfire semantics | **Confirmed** |
| Immutable notification plus trigger-owned current status projection | **Implemented and accepted Slice 5** |
| Private loopback/Tailscale principal submission and separate future device identity | **Confirmed** |
| Outbound secrets/network owner before polling | **Still gated; no poller is authorized** |
| Notification-only first; durable delegated authority before state changes | **Confirmed; state-changing targets remain blocked** |
| Raw GPS/phone-notification source profiles | **Still gated; excluded from default companion data** |
| Final public administration and feature acceptance | **Implemented and accepted Slice 10** |

## Next leaf and gate

The downstream notification-only trigger plan is complete through Slice 10. State-changing
scheduled or external actions remain blocked until durable scoped delegated authorization has a
separate confirmed owner and expiry, revocation, scope, and replay evidence. Outbound polling,
raw-location profiles, forwarded phone notifications, push delivery, retention execution, and a
phone application likewise remain separate future decisions.

## Planning receipt

- E8 routing implementation status changed: none; downstream notification-only trigger scheduling
  Slices 0–10 are accepted and this dependency plan is complete.
- Slice 0 status: accepted by
  [implementation document](E8-TRIGGER-SCHEDULING-SLICE-0-IMPLEMENTATION.md) and
  [receipt](E8-TRIGGER-SCHEDULING-SLICE-0-RECEIPT.md).
- Slice 1 status: accepted by
  [implementation document](E8-TRIGGER-SCHEDULING-SLICE-1-IMPLEMENTATION.md) and
  [receipt](E8-TRIGGER-SCHEDULING-SLICE-1-RECEIPT.md).
- Confirmed owner: durable trigger scheduling and external observations, downstream of E8.
- Requested endpoint represented: application-scoped observation ingestion with `source`,
  registered `structure`, object-root `data`, occurrence identity, timestamps, authentication,
  idempotency, and safe receipt.
- Slice 2 status: accepted by its [implementation document](E8-TRIGGER-SCHEDULING-SLICE-2-IMPLEMENTATION.md)
  and [receipt](E8-TRIGGER-SCHEDULING-SLICE-2-RECEIPT.md).
- Slice 2A status: accepted by its [implementation document](E8-TRIGGER-SCHEDULING-SLICE-2A-IMPLEMENTATION.md)
  and [receipt](E8-TRIGGER-SCHEDULING-SLICE-2A-RECEIPT.md).
- Slice 3 status: accepted by its [implementation document](E8-TRIGGER-SCHEDULING-SLICE-3-IMPLEMENTATION.md)
  and [receipt](E8-TRIGGER-SCHEDULING-SLICE-3-RECEIPT.md).
- Slice 4 status: accepted by its [implementation document](E8-TRIGGER-SCHEDULING-SLICE-4-IMPLEMENTATION.md)
  and [receipt](E8-TRIGGER-SCHEDULING-SLICE-4-RECEIPT.md).
- Slice 5 status: accepted by its [implementation document](E8-TRIGGER-SCHEDULING-SLICE-5-IMPLEMENTATION.md)
  and [receipt](E8-TRIGGER-SCHEDULING-SLICE-5-RECEIPT.md).
- Slice 6 status: accepted by its [implementation document](E8-TRIGGER-SCHEDULING-SLICE-6-IMPLEMENTATION.md)
  and [receipt](E8-TRIGGER-SCHEDULING-SLICE-6-RECEIPT.md).
- Slice 7 status: accepted by its [implementation document](E8-TRIGGER-SCHEDULING-SLICE-7-IMPLEMENTATION.md)
  and [receipt](E8-TRIGGER-SCHEDULING-SLICE-7-RECEIPT.md).
- Slice 8 status: accepted by its [implementation document](E8-TRIGGER-SCHEDULING-SLICE-8-IMPLEMENTATION.md)
  and [receipt](E8-TRIGGER-SCHEDULING-SLICE-8-RECEIPT.md).
- Slice 9 status: accepted by its [implementation document](E8-TRIGGER-SCHEDULING-SLICE-9-IMPLEMENTATION.md)
  and [receipt](E8-TRIGGER-SCHEDULING-SLICE-9-RECEIPT.md).
- Slice 10 status: accepted by its [implementation document](E8-TRIGGER-SCHEDULING-SLICE-10-IMPLEMENTATION.md)
  and [receipt](E8-TRIGGER-SCHEDULING-SLICE-10-RECEIPT.md).
- Deliberate stop: no outbound network access, phone application, raw GPS, forwarded phone
  notifications, state-changing target, or device-authored administration surface.
