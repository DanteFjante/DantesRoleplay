// Effect-free armor-training diagnostics for D&D 2024 creatures.
// Governed by procedure.mechanic.dnd2024.armor-training.
var sourceId = 'source.dnd2024.srd-5.2.1';
var locator = 'Rules Glossary > Armor Class and Armor Training';
var subject = ctx.roles.subject;

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
function valid(value) {
  return hasOnly(value, ['categories', 'sourceRef']) && validCategories(value.categories) &&
         hasOnly(value.sourceRef, ['locator', 'sourceId']) &&
         value.sourceRef.sourceId === sourceId && value.sourceRef.locator === locator;
}
if (!subject) throw new Error('A subject role is required.');
if (!hasOnly(ctx.input, [])) throw new Error('Reading armor-training diagnostics requires exactly an empty object input.');
var raw = subject.components && subject.components['dnd2024.armor-training'];
var state;
if (!raw) return { narration: subject.name + ' has no recorded armor training.', data: { test: 'armor-training-read', subjectId: subject.id, present: false, valid: false, problem: 'absent', categories: null, sourceRef: null }, effects: [] };
try { state = typeof raw === 'string' ? JSON.parse(raw) : raw; }
catch (error) { return { narration: subject.name + ' has malformed armor training.', data: { test: 'armor-training-read', subjectId: subject.id, present: true, valid: false, problem: 'malformed', categories: null, sourceRef: null }, effects: [] }; }
if (!valid(state)) return { narration: subject.name + ' has invalid armor training.', data: { test: 'armor-training-read', subjectId: subject.id, present: true, valid: false, problem: 'invalid', categories: null, sourceRef: null }, effects: [] };
return { narration: subject.name + ' has valid armor training.', data: { test: 'armor-training-read', subjectId: subject.id, present: true, valid: true, problem: null, categories: state.categories, sourceRef: state.sourceRef }, effects: [] };
