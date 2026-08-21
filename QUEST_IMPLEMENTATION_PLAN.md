# Quest implementation plan

Status: **Base quest roadmap — Q0, Q1, and Q2 verified; Q3 is the next gated feature**
Last updated: 2026-08-20

## Purpose and authority

This is the base roadmap, design boundary, verified-history record, and feature index for quests
and objectives. The active feature dependency plan owns precise implementation requirements. Q0/Q1
feature-plan files and the Q1 receipt are compatibility pointers only; Q2's
[Feature 2 dependency plan](quest/feature-02/QUEST-FEATURE-02-DEPENDENCY-PLAN.md) is authoritative
for Q2 implementation.

Repository development follows [AGENTS.md](AGENTS.md),
[procedure.system.create-feature](catalog/procedures/system/procedure.system.create-feature.md),
[procedure.system.modify](catalog/procedures/system/procedure.system.modify.md), and the
[Terra planning guide](ruleset/dnd2024/TERRA-FEATURE-PLANNING-GUIDE.md). Runtime contracts remain
authoritative in catalog/source, particularly
[procedure.quest.create](catalog/procedures/quest/procedure.quest.create.md), the quest component
definitions, and implementation/tests. This roadmap does not duplicate runtime source.

One reviewed feature slice is implemented at a time. A feature becomes verified only after focused
acceptance, applicable catalog validation, full suite, receipt, and status updates pass. No slice
authorizes its successor automatically.

## Product goal and boundaries

Quests are campaign-scoped, persistent, inspectable game state. A quest records lifecycle,
independent objectives, relevant context, and world evidence without copying mutable campaign/world
truth. The generic kernel gains no quest table, effect type, or hidden state.

The first playable route is deliberately narrow:

- one campaign-owned quest with exactly three initial objectives, including one optional objective;
- multiple viable routes, so no NPC, clue, roll, combat, or location is a single point of failure;
- manual lifecycle and reconciliation before event automation;
- durable audit/event evidence and later bounded trusted-host summaries;
- no reward, item, deadline, template, generated content, party authorization, or player UI claim.

## Ownership model

| Owner | Responsibility | Must not own |
| --- | --- | --- |
| Campaign/C3 | Campaign, chapter, and arc lifecycle; the campaign’s world/reference graph. | Quest lifecycle or copied quest state. |
| Quest | Quest state, objective graph, scoped references, and later manual reconciliation. | World truth, rewards, inventory, or campaign transitions. |
| World | Actors, locations, factions, facts, rumours, secrets, clues, visibility, and their lifecycle. | Quest progress. |
| Item/character/time owners | Possession, delivery, grants, crafting, character state, and the world clock. | Quest counters or copied state. |
| Event runtime | Accepted events, routing, causation, replay limits, and transaction rollback. | Plot criteria or independent progress state. |
| Q3/Q13 readers | Trusted-host projection, then real audience authorization. | A second mutable quest/history store. |

Relationships carry scope, membership, dependencies, and evidence. Components do not contain
duplicated entity IDs, evidence arrays, mutable world snapshots, conditions, rewards, or history.

## Verified Q0 — first quest editorial review

Q0 is a documentation-only, ratified review. It created no catalog/runtime state.

### Fixture

| Field | Ratified value |
| --- | --- |
| Campaign | `campaign.test.sealed-observatory` |
| Arc | `campaign.test.sealed-observatory.arc.observatory` |
| Initial chapter | `campaign.test.sealed-observatory.chapter.opening` |
| Quest | **The Missing Margin** — the old toll ledger points to a missing margin in the market archive; the group decides which evidence deserves trust. |
| GM boundary | The ledger seal and lantern traces may complicate the investigation but do not prescribe an outcome. |

The supported approaches are comparing records/rumours, speaking with Mara or Oren, exploring the
market/observatory route, and inspecting physical traces. Relevant records are Mara and Oren,
market and observatory locations, the toll-ledger fact, observatory-signal rumour, ledger-seal clue,
and the Lantern Compact faction.

