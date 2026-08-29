// Convention, not a rule of the system: numbers live in a component called "stats", and the
// caller names which one. Nothing in the kernel knows that — a game that stores its numbers
// somewhere else writes its own version of this rule and stops using this one.
var raw = ctx.roles.subject.components.stats;

if (!raw) {
  throw new Error(
    ctx.roles.subject.name + ' has no "stats" component, so there is nothing to test. ' +
    'Attach one with commit(kind: "effects"), or use a rule that reads whatever component ' +
    'this game uses.');
}

var stats = JSON.parse(raw);
var field = ctx.input.field;

if (!field) {
  throw new Error('input.field is required — name which number to test, e.g. {"field":"vigour"}.');
}

if (typeof stats[field] !== 'number') {
  throw new Error(
    ctx.roles.subject.name + ' has no number called "' + field + '". Available: ' +
    Object.keys(stats).join(', ') + '.');
}

var threshold = typeof ctx.input.threshold === 'number' ? ctx.input.threshold : 12;
var bonus = typeof ctx.input.bonus === 'number' ? ctx.input.bonus : 0;

// Seeded and reproducible. Never Math.random() — an outcome nobody can replay is an outcome
// nobody can review, and the seed is recorded with the operation precisely so it can be.
var roll = ctx.randomInt(1, 20);
var total = roll + stats[field] + bonus;
var succeeded = total >= threshold;

ctx.log('rolled ' + roll + ' + ' + field + ' ' + stats[field] +
        (bonus ? ' + bonus ' + bonus : '') + ' = ' + total + ' vs ' + threshold);

// No effects. Deciding an outcome and changing the world are separate things, and a rule that
// only answers a question is the more reusable half.
return {
  narration: ctx.roles.subject.name + (succeeded ? ' manages it' : ' falls short') +
             ' (' + total + ' against ' + threshold + ').',
  effects: [],
  data: { roll: roll, total: total, threshold: threshold, succeeded: succeeded }
};
