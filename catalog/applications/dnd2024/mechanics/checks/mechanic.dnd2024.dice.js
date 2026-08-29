var count = ctx.input && typeof ctx.input.count === "number" ? ctx.input.count : 1;
var sides = ctx.input && typeof ctx.input.sides === "number" ? ctx.input.sides : 20;
var modifier = ctx.input && typeof ctx.input.modifier === "number" ? ctx.input.modifier : 0;

if (!Number.isInteger(count) || count < 1) {
  throw new Error("input.count must be a positive integer.");
}
if (!Number.isInteger(sides) || sides < 2) {
  throw new Error("input.sides must be an integer of at least 2.");
}
if (!Number.isInteger(modifier)) {
  throw new Error("input.modifier must be an integer.");
}

var rolls = [];
var total = modifier;
for (var i = 0; i < count; i++) {
  var roll = ctx.randomInt(1, sides);
  rolls.push(roll);
  total += roll;
}

ctx.log("rolled " + count + "d" + sides + (modifier ? (modifier > 0 ? "+" : "") + modifier : "") + " = " + total);

return {
  narration: count + "d" + sides + (modifier ? (modifier > 0 ? "+" : "") + modifier : "") + " rolled " + rolls.join(", ") + " for a total of " + total + ".",
  effects: [],
  data: { count: count, sides: sides, modifier: modifier, rolls: rolls, total: total }
};
