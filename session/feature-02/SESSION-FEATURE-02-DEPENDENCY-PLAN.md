# Session Feature S2 dependency plan — resume from authoritative context

> **D&D implementation reference:** When this work includes D&D 2024 mechanics, inspect [Foundry VTT dnd5e](https://github.com/foundryvtt/dnd5e) before designing or coding. Use it as a licensed engineering reference—not a direct dependency or rules authority—while retaining the exact `source.dnd2024.srd-5.2.1` locator and all applicable MIT, CC-BY, and asset-license notices for any reused material.

Status: **Accepted: focused, catalog, and full-suite validation passed. C4/Q3 and C5 remain deferred from the first fixture.**  
Last updated: 2026-08-20

## Execution rule

This plan follows AGENTS.md, `procedure.system.modify`, `procedure.mcp.add-tool`, the [Session Operations Plan](../../SESSION_OPERATIONS_PLAN.md), [S0](../feature-00/SESSION-FEATURE-00-DEPENDENCY-PLAN.md), [S1](../feature-01/SESSION-FEATURE-01-DEPENDENCY-PLAN.md), and [Campaign Feature C8](../../campaign/feature-08/CAMPAIGN-FEATURE-08-DEPENDENCY-PLAN.md). C8 owns the concrete session contract. S2 implements only the confirmed read composition.

## Target capability

A fresh trusted host can resume one active campaign session and receive one bounded, canonical factual context assembled at read time from the session record plus the S0-approved C3 campaign/chapter/arc projection. The result identifies the active session and its current playable context without chat transcript, prompt memory, cached summary, raw graph access, or a durable resume write.

The first fixture is the S0-ratified active session from S1. It is a reusable read composition boundary, not a session snapshot, recap, activity log, player view, GM notes store, campaign search engine, or a command that changes state.

### Included

- Resolution of one active S1 session and its single campaign scope relationship.
- A read-only composition of the exact S0-ratified C3/C4/C5 and world/character/item projections, with canonical field/record order and bounded sizes.
- Fresh-host, stale/archived/corrupt-source, audience/redaction, no-write, and transport-surface evidence.
- A literal handoff to the next governed action or S3 end flow, never an instruction to use raw components or replay a transcript.

### Excluded

- Starting/ending/changing a session, writing a recap/checkpoint, repairing interrupted state, restoring/forking, or emitting a session event/audit merely for a read (S1/S3/S4).
- Writing or copying chapter/arc, quest/objective, knowledge, world/location/clock, character/item, participant, combat, ruleset, or action outcome state.
- A player-safe view, player identity/control, browser write, free-form filtering/search, arbitrary history/event dump, transcript, AI recap, or a generic session query kind (S5–S9/C5/CH14).

## Ownership and projection boundary

| Concern | Authoritative owner and S2 rule |
| --- | --- |
| Active session identity/status/scope | S1/C8. S2 requires one valid active session linked to one campaign and returns a bounded session header only. |
| Campaign/chapter/arc continuity | C3 `campaign-resume` projection. S2 consumes its approved bounded result; it does not restate root/chapter/arc component data. |
| Quest/objective context | C4 and Q3 bounded projection. S2 includes it only as approved by the S0 fixture; it never queries raw objectives/evidence. |
| Knowledge/faction/world visibility | C5 and World owner projections. S2 applies their exact trusted-host/audience behavior; it does not classify or redact facts itself. |
| Character/items/participants | Their owners and later S5. Initial S2 either omits them or uses the exact S0-approved bounded projection; it cannot create a roster copy. |
| Read transport | Existing `query`/`campaign-resume` surface unless confirmed otherwise. A new kind/tool is forbidden absent `procedure.mcp.add-tool` confirmation. |
| Audit/events | Existing read/query policy. S2 has zero effects and creates no session success event or persistent resume/audit record. |

The session record is an eligibility/header source, not a context cache. If C3/C4/C5 values have changed through independently committed play since start, a later resume reflects the current authorized owner projection. Historical “what changed during this session” belongs to S3 factual closure or a later explicitly owned projection, never S2's mutable session state.

## Proposed public/read vocabulary — confirmation required

| Role | Proposed boundary |
| --- | --- |
| Governing contract | `procedure.campaign.session`, extended with explicit read-only resume semantics and its owner map. |
| Read mechanism | `CampaignSessionResumeReader`, a zero-effect compiled composer matching C3's existing read-model pattern. A sandbox mechanic would require an action-selection surface that S2 intentionally does not expose. |
| Query surface | Existing `query(kind: "campaign-resume")` remains backward-compatible by default. Its confirmed opt-in form is `includeSession: true`, which returns a bounded `Session` header plus the existing C3 `Campaign` projection; no generic session query kind is added. |
| Result section | A session header plus named owner projection sections. Exact field names/limits/order are S0 deliverables, not permission to add raw components or arbitrary records. |

Confirm the read route, backward compatibility, source projection versions, result envelope, audience/redaction/error behavior, output limits, and fresh-host caching policy before implementation. C3/C4/C5 owners must approve any projection extension that causes their values to appear in the combined result.

## Closed read boundary

The preferred request remains the existing canonical campaign-resume identity shape, augmented only after confirmation to resolve the current active session for that campaign. It must not accept session status, arbitrary component names, filters, graph traversal, history/event query, audience/principal assertion, player ID, cache key, raw projection, transcript, or source overrides. If a session ID is necessary for disambiguation in a future multiple-session history view, that is S3/S4 work—not initial S2.

S2 must distinguish these results without guessing:

- no valid active session for the campaign: named `no-active-session` recovery to S1/read historical session as appropriate;
- exactly one valid active session: bounded current context result;
- multiple/corrupt/dangling/cross-campaign session records: corrupt-state failure, no selected session;
- a required source unavailable/archived/stale/out-of-scope: S0-confirmed safe failure or omission reason, never substitution from cache/chat;
- denied audience: policy denial before exposing an owner projection.

The canonical result includes only approved session header fields, approved owner sections in a fixed section order, explicit unavailable/omitted reason codes where S0 permits them, and a literal next action. It contains no raw components/relationships, hidden facts, source prose, transcript, item/character roster beyond S0 scope, events/audits, root-operation IDs, raw effects, or derived permission claim.

## Resolution rules

1. Resolve the campaign through the confirmed campaign-resume/read path and validate exactly one active S1 session scope link/component. Reject malformed state before querying broader owner context.
2. Resolve each S0-approved owner projection with the exact campaign/session context and policy audience. Preserve the owner's already-bounded fields and source/version semantics; do not join raw entity data.
3. Canonically order sections by the S0 field order and repeated records by each owning projection's canonical order. Apply S0's fail-versus-safe-omit rule consistently; no missing record is replaced with a fabricated “unchanged” statement.
4. Return zero structural effects. S2 does not mutate the session, refresh a stored summary, reserve a choice, invoke an action, call randomness, emit an event, or warm a cache that changes later behavior. The ordinary read-operation row follows the existing query protocol and is not session state.
5. A fresh host repeats the same read against the same durable state and obtains the same normalized result. A changed owner state produces a current owner-backed difference, not a session-local revision.

## Dependency graph and slices

~~~text
S0 ratified field/audience/source inventory + S1 active session
├─ C3 bounded campaign/chapter/arc resume projection                    [required]
├─ C4/Q3 quest context and C5/world audience projection as selected     [required or explicit omission]
├─ confirmed existing query/result compatibility                         [public-read gate]
└─ no-write/fresh-host projection evidence                               [read gate]
   └─ Slice 1: scoped active-session reader and owner-projection composer
      └─ S3 factual end/recap and S5 participant context
~~~

### Slice 1 — read-only active-session resume

**Prerequisites:** S0/S1 accepted; all selected owner projections are verified and approve composition; query/result route and audience policy are confirmed.

1. Add/extend the confirmed read contract and zero-effect composer without changing any persistent session or owner state.
2. Resolve the active session before every source section; compose only the S0 field inventory and fixed output order.
3. Test valid fresh-host resume, no/multiple/corrupt session, wrong campaign, required source absence/staleness/cross-scope, audience denial/redaction, output bounds/order, repeated read, changed owner state, no cache/transcript dependence, and zero effects/events/audits.
4. Run focused projection/protocol tests, catalog validation if contracts/catalog change, and protocol walk only if public read registration changes.

**Exit:** implemented. A fresh trusted host can reconstruct the active-session header and the approved C3 current context entirely from durable owner projections. No-active and corrupt session state return named failures; no structural state is written. Quest, audience, participant, and gameplay sections remain deferred.

## Acceptance matrix

| Case | Exact expected result |
| --- | --- |
| Active-session resolution | Exactly one valid active session produces a header/context. None gives named recovery; multiple/corrupt/dangling/cross-scope records fail without choosing one. |
| Context truth | Every section comes from its approved C3/C4/C5/World/Character/Items owner projection at read time; S2 stores/copies no owner component or summary cache. |
| Fresh continuity | A new host reconstructs the same normalized result from database state alone; chat, prompt memory, browser state, and earlier resume results are unnecessary. |
| Current-state behavior | Independently committed changes appear through current owner projections. S2 does not try to compute a session delta or claim historical causation. |
| Audience/safety | Trusted-host or verified policy behavior follows S0/C5 exactly. Denial/redaction occurs before the relevant projection is exposed; visibility labels alone do nothing. |
| No-write | Resume performs zero effects and no event/audit/session-summary/checkpoint/action/randomness mutation, including error paths. |
| Boundary | S2 does not start/end a session, create recap/checkpoint, manage participants, run gameplay, or expose generic browsing/transcript/history. |

## Evidence and change control

The implementation receipt records the S0 field inventory, confirmed C3/C4/C5/owner projection versions, read route/schema decision, canonical fixtures, fresh-host/repeated/current-state results, redaction/absence/corrupt-state/no-write cases, catalog validation, and protocol evidence. It does not store a transcript, a copied context, hidden source data, raw effects, player identity, or a root operation ID.

Amend S2 before adding historical session selection, stored context snapshots, delta/activity history, recap prose, participant/character roster, player controls, search/filtering, checkpoint/restore, browser writes, another audience model, or a new query kind. Those belong to S3–S9, C5/CH14, Website/API, C8/S4, or `procedure.mcp.add-tool` confirmation.
