# D&D 2024 Campaign Places Visited — Slice 19 implementation

Status: **DM read and trusted write accepted; Player projection remains pending A5**
Owner/roadmap: `web/WEB-INTERFACE-ROADMAP.md`, Feature 5
Dependency tree/leaf: `web/DND2024-CAMPAIGN-PLACES-VISITED-DEPENDENCY-TREE.md`, Leaves 1 and 3 DM read side
Ruleset alignment: **ruleset-neutral owner; dnd2024-compatible web adapter**
Source: explicit application ECS component and relationships
Outcome: define the canonical visit record and populate Places Visited for an authorized DM when exact records exist.
Exclusions: clock mutation, migration, Player raw-relationship reads, inference, deployment, and fixture rewrites.
Allowed areas: generic campaign component catalog; D&D live adapter/read model/focused tests; the Places Visited tree and one receipt.
Stop point: the catalog validates, exact DM records project, malformed/ambiguous/unknown records fail closed, Player stays empty, and no writes exist.

## State and relationship contract

`game.core.campaign.location-visit` is a closed aggregate record:

- `firstVisitedMinute` and `lastVisitedMinute`: non-negative safe integers; reader additionally requires
  `lastVisitedMinute >= firstVisitedMinute`.
- `visitCount`: integer from 1 through 1,000,000.
- `status`: `current` or `departed`, explicitly written rather than inferred.
- `summary`: bounded party-safe description of the place in campaign continuity.
- `memory`: bounded party-safe account of what the party remembers there.
- optional `gmContext`: bounded DM-only continuity.

The campaign owns the record via `has-location-visit`; the record targets exactly one location via
`location-visit.at-location`. Both relationships have empty data. Duplicate campaign/location
records, multiple location targets, missing components, and targets absent from the projected World
are omitted rather than merged or guessed.

## Read projection

- Only DM perspective reads raw visit relationships/components in this slice.
- The adapter caps the campaign at 100 visit records and each visit at one exact location target.
- The final Hub joins the target to the already-projected World location and uses that canonical name
  and region. Stored minute coordinates display as `Campaign minute N` until the G6 calendar bridge.
- Player and DM Player-preview remain empty until A5 owns a server-filtered campaign projection.

## Trusted write projection

- `dnd2024.mechanic.campaign.location-visit.record` creates or updates the exact aggregate record in
  one typed application action transaction.
- The action requires an active campaign, its exact active World, the World clock, an active location
  already referenced by the campaign, and the existing derived visit record when updating.
- The first/last minute is read from the clock; callers supply no time or identity. Exact operation
  replay does not increment the record again.

## Acceptance matrix

| Case | Expected |
| --- | --- |
| one valid visit and projected location | one Places Visited card |
| target not in projected World | omitted |
| two targets, duplicate location record, malformed component | omitted |
| current location/map click without visit record | no visit |
| Player or Player-preview | empty without raw visit fetch |

## Verification

- `roleplay validate catalog`
- focused/full D&D web tests and production build
- isolated build for the affected web server project
- shared C# tests only if the current unrelated compile blocker is cleared

## Completion evidence

Acceptance will be recorded in
`web/evidence/dnd2024/DND2024-CAMPAIGN-PLACES-VISITED-SLICE-19-RECEIPT.md`.
