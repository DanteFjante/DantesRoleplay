# Story-first gameplay roadmap

Status: **Active integration roadmap; planning only**
Last updated: 2026-08-21

## Outcome

A player explores a small persistent world, learns facts and rumours, chooses an approach to a
multi-objective quest, changes authoritative state through governed mechanics, ends the session,
and resumes with a fresh host from stored state rather than prior chat.

There are two gates:

1. **Internal story-play proof:** may use the existing catalog actor and trusted-host projections.
2. **Player-ready story release:** adds governed protagonist creation, starting items, participation,
   and a safe first action.

Combat, remote multiplayer, a website, and generated campaigns are not required for either gate.

## Ownership

| Concern | Owner | Boundary |
| --- | --- | --- |
| Topology, locations, time, travel, factions, NPC motives, knowledge and clues | [WORLD_AND_LORE_PLAN.md](WORLD_AND_LORE_PLAN.md) | Campaign and quests reference World state; they do not copy it. |
| Campaign root, chapters, arcs, participation, resume projection | [CAMPAIGN_CREATION_PLAN.md](CAMPAIGN_CREATION_PLAN.md) | Campaign frames continuity; it does not own quest/objective state. |
| Quest definition, objectives, routes, lifecycle, quest summary | [QUEST_IMPLEMENTATION_PLAN.md](QUEST_IMPLEMENTATION_PLAN.md) | Quest owns progression; campaign holds a bounded link/context. |
| Session start/resume/end, factual recap, checkpoints | [SESSION_OPERATIONS_PLAN.md](SESSION_OPERATIONS_PLAN.md) | Session records continuity references; it does not become canonical story state. |
| Narrative presentation and model-authored story plans | `procedure.play.storytelling` and [Story plan orchestration](storytelling/story-plan-orchestration/STORY_PLAN_ORCHESTRATION_PLAN.md) | Prose and plans consume owner projections; only governed actions change truth. |
| Character and item creation | [CHARACTER_CREATION_PLAN.md](CHARACTER_CREATION_PLAN.md), [ITEMS_AND_INVENTORY_PLAN.md](ITEMS_AND_INVENTORY_PLAN.md) | Ruleset owners calculate grants and rules; campaign owns participation. |
| D&D behavior | [ruleset/dnd2024/ROADMAP.md](ruleset/dnd2024/ROADMAP.md) | Game mechanics stay in catalog JavaScript, never feature-specific C#. |

## Dependency path

```text
verified kernel/events/catalog
  -> persistent world and knowledge                 [verified slices]
  -> campaign chapter/arc continuity                [verified]
  -> manual quest lifecycle and bounded summary     [verified]
  -> campaign quest-context bridge                  [C4 focused verification]
  -> session continuity and factual recap           [accepted foundations]
  -> internal played-session receipt                [next integration proof]
  -> governed character + starting items            [parallel lane]
  -> player-ready played-session receipt
```

The in-progress story-plan orchestration feature is a consumer of backend-owned context, knowledge,
and action services. It does not replace the played-session proof or create a second workflow/state
authority.

## Internal proof ledger

| Gate | State | Evidence / next action |
| --- | --- | --- |
| World topology, movement, factions, knowledge, time, and travel | Verified | `world/feature-*/` receipts; World Feature 17 also proves bounded small-world composition. |
| Campaign existing-world root and chapter/arc resume | Verified | Campaign C0–C3 plans/receipts and tests. |
| Storytelling procedure publication | Implemented | [validation](storytelling/feature-01/STORYTELLING-FEATURE-01-VALIDATION.md). |
| Quest definition and manual lifecycle | Verified | Quest Q0–Q2 evidence. |
| Bounded quest summary | Accepted | [Q3 validation](quest/feature-03/QUEST-FEATURE-03-SLICE-1-VALIDATION.md) and [receipt](quest/feature-03/QUEST-FEATURE-03-SLICE-1-RECEIPT.md). |
| Campaign-to-quest continuity | Focused verification complete | [C4 receipt](campaign/feature-04/CAMPAIGN-FEATURE-04-SLICE-1-RECEIPT.md). Recheck full-suite acceptance against a stable worktree. |
| Fresh-host played story | Verified | [Played-session receipt](storytelling/story-first/STORY_FIRST_PLAYED_SESSION_RECEIPT.md) records a disposable fresh-host proof with no transcript dependency. |

Do not reopen a verified owner because an older plan or handoff says “waiting.” Code, catalog state,
tests, and receipts outrank stale planning prose.

## Internal played-session scenario

Use a disposable database and the existing supported actor.

1. Start or resume the approved campaign/session and retrieve the bounded world, campaign, and quest
   context.
2. Present at least two meaningful approaches using only visible facts, rumours, clues, locations,
   NPC motives, and the active quest.
3. Let the player travel or investigate through an existing governed action; reject one invalid
   attempt without state change.
4. Advance one quest objective manually through its owner and show the resulting bounded summaries.
5. End the session and store the factual recap/reference through the session owner.
6. Start a fresh host/process with no transcript and recover current location, campaign chapter/arc,
   quest state, learned knowledge, and session recap from SQLite.
7. Continue with one valid action, then record commands, state evidence, negative case, and any
   defects in a concise played-session receipt.

The proof fails if the host needs the old conversation, raw component inspection, copied hidden
state, an ungoverned write, or a feature-specific C# rule branch.

## Player-ready lane

This lane may proceed without delaying the internal proof:

1. Follow the current owner map and ordered ledger in
   [character-creation MCP interface dependency plan](character/feature-06/CHARACTER_CREATION_MCP_INTERFACE_DEPENDENCY_PLAN.md).
2. Reuse the accepted inventory/item owners and campaign participation contract.
3. Complete one supported non-spellcasting level-one build through one atomic creation root.
4. Prove discovery, creation, inspection, one safe action, session enrollment, end, and fresh-host
   resume without raw component edits.

Additional classes, spellcasting, advancement, a character-builder UI, remote identity, and
self-service control remain separate roadmap work.

## Execution rule

For the next row only:

1. Read `AGENTS.md`, the owning catalog procedures, the row's active plan, and only the prerequisite
   receipts needed to prove the boundary.
2. Search for existing IDs and owners before proposing new ones.
3. Confirm permanent IDs, schema meaning, migrations, public surface, or cross-owner semantics.
4. Implement one lowest slice; keep game rules in catalog JavaScript and generic hosting in C#.
5. Run focused tests, disposable catalog validation after catalog edits, and the full suite at
   feature acceptance.
6. Record a short receipt and stop. Update this roadmap only when a gate changes.

## Deferred until after the proofs

- Generated campaign/world content beyond a reviewed fixed blueprint.
- Combat depth, spells, monsters, tactical rendering, and complete SRD play.
- Player-safe visibility and control before real identity/authorization exists.
- Website/map consumers before stable read and write contracts.
- Remote collaboration, scaling infrastructure, and speculative workflow features.
