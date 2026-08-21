# Snapshot Feature SP1 validation

Status: **Accepted.**
Date: 2026-08-21

## Accepted implementation boundary

SP1 stores one bounded, immutable, opaque SQLite evidence package created only by the typed
ended-session producer. The package keeps canonical v1 JSON bytes, scope/provenance metadata,
byte count, SHA-256 digest, and its source-boundary fingerprint. Staging joins a caller-owned
transaction; verification returns only a byte-free reference and fails closed for missing,
mismatched, unavailable, or corrupt data.

Confirmed permanent vocabulary:

- `procedure.snapshot.package`;
- amended `procedure.campaign.session` scope owner;
- `snapshot.producer.campaign-session-evidence`, version `1`;
- `dantes.snapshot.campaign-session-evidence`, version `1`;
- `dantes-canonical-json-v1` and `sha256`.

The forward migration is `20260821103457_SnapshotPackages`. It creates `snapshot_package` with
portable integrity checks and SQLite update/delete abort triggers.

## Acceptance evidence

- `SnapshotFeature1Tests` — 6 passed.
- `SessionFeature1Tests` — 5 passed.
- `MigrationDriftTests` — 4 passed.
- `roleplay validate catalog` — valid: 328 records, 47 pre-existing warning-level near-duplicate
  findings, no errors, and no live data touched.
- `dotnet test DantesRoleplay.slnx --no-build --no-restore` — 630 passed, 0 failed.
- Full SP1 boundary review and `git diff --check` found no snapshot MCP verb/kind/handler, payload
  open/list/download path, caller-supplied snapshot id/digest/count, transaction commit/rollback,
  update/delete API, external storage, persistent catalog import, checkpoint graph, restore, or
  fork behavior.

No protocol walk was run because SP1 adds no MCP surface or dependency registration to the MCP
host; its two registrations remain internal data-access services.

## Deliberately deferred

SP1 does not create named checkpoints, public metadata reads, player access, package retirement,
restore, campaign/world fork, state export, or import into the persistent game database. Session
S4 owns checkpoint/reference composition; later planned features own those separate semantics.

## Human acceptance

Approved 2026-08-21 as the reusable immutable evidence-package foundation. This closes SP1 and
authorizes Session S4 to depend on its permanent contract/payload/table vocabulary; it does not
authorize the deferred public, lifecycle, restore, or fork capabilities.
