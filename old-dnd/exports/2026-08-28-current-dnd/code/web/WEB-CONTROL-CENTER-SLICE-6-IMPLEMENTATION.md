# Web Interface Feature 2 Slice 6 implementation — versioned host setting overrides

Status: **accepted**  
Owner/roadmap: [Web interface roadmap](WEB-INTERFACE-ROADMAP.md)  
Dependency tree/leaf: [Control-center dependency plan](WEB-CONTROL-CENTER-DEPENDENCY-PLAN.md), safe server settings / versioned overrides  
Ruleset alignment: **ruleset-neutral**  
Outcome: Let an authorized operator stage, inspect, reset, and roll back the seven confirmed local-completion settings without changing the running provider; apply validated pending revisions at the next successful host startup.  
Exclusions: process restart controls, live provider refresh, local-model registration/calls, secrets, arbitrary configuration, database/listen/MCP/Tailscale settings, assistants, Codex, and game/catalog/page mutation.  
Allowed areas: a new generic host-settings component; kernel DbContext/registration/migration; host setting provider/startup; web setting contracts/projection/routes/panel/tests; web/component/Feature 2 documentation and receipt.  
Stop point: staged settings survive restart and are auditable; stop before Slice 7 provider and conversation work.

## Confirmed boundary

The user's **Continue** on 2026-08-24 confirms this Sol-owned migration and public write boundary. All
seven Slice 5 definitions remain `restart-required` with `host-restart` disruption. This slice never
hot-swaps a provider or starts a model.

The only mutable keys are the seven keys in the Slice 5 receipt. The host definition provider owns
key recognition and normalization. Values are exact JSON primitives: boolean for `enabled`; strings
for endpoint/model/profile; integers for token, timeout, and concurrency bounds. Endpoint remains an
absolute loopback HTTP/HTTPS URI. The web layer cannot enumerate configuration or invent a key.

## Persistence and transaction contract

New kernel-owned, ruleset-neutral records:

- `host_setting_override`: `Key` (PK, max 100), `CurrentVersion` (positive), `AppliedVersion`
  (non-negative and not greater than current), and `UpdatedAtUtc`.
- `host_setting_override_version`: `Id` (integer identity), `SettingKey` (FK/cascade), `Version`
  (positive), nullable `ValueJson` (null means reset/inherit startup configuration), `CreatedAtUtc`,
  `CreatedBy` (max 200), and `OperationId` (32 lowercase hex FK/restrict to `operation`). The pair
  `(SettingKey, Version)` is unique.

The confirmed EF migration is `20260824105304_HostSettingOverrides`. History is immutable. There is no update
or delete route. A scoped `IHostSettingOverrideStore` is the only persistence port.

Each stage/reset/rollback allocates its operation ID first and executes one SQLite transaction:
validate the optimistic revision, append exactly one version, advance the current pointer, record
one successful operation through `IOperationLog` using the same scoped DbContext, then commit. The
operation tools are `control.settings.update`, `control.settings.reset`, and
`control.settings.rollback`. Any failure rolls back both setting and operation rows.

`expectedRevision` is required and is `0` before a key has override history. A mismatch returns
`SETTING_REVISION_STALE` (409). A reset with no effective override, or an update/rollback normalized
to the current staged value, returns `SETTING_NO_CHANGE` (409) and writes nothing. Rollback requires
an existing target revision lower than current and appends its value/reset marker as a new revision.

## Startup apply contract

After kernel migration/seeding and before endpoints listen, the host loads current override heads,
validates every non-null value through the same host definition provider, and constructs a new
immutable provider catalog from base startup configuration plus those heads. A reset head restores
the base configuration/default value. Invalid or unknown durable data aborts startup.

Only after catalog construction succeeds, the store marks pending heads applied and records one
`host.settings.startup` operation in the same transaction. Failure aborts startup; retry is safe.
No pending heads means no write. `CurrentVersion > AppliedVersion` is pending; equality is applied.
The read projection combines the provider startup snapshot with durable heads. An unapplied head is
returned as `pendingValue`, `restartRequired: true`, `revision`, and `appliedRevision`; after a
successful restart it becomes `value`, source `override` (or the restored base source for reset),
and no pending value. `effectiveValue` remains null while Slice 5's provider runtime is
`not-registered`.

## HTTP and identity contract

Existing GET routes become asynchronous and add revision fields. New routes are:

- `GET /api/control/settings/{key}/versions?beforeVersion=&limit=` under `control.read`; descending,
  default 25, maximum 100.
- `PUT /api/control/settings/{key}` under `control.settings.write`, JSON
  `{ "expectedRevision": number, "value": <primitive> }`.
- `POST /api/control/settings/{key}/reset` under `control.settings.write`, JSON
  `{ "expectedRevision": number }`.
- `POST /api/control/settings/{key}/rollback` under `control.settings.write`, JSON
  `{ "expectedRevision": number, "targetRevision": number }`.

Bodies are bounded to 16 KiB and reject missing/extra/wrongly typed members. The actor is derived
server-side as `local-operator` or the authenticated exact Tailscale login; caller identity is
ignored. Mutations retain the Slice 0 JSON, Host, Origin, capability, rate-limit, and no-store
boundary. Stable client errors are `INVALID_SETTING_KEY`/`INVALID_SETTING_VALUE` (400),
`SETTING_UNKNOWN`/`SETTING_REVISION_UNKNOWN` (404), and the two conflicts above (409).

Version items contain `version`, `state` (`pending`, `applied`, or `history`), `createdAtUtc`,
`createdBy`, `operationId`, `isReset`, and the redacted-or-public value. The settings panel edits
only public values, shows the revision and pending-restart state, supports reset and rollback with a
confirmation, and reloads after success. It cannot restart the process.

## Acceptance and verification

- Migration/table constraints, history ordering, optimistic conflict, no-change, append-only reset
  and rollback, and audit atomicity have focused store tests.
- Host tests cover normalization, unknown keys, pending application, reset fallback, invalid durable
  values aborting startup, and idempotent second startup.
- Web tests cover exact route/capability metadata, body/identity rules, redaction, stable errors, and
  editable/restart UI without a restart control.
- Run focused host-settings/web tests, local-AI tests, clean solution build, full solution tests,
  catalog validation after component metadata changes, the public MCP protocol walk because startup
  registration changed, a disposable browser restart walk, and `git diff --check`.
