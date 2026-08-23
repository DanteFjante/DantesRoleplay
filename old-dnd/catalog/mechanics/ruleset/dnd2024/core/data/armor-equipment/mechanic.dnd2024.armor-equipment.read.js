// Effect-free direct armor/Shield aggregation for D&D 2024 creatures.
// Governed by procedure.mechanic.dnd2024.armor-equipment.
var subject = ctx.roles.subject;
var SID = 'source.dnd2024.srd-5.2.1';
var LOC = 'Equipment > Armor';

function empty(value) { return value !== null && !Array.isArray(value) && typeof value === 'object' && Object.keys(value).length === 0; }
function closed(value, keys) {
  if (value === null || Array.isArray(value) || typeof value !== 'object') return false;
  var actual = Object.keys(value).sort();
  if (actual.length !== keys.length) return false;
  for (var i = 0; i < keys.length; i++) if (actual[i] !== keys[i]) return false;
  return true;
}
function parse(raw, message) {
  try { var value = typeof raw === 'string' ? JSON.parse(raw) : raw; }
  catch (error) { throw new Error(message); }
  if (value === null || Array.isArray(value) || typeof value !== 'object') throw new Error(message);
  return value;
}
function source(value) { return closed(value, ['locator', 'sourceId']) && value.sourceId === SID && value.locator === LOC; }
function armorProfile(value) {
  if (value === null || Array.isArray(value) || typeof value !== 'object') return false;
  if (value.category === 'light') return closed(value, ['baseArmorClass', 'category', 'dexterityRule', 'donDoff', 'stealthDisadvantage']) &&
    (value.baseArmorClass === 11 || value.baseArmorClass === 12) && value.dexterityRule === 'full' && typeof value.stealthDisadvantage === 'boolean' && closed(value.donDoff, ['doffMinutes', 'donMinutes']) && value.donDoff.donMinutes === 1 && value.donDoff.doffMinutes === 1;
  if (value.category === 'medium') return closed(value, ['baseArmorClass', 'category', 'dexterityRule', 'donDoff', 'stealthDisadvantage']) &&
    (value.baseArmorClass === 12 || value.baseArmorClass === 13 || value.baseArmorClass === 14 || value.baseArmorClass === 15) && value.dexterityRule === 'max-2' && typeof value.stealthDisadvantage === 'boolean' && closed(value.donDoff, ['doffMinutes', 'donMinutes']) && value.donDoff.donMinutes === 5 && value.donDoff.doffMinutes === 1;
  if (value.category === 'heavy') return closed(value, ['baseArmorClass', 'category', 'dexterityRule', 'donDoff', 'stealthDisadvantage', 'strengthMinimum']) &&
    (value.baseArmorClass === 14 || value.baseArmorClass === 16 || value.baseArmorClass === 17 || value.baseArmorClass === 18) && value.dexterityRule === 'none' && (value.strengthMinimum === 13 || value.strengthMinimum === 15) && typeof value.stealthDisadvantage === 'boolean' && closed(value.donDoff, ['doffMinutes', 'donMinutes']) && value.donDoff.donMinutes === 10 && value.donDoff.doffMinutes === 5;
  return false;
}
function shieldProfile(value) { return closed(value, ['armorClassBonus', 'category', 'donDoff']) && value.category === 'shield' && value.armorClassBonus === 2 && closed(value.donDoff, ['kind']) && value.donDoff.kind === 'utilize-action'; }
function definition(value) {
  if (!value || value.definitionVersion !== 1 || value.stackPolicy !== 'separate' || !source(value.sourceRef) || !Array.isArray(value.equipmentModes) || value.equipmentModes.length !== 1) return false;
  return value.kind === 'armor' ? value.equipmentModes[0] === 'worn' && armorProfile(value.armorProfile) : value.kind === 'shield' && value.equipmentModes[0] === 'held' && shieldProfile(value.armorProfile);
}
function selection(node, instance, definition, state) { return { itemId: node.id, definitionId: instance.definitionId, state: state, armorProfile: definition.armorProfile, sourceRef: definition.sourceRef }; }

if (!subject) throw new Error('A subject role is required.');
if (!empty(ctx.input)) throw new Error('Reading equipped armor and Shield requires exactly an empty object input.');
var armor = null;
var shield = null;
var contents = subject.contains || [];
for (var i = 0; i < contents.length; i++) {
  var node = contents[i];
  var rawInstance = node.components && node.components['dnd2024.item-instance'];
  if (!rawInstance) continue;
  var instance = parse(rawInstance, 'A direct physical item instance is invalid.');
  if (!closed(instance, ['definitionId']) || typeof instance.definitionId !== 'string' || instance.definitionId.length === 0) throw new Error('A direct physical item instance is invalid.');
  var referenced = ctx.references[instance.definitionId];
  if (!referenced || !referenced.components || !referenced.components['dnd2024.item-definition']) throw new Error('A direct physical item definition is unavailable.');
  var itemDefinition = parse(referenced.components['dnd2024.item-definition'], 'A direct physical item definition is invalid.');
  if (itemDefinition.kind !== 'armor' && itemDefinition.kind !== 'shield') continue;
  if (!definition(itemDefinition)) throw new Error('A direct armor or Shield definition is invalid.');
  if (node.components['dnd2024.item-quantity']) throw new Error('Direct armor and Shield items must be separate, not stacks.');
  var rawState = node.components['dnd2024.equipment-state'];
  if (!rawState) throw new Error('A direct armor or Shield item lacks explicit equipment state.');
  var state = parse(rawState, 'A direct armor or Shield equipment state is invalid.');
  if (!closed(state, ['state']) || ['held', 'worn', 'unequipped'].indexOf(state.state) < 0) throw new Error('A direct armor or Shield equipment state is invalid.');
  if (state.state === 'unequipped') continue;
  if (itemDefinition.kind === 'armor') {
    if (state.state !== 'worn') throw new Error('Direct armor must be worn or explicitly unequipped.');
    if (armor !== null) throw new Error('More than one direct worn armor suit is present.');
    armor = selection(node, instance, itemDefinition, state.state);
  } else {
    if (state.state !== 'held') throw new Error('A direct Shield must be held or explicitly unequipped.');
    if (shield !== null) throw new Error('More than one direct held Shield is present.');
    shield = selection(node, instance, itemDefinition, state.state);
  }
}
return { narration: subject.name + ' has ' + (armor ? 'a direct worn armor suit' : 'no direct worn armor suit') + ' and ' + (shield ? 'a direct held Shield' : 'no direct held Shield') + '.', data: { test: 'armor-equipment-read', subjectId: subject.id, armor: armor, shield: shield }, effects: [] };
