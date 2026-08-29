# D&D 2024 Thalorien prototype-state migration dependency tree

Status: complete
Ruleset alignment: dnd2024-compatible
Source: live SQLite export `old-dnd/prototype-cutover-archive/2026-08-29-thalorien-live-pre-migration-export`; no D&D rule is being decided
Owning roadmap: `ruleset/dnd2024/ROADMAP.md`

## Outcome and non-goals

Move the selected current Thalorien campaign state into a prototype-compatible D&D runtime state
space, retaining records and relationships only where their target owner is explicit. Preserve the
current SQLite graph and its fresh export as rollback evidence.

This does not create new D&D rule behavior, alter world secrets, delete classic state, migrate
unrelated worlds, or replace the classic state. It keeps the explicitly authorised provisional
Orban choices traceable so they can be reviewed and replaced later.

## Existing owners and evidence

| Concern | Owner | State | Evidence |
| --- | --- | --- | --- |
| Live legacy world/campaign state | SQLite classic entity/component graph | verified | fresh export: 199 Thalorien entities, 366 components, 357 scoped relationships |
| Prototype component contracts | `catalog/applications/dnd2024/components/` | verified source only | 154 schemas compile through bounded profile |
| Prototype definitions | `catalog/applications/dnd2024/content/entities/` | verified source only | 2,329 records |
| Runtime application ECS state | generic state-space administration | migrated | new `dnd2024-thalorien-migrated` state space, exact adoption receipt |
| Generic world/campaign state | `game.core.*` classic components and relationships | verified | 198 of the 199 Thalorien entities use only retained generic world/campaign owners |
| Orban character | `dnd2024.playtest-character-record` legacy narrative component | migration decision required | no species, ability scores, level, legal background, or equipment facts are present |

## Dependency tree

```text
Thalorien campaign available on prototype-compatible runtime state [complete]
├── Freeze exact live source and rollback copy [verified: fresh export]
├── Register and activate the prototype component contract in SQLite [complete]
│   ├── register every source schema as an exact application component type [complete]
│   └── activate the source-only cutover [complete]
├── Select the transfer scope [confirmed]
│   └── full Thalorien graph: 199 entities / 357 relationships [confirmed]
├── Declare an explicit component and relationship crosswalk [complete]
│   ├── direct D&D campaign/world ECS components [complete]
│   └── legacy playtest character ledger to a provisional prototype character sheet and review ledger [complete]
├── Dry-run exact copy into one new D&D state space [complete]
└── Commit, read back, and retain the previous graph [complete]
```

## Conflicts and decisions

1. The existing generic legacy-adoption service is intentionally unscoped: it would copy every
   live entity, not only Thalorien. It cannot be used for this request without a scoped generic
   migration seam.
2. The current application source is now registered as live application component types and
   activated in SQLite.
3. Orban's `dnd2024.playtest-character-record` is a narrative ledger. The user explicitly
   authorised provisional choices for its missing mechanical facts on 2026-08-29, provided every
   invention is written down for later review. The migration therefore creates an explicitly
   provisional character sheet and a review ledger; the ocarina's special effects remain narrative
   and unresolved rather than becoming a magic-item rule.

## Confirmed migration boundary

- Transfer the full Thalorien graph: 199 active entities, 366 components, 26 containments, and
  357 relationships internal to that graph, plus all retained component values.
- Create Orban as a provisional 2024-compatible character and preserve both the original narrative
  ledger and a separate explicit-inventions review ledger.
- Add only an application-agnostic scoped-adoption capability; its scope is caller-supplied entity
  identifiers, never D&D or Thalorien identifiers in C#.

## Planning receipt

- A read-only live export with history was written before mutation. It contains 342 records and
  2,572 exported operation entries; the original graph remains intact.
