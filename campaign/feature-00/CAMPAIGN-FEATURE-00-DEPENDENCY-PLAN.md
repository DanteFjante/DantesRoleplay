# Campaign Feature 0 dependency plan — ratify the first existing-world campaign blueprint

Status: **Ratified test brief; C1 validation is authorised.**
Last updated: 2026-08-20

## Execution rule

This is a repository planning artifact. It follows AGENTS.md and procedure.system.create-feature:
repository files are authoritative during development, C0 writes no runtime game state, and no later
campaign slice begins until this semantic review is complete.

C0 has no D&D rules source. It is authored campaign composition; the governing sources are the
campaign plan and the verified world records it is allowed to reference.

## Target capability

A host can approve one complete, visibility-classified campaign brief attached to the verified
existing world. That approved brief gives C1 an authoritative human decision for every campaign
concept it must validate, without an AI inventing facts, identifiers, outcomes, or hidden content.

## Boundary

### Included

- Exactly one existing active world root and one active starting location in that world.
- A title, party-facing premise, 1–3 party-facing goals, tone and content boundaries.
- Exactly 2–3 existing NPC references, one existing faction stake, one active chapter question, and
  one longer arc stake.
- An optional future quest-shaped problem recorded only as editorial prose.
- A line-by-line party or GM visibility classification and a host ratification record.
- Read-only checks against the verified world fixture and a no-runtime-write proof.

### Excluded

- A campaign entity, component, relationship, permanent ID, schema, procedure, mechanic, public
  surface, validation code, action, event, notification, or audit row.
- New or changed world topology, faction agenda, motive, knowledge, clue reveal, rumour state,
  traveller location, clock, quest, character, item, or transport state.
- AI proposal, automatic brief completion, player authorization, generated setting content, quest
  lifecycle, and any precommitted player choice or campaign outcome.

## Source and contract basis

| Authority | Exact evidence | C0 decision supplied |
| --- | --- | --- |
| Feature workflow | AGENTS.md; procedure.system.create-feature instructions 1–5 and 11–12 | One planning-only capability, ownership search, semantic confirmation, no downstream implementation. |
| Campaign scope | CAMPAIGN_CREATION_PLAN.md, Goal, Campaign model, and Slice 0 | Existing-world campaign brief requirements; campaign consumes rather than copies world state. |
| Delivery order | STORY_FIRST_ROADMAP.md, C0 through C4 | C0 follows W4 and is the only route into C1. |
| World topology | World Feature 1 receipt; world.feature-01.fixture; location.feature-01.gate, market, observatory | Eligible world and starting-location records; containment remains current-location authority. |
| Faction/NPC state | World Feature 3 Slice 2 receipt; faction.feature-03.fixture; actor.feature-03.mara-vell; actor.feature-03.oren-dale | Eligible faction stake and the required 2–3 NPC references; motives remain world-owned. |
| Knowledge/visibility | World Feature 4 Slice 2 receipt; Feature 4 fact, rumour, secret, and clue records | Party/GM editorial boundary; C0 does not reveal or change any knowledge record. |
| Generic world records | procedure.world.model and procedure.world.change | No campaign copy, raw effects, or alternate ownership representation is created. |

Repository searches for campaign, chapter, arc, campaign blueprint, and campaign relationships found
no campaign-specific catalog owner. That absence is deliberate at C0: C1, not C0, is the first
slice allowed to propose campaign runtime vocabulary.

## Verified existing reference inventory

The first brief may reference only current active records in the verified fixture unless the host
first requests a separately governed world change.

| Category | Eligible fixture records | C0 restriction |
| --- | --- | --- |
| World | world.feature-01.fixture | Exactly one selected world. |
| Start location | location.feature-01.gate; location.feature-01.market; location.feature-01.observatory | Exactly one selected active location within the selected world's containment tree. |
| NPCs | actor.feature-03.mara-vell; actor.feature-03.oren-dale | Select exactly 2–3 references. C0 does not make them characters, campaign members, or quest givers. |
| Faction | faction.feature-03.fixture, The Lantern Compact | Select exactly one faction stake; C0 cannot advance its agenda or redefine its goals. |
| Party-safe knowledge | active public fact; party/public rumour; revealed party clue when one exists | A rumour remains a rumour. C0 may not restate it as confirmed fact. |
| GM-only knowledge | active secret; unrevealed GM clue; GM-only motive | May inform GM brief text only. It cannot appear in party premise, goals, or party references. |

