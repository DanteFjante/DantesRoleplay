# Campaign Feature 1 dependency plan — validate an existing-world campaign blueprint

Status: **Implemented and verified; C1 validation is the accepted input to C2.**
Last updated: 2026-08-20

## Execution rule

C1 is repository-mode planning and implementation. It follows AGENTS.md, procedure.system.create-feature,
procedure.system.modify for any C# surface work, and procedure.mcp.add-tool before introducing or
changing a query/commit kind. No catalog or live campaign state is created by validation.

The campaign kind now advertises the exact closed validation contract. Creation was added only with
C2's transaction owner; both operations remain on this single reviewed surface.

## Target capability

A host can submit one C0-ratified CampaignBlueprint for an existing world and receive a deterministic,
read-only validation report: resolved references, exact proposed creation counts, warnings, blocking
problems, and one review fingerprint. C2 may accept only that same closed blueprint accompanied by
its matching review fingerprint; C1 does not retain a durable review reservation.

## Boundary

### Included

- One closed manual CampaignBlueprint grammar and a read-only validate operation.
- Campaign-owned root/reference vocabulary proposed for later C2 creation.
- Stable permanent campaign-ID validation, local-key validation, existing-world scope checks,
  descriptive visibility checks, and exact creation-count calculation.
- Canonical validation ordering, warnings/errors, immutable review fingerprint, capability discovery,
  and no-write/public-surface tests.

### Excluded

- Creating a campaign, a campaign world, chapters/arcs, quests, characters, items, travel, maps,
  session state, AI proposals, authorization, raw effects, SQL, JavaScript, arbitrary filters,
  reservations, or a caller-supplied effect/audit result.
- Rewriting existing world, faction, motive, knowledge, clue, location, route, clock, or history
  state; C1 reads them only.
- Treating party/gm labels as authentication. C1 checks editorial consistency only.

## Source and contract basis

| Authority | Exact evidence | Decision supplied |
| --- | --- | --- |
| Feature workflow | AGENTS.md; procedure.system.create-feature | Repository authority, permanent-ID confirmation, focused tests, catalog validation after catalog changes. |
| Public surface | procedure.mcp.add-tool; procedure.system.use | Existing tools/kinds, thin dispatcher rule, capability/dispatch parity, and no invented public call. |
| Generic model | procedure.world.model; procedure.world.naming; procedure.world.change | Thin entities, permanent IDs, component/relationship ownership, and no raw caller effects. |
| Campaign specification | CAMPAIGN_CREATION_PLAN.md, Blueprint and creation boundary plus Slice 1 | One semantic validate/create path; existing-world reference; review fingerprint; manual validation before writes. |
| C0 source | Campaign Feature 0 plan | Approved editorial brief, existing reference inventory, party/GM classification, and no new-world content. |
| World references | World Feature 1/3/4 receipts and governing procedures | Root/location containment, faction/motive, fact/rumour/secret/clue scope and descriptive visibility remain authoritative. |

Repository searches for campaign.root, campaign.in-world, campaign.reference, CampaignBlueprint,
campaign validation, and campaign commit found no existing owner. The proposed vocabulary below is
new and requires one confirmation boundary before any implementation.

## Ownership and confirmation boundary

C1 owns validation grammar and the campaign-owned vocabulary needed by C2. It does not own world
data or lifecycle. C2 owns creation; C3 owns durable chapter/arc records; the quest plan owns quest
state.

Confirm all of these together before implementation:

| Artifact | Proposed meaning |
| --- | --- |
| game.core.campaign.root | Closed campaign-root state: lifecycle, display/premise/goals/tone, ruleset scope, creation method, and review fingerprint. It contains no world ID, chapter, arc, quest, character, item, clock, or history list. |
| game.core.campaign.in-world | Directed empty-data relationship from campaign root to exactly one active world root. |
| game.core.campaign.references | Directed relationship from campaign root to an allowed existing world record. Data is exactly role plus audience, allowing start, npc, faction-stake, or knowledge context without copying source state. |
| procedure.campaign.create | Governs blueprint validation, C2 creation, reference conventions, fingerprint/replay policy, and recovery. |
| commit(kind: "campaign") | Proposed existing commit kind with closed validate/create operations. C1 implements validate only; C2 later adds create. |
| CampaignBlueprint and CampaignValidationResult | Public request/result contracts; implementation-local types are not catalog records. |

If the MCP surface review rejects a campaign commit kind, revise this plan before implementation.
Do not disguise the validation operation as generic effects or an undocumented query.

## Closed request and proposed vocabulary

The C1 request is exactly:

