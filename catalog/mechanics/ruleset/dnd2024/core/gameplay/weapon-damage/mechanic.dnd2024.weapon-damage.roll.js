// D&D 2024 confirmed weapon damage with no persisted outcome or Hit Point change.
// Governed by procedure.mechanic.dnd2024.weapon-damage.roll.
var input = ctx.input;
var subject = ctx.roles.subject;
var weapon = ctx.roles.weapon;
var abilityOrder = ['str', 'dex', 'con', 'int', 'wis', 'cha'];
var abilityKeys = ['cha', 'con', 'dex', 'int', 'str', 'wis'];
var sourceId = 'source.dnd2024.srd-5.2.1';
var profileLocator = 'Equipment > Weapons';
var damageLocator = 'Playing the Game > Damage and Healing > Damage Rolls';
var criticalLocator = 'Playing the Game > Damage and Healing > Critical Hits';
var maxDiceRolled = 100;

function closed(value, keys) {
  if (value === null || Array.isArray(value) || typeof value !== 'object') { return false; }
  var actual = Object.keys(value).sort();
  if (actual.length !== keys.length) { return false; }
  for (var i = 0; i < keys.length; i++) {
    if (actual[i] !== keys[i]) { return false; }
  }
  return true;
}

function sourceRef(value, locator) {
  return closed(value, ['locator', 'sourceId']) && value.sourceId === sourceId && value.locator === locator;
}

function parse(raw, name) {
  try { return JSON.parse(raw); }
  catch (error) { throw new Error(name + ' is corrupt. Use its governed recording path or migration.'); }
}

function validScores(value) {
  if (!closed(value, abilityKeys)) { return false; }
  for (var i = 0; i < abilityOrder.length; i++) {
    var score = value[abilityOrder[i]];
    if (typeof score !== 'number' || !isFinite(score) || Math.floor(score) !== score || score < 1 || score > 30) {
      return false;
    }
  }
  return true;
}

var propertyOrder = ['ammunition', 'finesse', 'heavy', 'light', 'loading', 'reach', 'thrown', 'two-handed', 'versatile'];
var masteries = ['cleave', 'graze', 'nick', 'push', 'sap', 'slow', 'topple', 'vex'];
function validRange(value) { return closed(value, ['long', 'normal']) && safePositive(value.normal) && safePositive(value.long) && value.normal % 5 === 0 && value.long % 5 === 0 && value.normal <= value.long; }
function safePositive(value) { return typeof value === 'number' && isFinite(value) && Math.floor(value) === value && value >= 1 && value <= 9007199254740991; }
function hasTag(tags, tag) { return tags.indexOf(tag) !== -1; }
function validTags(value) { var previous = -1; if (!Array.isArray(value) || value.length > propertyOrder.length) return false; for (var i = 0; i < value.length; i++) { var index = propertyOrder.indexOf(value[i]); if (index <= previous) return false; previous = index; } return true; }
function validDamage(value) { return closed(value, ['count', 'faces', 'type']) && safePositive(value.count) && value.count <= maxDiceRolled / 2 && (value.faces === 4 || value.faces === 6 || value.faces === 8 || value.faces === 10 || value.faces === 12) && (value.type === 'bludgeoning' || value.type === 'piercing' || value.type === 'slashing'); }
function validProfile(value) {
  if (!value || !validTags(value.propertyTags)) return false;
  var keys = ['attackAbilities', 'category', 'damage', 'kind', 'mastery', 'propertyTags', 'sourceRef'];
  if (value.kind === 'ranged') keys.push('rangeFeet');
  if (hasTag(value.propertyTags, 'ammunition')) keys.push('ammunitionType');
  if (hasTag(value.propertyTags, 'thrown')) keys.push('thrownRangeFeet');
  if (hasTag(value.propertyTags, 'versatile')) keys.push('versatileDamage');
  keys.sort();
  if (!closed(value, keys) || (value.category !== 'simple' && value.category !== 'martial') || (value.kind !== 'melee' && value.kind !== 'ranged') || !sourceRef(value.sourceRef, profileLocator) || !Array.isArray(value.attackAbilities) || value.attackAbilities.length < 1 || value.attackAbilities.length > 2) return false;
  var previous = -1;
  for (var i = 0; i < value.attackAbilities.length; i++) {
    var index = value.attackAbilities[i] === 'str' ? 0 : value.attackAbilities[i] === 'dex' ? 1 : -1;
    if (index <= previous) { return false; }
    previous = index;
  }
  return validDamage(value.damage) && (value.kind !== 'ranged' || validRange(value.rangeFeet)) &&
         (!hasTag(value.propertyTags, 'ammunition') || (value.kind === 'ranged' && ['arrow', 'bolt', 'bullet', 'needle'].indexOf(value.ammunitionType) !== -1)) &&
         (!hasTag(value.propertyTags, 'thrown') || validRange(value.thrownRangeFeet)) &&
         (!hasTag(value.propertyTags, 'versatile') || (validDamage(value.versatileDamage) && value.versatileDamage.type === value.damage.type && value.versatileDamage.count === value.damage.count && value.versatileDamage.faces > value.damage.faces)) &&
         masteries.indexOf(value.mastery) !== -1;
}

