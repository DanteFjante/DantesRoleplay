var raw = ctx.roles.subject.components['fixture.legacy.stats'];

if (!raw) {
  throw new Error(ctx.roles.subject.name + ' has no "fixture.legacy.stats" component to adjust.');
}

var stats = JSON.parse(raw);
var field = ctx.input.field;
var by = ctx.input.by;

if (!field) {
  throw new Error('input.field is required — name which number to change, e.g. {"field":"vigour"}.');
}

if (typeof by !== 'number') {
  throw new Error('input.by is required and must be a number, e.g. {"by":-3}.');
}

if (typeof stats[field] !== 'number') {
  throw new Error(
    ctx.roles.subject.name + ' has no number called "' + field + '". Available: ' +
    Object.keys(stats).join(', ') + '.');
}

var before = stats[field];
var after = before + by;

// Clamping is opt-in. A rule that silently floored everything at zero would make "how much did
// that actually cost?" unanswerable, which matters when another rule reads the result.
if (typeof ctx.input.min === 'number' && after < ctx.input.min) { after = ctx.input.min; }
if (typeof ctx.input.max === 'number' && after > ctx.input.max) { after = ctx.input.max; }

var change = {};
change[field] = after;

ctx.log(field + ': ' + before + ' -> ' + after + ' (asked for ' + (by >= 0 ? '+' : '') + by + ')');

// component.merge, never component.set: set would replace the whole component and quietly discard
// every other number on it.
return {
  narration: ctx.roles.subject.name + "'s " + field + ' is now ' + after +
             ' (was ' + before + ').',
  effects: [{
    type: 'component.merge',
    entityId: ctx.roles.subject.id,
    definitionId: 'fixture.legacy.stats',
    data: JSON.stringify(change)
  }],
  data: { field: field, before: before, after: after, applied: after - before }
};
