# D&D 2024 Campaign Places Visited dependency tree

Status: **owner, replay-safe writer, and DM read projection implemented; Player projection blocked**
Ruleset alignment: **ruleset-neutral owner with dnd2024-compatible projection**
Source: explicit campaign history state in application ECS
Owner/roadmap: `web/WEB-INTERFACE-ROADMAP.md`, Feature 5; complete-campaign graph W11 chronology

## Outcome

Places Visited is backed by one explicit campaign-location visit record. It is never inferred from
map clicks, current location, containment, prose, record references, or campaign membership.

## Dependency tree

~~~text
Campaign Places Visited                                                      [partial]
├─ 1. Canonical visit owner                                                  [verified]
│  ├─ game.core.campaign.location-visit component                            [verified]
│  ├─ campaign → visit ownership relationship                                [verified contract]
│  └─ visit → exact World location relationship                              [verified contract]
├─ 2. Trusted visit capture/update transaction                               [verified]
│  ├─ reviewed D&D campaign recording action                                 [verified]
│  ├─ authoritative world-clock coordinate                                   [verified by G6]
│  └─ replay-safe first/last/count update                                     [verified by G4/action tests]
├─ 3. Audience-safe read projection                                          [partial]
│  ├─ DM reads exact canonical records                                       [verified]
│  ├─ omit malformed, ambiguous, unknown, and non-location targets           [verified]
│  └─ Player reads through a server-filtered campaign projection             [blocked by A5]
└─ 4. Existing Campaign UI                                                    [verified]
   ├─ list/filter/detail                                                      [verified]
   └─ empty state when no canonical records are projected                    [verified]
~~~

## Confirmed owner contract

- Component: `game.core.campaign.location-visit`, qualified in D&D runtime as
  `dnd2024.game.core.campaign.location-visit`.
- Ownership: `dnd2024.game.core.campaign.has-location-visit` from campaign to visit entity.
- Target: `dnd2024.game.core.campaign.location-visit.at-location` from visit to one World location.
- The component owns `firstVisitedMinute`, `lastVisitedMinute`, `visitCount`, `status`, `summary`,
  `memory`, and optional `gmContext`.
- One accepted record represents the campaign's aggregate memory for one location. The trusted writer
  derives one campaign/location identity, enforces exact ownership/target edges and monotonic
  minutes/count, and reads the minute from the World clock; the browser cannot write it.

## Deliberate boundaries

The Player projection remains empty until A5 provides a server-filtered campaign envelope;
fetching raw visit relationship IDs from a Player browser would violate the secrecy contract.
