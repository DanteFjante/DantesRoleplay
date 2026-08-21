# Campaign Feature 15 dependency plan — campaign-owned character participation

Status: **Slices 1–3 are implemented. C15 now exposes CH13's effect-free withdrawal fragment;
the CH13 lifecycle root remains its planned consumer.**
Last updated: 2026-08-21

## Target capability

An active campaign can hold one durable, auditable participation record that associates it with
one pre-existing actor. The campaign, not the actor or a character profile, owns the association
and its availability for ordinary player-character use. It gives Character CH1/CH5 one canonical
active-scope verifier and gives CH13 one typed campaign-side withdrawal transition.

The initial fixture is one existing active C2 campaign and one existing actor. A trusted host
attaches that actor once; CH1 may then record its descriptive profile, and a later CH13 retirement
can withdraw the participation without deleting or mutating the actor. This proves campaign scope
and lifecycle handoff, not party management, player authentication, NPC management, transfer, or
general roster editing.

## Ownership and boundary

| Concern | Owner / decision |
| --- | --- |
| Campaign scope and participation history | C15. A participation record belongs to exactly one campaign and points to exactly one actor. |
| Actor identity, display name, profile, origins, classes, items, and rules | Character and Ruleset owners. C15 stores none of them. |
| Character lifecycle | CH13. It owns `active -> retired -> archived`; C15 owns only whether the campaign offers the attached actor as an active participant. |
| Character creation | CH5. It creates the actor and composes C15's attachment planner in its one root transaction. C15 does not create an actor. |
| Player authentication/control and audience | CH14 and policy owners. An active participation is not a user account, permission, roster seat, or player-control grant. |
| Sessions and encounter rosters | Session S5 and later session owners. They consume this scope but do not infer it from an arbitrary actor link. |

### Included

- One campaign-owned participation entity with closed availability state and two canonical links.
- Attach validation, active-scope projection, and a typed atomic withdrawal child transition.
- A composition seam for CH5 and CH13 that contributes effects to their root transaction without a
  nested commit.
- Trusted-host attach/withdraw behavior, bounded readback, lifecycle/scope/cardinality checks,
  audit/event participation, rollback, and fresh-context tests.

### Excluded

- Actor creation, character marker/profile, source choices, abilities, class/level, inventory,
  possession, locations, quests, sessions, encounters, XP, advancement, or character deletion.
- Player identities, administrator policy, audience labels, invitations, player count, co-control,
  parties, formations, NPC conversion, replacement characters, campaign transfer, reactivation,
  or a generic relationship editor.
- Copying a campaign ID, lifecycle, account, authorization, profile, item list, source data, or
  history list into the actor or character component.

## Implemented model

These permanent names and meanings are confirmed and implemented through Slice 2. The existing
`campaign` commit kind carries the closed trusted-host attach request; no new MCP tool or kind was
added.

| Role | Proposed vocabulary and closed meaning |
| --- | --- |
| Participation entity | Server-derived exact `<campaignId>.participation.<actorId>`. It has no actor/campaign ID in its name-independent data. Callers never select it. A request whose derived id exceeds the canonical ID boundary is rejected before effects. |
| State component | `game.core.campaign.character-participation`, exactly `{ "status": "active" | "withdrawn" }`. It contains no actor/campaign IDs, character/profile marker, dates, reason, user, party role, inventory, class, rules, or operation ID. |
| Campaign link | `game.core.campaign.has-character-participation`, directed empty-data link from campaign root to participation. |
| Actor link | `game.core.campaign.character-participation.for-actor`, directed empty-data link from participation to the existing actor. |
| Governing procedure | `procedure.campaign.character-participation`, defining validation, cardinality, typed projection, attachment, withdrawal, transactions, and recovery. |
| Operations | `attach-character-participation` is implemented on the existing governed campaign command family. `withdraw-character-participation` remains reserved for C15 Slice 3/CH13; no new command kind is proposed. |