~~~text
commit(kind: "campaign")
{
  operation: "validate",
  blueprint: CampaignBlueprint
}
~~~

CampaignBlueprint is closed:

~~~text
{
  campaignId: stable lowercase dotted ID, 3–100 characters, prefix "campaign.",
  title: trimmed text, 1–160 characters,
  premise: trimmed text, 1–2 sentences,
  partyGoals: ordered array of 1–3 trimmed texts, each 1–500 characters,
  toneAndBoundaries: ordered array of 1–8 trimmed texts, each 1–300 characters,
  rulesetScope: exactly "dnd2024" in the first delivery,
  existingWorldId: one active world-root ID,
  startingLocationId: one active location ID in existingWorldId,
  references: ordered array of 4–12 unique entries,
  initialChapter: {
    localKey: "chapter." followed by lowercase dot/hyphen segments,
    partyQuestion: trimmed open question, 1–500 characters,
    gmContext: absent | trimmed text, 1–1,000 characters
  },
  initialArc: {
    localKey: "arc." followed by lowercase dot/hyphen segments,
    partyStake: trimmed open stake/question, 1–500 characters,
    gmContext: absent | trimmed text, 1–1,000 characters
  },
  futureQuestShapedProblem: absent | {
    audience: exactly "gm",
    summary: trimmed text, 1–1,000 characters
  }
}
~~~

Each reference is exactly:

~~~text
{
  entityId: existing world-owned entity ID,
  role: "start" | "npc" | "faction-stake" | "knowledge",
  audience: "party" | "gm"
}
~~~

The references must contain exactly one party/start entry equal to startingLocationId; exactly 2–3
npc entries; exactly one faction-stake entry; and 0–8 knowledge entries. Entity IDs are unique
across entries except no duplicate is allowed at all. The campaignId is a caller-proposed permanent
identity and is therefore validated for syntax/collision but is not derived or silently rewritten.

Missing, null, arrays/scalars in place of objects, unknown fields, unsafe text, duplicate array
values, invalid local key, raw effect, operation/audit field, campaign world ID, permanent child
ID, script, SQL, arbitrary filter, or caller-supplied count/fingerprint/result rejects.

## Derived validation result

CampaignValidationResult is closed and read-only:

~~~text
{
  status: "valid" | "invalid",
  campaignId: proposed ID,
  worldId: resolved existing root ID | null,
  resolvedReferences: ordered entries with entityId, role, audience, component evidence,
  creationCounts: {
    entities: 1,
    rootComponents: 1,
    inWorldRelationships: 1,
    referenceRelationships: 4–12
  } | null,
  warnings: ordered stable array,
  problems: ordered stable array of { code, path, reason, recovery },
  reviewFingerprint: 64-character lowercase hex | null
}
~~~

A valid result has no problems, non-null worldId/counts/fingerprint, and canonical reference order:
role order start, npc, faction-stake, knowledge; then audience party before gm; then entityId. An
invalid result has null counts/fingerprint and may still report safely resolved reference evidence.
Warnings do not make a blueprint creatable unless explicitly classified non-blocking by the
confirmed contract.

The fingerprint is SHA-256 over the canonical closed blueprint plus the canonical resolved world
reference identities/component revisions used by validation. It is not caller input, a reservation,
or a promise that later world state cannot change. C2 must revalidate reference lifecycle and
fingerprint before creating anything.

## Validation algorithm

1. Reject an envelope other than campaign/validate and reject malformed or non-object blueprint.
2. Validate every closed field, text limit, array limit/order, local key, campaign ID, and forbidden
   caller-derived field before reading partial state.
3. Check campaignId against permanent entity identity and campaign identity conventions; collision
   is a blocking problem.
4. Resolve existingWorldId. It must have one active game.core.world.root component.
5. Resolve startingLocationId. It must have an active game.core.world.location component and belong
   to the selected root through the canonical containment tree.
6. Resolve each reference once. Validate role-to-component compatibility: start is the selected
   location; npc has world motive/actor eligibility; faction-stake has active world faction; and
   knowledge has an allowed active/revealed/descriptive visibility state.
7. Validate all selected records are scoped to the selected world where their owner supplies scope.
   C1 never infers a reference from summary text.
8. Apply the C0 party/GM visibility rules: party knowledge cannot be secret, unrevealed clue, or
   GM-only source; party rumour remains explicitly a rumour; a party role cannot cite GM-only
   motive context.
9. Compute creation counts for C2 only: one campaign entity/root component, one in-world link, and
   one references link per valid reference. Initial chapter/arc and future quest prose create no
   C2 records.
