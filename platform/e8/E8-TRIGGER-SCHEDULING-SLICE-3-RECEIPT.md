# E8 trigger scheduling Slice 3 completion receipt

Status: **accepted**
Completed: 2026-08-25
Ruleset alignment: **ruleset-neutral**
Implementation document: [private observation ingestion](E8-TRIGGER-SCHEDULING-SLICE-3-IMPLEMENTATION.md)
Dependency tree: [durable scheduling and external triggers](E8-TRIGGER-SCHEDULING-DEPENDENCY-PLAN.md)

## Delivered boundary

Slice 3 adds the exact private application-scoped observation endpoint and closes its security
review before any worker or consumer can act on submitted evidence:

- `POST /api/applications/{applicationId}/observations` authenticates the loopback/Tailscale
  private-host principal with the server-selected `trigger.observation.submit` capability before
  reading the body;
- strict UTF-8 parsing enforces the confirmed 64 KiB envelope, exact fields, `Z` UTC timestamp,
  object-root data, depth/node/property/array/string bounds, and rejects duplicates and unknowns;
- current enabled source revisions carry immutable opaque principal permissions, and both the
  ingestion service and hardened store recheck the exact principal/current-revision boundary;
- exact stored-profile schema validation precedes the append-only transactional observation store;
- a bounded fixed-window limiter enforces 10 attempts/minute per principal, each source's lower
  rate, two concurrent requests, no queue, and pruning of expired capacity state;
- the `TriggerObservationIngestionSecurity` migration preserves historical observations with null
  provenance while requiring every new row to reference an exact source-revision principal grant;
  direct updates/deletes remain blocked; and
- append and exact replay return only the confirmed safe `202` receipt, conflict returns `409`, and
  no response exposes principal, schema/hash, canonical data, or internal transport evidence.

Existing source revisions receive no implicit principal grant during migration. They deliberately
fail closed until an explicit newer source revision grants a verified opaque principal.

## Evidence

- Focused trigger-scheduling, ingestion, authorization, migration, and web tests: **106 passed, 0
  failed**.
- Hostile reader tests cover content type, streamed/declarative size, invalid UTF-8/JSON, exact
  shape, duplicate/unknown fields, UTC form, data root, and every closed JSON resource bound.
- Principal/schema tests prove one allowed append/replay and no row for a wrong principal or
  schema-invalid input.
- Rate tests prove lower source limits, principal 10/minute, two active requests, no queue, UTC
  rollover, capacity exhaustion, and expired-state recovery.
- Migration tests prove historical null provenance survives, new null/unbound evidence is rejected,
  and fourteen append-only permission/evidence triggers remain installed.
- Safe endpoint test proves the exact four-field `202` response and `Cache-Control: no-store`.
- `dotnet build DantesRoleplay.slnx -c Release --no-restore`: **0 warnings, 0 errors**.
- `dotnet ef migrations has-pending-model-changes --project DantesRoleplay.DataAccess
  --configuration Release --no-build`: **no pending model changes**. The local EF CLI retains its
  existing 10.0.2-versus-10.0.11 informational version warning.
- `dotnet test DantesRoleplay.slnx -c Release --no-restore`: **872 shared tests passed and 20
  local-AI tests passed**.
- `git diff --check`: passed; only existing line-ending notices were reported.

## Deliberate exclusions

No source/structure administration route, hosted worker, lease/retry state, trigger matching,
notification writer/status projection, recurrence, world-clock condition, coded adapter, phone
identity, action/effect/event write, MCP kind, or public Internet hosting was added. Recording an
observation still causes no downstream world mutation.

## Handoff

Slice 4 is the separately gated durable one-time scheduler worker. It must add lease/retry/misfire
and restart-recovery evidence while preserving notification-only targeting and stopping before any
notification or application-state writer.
