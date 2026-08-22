# Campaign Feature 14 dependency plan — advancement policy and authorization

> **D&D implementation reference:** When this work includes D&D 2024 mechanics, inspect [Foundry VTT dnd5e](https://github.com/foundryvtt/dnd5e) before designing or coding. Use it as a licensed engineering reference—not a direct dependency or rules authority—while retaining the exact `source.dnd2024.srd-5.2.1` locator and all applicable MIT, CC-BY, and asset-license notices for any reused material.

Status: **Planned; C15 now supplies the campaign-bound active-character scope, but Slice 1 still awaits the CH9 consume seam and semantic confirmation. The later XP bridge also awaits the Feature 36 eligibility seam.**
Last updated: 2026-08-21

## Target capability

A campaign can use one deliberate advancement policy—XP or milestone—to issue one auditable,
character-specific authorization for exactly the next total character level. The authorization is
consumed only within the character owner's successful level-up transaction.

### Included

- One immutable active-campaign policy: `xp` or `milestone`.
- Campaign-owned, actor-specific `N→N+1` advancement authorization lifecycle: available, consumed,
  or revoked.
- A trusted-host milestone issuance path and a typed authorization projection/consume seam for CH9.
- Later integration points for the ruleset-owned XP award/threshold reader.

### Excluded

- Character XP totals, XP threshold tables, character/class-level changes, Hit Points, class
  features, grants, feats, subclasses, multiclassing, rest recovery, rewards, quest completion,
  or automatic advancement.
- A campaign actor roster, player authorization, party voting, changing policy in place, raw effects,
  caller-supplied target level, nested transactions, or a new MCP kind.

## Authority and source boundary

This is campaign policy, not an SRD rule implementation. It consumes the D&D source-backed
threshold/level result only from Feature 36. Its authoritative cross-owner contracts are:

| Concern | Owner and required seam |
| --- | --- |
| Campaign root and transaction | C2 / `procedure.campaign.create` and current campaign runner conventions. |
| Campaign continuity | C3 chapter/arc state remains unrelated; a milestone must not close a chapter or resolve a quest. |
| Character attachment/lifecycle | CH5/CH6 and later character lifecycle/control owners must prove an active character belongs to this campaign. |
| XP total and threshold | Feature 36 only. C14 receives a typed eligibility result; it never stores, calculates, or trusts XP. |
| Exact level transition | Existing `dnd2024.character-level` remains authoritative; C14 reads a typed current/next-level result and never accepts an arbitrary level. |
| Level-up effects and consume | CH9 owns the one root transaction. C14 supplies/consumes only its authorization within that root. |

## Recursive dependency analysis

```text
C14 campaign advancement authorization                                  [blocked parent]
├─ active campaign root and transaction/audit conventions                [implemented: C2/C3 evidence]
├─ active campaign-bound playable character relation/lifecycle           [missing: CH5/CH6/CH13 evidence]
├─ Feature 36 total-XP / exact-next-level eligibility result             [missing: Feature 36 Slice 1]
├─ CH9 typed authorization-consume composition seam                      [planned: character Feature 9]
├─ policy + authorization vocabulary confirmation                        [semantic leaf]
│  └─ Slice 1: policy record and milestone authorization
└─ XP award integration                                                   [blocked: Feature 36 Slice 2]
```

The first runtime delivery deliberately supports only the milestone branch. It proves the campaign
authorization owner without inventing XP state or treating a quest/chapter event as an automatic
milestone. XP later produces the identical authorization shape through Feature 36's verified
eligibility result.

## Ownership decisions

1. **One policy per campaign, no implicit default.** Missing policy means advancement is not
   configured. The first write records an immutable mode; a later switch requires an explicit
   migration/reconciliation plan, never a normal correction action.
2. **Authorization is campaign state, not character state.** It belongs to one campaign and one
   active character through canonical directed links. It records no character XP, class, HP,
   campaign copy, source prose, result bundle, or operation id.
3. **The next level is derived.** A milestone issuer supplies a character and stale-intent guard,
   not `fromLevel`/`toLevel`; C14 reads the character owner's exact current total level and derives
   the one next level. Level 20 fails as capped, never wraps or creates a level 21 authorization.
4. **Authorization is permission, not advancement.** Issuance has no character effects. CH9 repeats
   all state checks, consumes the authorization in its own root transaction, and performs class/HP/
   grant effects. A failed CH9 action leaves it available.
5. **XP and milestone converge only at the seam.** Feature 36's XP reader may request the same
   exact authorization only after it proves the actor reached the next threshold. C14 does not
   store a second eligibility flag or threshold snapshot.

## Confirmation-required vocabulary

The following names are proposals, not authorized runtime IDs:

| Role | Proposed vocabulary and meaning |
| --- | --- |
| Campaign policy | `game.core.campaign.advancement-policy`: closed `{ mode: "xp" | "milestone" }`, attached only to an active campaign root. |
| Authorization entity/state | One `campaign.advancement-authorization` child with closed state `available`, `consumed`, or `revoked`; exact derived `fromTotalLevel`/`toTotalLevel`; basis `xp` or `milestone`; and no copied campaign/character IDs. |
| Links | Empty-data campaign-to-authorization and authorization-to-character links. Cardinality and direction must be confirmed with C2/character attachment conventions. |
| Campaign contract | `procedure.campaign.advancement`, governing policy record, issuance/revocation, trusted projection, and CH9 consume handoff. |
| Operations | Administrative `record-advancement-policy`, trusted `issue-milestone-advancement`, optional `revoke-advancement`; no player phrase is reserved. |

## Slice order and stop gates

| Order | Slice | Starts only when | Exit gate |
| ---: | --- | --- | --- |
| 1 | Policy and milestone authorization | Vocabulary, campaign-character attachment, CH9 consume seam, and level-cap behavior confirmed | One active campaign issues/revokes a single exact next-level milestone authorization with no character change; duplicate/stale/cross-scope paths remain unchanged. |
| 2 | Feature 36 XP authorization bridge | Slice 1 and Feature 36 XP reader verified | A threshold-eligible character receives the same authorization shape once; below threshold produces none. C14 computes no XP. |
| 3 | CH9 atomic level-up integration | Slices 1–2, CH9, and Feature 27 level/HP/feature resolver verified | Consumption and level-up effects commit or roll back together; both policy modes use the one CH9 root. |
| 4 | Expanded advancement | One supported 1→2 path accepted | Additional class/source levels only through CH9/Feature 27/CH10–12 amendments. |

## Slice 1 — policy and milestone authorization

### Required state and closed requests

The policy recorder accepts only campaign role and `{ mode }`; it requires an active campaign and
absent policy. The milestone issuer accepts only campaign role, one active character role, and an
expected current total-level/revision guard. It does not accept XP, a target level, reason text,
class, source definition, grant, HP, effects, campaign id, or authorization id. The campaign/actor
scope is derived through the confirmed attachment.

Authorization creation derives its child ID from the campaign plus a canonical server-owned local
key or uses a confirmed generated-ID convention; callers never choose it. One available
authorization for the same character/current transition is the maximum. Existing consumed/revoked
records remain immutable history. A valid revocation changes only an available authorization to
revoked; it never repairs or deletes history.

### Effects and acceptance matrix

Record policy produces one complete policy effect. Issue produces one child entity, one state
component, and the confirmed two links. Revoke is one complete state replacement. None writes a
character, class, XP, quest, chapter, clock, inventory, or source record.

| Case | Required assertion |
| --- | --- |
| Happy path | A milestone-policy campaign issues one `N→N+1` available authorization for its attached level-`N` active character; character bytes are identical. |
| Closed input | Missing/extra mode, invalid mode, caller target level/XP/effects/id, malformed expected state, wrong role, and duplicate key reject with no effects. |
| Scope/lifecycle | Missing/archived campaign, inactive/retired/foreign character, absent policy, XP policy on milestone operation, level 20, or stale actor state reject unchanged. |
| Replay | Reissuing the same current transition returns a stable duplicate/available result and creates no second authorization. |
| Revocation | Only available same-campaign authorization becomes revoked; consumed/revoked or cross-scope targets reject unchanged. |
| Transaction | Inject each entity/component/link/event/audit failure; no partial authorization, policy, event, or success audit remains. |
| Handoff | A CH9 dry-run sees exactly the available authorization and derives the same `N→N+1`; a forced CH9 failure leaves it available. |
| Readback/cleanup | Fresh campaign projection orders authorizations canonically and identifies no XP total; disposable fixtures are deleted through normal governed paths. |

## Plan-change rule

Revise before implementation if campaign-character attachment is owned differently, CH9 cannot
consume a campaign child atomically, Feature 36 chooses a non-actor XP owner, or a campaign needs
shared/party-level rather than per-character advancement. Do not use quest completion, chapter
closure, activity history, or an AI judgment as hidden authorization state.