if (!closed(input, ['ability', 'critical'])) {
  throw new Error('Input must contain exactly ability and critical. Do not supply hit, AC, profile, modifiers, dice, damage, Hit Point, or effects fields.');
}
if (typeof input.ability !== 'string' || (input.ability !== 'str' && input.ability !== 'dex')) {
  throw new Error('input.ability must be exactly "str" or "dex" and permitted by the selected weapon profile.');
}
if (typeof input.critical !== 'boolean') {
  throw new Error('input.critical must be a Boolean copied only from a confirmed Feature 8 hit.');
}
if (!subject || !weapon || !subject.components || !weapon.components ||
    !subject.components['dnd2024.abilities'] || !weapon.components['dnd2024.weapon-profile']) {
  throw new Error('Confirmed weapon damage requires subject abilities and a canonical weapon profile.');
}

var abilities = parse(subject.components['dnd2024.abilities'], 'Subject ability state');
if (!validScores(abilities)) { throw new Error('Subject ability state is invalid.'); }
var profile = parse(weapon.components['dnd2024.weapon-profile'], 'Weapon profile state');
if (!validProfile(profile)) { throw new Error('Weapon profile state is invalid or exceeds this resolver\'s 100-die safety limit.'); }
if (profile.attackAbilities.indexOf(input.ability) === -1) {
  throw new Error('The selected ability is not permitted by this canonical weapon profile.');
}

var abilityModifier = Math.floor((abilities[input.ability] - 10) / 2);
var multiplier = input.critical ? 2 : 1;
var diceCount = profile.damage.count * multiplier;
var rolls = [];
var diceSubtotal = 0;
for (var rollIndex = 0; rollIndex < diceCount; rollIndex++) {
  var roll = ctx.randomInt(1, profile.damage.faces);
  rolls.push(roll);
  diceSubtotal += roll;
}
var damage = Math.max(0, diceSubtotal + abilityModifier);

ctx.log('Weapon damage: ' + diceCount + 'd' + profile.damage.faces + ' + ' + abilityModifier + ' = ' + damage + '.');
return {
  narration: subject.name + ' deals ' + damage + ' ' + profile.damage.type + ' damage with ' + weapon.name + '.',
  effects: [],
  data: {
    test: 'weapon-damage',
    subjectId: subject.id,
    weaponId: weapon.id,
    ability: input.ability,
    critical: input.critical,
    damageType: profile.damage.type,
    baseDiceCount: profile.damage.count,
    damageDieFaces: profile.damage.faces,
    damageDiceCount: diceCount,
    rolls: rolls,
    diceSubtotal: diceSubtotal,
    abilityModifier: abilityModifier,
    damage: damage,
    source: 'SRD 5.2.1 - Playing the Game > Damage and Healing > Damage Rolls; Playing the Game > Damage and Healing > Critical Hits; Equipment > Weapons'
  }
};
