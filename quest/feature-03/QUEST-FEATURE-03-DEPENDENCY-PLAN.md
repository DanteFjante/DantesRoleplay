# Quest Feature 3 dependency plan — bounded trusted-host summary and lifecycle timeline

Status: **Q3.0–Q3.1 are implemented and accepted. Q3.2 remains deferred until its separate
storytelling-handoff prerequisite is accepted.**
Last updated: 2026-08-20

## Execution rule

This is Q3's authoritative repository dependency plan. It implements the base
[Quest implementation plan](../../QUEST_IMPLEMENTATION_PLAN.md) and follows [AGENTS.md](../../AGENTS.md),
`procedure.system.create-feature`, `procedure.system.modify`, and `procedure.mcp.add-tool`.
Implement one accepted slice, write its receipt, and stop. Do not import the persistent database
during ordinary repository development.

## Target capability and boundary

A trusted host can read one active Q2 quest as a fixed bounded handoff: its current root and
objective state, authored evidence-reference metadata, and a recent structural lifecycle timeline.
The result is derived from the authoritative quest graph and event ledger, never stored as a
second mutable quest/history record.

Included: one fixed read by active quest ID; current root/objective state; at most three objectives,
each with at most five directed evidence-reference metadata records; at most twelve recent accepted
status transitions; and a trusted-host storytelling handoff only after independent S1 verification.

Excluded: player/party authorization, request-selected audience, player discovery, UI, free-form
graph/history queries, raw event payloads, audit intent/reason parsing, recap prose, cache, writes,
notifications, rewards, automation, campaign links/digest, session state, and correction/versioning.
C4 consumes this summary later; C5 owns real GM/party policy; Q7 owns correction/replay work.

## Rule basis and repository inventory

Q3 is campaign workflow/read-model design, not an SRD rule; no D&D source locator applies.

| Dependency | Status and evidence |
| --- | --- |
| Q2 graph/lifecycle | Implemented: Q2.1–Q2.3 receipts prove one root, three objectives, atomic status changes, audit correlation, and structural events. |
| Quest references | Implemented: `QuestCreator` creates directed `game.core.quest.objective.references` records with closed `role`/`audience` metadata. |
| Event ledger | Implemented: `IEventLedger.FindAsync` filters accepted rows by affected entity; `GetAsync` returns immutable versioned payloads. |
| Operation audit | Implemented, but unsuitable as Q3 source: Q2 reason is human text in `Operation.Summary`; Q3 must not parse it. |
| Trusted-host read pattern | Implemented: `CampaignResumeReader` and graph recipes use fixed bounded shapes rather than arbitrary graph/history APIs. |
| Current quest read | Implemented: `query(kind: "quest-summary", id: "quest.*")` delegates to the fixed Q3.1 reader. Raw `entities`, `events`, and `history` remain broader trusted administrative reads, not the Q3 handoff. |
| C4 consumer | Planned: [Campaign Feature 4](../../campaign/feature-04/CAMPAIGN-FEATURE-04-DEPENDENCY-PLAN.md) requires a quest-owned bounded summary and must not copy quest data. |
| S1 storytelling procedure | Implemented at `storytelling/feature-01/`; global acceptance remains blocked by an unrelated repository test failure, so Q3 cannot claim a storytelling handoff yet. |

## Ownership and non-decisions

| Concern | Q3 reads | Q3 never owns |
| --- | --- | --- |
| Current state | Q2 root/objective components; Entity Name remains title. | Cache, copied status/title/context, or component. |
| Evidence | Reference target ID, role, and audience metadata only. | Endpoint content, inferred truth, discovery, or evidence list. |
| Timeline | Accepted root/objective `world.component.replaced` status changes. | Event writing, raw payloads, audit parsing, journal, or narrative. |
| Visibility | Authored labels as trusted-host descriptive metadata. | Security filtering, audience input, or player discovery; C5 owns policy. |
| Consumer | One bounded quest-owner result for C4/S1. | Campaign digest/link, session state, or generated prose. |

## Required Q3.0 confirmation

Approve these permanent/public meanings together before Q3.1 implementation:

| Proposed artifact | Meaning |
| --- | --- |
| `procedure.quest.inspect` | Governs one bounded trusted-host quest summary and status-timeline interpretation. |
| `query(kind: "quest-summary", id: "quest.*")` | The only fixed public read. It accepts quest ID only: no audience, history limit/range, component list, include-hidden flag, graph depth, sort, or arbitrary IDs. |
| `QuestSummary` | Current root, at most three ordered objectives, reference metadata, at most twelve recent transitions, and a fixed trust-boundary statement. |
| Event rule | Retain only accepted `world.component.replaced` rows naming a current quest root/objective with valid before/after lifecycle status; never parse `Operation.Summary`. |

If this query kind is rejected, do not stretch generic `entities`, `events`, or `history` with new
filters. Revise the plan and select a fixed existing consumer instead.

## Closed result and algorithm

```text
QuestSummary
  questId, title, status, summary, visibility
  objectives[0..3]
    id, title, status, actionableSummary, required, visibility, displayOrder
    evidence[0..5]: targetId, role, audience
  recentTransitions[0..12]
    eventId, rootOperationId, timestamp, sequence, entityId, recordKind,
    beforeStatus, afterStatus
  trustBoundary
```

Collections are `[]`, never `null`. Objective order is display order then ID; evidence order is
role, audience, then target ID; timeline order is newest timestamp, descending sequence, then event
ID. The reader returns unavailable/failure, never a widened partial result, for unknown, inactive,
terminal, archived, malformed, duplicate, dangling, out-of-order, foreign-dependency, or invalid-
context quest state.

