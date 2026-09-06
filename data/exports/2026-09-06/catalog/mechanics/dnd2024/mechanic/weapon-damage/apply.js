// D&D 2024 transactional application of one composed confirmed weapon-damage result.
// Governed by dnd2024.procedure.mechanic.weapon-damage.apply.
var input = ctx.input;
var subject = ctx.roles.subject;
var target = ctx.roles.target;
var weapon = ctx.roles.weapon;
var sourceId = 'dnd2024.source.srd-5.2.1';
var hitPointLocator = 'Playing the Game > Damage and Healing > Hit Points';
var damageSource = 'SRD 5.2.1 - Playing the Game > Damage and Healing > Damage Rolls; Playing the Game > Damage and Healing > Critical Hits; Equipment > Weapons';

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

function safeInteger(value, minimum) {
  return typeof value === 'number' && isFinite(value) && Math.floor(value) === value && value >= minimum && value <= 9007199254740991;
}

function validHitPoints(value) {
  return closed(value, ['current', 'maximum', 'sourceRef']) &&
         safeInteger(value.current, 0) && safeInteger(value.maximum, 1) && value.current <= value.maximum &&
         sourceRef(value.sourceRef, hitPointLocator);
}

if (!closed(input, ['ability', 'critical'])) {
  throw new Error('Input must contain exactly ability and critical. Do not supply Hit Points, damage, dice, target delta, or effects fields.');
}
if (typeof input.ability !== 'string' || (input.ability !== 'str' && input.ability !== 'dex') || typeof input.critical !== 'boolean') {
  throw new Error('input must contain permitted str|dex ability and Boolean critical confirmation.');
}
if (!subject || !target || !weapon || !target.components || !target.components['dnd2024.hit-points']) {
  throw new Error('Applying confirmed weapon damage requires subject, target Hit Points, and weapon roles.');
}
var before = parse(target.components['dnd2024.hit-points'], 'Target Hit Point state');
if (!validHitPoints(before)) { throw new Error('Target Hit Point state is invalid.'); }

if (!ctx.children || !ctx.children.damage || ctx.children.damage.length !== 1) {
  throw new Error('Exactly one composed weapon-damage child result is required.');
}
var child = ctx.children.damage[0];
if (!child || child.mechanicId !== 'dnd2024.mechanic.weapon-damage.roll' || !child.roleEntityIds ||
    child.roleEntityIds.subject !== subject.id || child.roleEntityIds.weapon !== weapon.id || !child.output) {
  throw new Error('The composed weapon-damage child does not match this action roles.');
}
var damage = parse(child.output.data, 'Composed weapon-damage result');
if (!closed(damage, ['ability', 'abilityModifier', 'baseDiceCount', 'critical', 'damage', 'damageDiceCount', 'damageDieFaces', 'damageType', 'diceSubtotal', 'rolls', 'source', 'subjectId', 'test', 'weaponId']) ||
    damage.test !== 'weapon-damage' || damage.subjectId !== subject.id || damage.weaponId !== weapon.id ||
    damage.ability !== input.ability || damage.critical !== input.critical || damage.source !== damageSource ||
    !safeInteger(damage.baseDiceCount, 1) || !safeInteger(damage.damageDiceCount, 1) ||
    damage.damageDiceCount !== damage.baseDiceCount * (damage.critical ? 2 : 1) ||
    (damage.damageDieFaces !== 4 && damage.damageDieFaces !== 6 && damage.damageDieFaces !== 8 && damage.damageDieFaces !== 10 && damage.damageDieFaces !== 12) ||
    (damage.damageType !== 'bludgeoning' && damage.damageType !== 'piercing' && damage.damageType !== 'slashing') ||
    !Array.isArray(damage.rolls) || damage.rolls.length !== damage.damageDiceCount ||
    !safeInteger(damage.diceSubtotal, 0) || !safeInteger(damage.damage, 0) ||
    typeof damage.abilityModifier !== 'number' || !isFinite(damage.abilityModifier) || Math.floor(damage.abilityModifier) !== damage.abilityModifier) {
  throw new Error('The composed weapon-damage child result is invalid.');
}
var subtotal = 0;
for (var index = 0; index < damage.rolls.length; index++) {
  var die = damage.rolls[index];
  if (!safeInteger(die, 1) || die > damage.damageDieFaces) {
    throw new Error('The composed weapon-damage child contains an invalid die.');
  }
  subtotal += die;
}
if (subtotal !== damage.diceSubtotal || damage.damage !== Math.max(0, subtotal + damage.abilityModifier)) {
  throw new Error('The composed weapon-damage child arithmetic is invalid.');
}

var afterCurrent = Math.max(0, before.current - damage.damage);
var after = { current: afterCurrent, maximum: before.maximum, sourceRef: before.sourceRef };
ctx.log('Applied ' + damage.damage + ' ' + damage.damageType + ' damage to ' + target.name + ': ' + before.current + ' -> ' + afterCurrent + '.');
return {
  narration: target.name + ' takes ' + damage.damage + ' ' + damage.damageType + ' damage: ' + before.current + ' to ' + afterCurrent + ' Hit Points.',
  effects: [{ type: 'component.set', entityId: target.id, definitionId: 'dnd2024.hit-points', data: JSON.stringify(after) }],
  data: {
    test: 'weapon-damage-application',
    childMechanicId: child.mechanicId,
    childMechanicVersion: child.mechanicVersion,
    childSeed: child.seed,
    subjectId: subject.id,
    targetId: target.id,
    weaponId: weapon.id,
    ability: damage.ability,
    critical: damage.critical,
    damageType: damage.damageType,
    damage: damage.damage,
    beforeCurrent: before.current,
    afterCurrent: afterCurrent,
    maximum: before.maximum,
    source: 'SRD 5.2.1 - Playing the Game > Damage and Healing > Hit Points; Playing the Game > Damage and Healing > Damage Rolls; Playing the Game > Damage and Healing > Critical Hits'
  }
};
