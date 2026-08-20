# Quest implementation plan

Status: **Draft — design plan only; no quest content is authorised by this document**  
Last updated: 2026-08-20

## Execution rule

Use [GAME_SYSTEM_MASTER_PLAN.md](GAME_SYSTEM_MASTER_PLAN.md) for cross-subsystem ownership,
[TERRA-FEATURE-PLANNING-GUIDE.md](ruleset/dnd2024/TERRA-FEATURE-PLANNING-GUIDE.md) for plan quality,
and a populated [SUBSYSTEM_IMPLEMENTATION_HANDOFF.md](SUBSYSTEM_IMPLEMENTATION_HANDOFF.md) for the active assignment. Implement one reviewed delivery
slice, meet its exit gate, record evidence, and stop.

## Goal

Support persistent, inspectable quests that can be created, progressed, completed, failed, and
resumed across sessions. A quest records what the campaign knows, what is currently actionable,
what evidence supports it, and why its state changed.

Quests are campaign/ruleset content built from the existing entity-component model, mechanics,
relationships, effects, event ledger, and subscriptions. The C# kernel gains no quest-specific
vocabulary, table, effect type, or hidden quest state.

## First playable scope

The first release supports one campaign-owned quest with:

- title, synopsis, visibility, and lifecycle state;
- multiple ordered objectives, including optional objectives and dependencies;
- links to NPCs, locations, clues, items, and other world entities;
- manual advancement through governed quest mechanics;
- one automatic objective update from a verified event/subscription;
- reconciliation of the parent quest when all required objectives complete;
- durable history and player-facing summary.

Quest templates, branching dialogue, timers, procedural generation, rewards, faction reputation,
parallel-party sharing, and a visual quest-board UI are later features.

## Design principles

1. **A quest is world data.** It must survive a fresh model/session without relying on chat memory.
2. **Objectives are entities, not JSON arrays.** They can link to clues, locations, NPCs, events,
   and dependencies without becoming one opaque ever-growing record.
3. **The model narrates; rules change state.** A narration does not silently complete an objective.
   A quest mechanic or approved manual command produces the structural effects.
4. **Use generic structural events first.** A component/relationship/containment change already has
   a ledger event. Register a semantic quest event only when it expresses information that the
   structural event cannot.
5. **No executable criteria in data.** Quest definitions may declare closed state/dependency/event
   metadata. They may not contain SQL, JavaScript, JSONPath, or a general condition language.
6. **Every automatic transition is auditable.** The history identifies the triggering event,
   subscription, mechanic/version, exact objective state before/after, and root operation.
7. **Visibility is separate from truth.** Initial MCP/GM use is trusted. A future player website
   needs a real audience/authorisation policy before hidden quest fields are exposed.

## Proposed campaign data model

### Quest entity

A quest is an entity with a quest component:

    {
      "title": "Find the source of the poisoned wells",
      "status": "active",
      "summary": "The town believes the old well was sabotaged.",
      "visibility": "party",
      "priority": "main",
      "openedAt": "2026-08-20T...",
      "completedAt": null,
      "resolution": null
    }

Initial status values are draft, active, completed, failed, abandoned, and archived. Transitions are
closed and validated by quest mechanics; direct component edits are reserved for correction
procedures.

### Objective entity

Each objective is an entity with a quest.objective component:

    {
      "title": "Examine the well",
      "status": "active",
      "required": true,
      "displayOrder": 10,
      "visibility": "party",
      "summary": "Look for signs of tampering.",
      "completionMode": "manual"
    }

An objective's completion mode begins as manual, event, or derived. The mode selects a registered
mechanic/subscription arrangement; it is not an executable expression embedded in the component.

### Relationships and evidence

Use named relationships rather than fields containing arbitrary entity-id arrays:

- quest.has-objective: quest to objective;
- objective.depends-on: objective to prerequisite objective;
- quest.related-to: quest to NPC, location, faction, or item;
- objective.supported-by: objective to clue or evidence;
- objective.targets: objective to the world entity it concerns.

The renderer follows a bounded number of links. A nested quest/objective graph must not turn into a
recursive wall of cards.

### Quest history

The event ledger is canonical for automatic changes. A compact quest-history projection may
summarise player-readable milestones, but it must link back to root operation/event IDs rather than
duplicate mutable state. A quest completion summary is authored once by the GM and becomes part of
the persistent quest state.

## Ruleset mechanics and procedures

Implement quest behaviour as stored, versioned mechanics and procedure contracts:

- mechanic.quest.create creates a quest and its initial objective entities/relationships.
- mechanic.quest.activate moves a draft quest to active after validation.
- mechanic.quest.objective.advance completes, fails, reopens, or marks an objective blocked under
  a closed state-transition table.
- mechanic.quest.reconcile evaluates an affected quest after an objective change and resolves
  active/completed/failed state from required objectives and dependencies.
- mechanic.quest.event.evaluate is an event reaction that determines whether an approved event
  satisfies an event-driven objective, then proposes normal quest-objective effects.
- mechanic.quest.archive retains historical quest data without leaving it in active discovery.

The relevant procedures are procedure.quest.create, procedure.quest.modify,
procedure.quest.advance, procedure.quest.inspect, and procedure.quest.event-react. They define
scope, inputs, valid transitions, event behavior, recovery calls, test fixtures, and narrative
constraints.

No generic quest service is added to the kernel. A later registered workflow can coordinate a
larger quest operation, but each individual quest rule remains ordinary versioned content.