1. Resolve one active Q2 quest using Q2's one-root, three-objective, backwards-dependency, and
   active campaign/world/arc/chapter rules.
2. Read only result fields. Traverse only outgoing objective-reference links; require unique closed
   `{ role, audience }` data and an existing target ID. Never load endpoint components or names.
3. Read ledger rows for the root and returned objective IDs; de-duplicate IDs; retain only valid
   structural status replacements; order and cap at twelve. No audit prose is read.
4. Return a fixed statement that visibility is descriptive, not authorization, and factual reasons
   remain in the ordinary trusted administrative audit history.
5. Write no game state. The routine query audit must not include the response or hidden payload.

## Recursive dependency analysis

```text
Q3 trusted-host quest summary [Q3.1 implemented and accepted]
├─ Q2 authoritative graph/lifecycle/events                    [implemented]
├─ fixed result/procedure/query semantics                      [implemented: Q3.0]
│  └─ QuestSummaryReader + quest-summary query                 [implemented: Q3.1]
├─ S1 storytelling publication/procedure                       [implemented; acceptance pending]
│  └─ trusted-host handoff wording                             [Q3.2 blocked until acceptance]
├─ C4 campaign quest context/digest                            [blocked consumer]
└─ authenticated audience filtering                            [excluded: C5]
```

Q3.1 has no migration, component, event type, subscription, mechanic, fixture quest, write
operation, or commit kind. It is accepted; see
[`QUEST-FEATURE-03-SLICE-1-RECEIPT.md`](QUEST-FEATURE-03-SLICE-1-RECEIPT.md). Q3.2 adds no query
or state mutation.

## Slice order and stop gates

| Slice | Prerequisite | Exit gate |
| --- | --- | --- |
| Q3.0 — semantic/public confirmation | Accepted | `procedure.quest.inspect`, `quest-summary`, its active-only scope, fixed result, twelve-transition cap, and descriptive-only visibility are implemented. |
| Q3.1 — bounded owner summary | Implemented and accepted | Fresh imported Q2 fixture returns only fixed current/evidence/timeline data; malformed/rejected reads write no game state. |
| Q3.2 — storytelling handoff | Q3.1 and verified S1 | Storytelling procedure directs trusted hosts to the exact Q3 read without claiming party authorization or recap prose; stop. |

## Q3.1 — bounded owner summary

### Runtime artifacts and governing contracts

Add `catalog/procedures/quest/procedure.quest.inspect.md`; typed summary contracts; one
`QuestSummaryReader`/interface registered in data access; thin `QuestTools` query delegation;
`quest-summary` in `QueryTool` and `VerbSurface`; focused `QuestFeature3Tests`; and affected
DI/surface/guard/protocol tests. Revise no Q2 writer except a successor link. Do not add a component,
fixture quest, event type, subscription, mechanic, migration, cache, or commit kind.

Immediately before writing, re-read `procedure.system.create-feature`, `procedure.system.modify`,
`procedure.mcp.add-tool`, `procedure.quest.modify`, `procedure.event.inspect`, and this plan.

### Acceptance matrix

| Case | Exact expected result |
| --- | --- |
| Active Q2 fixture | Exact root fields; three display-ordered objectives; reference metadata only; fixed trust boundary. |
| Timeline | Offer, accept, completion, and reconciliation retain only valid replacements, in canonical latest-first order, capped at twelve with no duplicate event ID. |
| Evidence boundary | A GM-labelled clue/reference returns ID/role/audience only—never endpoint name, component JSON, hidden status, provenance, or raw link data. |
| No-change reconcile | `NO_RECONCILIATION_CHANGE` produces no timeline entry because no accepted structural event exists. |
| Closed input | Missing/null/extra/alternate ID/filter/audience/history/component/sort input rejects with literal callable recovery. |
| Missing/corrupt state | Unknown, offered/terminal/archived, malformed root/objective, duplicate membership, foreign/forward dependency, dangling reference, or invalid C3 context returns stable unavailable/failure with no partial data. |
| Ledger fault | Wrong type/component, absent before/after, malformed JSON, foreign entity, invalid status, or duplicate row is omitted, never crashes or fabricates a transition. |
| Isolation/readback | No entity/component/link/event/notification/cache write; fresh import/session reconstructs same result; query audit contains no response payload. |
| Repository gate | Focused tests, catalog validation, surface guards, protocol walk, full suite, diff check. |

### Exit gate

Q3.1 is verified only when a fresh imported active Q2 fixture produces the exact bounded trusted-
host summary and status timeline from graph/ledger state, while malformed or hidden data cannot
widen the result or mutate state. Write a receipt and stop before Q3.2.

## Q3.2 — trusted-host storytelling handoff

After S1 exists, revise only its storytelling procedure and Q3 tests. It names
`query(kind: "quest-summary", id: "...")` as the current-quest handoff source, respects descriptive
labels, and says it is neither player authorization nor a recap generator. It adds no fields,
mutable story record, campaign digest, reaction, or generated prose.

Prove a fresh host can retrieve the procedure then the fixed Q3 summary without a claim of party
filtering, lifecycle mutation, reward, automation, or transcript persistence. Record Q3.2 and stop;
C4 receives its own implementation pass.

## Change rule

Revise before implementation if confirmation changes public result/contract, Q2 no longer gives one
active campaign context, ledger data is insufficient, a consumer needs raw evidence, real policy is
required, or C4 needs another bounded shape. Never solve a gap by parsing human audit summaries,
copying quest state into campaign/session data, accepting caller-selected history/visibility filters,
or exposing raw entity/event payloads.
