// Shared-game deterministic one-hop world movement.
// Governed by procedure.game.core.world.travel.
var traveller = ctx.roles.traveller;
var origin = ctx.roles.origin;
var destination = ctx.roles.destination;
var connectionKind = 'game.core.world.location.connected-to';

function closed(value, keys) {
  if (value === null || Array.isArray(value) || typeof value !== 'object') { return false; }
  var actual = Object.keys(value).sort();
  if (actual.length !== keys.length) { return false; }
  for (var index = 0; index < keys.length; index++) {
    if (actual[index] !== keys[index]) { return false; }
  }
  return true;
}

function parse(raw, name) {
  if (typeof raw !== 'string') { throw new Error(name + ' is corrupt.'); }
  try { return JSON.parse(raw); }
  catch (error) { throw new Error(name + ' is corrupt.'); }
}

function validTraveller(value) {
  return closed(value, ['status']) && value.status === 'active';
}

function validLocation(value) {
  return closed(value, ['kind', 'status', 'summary', 'visibility']) &&
    (value.kind === 'region' || value.kind === 'settlement' || value.kind === 'site' || value.kind === 'interior') &&
    value.status === 'active' &&
    typeof value.summary === 'string' && value.summary.trim() === value.summary && value.summary.length >= 1 && value.summary.length <= 1000 &&
    (value.visibility === 'public' || value.visibility === 'party' || value.visibility === 'gm');
}

if (!closed(ctx.input, [])) {
  throw new Error('Adjacent movement input must be exactly {}. Do not supply origin, destination, connection, route, time, slot, result, or effects.');
}
if (!traveller || !origin || !destination || !traveller.components || !origin.components || !destination.components) {
  throw new Error('Adjacent movement requires traveller, origin, and destination roles.');
}
if (traveller.id === origin.id || traveller.id === destination.id || origin.id === destination.id) {
  throw new Error('Traveller, origin, and destination must be three distinct entities.');
}
if (!traveller.components['game.core.world.traveller']) {
  throw new Error('Traveller is missing game.core.world.traveller.');
}
if (!origin.components['game.core.world.location'] || !destination.components['game.core.world.location']) {
  throw new Error('Origin and destination must each carry game.core.world.location.');
}

var travellerState = parse(traveller.components['game.core.world.traveller'], 'Traveller state');
var originState = parse(origin.components['game.core.world.location'], 'Origin location state');
var destinationState = parse(destination.components['game.core.world.location'], 'Destination location state');
if (!validTraveller(travellerState)) { throw new Error('Traveller state is invalid or inactive.'); }
if (!validLocation(originState) || !validLocation(destinationState)) { throw new Error('Origin or destination location state is invalid or inactive.'); }
if (traveller.containerId !== origin.id || traveller.containerSlot !== 'presence') {
  throw new Error('Traveller is not currently present at the claimed origin.');
}
if (origin.containerId !== destination.containerId || origin.containerSlot !== 'location' || destination.containerSlot !== 'location' || !origin.containerId) {
  throw new Error('Origin and destination must be sibling locations in the same region or location.');
}
if (!Array.isArray(origin.relationships)) {
  throw new Error('Origin adjacency projection is missing. Re-read the movement mechanic requirements.');
}

var matchingEdges = 0;
for (var edgeIndex = 0; edgeIndex < origin.relationships.length; edgeIndex++) {
  var edge = origin.relationships[edgeIndex];
  if (!closed(edge, ['data', 'fromEntityId', 'kind', 'toEntityId']) ||
      typeof edge.fromEntityId !== 'string' || typeof edge.toEntityId !== 'string' ||
      typeof edge.kind !== 'string' || typeof edge.data !== 'string') {
    throw new Error('Origin relationship projection is corrupt.');
  }
  if (edge.kind !== connectionKind) { continue; }
  if (edge.fromEntityId === edge.toEntityId ||
      (edge.fromEntityId !== origin.id && edge.toEntityId !== origin.id) ||
      edge.fromEntityId >= edge.toEntityId ||
      !closed(parse(edge.data, 'Adjacency data'), [])) {
    throw new Error('Origin has corrupt noncanonical adjacency state.');
  }
  if ((edge.fromEntityId === origin.id && edge.toEntityId === destination.id) ||
      (edge.fromEntityId === destination.id && edge.toEntityId === origin.id)) {
    matchingEdges++;
  }
}
if (matchingEdges !== 1) {
  throw new Error('Origin and destination must have exactly one stored canonical adjacency connection.');
}

return {
  narration: traveller.name + ' travels from ' + origin.name + ' to ' + destination.name + '.',
  effects: [{ type: 'containment.move', entityId: traveller.id, toEntityId: destination.id, slot: 'presence' }],
  data: {
    test: 'adjacent-world-movement',
    travellerId: traveller.id,
    originId: origin.id,
    destinationId: destination.id,
    previousSlot: traveller.containerSlot,
    currentSlot: 'presence',
    adjacencyKind: connectionKind
  }
};
