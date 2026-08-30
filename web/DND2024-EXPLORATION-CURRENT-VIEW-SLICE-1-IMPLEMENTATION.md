# D&D 2024 Exploration Current View Slice 1 implementation — authoritative location scene

Status: **implementation complete; feature acceptance pending 2026-08-30**
Owner/roadmap: `web/WEB-INTERFACE-ROADMAP.md`, Feature 5
Dependency tree/leaf: `web/DND2024-WEB-INFORMATION-HUB-DEPENDENCY-TREE.md`, Leaf 11
Ruleset alignment: **dnd2024-compatible**
Source ID and locator: **not applicable**. This slice presents authorized world state and defines no D&D rule.
Outcome: replace the Current View placeholder with a responsive Exploration scene sourced from the
ambient actor's exact current location, its authorized observations, co-present people, known exits,
and optional authorized location image.
Exclusions: Combat or Conversation selection, encounter reads, current-conversation ownership,
travel calculation, inferred exits, state writes, model calls, new catalog IDs/schemas, and fixture
fallback for unavailable live state.
Allowed files/areas: `src/system/web-interface/dnd2024` server adapter, closed envelope types and
projection, Current View React component/styles, focused JavaScript tests, this implementation
document, its receipt, and the owning roadmap/dependency row.
Stop point: stop when Exploration renders only an exact projected location or a friendly unavailable
state; do not begin Leaf 12.

## Confirmed decisions

- The user's 2026-08-30 request to implement the D&D 2024 Current View activates this bounded
  Exploration slice.
- Existing direct `presence` containment is the only current-location selector. The browser must not
  choose the first location, infer a place from prose, or promote a selected atlas location to current.
- A local Game Master seat has no actor identity. Until a campaign-owned current-scene selector
  exists, it receives an explicit unavailable Current View rather than a guessed location.
- Missing known exits or imagery render honest empty states and never block the rest of the scene.

## D&D 5e 2024 alignment

No D&D rule calculation, action economy, encounter decision, movement rule, or content meaning is
implemented. Existing D&D-compatible world component identities are read as already-authorized
presentation input only.

## External implementation reference

Foundry dnd5e review is not applicable because this slice defines no D&D mechanic, rule data, or
character/encounter behavior.

## Prerequisite evidence

- `DND2024-WEB-UI-SLICE-7C-RECEIPT.md` accepts the exact direct-containment current-location read and
  bounded co-present people behavior.
- `DND2024-LOCAL-TABLE-DM-SEAT-SLICE-1-RECEIPT.md` records the ambient actor/Game Master distinction
  and non-escalating Player preview implementation.
- The current React hub already projects audience-filtered locations, observations, people, map
  variants, and empty routes through one closed envelope.

## Runtime artifacts

- No new route, permanent ID, schema, migration, component type, mechanic, procedure, or effect.
- The connected adapter adds the actor's exact projected current-location ID to its internal
  connection envelope after validating a direct `presence` edge against the authorized location
  directory.
- The existing ready hub envelope keeps `world.currentLocationId` as the scene selector; an empty
  value means the server did not project an authoritative current location.

## Authoritative state and closed input

- Application, state space, campaign, role, actor, and effective perspective come only from the
  server-issued audience context.
- For an actor seat, the adapter reads exactly that actor's direct containment. The edge must name
  the same actor, use slot `presence`, and point to a location already admitted by the authorized
  location directory.
- The browser supplies no actor, location, scene kind, or audience value. Atlas selection and local
  storage never alter Current View authority.

## Behavior, result, and typed effects

- A valid projected actor location renders an Exploration heading, location identity/description,
  all authorized observations, co-present people, known exits, and an authorized location-scope
  image when one exists.
- Missing observations, people, exits, or image each have independent visible empty behavior.
- DM-only location context may render only when it is already present in the DM envelope. Player and
  DM-as-Player responses contain none of it.
- This slice is read-only and creates no effects or transaction.

## Failure, replay, and rollback contract

- Missing, malformed, wrong-actor, non-`presence`, or nonprojected containment yields an unavailable
  Current View and no location guess.
- Failed optional image/people/route data leaves the remaining authorized scene readable.
- Equal authorized envelopes render the same scene. No replay, rollback, or database change applies.

## Implementation sequence

1. Add the exact actor-presence read and closed-envelope validation with focused negative tests.
2. Replace the placeholder Current View with the responsive Exploration composition and safe empty
   state.
3. Run the complete web tests and canonical server-bundle build, then record a receipt without
   starting Combat or Conversation work.

## Acceptance matrix

| Case | Expected result |
| --- | --- |
| actor with exact projected `presence` location | authoritative Exploration scene |
| actor containment missing/malformed/wrong slot | friendly unavailable state; no first-location fallback |
| Game Master seat without actor | friendly unavailable state |
| authorized observations/people/routes | all render in dedicated sections |
| optional authorized location image | renders with its projected alt text |
| missing optional scene fields | independent empty states; scene remains readable |
| Player or DM-as-Player | no DM-only text, people, image, count, or identifier appears |
| repeated read | deterministic and read-only |

## Verification commands

- `npm test`
- `npm run build:server`
- `git diff --check -- src/system/web-interface/dnd2024 web`

No catalog validation, full .NET suite, or MCP protocol walk is required because this slice changes
no catalog record, C# runtime surface, or MCP dependency registration.

## Completion receipt and exit gate

Record the delivered scene, exact-containment tests, complete web suite, server build, and deliberate
Leaf 12 exclusions in `DND2024-EXPLORATION-CURRENT-VIEW-SLICE-1-RECEIPT.md`. Stop before Combat,
Conversation, encounter selection, current-scene persistence, or travel logic.
