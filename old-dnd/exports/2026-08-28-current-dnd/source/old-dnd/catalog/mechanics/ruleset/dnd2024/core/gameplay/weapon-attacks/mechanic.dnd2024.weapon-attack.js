// D&D 2024 weapon attack resolution with no persisted outcome or damage.
// Governed by procedure.mechanic.dnd2024.weapon-attack.
var input = ctx.input;
var subject = ctx.roles.subject;
var target = ctx.roles.target;
var weapon = ctx.roles.weapon;
var abilityOrder = ['str', 'dex', 'con', 'int', 'wis', 'cha'];
var abilityKeys = ['cha', 'con', 'dex', 'int', 'str', 'wis'];
var sourceId = 'source.dnd2024.srd-5.2.1';
var attackLocator = 'Playing the Game > D20 Tests > Attack Rolls';
var levelLocator = 'Character Creation > Character Advancement';
var proficiencyLocator = 'Equipment > Weapons > Weapon Proficiency';
var profileLocator = 'Equipment > Weapons';

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

function readArmorClass(child) {
  var value = parse(child && child.output && child.output.data ? child.output.data : null, 'Derived target Armor Class result');
  if (!closed(value, ['armorClass', 'base', 'shield', 'sourceRef', 'subjectId', 'test']) ||
      value.test !== 'armor-class-read' || value.subjectId !== target.id ||
      typeof value.armorClass !== 'number' || !isFinite(value.armorClass) ||
      Math.floor(value.armorClass) !== value.armorClass || value.armorClass < 1 ||
      value.armorClass > 9007199254740991) {
    throw new Error('Derived target Armor Class result is invalid.');
  }
  return value.armorClass;
}

function validScores(value) {
  if (!closed(value, abilityKeys)) { return false; }
  for (var i = 0; i < abilityOrder.length; i++) {
    var score = value[abilityOrder[i]];
    if (typeof score !== 'number' || !isFinite(score) || Math.floor(score) !== score || score < 1 || score > 30) {
      return false;
    }
  }
  return true;
}

function validCategories(value) {
  if (!Array.isArray(value) || value.length > 2) { return false; }
  var previous = -1;
  for (var i = 0; i < value.length; i++) {
    var index = value[i] === 'simple' ? 0 : value[i] === 'martial' ? 1 : -1;
    if (index <= previous) { return false; }
    previous = index;
  }
  return true;
}

var propertyOrder = ['ammunition', 'finesse', 'heavy', 'light', 'loading', 'reach', 'thrown', 'two-handed', 'versatile'];
var masteries = ['cleave', 'graze', 'nick', 'push', 'sap', 'slow', 'topple', 'vex'];
function safePositive(value) { return typeof value === 'number' && isFinite(value) && Math.floor(value) === value && value >= 1 && value <= 9007199254740991; }
function validRange(value) { return closed(value, ['long', 'normal']) && safePositive(value.normal) && safePositive(value.long) && value.normal % 5 === 0 && value.long % 5 === 0 && value.normal <= value.long; }
function hasTag(tags, tag) { return tags.indexOf(tag) !== -1; }
function validTags(value) { var previous = -1; if (!Array.isArray(value) || value.length > propertyOrder.length) return false; for (var i = 0; i < value.length; i++) { var index = propertyOrder.indexOf(value[i]); if (index <= previous) return false; previous = index; } return true; }
function validDamage(value) { return closed(value, ['count', 'faces', 'type']) && safePositive(value.count) && (value.faces === 4 || value.faces === 6 || value.faces === 8 || value.faces === 10 || value.faces === 12) && (value.type === 'bludgeoning' || value.type === 'piercing' || value.type === 'slashing'); }
function validProfile(value) {
  if (!value || !validTags(value.propertyTags)) return false;
  var keys = ['attackAbilities', 'category', 'damage', 'kind', 'mastery', 'propertyTags', 'sourceRef'];
  if (value.kind === 'ranged') keys.push('rangeFeet');
  if (hasTag(value.propertyTags, 'ammunition')) keys.push('ammunitionType');
  if (hasTag(value.propertyTags, 'thrown')) keys.push('thrownRangeFeet');
  if (hasTag(value.propertyTags, 'versatile')) keys.push('versatileDamage');
  keys.sort();
  if (!closed(value, keys) || (value.category !== 'simple' && value.category !== 'martial') || (value.kind !== 'melee' && value.kind !== 'ranged') || !sourceRef(value.sourceRef, profileLocator) || !Array.isArray(value.attackAbilities) || value.attackAbilities.length < 1 || value.attackAbilities.length > 2) return false;
  var previous = -1;
  for (var i = 0; i < value.attackAbilities.length; i++) {
    var index = value.attackAbilities[i] === 'str' ? 0 : value.attackAbilities[i] === 'dex' ? 1 : -1;
    if (index <= previous) { return false; }
    previous = index;
  }
  return validDamage(value.damage) && (value.kind !== 'ranged' || validRange(value.rangeFeet)) &&
         (!hasTag(value.propertyTags, 'ammunition') || (value.kind === 'ranged' && ['arrow', 'bolt', 'bullet', 'needle'].indexOf(value.ammunitionType) !== -1)) &&
         (!hasTag(value.propertyTags, 'thrown') || validRange(value.thrownRangeFeet)) &&
         (!hasTag(value.propertyTags, 'versatile') || (validDamage(value.versatileDamage) && value.versatileDamage.type === value.damage.type && value.versatileDamage.count === value.damage.count && value.versatileDamage.faces > value.damage.faces)) &&
         masteries.indexOf(value.mastery) !== -1;
}