10. Canonicalize the valid blueprint/reference evidence and calculate the fingerprint. Return a
    complete valid or invalid result with zero effects.

## Recursive dependency analysis

~~~text
Campaign Feature 1: deterministic existing-world validation
├─ C0 host-ratified CampaignBriefReview                              [must be verified]
├─ W1 world root/location containment                               [verified]
├─ W3 faction/NPC motive records                                    [verified]
├─ W4 knowledge and descriptive visibility                          [verified]
├─ campaign root/reference IDs and public campaign kind             [missing semantic leaf]
│  └─ Slice 1: procedure, closed contracts, validator, capability surface
└─ C2 atomic bootstrap                                               [blocked parent]

World creation, chapters/arcs, quests, AI, and authorization [excluded]
~~~

## Slice order and stop gate

| Order | Slice | Starts only when | Exit gate |
| --- | --- | --- | --- |
| 1 | Existing-world blueprint validation | C0 is ratified; all C1 IDs, closed request/result, commit-kind decision, and fingerprint policy are confirmed. | The real public validator returns one stable valid preview or complete named invalid result with no writes; C2 can consume the same closed blueprint only with its matching fingerprint. |

## Slice 1 — deterministic manual validator

### Runtime artifacts

| Artifact | Change |
| --- | --- |
| Campaign component definition/schema | Add game.core.campaign.root with the confirmed closed root contract. |
| Campaign procedure | Add procedure.campaign.create defining validate-only behavior, reference conventions, public surface, and recovery. |
| Public surface | Add campaign to commit kinds only if the confirmed MCP review approves it; capability description and dispatcher land together. |
| Validation service | Add a thin semantic validator over existing world/component/containment/relationship reads. No logic belongs in the MCP handler. |
| Tests | Add focused CampaignFeature1Tests plus capability/dispatcher guard and protocol-walk coverage if the public kind changes. |
| Catalog | Add only the component/procedure artifacts required for discoverability. No campaign entity or fixture campaign is created. |

### Result and effects

Validation returns CampaignValidationResult and exactly zero structural effects. It creates no
entity, component data, relationship, event, notification, operation success record, clock change,
or durable review reservation. Any audit behavior must be the existing call audit only and must not
claim campaign creation occurred.

### Acceptance matrix

| Test class | Setup | Exact expected result |
| --- | --- | --- |
| Valid minimal | Ratified first-world brief with 2 NPCs, one faction, start, and no knowledge references. | Valid result has counts 1/1/1/4, canonical references, no problems, and 64-character fingerprint. |
| Valid maximum | 3 NPCs and 8 knowledge references within one world. | Valid result has referenceRelationships 12 and stable canonical order. |
| Closed input | Missing, null, extra, wrong-type, blank, untrimmed, duplicate, unsafe, raw-effect, script, SQL, permanent-child-ID, count, or fingerprint field. | Invalid result names path/code; no state changes. |
| Reference compatibility | Wrong start, wrong component for role, archived/inactive record, out-of-world record, duplicate reference, or unscoped knowledge. | Invalid result names each offending reference; no partial valid fingerprint. |
| Visibility | Secret, unrevealed clue, GM-only motive, or rumour stated as party fact. | Invalid result reports visibility/claim failure and preserves source data byte-for-byte. |
| ID/local keys | Invalid/colliding campaignId or malformed/duplicate chapter/arc local key. | Invalid result with no proposed counts/fingerprint. |
| Determinism | Identical request/state twice. | Byte-identical result, reference order, counts, warnings, and fingerprint. |
| No-write | Before/after entity/component/relationship/event/notification/operation/clock comparison. | Validator changes no game state and has zero effects. |
| Public surface | Fresh MCP session discovers the approved campaign kind and calls validate. | Capability and dispatcher agree; failure recovery names a literal supported next call. |
| Repository | Focused tests, catalog validation, public-surface guard/protocol walk when applicable, full suite at acceptance, diff check. | All pass without persistent import. |

### Slice 1 exit gate

C1 is verified only when the approved public validation path executes against a fresh imported world,
returns deterministic review evidence with zero writes, and rejects every invalid/cross-scope/
visibility/replay-shaped input without making C2 state. Stop before creation.

## Plan-change rule

Revise before implementation if C0 ratifies a ruleset other than dnd2024, campaign roots require
more than one world, world scope cannot be derived through current containment, a party reader needs
real authorization, chapter/arc content must become durable before C3, or MCP review rejects the
campaign commit kind. None may be solved by adding caller effects, copied world state, or an
undocumented public call.