| Objective | Required | Audience | Dependency | Meaning |
| --- | --- | --- | --- | --- |
| Trace the Missing Margin | Yes | party | none | Establish the ledger/archive/signal connection. |
| Test the Witnesses | Yes | party | Trace the Missing Margin | Test accounts against independent evidence; neither witness is mandatory. |
| Read the Seal | No | gm | Trace the Missing Margin | Decide whether the physical seal is worth pursuing. |

All three begin dormant. Q0 ratified future manual operations only: offer, accept, set an active
objective to completed/blocked/failed, unblock, reopen, reconcile required objectives, fail,
reopen a terminal quest, and archive an unaccepted offer. Each requires a named host action,
factual reason/evidence, expected-state/replay protection, and one quest owner. No Q0 transition
changes chapter/arc/world state, grants rewards, or scripts player choice. The only candidate
automation is a later Q4 reaction to revealing the Ledger Seal; it is disabled until Q4.

Q0 review rules remain binding for future fixtures: three objectives, two required/one optional,
backward acyclic dependencies, explicit descriptive visibility, at least three viable approaches,
and no single-point completion route.

## Verified Q1 — closed draft creation

Q1 is verified. It creates exactly one campaign-scoped draft quest with exactly three dormant
objectives through the closed `commit(kind: "quest")` capability. Q1 performs no lifecycle
transition, prerequisite evaluation, reward, notification, campaign change, or world change.

### Canonical vocabulary

| Record | Meaning |
| --- | --- |
| `game.core.quest.root` | `status`, `premise`, `summary`, and descriptive `visibility`. Entity Name is the sole title owner. |
| `game.core.quest.objective` | `status`, `actionableSummary`, `required`, `visibility`, and `displayOrder`. Entity Name is the sole title owner. |
| `game.core.quest.in-campaign` | Quest → one active campaign. |
| `game.core.quest.in-arc` | Quest → the campaign’s active arc. |
| `game.core.quest.in-chapter` | Quest → one or two active-or-closed chapters in that arc. |
| `game.core.quest.has-objective` | Quest → objective membership. |
| `game.core.quest.objective.depends-on` | Objective → earlier same-quest prerequisite. |
| `game.core.quest.objective.references` | Objective → existing record with closed `{ role, audience }` data. |

### Closed request and validation

`QuestCreateRequest` supplies a lowercase `quest.*` ID, title/premise/summary, party or GM
visibility, campaign/arc IDs, one or two ordered chapter IDs, and exactly three ordered objectives.
An objective supplies a unique `objective.*` local key, title, actionable summary, required flag,
visibility, display order 1–3, earlier prerequisite keys, and zero to five references. Child IDs
are derived as `<questId>.<objectiveLocalKey>`; callers cannot supply effects, child IDs, link
data, lifecycle operations, or audit data.

Validation rejects duplicate IDs, malformed/extra surface fields, duplicate chapters/references/
dependencies, non-C3 context, inactive/cross-scope endpoints, forward/self dependencies, and
party exposure of secrets, unrevealed clues, or GM-only material. It proves:

1. one active campaign root and exactly one linked active world;
2. one active selected arc linked from that campaign;
3. one or two selected active-or-closed chapters linked from that campaign, each with exactly one
   chapter-in-arc edge to the selected arc;
4. active motive-bearing actors referenced by the campaign; active contained locations; active
   world-linked factions; and active world-linked facts, rumours, secrets, or clues;
5. party references only to public/party material, never secrets or unrevealed clues.

The generated effect order is quest entity, root component, campaign/arc/chapter links, then each
objective in display order with its component, membership, prerequisites, and references. The
ratified fixture produces 19 structural effects/events; the two-chapter boundary produces 20.
Failures use stable codes and leave no partial quest graph, structural event, or successful audit.
Every MCP rejection returns a callable quest-create recovery call.

### Q1 evidence

- Q1 focused tests: 5 passed, covering readback, replay, visibility, bad context, two chapters,
  and create-only surface rejection.