## Ownership decisions

1. The campaign brief is host-authored editorial source material. It is not durable game state until
   C1 confirms its closed CampaignBlueprint contract.
2. World entities remain references. C0 never embeds their summary, provenance, motive, agenda,
   current location, clock, or relationship list as a second authoritative copy.
3. Starting location means the initial scenario reference only. It does not move a traveller,
   create a party, or store a campaign-owned location field.
4. A future quest-shaped problem has no quest/objective ID, status, transition, reward, or link.
   Quest ownership begins only in the quest plan after C3.
5. Visibility is an editorial classification at C0. It does not claim that current trusted-GM
   repository reads enforce access control.

## Closed ratification record

CampaignBriefReview is a documentation-only record in this plan. It is not a component, a JSON
schema, an API request, or a permanent identifier.

## Ratified test brief

The host authorised this deliberately small test campaign on 2026-08-20. It is fixture-only source
material for C1/C2, not a claim about the eventual authored setting.

~~~text
CampaignBriefReview
{
  status: "ratified",
  title: "The Sealed Observatory",
  worldReference: "world.feature-01.fixture",
  startingLocationReference: "location.feature-01.gate",
  partyPremise: "A strange signal from the sealed observatory threatens to draw opportunists to the old market records.",
  partyGoals: [
    "Reach the market archive and learn what the old toll ledger reveals.",
    "Decide whom to trust with news of the observatory signal."
  ],
  toneAndBoundaries: ["Curious local mystery.", "No assumed violence or player-character backstory."],
  npcReferences: ["actor.feature-03.mara-vell", "actor.feature-03.oren-dale"],
  factionStake: { factionReference: "faction.feature-03.fixture", partyStatement: "The Lantern Compact wants the market records protected from opportunists.", gmStatement: "Oren's private correspondence may explain why the observatory was sealed." },
  activeChapter: { partyQuestion: "What does the old toll ledger reveal about the observatory signal?", gmContext: "The answer is not precommitted by this test brief." },
  arc: { partyStake: "Can the group keep the observatory's history from becoming another source of leverage?", gmContext: "No arc outcome is fixed." },
  partyReferences: ["fact.feature-04.toll-ledger", "rumour.feature-04.observatory-signal"],
  gmReferences: ["secret.feature-04.oren-correspondence"],
  futureQuestShapedProblem: "A future investigation may ask the group to reconcile the observatory record with Oren's family history.",
  ratifiedBy: "Dante",
  ratifiedOn: "2026-08-20"
}
~~~

~~~text
CampaignBriefReview
{
  status: "draft" | "ratified",
  title: trimmed text, 1–160 characters,
  worldReference: one existing active world ID,
  startingLocationReference: one existing active location ID in that world,
  partyPremise: trimmed text, 1–2 sentences,
  partyGoals: ordered array of 1–3 trimmed concrete outcomes,
  toneAndBoundaries: ordered array of 1–8 trimmed statements,
  npcReferences: ordered array of exactly 2–3 existing actor IDs,
  factionStake: {
    factionReference: one existing faction ID,
    partyStatement: trimmed text,
    gmStatement: optional trimmed text
  },
  activeChapter: {
    partyQuestion: trimmed open question,
    gmContext: optional trimmed text
  },
  arc: {
    partyStake: trimmed open stake/question,
    gmContext: optional trimmed text
  },
  partyReferences: ordered array of 0–3 existing party-safe knowledge/location/faction IDs,
  gmReferences: ordered array of 0–6 existing world IDs,
  futureQuestShapedProblem: absent | trimmed planning prose,
  ratifiedBy: absent | host-supplied display name,
  ratifiedOn: absent | ISO-8601 calendar date
}
~~~

All required text is trimmed and nonempty. Arrays are ordered by host intent; duplicate IDs are
invalid. Missing required fields, null values, unknown fields, wrong audience classification,
ineligible references, or a ratified record lacking ratifiedBy/ratifiedOn leave C0 unratified.

The host may use either the current fixture or a separately governed existing world. C0 does not
permit a placeholder, a local key, a proposed permanent ID, or an entity that must be created later.

## Visibility algorithm

Evaluate every party-facing statement before ratification:

