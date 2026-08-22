# Campaign Feature 8 dependency plan — session operations and read-only campaign view

> **D&D implementation reference:** When this work includes D&D 2024 mechanics, inspect [Foundry VTT dnd5e](https://github.com/foundryvtt/dnd5e) before designing or coding. Use it as a licensed engineering reference—not a direct dependency or rules authority—while retaining the exact `source.dnd2024.srd-5.2.1` locator and all applicable MIT, CC-BY, and asset-license notices for any reused material.

Status: **Planned; S0 has ratified the first trusted-host C3-only fixture. C4/Q3, C5, and checkpoint/restore are deliberately deferred rather than prerequisites of the first session lifecycle slice.**
Last updated: 2026-08-20

## Roadmap parent

This is the first concrete implementation plan for S0–S4 in the
[Session Operations Plan](../../SESSION_OPERATIONS_PLAN.md). It owns the initial session record,
start/resume/end contract, factual summary lifecycle, and confirmation of checkpoint/snapshot
boundaries. It must not silently absorb later participant control (S5), gameplay handoff (S6),
narrative artifacts (S7), player controls (S8), or multi-host collaboration (S9).

[Session Feature S0](../../session/feature-00/SESSION-FEATURE-00-DEPENDENCY-PLAN.md) must first
ratify the fixture, lifecycle, factual projection, audience, and checkpoint/restore boundary. C8
may not choose those permanent semantics while implementing its first slice.

[Session Feature S1](../../session/feature-01/SESSION-FEATURE-01-DEPENDENCY-PLAN.md) refines the
initial active-session entity, scope, uniqueness, and start transaction. C8 must implement that
contract before adding S2 resume context or S3 end/recap behavior.

[Session Feature S2](../../session/feature-02/SESSION-FEATURE-02-DEPENDENCY-PLAN.md) composes the
active session's current factual context through approved owner projections with zero effects. It
must not cache or copy those projections into C8 session state.

[Session Feature S3](../../session/feature-03/SESSION-FEATURE-03-DEPENDENCY-PLAN.md) ends the
active session once with an immutable, source-bound factual recap. It must not accept recap prose
or mutate the state described by that recap.

[Session Feature S4](../../session/feature-04/SESSION-FEATURE-04-DEPENDENCY-PLAN.md) defines
named checkpoint evidence and interruption recovery. It keeps restore unavailable unless a
snapshot owner and every classified domain can compose one atomic restore root.

[Session Feature S5](../../session/feature-05/SESSION-FEATURE-05-DEPENDENCY-PLAN.md) adds a
separate immutable session-to-character roster reference only after campaign attachment and
character lifecycle owners exist. It is not a player-control or gameplay capability.

[Session Feature S6](../../session/feature-06/SESSION-FEATURE-06-DEPENDENCY-PLAN.md) is the
later gameplay-handoff successor: it admits one rostered character to an opt-in existing action
and correlates that action's root audit without adding a C8 activity log or wrapping the action
root.

[Session Feature S7](../../session/feature-07/SESSION-FEATURE-07-DEPENDENCY-PLAN.md) is the
later narrative-artifact successor: it can publish an attributed noncanonical recap only from the
immutable factual S3 source and cannot change C8's lifecycle, summary, or factual record owner.

[Session Feature S8](../../session/feature-08/SESSION-FEATURE-08-DEPENDENCY-PLAN.md) is the
later participant-consumer successor: it receives a fixed policy-gated C8 projection and delegates
one separately authorized action without giving a browser or player direct control of C8 records.

[Session Feature S9](../../session/feature-09/SESSION-FEATURE-09-DEPENDENCY-PLAN.md) is the
later remote-collaboration successor: it transports and refreshes C8-derived participant views
under per-request policy without creating a multi-host C8 authority, session lock, or activity log.

## Target capability

A host can start, resume, and end a campaign session with a bounded stored summary, while a human
reader can inspect a read-only current campaign view that refreshes only after committed changes.

## Included and excluded

Included: campaign session lifecycle, C3-only trusted-host current context, S3 factual-summary
policy, committed-change refresh contract, and fresh-host continuity tests. S4 owns checkpoint
evidence and any later restore boundary.

Excluded: browser writes, live multiplayer, chat transcript as authority, campaign state creation,
quest/world transitions, AI narration, player authorization beyond C5, map interaction, SSE write
handling, and campaign forks.

## Ownership

Feature 8 owns session records and summary lifecycle. C3/C4 remain owners of chapter/arc/quest
context. C5 owns audience-filtered projections. The website is a consumer and must never create or
advance state in this slice.

## Dependencies and slices

~~~text
C8 first session continuity
├─ accepted S0 C3-only trusted-host fixture                 [ratified]
├─ C3 campaign continuity                                  [must be verified]
├─ confirmed session lifecycle/root boundary                [next semantic leaf]
│  └─ Slice 1: session shape and zero-effect validate
├─ S4 snapshot/checkpoint evidence                          [separate blocked successor]
└─ C4/Q3 quest context and C5 authorised projections        [separate deferred successors]
~~~

## Required confirmation

S0 confirms one-active-session policy, trusted-host-only audience, C3-only context, immutable
end retention, factual closure fields, and evidence-only/no-restore checkpoint policy. C8 next
confirms only its concrete session vocabulary and root transaction implementation.

## Acceptance matrix

| Case | Exact expected result |
| --- | --- |
| Start/resume/end | Allowed sequence records only session-owned state and returns bounded current context. |
| Fresh continuity | A new host reconstructs active chapter, quest context, permitted knowledge, and milestones without chat history. |
| Invalid/replay | Duplicate start, stale end, bad campaign, missing context, or repeated request rejects unchanged. |
| Snapshot/restore | Confirmed checkpoint restores exactly its declared campaign/session scope with auditable evidence. |
| Read-only view | UI/read consumer has no state-changing controls and refreshes only committed projections. |
| Isolation | Session actions do not advance chapter, quest, faction, clue, location, inventory, or clock state. |

## Exit gate

C8 completes when session continuity and read-only inspection have been proven from stored state and
snapshot/restore evidence is sufficient for later fork design.
