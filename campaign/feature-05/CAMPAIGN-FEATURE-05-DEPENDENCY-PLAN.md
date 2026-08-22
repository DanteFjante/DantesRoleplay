# Campaign Feature 5 dependency plan — authorised campaign knowledge and faction projections

> **D&D implementation reference:** When this work includes D&D 2024 mechanics, inspect [Foundry VTT dnd5e](https://github.com/foundryvtt/dnd5e) before designing or coding. Use it as a licensed engineering reference—not a direct dependency or rules authority—while retaining the exact `source.dnd2024.srd-5.2.1` locator and all applicable MIT, CC-BY, and asset-license notices for any reused material.

Status: **Planned; blocked by verified C4 and a real authenticated audience-policy capability.**
Last updated: 2026-08-20

Shared policy seam and current repository audit: [Knowledge Slice 6 readiness](../../knowledge/KNOWLEDGE_AND_FACTS-SLICE-6-READINESS.md).

## Execution rule and target

C5 is repository-mode work governed by AGENTS.md, procedure.system.create-feature,
procedure.system.modify, and procedure.mcp.add-tool. It has no implementation slice until the
audience-policy dependency is verified. Public, party, and GM labels are descriptive today, not
authorization; trusted MCP sessions are GM scope only.

Once a caller is authenticated and authorised for one campaign, C5 returns one fixed, bounded,
read-only campaign projection. It contains only C2-selected world references, C3 continuity, and
C4/Q3 quest context that the caller may see. It never stores a player copy, discovers information,
or changes game state.

## Boundary and ownership

Included: authenticated principal-to-campaign resolution; fixed GM/party shapes; field-level
visibility/status filtering; C2/C3/C4/Q3 allowlists; stable order/caps; omitted/denied behavior;
and no-copy/leakage tests.

Excluded: accounts, roles, party membership, tokens, world/faction/quest lifecycle, player
discovery, arbitrary graph/search/history, AI/UI/notifications, writes/cache, and audience chosen
by a request parameter.

| Owner | C5 consumes | C5 never owns |
| --- | --- | --- |
| C2 | Root, world link, canonical start/NPC/faction/knowledge references. | Reference selection or world lifecycle. |
| C3 | Chapter/arc continuation. | Chapter/arc lifecycle or GM context storage. |
| C4 and Q3 | Explicit quest context and bounded owner summary. | Quest/objective state, evidence, hidden criteria, cache. |
| W3/W4 | Faction/knowledge status, provenance, descriptive visibility. | Faction agendas/motives and knowledge truth. |
| Audience-policy feature | Authenticated principal, membership, role, revocation. | Authentication/authorization implementation. |

Repository evidence is explicit: the master plan says visibility is not a security boundary before
authenticated audience policy, and no current component/query/principal/policy service owns it.
C5 must not create a campaign-local substitute.

## Required external contract

The external owner must provide a read-only resolver which accepts authenticated transport context
and campaign ID, then either denies or returns stable principal ID, exact campaign ID, audience
role GM or party, and stable policy revision. Identity is supplied by transport, never payload.
It fails closed for missing, expired, revoked, malformed, ambiguous, or cross-campaign membership.
A party principal cannot request GM data. Character-specific audience is outside C5 and denies.

Confirm together: principal source, membership semantics, role vocabulary, revocation point,
policy-revision/cache rule, unauthenticated error, trusted-MCP migration, and fixture mechanism.
C5 begins only after independent tests prove denied callers cannot obtain GM-only data through any
applicable read route.

## Closed projection

The one confirmed campaign read takes campaign ID only: no audience, component list, graph depth,
include-hidden flag, search, sort, history range, or arbitrary entity IDs. It returns campaign
identity/premise/goals/tone/ruleset/world ID; current chapter title and party question; current arc
title and party stake; one start; at most three NPCs; at most one faction; at most eight knowledge
records; at most three Q3 quest summaries; generic omission counts; and the policy revision.

The result contains no raw component JSON, GM context, motive, secret, unrevealed clue, hidden
objective criterion/evidence, operation/event/subscription data, or caller-supplied text. GM output
may include campaign-scoped GM records and GM chapter/arc context but uses the same hard caps.

| Source | Party predicate |
| --- | --- |
| C2 root/start/NPC/faction | Active public or party data only; no GM motive/context. |
| Fact | Active public or party fact. |
| Rumour | Active public or party rumour, retaining rumour/certainty label. |
| Secret | Never included. |
| Clue | Only revealed party clue; unrevealed or GM clue never included. |
| Faction | Active permitted summary/agenda only; never inferred from links. |
| C3 | Party chapter question/arc stake only; GM context omitted. |
| C4/Q3 | Quest owner’s party-safe bounded result only. |

Unknown, inactive, archived, dangling, duplicate, malformed, wrong-world/campaign, or
visibility-incompatible source is omitted by a generic stable reason count, never replaced or
described specifically. Denied policy fails the whole request before projection. Apply filters first,
then limits in canonical order: start, NPC ID, faction ID, knowledge kind then ID, quest priority
then ID. Do not return hidden names/IDs in omission counts.

## Read algorithm

1. Authenticate and resolve policy before loading any campaign/world/quest data. A denial has no
   trusted-host fallback and does not expose campaign existence or hidden counts.
2. Resolve one active C2 root and its single world link; reject malformed scope without widening.
3. Read only C2 canonical references, C3 current records, C4 links, and Q3 bounded projection.
   Recheck owner status/scope.
4. Apply the table predicate field by field, then canonical order and caps.
5. Return the closed projection/policy revision. It writes zero entity, component, relationship,
   event, notification, history, discovery, cache, or audit game state; logs contain no hidden
   response data.

## Dependency tree and Slice 1

~~~text
C5 authorised campaign projection
├─ C4 campaign/quest context                              [must be verified]
├─ W3 factions and W4 knowledge/visibility                [verified]
├─ Q3 party-safe and trusted-host summaries               [must be verified]
├─ authenticated campaign audience policy                 [missing external leaf]
│  └─ Slice 1: fixed policy-gated projection/leak tests
└─ player-facing views                                    [blocked consumer]
~~~

Slice 1 depends on the confirmed policy rather than adding accounts/roles. It replaces C3's
trusted-host-only reader with a policy-gated fixed GM/party reader, updates the campaign inspect
contract/public capability only after confirmation, and adds principal-binding, every visibility/
status permutation, no-load-on-deny, scope, bounds/order, no-copy, fresh-readback, and route-leak
tests. Catalog validation, surface guards/protocol walk when applicable, full suite, and diff check
are required at acceptance.

## Acceptance matrix

| Case | Exact expected result |
| --- | --- |
| GM principal | Bounded campaign-scoped GM projection, no unrelated world/quest data. |
| Party principal | Only table-permitted data; no GM context, secrets, unrevealed clues, GM motives, or hidden quest truth. |
| Absent/revoked/wrong principal | Fails before game reads; no campaign existence or hidden count branch. |
| Scope/state fault | Generic omission or safe rejection; never an inferred replacement. |
| Boundary matrix | Every fact/rumour/secret/clue, faction, chapter, arc, quest, objective visibility/status permutation exposes only permitted fields. |
| Bounds/determinism | Stable capped canonical output and generic counts for identical state/principal. |
| Isolation | No game row, event/history/notification, discovery, or cache changes. |
| Route leak | Entity/graph/campaign/website reads under party principal cannot bypass policy. |

## Exit gate and change rule

C5 is verified only after a real policy independently proves fail-closed campaign membership and
every relevant read route returns exactly the permitted GM/party projection without copied or leaked
truth. Website work consumes it only after that proof.

Revise first if policy cannot bind a principal to one campaign, roles need finer groups, sources
lack field visibility, a needed record is outside C2/C4 allowlists, or any route bypasses policy.
Never solve this with an audience input, campaign component, obscured IDs, client-side filtering,
cached hidden summaries, or descriptive visibility treated as security.
