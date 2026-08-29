// Administrative zero-Hit-Point policy recording for D&D 2024 creatures.
// Governed by procedure.mechanic.dnd2024.zero-hit-points-policy.
var subject = ctx.roles.subject;
var input = ctx.input;
var definitionId = 'dnd2024.zero-hit-points-policy';
var sourceRef = { sourceId: 'source.dnd2024.srd-5.2.1', locator: 'Playing the Game > Damage and Healing > Dropping to 0 Hit Points' };

function closed(value, keys) {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return false;
  var actual = Object.keys(value).sort();
  if (actual.length !== keys.length) return false;
  for (var index = 0; index < keys.length; index++) if (actual[index] !== keys[index]) return false;
  return true;
}
function validPolicy(value) { return value === 'death-saves' || value === 'die-at-zero'; }
function validState(value) { return closed(value, ['policy', 'sourceRef']) && validPolicy(value.policy) && closed(value.sourceRef, ['locator', 'sourceId']) && value.sourceRef.sourceId === sourceRef.sourceId && value.sourceRef.locator === sourceRef.locator; }
function parse(raw) { try { return JSON.parse(raw); } catch (error) { throw new Error('The stored zero-Hit-Point policy is malformed.'); } }

if (!subject) throw new Error('A subject role is required.');
if (!closed(input, ['mode', 'policy'])) throw new Error('Input must contain exactly mode and policy. Do not supply sourceRef, creature type, Hit Points, damage, or effects.');
if (input.mode !== 'record' && input.mode !== 'correct') throw new Error('input.mode must be exactly "record" or "correct".');
if (!validPolicy(input.policy)) throw new Error('input.policy must be exactly "death-saves" or "die-at-zero".');

var raw = subject.components && subject.components[definitionId];
var previous = null;
if (raw) {
  previous = parse(raw);
  if (!validState(previous)) throw new Error('The stored zero-Hit-Point policy is invalid. Use a governed migration.');
}
if (input.mode === 'record' && previous) throw new Error('A zero-Hit-Point policy is already recorded. Use mode "correct".');
if (input.mode === 'correct' && !previous) throw new Error('A zero-Hit-Point policy is absent. Use mode "record".');

var state = { policy: input.policy, sourceRef: sourceRef };
var effectType = input.mode === 'record' ? 'component.add' : 'component.set';
ctx.log(input.mode + ' zero-Hit-Point policy ' + input.policy + ' for ' + subject.name + '.');
return {
  narration: subject.name + "'s zero-Hit-Point policy is " + (input.mode === 'record' ? 'recorded' : 'corrected') + ' as ' + input.policy + '.',
  effects: [{ type: effectType, entityId: subject.id, definitionId: definitionId, data: JSON.stringify(state) }],
  data: { mode: input.mode, previousPolicy: previous ? previous.policy : null, policy: input.policy, sourceRef: sourceRef }
};
