var abilityIds = ["str", "dex", "con", "int", "wis", "cha"];
var abilityRefs = {str: "dnd2024.vocabulary.ability.strength", dex: "dnd2024.vocabulary.ability.dexterity", con: "dnd2024.vocabulary.ability.constitution", int: "dnd2024.vocabulary.ability.intelligence", wis: "dnd2024.vocabulary.ability.wisdom", cha: "dnd2024.vocabulary.ability.charisma"};
var skillIds = ["acrobatics", "animal-handling", "arcana", "athletics", "deception", "history", "insight", "intimidation", "investigation", "medicine", "nature", "perception", "performance", "persuasion", "religion", "sleight-of-hand", "stealth", "survival"];
var skillAbilities = {"acrobatics":"dex","animal-handling":"wis","arcana":"int","athletics":"str","deception":"cha","history":"int","insight":"wis","intimidation":"cha","investigation":"int","medicine":"wis","nature":"int","perception":"wis","performance":"cha","persuasion":"cha","religion":"int","sleight-of-hand":"dex","stealth":"dex","survival":"wis"};
var sourceId = "dnd2024.source.srd-5.2.1";
var proficiencyRank = "dnd2024.vocabulary.proficiency-rank.proficiency";
var expertiseRank = "dnd2024.vocabulary.proficiency-rank.expertise";

function exactKeys(value, keys) {
  if (!value || typeof value !== "object" || Array.isArray(value)) { return false; }
  var actual = Object.keys(value).sort();
  var expected = keys.slice().sort();
  if (actual.length !== expected.length) { return false; }
  for (var index = 0; index < expected.length; index++) {
    if (actual[index] !== expected[index]) { return false; }
  }
  return true;
}

function exactRef(value, locator) {
  return exactKeys(value, ["locator", "sourceId"]) && value.sourceId === sourceId && value.locator === locator;
}

function finiteInteger(value, minimum, maximum) {
  return typeof value === "number" && isFinite(value) && Math.floor(value) === value &&
    value >= minimum && value <= maximum;
}

function parseComponent(subject, componentId, message) {
  var raw = subject.components && subject.components[componentId];
  if (typeof raw !== "string") { throw new Error(message); }
  try { return JSON.parse(raw); } catch (error) { throw new Error(message); }
}

function canonicalSet(value, allowed, maximum) {
  if (!Array.isArray(value) || value.length > maximum) { return null; }
  var present = {};
  for (var index = 0; index < value.length; index++) {
    if (typeof value[index] !== "string" || allowed.indexOf(value[index]) === -1 || present[value[index]]) {
      return null;
    }
    present[value[index]] = true;
  }
  var ordered = [];
  for (var allowedIndex = 0; allowedIndex < allowed.length; allowedIndex++) {
    if (present[allowed[allowedIndex]]) { ordered.push(allowed[allowedIndex]); }
  }
  return { present: present, ordered: ordered };
}

function derivedLevel(subject) {
  var children = ctx.children && ctx.children.level;
  if (!Array.isArray(children) || children.length !== 1 || !children[0].roleEntityIds ||
      children[0].roleEntityIds.subject !== subject.id) {
    throw new Error("Exactly one matching character-level result is required.");
  }
  var value;
  try { value = JSON.parse(children[0].output && children[0].output.data ? children[0].output.data : "{}"); }
  catch (error) { throw new Error("Character-level result is unreadable."); }
  if (!exactKeys(value, ["membershipCount", "present", "problem", "proficiencyBonus", "subjectId", "test", "totalLevel", "valid"]) ||
      value.test !== "character-level-read" || value.subjectId !== subject.id || !value.present || !value.valid ||
      !finiteInteger(value.totalLevel, 1, 20) || !finiteInteger(value.proficiencyBonus, 2, 6)) {
    throw new Error("Character-level result is invalid.");
  }
  return value;
}

if (!exactKeys(ctx.input, [])) {
  throw new Error("Character-sheet derivation input must be the empty object.");
}
if (!ctx.roles || !ctx.roles.subject || !ctx.roles.subject.components) {
  throw new Error("A character-sheet subject is required.");
}

var subject = ctx.roles.subject;
var abilities = parseComponent(subject, "dnd2024.creature.ability-scores", "Ability state is missing or malformed.");
var characterLevel = derivedLevel(subject);
var proficiencies = parseComponent(subject, "dnd2024.creature.proficiencies", "Proficiency state is missing or malformed.");

if (!exactKeys(abilities, ["scores"]) || !exactKeys(abilities.scores, abilityIds.map(function (ability) { return abilityRefs[ability]; }))) {
  throw new Error("Ability state must contain exactly the six vocabulary-keyed scores.");
}
for (var abilityIndex = 0; abilityIndex < abilityIds.length; abilityIndex++) {
  if (!finiteInteger(abilities.scores[abilityRefs[abilityIds[abilityIndex]]], 1, 30)) {
    throw new Error("Every ability score must be an integer from 1 through 30.");
  }
}
if (!exactKeys(proficiencies, ["entries", "recordedFamilies"]) ||
    !proficiencies.entries || typeof proficiencies.entries !== "object" || Array.isArray(proficiencies.entries) ||
    !Array.isArray(proficiencies.recordedFamilies) ||
    proficiencies.recordedFamilies.indexOf("skill") === -1 ||
    proficiencies.recordedFamilies.indexOf("saving-throw") === -1) {
  throw new Error("Skill and saving-throw proficiency state must be recorded.");
}

