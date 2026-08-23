// D&D 2024 transactional application of composed weapon damage and mitigation evidence.
// Governed by procedure.mechanic.dnd2024.weapon-damage.apply and procedure.mechanic.dnd2024.damage-mitigation.
var input = ctx.input;
var subject = ctx.roles.subject;
var target = ctx.roles.target;
var weapon = ctx.roles.weapon;
var sourceId = 'source.dnd2024.srd-5.2.1';
var hitPointLocator = 'Playing the Game > Damage and Healing > Hit Points';
var temporaryLocator = 'Playing the Game > Damage and Healing > Temporary Hit Points';
var mitigationLocator = 'Playing the Game > Damage and Healing';
var temporaryDefinition = 'dnd2024.temporary-hit-points';
var damageSource = 'SRD 5.2.1 - Playing the Game > Damage and Healing > Damage Rolls; Playing the Game > Damage and Healing > Critical Hits; Equipment > Weapons';
var maxSafe = 9007199254740991;
var damageTypes = ['acid', 'bludgeoning', 'cold', 'fire', 'force', 'lightning', 'necrotic', 'piercing', 'poison', 'psychic', 'radiant', 'slashing', 'thunder'];

function closed(value, keys) {
  if (value === null || Array.isArray(value) || typeof value !== 'object') { return false; }
  var actual = Object.keys(value).sort();
  if (actual.length !== keys.length) { return false; }
  for (var i = 0; i < keys.length; i++) { if (actual[i] !== keys[i]) { return false; } }
  return true;
}
function sourceRef(value, locator) { return closed(value, ['locator', 'sourceId']) && value.sourceId === sourceId && value.locator === locator; }
function parse(raw, name) { try { return JSON.parse(raw); } catch (error) { throw new Error(name + ' is corrupt. Use its governed recording path or migration.'); } }
function safeInteger(value, minimum) { return typeof value === 'number' && isFinite(value) && Math.floor(value) === value && value >= minimum && value <= maxSafe; }
function includes(values, value) { return Array.isArray(values) && values.indexOf(value) >= 0; }
function validHitPoints(value) { return closed(value, ['current', 'maximum', 'sourceRef']) && safeInteger(value.current, 0) && safeInteger(value.maximum, 1) && value.current <= value.maximum && sourceRef(value.sourceRef, hitPointLocator); }
function validTemporaryHitPoints(value) { return closed(value, ['amount', 'sourceRef']) && safeInteger(value.amount, 1) && sourceRef(value.sourceRef, temporaryLocator); }
function orderedTypes(values) {
  if (!Array.isArray(values) || values.length > damageTypes.length) { return false; }
  var previous = -1;
  for (var index = 0; index < values.length; index++) { var current = damageTypes.indexOf(values[index]); if (current <= previous) { return false; } previous = current; }
  return true;
}

if (!closed(input, ['ability', 'critical'])) { throw new Error('Input must contain exactly ability and critical. Do not supply Hit Points, damage, dice, target delta, or effects fields.'); }
if (typeof input.ability !== 'string' || (input.ability !== 'str' && input.ability !== 'dex') || typeof input.critical !== 'boolean') { throw new Error('input must contain permitted str|dex ability and Boolean critical confirmation.'); }
if (!subject || !target || !weapon || !target.components || !target.components['dnd2024.hit-points']) { throw new Error('Applying confirmed weapon damage requires subject, target Hit Points, and weapon roles.'); }
var before = parse(target.components['dnd2024.hit-points'], 'Target Hit Point state');
if (!validHitPoints(before)) { throw new Error('Target Hit Point state is invalid.'); }
var temporary = null;
if (Object.prototype.hasOwnProperty.call(target.components, temporaryDefinition)) {
  temporary = parse(target.components[temporaryDefinition], 'Target Temporary Hit Point state');
  if (!validTemporaryHitPoints(temporary)) { throw new Error('Target Temporary Hit Point state is invalid.'); }
}

