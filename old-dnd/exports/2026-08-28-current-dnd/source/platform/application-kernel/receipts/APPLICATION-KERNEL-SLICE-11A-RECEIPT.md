# Application kernel Slice 11A receipt — exact `dnd2024` legacy-source adoption proof

Status: **accepted**  
Completed: 2026-08-24  
Accepted implementation: [Slice 11A](../APPLICATION-KERNEL-SLICE-11A-IMPLEMENTATION.md)

## Delivered

- Added a real MCP-host acceptance proof that starts from a fresh disposable SQLite database and
  uses only the existing authenticated `system.application.register`, `system.source.register`,
  `system.application-preview`, `system.application.activate`, and discovery contracts.
- Registered the already-confirmed `dnd2024` identity plus eleven exact existing-catalog source
  selections covering ratified legacy component definitions/schema sidecars, mechanics/scripts,
  game/campaign/quest/play procedures, the game event type, and game subscriptions.
- Activated exactly 117 non-empty authored documents with zero scan problems. Every preview winner
  passes a closed legacy-game path classifier, and persisted activation winners exactly match the
  preview winners.
- Proved representative generic system procedures, structural events, the catalog manifest, world
  entity fixtures, and relationships are not claimed by the `dnd2024` activation.
- Proved exact activation replay and authenticated query-back while retaining immutable operation
  evidence for the application, every source, and activation.
- Kept the production host ruleset-neutral: no default application, startup registration,
  `dnd2024` branch, public kind, schema, migration, or copied catalog authority was added.
- Proved the adoption transaction creates no state space, system ECS entity/component, or legacy
  entity/component rows and does not touch a normal host database or authored file.

## Evidence

- Exact fresh-host `dnd2024` adoption proof: 1 passed, 0 failed.
- Combined live MCP, registry authorization, guard, and catalog-coverage checks: 29 passed, 0
  failed.
- Full shared suite: 630 passed, 0 failed.
- Standalone local-AI suite: 19 passed, 0 failed.
- Catalog validation: 144 records valid; 17 existing near-duplicate warnings; no live data touched.
- Solution build: passed with 0 warnings and 0 errors.
- Entity Framework model drift check: no changes since the existing migrations. The installed EF
  tool emitted only its existing patch-version advisory.

## Deliberate exclusions and next gate

This proof does not install `dnd2024` into a normal database, import/version its component
contracts, publish an activated catalog navigator, create or backfill a state space, migrate
fixtures/live values, adopt projections, add aliases, execute application mechanics, or implement
AI consumption. Slice 11B may classify and register application-owned component contracts, but it
must first reconcile the unqualified `stats` definition and verify every schema/version assignment
without coercing legacy values.