if (!input || typeof input !== 'object' || Array.isArray(input)) {
  throw new Error('Input must be an object containing ability and optional rollCircumstances.');
}
var inputKeys = Object.keys(input).sort();
if (inputKeys.length < 1 || inputKeys.length > 2 || inputKeys.indexOf('ability') === -1 ||
    (inputKeys.length === 2 && inputKeys.indexOf('rollCircumstances') === -1)) {
  throw new Error('Input must contain exactly ability and optional rollCircumstances. Do not supply AC, profile, Proficiency Bonus, modifiers, dice, outcomes, damage, or effects.');
}
if (typeof input.ability !== 'string' || (input.ability !== 'str' && input.ability !== 'dex')) {
  throw new Error('input.ability must be exactly "str" or "dex" and must be permitted by the selected weapon profile.');
}

var circumstances = [];
var hasAdvantage = false;
var hasDisadvantage = false;
var duplicateCircumstances = {};
if (inputKeys.indexOf('rollCircumstances') !== -1) {
  if (!Array.isArray(input.rollCircumstances)) {
    throw new Error('rollCircumstances must be an array when present.');
  }
  for (var c = 0; c < input.rollCircumstances.length; c++) {
    var circumstance = input.rollCircumstances[c];
    if (!closed(circumstance, ['kind', 'source']) ||
        (circumstance.kind !== 'advantage' && circumstance.kind !== 'disadvantage') ||
        typeof circumstance.source !== 'string' || circumstance.source.length === 0 || circumstance.source.trim() !== circumstance.source) {
      throw new Error('Each roll circumstance must contain only advantage|disadvantage kind and a nonempty trimmed source.');
    }
    var circumstanceKey = circumstance.kind + '\u0000' + circumstance.source;
    if (duplicateCircumstances[circumstanceKey]) {
      throw new Error('rollCircumstances must not repeat an exact kind and source pair.');
    }
    if (circumstance.source.indexOf('condition:') === 0) {
      throw new Error('rollCircumstances source prefix condition: is reserved for derived condition state.');
    }
    duplicateCircumstances[circumstanceKey] = true;
    circumstances.push({ kind: circumstance.kind, source: circumstance.source });
    if (circumstance.kind === 'advantage') { hasAdvantage = true; }
    else { hasDisadvantage = true; }
  }
}

if (!subject || !target || !weapon || !subject.components || !target.components || !weapon.components) {
  throw new Error('Attack requires subject, target, and weapon roles with component state.');
}
if (!subject.components['dnd2024.abilities'] || !subject.components['dnd2024.character-level'] ||
    !subject.components['dnd2024.weapon-proficiencies'] || !weapon.components['dnd2024.weapon-profile']) {
  throw new Error('Attack requires subject abilities, level, and weapon proficiencies plus a weapon profile.');
}

