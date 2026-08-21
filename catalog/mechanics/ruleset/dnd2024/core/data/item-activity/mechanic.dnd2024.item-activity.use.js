var item = ctx.roles.item;
var definition = ctx.roles.definition;
var grantDefinition = ctx.roles.grantDefinition;

function only(value, keys) { if (value === null || Array.isArray(value) || typeof value !== 'object') return false; var actual = Object.keys(value).sort(); if (actual.length !== keys.length) return false; for (var i = 0; i < keys.length; i++) if (actual[i] !== keys[i]) return false; return true; }
function parse(raw, message) { try { var value = JSON.parse(raw); } catch (error) { throw new Error(message); } if (value === null || Array.isArray(value) || typeof value !== 'object') throw new Error(message); return value; }
function positive(value) { return typeof value === 'number' && Number.isSafeInteger(value) && value > 0; }
function id(value) { return typeof value === 'string' && /^[a-z][a-z0-9.-]{0,199}$/.test(value); }

if (!only(ctx.input, ['activityId', 'grantItemId']) || typeof ctx.input.activityId !== 'string' || !/^[a-z][a-z0-9-]{0,63}$/.test(ctx.input.activityId) || !id(ctx.input.grantItemId)) throw new Error('Input must contain exactly a valid activityId and grantItemId.');
if (!item.containerId) throw new Error('An item activity requires the source stack to have a direct container.');
if (item.contains && item.contains.length > 0) throw new Error('An item stack with direct contents cannot be consumed by an activity.');
var sourceDefinition = parse(definition.components['dnd2024.item-definition'], 'The definition role lacks a valid item definition.');
var activities = parse(definition.components['dnd2024.item-activity'], 'The definition role lacks valid item activities.');
var grantedDefinition = parse(grantDefinition.components['dnd2024.item-definition'], 'The grant definition role lacks a valid item definition.');
var instance = parse(item.components['dnd2024.item-instance'], 'The item role is not a valid physical item instance.');
var quantity = parse(item.components['dnd2024.item-quantity'], 'The item role lacks a valid item quantity.');
if (instance.definitionId !== definition.id || sourceDefinition.stackPolicy !== 'fungible' || quantity.stackKey !== definition.id || !positive(quantity.count)) throw new Error('The item stack does not match the selected immutable fungible definition.');
if (!Array.isArray(activities.activities)) throw new Error('The definition role lacks valid item activities.');
var activity = null;
for (var i = 0; i < activities.activities.length; i++) if (activities.activities[i] && activities.activities[i].id === ctx.input.activityId) { activity = activities.activities[i]; break; }
if (!activity || activity.kind !== 'consume-and-grant-item' || !positive(activity.consumeQuantity) || !activity.grant || !id(activity.grant.definitionId) || typeof activity.grant.name !== 'string' || activity.grant.name.trim().length === 0 || typeof activity.grant.slot !== 'string' || !/^[a-z][a-z0-9-]*$/.test(activity.grant.slot)) throw new Error('The selected item activity is invalid.');
if (activity.grant.definitionId !== grantDefinition.id || !grantedDefinition || grantDefinition.id === definition.id) throw new Error('The selected grant definition does not match the immutable activity descriptor.');
if (quantity.count < activity.consumeQuantity) throw new Error('The item stack does not contain enough quantity for this activity.');
var effects = [];
if (quantity.count === activity.consumeQuantity) effects.push({ type: 'entity.delete', entityId: item.id });
else effects.push({ type: 'component.set', entityId: item.id, definitionId: 'dnd2024.item-quantity', data: JSON.stringify({ count: quantity.count - activity.consumeQuantity, stackKey: quantity.stackKey }) });
effects.push({ type: 'entity.create', entityId: ctx.input.grantItemId, name: activity.grant.name });
effects.push({ type: 'component.add', entityId: ctx.input.grantItemId, definitionId: 'dnd2024.item-instance', data: JSON.stringify({ definitionId: grantDefinition.id }) });
effects.push({ type: 'containment.move', entityId: ctx.input.grantItemId, toEntityId: item.containerId, slot: activity.grant.slot });
return { narration: item.name + ' is used to create ' + activity.grant.name + '.', effects: effects, data: { itemId: item.id, activityId: activity.id, consumed: activity.consumeQuantity, remaining: quantity.count - activity.consumeQuantity, grantItemId: ctx.input.grantItemId, grantDefinitionId: grantDefinition.id, containerId: item.containerId, slot: activity.grant.slot } };
