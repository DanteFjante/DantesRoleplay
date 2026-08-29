# Trail Game TG3 Slice 4 receipt — deterministic simulation-loop acceptance

Status: **accepted through equivalent automated invariant evidence**
Completed: **2026-08-25**
Implementation: [TG3 Slice 4](TG3-SLICE-4-IMPLEMENTATION.md)
Parent: [TG3 simulation dependency plan](TG3-SIMULATION-DEPENDENCY-PLAN.md)
Ruleset alignment: **ruleset-neutral**

## Delivered boundary

- Completed all seven confirmed mechanics: create run, trade, set policy, rest, forage, travel, and
  event choice.
- Added one immutable data-only scenario component contract and the run seed/cursor revision; no
  authored playable scenario instance was added.
- Derived setup state, economy, capacity, policy eligibility, food, health, conveyance wear, time,
  forage yield, route movement, weighted event draw, pending choice, arrival, victory, and defeat
  entirely in catalog JavaScript from pinned scenario plus canonical state.
- Preserved one generic application-execution/ECS-effect transaction root. The generic batch now
  carries optional exact mechanic ID/version, seed, and frozen projection into the existing root
  operation columns without any Trail identifier or formula in C#.
- Proved two independent known-seed setup→event→choice→victory loops produce byte-identical
  canonical entity/component/containment snapshots; a different initial seed diverges.
- Proved exact operation replay, request-fingerprint conflict behavior through the generic owner,
  wrong-seed/stale no-change, late-collision atomic rollback, pending-command blocking, invalid
  offered choice no-change, and terminal-command blocking.

## Acceptance evidence

- Focused Trail plus generic application-execution/ECS-effect suite: **24 passed, 0 failed**.
- Full shared current-source suite: **942 passed, 0 failed**.
- Standalone local-AI suite: **20 passed, 0 failed**.
- Current-source isolated solution build: **0 warnings, 0 errors**.
- Authored audit: **24 JSON files parsed**, seven mechanic contracts, three governing procedures,
  all Trail Game local links resolved, no owned trailing whitespace, no scoped diff-check error,
  and no Trail vocabulary in the changed generic C# files.
- Repeated disposable real-source preview, activation, catalog materialization, schema registration,
  application action execution, state-space isolation, and headless runs pass in the focused/full
  suites.

The standalone `roleplay validate catalog` command could not begin its final pass because unrelated
concurrent trigger-scheduling work currently raises EF's pending-model-changes warning before the
validator runs. It had passed after TG3.1, and the final Trail additions are covered by direct
parse/schema plus real activated-materializer/execution tests. No migration or snapshot was changed
to hide that external condition.

## Deliberate exclusions and next boundary

TG3 adds no authored starter scenario entity, narrative, balance claim, browser/UI/HTTP/MCP surface,
authorization adapter, startup registration, migration, normal-database mutation, external code,
or external asset. TG4 must separately plan and confirm the first original playable content pack;
TG5 remains responsible for trusted browser/public command authorization and transport.
