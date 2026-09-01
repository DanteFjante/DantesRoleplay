// Administrative skill-proficiency recording for D&D 2024 characters.
// Governed by dnd2024.procedure.mechanic.skill-proficiencies.
var subject = ctx.roles.subject;
var keys = Object.keys(ctx.input).sort();

if (keys.length !== 1 || keys[0] !== 'skills') {
  throw new Error(
    'Input must contain exactly one field: {"skills":[<stable skill ids>]}. ' +
    'Do not supply sourceRef, defaultAbilities, proficiencyBonus, Expertise, or acquisition data.');
}

var skills = ctx.input.skills;
if (!Array.isArray(skills)) {
  throw new Error('input.skills must be an array of exact lowercase kebab-case SRD skill ids.');
}

var DEFAULTS = {
  'acrobatics': 'dex',
  'animal-handling': 'wis',
  'arcana': 'int',
  'athletics': 'str',
  'deception': 'cha',
  'history': 'int',
  'insight': 'wis',
  'intimidation': 'cha',
  'investigation': 'int',
  'medicine': 'wis',
  'nature': 'int',
  'perception': 'wis',
  'performance': 'cha',
  'persuasion': 'cha',
  'religion': 'int',
  'sleight-of-hand': 'dex',
  'stealth': 'dex',
  'survival': 'wis'
};
var VALID_IDS = 'acrobatics, animal-handling, arcana, athletics, deception, history, insight, intimidation, investigation, medicine, nature, perception, performance, persuasion, religion, sleight-of-hand, stealth, survival';
var seen = {};
var canonical = [];

for (var i = 0; i < skills.length; i++) {
  var skill = skills[i];
  if (typeof skill !== 'string') {
    throw new Error('Every input.skills member must be a string containing an exact stable skill id.');
  }
  if (typeof DEFAULTS[skill] === 'undefined') {
    throw new Error('Unknown skill id "' + skill + '". Valid ids: ' + VALID_IDS + '.');
  }
  if (seen[skill] === true) {
    throw new Error('Duplicate skill id "' + skill + '" is not allowed.');
  }
  seen[skill] = true;
  canonical.push(skill);
}
canonical.sort();

var raw = subject.components['dnd2024.skill-proficiencies'];
var previousSkills = null;
if (raw) {
  var previous = JSON.parse(raw);
  if (Array.isArray(previous.skills)) { previousSkills = previous.skills; }
}

var sourceRef = {
  sourceId: 'dnd2024.source.srd-5.2.1',
  locator: 'Playing the Game > Proficiency > Skill Proficiencies and Skills'
};
var record = { skills: canonical, sourceRef: sourceRef };
var defaultAbilities = {};
for (var j = 0; j < canonical.length; j++) {
  defaultAbilities[canonical[j]] = DEFAULTS[canonical[j]];
}
var effectType = raw ? 'component.set' : 'component.add';

ctx.log('Record ' + canonical.length + ' canonical skill proficiencies.');

return {
  narration: subject.name + "'s skill proficiencies are recorded as [" + canonical.join(', ') + '].',
  effects: [{
    type: effectType,
    entityId: subject.id,
    definitionId: 'dnd2024.skill-proficiencies',
    data: JSON.stringify(record)
  }],
  data: {
    skills: canonical,
    previousSkills: previousSkills,
    defaultAbilities: defaultAbilities,
    sourceRef: sourceRef
  }
};
