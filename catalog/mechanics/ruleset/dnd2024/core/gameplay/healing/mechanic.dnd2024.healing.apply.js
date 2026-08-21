// D&D 2024 bounded healing application.
// Governed by procedure.mechanic.dnd2024.healing.
var subject = ctx.roles.subject;
var input = ctx.input;
var definitionId = 'dnd2024.hit-points';
var maxSafe = 9007199254740991;
var sourceId = 'source.dnd2024.srd-5.2.1';
var hitPointLocator = 'Playing the Game > Damage and Healing > Hit Points';
var healingLocator = 'Playing the Game > Damage and Healing > Healing';

function closed(value, keys) { if (!value || typeof value !== 'object' || Array.isArray(value)) return false; var actual = Object.keys(value).sort(); if (actual.length !== keys.length) return false; for (var index = 0; index < keys.length; index++) if (actual[index] !== keys[index]) return false; return true; }
function safe(value, minimum) { return typeof value === 'number' && isFinite(value) && Math.floor(value) === value && value >= minimum && value <= maxSafe; }
function validHitPoints(value) { return closed(value, ['current', 'maximum', 'sourceRef']) && safe(value.current, 0) && safe(value.maximum, 1) && value.current <= value.maximum && closed(value.sourceRef, ['locator', 'sourceId']) && value.sourceRef.sourceId === sourceId && value.sourceRef.locator === hitPointLocator; }
function parse(raw) { try { return JSON.parse(raw); } catch (error) { throw new Error('The stored Hit Point state is malformed.'); } }

if (!subject || !subject.components || !subject.components[definitionId]) throw new Error('Healing requires a subject with authoritative Hit Points.');
if (!closed(input, ['amount']) || !safe(input.amount, 1)) throw new Error('Input must contain exactly a positive safe integer amount. Do not supply current, maximum, sourceRef, final, cause, effects, or an event.');
var before = parse(subject.components[definitionId]);
if (!validHitPoints(before)) throw new Error('The stored Hit Point state is invalid.');
var missing = before.maximum - before.current;
var appliedAmount = Math.min(input.amount, missing);
var afterCurrent = before.current + appliedAmount;
var lostToMaximum = input.amount - appliedAmount;
var after = { current: afterCurrent, maximum: before.maximum, sourceRef: before.sourceRef };
ctx.log('Applied ' + appliedAmount + ' healing to ' + subject.name + ': ' + before.current + ' -> ' + afterCurrent + '.');
return { narration: subject.name + ' regains ' + appliedAmount + ' Hit Points: ' + before.current + ' to ' + afterCurrent + '.', effects: [{ type: 'component.set', entityId: subject.id, definitionId: definitionId, data: JSON.stringify(after) }], events: [{ type: 'dnd2024.healing.received', payload: { targetId: subject.id, requestedAmount: input.amount, appliedAmount: appliedAmount, lostToMaximum: lostToMaximum, beforeCurrent: before.current, afterCurrent: afterCurrent, maximum: before.maximum, sourceRef: { sourceId: sourceId, locator: healingLocator } }, entityIds: [subject.id] }], data: { test: 'healing-application', subjectId: subject.id, requestedAmount: input.amount, appliedAmount: appliedAmount, lostToMaximum: lostToMaximum, beforeCurrent: before.current, afterCurrent: afterCurrent, maximum: before.maximum, sourceRef: { sourceId: sourceId, locator: healingLocator } } };
