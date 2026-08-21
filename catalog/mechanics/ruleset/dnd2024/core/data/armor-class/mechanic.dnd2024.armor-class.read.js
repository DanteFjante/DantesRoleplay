// Effect-free D&D 2024 Armor Class derivation.
// Governed by procedure.mechanic.dnd2024.armor-class.
var subject = ctx.roles.subject;
var SID = 'source.dnd2024.srd-5.2.1';
var LOC = 'Rules Glossary > Armor Class and Armor Training';
var keys = ['cha', 'con', 'dex', 'int', 'str', 'wis'];

function closed(value, expected) { if (value === null || Array.isArray(value) || typeof value !== 'object') return false; var actual = Object.keys(value).sort(); if (actual.length !== expected.length) return false; for (var i = 0; i < expected.length; i++) if (actual[i] !== expected[i]) return false; return true; }
function parse(raw, message) { try { var value = typeof raw === 'string' ? JSON.parse(raw) : raw; } catch (error) { throw new Error(message); } if (value === null || Array.isArray(value) || typeof value !== 'object') throw new Error(message); return value; }
function scores(value) { if (!closed(value, keys)) return false; for (var i = 0; i < keys.length; i++) if (typeof value[keys[i]] !== 'number' || !isFinite(value[keys[i]]) || Math.floor(value[keys[i]]) !== value[keys[i]] || value[keys[i]] < 1 || value[keys[i]] > 30) return false; return true; }
function source(value) { return closed(value, ['locator', 'sourceId']) && value.sourceId === SID && value.locator === 'Equipment > Armor'; }
function validTraining(value) { var order = ['light', 'medium', 'heavy', 'shield']; if (!closed(value, ['categories', 'sourceRef']) || !Array.isArray(value.categories) || !sourceRef(value.sourceRef)) return false; var previous = -1; for (var i = 0; i < value.categories.length; i++) { var index = order.indexOf(value.categories[i]); if (index <= previous) return false; previous = index; } return true; }
function sourceRef(value) { return closed(value, ['locator', 'sourceId']) && value.sourceId === SID && value.locator === LOC; }
function selection(value, kind) { if (value === null) return null; if (!closed(value, ['armorProfile', 'definitionId', 'itemId', 'sourceRef', 'state']) || typeof value.itemId !== 'string' || typeof value.definitionId !== 'string' || !source(value.sourceRef)) throw new Error('Armor equipment result has an invalid selection.'); if (kind === 'armor') { if (value.state !== 'worn' || !value.armorProfile || ['light', 'medium', 'heavy'].indexOf(value.armorProfile.category) < 0) throw new Error('Armor equipment result has an invalid armor selection.'); } else if (value.state !== 'held' || !value.armorProfile || value.armorProfile.category !== 'shield' || value.armorProfile.armorClassBonus !== 2) throw new Error('Armor equipment result has an invalid Shield selection.'); return value; }
function equipment() { var children = ctx.children && ctx.children.equipment; if (!Array.isArray(children) || children.length !== 1) throw new Error('Exactly one armor-equipment result is required.'); var output = children[0] && children[0].output; var value = parse(output && output.data ? output.data : null, 'Armor equipment result is unreadable.'); if (!closed(value, ['armor', 'shield', 'subjectId', 'test']) || value.test !== 'armor-equipment-read' || value.subjectId !== subject.id) throw new Error('Armor equipment result has an invalid shape.'); return { armor: selection(value.armor, 'armor'), shield: selection(value.shield, 'shield') }; }

if (!subject || !subject.components) throw new Error('A subject role is required.');
if (!ctx.input || !closed(ctx.input, [])) throw new Error('Reading derived Armor Class requires exactly an empty object input.');
var abilities = parse(subject.components['dnd2024.abilities'], 'Ability score state is invalid.');
if (!scores(abilities)) throw new Error('Ability score state is invalid.');
var selected = equipment();
var dexterityModifier = Math.floor((abilities.dex - 10) / 2);
var baseArmorClass = 10;
var baseKind = 'default';
var dexterityModifierApplied = dexterityModifier;
if (selected.armor !== null) {
  baseKind = selected.armor.armorProfile.category;
  baseArmorClass = selected.armor.armorProfile.baseArmorClass;
  dexterityModifierApplied = baseKind === 'light' ? dexterityModifier : baseKind === 'medium' ? Math.min(dexterityModifier, 2) : 0;
}
var shieldBonus = 0;
var shieldTraining = 'not-equipped';
if (selected.shield !== null) {
  var rawTraining = subject.components['dnd2024.armor-training'];
  if (!rawTraining) throw new Error('A held Shield requires explicit valid armor-training state to derive Armor Class.');
  var training = parse(rawTraining, 'Armor-training state is invalid.');
  if (!validTraining(training)) throw new Error('Armor-training state is invalid.');
  shieldTraining = training.categories.indexOf('shield') >= 0 ? 'trained' : 'untrained';
  if (shieldTraining === 'trained') shieldBonus = 2;
}
var armorClass = baseArmorClass + dexterityModifierApplied + shieldBonus;
return { narration: subject.name + ' has derived Armor Class ' + armorClass + '.', effects: [], data: { test: 'armor-class-read', subjectId: subject.id, armorClass: armorClass, base: { kind: baseKind, baseArmorClass: baseArmorClass, dexterityModifier: dexterityModifier, dexterityModifierApplied: dexterityModifierApplied, armor: selected.armor }, shield: { selection: selected.shield, training: shieldTraining, bonusApplied: shieldBonus }, sourceRef: { sourceId: SID, locator: LOC } } };