1. Resolve every named world reference from the inventory.
2. Reject a missing, archived, out-of-world, duplicate, or unsupported reference.
3. For a fact, accept only active public or party visibility.
4. For a rumour, preserve its status as a claim; it may not be worded as verified truth.
5. For a clue, accept only a revealed party clue. An unrevealed clue is GM-only even if its target
   is otherwise party-safe.
6. Reject every secret and GM-only motive from party premise, goals, chapter question, arc stake,
   and partyReferences.
7. Confirm the resulting party text does not state a player choice, quest outcome, faction agenda
   transition, or any world change as already decided.

GM context may cite GM-only records, but it remains descriptive planning prose and creates no
knowledge, clue, quest, or campaign state.

## Recursive dependency analysis

~~~text
Campaign Feature 0: ratify first existing-world campaign brief
├─ W1 active root/topology and containment                          [verified: Feature 1 receipt]
├─ W3 faction and recurring-motive records                          [verified: Feature 3 receipt]
├─ W4 fact/rumour/secret/clue records                               [verified: Feature 4 receipt]
├─ host decisions in CampaignBriefReview                            [missing leaf]
│  └─ C0: inspect, classify, and ratify one complete brief
└─ C1 CampaignBlueprint validation                                  [blocked parent]

Campaign records, identifiers, quests, AI, authorization, and world mutation [excluded]
~~~

## Slice order and stop gate

| Order | Slice | Starts only when | Exit gate |
| --- | --- | --- | --- |
| 0 | Host brief ratification | W1/W3/W4 receipts and fixture records remain verified. | One complete CampaignBriefReview is explicitly ratified; no runtime state exists; C1 is the next and only authorised implementation planning target. |

## Slice 0 — host brief ratification

### Required review sequence

1. Read the selected world, starting location, NPCs, faction, and every named knowledge reference.
2. Fill every field in the closed ratification record using existing IDs only.
3. Classify each sentence party or GM and apply the visibility algorithm.
4. Check that chapter and arc language remains an open question/stake rather than a scripted result.
5. The host explicitly supplies ratifiedBy and ratifiedOn and changes status from draft to ratified.
6. Record no catalog/live state, handoff, or implementation receipt in this pass.
7. Stop. C1 alone receives the ratified brief as a planning input.

### Acceptance matrix

| Test class | Setup | Exact expected result |
| --- | --- | --- |
| Complete brief | All required fields use eligible active fixture references. | Status can become ratified; C1 receives a complete editorial source record. |
| Minimum/maximum | 1 versus 3 goals; 2 versus 3 NPCs; 0 versus 3 party references; 0 versus 6 GM references. | Inclusive limits pass; below/above limits leave status draft. |
| Closed record | Missing, null, blank, untrimmed, unknown, wrong-type, duplicate, placeholder, or local-key field. | Ratification rejects; no default/fallback content is generated. |
| World scope | Wrong root, archived location, location outside selected root, inactive faction/NPC/knowledge reference. | Ratification rejects with the named bad reference. |
| Visibility | Secret, unrevealed clue, GM-only motive, or unconfirmed rumour phrased as fact in party text. | Ratification rejects; source world records are unchanged. |
| Narrative boundary | Chapter/arc says player action, quest result, faction advance, clue reveal, or world consequence has already occurred. | Ratification rejects until phrased as a question/stake. |
| Determinism | Same draft and same selected record data are reviewed twice. | Same eligibility/visibility findings in the same canonical reference order. |
| No-write | Before/after catalog and runtime inventory comparison. | No entity, component, relationship, event, notification, operation, or clock change. |
| Repository | Planning document update only. | git diff --check passes; no catalog validation is required because C0 changes no catalog content. |

### Exit gate

C0 is complete only when the host explicitly ratifies the complete CampaignBriefReview and the
review records no unresolved validation, scope, visibility, or outcome-precommit issue. Stop before
a campaign ID, CampaignBlueprint schema, procedure, query/commit surface, entity, component,
relationship, mechanic, event, subscription, or database write. C1 is next.

## Plan-change rule

Revise this plan instead of ratifying if the brief needs a new world record, a new NPC/faction/
knowledge fact, a player character, item, quest, audience enforcement, AI-generated content as
authority, or a world change. Each is owned by another governed feature and must be completed or
separately planned before C1.