if (!ctx.children || !ctx.children.damage || ctx.children.damage.length !== 1 || !ctx.children.mitigation || ctx.children.mitigation.length !== 1) { throw new Error('Exactly one composed damage and mitigation child result is required.'); }
var child = ctx.children.damage[0];
if (!child || child.mechanicId !== 'mechanic.dnd2024.weapon-damage.roll' || !child.roleEntityIds || child.roleEntityIds.subject !== subject.id || child.roleEntityIds.weapon !== weapon.id || !child.output) { throw new Error('The composed weapon-damage child does not match this action roles.'); }
var damage = parse(child.output.data, 'Composed weapon-damage result');
if (!closed(damage, ['ability', 'abilityModifier', 'baseDiceCount', 'critical', 'damage', 'damageDiceCount', 'damageDieFaces', 'damageType', 'diceSubtotal', 'rolls', 'source', 'subjectId', 'test', 'weaponId']) || damage.test !== 'weapon-damage' || damage.subjectId !== subject.id || damage.weaponId !== weapon.id || damage.ability !== input.ability || damage.critical !== input.critical || damage.source !== damageSource || !safeInteger(damage.baseDiceCount, 1) || !safeInteger(damage.damageDiceCount, 1) || damage.damageDiceCount !== damage.baseDiceCount * (damage.critical ? 2 : 1) || (damage.damageDieFaces !== 4 && damage.damageDieFaces !== 6 && damage.damageDieFaces !== 8 && damage.damageDieFaces !== 10 && damage.damageDieFaces !== 12) || (damage.damageType !== 'bludgeoning' && damage.damageType !== 'piercing' && damage.damageType !== 'slashing') || !Array.isArray(damage.rolls) || damage.rolls.length !== damage.damageDiceCount || !safeInteger(damage.diceSubtotal, 0) || !safeInteger(damage.damage, 0) || typeof damage.abilityModifier !== 'number' || !isFinite(damage.abilityModifier) || Math.floor(damage.abilityModifier) !== damage.abilityModifier) { throw new Error('The composed weapon-damage child result is invalid.'); }
var subtotal = 0;
for (var dieIndex = 0; dieIndex < damage.rolls.length; dieIndex++) { var die = damage.rolls[dieIndex]; if (!safeInteger(die, 1) || die > damage.damageDieFaces) { throw new Error('The composed weapon-damage child contains an invalid die.'); } subtotal += die; }
if (subtotal !== damage.diceSubtotal || damage.damage !== Math.max(0, subtotal + damage.abilityModifier)) { throw new Error('The composed weapon-damage child arithmetic is invalid.'); }

var mitigationChild = ctx.children.mitigation[0];
if (!mitigationChild || mitigationChild.mechanicId !== 'mechanic.dnd2024.damage.resolve' || !mitigationChild.roleEntityIds || mitigationChild.roleEntityIds.defender !== target.id || !mitigationChild.output) { throw new Error('The composed mitigation child does not match this action target.'); }
var profile = parse(mitigationChild.output.data, 'Composed mitigation profile');
if (!closed(profile, ['conditionsKnown', 'defenderId', 'immunities', 'mitigationKnown', 'petrified', 'resistances', 'sourceRef', 'test', 'vulnerabilities']) || profile.test !== 'damage-mitigation-profile' || profile.defenderId !== target.id || typeof profile.mitigationKnown !== 'boolean' || typeof profile.conditionsKnown !== 'boolean' || typeof profile.petrified !== 'boolean' || !orderedTypes(profile.immunities) || !orderedTypes(profile.resistances) || !orderedTypes(profile.vulnerabilities) || !sourceRef(profile.sourceRef, mitigationLocator)) { throw new Error('The composed mitigation child result is invalid.'); }