## Objective progression semantics

### Manual progression

The GM or approved host resolves a quest action using a quest mechanic. It validates the quest,
objective, actor authority, state, dependencies, and required context, then returns structural
effects. A completed objective cannot be completed twice; a blocked objective cannot progress
until its named prerequisites are satisfied.

### Event-driven progression

An event-driven objective has an explicit subscription to an already-registered event type. The
subscription uses only the existing bounded filters, tracked entity IDs, and a reusable event
mechanic. The mechanic reads the frozen event plus its declared objective/quest projection, then
returns no effects unless the event is actually relevant.

For example:

    world.component.replaced for a clue entity
      -> tracked objective subscription
      -> quest event evaluator sees clue.found changed to true
      -> objective component changes active to completed
      -> accepted structural event routes quest reconciliation
      -> all required objectives complete, so quest changes active to completed

All of this remains in one root transaction. A guard denial, invalid effect, reaction failure, or
chain-limit breach leaves both objective and quest unchanged.

### Derived progression

A derived objective is recomputed by an explicit reconciliation mechanic after a relevant
structural event. Version 1 supports only known aggregate rules, such as “all required child
objectives are complete.” Do not add arbitrary predicates or a quest query language.

## Delivery slices

### Slice 0 — ratify the first quest and boundaries

Write one small quest with three objectives, one clue, one NPC, one location, one optional
objective, and exactly one event-driven completion. Define who may see each item and which
objective state transitions are supported.

**Acceptance:** every state change has an identified mechanic/event/manual action; no vague
criterion such as “when the story feels finished” is needed for automatic progression.

### Slice 1 — quest and objective components

Create the component definitions, state vocabulary, relationship conventions, catalog records,
and the quest procedure contracts. Add source/schema documentation as documentation first; do not
claim generic component-schema enforcement.

**Acceptance:** a quest and independently inspectable objectives can be created through normal
world effects, queried back, and linked without a quest-specific database migration.

### Slice 2 — manual lifecycle mechanics

Author and validate create, activate, objective advance, reconcile, and archive mechanics. Make
the transition table explicit, including forbidden transitions, dependency checks, duplicate
completion behavior, and correction/rollback procedure.

**Acceptance:** a valid manual advance changes objective and quest state atomically; every invalid
transition is rejected with the same state bytes as before the attempt.

### Slice 3 — evidence, history, and narrative handoff

Link objectives to clues/NPCs/locations, add bounded inspection views, and define the player-facing
quest summary versus GM-only hidden truth. Update the storytelling procedure to query active quests
and unresolved clues at session start/end.

**Acceptance:** a fresh host can answer what is active, what is known, and what remains actionable
using stored state alone, without exposing hidden information in the party summary.

### Slice 4 — event-driven objective

Register the first required event type only if the existing structural event payload cannot
express it; otherwise subscribe to the structural event. Add the subscription, event mechanic,
reconciliation route, event-chain limits, and failure evidence.

**Acceptance:** a qualifying committed event completes exactly one intended objective and may
complete its parent quest; an unrelated event, rejected root change, or repeated event changes
nothing.

### Slice 5 — notifications and reactive consequences

Use the event/notification system for durable quest updates such as an objective appearing,
completing, failing, or being blocked. Keep notifications informational; marking one read must not
alter quest state or emit another quest event.

**Acceptance:** a player/GM can inspect what changed and trace it to the source event without
creating notification loops.

### Slice 6 — human-facing read-only quest journal

Add an active/completed quest list and objective detail to the planned campaign/world website.
Server-render useful initial HTML, then use normal resource refresh/SSE invalidation for current
state. Keep it read-only and ensure visibility policy is enforced before any player login exists.

**Acceptance:** a page refreshes after a committed quest change and never reveals data outside the
viewer’s permitted quest/objective visibility.

### Slice 7 — controlled expansion

After a played quest proves the model, add one feature at a time: quest templates, reward grants,
time/deadline objectives, multi-quest dependency, or a registered workflow for larger quest
operations. Each needs its own state ownership and failure design.

**Acceptance:** every expansion preserves a clear answer to “what changed, why, and which rule
did it?”

## Test matrix

- status values and every allowed/forbidden quest/objective transition;
- relationship integrity, duplicate links, dependency cycle detection, and bounded graph rendering;
- manual advancement validation and full rollback;
- automatic completion from matching, non-matching, repeated, and rolled-back events;
- guard/reaction failure, event-chain limits, causation/correlation, and notification-loop safety;
- hidden versus party-visible summary projection once audience policy exists;
- fresh-session reconstruction of active quests, objectives, clues, and recent milestones;
- catalog import/export, mechanic/version preservation, correction, and replay evidence;
- no raw SQL, browser mutation, or untrusted mechanic can bypass the transition mechanics.

## Non-goals

This plan does not build a hard-coded quest engine, a general-purpose quest condition language,
procedural quest generation, faction/reputation systems, dialogue trees, rewards/XP, timers,
multiplayer visibility, or an interactive quest-map UI. Those are separate content/experience
features once the first persistent quest is played.

## Dependencies

The entity-component, relationship, effect, mechanic, and operation/audit foundations already
exist. Event-driven objectives depend on the completed events/subscriptions runtime. The quest
journal depends on the read-only website plan. Registered workflows and local-model routing may
reduce orchestration effort later, but neither is required for the first manually advanced quest.
