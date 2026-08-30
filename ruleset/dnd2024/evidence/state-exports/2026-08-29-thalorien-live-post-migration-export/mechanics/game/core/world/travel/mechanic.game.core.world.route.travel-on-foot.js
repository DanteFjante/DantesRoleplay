// Deterministic one-route on-foot journey. Governed by procedure.game.core.world.travel and time.
var traveller = ctx.roles.traveller, origin = ctx.roles.origin, destination = ctx.roles.destination;
var route = ctx.roles.route, world = ctx.roles.world;
var travellerId = 'game.core.world.traveller', locationId = 'game.core.world.location';
var routeId = 'game.core.world.route', availabilityId = 'game.core.world.route.availability', rootId = 'game.core.world.root', clockId = 'game.core.world.clock';
var adjacencyKind = 'game.core.world.location.connected-to';
var scopeKind = 'game.core.world.route.in-world', fromKind = 'game.core.world.route.from', toKind = 'game.core.world.route.to';

function closed(value, keys) { if (value === null || Array.isArray(value) || typeof value !== 'object') return false; var actual = Object.keys(value).sort(); if (actual.length !== keys.length) return false; for (var i = 0; i < keys.length; i++) if (actual[i] !== keys[i]) return false; return true; }
function parse(raw, name) { if (typeof raw !== 'string') throw new Error(name + ' is corrupt.'); try { return JSON.parse(raw); } catch (error) { throw new Error(name + ' is corrupt.'); } }
function text(value, maximum) { return typeof value === 'string' && value.length >= 1 && value.trim() === value && Array.from(value).length <= maximum; }
function integer(value, minimum, maximum) { return typeof value === 'number' && Number.isSafeInteger(value) && value >= minimum && value <= maximum; }
function routeLink(edge, kind, target) { return edge.kind === kind && edge.fromEntityId === route.id && edge.toEntityId === target && closed(parse(edge.data, 'Route relationship data'), []); }
function validLocation(value) { return closed(value, ['kind', 'status', 'summary', 'visibility']) && (value.kind === 'region' || value.kind === 'settlement' || value.kind === 'site' || value.kind === 'interior') && value.status === 'active' && text(value.summary, 1000) && (value.visibility === 'public' || value.visibility === 'party' || value.visibility === 'gm'); }

if (!closed(ctx.input, [])) throw new Error('Route journey input must be exactly {}. Do not supply route, mode, duration, origin, destination, clock, effects, or result.');
if (!traveller || !origin || !destination || !route || !world || !traveller.components || !origin.components || !destination.components || !route.components || !world.components) throw new Error('Route journey requires traveller, origin, destination, route, and world roles.');
var ids = [traveller.id, origin.id, destination.id, route.id, world.id];
if (ids.some(function (id) { return typeof id !== 'string' || id.length === 0; }) || new Set(ids).size !== 5) throw new Error('Route journey roles must name five distinct entities.');
if (!traveller.components[travellerId] || !origin.components[locationId] || !destination.components[locationId] || !route.components[routeId] || !route.components[availabilityId] || !world.components[rootId] || !world.components[clockId]) throw new Error('Route journey roles are missing required traveller, location, route availability, root, or clock components.');
var travellerState = parse(traveller.components[travellerId], 'Traveller state');
var originState = parse(origin.components[locationId], 'Origin location state');
var destinationState = parse(destination.components[locationId], 'Destination location state');
var routeState = parse(route.components[routeId], 'Route state');
var availabilityState = parse(route.components[availabilityId], 'Route availability');
var rootState = parse(world.components[rootId], 'World root state');
var clockState = parse(world.components[clockId], 'World clock state');
if (!closed(travellerState, ['status']) || travellerState.status !== 'active') throw new Error('Traveller state is invalid or inactive.');
if (!validLocation(originState) || !validLocation(destinationState)) throw new Error('Origin or destination location state is invalid or inactive.');
if (!closed(routeState, ['durationMinutes', 'mode', 'status', 'summary', 'visibility']) || routeState.status !== 'active' || !text(routeState.summary, 1000) || (routeState.visibility !== 'public' && routeState.visibility !== 'party' && routeState.visibility !== 'gm') || routeState.mode !== 'on-foot' || !integer(routeState.durationMinutes, 1, 1440)) throw new Error('Route state is invalid or inactive.');
if (!closed(availabilityState, ['status']) || (availabilityState.status !== 'open' && availabilityState.status !== 'closed')) throw new Error('Route availability is corrupt.');
if (availabilityState.status !== 'open') throw new Error('Route is currently closed.');
if (!closed(rootState, ['status', 'summary', 'visibility']) || rootState.status !== 'active' || !text(rootState.summary, 1000) || (rootState.visibility !== 'public' && rootState.visibility !== 'party' && rootState.visibility !== 'gm')) throw new Error('World root is invalid or inactive.');
if (!closed(clockState, ['calendarId', 'currentMinute', 'revision']) || !text(clockState.calendarId, 100) || !integer(clockState.currentMinute, 0, 1000000000) || !integer(clockState.revision, 0, 2147483647)) throw new Error('World clock is corrupt.');
if (traveller.containerId !== origin.id || traveller.containerSlot !== 'presence') throw new Error('Traveller is not currently present at the claimed origin.');
if (!origin.containerId || origin.containerId !== destination.containerId || origin.containerSlot !== 'location' || destination.containerSlot !== 'location') throw new Error('Origin and destination must be active sibling locations.');
if (!Array.isArray(origin.relationships) || !Array.isArray(route.relationships)) throw new Error('Route journey relationship projection is missing. Re-read the mechanic requirements.');

