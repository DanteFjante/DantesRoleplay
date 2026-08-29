# Trail Game TG3 simulation confirmation

Confirmed: **2026-08-25 by the user's request to implement TG3**
Ruleset alignment: **ruleset-neutral**

## Permanent catalog identities

- Component: `trail-survival.scenario`.
- Procedure: `procedure.trail-survival.simulation`.
- Mechanics: `mechanic.trail-survival.run.create`, `.trade`, `.policy.set`, `.rest`, `.forage`,
  `.travel`, and `.event.choose`.

These are original Trail Survival identities. They do not reuse Oregon Trail branding, content,
code, or assets.

## State and scenario meanings

- `trail-survival.scenario` is immutable, data-only rule content attached to a scenario entity. It
  carries identity/version/hash/profile parity, route, initial state, market, policies, daily
  tuning, event choices, and terminal cause IDs.
- `trail-survival.run` additionally requires `randomSeed` and `seedCursor`. Registration creates a
  new schema version when an earlier run type already exists; no migration or existing-state
  rewrite is included.
- A run contains its party; the party contains its members and conveyance. This lets declared,
  bounded projections reveal the complete simulation graph without caller-selected child roles.
- Scenario pin values must exactly match the referenced scenario component. Published source and
  state-space activation fingerprints continue to pin the executable mechanics.

## Seed and replay contract

- Setup accepts the host-recorded unsigned 32-bit seed through the generic runner, never through
  action JSON, and stores it with `seedCursor = 0`.
- Every later root command requires the deterministic unsigned 32-bit seed
  `randomSeed XOR imul(seedCursor + 1, 2654435761)`, increments `seedCursor` once, and increments
  the monotonic turn once.
- JavaScript's seeded `ctx.random`/`ctx.randomInt` resolves all chance. Callers may select an
  offered choice but may never submit a roll, event, price, cost, distance, health delta, resource
  delta, arrival, or outcome.
- The generic operation identity/request fingerprint owns idempotent replay. A reused operation ID
  with a different fingerprint rejects; a new stale command fails its seed/state validation.

## Command and transaction boundary

- Create input contains stable run/party/conveyance/member entity IDs and player-authored names and
  role IDs only. Trade supplies mode/resource/quantity; policy supplies pace/ration IDs; event
  choice supplies one offered choice ID. Rest and forage use `{}`. Travel supplies a leg ID or
  `null` as an explicit next-leg decision.
- Every mechanic returns only generic typed effects. `ApplicationActionRunner` and the application
  ECS effect applier validate and commit one atomic root batch with audit; JavaScript never writes.
- Pending choice permits only event choice. Finished runs reject every further command without
  mutation. Terminal results are stored once in `trail-survival.outcome`.

## Acceptance decision

Equivalent automated invariant evidence may confirm each completed slice and final TG3 acceptance
when it covers positive, malformed, wrong-phase, pending/terminal, boundary, deterministic,
replay, stale, cross-application, rollback/injected-failure, catalog validation, and compatibility
cases. No public protocol walk is required because TG3 adds no public or MCP surface.
