# DantesRoleplay status

Last updated: 2026-08-26
Purpose: compact cross-system summary. Owning roadmaps, catalog records, code, tests, and receipts
carry the details.

## Current baseline

This checkout contains concurrent in-progress campaign, story-plan, tactical, catalog, migration,
and test changes. On 2026-08-26 the solution built with zero warnings/errors and the disposable
catalog validated 144 records with 21 non-blocking near-duplicate warnings. The full shared suite
reported 1,106 passed with no failures. Run acceptance commands against the same worktree rather than copying
this count into another plan.

The public MCP surface remains exactly `orient`, `query`, and `commit`. The engine is C#; game rules
and game-specific decisions are catalog data and sandboxed JavaScript. See
[ARCHITECTURE.md](ARCHITECTURE.md) for the placement boundary.

## Capability summary

| Area | State | Detail owner |
| --- | --- | --- |
| Generic kernel | Implemented: contracts, dynamic state, JavaScript mechanics, typed effects, transactions, audit, retrieval | [ARCHITECTURE.md](ARCHITECTURE.md) and tests |
| Events and subscriptions | Implemented: pre-commit guards, structural/declared events, deterministic reactions, notifications | [receipt](platform/e1/EVENTS_AND_SUBSCRIPTIONS_RECEIPT.md) |
| Catalog portability | Implemented file-first validation/import/export workflow | [CATALOG_HANDOVER.md](CATALOG_HANDOVER.md) |
| MCP surface | Three-verb migration complete; extend closed kinds rather than tools | [VERB_MIGRATION.md](VERB_MIGRATION.md) |
| D&D 2024 | Features 1–16 verified; code-adoption Slices 0–6, 7A1–7A2, and native-recovery Slice 8 are accepted; later work remains mixed verified/planned | [ruleset/dnd2024/ROADMAP.md](ruleset/dnd2024/ROADMAP.md) |
| World | Persistent topology, knowledge, time, travel modes, reactions, and small-world composition have verified slices | [WORLD_AND_LORE_PLAN.md](WORLD_AND_LORE_PLAN.md) and `world/**/` receipts |
| Campaign | Existing-world continuity and quest-context integration are implemented; composition/participation work has current receipts | [CAMPAIGN_CREATION_PLAN.md](CAMPAIGN_CREATION_PLAN.md) |
| Quest | Closed creation, manual lifecycle, and bounded summary are implemented | [QUEST_IMPLEMENTATION_PLAN.md](QUEST_IMPLEMENTATION_PLAN.md) and `quest/**/` receipts |
| Session/snapshot | Start, resume, end/recap, and immutable snapshot-package foundations are accepted; later recovery/table features remain planned | [SESSION_OPERATIONS_PLAN.md](SESSION_OPERATIONS_PLAN.md), [SNAPSHOT_OPERATIONS_PLAN.md](SNAPSHOT_OPERATIONS_PLAN.md) |
| Character/items | Inventory foundation is accepted; character participation, profile, abilities, and staged composition have verified slices; complete player creation remains open | [CHARACTER_CREATION_PLAN.md](CHARACTER_CREATION_PLAN.md), [ITEMS_AND_INVENTORY_PLAN.md](ITEMS_AND_INVENTORY_PLAN.md) |
| Knowledge/retrieval | Knowledge state, timeline, lexical/vector retrieval, and bounded local orchestration have receipts | [Knowledge and facts](knowledge/KNOWLEDGE_AND_FACTS_PLAN.md) |
| Private web workspace | Shared navigation, system/application chat scopes, system controls, and live Home/Control pages are accepted | [Slice H receipt](web/WEB-APPLICATION-AWARE-WORKSPACE-SLICE-H-RECEIPT.md) |
| Story orchestration | An in-progress implementation is governed by its approved confirmation and implementation receipt | [confirmation](storytelling/story-plan-orchestration/STORY_PLAN_ORCHESTRATION-SLICE-0-CONFIRMATION.md), [implementation receipt](storytelling/story-plan-orchestration/STORY_PLAN_ORCHESTRATION-IMPLEMENTATION-RECEIPT.md) |

## First playable outcome

The next product proof is a fresh-host story session using the persistent world, campaign, quest,
and session owners without relying on prior chat. The exact order and acceptance scenario live only
in [STORY_FIRST_ROADMAP.md](STORY_FIRST_ROADMAP.md).

The current foundation includes:

- a small persistent world with locations, factions/NPC motives, knowledge/clues, time, and travel;
- campaign chapter/arc continuity and bounded campaign resume;
- a quest with manual lifecycle and bounded quest summary;
- campaign-to-quest context integration;
- session start/resume/end with factual recap foundations; and
- an existing actor capable of the supported checks needed for an internal proof.

Player-ready release additionally needs the governed character-creation path and its item grants.
The current execution ledger is
[character-creation MCP interface dependency plan](character/feature-06/CHARACTER_CREATION_MCP_INTERFACE_DEPENDENCY_PLAN.md).

## Recent evidence worth locating

These links are navigation, not duplicate confirmation:

- Tactical movement through difficult terrain and creature spaces:
  [Feature 20 Slice 5 receipt](ruleset/dnd2024/feature-20/FEATURE-20-SLICE-5-MOVEMENT-RECEIPT.md).
- Encounter sides and Heroic Inspiration foundations:
  [Feature 33 Slice 1](ruleset/dnd2024/feature-33/FEATURE-33-SLICE-1-RECEIPT.md) and
  [Feature 39 Slice 1](ruleset/dnd2024/feature-39/FEATURE-39-SLICE-1-RECEIPT.md).
- Campaign quest context and character participation:
  [Campaign C4](campaign/feature-04/CAMPAIGN-FEATURE-04-SLICE-1-RECEIPT.md) and
  [Campaign C15](campaign/feature-15/CAMPAIGN-FEATURE-15-SLICE-3-RECEIPT.md).
- Small-world composition:
  [World Feature 17](world/feature-17/WORLD-FEATURE-17-SLICE-1-RECEIPT.md).
- Status reconciliation evidence remains in the `STATUS_REMEDIATION-SLICE-*-RECEIPT.md` files.

## Maintenance rule

Update this file only when the cross-system summary changes. Put feature detail in its owner roadmap,
prospective work in one active feature plan, proof in a receipt, and reproducible failures in
`KNOWN_ISSUES.md`. Never append a dated implementation diary here.
