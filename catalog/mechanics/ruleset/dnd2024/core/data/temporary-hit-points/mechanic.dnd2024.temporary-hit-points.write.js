// D&D 2024 Temporary Hit Point grant and expiry transition.
// Governed by procedure.mechanic.dnd2024.temporary-hit-points.
var subject = ctx.roles.subject;
var input = ctx.input;
var definitionId = 'dnd2024.temporary-hit-points';
var maxSafe = 9007199254740991;
var source = { sourceId: 'source.dnd2024.srd-5.2.1', locator: 'Playing the Game > Damage and Healing > Temporary Hit Points' };

function closed(value, keys) {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return false;
  var actual = Object.keys(value).sort();
  if (actual.length !== keys.length) return false;
  for (var index = 0; index < keys.length; index++) if (actual[index] !== keys[index]) return false;
  return true;
}
function positiveSafe(value) { return typeof value === 'number' && isFinite(value) && Math.floor(value) === value && value >= 1 && value <= maxSafe; }
function validState(value) { return closed(value, ['amount', 'sourceRef']) && positiveSafe(value.amount) && closed(value.sourceRef, ['locator', 'sourceId']) && value.sourceRef.sourceId === source.sourceId && value.sourceRef.locator === source.locator; }
function parse(raw) { try { return JSON.parse(raw); } catch (error) { throw new Error('The stored Temporary Hit Point state is malformed.'); } }

if (!subject) throw new Error('A subject role is required.');
if (!input || typeof input !== 'object' || Array.isArray(input) || typeof input.mode !== 'string') throw new Error('Input must be a closed Temporary Hit Point transition object with a mode.');
var raw = subject.components && subject.components[definitionId];
var previous = null;
if (raw) { previous = parse(raw); if (!validState(previous)) throw new Error('The stored Temporary Hit Point state is invalid.'); }

if (input.mode === 'expire') {
  if (!closed(input, ['mode'])) throw new Error('Expiry requires exactly {"mode":"expire"}.');
  if (!previous) throw new Error('Temporary Hit Points are absent and cannot expire.');
  return { narration: subject.name + "'s " + previous.amount + ' Temporary Hit Points expire.', data: { mode: 'expire', previousAmount: previous.amount, grantedAmount: null, resultingAmount: null, kept: false, replaced: false, discardedAmount: previous.amount, sourceRef: previous.sourceRef }, effects: [{ type: 'component.remove', entityId: subject.id, definitionId: definitionId }] };
}

if (input.mode !== 'grant') throw new Error('input.mode must be exactly "grant" or "expire".');
var expected = previous ? ['amount', 'mode', 'onExisting'] : ['amount', 'mode'];
if (!closed(input, expected)) throw new Error(previous ? 'Granting over an existing buffer requires exactly amount, mode, and onExisting.' : 'Granting a first buffer requires exactly amount and mode.');
if (!positiveSafe(input.amount)) throw new Error('Temporary Hit Point amount must be a positive safe integer; zero is represented by component absence.');
if (previous && input.onExisting !== 'keep' && input.onExisting !== 'replace') throw new Error('onExisting must be exactly "keep" or "replace" when a buffer exists.');

var state = { amount: input.amount, sourceRef: source };
if (!previous) {
  return { narration: subject.name + ' gains ' + input.amount + ' Temporary Hit Points.', data: { mode: 'grant', previousAmount: null, grantedAmount: input.amount, resultingAmount: input.amount, kept: false, replaced: false, discardedAmount: null, sourceRef: source }, effects: [{ type: 'component.add', entityId: subject.id, definitionId: definitionId, data: JSON.stringify(state) }] };
}
if (input.onExisting === 'keep') {
  return { narration: subject.name + ' keeps ' + previous.amount + ' Temporary Hit Points and discards ' + input.amount + '.', data: { mode: 'grant', previousAmount: previous.amount, grantedAmount: input.amount, resultingAmount: previous.amount, kept: true, replaced: false, discardedAmount: input.amount, sourceRef: previous.sourceRef }, effects: [] };
}
return { narration: subject.name + ' replaces ' + previous.amount + ' Temporary Hit Points with ' + input.amount + '.', data: { mode: 'grant', previousAmount: previous.amount, grantedAmount: input.amount, resultingAmount: input.amount, kept: false, replaced: true, discardedAmount: previous.amount, sourceRef: source }, effects: [{ type: 'component.set', entityId: subject.id, definitionId: definitionId, data: JSON.stringify(state) }] };