`active` means only that Campaign offers this actor as an active player-character participation
candidate in that campaign. It says nothing about the actor's D&D state, player control,
authentication, encounter presence, or whether any later character profile has been recorded.
`withdrawn` preserves the campaign's historical attachment but fails all active-scope checks. It
is irreversible in C15; a transfer, replacement, or reactivation needs its own plan.

## Invariants and verifier

1. A participation has exactly one active-or-withdrawn state component, exactly one campaign
   parent link, and exactly one actor link; all links carry `{}`.
2. The linked campaign is an active C2 campaign. Its root is never copied into participation data.
3. An actor has at most one participation record across all campaigns in this first contract,
   including withdrawn history. C15 therefore has no implicit cross-campaign move.
4. An active-scope verifier accepts only an `active` participation whose campaign and actor links
   are structurally valid and unique. It returns a typed campaign scope to callers; callers do not
   provide or persist a campaign-ID assertion.
5. Withdrawal is `active -> withdrawn` only. Absent, withdrawn, malformed, duplicate,
   cross-scope, or stale requests fail unchanged.
6. C15 never removes the actor, links, component, receipts, events, or audit history. It changes
   only the complete participation state component in the root transaction that requested it.

The attachment deliberately does not require a CH1 profile: CH5 must establish campaign scope
before it can create that profile. Conversely, CH1 must call this verifier before it writes its
profile. This avoids circular ownership while preventing a profile from becoming a campaign ID
surrogate.

## Contract and transaction composition

The trusted-host attach request accepts an active campaign role and one pre-existing actor role.
It accepts no campaign ID in payload, participation ID, status, actor name, profile, effects,
relationships, audit/event data, role/party label, or authorization assertion. The runner resolves
the canonical actors from declared roles, verifies the invariants above, derives the participation
ID, and returns a closed result containing only the actor ID, participation ID, current status, and
one literal recovery/read action.

For CH5, the same owner exposes an internal typed planner: given the already validated active
campaign role, reserved actor ID, and prior virtual effects, it validates the virtual actor and
returns exactly the entity/component/link effects needed for attachment. It performs no write,
opens no transaction, and accepts no raw effects. CH5's ActionRunner remains the only root that
applies the returned bundle.

For CH13, C15 exposes a typed withdrawal planner that validates the existing active participation
and contributes one complete state-replacement effect. CH13's lifecycle root applies it together
with its character-lifecycle change. A failed child validation or any effect/event/audit failure
rolls back both changes. C15 must not infer retirement itself.

The public attach/withdraw dispatch, if confirmed, is thin and delegates to one semantic runner.
It follows the existing campaign transaction, structural-event, guard/subscription, and audit
conventions; neither an MCP handler nor a child planner performs an independent commit.

## Recursive dependency analysis

```text
C15 campaign-owned character participation                              [planned parent]
├─ C2 active campaign root + campaign transaction/audit conventions     [implemented]
├─ generic entity/relationship/component effect validation               [implemented foundation]
├─ confirmed participation vocabulary + public/internal composition seam [semantic leaf]
│  └─ Slice 1: schema/procedure and zero-write verifier
├─ confirmed attach root operation and derived-ID convention             [semantic leaf]
│  └─ Slice 2: one trusted-host attachment transaction
└─ CH13 lifecycle contract                                               [planned consumer]
   └─ Slice 3: typed withdrawal planner                                  [implemented]

CH1 Slice 2 profile recording and CH5 atomic creation consume Slice 2.
CH13 consumes Slice 3. C14 and Session S5 consume the verified active-scope projection only.
```

## Slice order and stop gates

