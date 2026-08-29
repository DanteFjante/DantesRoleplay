# Campaign Feature 12 dependency plan — parallel and branching arc continuity

> **D&D implementation reference:** When this work includes D&D 2024 mechanics, inspect [Foundry VTT dnd5e](https://github.com/foundryvtt/dnd5e) before designing or coding. Use it as a licensed engineering reference—not a direct dependency or rules authority—while retaining the exact `source.dnd2024.srd-5.2.1` locator and all applicable MIT, CC-BY, and asset-license notices for any reused material.

Status: **Planned; blocked by verified C3 continuity, a played C3 campaign, and semantic confirmation**  
Last updated: 2026-08-20

## Target capability

A trusted host can continue one campaign through more than one active story arc and can deliberately
branch an active chapter into separate active next chapters. Every chapter remains in exactly one
campaign and one arc; every successor relationship is explicit, same-campaign, same-arc, and
acyclic. A fresh resume view reports all currently active arcs and their active chapter threads
within fixed bounds.

This replaces neither C3's first continuity proof nor C4's quest contract. C3 remains the owner of
chapter/arc lifecycle and the transaction runner; C12 widens its cardinality and transition rules
only after the one-arc foundation has evidence.

### Included

- Up to eight active arcs in one campaign and up to eight active chapters in each active arc.
- One explicit optional predecessor link for each chapter, allowing a closed or active chapter to
  have multiple successor chapters.
- Expected-state create, advance, branch, close, and conclude operations under C3's existing
  transaction and event/audit owner.
- A bounded trusted-host resume projection with canonical ordering for active arcs, their active
  chapters, and recent closed milestones.
- Migration/readback compatibility from a valid C3 one-arc campaign.

### Excluded

- Cross-campaign or cross-arc chapter links; arbitrary graph editing; merging branches; automatic
  transitions; quest lifecycle changes; player-safe filtering; AI decisions; maps, clocks,
  encounters, rewards, parties, or campaign forking/snapshots.
- Claiming that a quest can span or move between arcs. C4 retains its one explicit campaign/arc
  relationship until a separately confirmed quest-integration revision exists.

## Confirmation boundary

The following changes are semantic boundaries. Confirm them together before any schema, migration,
public-surface, or runtime change:

| Decision | Proposed rule |
| --- | --- |
| Active-arc cardinality | A campaign may have 0–8 active arcs. An arc is active until resolved or abandoned. |
| Active-chapter cardinality | An active arc may have 0–8 active chapters. An active chapter belongs to exactly one active arc. |
| Advance | Closes one expected active chapter and creates exactly one active successor in its same arc. |
| Branch | Closes one expected active chapter and creates 2–8 active successors in its same arc. |
| Predecessor | A successor may have exactly one predecessor; an earlier chapter may have many successors. Links must be acyclic. |
| Close/conclude | Closing a chapter affects only that chapter. Concluding an arc requires no active chapters in it. |
| Resume bounds | Return at most eight active arcs, eight active chapters per arc, and five closed milestones per arc in canonical ID order. |

## Ownership and proposed vocabulary

| Artifact | Proposed meaning |
| --- | --- |
| game.core.campaign.chapter.follows | Directed empty-data link from successor chapter to one predecessor chapter in the same campaign and arc. |
| procedure.campaign.chapter | Existing C3 owner, revised for cardinality, predecessor validation, branch lifecycle, migration, and resume projection. |
| branch-chapter | C3 commit operation that closes one expected active parent and creates 2–8 explicitly supplied successor chapter records and follows links. |
| create-arc | C3 commit operation that creates one active arc with zero active chapters; it never creates a duplicate campaign root. |

No new campaign root, copied world state, root child arrays, or alternative transaction owner is
permitted. Existing C3 has-chapter, has-arc, and chapter.in-arc links remain authoritative.

## Data, operations, and algorithm

C3 chapter and arc component schemas remain closed; C12 changes their **relationship cardinality**
and validates all lifecycle operations against the confirmed caps. branch-chapter input carries a
campaign ID, expected active parent chapter ID, and 2–8 closed successor seed records using C3's
title/question/context grammar. The server derives all entity IDs from the campaign and local keys.

For every operation:

1. Resolve the campaign, its single world link, all referenced arcs/chapters, and expected states
   inside C3's one outer transaction.
2. Reject wrong campaign/arc, terminal parent, duplicate local key, malformed/missing/reversed
   link, cap overflow, self-link, cycle, or stale expected state before any effect.
3. Build the ordered C3 effects: component changes, campaign/arc links, predecessor links, normal
   structural events, and one success audit. No branch uses a raw graph write.
4. Dry-run and commit once. Any failure in effects, eventing, auditing, subscription, cancellation,
   or migration rolls back every change and success evidence.
5. The resume view derives its bounded branch shape from persisted links and lifecycle state; it
   never stores or returns caller-selected history.

## Dependency order and tests

~~~text
C12 parallel and branching arc continuity
├─ C3 one-arc lifecycle/resume evidence                         [must be verified and played]
├─ C4 quest-in-arc read compatibility                            [must be inspected]
├─ confirmed cardinality, follows link, operations, migration    [semantic boundary]
│  └─ Slice 1: schema/link validation and multi-arc operations
└─ verified Slice 1
   └─ Slice 2: branch operation and bounded fresh resume view
~~~

| Case | Exact expected result |
| --- | --- |
| C3 compatibility | A valid one-arc C3 campaign imports/reads unchanged and remains a legal one-arc case. |
| Parallel arcs | Two active arcs with separate active chapters coexist; a chapter can never link to the wrong arc or campaign. |
| Branch | One active parent closes, 2–8 active successors link back to it, and all effects/events/audit commit atomically. |
| Invalid graph | Cycle, self-link, duplicate key, over-cap request, stale parent, cross-arc predecessor, or repeated branch rejects with no partial state. |
| Arc conclusion | An arc with active chapters cannot conclude; closing its final chapters permits its independent conclusion without changing another arc. |
| Quest boundary | Existing C4 quest context stays in its linked arc and no operation silently moves or duplicates it. |
| Resume | A fresh host gets canonically ordered, bounded active arc/thread data and correctly scoped recent milestones. |
| Rollback | Injected component/link/event/audit/notification/cancellation failure leaves no new arcs, chapters, links, events, or success audit. |

## Completion boundary

C12 is complete when the verified C3 owner can preserve and resume several independent campaign
threads and an explicit chapter branch without ambiguity or partial writes. Stop before cross-arc
quest changes, player views, automatic story advancement, or campaign forks.
