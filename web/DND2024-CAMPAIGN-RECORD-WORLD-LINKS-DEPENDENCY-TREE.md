# DND2024 Campaign record World links dependency tree

Status: **planning only; live association owner awaiting confirmation**
Ruleset alignment: **dnd2024-compatible presentation and campaign continuity**
Source: authorized application ECS state; no D&D rule source applies
Owner/roadmap: `web/WEB-INTERFACE-ROADMAP.md`, Feature 5
Parent: `web/DND2024-WEB-INFORMATION-HUB-DEPENDENCY-TREE.md`, Campaign Leaf 9

## Outcome and non-goals

An authorized Campaign recap, terminal outcome, or evidence clue can carry exact links to relevant
World locations, people/creatures, and factions. The website opens only targets already present in
the same audience-projected World.

This work does not infer associations from prose, names, current location, map interaction, actor
presence, or campaign membership. It does not turn a link into a visit, mutate an immutable recap,
or make fixture IDs authoritative.

## Existing owners and evidence

| Concern | Owner | State | Evidence |
| --- | --- | --- | --- |
| Link presentation/navigation | `CampaignEntityLinks` and World directories | verified | Campaign link navigation Slice 17 resolves exact projected IDs and fails closed; its focused/full tests and production build pass. |
| Fixture association shape | authored `locationIds`, `personIds`, and `factionIds` in the fixture source | verified as presentation evidence only | `hub-envelope.js` filters every target through the projected World before emitting links. |
| Live recap content | ended session, `dnd2024.game.core.campaign.session-recap`, `procedure.campaign.session` | verified without World links | The immutable recap contains chapter/arc/milestone narrative only and deliberately copies no event ID or current World state. |
| Live outcome content | terminal `dnd2024.campaign.arc` | verified without World links | Arc schema owns status, title, party stake, GM context, and closing summary only. |
| Clue subject | `dnd2024.game.core.world.knowledge.about` | verified internally | Canonical knowledge documents retain exact subject ID/name; the authorized notebook intentionally omits entity IDs and hides the subject for familiarity-only entries. |
| Player-safe World targets | authorized knowledge projection | verified for text and known location labels; missing for general link targets | Current notebook output exposes no subject identity suitable for exact navigation. |
| DM World targets | exact location/world directories | verified | Structured people/factions/locations are admitted only in DM perspective. |
| Places Visited | no canonical owner | blocked | Live Campaign Slice 16 records the deliberate empty state. |

## Dependency tree

~~~text
Live Campaign record → World entity links                                  [planned]
├─ 1. Existing projected-link navigation                                  [verified]
│  ├─ locations open exact World Location                                 [verified]
│  ├─ people/creatures open exact World People card                       [verified]
│  └─ factions open exact selected World Faction card                     [verified]
├─ 2. Authoritative record association                                    [missing]
│  ├─ recap/session or closed-chapter → World entity association owner    [awaiting confirmation]
│  ├─ terminal arc → World entity association owner                       [awaiting confirmation]
│  └─ association capture during play without changing recap prose        [missing]
├─ 3. Audience-safe clue subject reference                                [planned]
│  ├─ reuse existing knowledge.about; no parallel clue link               [verified]
│  ├─ omit subject for familiarity/recognition                            [verified invariant]
│  └─ emit exact subject reference only when target is audience-projected [awaiting public-surface confirmation]
└─ 4. Closed live web projection                                          [blocked by 2–3]
   ├─ resolve targets only from same-world projected directories          [ready after 2–3]
   ├─ omit unknown, cross-world, malformed, and unauthorized targets      [ready after 2–3]
   └─ preserve empty Places Visited and no-write browser behavior         [verified invariant]
~~~

## Conflicts and decisions

| Conflict | Decision |
| --- | --- |
| Useful links versus absent live association data | Keep live link arrays empty until an authoritative association exists. Never search recap/outcome/clue prose for names. |
| Immutable recap versus later references | Keep recap component bytes immutable. If confirmed, store associations as separate empty-data relationships on the session/chapter/arc entity. |
| One association kind versus target-specific kinds | Proposed: one closed relationship meaning “this campaign record explicitly references this World entity”; projected target type determines the UI destination. The permanent ID and allowed source/target archetypes require confirmation. |
| Clue links versus a duplicate campaign relationship | Reuse the existing `knowledge.about` subject. Do not add a second clue association. |
| Familiarity versus link disclosure | Familiarity-only knowledge continues to reveal neither proposition subject nor link. |
| Link versus visit | A reference is narrative association only. It never creates or increments Places Visited. |
| Automatic capture versus caller authority | Do not let the browser or an arbitrary caller declare “relevance.” A later write slice must name the trusted play owner and closed capture operation. |

## Ordered leaves

| Order | Leaf | Depends on | Exit gate |
| ---: | --- | --- | --- |
| 1 | Existing projected-link navigation | Current read model | All three target types navigate exactly and unknown IDs fail closed. |
| 2 | Confirm campaign-record association owner | User confirmation | One permanent relationship meaning, allowed source/target records, and capture transaction owner are fixed. |
| 3 | Author and test association capture during play | 2 | Exact links are created only by the confirmed owner; malformed/cross-world/unauthorized input leaves no relationship. |
| 4 | Audience-safe clue subject projection | Existing `knowledge.about` plus public-surface confirmation | Known/evidence content may emit an exact projected subject; familiarity and omitted targets leak nothing. |
| 5 | Live web link projection | 3–4 and Leaf 1 | Recaps, outcomes, and clues emit only same-audience projected World links; retries are read-only and deterministic. |

## Lowest ready leaf

Leaf 1 is implemented and accepted. Leaf 2 is not ready because a permanent
relationship ID, allowed source/target meaning, and trusted capture workflow are semantic choices.

## Confirmation gates

Before live association work, confirm:

- one new empty-data relationship from a campaign record entity (ended session/closed chapter or
  terminal arc) to an exact World entity, with a permanent DND2024-qualified ID;
- whether the trusted play/campaign procedure captures those associations during record closure or
  a separate post-record annotation operation owns them;
- the authorized notebook public response may expose an exact knowledge subject reference only for
  non-familiar admitted content and only when the target is present in the same projected World; and
- this relationship is narrative reference only and never a campaign visit.

## Planning receipt

- Runtime artifacts created by this dependency tree: none.
- D&D rule calculations or content introduced: none.
- Deliberate exclusions: inferred associations, Places Visited, recap mutation, and browser writes.
