// Administrative armor-training recording for D&D 2024 creatures.
// Governed by procedure.mechanic.dnd2024.armor-training.
var subject = ctx.roles.subject;
var sourceRef = {
  sourceId: 'source.dnd2024.srd-5.2.1',
  locator: 'Rules Glossary > Armor Class and Armor Training'
};

function hasOnly(object, keys) {
  if (object === null || Array.isArray(object) || typeof object !== 'object') return false;
  var actual = Object.keys(object).sort();
  if (actual.length !== keys.length) return false;
  for (var i = 0; i < keys.length; i++) if (actual[i] !== keys[i]) return false;
  return true;
}

function validCategories(value) {
  if (!Array.isArray(value) || value.length > 4) return false;
  var previous = -1;
  for (var i = 0; i < value.length; i++) {
    var index = value[i] === 'light' ? 0 : value[i] === 'medium' ? 1 : value[i] === 'heavy' ? 2 : value[i] === 'shield' ? 3 : -1;
    if (index <= previous) return false;
    previous = index;
  }
  return true;
}

function validState(value) {
  return hasOnly(value, ['categories', 'sourceRef']) && validCategories(value.categories) &&
         hasOnly(value.sourceRef, ['locator', 'sourceId']) &&
         value.sourceRef.sourceId === sourceRef.sourceId && value.sourceRef.locator === sourceRef.locator;
}

if (!hasOnly(ctx.input, ['categories', 'mode'])) {
  throw new Error('Input must contain exactly {"mode":"record"|"correct","categories":["light"|"medium"|"heavy"|"shield"...]}. Do not supply sourceRef, grant provenance, class, species, armor, Shield, Armor Class, D20 effects, Speed, spellcasting, actions, or effects.');
}
var mode = ctx.input.mode;
if (mode !== 'record' && mode !== 'correct') throw new Error('input.mode must be exactly "record" or "correct".');
if (!validCategories(ctx.input.categories)) throw new Error('input.categories must be a canonical duplicate-free subset ordered light, medium, heavy, then shield. An empty array means known training with none of those categories.');

var raw = subject.components['dnd2024.armor-training'];
var previous = null;
if (raw) {
  try { previous = JSON.parse(raw); }
  catch (error) { throw new Error('The existing armor-training component is corrupt and cannot be corrected by this rule. Use a governed migration.'); }
  if (!validState(previous)) throw new Error('The existing armor-training component has an invalid shape and cannot be corrected by this rule. Use a governed migration.');
}
if (mode === 'record' && raw) throw new Error('Armor training is already recorded. Use mode "correct" to replace the complete known category set.');
if (mode === 'correct' && !raw) throw new Error('Armor training is absent. Use mode "record" to establish the complete known category set.');

var record = { categories: ctx.input.categories, sourceRef: sourceRef };
var effectType = mode === 'record' ? 'component.add' : 'component.set';
ctx.log(mode + ' armor training [' + record.categories.join(', ') + '].');
return {
  narration: subject.name + "'s armor training is " + (mode === 'record' ? 'recorded' : 'corrected') + ' as [' + record.categories.join(', ') + '].',
  effects: [{ type: effectType, entityId: subject.id, definitionId: 'dnd2024.armor-training', data: JSON.stringify(record) }],
  data: { mode: mode, categories: record.categories, previousCategories: previous === null ? null : previous.categories, sourceRef: sourceRef }
};