function rankFor(entityId) {
  var entry = proficiencies.entries[entityId];
  if (!entry) { return null; }
  if (!exactKeys(entry, ["rankRef", "sourceRefs"]) || !exactKeys(entry.rankRef, ["entityId"]) ||
      (entry.rankRef.entityId !== proficiencyRank && entry.rankRef.entityId !== expertiseRank) ||
      !Array.isArray(entry.sourceRefs) || entry.sourceRefs.length === 0) {
    throw new Error("Proficiency state contains an invalid rank or source list.");
  }
  return entry.rankRef.entityId;
}

var skillSet = {present: {}, ordered: []};
for (var skillSetIndex = 0; skillSetIndex < skillIds.length; skillSetIndex++) {
  var skillRank = rankFor("dnd2024.vocabulary.skill." + skillIds[skillSetIndex]);
  if (skillRank) {
    skillSet.present[skillIds[skillSetIndex]] = skillRank;
    skillSet.ordered.push(skillIds[skillSetIndex]);
  }
}
var saveSet = {present: {}, ordered: []};
for (var saveSetIndex = 0; saveSetIndex < abilityIds.length; saveSetIndex++) {
  var saveRank = rankFor(abilityRefs[abilityIds[saveSetIndex]]);
  if (saveRank) {
    if (saveRank !== proficiencyRank) { throw new Error("Saving-throw Expertise is not a valid state in this ruleset."); }
    saveSet.present[abilityIds[saveSetIndex]] = saveRank;
    saveSet.ordered.push(abilityIds[saveSetIndex]);
  }
}

var level = characterLevel.totalLevel;
var proficiencyBonus = characterLevel.proficiencyBonus;
var abilityModifiers = {};
var abilityEntries = [];
var savingThrowModifiers = {};
var savingThrowEntries = [];
for (abilityIndex = 0; abilityIndex < abilityIds.length; abilityIndex++) {
  var ability = abilityIds[abilityIndex];
  var score = abilities.scores[abilityRefs[ability]];
  var modifier = Math.floor((score - 10) / 2);
  var saveProficient = !!saveSet.present[ability];
  var saveModifier = modifier + (saveProficient ? proficiencyBonus : 0);
  abilityModifiers[ability] = modifier;
  abilityEntries.push({ id: ability, score: score, modifier: modifier });
  savingThrowModifiers[ability] = saveModifier;
  savingThrowEntries.push({ ability: ability, proficient: saveProficient, modifier: saveModifier });
}

var skillModifiers = {};
var skillEntries = [];
for (var skillIndex = 0; skillIndex < skillIds.length; skillIndex++) {
  var skill = skillIds[skillIndex];
  var skillAbility = skillAbilities[skill];
  var skillProficient = !!skillSet.present[skill];
  var skillMultiplier = skillSet.present[skill] === expertiseRank ? 2 : (skillProficient ? 1 : 0);
  var skillModifier = abilityModifiers[skillAbility] + proficiencyBonus * skillMultiplier;
  skillModifiers[skill] = skillModifier;
  skillEntries.push({ id: skill, ability: skillAbility, proficient: skillProficient, modifier: skillModifier });
}

var passivePerception = 10 + skillModifiers.perception;
return {
  narration: subject.name + " has a derived level " + level + " core character sheet.",
  effects: [],
  events: [],
  notifications: [],
  data: {
    test: "character-sheet-core",
    level: level,
    proficiencyBonus: proficiencyBonus,
    abilities: abilityEntries,
    abilityModifiers: abilityModifiers,
    savingThrows: savingThrowEntries,
    savingThrowProficiencies: saveSet.ordered,
    savingThrowModifiers: savingThrowModifiers,
    skills: skillEntries,
    skillProficiencies: skillSet.ordered,
    skillModifiers: skillModifiers,
    initiative: { ability: "dex", modifier: abilityModifiers.dex },
    initiativeModifier: abilityModifiers.dex,
    basePassivePerceptionBreakdown: { base: 10, skill: "perception", modifier: skillModifiers.perception, total: passivePerception },
    basePassivePerception: passivePerception,
    sourceId: sourceId,
    sourceLocators: [
      "Character Creation > Step 5: Character Creation Details > Fill In Numbers",
      "Character Creation > Level Advancement > Character Advancement",
      "Playing the Game > The Six Abilities > Ability Scores/Ability Modifiers",
      "Playing the Game > Proficiency > Saving Throw Proficiencies",
      "Playing the Game > Proficiency > Skill Proficiencies and Skills",
      "Rules Glossary > Passive Perception"
    ]
  }
};
