# Session operations plan and roadmap

> **D&D implementation reference:** When this work includes D&D 2024 mechanics, inspect [Foundry VTT dnd5e](https://github.com/foundryvtt/dnd5e) before designing or coding. Use it as a licensed engineering reference—not a direct dependency or rules authority—while retaining the exact `source.dnd2024.srd-5.2.1` locator and all applicable MIT, CC-BY, and asset-license notices for any reused material.

Status: **Authoritative session-product roadmap — planning only; Campaign Feature C8 remains the first bounded implementation plan.**  
Last updated: 2026-08-20

## Execution rule

Use [ARCHITECTURE.md](ARCHITECTURE.md) for ownership, [CAMPAIGN_CREATION_PLAN.md](CAMPAIGN_CREATION_PLAN.md) for campaign boundaries, [STORY_FIRST_ROADMAP.md](STORY_FIRST_ROADMAP.md) for first-play sequencing, [Campaign Feature C8](campaign/feature-08/CAMPAIGN-FEATURE-08-DEPENDENCY-PLAN.md) for the first concrete session slice, and a populated [SUBSYSTEM_IMPLEMENTATION_HANDOFF.md](SUBSYSTEM_IMPLEMENTATION_HANDOFF.md) before implementation.

This roadmap is the single planning authority for session scope, ownership, ordering, and Session Features S0–S9. It authorizes no runtime artifact by itself. A linked dependency plan must define each feature's permanent IDs, schemas, inputs, event/audit behavior, implementation slices, and tests after its owners are verified.

“Game session” here means a durable campaign play period. It is not the stateless MCP read-evidence window established by `orient`, a browser login session, a database connection, a chat transcript, a combat encounter, or a generic workflow execution. Those concepts have separate owners and must not share a record merely because they are all called a session.

## Goal

Let a campaign start, resume, conduct, pause safely, and end a play session without relying on prior chat history. At every boundary, a fresh host reconstructs the current playable context from durable campaign/world/quest/character state and a bounded factual session record. The session record identifies and summarizes what occurred; it never becomes a second source of truth for the world, quest, character, item, time, or mechanical result.

The first release is trusted-host campaign continuity: one active session, factual start/resume/end context, and read-only current-state view. Authenticated player participation, richer table activity, narrative artifacts, multi-user live play, and remote hosting follow only after their real dependencies exist.

## Product boundary

### Included

- Session lifecycle as a campaign-owned, auditable record: start, resume, end, and later bounded interruption/recovery.
- A fixed, bounded factual context and recap assembled from authoritative owner projections.
- Session checkpoint/snapshot evidence with explicit declared scope and later restore/fork consumers.
- Fresh-host continuity, lifecycle-aware participant/control integration, and read-only human/MCP projections.
- A roadmap for later gameplay handoff, narrative artifacts, player-facing views, and collaboration without making session code own their state.

### Excluded

- Chat transcript memory as authority; AI memory, free-form GM notes, and recap prose as replacements for governed facts.
- Duplicating campaign/world/clock/quest/objective/character/item/mechanic state inside a session component.
- A new global transaction/workflow wrapper around every action, automatic time advancement, combat/encounter turns, scene/quest progression, travel, rest, rewards, or rules adjudication.
- Identity/login/security implementation, player permission inferred from visibility, browser write authority, live multi-user collaboration, remote deployment, or a generic activity log.
- Deleting/restoring arbitrary database state or silently rolling back played history.

## Ownership model

| Concern | Authoritative owner | Session features may do | Session features must not do |
| --- | --- | --- | --- |
| Campaign scope, lifecycle, chapters/arcs, session records | Campaign plans; C8 for initial records | Validate one campaign and write only confirmed session-owned state | Copy campaign state or mutate chapter/arc without its owner |
| World location, clocks, travel, facts, factions, clues | World plans | Read bounded approved projections/readings into context or recap | Advance time, move actors, reveal clues, or restate world truth as session-owned |
| Quests/objectives/evidence | Quest plan and C4 context | Include the owner-approved bounded summary | Change lifecycle/evidence or cache hidden objective truth |
| Characters, items, player control | Character/Items plans; CH13/CH14 | Validate selected active participants and consume safe projections | Store roster copies, possession, player identity, or mechanical state |
| Game actions, dice, effects, events, audit | Ruleset/action/event owners | Correlate committed results through existing history/projections | Select mechanics, invent outcomes, rerun actions, or create a parallel activity/event log |
| Snapshot/restore/fork | C8 confirmation and C11/canonical save owner | Record a checkpoint reference and declared scope only | Deep-copy/restore domains or overwrite newer state without the snapshot owner |
| Identity, authorization, audience | Identity/authorization policy; Campaign C5; CH14 | Request policy-filtered views and validate participant control | Treat party/GM labels, profile fields, or client inputs as permission |
| Website/MCP/read models | CH6/CH8, Website/API plan, C5/C8 | Consume the same bounded read projection | Write session state from a browser or expose raw data/filters |
| Narrative recap/notes | Later S7 narrative-artifact owner | Link attributed noncanonical narrative to factual context | Treat prose as mechanical or historical truth |

Every durable session fact must name its owner and retain a stable reference/projection boundary. A session close may say that an objective became active only if the quest owner did so; it cannot make it active by recording a recap sentence.

## Initial continuity contract

S1–S4 are implemented only through Campaign Feature C8 after its existing confirmations. The session record and `procedure.campaign.session` remain C8-owned; this roadmap intentionally does not pre-name their components, relationship IDs, state schema, summary fields, or transport kind.

The first session contract must establish all of the following before implementation:

- exact lifecycle state names and one-active-session-per-campaign policy;
- start/resume/end input and stale/replay behavior;
- a bounded context/summary field list, canonical order, per-field limits, null/empty semantics, and safe audience projection;
- a distinction between an authoritative source reference/projection and an attributed noncanonical narrative;
- checkpoint/snapshot identifiers, declared scope, creation authority, retention, restore authority, interruption recovery, and relationship to C11 fork preview;
- event/audit correlation and fresh-host readback without copying root operation IDs into session state; and
- proof that session operations do not mutate world, quest, character, item, clock, or gameplay state merely by starting, resuming, or ending.

## Session feature roadmap

| Feature | Capability | Direct prerequisites | Current implementation owner / gate |
| --- | --- | --- | --- |
| S0 | Ratify session boundary and continuity fixture | Campaign C3/C4 context inventory; C5 audience boundary; snapshot/restore decision | **Ratified:** [S0 record](session/feature-00/SESSION-FEATURE-00-RATIFICATION.md) selects a C3-only trusted-host first fixture, omits C4/Q3 and C5, and defers checkpoint evidence to S4. C8 owns the resulting runtime work; S0 created no runtime artifact. |
| S1 | Start and one-active-session lifecycle | Verified C3/C4/C5; S0 state/one-active policy | [S1 plan](session/feature-01/SESSION-FEATURE-01-DEPENDENCY-PLAN.md); C8 Slice 1 creates one campaign-owned session record without changing story/mechanical state. |
| S2 | Resume and fresh-host factual context | Verified S1; owner-approved world/quest/chapter projections | [S2 plan](session/feature-02/SESSION-FEATURE-02-DEPENDENCY-PLAN.md); C8 Slice 1 resume is a bounded projection, never prior-chat replay. |
| S3 | End, factual recap, and continuity receipt | Verified S1–S2; summary/attribution policy | [S3 plan](session/feature-03/SESSION-FEATURE-03-DEPENDENCY-PLAN.md); C8 Slice 1 closes with current factual context, not GM prose as canon. |
| S4 | Checkpoint, interruption, restore, and fork evidence | Verified S1–S3; snapshot/restore owner; C11 domain classification | [S4 plan](session/feature-04/SESSION-FEATURE-04-DEPENDENCY-PLAN.md); C8 evidence first, then a dedicated restore branch only after full scope proof. |
| S5 | Session participant roster and active-character eligibility | CH1 campaign attachment; CH13 lifecycle; CH14 when player-controlled; campaign participation contract | [S5 plan](session/feature-05/SESSION-FEATURE-05-DEPENDENCY-PLAN.md); it references, rather than copies, participant/control state. |
| S6 | Session-to-gameplay handoff and committed activity context | S1–S5; one compatible action owner; ActionRunner/audit-context extension | [S6 plan](session/feature-06/SESSION-FEATURE-06-DEPENDENCY-PLAN.md) consumes committed results and never wraps or replays actions. |
| S7 | Attributed narrative recap and table artifacts | S3 factual recap; trusted-host storage/retention decision; C5/CH14 for player exposure | [S7 plan](session/feature-07/SESSION-FEATURE-07-DEPENDENCY-PLAN.md) first publishes one source-bound noncanonical trusted-host recap. |
| S8 | Player-safe session view and bounded table controls | C5/CH14 authorization; S5/C8; Website/API exposure; S6 plus player-safe action for controls | [S8 plan](session/feature-08/SESSION-FEATURE-08-DEPENDENCY-PLAN.md) first consumes a fixed read-only view; one delegated control is separately gated. |
| S9 | Concurrent/live/remote session collaboration | Accepted S8; remote identity/security/deployment; bounded SSE; one action-owner conflict fixture | [S9 plan](session/feature-09/SESSION-FEATURE-09-DEPENDENCY-PLAN.md) proves remote participant freshness and one action conflict, not multi-host authority. |

S1–S4 are deliberately grouped beneath C8 because one start/resume/end/snapshot contract must be coherent. The later rows are not promises of data structures or APIs: each is a named successor so that a fixture limitation is never mistaken for a permanent capability boundary.

## Dependency flow

~~~text
C3 campaign continuity + C4 quest context + C5 authorized projection
└─ S0 session boundary ratification
   └─ S1 one active session lifecycle
      └─ S2 fresh-host resume context
         └─ S3 end and factual continuity recap
            ├─ S4 checkpoint/interruption/restore evidence ── C11 fork consumer
            ├─ S5 participant roster/control ── CH13/CH14 and campaign attachment
            │  └─ S8 player-safe session view and controls
            ├─ S6 gameplay handoff ── ruleset/world/quest action owners
            ├─ S7 attributed narrative artifacts ── S3 factual recap and data-lifecycle owner
            └─ S9 concurrent/remote collaboration
~~~

The lowest missing implementation leaf is C8's S0 confirmation, not a new session-specific component. S5–S9 must not begin merely because a generic session record exists.

## First playable session scenario

The C8 acceptance fixture refines, rather than duplicates, the first played-session scenario in [STORY_FIRST_ROADMAP.md](STORY_FIRST_ROADMAP.md):

1. A trusted host reads the campaign/session procedure and starts one session for an eligible campaign.
2. Resume returns bounded current campaign/chapter, owner-approved quest context, authorized knowledge, and world/character readings from durable sources.
3. During play, normal world, quest, character, and ruleset operations commit through their own procedures and audits.
4. The host ends the session. C8 records only its confirmed lifecycle/summary/checkpoint evidence and leaves all external owner state unchanged unless that owner was separately invoked during play.
5. A fresh host resumes from durable state and obtains a faithful factual context without chat transcript, prompt memory, or a cached recap.

The initial fixture does not require a player account, a player-created character, a browser, or live table synchronization. Those are later S5/S8/S9 consumers.

## Cross-cutting invariants and test matrix

- **One session truth:** exactly the confirmed C8 record/state applies to a campaign; duplicate start, simultaneous active session, stale end, or unknown/replayed request fails unchanged.
- **No summary authority:** recap/context references current approved owner projections; a mismatch, missing source, hidden audience, malformed reference, or stale source fails/omits according to the confirmed safe policy, never silently invents a fact.
- **Fresh continuity:** a fresh host reconstructs context from campaign/world/quest/character/item/action history plus bounded session state alone; no prior chat, server cache, browser storage, or model memory is needed.
- **Lifecycle isolation:** start/resume/end cannot by themselves alter campaign chapter/arc, quest/objective, faction, clue, location, clock, character, inventory, resource, combat, or action outcomes.
- **Checkpoint safety:** checkpoint/restore scope is explicit, auditable, versioned, and read back. Restore/fork is impossible until its owner proves no cross-domain data is silently overwritten or omitted.
- **Authorization boundary:** before a real policy exists, trusted host only. Later audience/player controls deny before projection/action and no visibility label substitutes for enforcement.
- **Atomicity:** a session lifecycle transition, its owned summary/checkpoint reference, events, notifications, and success audit all commit together or roll back; external actions retain their own roots.
- **History and retention:** end/archive never deletes source state or operation evidence. Purge/retention policy is a separately confirmed data-lifecycle decision.
- **Transport parity:** MCP and any HTTP consumer call one semantic session service; no browser-to-DB/MCP route, raw effect endpoint, or client-side state engine appears.

## Planning protocol for future session features

Every S1–S9 dependency plan must link here and to its owning subsystem plan. It must contain:

- one player/table-facing target and included/excluded behavior;
- an ownership/overlap search across Campaign, World, Quest, Character, Items, ruleset actions, events/audit, snapshots, identity, and Website/API;
- a recursive dependency graph whose lowest missing leaf is the next implementation slice;
- closed input/state/result schemas, canonical order, source/projection versions, missing/null/empty behavior, stale/replay/corrupt-state handling, and forbidden caller data;
- exact transaction/effect/event/audit ownership, cancellation/timeout/rollback/restoration/readback cases, and fresh-host proof;
- a breadth statement separating the reusable session contract, first fixture, and named deferred capabilities; and
- an exit gate that stops before the next Session Feature.

For D&D rules-bearing session behavior, the plan must also cite exact SRD 5.2.1 source/version/locators and the named ruleset owner. S1–S5 lifecycle and projection work has no independent D&D rule source; it must not invent one.

## Change control

Amend this roadmap and the affected feature plan before adding more than one active session, restoring/rolling back play, multi-host concurrency, chat as authority, automated recap mutation, player self-service, party roster copies, arbitrary transcript search, session-owned time/quest/world changes, new public tools/kinds, or remote deployment. Those changes respectively belong to S4/S9, a narrative/artifact plan, CH14/Campaign policy, World/Quest/Character/Items/ruleset owners, `procedure.mcp.add-tool`, and the Website/API/identity/deployment plans.
