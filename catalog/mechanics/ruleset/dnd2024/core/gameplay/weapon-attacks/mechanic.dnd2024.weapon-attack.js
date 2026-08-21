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
var armorClassLocator = 'Playing the Game > D20 Tests > Attack Rolls > Armor Class';

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

function validProfile(value) {
  if (!closed(value, ['attackAbilities', 'category', 'damage', 'kind', 'sourceRef']) ||
      (value.category !== 'simple' && value.category !== 'martial') ||
      (value.kind !== 'melee' && value.kind !== 'ranged') ||
      !sourceRef(value.sourceRef, profileLocator) || !Array.isArray(value.attackAbilities) ||
      value.attackAbilities.length < 1 || value.attackAbilities.length > 2) {
    return false;
  }
  var previous = -1;
  for (var i = 0; i < value.attackAbilities.length; i++) {
    var index = value.attackAbilities[i] === 'str' ? 0 : value.attackAbilities[i] === 'dex' ? 1 : -1;
    if (index <= previous) { return false; }
    previous = index;
  }
  var damage = value.damage;
  return closed(damage, ['count', 'faces', 'type']) &&
         typeof damage.count === 'number' && isFinite(damage.count) && Math.floor(damage.count) === damage.count && damage.count >= 1 && damage.count <= 9007199254740991 &&
         (damage.faces === 4 || damage.faces === 6 || damage.faces === 8 || damage.faces === 10 || damage.faces === 12) &&
         (damage.type === 'bludgeoning' || damage.type === 'piercing' || damage.type === 'slashing');
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
    !subject.components['dnd2024.weapon-proficiencies'] || !target.components['dnd2024.armor-class'] ||
    !weapon.components['dnd2024.weapon-profile']) {
  throw new Error('Attack requires subject abilities, level, and weapon proficiencies; target Armor Class; and a weapon profile.');
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
var armorClassState = parse(target.components['dnd2024.armor-class'], 'Target Armor Class state');
if (!closed(armorClassState, ['sourceRef', 'value']) || typeof armorClassState.value !== 'number' ||
    !isFinite(armorClassState.value) || Math.floor(armorClassState.value) !== armorClassState.value ||
    armorClassState.value < 1 || armorClassState.value > 9007199254740991 ||
    !sourceRef(armorClassState.sourceRef, armorClassLocator)) {
  throw new Error('Target Armor Class state is invalid.');
}
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
      typeof report.byTest !== 'object' || Array.isArray(report.byTest) || !Array.isArray(report.byTest[branch])) {
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
  return { conditionsKnown: report.conditionsKnown, circumstances: derived };
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
var total = roll + abilityModifier + proficiencyBonusApplied;
var hit = total >= armorClassState.value;
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
var modifiers = [{ source: input.ability + ' ' + abilities[input.ability], value: abilityModifier }];
if (proficient) {
  modifiers.push({ source: 'weapon proficiency (level ' + levelState.level + '; ' + profile.category + ')', value: proficiencyBonusApplied });
}

ctx.log('Weapon attack (' + rollMode + '): d20 ' + roll + ', total ' + total + ' vs AC ' + armorClassState.value + ', ' + hitReason + '.');
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
    targetArmorClass: armorClassState.value,
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
