# E10 Slice 3A receipt — local reversible retention staging

Status: implemented and accepted on 2026-08-21.

## Delivered

- A separate retention projection for each feedback report: archive timestamp, hold state, and
  independently versioned optimistic concurrency.
- Immutable `feedback-retention.<32 lowercase hex>` action rows for archive, restore, place-hold,
  and release-hold; every action records its before/after projection and rationale.
- Exact local eligibility windows: 180 days after the latest resolved/dismissed transition for
  non-positive feedback and 90 days for positive feedback. Reopening and closing again resets the
  clock; held reports cannot be archived.
- Local `roleplay feedback retention` commands for eligibility preview and one-report archive,
  restore, hold, and release actions. Normal local list output hides archived reports unless
  `--include-archived` is supplied; show includes retention history.
- An atomic SQLite forward migration, model-drift coverage, and catalog documentation explaining
  that archive is reversible and leaves MCP feedback reads/commits unchanged.

## Explicitly not delivered

No deletion, purge, scheduler, bulk action, policy editor, remote transport, remote identity,
authorization, rate limit, or external issue delivery was added. Slice 3B remains blocked on the
accepted E9 identity/authorization evidence; Slice 3C remains blocked on 3B.

## Evidence

- Retention/domain and local CLI focused tests passed: 6 tests.
- Feedback, migration-drift, and catalog-coverage focused checks passed: 19 tests.
- MCP protocol walk passed: 6 tests; no retention field or operation was added to the MCP surface.
- `roleplay validate catalog` passed: 380 records. It reported the repository's existing 65
  near-duplicate warnings only and did not touch live data.
- Full suite passed: 711 tests, 0 failed, 0 skipped (59 seconds).
- `git diff --check` found no whitespace errors in the Slice 3A paths (Git emitted the existing
  line-ending warning for the shared `DantesRoleplayDbContext.cs` worktree file).

## Next gate

Do not start Slice 3B until accepted E9 Slice 1–2 receipts prove the required verified principal,
deny-default authorization, audit-privacy, and transport-parity contracts. Hard purge and external
delivery require a separate decision record and approval.
