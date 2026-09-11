// Application rules live here; the host only supplies the declared, bounded graph.
var graph = ctx.graphSnapshots && ctx.graphSnapshots.worldChronology;
var perspective = ctx.audience && ctx.audience.perspective;
var CAMPAIGN = 'game.core.campaign.root';
var WORLD = 'game.core.world.root';
var CLOCK = 'game.core.world.clock';
var RECORD = 'game.core.world.chronology';
var IN_WORLD = 'game.core.world.chronology.in-world';
var ABOUT = 'game.core.world.chronology.about';
var SUBJECT_SCOPES = ['game.core.world.faction.in-world', 'game.core.world.knowledge.in-world'];
var PAGE_ENTRY_LIMIT = 40;
var PAGE_BYTE_LIMIT = 60000;
var coverage = 'complete';

function object(value) { return value !== null && typeof value === 'object' && !Array.isArray(value); }
function own(value, key) { return object(value) && Object.prototype.hasOwnProperty.call(value, key); }
function text(value, maximum) {
  return typeof value === 'string' && value.length > 0 && value.length <= maximum && value.trim() === value;
}
function empty(value) { return value !== null && typeof value === 'object' && !Array.isArray(value) && Object.keys(value).length === 0; }
function fingerprint(value) { return typeof value === 'string' && /^[0-9A-F]{64}$/.test(value); }
function pageInput(value) {
  if (!object(value)) return null;
  var keys = Object.keys(value);
  for (var keyIndex = 0; keyIndex < keys.length; keyIndex++)
    if (keys[keyIndex] !== 'cursor' && keys[keyIndex] !== 'expectedSourceRevision' &&
        keys[keyIndex] !== 'expectedGraphSourceRevision') return null;
  var hasCursor = own(value, 'cursor'), hasRevision = own(value, 'expectedSourceRevision'),
    hasGraphRevision = own(value, 'expectedGraphSourceRevision');
  if (hasCursor !== hasRevision || hasCursor !== hasGraphRevision) return null;
  if (!hasCursor) return { offset: 0 };
  if (typeof value.cursor !== 'string' || !/^[1-9][0-9]{0,2}$/.test(value.cursor) ||
      Number(value.cursor) > 500 || !fingerprint(value.expectedSourceRevision) ||
      !fingerprint(value.expectedGraphSourceRevision)) return null;
  return { offset: Number(value.cursor) };
}
function utf8Bytes(value) {
  // encodeURIComponent emits one %XX triplet per UTF-8 byte and leaves every
  // remaining ASCII character as one byte. Built-ins keep byte accounting out
  // of the mechanic statement budget while preserving escaped/unicode size.
  return encodeURIComponent(JSON.stringify(value)).replace(/%[0-9A-F]{2}/g, 'x').length;
}
// These fixed qualified keys cannot name a JavaScript prototype member. The
// graph's component dictionary is already a host-owned JSON object.
function component(node, id) { return node && node.components ? node.components[id] : null; }
function pageData(status, entries, reason, nextCursor) {
  var fieldCoverage = status === 'unavailable' ? 'partial' : coverage;
  var data = { status: status, perspective: perspective === 'dm' ? 'dm' : 'player',
    entries: entries, coverage: status === 'unavailable' || nextCursor ? 'partial' : fieldCoverage,
    fieldCoverage: fieldCoverage };
  if (graph && text(graph.sourceRevisionFingerprint, 128)) data.sourceRevision = graph.sourceRevisionFingerprint;
  if (reason) data.reason = reason;
  if (nextCursor) data.nextCursor = nextCursor;
  return data;
}
function outcome(status, entries, reason, nextCursor) {
  var data = pageData(status, entries, reason, nextCursor);
  return { narration: status === 'unavailable' ? 'World history is unavailable.' : 'World history projected.',
    effects: [], events: [], notifications: [], data: data };
}
function invalid(reason) { return outcome('unavailable', [], reason); }
function group(edges) {
  var result = Object.create(null);
  for (var i = 0; i < edges.length; i++) {
    var edge = edges[i];
    if (!result[edge.fromEntityId]) result[edge.fromEntityId] = [];
    result[edge.fromEntityId].push(edge);
  }
  return result;
}
function nodes(values) {
  var result = Object.create(null);
  for (var i = 0; i < values.length; i++) {
    var node = values[i];
    if (!node || !text(node.id, 200) || result[node.id]) return null;
    result[node.id] = node;
  }
  return result;
}
function stepValid(step) {
  // The generic graph reader already owns node/edge envelope validation and
  // deduplication. Validate application relationship meaning only where used.
  return step && step.complete === true && Array.isArray(step.nodes) && Array.isArray(step.edges);
}

