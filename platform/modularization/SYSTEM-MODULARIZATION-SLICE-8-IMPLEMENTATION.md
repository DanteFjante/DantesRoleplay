# System modularization Slice 8 implementation — events/notifications physical component

Status: **accepted**  
Owner/roadmap: [Platform roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)  
Dependency tree/leaf: [Modularization Leaf 7](SYSTEM-MODULARIZATION-DEPENDENCY-PLAN.md#ordered-leaves)  
Ruleset alignment: **ruleset-neutral**  
Source ID and locator: **not applicable**  
Outcome: Co-locate generic event, guard, subscription, reaction, ledger, and notification domain,
persistence, hosting, and focused tests.  
Exclusions: Catalog parsing/seeding, game-specific event tests/consumers, EF mappings/migrations,
APIs/namespaces, MCP, and local AI.  
Allowed files/areas: Named event/notification domain, stores/routers/helpers, focused generic tests,
component manifest, and planning evidence.  
Stop point: Focused generic event/notification and guard tests plus build pass.

## Confirmed decisions

Structural event hosting is one generic capability. Catalog authoring stays with catalog; D&D
initiative-event behavior remains a game consumer.

## D&D 5e 2024 alignment

Not applicable; no event rule or consumer meaning changes.

## External implementation reference

No Foundry reference is relevant.

## Prerequisite evidence

- [Slice 7 receipt](SYSTEM-MODULARIZATION-SLICE-7-RECEIPT.md).
- Generic derived-event, chain, ledger, router, guard, notification, and subscription tests own the
  moved behavior.

## Runtime artifacts

None; types retain assemblies/namespaces and existing mappings.

## Authoritative state and closed input

Existing declarations, typed structural events, stores, and routing inputs remain unchanged.

## Behavior, result, and typed effects

Physical placement only; chain budgets, guard/reaction order, event evidence, notification
projection, replay, and transaction ownership do not change.

## Failure, replay, and rollback contract

Focused tests retain negative, chain-limit, replay, and rollback coverage; build rejects duplicate
or missing source.

## Implementation sequence

Move domain/persistence/generic tests; leave catalog/game consumers; update manifest; verify; receipt.

## Acceptance matrix

| Case | Evidence |
| --- | --- |
| Positive/negative | Existing generic event/notification test suites pass. |
| Boundary | Catalog seeder/files and D&D event consumer stay outside. |
| Replay/rollback | Existing chain/router/guard tests retain coverage. |
| Compatibility | Same assemblies, types, mappings, and registration. |

## Verification commands

- `dotnet test ... --filter "FullyQualifiedName~DerivedEventTests|FullyQualifiedName~EventChainTests|FullyQualifiedName~EventLedgerTests|FullyQualifiedName~EventRouterTests|FullyQualifiedName~GuardRouterTests|FullyQualifiedName~NotificationTests|FullyQualifiedName~SubscriptionStoreTests|FullyQualifiedName~GuardTests"`
- `dotnet build DantesRoleplay.slnx --no-restore`

## Completion receipt and exit gate

Evidence is recorded in [the Slice 8 receipt](SYSTEM-MODULARIZATION-SLICE-8-RECEIPT.md). Stop before another component move.
