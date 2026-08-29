// Administrative canonical weapon-profile recording for D&D 2024.
// Governed by procedure.mechanic.dnd2024.weapon-profile.
var weapon = ctx.roles.weapon;
var maxSafe = 9007199254740991;
var sourceRef = {
  sourceId: 'source.dnd2024.srd-5.2.1',
  locator: 'Equipment > Weapons'
};

function isSafePositiveInteger(value) {
  return typeof value === 'number' && isFinite(value) && Math.floor(value) === value &&
         value >= 1 && value <= maxSafe;
}

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

function validAbilities(value) {
  if (!Array.isArray(value) || value.length < 1 || value.length > 2) {
    return false;
  }

  var previous = -1;
  for (var i = 0; i < value.length; i++) {
    var index = value[i] === 'str' ? 0 : value[i] === 'dex' ? 1 : -1;
    if (index <= previous) {
      return false;
    }
    previous = index;
  }
  return true;
}

function validDamage(value) {
  return hasOnly(value, ['count', 'faces', 'type']) &&
         isSafePositiveInteger(value.count) &&
         (value.faces === 4 || value.faces === 6 || value.faces === 8 ||
          value.faces === 10 || value.faces === 12) &&
         (value.type === 'bludgeoning' || value.type === 'piercing' || value.type === 'slashing');
}

function validRange(value) {
  return hasOnly(value, ['long', 'normal']) &&
         isSafePositiveInteger(value.normal) && isSafePositiveInteger(value.long) &&
         value.normal % 5 === 0 && value.long % 5 === 0 && value.normal <= value.long;
}

var propertyOrder = ['ammunition', 'finesse', 'heavy', 'light', 'loading', 'reach', 'thrown', 'two-handed', 'versatile'];
var masteries = ['cleave', 'graze', 'nick', 'push', 'sap', 'slow', 'topple', 'vex'];

function hasTag(tags, tag) {
  return tags.indexOf(tag) !== -1;
}

function validTags(value) {
  if (!Array.isArray(value) || value.length > propertyOrder.length) return false;
  var previous = -1;
  for (var i = 0; i < value.length; i++) {
    var index = propertyOrder.indexOf(value[i]);
    if (index <= previous) return false;
    previous = index;
  }
  return true;
}

function validProfile(value) {
  if (!value || !validTags(value.propertyTags)) return false;
  var keys = ['attackAbilities', 'category', 'damage', 'kind', 'mastery', 'propertyTags', 'sourceRef'];
  if (value.kind === 'ranged') keys.push('rangeFeet');
  if (hasTag(value.propertyTags, 'ammunition')) keys.push('ammunitionType');
  if (hasTag(value.propertyTags, 'thrown')) keys.push('thrownRangeFeet');
  if (hasTag(value.propertyTags, 'versatile')) keys.push('versatileDamage');
  keys.sort();
  return hasOnly(value, keys) &&
         (value.category === 'simple' || value.category === 'martial') &&
         (value.kind === 'melee' || value.kind === 'ranged') &&
         validAbilities(value.attackAbilities) && validDamage(value.damage) &&
         (value.kind !== 'ranged' || validRange(value.rangeFeet)) &&
         (!hasTag(value.propertyTags, 'ammunition') ||
           (value.kind === 'ranged' && ['arrow', 'bolt', 'bullet', 'needle'].indexOf(value.ammunitionType) !== -1)) &&
         (!hasTag(value.propertyTags, 'thrown') || validRange(value.thrownRangeFeet)) &&
         (!hasTag(value.propertyTags, 'versatile') ||
           (validDamage(value.versatileDamage) && value.versatileDamage.type === value.damage.type &&
             value.versatileDamage.count === value.damage.count && value.versatileDamage.faces > value.damage.faces)) &&
         masteries.indexOf(value.mastery) !== -1 &&
         hasOnly(value.sourceRef, ['locator', 'sourceId']) &&
         value.sourceRef.sourceId === sourceRef.sourceId &&
         value.sourceRef.locator === sourceRef.locator;
}

