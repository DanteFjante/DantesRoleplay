var item = ctx.roles.item;
var definition = ctx.roles.definition;

function only(value, keys) { if (value === null || Array.isArray(value) || typeof value !== 'object') return false; var actual = Object.keys(value).sort(); if (actual.length !== keys.length) return false; for (var i = 0; i < keys.length; i++) if (actual[i] !== keys[i]) return false; return true; }
function positive(value) { return typeof value === 'number' && Number.isSafeInteger(value) && value > 0; }
function parse(raw, message) { try { var value = JSON.parse(raw); } catch (error) { throw new Error(message); } if (value === null || Array.isArray(value) || typeof value !== 'object') throw new Error(message); return value; }

if (!only(ctx.input, ['count'])) throw new Error('Input must contain exactly count.');
if (!positive(ctx.input.count)) throw new Error('count must be a positive safe integer.');
if (item.contains && item.contains.length > 0) throw new Error('A stack with direct contents cannot be wholly consumed.');
var definitionData = parse(definition.components['dnd2024.item-definition'], 'The definition role lacks a valid item definition.');
var instance = parse(item.components['dnd2024.item-instance'], 'The item role is not a valid physical item instance.');
var quantity = parse(item.components['dnd2024.item-quantity'], 'The item role lacks a valid item quantity.');
if (definitionData.stackPolicy !== 'fungible' || instance.definitionId !== definition.id || quantity.stackKey !== definition.id || !positive(quantity.count)) throw new Error('The item stack does not match the selected immutable fungible definition.');
if (ctx.input.count > quantity.count) throw new Error('Cannot consume more items than the stack contains.');
if (ctx.input.count === quantity.count) return { narration: item.name + ' is fully consumed.', effects: [{ type: 'entity.delete', entityId: item.id }], data: { itemId: item.id, definitionId: definition.id, stackKey: quantity.stackKey, consumed: ctx.input.count, remaining: 0, deleted: true } };
return { narration: ctx.input.count + ' from ' + item.name + ' is consumed.', effects: [{ type: 'component.set', entityId: item.id, definitionId: 'dnd2024.item-quantity', data: JSON.stringify({ count: quantity.count - ctx.input.count, stackKey: quantity.stackKey }) }], data: { itemId: item.id, definitionId: definition.id, stackKey: quantity.stackKey, consumed: ctx.input.count, remaining: quantity.count - ctx.input.count, deleted: false } };