var adjacency = 0;
for (var i = 0; i < origin.relationships.length; i++) {
  var edge = origin.relationships[i];
  if (!closed(edge, ['data', 'fromEntityId', 'kind', 'toEntityId']) || typeof edge.fromEntityId !== 'string' || typeof edge.toEntityId !== 'string' || typeof edge.kind !== 'string' || typeof edge.data !== 'string') throw new Error('Origin relationship projection is corrupt.');
  if (edge.kind !== adjacencyKind) continue;
  if (edge.fromEntityId === edge.toEntityId || (edge.fromEntityId !== origin.id && edge.toEntityId !== origin.id) || edge.fromEntityId >= edge.toEntityId || !closed(parse(edge.data, 'Adjacency data'), [])) throw new Error('Origin has corrupt noncanonical adjacency state.');
  if ((edge.fromEntityId === origin.id && edge.toEntityId === destination.id) || (edge.fromEntityId === destination.id && edge.toEntityId === origin.id)) adjacency++;
}
if (adjacency !== 1) throw new Error('Origin and destination must have exactly one stored canonical adjacency connection.');

var scope = 0, from = 0, to = 0;
for (var j = 0; j < route.relationships.length; j++) {
  var link = route.relationships[j];
  if (!closed(link, ['data', 'fromEntityId', 'kind', 'toEntityId']) || typeof link.fromEntityId !== 'string' || typeof link.toEntityId !== 'string' || typeof link.kind !== 'string' || typeof link.data !== 'string') throw new Error('Route relationship projection is corrupt.');
  if (link.fromEntityId !== route.id) continue;
  if ((link.kind !== scopeKind && link.kind !== fromKind && link.kind !== toKind) || !closed(parse(link.data, 'Route relationship data'), [])) throw new Error('Route has corrupt scope or endpoint links.');
  if (routeLink(link, scopeKind, world.id)) scope++;
  else if (routeLink(link, fromKind, origin.id)) from++;
  else if (routeLink(link, toKind, destination.id)) to++;
  else throw new Error('Route does not bind the claimed world, origin, and destination.');
}
if (scope !== 1 || from !== 1 || to !== 1 || scope + from + to !== 3) throw new Error('Route must have exactly one scope, origin, and destination link.');
if (clockState.currentMinute > 1000000000 - routeState.durationMinutes || clockState.revision === 2147483647) throw new Error('Route journey cannot advance the world clock beyond its confirmed bounds.');
var nextClock = { calendarId: clockState.calendarId, currentMinute: clockState.currentMinute + routeState.durationMinutes, revision: clockState.revision + 1 };
return { narration: traveller.name + ' travels from ' + origin.name + ' to ' + destination.name + ' along ' + route.name + '.', effects: [{ type: 'containment.move', entityId: traveller.id, toEntityId: destination.id, slot: 'presence' }, { type: 'component.set', entityId: world.id, definitionId: clockId, data: JSON.stringify(nextClock) }], events:[{type:'game.core.world.clock.advanced',payload:{worldId:world.id,calendarId:clockState.calendarId,beforeMinute:clockState.currentMinute,afterMinute:nextClock.currentMinute,beforeRevision:clockState.revision,afterRevision:nextClock.revision},entityIds:[world.id],scope:world.id}], data: { test: 'route-travel-on-foot', travellerId: traveller.id, routeId: route.id, worldId: world.id, originId: origin.id, destinationId: destination.id, mode: routeState.mode, minutes: routeState.durationMinutes, previousMinute: clockState.currentMinute, currentMinute: nextClock.currentMinute, previousRevision: clockState.revision, currentRevision: nextClock.revision } };