- Capability/guard/protocol coverage: 17 passed.
- Catalog validation: 197 records valid; four non-blocking overlap warnings; no live data touched.
- Serialized full suite: 472 passed.
- No persistent database import occurred.

## Q2 — manual lifecycle and reconciliation

Status: **Verified — Q2 manual lifecycle and reconciliation is complete.**

The detailed dependency analysis, closed input/result/error contract, implementation sequence,
acceptance matrix, and Q2.1–Q2.3 stop gates are in the
[Quest Feature 2 dependency plan](quest/feature-02/QUEST-FEATURE-02-DEPENDENCY-PLAN.md).
The Q2.1–Q2.3 receipts record the accepted lifecycle boundary. Q3 remains a separately planned
history/projection feature and may not create a second mutable quest state store.

## Future roadmap

| Feature | Capability | Gate |
| --- | --- | --- |
| Q3 | Bounded trusted-host quest/evidence/history projection and storytelling handoff. See the [Q3 dependency plan](quest/feature-03/QUEST-FEATURE-03-DEPENDENCY-PLAN.md). | Q3.0 must confirm the procedure/query/result boundary; Q3.1 then needs verified Q2, while Q3.2 waits for S1. C4 is a later consumer. |
| Q4 | One event-driven objective transition. | Played manual Q2 behavior; use the normal Q2 owner. |
| Q5 | Selected informational quest notifications. | Verified Q4 and notification evidence. |
| Q6 | Trusted-GM quest journal/UI. | Verified Q3 and stable read/UI contracts. |
| Q7 | Correction, replay, versioning, and second-fixture gate. | Played Q3/Q4 quest and catalog/snapshot evidence. |
| Q8 | One current-possession item objective. | Verified item ownership and played manual quest. |
| Q9 | One atomic item/character reward grant. | Q2/Q7 plus grant owner. |
| Q10 | One world-clock deadline. | Q4 and a played non-timed quest. |
| Q11 | Explicit inter-quest dependency chain. | Q7 and multi-quest play evidence. |
| Q12 | One branching resolution creating a campaign opportunity. | Q7/C4 ownership confirmation. |
| Q13 | Party/player authorization and sharing. | Q3/Q6 plus authentication/audience policy. |
| Q14 | Schema-bound generated quest proposal for host review. | Q7/Q15 and review authority. |
| Q15 | Reusable quest-definition/template content. | Q7 and two played objective families. |

Candidate mechanics are intentionally distinct by source of truth: current possession, historical
acquisition, delivery, reach/location, discovery, social evidence, checks/challenges, encounter
results, escort, crafting, composite all/any/n-of groups, hidden objectives, contribution,
abandon/expiry/retry, repeatable bounties, reward selection, offer boards, tracking/map hints, GM
authoring, and content packs/migrations. A subscriber may trigger re-evaluation, but never owns a
blind counter or bypasses the source subsystem’s transaction.

## Cross-subsystem rules

- Item delivery and possession read item-owner state; historical acquisition retains immutable,
  deduplicated event receipts rather than an incrementing integer.
- Deadlines read the world root clock only.
- Rewards route through item/character grants only.
- Descriptive `party`/`gm` visibility is not authorization; Q13 owns caller access control.
- Event reactions consume frozen accepted events and invoke Q2; they never mutate quest state
  independently.
- Campaign closure and quest completion never imply each other. C4 consumes Q3 summaries only.

## Planning and acceptance standard

Every future quest feature must have one dependency-plan document with one owner, one lowest
executable slice, closed input/state semantics, canonical ordering/IDs, derived effects/events,
stable errors/recovery, cleanup, and focused/full verification. It must cover happy, boundary,
malformed, missing/corrupt, scope/visibility, replay, transaction failure, routing,
readback/isolation, and appropriate repository checks.

Revise the feature plan before implementation if it needs a new permanent ID/schema, public
command, migration, event type, owner, transaction boundary, or semantic decision. Do not use raw
effects, copied world/campaign state, placeholder conditions, or a second quest owner to bridge an
unplanned gap.
