var input = ctx.input;
var abilities = ["str", "dex", "con", "int", "wis", "cha"];

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

if (!exactKeys(input, ["ability", "dc"]) || typeof input.ability !== "string" ||
    abilities.indexOf(input.ability) === -1 || typeof input.dc !== "number" ||
    !isFinite(input.dc) || Math.floor(input.dc) !== input.dc || input.dc < 0 ||
    input.dc > 9007199254740991) {
  throw new Error("Input must contain only one ability id and one finite nonnegative integer DC.");
}

if (!exactKeys(ctx.roles, ["subject"]) || !ctx.roles.subject || !ctx.roles.subject.components ||
    !exactKeys(ctx.roles.subject.components, ["operation-view"])) {
  throw new Error("The declared operation view is required.");
}

var view;
try {
  view = JSON.parse(ctx.roles.subject.components["operation-view"]);
} catch (error) {
  throw new Error("The declared operation view must be JSON.");
}

if (!exactKeys(view, ["scores"]) || !exactKeys(view.scores, abilities)) {
  throw new Error("The declared operation view must contain exactly the six ability scores.");
}
for (var scoreIndex = 0; scoreIndex < abilities.length; scoreIndex++) {
  var candidate = view.scores[abilities[scoreIndex]];
  if (typeof candidate !== "number" || !isFinite(candidate) || Math.floor(candidate) !== candidate ||
      candidate < 1 || candidate > 30) {
    throw new Error("Every declared ability score must be an integer from 1 through 30.");
  }
}

var ability = input.ability;
var score = view.scores[ability];
var modifier = Math.floor((score - 10) / 2);
var roll = ctx.randomInt(1, 20);
var total = roll + modifier;
var succeeded = total >= input.dc;

ctx.log("ability-check roll " + roll);

return {
  effects: [],
  events: [],
  notifications: [],
  data: {
    test: "ability-check",
    ability: ability,
    score: score,
    dc: input.dc,
    die: "1d20",
    roll: roll,
    modifiers: [{ source: ability + " " + score, value: modifier }],
    total: total,
    succeeded: succeeded,
    sourceId: "source.dnd2024.srd-5.2.1",
    sourceLocator: "Playing the Game > D20 Tests > Ability Checks"
  }
};
