# Trail Game TG3 dependency tree — deterministic simulation loop

Status: **accepted; TG3.1 through TG3.5 verified**
Ruleset alignment: **ruleset-neutral**
Source: **not applicable; original Trail Survival rules and contracts**

## Outcome and non-goals

One exact application command advances one canonical Trail Survival root transaction. Setup,
trade, policy, rest, forage, travel, event choice, arrival, victory, and defeat derive from a
pinned scenario plus ECS state and a host-supplied deterministic action seed. The browser, public
transport, authored starter scenario, balance library, migration, and live database remain TG4+
work.

## Existing owners and evidence

| Concern | Owner | State | Evidence |
| --- | --- | --- | --- |
| Run/party/decision state | Trail component schemas | verified | [TG2 receipt](TG2-SLICE-3-RECEIPT.md) |
| Exact mechanic lookup/projection | `application-execution` | verified | active catalog fingerprint and frozen declared projection checks |
| Rules and branching | application catalog JavaScript | ready | Jint seeded random helpers and generic effect vocabulary |
| Atomic write/replay/audit | application ECS effect applier | verified | one SQLite effect batch and operation identity replay |
| Scenario rules input | Trail scenario component | verified | TG3.1 added the immutable data-only contract; TG4 authors instances |
| Browser/public command | TG5 | planned | deliberately not a TG3 prerequisite |

## Dependency tree

```text
TG3 deterministic simulation loop                                      [accepted]
├─ TG3.1 scenario contract, seed cursor, create-run transaction         [verified; TG3-SLICE-1-RECEIPT.md]
├─ TG3.2 trade, policy, rest, and forage commands                       [verified; TG3-SLICE-2-RECEIPT.md]
├─ TG3.3 travel, seeded event draw, and event-choice resolution         [verified; TG3-SLICE-3-RECEIPT.md]
├─ TG3.4 stable headless replay, exact audit, rollback, full acceptance [verified; TG3-SLICE-4-RECEIPT.md]
└─ TG3.5 boundary and acceptance-document hardening                     [verified; TG3-SLICE-5-RECEIPT.md]
```

## Conflicts and decisions

- Scenario rules are one referenced immutable component because the current generic projection
  follows declared component references one level. It embeds route, market, policy, event, tuning,
  and outcome definitions without creating recursive host traversal.
- The `run` schema gains `randomSeed` and `seedCursor`. A command validates the host seed from those
  fields and increments the cursor with the turn; resolved rolls never enter action input.
- The generic C# runner remains the only transaction root. Mechanics return ordinary component,
  entity, and containment effects; no Trail identifier or formula enters C#.
- Setup content used by tests is disposable state, not the authored TG4 starter scenario.
- Pending choice blocks every command except event choice. A finished run blocks every command.

## Ordered leaves

| Order | Leaf | Depends on | Exit gate |
| ---: | --- | --- | --- |
| 1 | TG3.1 scenario/setup | TG2 | Exact setup creates a pinned, nested run atomically and replays. |
| 2 | TG3.2 daily commands | TG3.1 | Trade/policy/rest/forage derive bounded costs and state changes. |
| 3 | TG3.3 journey/events | TG3.1/2 | Travel deterministically moves, draws an event, and choice resolves. |
| 4 | TG3.4 terminal acceptance | TG3.1-3 | Known-seed victory and defeat runs are byte-stable with rollback evidence. |
| 5 | TG3.5 boundary hardening | TG3.1-4 | Maximum setup and negative boundary witnesses preserve the accepted behavior. |

## Accepted boundary

TG3.1 through TG3.5 are accepted. TG3.5 is a test/document-only hardening leaf that closes explicit
maximum-party, affordability/capacity, partial-leg continuation, and insufficient-event-cost
no-change evidence. It adds no game behavior or production artifact.

## Confirmation gates

The permanent IDs, schema meanings, seed derivation, effect boundary, and command inputs are
confirmed in [TG3 simulation confirmation](TG3-SIMULATION-CONFIRMATION.md). TG3.1 through TG3.4
acceptance is recorded; TG3.5 introduces no new confirmation gate.

## Planning receipt

- Runtime artifacts through TG3.4: one scenario contract, one run schema revision, one governing
  procedure, seven mechanics, generic exact-audit enrichment, focused tests, and receipts.
- External implementation reference: none; no outside code or assets are needed.
- Public surface, migration, and authored playable scenario: excluded.
- Final hardening evidence: [TG3 Slice 5 receipt](TG3-SLICE-5-RECEIPT.md).
- Active implementation: none; TG4 planning is next and has not begun.
