// D&D 2024 confirmed weapon damage with no persisted outcome or Hit Point change.
// Governed by dnd2024.procedure.mechanic.weapon-damage.roll.
var input = ctx.input;
var subject = ctx.roles.subject;
var weapon = ctx.roles.weapon;
var abilityOrder = ['str', 'dex', 'con', 'int', 'wis', 'cha'];
var abilityKeys = ['cha', 'con', 'dex', 'int', 'str', 'wis'];
var sourceId = 'dnd2024.source.srd-5.2.1';
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

function validProfile(value) {
  if (!closed(value, ['attackAbilities', 'category', 'damage', 'kind', 'sourceRef']) ||
      (value.category !== 'simple' && value.category !== 'martial') ||
      (value.kind !== 'melee' && value.kind !== 'ranged') ||
      !sourceRef(value.sourceRef, profileLocator) || !Array.isArray(value.attackAbilities) ||
      value.attackAbilities.length < 1 || value.attackAbilities.length > 2) {
    return false;
  }
  var previous = -1;
  for (var i = 0; i < value.attackAbilities.length; i++) {
    var index = value.attackAbilities[i] === 'str' ? 0 : value.attackAbilities[i] === 'dex' ? 1 : -1;
    if (index <= previous) { return false; }
    previous = index;
  }
  var damage = value.damage;
  return closed(damage, ['count', 'faces', 'type']) &&
         typeof damage.count === 'number' && isFinite(damage.count) && Math.floor(damage.count) === damage.count && damage.count >= 1 && damage.count <= maxDiceRolled / 2 &&
         (damage.faces === 4 || damage.faces === 6 || damage.faces === 8 || damage.faces === 10 || damage.faces === 12) &&
         (damage.type === 'bludgeoning' || damage.type === 'piercing' || damage.type === 'slashing');
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