| Order | Slice | Starts only when | Exit gate |
| ---: | --- | --- | --- |
| 1 | Participation definition and active-scope verifier | All proposed vocabulary, cardinality, irreversible-withdrawal rule, and no-new-kind/public dispatch boundary are confirmed. | Catalog/procedure validation proves the closed state/link shape; a zero-effect verifier accepts exactly one structurally active campaign-actor attachment and rejects every malformed/ambiguous scope. |
| 2 | Trusted-host attach transaction and CH5 planner | Slice 1 verified; derived-ID policy, attach operation, runner, audit/event behavior, and test fixture are confirmed. | One active campaign attaches one existing actor atomically; the effect-free planner validates the same closed attachment against a staged actor; duplicate, cross-campaign, inactive, stale, and injected-failure paths leave no participation evidence. CH1 and CH5 can consume the verifier/planner boundary. |
| 3 | Withdrawal composition seam | Slice 2 verified; C15's active-scope and no-standalone-command boundaries remain confirmed. | An internal planner returns one typed withdrawal fragment without writing. Its containing root can roll back the fragment; CH13 remains responsible for proving its own combined lifecycle atomicity. |

### Slice 1 receipt

**Verified 2026-08-21.** The closed `game.core.campaign.character-participation` component,
`procedure.campaign.character-participation`, and `CampaignCharacterParticipationVerifier` now
resolve only one structurally valid active campaign scope for an existing actor. They add no
public command and write no state. `CampaignFeature15Tests` covers valid, withdrawn, malformed,
duplicate, and inactive-campaign graphs; `roleplay validate catalog` passed with 253 records and
zero warnings. See [Slice 1 receipt](CAMPAIGN-FEATURE-15-SLICE-1-RECEIPT.md).

### Slice 3 receipt

**Implemented 2026-08-21.** `CampaignCharacterParticipationWithdrawalPlanner` accepts only an
actor id, reuses C15's canonical active-scope verifier, and returns exactly one complete
`component.set` replacement to `{"status":"withdrawn"}`. It neither writes, opens a transaction,
records an operation, nor creates a public campaign command. The consuming CH13 lifecycle root
will append this fragment to its own atomic bundle. See [Slice 3 receipt](CAMPAIGN-FEATURE-15-SLICE-3-RECEIPT.md).

## Acceptance matrix

| Case | Required result |
| --- | --- |
| Valid attachment | One active campaign plus one pre-existing actor yields one derived participation entity, closed active component, two empty-data links, normal structural evidence, and one root success audit. |
| Closed input | Missing/extra/null/wrong-type role/payload fields, caller IDs/status/effects/links/audit/user/profile fields, or unknown operation reject before effects. |
| Scope/cardinality | Missing/inactive campaign, absent actor, duplicate links/components, actor already attached or withdrawn elsewhere, cross-campaign request, or malformed participation rejects unchanged. |
| Verifier | Only one valid active attachment returns scope; absent, withdrawn, ambiguous, malformed, or inactive-campaign paths return no usable scope. |
| Withdrawal | Only exact active state becomes withdrawn through the typed child path; actor, profile, items, source choices, campaign root, world, chapter/arc/quest, and history bytes remain unchanged. |
| Atomicity | Fail each entity/component/link/guard/event/subscription/audit/cancellation point; no partial participation, structural event, notification, or success audit persists. |
| Consumer handoff | CH1 profile recorder accepts only the C15 typed active scope; CH5 uses the planner without nested commit; CH13 failure rolls back its participation transition. |
| Fresh readback | A new context reconstructs canonical campaign-to-participation-to-actor scope, never discovers it from actor data or arbitrary graph traversal. |

## Evidence and change control

Each implemented slice records confirmed vocabulary, focused test names/results, catalog-validation
result, and the exact consumer seam it enables. Run the full suite only at C15 feature acceptance;
run the protocol walk only if the confirmed campaign command registration changes.

Revise before implementation if a campaign must support transfer/rejoin, a participation must
represent NPCs or a party/formation, CH5 cannot consume virtual attachment effects in its root,
CH13 needs a different withdrawal state, or authorization requires data on the participation.
Do not solve any of those by adding nullable fields, a second actor-to-campaign relationship,
character-owned campaign data, or a direct database write.
