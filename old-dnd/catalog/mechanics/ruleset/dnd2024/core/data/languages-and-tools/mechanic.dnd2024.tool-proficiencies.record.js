// Administrative tool proficiencies recording for D&D 2024 characters.
// Governed by procedure.mechanic.dnd2024.languages-and-tools.
var subject = ctx.roles.subject;
var sourceRef = {
  sourceId: 'source.dnd2024.srd-5.2.1',
  locator: 'Equipment > Tools > Tool Proficiency'
};
var vocabulary = ["alchemists-supplies","bagpipes","brewers-supplies","calligraphers-supplies","carpenters-tools","cartographers-tools","cobblers-tools","cooks-utensils","dice-set","disguise-kit","dragonchess-set","drum","dulcimer","flute","forgery-kit","glassblowers-tools","herbalism-kit","horn","jewelers-tools","leatherworkers-tools","lute","masons-tools","navigators-tools","painters-supplies","pan-flute","playing-cards","poisoners-kit","potters-tools","shawm","smiths-tools","thieves-tools","three-dragon-ante","tinkers-tools","viol","weavers-tools","woodcarvers-tools"];
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
  return hasOnly(value, ['tools', 'sourceRef']) &&
    validList(value.tools, true) &&
    hasOnly(value.sourceRef, ['locator', 'sourceId']) &&
    value.sourceRef.sourceId === sourceRef.sourceId &&
    value.sourceRef.locator === sourceRef.locator;
}

if (!hasOnly(ctx.input, ['tools'])) {
  throw new Error('Input must contain exactly {"tools":[<canonical SRD ids>]}. Do not supply sourceRef, grant provenance, class, background, species, item, ability, Proficiency Bonus, check result, Advantage, or effects.');
}
if (!validList(ctx.input.tools, false)) {
  throw new Error('input.tools must be a duplicate-free array of exact lowercase SRD tool proficiencies ids. An empty array means known none.');
}

var raw = subject.components['dnd2024.tool-proficiencies'];
var previous = null;
if (raw) {
  try {
    previous = JSON.parse(raw);
  } catch (error) {
    throw new Error('The existing tool proficiencies component is corrupt and cannot be corrected by this rule. Use a governed migration.');
  }
  if (!validState(previous)) {
    throw new Error('The existing tool proficiencies component has an invalid shape and cannot be corrected by this rule. Use a governed migration.');
  }
}

var canonical = ctx.input.tools.slice();
canonical.sort(function (left, right) { return indexes[left] - indexes[right]; });
var record = { tools: canonical, sourceRef: sourceRef };
var effectType = raw ? 'component.set' : 'component.add';

ctx.log((raw ? 'Replace' : 'Record') + ' tool proficiencies [' + canonical.join(', ') + '].');

return {
  narration: subject.name + "'s tool proficiencies are " + (raw ? 'replaced' : 'recorded') +
    ' as [' + canonical.join(', ') + '].',
  effects: [{
    type: effectType,
    entityId: subject.id,
    definitionId: 'dnd2024.tool-proficiencies',
    data: JSON.stringify(record)
  }],
  data: {
    tools: canonical,
    previousTools: previous === null ? null : previous.tools,
    sourceRef: sourceRef
  }
};