if (!ctx.input || typeof ctx.input !== 'object' || Array.isArray(ctx.input)) {
  throw new Error('Input must be one closed canonical weapon-profile object.');
}

var mode = ctx.input.mode;
if (mode !== 'record' && mode !== 'correct') {
  throw new Error('input.mode must be exactly "record" or "correct".');
}

var expectedInputKeys = ['attackAbilities', 'category', 'damage', 'kind', 'mastery', 'mode', 'propertyTags'];
if (ctx.input.kind === 'ranged') expectedInputKeys.push('rangeFeet');
if (hasTag(ctx.input.propertyTags || [], 'ammunition')) expectedInputKeys.push('ammunitionType');
if (hasTag(ctx.input.propertyTags || [], 'thrown')) expectedInputKeys.push('thrownRangeFeet');
if (hasTag(ctx.input.propertyTags || [], 'versatile')) expectedInputKeys.push('versatileDamage');
expectedInputKeys.sort();
if (!hasOnly(ctx.input, expectedInputKeys)) {
  throw new Error('Input must contain canonical profile facts, ordered propertyTags, one mastery, and only the structured fields required by its tags. Do not supply sourceRef, equipment, attack results, or effects.');
}

var candidate = {
  category: ctx.input.category,
  kind: ctx.input.kind,
  attackAbilities: ctx.input.attackAbilities,
  damage: ctx.input.damage,
  propertyTags: ctx.input.propertyTags,
  mastery: ctx.input.mastery,
  sourceRef: sourceRef
};
if (ctx.input.kind === 'ranged') candidate.rangeFeet = ctx.input.rangeFeet;
if (hasTag(ctx.input.propertyTags || [], 'ammunition')) candidate.ammunitionType = ctx.input.ammunitionType;
if (hasTag(ctx.input.propertyTags || [], 'thrown')) candidate.thrownRangeFeet = ctx.input.thrownRangeFeet;
if (hasTag(ctx.input.propertyTags || [], 'versatile')) candidate.versatileDamage = ctx.input.versatileDamage;

if (!validProfile(candidate)) {
  throw new Error('A weapon profile needs canonical category, kind, abilities, damage, ordered property tags, one mastery, and exactly the range/ammunition/versatile fields its kind and tags require.');
}

var raw = weapon.components['dnd2024.weapon-profile'];
var previous = null;
if (raw) {
  try {
    previous = JSON.parse(raw);
  } catch (error) {
    throw new Error('The existing weapon profile is corrupt and cannot be corrected by this rule. Use a governed migration.');
  }
  if (!validProfile(previous)) {
    throw new Error('The existing weapon profile has an invalid shape and cannot be corrected by this rule. Use a governed migration.');
  }
}

if (mode === 'record' && raw) {
  throw new Error('A weapon profile is already recorded. Use mode "correct" to replace its canonical static facts.');
}
if (mode === 'correct' && !raw) {
  throw new Error('A weapon profile is absent. Use mode "record" to create it.');
}

var effectType = mode === 'record' ? 'component.add' : 'component.set';
ctx.log(mode + ' weapon profile ' + candidate.category + ' ' + candidate.kind + ' ' +
        candidate.damage.count + 'd' + candidate.damage.faces + ' ' + candidate.damage.type + '.');

return {
  narration: weapon.name + "'s canonical weapon profile is " + (mode === 'record' ? 'recorded' : 'corrected') + '.',
  effects: [{
    type: effectType,
    entityId: weapon.id,
    definitionId: 'dnd2024.weapon-profile',
    data: JSON.stringify(candidate)
  }],
  data: {
    mode: mode,
    category: candidate.category,
    kind: candidate.kind,
    attackAbilities: candidate.attackAbilities,
    damage: candidate.damage,
    rangeFeet: candidate.kind === 'ranged' ? candidate.rangeFeet : null,
    propertyTags: candidate.propertyTags,
    ammunitionType: candidate.ammunitionType || null,
    thrownRangeFeet: candidate.thrownRangeFeet || null,
    versatileDamage: candidate.versatileDamage || null,
    mastery: candidate.mastery,
    previous: previous,
    sourceRef: sourceRef
  }
};