var requestedPage = pageInput(ctx.input);
if (!requestedPage || !graph || graph.complete !== true || !graph.root || !object(graph.steps) ||
    !Array.isArray(graph.containment) || !fingerprint(graph.sourceRevisionFingerprint) ||
    (perspective !== 'player' && perspective !== 'dm')) return invalid('CHRONOLOGY_CONTEXT_INVALID');
if (requestedPage.offset > 0 && ctx.input.expectedGraphSourceRevision !== graph.sourceRevisionFingerprint)
  return invalid('CHRONOLOGY_SOURCE_CHANGED');
var stepIds = ['world', 'records', 'recordWorlds', 'subjects', 'subjectScopes', 'subjectAncestors'];
for (var stepIndex = 0; stepIndex < stepIds.length; stepIndex++) {
  if (!stepValid(graph.steps[stepIds[stepIndex]])) return invalid('CHRONOLOGY_GRAPH_INCOMPLETE');
}
var campaign = component(graph.root, CAMPAIGN);
if (!campaign || campaign.status !== 'active' || campaign.rulesetScope !== 'dnd2024')
  return invalid('CHRONOLOGY_SELECTION_INVALID');
var worldStep = graph.steps.world;
if (worldStep.edges.length !== 1 || worldStep.nodes.length !== 1 ||
    worldStep.edges[0].kind !== 'game.core.campaign.in-world' ||
    worldStep.edges[0].fromEntityId !== graph.root.id ||
    worldStep.edges[0].toEntityId !== worldStep.nodes[0].id || !empty(worldStep.edges[0].data))
  return invalid('CHRONOLOGY_WORLD_INVALID');
var world = worldStep.nodes[0];
var worldRoot = component(world, WORLD), clock = component(world, CLOCK);
if (!worldRoot || worldRoot.status !== 'active' || !clock || !text(clock.calendarId, 100))
  return invalid('CHRONOLOGY_WORLD_INVALID');

