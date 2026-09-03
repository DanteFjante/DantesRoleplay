var statsRaw = ctx.roles.subject.components.stats;
var lockRaw = ctx.roles.lock.components.lock;

if (!statsRaw) {
  throw new Error(ctx.roles.subject.name + ' has no stats component for a lock-picking attempt.');
}
if (!lockRaw) {
  throw new Error(ctx.roles.lock.name + ' has no lock component.');
}

var stats = JSON.parse(statsRaw);
var lock = JSON.parse(lockRaw);
if (typeof stats.agility !== 'number') {
  throw new Error(ctx.roles.subject.name + ' needs a numeric agility value to pick a lock.');
}
if (typeof lock.difficulty !== 'number') {
  throw new Error(ctx.roles.lock.name + ' needs a numeric lock difficulty.');
}

var bonus = typeof ctx.input.bonus === 'number' ? ctx.input.bonus : 0;
var roll = ctx.randomInt(1, 20);
var total = roll + stats.agility + bonus;
var succeeded = total >= lock.difficulty;

return {
  narration: ctx.roles.subject.name + (succeeded ? ' picks the lock on ' : ' fails to pick the lock on ') +
    ctx.roles.lock.name + ' (' + total + ' against ' + lock.difficulty + ').',
  effects: [],
  data: { roll: roll, agility: stats.agility, bonus: bonus, total: total, difficulty: lock.difficulty, succeeded: succeeded }
};
