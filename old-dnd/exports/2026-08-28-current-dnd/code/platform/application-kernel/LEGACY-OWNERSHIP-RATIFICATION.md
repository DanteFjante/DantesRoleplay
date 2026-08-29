# Application kernel legacy ownership ratification

Status: **accepted**  
Decision date: 2026-08-23  
Owner/roadmap: [Platform enabling features roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)  
Dependency tree: [Generic application kernel dependency plan](APPLICATION-KERNEL-DEPENDENCY-PLAN.md), B / `game.core.*` ownership gate  
Evidence: [Slice 1 legacy inventory](inventory/LEGACY-APPLICATION-KERNEL-INVENTORY.json) at SHA-256
`BD2157F577E8C75330A0491D3C66D1C87057802D8A87B3A344E24B159A53BB4D`

## Accepted decision

The initial application is `dnd2024`. Every record listed as `unresolved` in the accepted Slice 1
inventory is a legacy migration candidate owned by `dnd2024`; no second application is introduced
at this stage.

This includes:

- every legacy `game.core.*` component definition, procedure, mechanic, event type, and
  subscription;
- the unqualified `stats` component definition;
- `mechanic.check.threshold` and `mechanic.value.adjust`; and
- the legacy campaign, quest, and play procedures.

The existing names are not thereby converted to `dnd2024.*`, activated, aliased, or rewritten.
They remain legacy identifiers until later migration work creates reviewed compatibility aliases and
application-qualified records. They cannot be treated as `system` records during that period.

## Public-surface consequence

The legacy game-facing query kinds `journey-plan`, `itinerary-plan`, `campaign-resume`,
`session-recap`, `quest-summary`, `knowledge-answer`, and `story-plan`, along with commit kinds
`action`, `itinerary-advance`, `campaign`, `quest`, and `story-plan`, are `dnd2024` migration
candidates. Slice 10 owns exact replacement kinds, aliases, client compatibility, and authorization;
this ratification creates none of them.

## Boundaries retained

- The 39 records already evidenced as generic system behavior remain system migration candidates.
- `dnd2024` ownership does not make D&D rule logic valid in C#; mechanics remain application-owned
  catalog JavaScript/data and generic system code stays ruleset-neutral.
- The catalog manifest's path/coverage findings remain open and must be reconciled before manifest
  activation.
- No database, catalog, public protocol, source registration, application revision, state-space, or
  runtime behavior changed.

## Next gate

Slice 2 may now define pure generic application/source/type/state-space/projection/catalog contracts
and in-memory validator fakes. It must not migrate or activate the legacy records, invent aliases,
or encode `dnd2024` in generic system/local-AI code.
