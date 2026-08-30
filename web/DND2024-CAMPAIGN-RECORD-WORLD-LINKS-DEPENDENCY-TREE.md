# DND2024 Campaign record World links dependency tree

Status: **read projection and explicit post-closure write workflow accepted**
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
| Live recap content | ended session, `dnd2024.game.core.campaign.session-recap`, `procedure.campaign.session` | verified with separate World links | The immutable recap remains unchanged; reviewed associations are empty-data relationships on the ended session. |
| Live outcome content | terminal `dnd2024.campaign.arc` | verified with separate World links | Arc component bytes remain unchanged; reviewed associations are empty-data relationships on the terminal arc. |
| Clue subject | `dnd2024.game.core.world.knowledge.about` | verified internally | Canonical knowledge documents retain exact subject ID/name; the authorized notebook intentionally omits entity IDs and hides the subject for familiarity-only entries. |
| Player-safe World targets | authorized knowledge projection | verified for text and known location labels; missing for general link targets | Current notebook output exposes no subject identity suitable for exact navigation. |
| DM World targets | exact location/world directories | verified | Structured people/factions/locations are admitted only in DM perspective. |
| Places Visited | `game.core.campaign.location-visit` | verified separately | A narrative reference still never implies or increments a visit. |

## Dependency tree

~~~text
Live Campaign record → World entity links                                  [verified]
├─ 1. Existing projected-link navigation                                  [verified]
│  ├─ locations open exact World Location                                 [verified]
│  ├─ people/creatures open exact World People card                       [verified]
│  └─ factions open exact selected World Faction card                     [verified]
├─ 2. Authoritative record association                                    [verified]
│  ├─ ended session → World entity association owner                      [verified]
│  ├─ terminal arc → World entity association owner                       [verified]
│  └─ explicit post-closure capture without changing record prose         [verified]
├─ 3. Audience-safe clue subject reference                                [verified]
│  ├─ reuse existing knowledge.about; no parallel clue link               [verified]
│  ├─ omit subject for familiarity/recognition                            [verified invariant]
│  └─ emit exact subject reference only when target is audience-projected [confirmed]
└─ 4. Closed live web projection                                          [verified]
   ├─ resolve targets only from same-world projected directories          [verified]
   ├─ omit unknown, cross-world, malformed, and unauthorized targets      [verified]
   └─ preserve separation between narrative reference and visit state     [verified invariant]
~~~

## Conflicts and decisions

| Conflict | Decision |
| --- | --- |
| Useful links versus absent live association data | Keep live link arrays empty until an authoritative association exists. Never search recap/outcome/clue prose for names. |
| Immutable recap versus later references | Keep recap component bytes immutable. Store associations as separate empty-data relationships on the session/chapter/arc entity. |
| One association kind versus target-specific kinds | Use `dnd2024.game.core.campaign.record.references-world-entity`: “this campaign record explicitly references this World entity.” Projected target type determines the UI destination. |
| Clue links versus a duplicate campaign relationship | Reuse the existing `knowledge.about` subject. Do not add a second clue association. |
| Familiarity versus link disclosure | Familiarity-only knowledge continues to reveal neither proposition subject nor link. |
| Link versus visit | A reference is narrative association only. It never creates or increments Places Visited. |
| Automatic capture versus caller authority | The reviewed application mechanic can target only an entity already present on the active campaign's exact `campaign.references` edges. The browser remains read-only. |

## Ordered leaves

| Order | Leaf | Depends on | Exit gate |
| ---: | --- | --- | --- |
| 1 | Existing projected-link navigation | Current read model | All three target types navigate exactly and unknown IDs fail closed. |
| 2 | Confirm campaign-record association owner | User confirmation | One permanent relationship meaning and allowed source/target records are fixed. |
| 3 | Author and test association capture during play | 2, W10, G9 | Exact links are created only by the trusted closure owner; malformed/cross-world/unauthorized input leaves no relationship. |
| 4 | Audience-safe clue subject projection | Existing `knowledge.about` plus public-surface confirmation | Known/evidence content may emit an exact projected subject; familiarity and omitted targets leak nothing. |
| 5 | Live web link projection | 3–4 and Leaf 1 | Recaps, outcomes, and clues emit only same-audience projected World links; retries are read-only and deterministic. |

## Lowest ready leaf

All leaves are implemented for the current DM campaign projection. The application actions run only
after the existing lifecycle has produced an ended session or terminal arc, preserve component
bytes, require exact campaign ownership/relevance, and use G9's typed replay-safe action transaction.

## Confirmation gates

Confirmed on 2026-08-30:

- one new empty-data relationship from a campaign record entity (ended session/closed chapter or
  terminal arc) to an exact World entity, with a permanent DND2024-qualified ID;
- the trusted play/campaign procedure captures associations beside record closure, never in recap
  bytes; enabling that write waits for the replacement W10/G9 workflow;
- the authorized notebook public response may expose an exact knowledge subject reference only for
  non-familiar admitted content and only when the target is present in the same projected World; and
- this relationship is narrative reference only and never a campaign visit.

## Planning receipt

- Runtime artifacts created by this dependency tree: none.
- D&D rule calculations or content introduced: none.
- Deliberate exclusions: inferred associations, Places Visited, recap mutation, and browser writes.