var abilities = parse(subject.components['dnd2024.abilities'], 'Subject ability state');
if (!validScores(abilities)) { throw new Error('Subject ability state is invalid.'); }
var levelState = parse(subject.components['dnd2024.character-level'], 'Subject character-level state');
if (!closed(levelState, ['level', 'sourceRef']) || typeof levelState.level !== 'number' || !isFinite(levelState.level) ||
    Math.floor(levelState.level) !== levelState.level || levelState.level < 1 || levelState.level > 20 ||
    !sourceRef(levelState.sourceRef, levelLocator)) {
  throw new Error('Subject character-level state is invalid.');
}
var proficiencyState = parse(subject.components['dnd2024.weapon-proficiencies'], 'Subject weapon-proficiencies state');
if (!closed(proficiencyState, ['categories', 'sourceRef']) || !validCategories(proficiencyState.categories) ||
    !sourceRef(proficiencyState.sourceRef, proficiencyLocator)) {
  throw new Error('Subject weapon-proficiencies state is invalid.');
}
var targetArmorChildren = ctx.children && ctx.children.targetArmorClass;
if (!Array.isArray(targetArmorChildren) || targetArmorChildren.length !== 1) throw new Error('Exactly one derived target Armor Class result is required.');
var armorClass = readArmorClass(targetArmorChildren[0]);
var profile = parse(weapon.components['dnd2024.weapon-profile'], 'Weapon profile state');
if (!validProfile(profile)) { throw new Error('Weapon profile state is invalid.'); }
if (profile.attackAbilities.indexOf(input.ability) === -1) {
  throw new Error('The selected ability is not permitted by this canonical weapon profile.');
}

var attackerChildren = ctx.children && ctx.children.attackerEffects;
var targetChildren = ctx.children && ctx.children.targetEffects;
if (!Array.isArray(attackerChildren) || attackerChildren.length !== 1 || !Array.isArray(targetChildren) || targetChildren.length !== 1) {
  throw new Error('Exactly one condition state-effects result is required for each attack participant.');
}
function readEffects(child, expectedId, branch) {
  var report;
  try { report = JSON.parse(child.output && child.output.data ? child.output.data : '{}'); }
  catch (error) { throw new Error('Condition state-effects result is unreadable.'); }
  if (!report || typeof report !== 'object' || Array.isArray(report) || report.test !== 'd20-test-state-effects' ||
      report.subjectId !== expectedId || typeof report.conditionsKnown !== 'boolean' || !report.byTest ||
      typeof report.byTest !== 'object' || Array.isArray(report.byTest) || !Array.isArray(report.byTest[branch]) ||
      !Array.isArray(report.derivedModifiers)) {
    throw new Error('Condition state-effects result has an invalid shape.');
  }
  var derived = report.byTest[branch];
  for (var index = 0; index < derived.length; index++) {
    var item = derived[index];
    if (!closed(item, ['kind', 'source']) || (item.kind !== 'advantage' && item.kind !== 'disadvantage') ||
        typeof item.source !== 'string' || item.source.indexOf('condition:') !== 0) {
      throw new Error('Condition-derived attack circumstance is invalid.');
    }
  }
  var modifiers = report.derivedModifiers;
  for (var modifierIndex = 0; modifierIndex < modifiers.length; modifierIndex++) {
    var modifier = modifiers[modifierIndex];
    var level = modifier && typeof modifier.value === 'number' ? -modifier.value / 2 : 0;
    if (!closed(modifier, ['source', 'value']) || !isFinite(modifier.value) || Math.floor(modifier.value) !== modifier.value ||
        level < 1 || level > 6 || Math.floor(level) !== level ||
        modifier.source !== 'condition:exhaustion (level ' + level + ')') {
      throw new Error('Condition-derived attack modifier is invalid.');
    }
  }
  return { conditionsKnown: report.conditionsKnown, circumstances: derived, modifiers: modifiers };
}
var attackerEffects = readEffects(attackerChildren[0], subject.id, 'attackRoll');
var targetEffects = readEffects(targetChildren[0], target.id, 'attackAgainst');
var mergedCircumstances = circumstances.concat(attackerEffects.circumstances, targetEffects.circumstances);
for (var derivedIndex = 0; derivedIndex < attackerEffects.circumstances.length; derivedIndex++) {
  if (attackerEffects.circumstances[derivedIndex].kind === 'advantage') { hasAdvantage = true; }
  else { hasDisadvantage = true; }
}
for (var targetIndex = 0; targetIndex < targetEffects.circumstances.length; targetIndex++) {
  if (targetEffects.circumstances[targetIndex].kind === 'advantage') { hasAdvantage = true; }
  else { hasDisadvantage = true; }
}

