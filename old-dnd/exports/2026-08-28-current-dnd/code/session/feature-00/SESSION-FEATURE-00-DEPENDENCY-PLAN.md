# Session Feature S0 dependency plan — ratify the first continuity boundary

> **D&D implementation reference:** When this work includes D&D 2024 mechanics, inspect [Foundry VTT dnd5e](https://github.com/foundryvtt/dnd5e) before designing or coding. Use it as a licensed engineering reference—not a direct dependency or rules authority—while retaining the exact `source.dnd2024.srd-5.2.1` locator and all applicable MIT, CC-BY, and asset-license notices for any reused material.

Status: **Ratified; see `SESSION-FEATURE-00-RATIFICATION.md`. No session runtime artifact, permanent ID, schema, public operation, or catalog record is created by S0.**  
Last updated: 2026-08-20

## Execution rule

This plan follows AGENTS.md, `procedure.system.create-feature`, `procedure.system.modify`, `procedure.mcp.add-tool`, the [Session Operations Plan](../../SESSION_OPERATIONS_PLAN.md), [Campaign Feature C8](../../campaign/feature-08/CAMPAIGN-FEATURE-08-DEPENDENCY-PLAN.md), [Campaign Creation Plan](../../CAMPAIGN_CREATION_PLAN.md), and [Story-first Roadmap](../../STORY_FIRST_ROADMAP.md). It is a repository planning artifact only and writes no runtime state.

S0 establishes the exact product and ownership boundary that C8 must implement. It does not convert a planning choice into a session record, make a database checkpoint, publish `procedure.campaign.session`, add a `commit`/`query` kind, or decide rules/world/quest/character outcomes.

## Target capability

The host ratifies one complete, testable first session-continuity fixture: which campaign state is eligible, what a session start/resume/end is allowed to record, which factual owner projections form the bounded context/recap, which audience is actually supported, and whether the initial checkpoint outcome is read-only evidence or a real restore. A later implementer can create C8 without guessing policy or storing duplicate state.

The initial fixture is one trusted-host campaign session. It is not a generic table-management system, a player-authenticated experience, an encounter, a browser session, or a promise to restore arbitrary game state.

### Included

- Ratification of the first campaign continuity fixture and its authoritative campaign/world/chapter/arc/quest/knowledge projection sources.
- A closed start/resume/end lifecycle policy, one-active-session decision, canonical summary/context field inventory, null/empty/omission behavior, and factual-versus-narrative rule.
- An explicit audience/exposure decision for the initial C8 reader, including whether C5 authorization is a hard prerequisite for any human-facing projection.
- A scoped checkpoint/interruption/restore decision resolving C8’s current “snapshot/restore guidance and evidence” wording.
- One owner map, dependency gate list, acceptance fixture, change-control rule, and handoff record required before C8 names artifacts.

### Excluded

- Creation of session entities/components/relationships, checkpoint/snapshot files, code, tests, catalog imports, persistent database writes, or a public session command.
- Selection or mutation of chapter/arc, quest/objective, world/faction/clue/location/clock, character/item, encounter, ruleset action, or event state.
- Player roster/control, accounts/identity, permission enforcement, player-safe data claims, browser writes, live collaboration, transcript storage/search, recap generation, scheduled jobs, or restore/fork execution.
- Assumption that an existing MCP read-evidence window, HTTP cookie, WebSocket, or chat has the same lifecycle as a game session.

## Existing-contract findings

| Finding | Evidence and S0 consequence |
| --- | --- |
| C8 is already the initial session implementation owner | C8 owns session records and summary lifecycle. S0 must not create a parallel session feature or preempt its permanent vocabulary. |
| C3 provides a trusted-host campaign resume digest | `procedure.campaign.chapter` governs `query(kind: "campaign-resume")`; its existing continuity result is a candidate source, not a session-owned copy. |
| C4 owns campaign-to-quest context and Q3 owns quest/objective state | S0 must identify the exact owner-approved quest projection or explicitly exclude quest context from the first fixture; a recap cannot read raw quest state. |
| C5 is the audience-filtered projection boundary but awaits real authorization | Initial trusted-host use must not be described as player-safe. Any human/player-facing C8 read path remains blocked until C5/identity policy is verified. |
| World, ruleset, character, items, and events own play truth | Session lifecycle cannot advance time, move a character, change an objective, grant an item, resolve a rule, or re-log generic activity. It may reference approved post-commit projections only. |
| C11 consumes C8 checkpoint evidence for read-only fork preview | S0 must choose an explicit checkpoint identity/scope/evidence contract; C11 may not infer one from recap prose or operation history. |
| C8 currently says both “snapshot/restore guidance and evidence” and “checkpoint restores exactly its declared scope” | This is ambiguous about whether C8 itself performs a restore write. S0 must resolve it before C8 implementation; it cannot be left as an implementation interpretation. |

## Required host-ratification record

Before C8 begins, record the following answers in an S0 implementation receipt or an approved amendment to this plan. Each answer must name the owning procedure/component/projection and, where relevant, the exact fixture identity/version. “Use the current state” or “we will decide later” is not sufficient.

### 1. Fixture campaign and continuity sources

- The exact campaign fixture ID, campaign lifecycle requirement, and scope derivation path.
- The exact C3 chapter/arc resume projection/version used at session start/resume/end.
- Whether C4/Q3 quest context is required for the first session. If yes, name the bounded C4/Q3 projection and its maximum cardinality; if no, state that S1–S3 deliberately omit quest context until C4 is verified.
- The exact World projection/readings allowed (for example, approved current location/clock/factual context) and the rule for unavailable/archived/cross-scope sources.
- Whether characters/items are absent from the initial fixture or which owner-approved projections are included. S0 must not imply S5 participant roster support.

### 2. Session lifecycle and concurrency policy

- Exact durable C8 lifecycle state names and allowed transitions for `start`, `resume`, and `end`.
- Whether one active session is global or exactly one per campaign. The recommended first boundary is **one active session per active campaign**, with unrelated campaigns independent; confirm or replace it explicitly.
- Canonical start/end request identifiers, stale guards, replay/idempotence result, cancellation/timeout behavior, and the recovery call for no-active, already-active, stale, interrupted, archived, or invalid campaign states.
- Whether a completed session is immutable and retained, or whether any correction/archive retention policy exists. No delete/purge/reopen is implied without a named successor.

### 3. Factual context and recap contract

- The bounded ordered field list, maximum count/length, source/projection version, and missing/null/empty/omitted semantics for start/resume/end output and stored summary.
- For every field, whether it is a source reference, a bounded owner projection, or C8-owned factual session metadata. It may not be a copied component, raw source prose, generic event list, audit ID, chat transcript, model memory, or unconstrained note.
- A canonical ordering rule for repeated records; a stable safe omission/reason rule for unavailable/unauthorised sources.
- The distinction between factual recap and any later attributed narrative. The initial C8 summary is factual only; no generated prose becomes campaign truth.

### 4. Audience and consumer boundary

- Whether S1–S3 output is trusted-host/GM-only until C5 is verified. This is the default and must be changed only with confirmed identity/audience policy evidence.
- Whether the C8 “human reader” means a trusted local read-only operator or an authenticated player/GM projection; name the C5/Website/API contract if the latter.
- Exact MCP/HTTP consumer boundary, error/redaction envelope, refresh rule, and proof that no browser or client owns a session write path.

### 5. Checkpoint, interruption, and restore boundary

Choose exactly one initial policy:

1. **Recommended — checkpoint evidence only:** C8 creates/records a named, auditable checkpoint reference with a declared scope and supports readback/recovery evidence. It performs no restore write. A later S4 dependency plan owns any restore transaction after every domain is classified.
2. **Broader — C8 restore:** C8 may restore exactly the declared scope. This requires the host to ratify the restore root owner, every copied/referenced domain, newer-state conflict/recovery rules, rollback, audit, and the C11 fork relationship before implementation. No generic database restore is allowed.

The record must specify checkpoint identity, when it is taken (start/end/both/other), included/referenced/unsupported domains, owner of stored bytes/reference, retention, scope/version evidence, interrupted-session recovery, and how a fresh host verifies it. A checkpoint is never inferred from a chat, an operation ID alone, or a database backup of unknown scope.

### 6. Atomicity and evidence

- The C8 root transaction owner and ordered owned effects; external action roots remain independent.
- Which structural events/notifications are emitted, their owner, and whether failure audit is separate under the current ActionRunner policy.
- The exact fresh-host proof: start, perform at least one separately governed committed play change, end, open a fresh host, resume, and compare each approved owner projection/summary field.
- Required negative cases: duplicate start, concurrent active session, stale/replayed end, invalid campaign/scope, unavailable source, redaction failure, malformed summary/checkpoint, child/effect/guard/reaction/audit failure, cancellation, timeout, corrupt record, and no external-owner mutation.

## Dependency graph and next slice

~~~text
S0 ratified first continuity fixture
├─ C3 trusted-host chapter/arc resume projection                    [existing prerequisite]
├─ C4/Q3 quest context or explicit first-fixture exclusion          [must be decided]
├─ C5/identity audience policy or trusted-host-only decision        [must be decided]
├─ checkpoint evidence-only vs restore-root/scope decision          [must be decided]
├─ Campaign session transaction/audit/event owner                   [must be confirmed]
└─ C8 session records and summary lifecycle                         [next implementation plan]
   └─ S1–S3 start/resume/end factual continuity
      └─ S4 checkpoint/restore after explicit scope proof
~~~

**Only next slice after S0:** amend C8 with the ratified state/input/summary/checkpoint decisions, then implement its lowest independent session-record/lifecycle slice. The ratification deliberately omits C4/Q3 and C5 from the first trusted-host fixture; any later added owner remains a separate amendment.

## Acceptance matrix

| Case | Exact expected result |
| --- | --- |
| Complete fixture | One named campaign, source projection inventory, lifecycle policy, summary fields, audience, checkpoint policy, owner map, and fresh-host scenario are recorded with no unresolved “TBD” that changes C8 schema or behavior. |
| Ownership isolation | Every summary/context field has one current owner. Session code is explicitly forbidden from storing duplicate world/quest/character/item/time/mechanical truth. |
| Checkpoint clarity | The host selects evidence-only checkpoint or a fully scoped restore root. The ambiguous current C8 wording is resolved before artifacts are named. |
| Audience truth | Initial trusted-host output is not called player-safe; any player/human filtering names verified C5/identity/Website dependencies. |
| Failure policy | Active-session, stale/replay, corrupt, unavailable source, interruption, cancellation, timeout, audit/event, and external-owner mutation behavior are decided with recovery calls. |
| Scope breadth | One campaign fixture proves a reusable C8 session contract. It does not silently authorize roster, gameplay, narrative, player control, multi-user, or remote capabilities, whose owning S5–S9 features remain named. |
| No runtime mutation | S0 produces only planning/receipt evidence. No database/catalog component, session, checkpoint, public surface, operation, event, or source state is created. |

## Evidence and change control

The S0 receipt records the confirmed answers, links/versions of all consulted owners, rationale for the checkpoint policy, fixture source inventory, canonical summary schema decision, negative-case list, and the precise C8 amendment/next leaf. It does not contain a session transcript, secrets, unapproved campaign facts, source rules, credentials, raw effects, or persistent operation IDs.

Amend S0 before naming a C8 component/procedure/mechanic, changing the selected campaign fixture, including a new owner domain, supporting player-readable data, selecting restore instead of evidence-only checkpoint, allowing more than one active session, adding a roster/gameplay/narrative/browser/collaboration capability, or introducing a public transport. Those boundaries belong to C8/S4–S9 and their named Campaign, Quest, World, Character, Items, ruleset, identity, Website/API, snapshot, and `procedure.mcp.add-tool` owners.
