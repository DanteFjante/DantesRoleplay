// Administrative D&D 2024 death-state transition.
// Governed by procedure.mechanic.dnd2024.death-state.
var subject = ctx.roles.subject;
var input = ctx.input;
var definitionId = 'dnd2024.death-state';
var sourceRef = { sourceId: 'source.dnd2024.srd-5.2.1', locator: 'Playing the Game > Damage and Healing > Dropping to 0 Hit Points' };

function closed(value, keys) {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return false;
  var actual = Object.keys(value).sort();
  if (actual.length !== keys.length) return false;
  for (var index = 0; index < keys.length; index++) if (actual[index] !== keys[index]) return false;
  return true;
}
function tally(value) { return typeof value === 'number' && isFinite(value) && Math.floor(value) === value && value >= 0 && value <= 2; }
function validState(value) {
  return closed(value, ['dead', 'failures', 'sourceRef', 'stable', 'successes']) &&
    tally(value.successes) && tally(value.failures) && typeof value.stable === 'boolean' && typeof value.dead === 'boolean' &&
    !(value.stable && value.dead) && !(value.stable && (value.successes !== 0 || value.failures !== 0)) &&
    closed(value.sourceRef, ['locator', 'sourceId']) && value.sourceRef.sourceId === sourceRef.sourceId && value.sourceRef.locator === sourceRef.locator;
}
function parse(raw) { try { return JSON.parse(raw); } catch (error) { throw new Error('The stored death state is malformed.'); } }
function resultState(value) { return { successes: value.successes, failures: value.failures, stable: value.stable, dead: value.dead }; }

if (!subject) throw new Error('A subject role is required.');
if (!input || typeof input !== 'object' || Array.isArray(input) || typeof input.mode !== 'string') throw new Error('Input must be a closed death-state transition object with a mode.');
var raw = subject.components && subject.components[definitionId];
var previous = null;
if (raw) {
  previous = parse(raw);
  if (!validState(previous)) throw new Error('The stored death state is invalid. Use a governed migration.');
}

if (input.mode === 'begin') {
  if (!closed(input, ['mode'])) throw new Error('Beginning death state requires exactly {"mode":"begin"}.');
  if (previous) throw new Error('Death state is already present and cannot begin again.');
  var begun = { successes: 0, failures: 0, stable: false, dead: false, sourceRef: sourceRef };
  return { narration: subject.name + ' begins death state.', effects: [{ type: 'component.add', entityId: subject.id, definitionId: definitionId, data: JSON.stringify(begun) }], data: { mode: 'begin', previous: null, state: resultState(begun), sourceRef: sourceRef } };
}

if (input.mode === 'end') {
  if (!closed(input, ['mode'])) throw new Error('Ending death state requires exactly {"mode":"end"}.');
  if (!previous) throw new Error('Death state is absent and cannot end.');
  if (previous.dead) throw new Error('Terminal death state cannot end.');
  return { narration: subject.name + "'s death state ends.", effects: [{ type: 'component.remove', entityId: subject.id, definitionId: definitionId }], data: { mode: 'end', previous: resultState(previous), state: null, sourceRef: previous.sourceRef } };
}

if (input.mode !== 'correct') throw new Error('input.mode must be exactly "begin", "correct", or "end".');
if (!closed(input, ['dead', 'failures', 'mode', 'stable', 'successes'])) throw new Error('Correcting death state requires exactly mode, successes, failures, stable, and dead.');
if (!previous) throw new Error('Death state is absent. Use mode "begin".');
var corrected = { successes: input.successes, failures: input.failures, stable: input.stable, dead: input.dead, sourceRef: sourceRef };
if (!validState(corrected)) throw new Error('Corrected death state is invalid: tallies are 0 through 2, Stable resets tallies, and Stable cannot be dead.');
if (previous.dead && !corrected.dead) throw new Error('Terminal death state cannot be cleared.');
ctx.log('Corrected death state for ' + subject.name + '.');
return { narration: subject.name + "'s death state is corrected.", effects: [{ type: 'component.set', entityId: subject.id, definitionId: definitionId, data: JSON.stringify(corrected) }], data: { mode: 'correct', previous: resultState(previous), state: resultState(corrected), sourceRef: sourceRef } };