var rollMode = 'normal';
if (hasAdvantage && !hasDisadvantage) { rollMode = 'advantage'; }
else if (hasDisadvantage && !hasAdvantage) { rollMode = 'disadvantage'; }

var rolls = [ctx.randomInt(1, 20)];
if (rollMode !== 'normal') { rolls.push(ctx.randomInt(1, 20)); }
var roll = rolls[0];
if (rollMode === 'advantage' && rolls[1] > roll) { roll = rolls[1]; }
if (rollMode === 'disadvantage' && rolls[1] < roll) { roll = rolls[1]; }

var abilityModifier = Math.floor((abilities[input.ability] - 10) / 2);
var proficiencyBonusDerived = 2 + Math.floor((levelState.level - 1) / 4);
var proficient = proficiencyState.categories.indexOf(profile.category) !== -1;
var proficiencyBonusApplied = proficient ? proficiencyBonusDerived : 0;
var modifiers = [{ source: input.ability + ' ' + abilities[input.ability], value: abilityModifier }];
if (proficient) {
  modifiers.push({ source: 'weapon proficiency (level ' + levelState.level + '; ' + profile.category + ')', value: proficiencyBonusApplied });
}
for (var modifierIndex = 0; modifierIndex < attackerEffects.modifiers.length; modifierIndex++) {
  modifiers.push(attackerEffects.modifiers[modifierIndex]);
}
var total = roll;
for (var totalIndex = 0; totalIndex < modifiers.length; totalIndex++) {
  total += modifiers[totalIndex].value;
}
var hit = total >= armorClass;
var critical = false;
var hitReason = 'armor-class';
if (roll === 20) {
  hit = true;
  critical = true;
  hitReason = 'natural-20';
} else if (roll === 1) {
  hit = false;
  hitReason = 'natural-1';
}
ctx.log('Weapon attack (' + rollMode + '): d20 ' + roll + ', total ' + total + ' vs AC ' + armorClass + ', ' + hitReason + '.');
return {
  narration: subject.name + ' attacks ' + target.name + ' with ' + weapon.name + ': ' +
             (hit ? 'hit' : 'miss') + ' (' + hitReason + ').',
  effects: [],
  data: {
    test: 'weapon-attack',
    subjectId: subject.id,
    targetId: target.id,
    weaponId: weapon.id,
    weaponCategory: profile.category,
    ability: input.ability,
    targetArmorClass: armorClass,
    proficient: proficient,
    proficiencyBonusDerived: proficiencyBonusDerived,
    proficiencyBonusApplied: proficiencyBonusApplied,
    abilityModifier: abilityModifier,
    modifiers: modifiers,
    die: '1d20',
    rollMode: rollMode,
    rolls: rolls,
    roll: roll,
    rollCircumstances: circumstances,
    attackerDerivedCircumstances: attackerEffects.circumstances,
    targetDerivedCircumstances: targetEffects.circumstances,
    mergedCircumstances: mergedCircumstances,
    attackerConditionsKnown: attackerEffects.conditionsKnown,
    targetConditionsKnown: targetEffects.conditionsKnown,
    total: total,
    hit: hit,
    critical: critical,
    hitReason: hitReason,
    source: 'SRD 5.2.1 - Playing the Game > D20 Tests > Attack Rolls; Equipment > Weapons > Weapon Proficiency'
  }
};