var rawAmount = damage.damage;
var immune = includes(profile.immunities, damage.damageType);
var resistanceReasons = [];
if (includes(profile.resistances, damage.damageType)) { resistanceReasons.push({ effect: 'resistance', reason: 'component' }); }
if (profile.petrified) { resistanceReasons.push({ effect: 'resistance', reason: 'condition:petrified' }); }
var resistanceApplied = !immune && resistanceReasons.length > 0;
var vulnerabilityApplied = !immune && includes(profile.vulnerabilities, damage.damageType);
var finalAmount = immune ? 0 : rawAmount;
if (resistanceApplied) { finalAmount = Math.floor(finalAmount / 2); }
if (vulnerabilityApplied) { if (finalAmount > Math.floor(maxSafe / 2)) { throw new Error('Vulnerability would make final damage exceed the safe integer limit.'); } finalAmount *= 2; }
var temporaryBefore = temporary ? temporary.amount : 0;
var temporaryAbsorbed = Math.min(temporaryBefore, finalAmount);
var temporaryAfter = temporaryBefore - temporaryAbsorbed;
var toHitPoints = finalAmount - temporaryAbsorbed;
var afterCurrent = Math.max(0, before.current - toHitPoints);
var overkill = Math.max(0, toHitPoints - before.current);
var after = { current: afterCurrent, maximum: before.maximum, sourceRef: before.sourceRef };
var mitigation = { rawAmount: rawAmount, type: damage.damageType, immune: immune, resistanceApplied: resistanceApplied, vulnerabilityApplied: vulnerabilityApplied, reasons: resistanceReasons, finalAmount: finalAmount };
var effects = [];
if (temporaryAbsorbed > 0) {
  if (temporaryAfter === 0) {
    effects.push({ type: 'component.remove', entityId: target.id, definitionId: temporaryDefinition });
  } else {
    effects.push({ type: 'component.set', entityId: target.id, definitionId: temporaryDefinition, data: JSON.stringify({ amount: temporaryAfter, sourceRef: temporary.sourceRef }) });
  }
}
effects.push({ type: 'component.set', entityId: target.id, definitionId: 'dnd2024.hit-points', data: JSON.stringify(after) });
ctx.log('Applied ' + finalAmount + ' ' + damage.damageType + ' damage to ' + target.name + ' (' + temporaryAbsorbed + ' absorbed by Temporary Hit Points): ' + before.current + ' -> ' + afterCurrent + '.');
return {
  narration: target.name + ' takes ' + finalAmount + ' ' + damage.damageType + ' damage' + (temporaryAbsorbed > 0 ? ' (' + temporaryAbsorbed + ' absorbed by Temporary Hit Points)' : '') + ': ' + before.current + ' to ' + afterCurrent + ' Hit Points.',
  effects: effects,
  events: [{ type: 'dnd2024.damage.dealt', payload: { targetId: target.id, sourceId: subject.id, rawAmount: rawAmount, type: damage.damageType, finalAmount: finalAmount, immune: immune, resistanceApplied: resistanceApplied, vulnerabilityApplied: vulnerabilityApplied, temporaryBefore: temporaryBefore, temporaryAfter: temporaryAfter, temporaryAbsorbed: temporaryAbsorbed, beforeCurrent: before.current, afterCurrent: afterCurrent, maximum: before.maximum, overkill: overkill, critical: damage.critical, sourceRef: { sourceId: sourceId, locator: mitigationLocator } }, entityIds: [target.id] }],
  data: { test: 'weapon-damage-application', childMechanicId: child.mechanicId, childMechanicVersion: child.mechanicVersion, childSeed: child.seed, mitigationChildMechanicId: mitigationChild.mechanicId, mitigationChildMechanicVersion: mitigationChild.mechanicVersion, mitigationChildSeed: mitigationChild.seed, subjectId: subject.id, targetId: target.id, weaponId: weapon.id, ability: damage.ability, critical: damage.critical, damageType: damage.damageType, damage: finalAmount, rawAmount: rawAmount, finalAmount: finalAmount, mitigation: mitigation, temporaryBefore: temporaryBefore, temporaryAfter: temporaryAfter, temporaryAbsorbed: temporaryAbsorbed, toHitPoints: toHitPoints, beforeCurrent: before.current, afterCurrent: afterCurrent, maximum: before.maximum, overkill: overkill, source: 'SRD 5.2.1 - Playing the Game > Damage and Healing > Hit Points; Playing the Game > Damage and Healing > Temporary Hit Points; Playing the Game > Damage and Healing > Damage Rolls; Playing the Game > Damage and Healing > Critical Hits; Playing the Game > Damage and Healing > Resistance and Vulnerability; Playing the Game > Damage and Healing > Immunity' }
};
