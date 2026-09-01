// Administrative total-level recording for D&D 2024 characters.
// Governed by dnd2024.procedure.mechanic.character-level.
var subject = ctx.roles.subject;
var keys = Object.keys(ctx.input).sort();

if (keys.length !== 1 || keys[0] !== 'level') {
  throw new Error(
    'Input must contain exactly one field: {"level": <integer 1-20>}. ' +
    'Do not supply proficiencyBonus or sourceRef; both are derived or fixed by this rule.');
}

var level = ctx.input.level;
if (typeof level !== 'number' || !isFinite(level) || Math.floor(level) !== level) {
  throw new Error('input.level must be a finite integer from 1 through 20; it is never rounded or parsed.');
}
if (level < 1 || level > 20) {
  throw new Error('input.level must be from 1 through 20. Received ' + level + '.');
}

var raw = subject.components['dnd2024.character-level'];
var previousLevel = null;
if (raw) {
  var previous = JSON.parse(raw);
  if (typeof previous.level === 'number') { previousLevel = previous.level; }
}

var sourceRef = {
  sourceId: 'dnd2024.source.srd-5.2.1',
  locator: 'Character Creation > Character Advancement'
};
var record = { level: level, sourceRef: sourceRef };
var proficiencyBonus = 2 + Math.floor((level - 1) / 4);
var effectType = raw ? 'component.set' : 'component.add';

ctx.log('Record level ' + level + '; derived Proficiency Bonus +' + proficiencyBonus + '.');

return {
  narration: subject.name + "'s total character level is recorded as " + level +
             ' (Proficiency Bonus +' + proficiencyBonus + ').',
  effects: [{
    type: effectType,
    entityId: subject.id,
    definitionId: 'dnd2024.character-level',
    data: JSON.stringify(record)
  }],
  data: {
    level: level,
    proficiencyBonus: proficiencyBonus,
    previousLevel: previousLevel,
    sourceRef: sourceRef
  }
};
