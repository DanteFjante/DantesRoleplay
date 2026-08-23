var DEF = 'dnd2024.heroic-inspiration';
var PROFILE = 'dnd2024.character.profile';
var SID = 'source.dnd2024.srd-5.2.1';
var LOCATOR = 'Rules Glossary > Heroic Inspiration';
var subject = ctx.roles.subject;

function closed(value, keys) {
  if (!value || typeof value !== 'object' || Array.isArray(value)) { return false; }
  var actual = Object.keys(value).sort();
  if (actual.length !== keys.length) { return false; }
  for (var index = 0; index < keys.length; index++) {
    if (actual[index] !== keys[index]) { return false; }
  }
  return true;
}

function parse(raw, name) {
  try { return typeof raw === 'string' ? JSON.parse(raw) : raw; }
  catch (error) { throw new Error(name + ' is malformed.'); }
}

function validProfile(value) {
  var keys = ['appearance', 'biography', 'pronouns'];
  if (!value || typeof value !== 'object' || Array.isArray(value)) { return false; }
  var actual = Object.keys(value);
  for (var index = 0; index < actual.length; index++) {
    if (keys.indexOf(actual[index]) === -1) { return false; }
    var text = value[actual[index]];
    var maximum = actual[index] === 'pronouns' ? 80 : actual[index] === 'appearance' ? 1000 : 2000;
    if (typeof text !== 'string' || text.length < 1 || text.length > maximum || text.trim() !== text) { return false; }
  }
  return true;
}

if (!closed(ctx.input, [])) {
  throw new Error('Granting Heroic Inspiration requires exactly an empty object input.');
}
if (!subject || !subject.components) {
  throw new Error('A player-character subject is required.');
}
if (!Object.prototype.hasOwnProperty.call(subject.components, PROFILE)) {
  throw new Error('Heroic Inspiration can be granted only to an existing player character.');
}
if (!validProfile(parse(subject.components[PROFILE], 'Character profile'))) {
  throw new Error('The subject character profile is invalid.');
}
if (Object.prototype.hasOwnProperty.call(subject.components, DEF)) {
  if (!closed(parse(subject.components[DEF], 'Heroic Inspiration state'), [])) {
    throw new Error('The subject Heroic Inspiration state is invalid.');
  }
  throw new Error('The subject already has Heroic Inspiration.');
}

return {
  narration: subject.name + ' gains Heroic Inspiration.',
  effects: [{ type: 'component.add', entityId: subject.id, definitionId: DEF, data: '{}' }],
  data: {
    test: 'heroic-inspiration-grant',
    subjectId: subject.id,
    heldBefore: false,
    heldAfter: true,
    sourceRef: { sourceId: SID, locator: LOCATOR }
  }
};
