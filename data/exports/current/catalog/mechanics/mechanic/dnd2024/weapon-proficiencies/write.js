// Administrative weapon-category proficiency recording for D&D 2024 creatures.
// Governed by procedure.mechanic.dnd2024.weapon-proficiencies.
var subject = ctx.roles.subject;
var sourceRef = {
  sourceId: 'source.dnd2024.srd-5.2.1',
  locator: 'Equipment > Weapons > Weapon Proficiency'
};

function hasOnly(object, keys) {
  if (object === null || Array.isArray(object) || typeof object !== 'object') {
    return false;
  }
  var actual = Object.keys(object).sort();
  if (actual.length !== keys.length) {
    return false;
  }
  for (var i = 0; i < keys.length; i++) {
    if (actual[i] !== keys[i]) {
      return false;
    }
  }
  return true;
}

function validCategories(value) {
  if (!Array.isArray(value) || value.length > 2) {
    return false;
  }

  var previous = -1;
  for (var i = 0; i < value.length; i++) {
    var index = value[i] === 'simple' ? 0 : value[i] === 'martial' ? 1 : -1;
    if (index <= previous) {
      return false;
    }
    previous = index;
  }
  return true;
}

function validState(value) {
  return hasOnly(value, ['categories', 'sourceRef']) && validCategories(value.categories) &&
         hasOnly(value.sourceRef, ['locator', 'sourceId']) &&
         value.sourceRef.sourceId === sourceRef.sourceId &&
         value.sourceRef.locator === sourceRef.locator;
}

if (!hasOnly(ctx.input, ['categories', 'mode'])) {
  throw new Error('Input must contain exactly {"mode":"record"|"correct","categories":["simple"|"martial"...]}. Do not supply sourceRef, class, weapon, Proficiency Bonus, attacks, damage, or effects.');
}

var mode = ctx.input.mode;
if (mode !== 'record' && mode !== 'correct') {
  throw new Error('input.mode must be exactly "record" or "correct".');
}
if (!validCategories(ctx.input.categories)) {
  throw new Error('input.categories must be a canonical duplicate-free subset ordered simple, then martial. An empty array means known no category proficiency.');
}

var raw = subject.components['dnd2024.weapon-proficiencies'];
var previous = null;
if (raw) {
  try {
    previous = JSON.parse(raw);
  } catch (error) {
    throw new Error('The existing weapon-proficiencies component is corrupt and cannot be corrected by this rule. Use a governed migration.');
  }
  if (!validState(previous)) {
    throw new Error('The existing weapon-proficiencies component has an invalid shape and cannot be corrected by this rule. Use a governed migration.');
  }
}

if (mode === 'record' && raw) {
  throw new Error('Weapon-category proficiency is already recorded. Use mode "correct" to replace the complete known category set.');
}
if (mode === 'correct' && !raw) {
  throw new Error('Weapon-category proficiency is absent. Use mode "record" to establish whether the creature is proficient with neither, Simple, Martial, or both categories.');
}

var record = { categories: ctx.input.categories, sourceRef: sourceRef };
var effectType = mode === 'record' ? 'component.add' : 'component.set';
ctx.log(mode + ' weapon-category proficiencies [' + record.categories.join(', ') + '].');

return {
  narration: subject.name + "'s weapon-category proficiencies are " +
             (mode === 'record' ? 'recorded' : 'corrected') + ' as [' + record.categories.join(', ') + '].',
  effects: [{
    type: effectType,
    entityId: subject.id,
    definitionId: 'dnd2024.weapon-proficiencies',
    data: JSON.stringify(record)
  }],
  data: {
    mode: mode,
    categories: record.categories,
    previousCategories: previous === null ? null : previous.categories,
    sourceRef: sourceRef
  }
};
