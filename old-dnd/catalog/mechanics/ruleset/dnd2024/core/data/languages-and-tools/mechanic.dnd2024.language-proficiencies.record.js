// Administrative language proficiencies recording for D&D 2024 characters.
// Governed by procedure.mechanic.dnd2024.languages-and-tools.
var subject = ctx.roles.subject;
var sourceRef = {
  sourceId: 'source.dnd2024.srd-5.2.1',
  locator: 'Character Creation > Step 2: Character Origin > Choose Languages'
};
var vocabulary = ["abyssal","celestial","common","common-sign-language","deep-speech","draconic","druidic","dwarvish","elvish","giant","gnomish","goblin","halfling","infernal","orc","primordial","sylvan","thieves-cant","undercommon"];
var indexes = {};
for (var i = 0; i < vocabulary.length; i++) { indexes[vocabulary[i]] = i; }

function hasOnly(value, keys) {
  if (value === null || Array.isArray(value) || typeof value !== 'object') { return false; }
  var actual = Object.keys(value).sort();
  if (actual.length !== keys.length) { return false; }
  for (var i = 0; i < keys.length; i++) {
    if (actual[i] !== keys[i]) { return false; }
  }
  return true;
}

function validList(value, canonical) {
  if (!Array.isArray(value) || value.length > vocabulary.length) { return false; }
  var previous = -1;
  var seen = {};
  for (var i = 0; i < value.length; i++) {
    var member = value[i];
    if (typeof member !== 'string' || typeof indexes[member] === 'undefined' || seen[member]) {
      return false;
    }
    if (canonical && indexes[member] <= previous) { return false; }
    seen[member] = true;
    previous = indexes[member];
  }
  return true;
}

function validState(value) {
  return hasOnly(value, ['languages', 'sourceRef']) &&
    validList(value.languages, true) &&
    hasOnly(value.sourceRef, ['locator', 'sourceId']) &&
    value.sourceRef.sourceId === sourceRef.sourceId &&
    value.sourceRef.locator === sourceRef.locator;
}

if (!hasOnly(ctx.input, ['languages'])) {
  throw new Error('Input must contain exactly {"languages":[<canonical SRD ids>]}. Do not supply sourceRef, grant provenance, class, background, species, item, ability, Proficiency Bonus, check result, Advantage, or effects.');
}
if (!validList(ctx.input.languages, false)) {
  throw new Error('input.languages must be a duplicate-free array of exact lowercase SRD language proficiencies ids. An empty array means known none.');
}

var raw = subject.components['dnd2024.language-proficiencies'];
var previous = null;
if (raw) {
  try {
    previous = JSON.parse(raw);
  } catch (error) {
    throw new Error('The existing language proficiencies component is corrupt and cannot be corrected by this rule. Use a governed migration.');
  }
  if (!validState(previous)) {
    throw new Error('The existing language proficiencies component has an invalid shape and cannot be corrected by this rule. Use a governed migration.');
  }
}

var canonical = ctx.input.languages.slice();
canonical.sort(function (left, right) { return indexes[left] - indexes[right]; });
var record = { languages: canonical, sourceRef: sourceRef };
var effectType = raw ? 'component.set' : 'component.add';

ctx.log((raw ? 'Replace' : 'Record') + ' language proficiencies [' + canonical.join(', ') + '].');

return {
  narration: subject.name + "'s language proficiencies are " + (raw ? 'replaced' : 'recorded') +
    ' as [' + canonical.join(', ') + '].',
  effects: [{
    type: effectType,
    entityId: subject.id,
    definitionId: 'dnd2024.language-proficiencies',
    data: JSON.stringify(record)
  }],
  data: {
    languages: canonical,
    previousLanguages: previous === null ? null : previous.languages,
    sourceRef: sourceRef
  }
};
