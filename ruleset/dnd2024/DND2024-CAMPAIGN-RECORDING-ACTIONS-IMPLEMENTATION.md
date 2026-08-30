# D&D 2024 campaign recording actions implementation

Status: active implementation slice
Owner: complete-campaign W10/W11 bridge and Campaign tab Features 5/18/19

## Boundary

This slice adds three reviewed D&D application actions. They do not replace the existing campaign
lifecycle owner or edit retained recap/outcome prose:

- an ended session may receive one explicit reference to an entity already referenced by its active
  campaign;
- a resolved or abandoned arc may receive the same explicit reference;
- an active campaign may capture or update its one aggregate visit record for an active referenced
  World location at the authoritative World clock minute.

The permanent mechanic ids are:

- `dnd2024.mechanic.campaign.session.reference-world-entity`;
- `dnd2024.mechanic.campaign.arc.reference-world-entity`;
- `dnd2024.mechanic.campaign.location-visit.record`.

## Invariants

- Session references require an ended session with an immutable recap and its exact
  `campaign.has-session` edge.
- Arc references require a terminal arc and its exact `campaign.has-arc` edge.
- A reference target must already be an exact `campaign.references` target. This prevents the
  recording action from expanding campaign or World membership.
- A visit location must be an active location already referenced by the campaign, and the campaign
  must have exactly one `campaign.in-world` edge to the projected active World whose clock is read.
- The visit entity id is derived as `<campaign-id>.visit.<location-id>`. A new record starts at count
  one; an update must supply that exact existing record and increments once. Minutes never move
  backward and callers cannot provide them.
- All effects are typed application-ECS effects in one action transaction. Operation identity owns
  replay; a conflicting retry fails without partial state.

## Deliberate exclusions

- No current location or map interaction implies a visit.
- No action creates campaign membership, World membership, a session, recap, arc, outcome, or clue.
- Player projection is a following slice because it requires a server-filtered campaign envelope;
  raw relationship enumeration remains forbidden for Player seats.
