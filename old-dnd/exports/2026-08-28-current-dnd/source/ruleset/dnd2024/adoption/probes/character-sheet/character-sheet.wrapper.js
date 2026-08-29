var abilityIds = ["str", "dex", "con", "int", "wis", "cha"];
var skillIds = ["acrobatics", "animal-handling", "arcana", "athletics", "deception", "history", "insight", "intimidation", "investigation", "medicine", "nature", "perception", "performance", "persuasion", "religion", "sleight-of-hand", "stealth", "survival"];
var skillAbilities = {"acrobatics":"dex","animal-handling":"wis","arcana":"int","athletics":"str","deception":"cha","history":"int","insight":"wis","intimidation":"cha","investigation":"int","medicine":"wis","nature":"int","perception":"wis","performance":"cha","persuasion":"cha","religion":"int","sleight-of-hand":"dex","stealth":"dex","survival":"wis"};
var sourceId = "source.dnd2024.srd-5.2.1";
var levelLocator = "Character Creation > Level Advancement > Character Advancement";
var skillLocator = "Playing the Game > Proficiency > Skill Proficiencies and Skills";
var saveLocator = "Playing the Game > Proficiency > Saving Throw Proficiencies";

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

if (!exactKeys(ctx.input, [])) {
  throw new Error("Character-sheet derivation input must be the empty object.");
}
if (!exactKeys(ctx.roles, ["subject"]) || !ctx.roles.subject || !ctx.roles.subject.components ||
    !exactKeys(ctx.roles.subject.components, ["character-sheet-view"])) {
  throw new Error("The declared character-sheet view is required.");
}

var view;
try {
  view = JSON.parse(ctx.roles.subject.components["character-sheet-view"]);
} catch (error) {
  throw new Error("The declared character-sheet view must be JSON.");
}
if (!exactKeys(view, ["abilities", "characterLevel", "savingThrowProficiencies", "skillProficiencies"]) ||
    !exactKeys(view.abilities, abilityIds)) {
  throw new Error("The character-sheet view must contain only the four declared state groups and six abilities.");
}
for (var abilityIndex = 0; abilityIndex < abilityIds.length; abilityIndex++) {
  if (!finiteInteger(view.abilities[abilityIds[abilityIndex]], 1, 30)) {
    throw new Error("Every ability score must be an integer from 1 through 30.");
  }
}
if (!exactKeys(view.characterLevel, ["level", "sourceRef"]) ||
    !finiteInteger(view.characterLevel.level, 1, 20) ||
    !exactRef(view.characterLevel.sourceRef, levelLocator)) {
  throw new Error("Character-level state is invalid or source-drifted.");
}
if (!exactKeys(view.skillProficiencies, ["skills", "sourceRef"]) ||
    !exactRef(view.skillProficiencies.sourceRef, skillLocator)) {
  throw new Error("Skill-proficiency state is invalid or source-drifted.");
}
if (!exactKeys(view.savingThrowProficiencies, ["abilities", "sourceRef"]) ||
    !exactRef(view.savingThrowProficiencies.sourceRef, saveLocator)) {
  throw new Error("Saving-throw proficiency state is invalid or source-drifted.");
}
var skillSet = canonicalSet(view.skillProficiencies.skills, skillIds, skillIds.length);
var saveSet = canonicalSet(view.savingThrowProficiencies.abilities, abilityIds, abilityIds.length);
if (!skillSet || !saveSet) {
  throw new Error("Proficiency state must contain unique canonical IDs only.");
}

var level = view.characterLevel.level;
var proficiencyBonus = 2 + Math.floor((level - 1) / 4);
var abilityModifiers = {};
var abilityEntries = [];
var savingThrowModifiers = {};
var savingThrowEntries = [];
for (abilityIndex = 0; abilityIndex < abilityIds.length; abilityIndex++) {
  var ability = abilityIds[abilityIndex];
  var score = view.abilities[ability];
  var modifier = Math.floor((score - 10) / 2);
  var saveProficient = saveSet.present[ability] === true;
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
  var skillProficient = skillSet.present[skill] === true;
  var skillModifier = abilityModifiers[skillAbility] + (skillProficient ? proficiencyBonus : 0);
  skillModifiers[skill] = skillModifier;
  skillEntries.push({ id: skill, ability: skillAbility, proficient: skillProficient, modifier: skillModifier });
}

var passivePerception = 10 + skillModifiers.perception;
ctx.log("derived character-sheet core for level " + level);
return {
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
