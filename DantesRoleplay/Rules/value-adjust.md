---
id: mechanic.value.adjust
category: change
name: Adjust a number
status: active
---

## Description
Adds to or subtracts from one of the subject's numbers, optionally clamped. The other half of most
rules: something was decided, now the world changes.

## Matches
spend
lose
gain
recover
restore
reduce
increase

## Requirements
```json
{
  "roles": {
    "subject": {
      "components": ["stats"],
      "description": "Whose number is changing."
    }
  }
}
```

## Source
```js
var raw = ctx.roles.subject.components.stats;

if (!raw) {
  throw new Error(ctx.roles.subject.name + ' has no "stats" component to adjust.');
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
    definitionId: 'stats',
    data: JSON.stringify(change)
  }],
  data: { field: field, before: before, after: after, applied: after - before }
};
```