var records = nodes(graph.steps.records.nodes);
var selectedWorlds = group(graph.steps.records.edges);
var recordWorlds = group(graph.steps.recordWorlds.edges);
var subjects = nodes(graph.steps.subjects.nodes);
var about = group(graph.steps.subjects.edges);
var subjectScopes = group(graph.steps.subjectScopes.edges);
if (!records || !subjects) return invalid('CHRONOLOGY_GRAPH_INCOMPLETE');
// Count only visible, active records before doing optional display work. Hidden
// or archived rows must not consume the viewer's 500-entry allowance.
var recordIds = Object.keys(records).sort(), visibleRecords = [];
for (var countIndex = 0; countIndex < recordIds.length; countIndex++) {
  var counted = component(records[recordIds[countIndex]], RECORD);
  if (!counted || (counted.status !== 'active' && counted.status !== 'archived') ||
      ['public', 'party', 'gm'].indexOf(counted.visibility) < 0) { coverage = 'partial'; continue; }
  if (counted.status === 'active' && (perspective === 'dm' || counted.visibility !== 'gm'))
    visibleRecords.push({ id: recordIds[countIndex], data: counted });
}
if (visibleRecords.length > 500) return invalid('CHRONOLOGY_ENTRY_LIMIT');
var parents = Object.create(null);
for (var parentIndex = 0; parentIndex < graph.containment.length; parentIndex++) {
  var edge = graph.containment[parentIndex];
  if (!edge || !text(edge.containedEntityId, 200) || !text(edge.containerEntityId, 200))
    return invalid('CHRONOLOGY_CONTAINMENT_INVALID');
  if (own(parents, edge.containedEntityId) && parents[edge.containedEntityId] !== edge.containerEntityId)
    return invalid('CHRONOLOGY_CONTAINMENT_INVALID');
  parents[edge.containedEntityId] = edge.containerEntityId;
}
function inWorld(id) {
  if (id === world.id) return true;
  var direct = subjectScopes[id] || [];
  if (direct.length) return direct.length === 1 && SUBJECT_SCOPES.indexOf(direct[0].kind) >= 0 &&
    direct[0].toEntityId === world.id && empty(direct[0].data);
  var visited = Object.create(null), current = id;
  for (var depth = 0; depth < 16; depth++) {
    if (own(visited, current) || !own(parents, current)) return false;
    visited[current] = true;
    current = parents[current];
    if (current === world.id) return true;
  }
  return false;
}
var orderedRecords = [];
for (var recordIndex = 0; recordIndex < visibleRecords.length; recordIndex++) {
  var id = visibleRecords[recordIndex].id, record = visibleRecords[recordIndex].data;
  var selected = selectedWorlds[id] || [], allWorlds = recordWorlds[id] || [];
  if (selected.length !== 1 || allWorlds.length !== 1 || selected[0].kind !== IN_WORLD ||
      allWorlds[0].kind !== IN_WORLD || selected[0].toEntityId !== world.id || allWorlds[0].toEntityId !== world.id ||
      !empty(selected[0].data) || !empty(allWorlds[0].data))
    return invalid('CHRONOLOGY_SCOPE_INVALID');
  if (record.calendarId !== clock.calendarId) return invalid('CHRONOLOGY_CALENDAR_INVALID');
  var subjectEdges = about[id] || [];
  if (subjectEdges.length > 10) return invalid('CHRONOLOGY_SUBJECT_LIMIT');
  var seenSubjects = Object.create(null);
  for (var subjectIndex = 0; subjectIndex < subjectEdges.length; subjectIndex++) {
    var subjectEdge = subjectEdges[subjectIndex], subject = subjects[subjectEdge.toEntityId];
    if (subjectEdge.kind !== ABOUT || !subject || own(seenSubjects, subject.id) ||
        !empty(subjectEdge.data) || !inWorld(subject.id)) return invalid('CHRONOLOGY_SUBJECT_SCOPE_INVALID');
    seenSubjects[subject.id] = true;
  }
  orderedRecords.push({ id: id, data: record, subjectEdges: subjectEdges });
}
orderedRecords.sort(function(a, b) {
  var aMinute = Number.isSafeInteger(a.data.occurredAtMinute) ? a.data.occurredAtMinute : Infinity;
  var bMinute = Number.isSafeInteger(b.data.occurredAtMinute) ? b.data.occurredAtMinute : Infinity;
  return aMinute < bMinute ? -1 : aMinute > bMinute ? 1 : a.id < b.id ? -1 : a.id > b.id ? 1 : 0;
});
function projectEntry(row, globalIndex) {
  var record = row.data, entry = { id: 'chronology-' + (globalIndex + 1) };
  if (text(record.title, 160)) entry.title = record.title;
  else coverage = 'partial';
  if (text(record.summary, 1000)) entry.summary = record.summary;
  else coverage = 'partial';
  if (text(record.dateLabel, 100)) entry.dateLabel = record.dateLabel;
  else coverage = 'partial';
  if (['exact', 'approximate', 'era'].indexOf(record.precision) >= 0) entry.precision = record.precision;
  else coverage = 'partial';
  if (Number.isSafeInteger(record.occurredAtMinute) && record.occurredAtMinute >= -1000000000 &&
      record.occurredAtMinute <= 1000000000) entry.occurredAtMinute = record.occurredAtMinute;
  else coverage = 'partial';
  if (perspective === 'dm') {
    var projectedSubjects = [];
    for (var subjectIndex = 0; subjectIndex < row.subjectEdges.length; subjectIndex++) {
      var subject = subjects[row.subjectEdges[subjectIndex].toEntityId];
      if (text(subject.name, 400)) projectedSubjects.push({ id: subject.id, name: subject.name });
      else coverage = 'partial';
    }
    entry.subjects = projectedSubjects.sort(function(a, b) { return a.id < b.id ? -1 : a.id > b.id ? 1 : 0; });
  }
  return entry;
}
if (!orderedRecords.length) {
  if (requestedPage.offset !== 0) return invalid('CHRONOLOGY_CURSOR_INVALID');
  return outcome('empty', []);
}
if (requestedPage.offset >= orderedRecords.length) return invalid('CHRONOLOGY_CURSOR_INVALID');
var page = [];
var pageEntryBytes = 0;
for (var pageIndex = requestedPage.offset;
    pageIndex < orderedRecords.length && page.length < PAGE_ENTRY_LIMIT; pageIndex++) {
  var entry = projectEntry(orderedRecords[pageIndex], pageIndex);
  var candidateCursor = pageIndex + 1 < orderedRecords.length ? String(pageIndex + 1) : null;
  var entryBytes = utf8Bytes(entry);
  var candidateBytes = utf8Bytes(pageData('ready', [], null, candidateCursor)) +
    pageEntryBytes + entryBytes + (page.length ? page.length : 0);
  if (candidateBytes > PAGE_BYTE_LIMIT) {
    if (!page.length) return invalid('CHRONOLOGY_ENTRY_SIZE');
    break;
  }
  page.push(entry);
  pageEntryBytes += entryBytes;
}
var nextOffset = requestedPage.offset + page.length;
var nextCursor = nextOffset < orderedRecords.length ? String(nextOffset) : null;
if (!page.length || utf8Bytes(pageData('ready', page, null, nextCursor)) > PAGE_BYTE_LIMIT)
  return invalid('CHRONOLOGY_PAGE_SIZE');
return outcome('ready', page, null, nextCursor);
