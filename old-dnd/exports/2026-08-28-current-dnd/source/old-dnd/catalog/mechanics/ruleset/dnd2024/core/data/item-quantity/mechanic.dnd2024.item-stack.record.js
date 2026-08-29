var item = ctx.roles.item;
var definition = ctx.roles.definition;

function only(value, keys) { if (value === null || Array.isArray(value) || typeof value !== 'object') return false; var actual = Object.keys(value).sort(); if (actual.length !== keys.length) return false; for (var i = 0; i < keys.length; i++) if (actual[i] !== keys[i]) return false; return true; }
function positive(value) { return typeof value === 'number' && Number.isSafeInteger(value) && value > 0; }
function parse(raw, message) { try { var value = JSON.parse(raw); } catch (error) { throw new Error(message); } if (value === null || Array.isArray(value) || typeof value !== 'object') throw new Error(message); return value; }

if (!only(ctx.input, ['count'])) throw new Error('Input must contain exactly count.');
if (!positive(ctx.input.count)) throw new Error('count must be a positive safe integer.');
if (item.components['dnd2024.item-definition']) throw new Error('A catalog definition entity cannot become a physical item stack.');
if (item.components['dnd2024.item-quantity']) throw new Error('This physical item already has a quantity; stack identity cannot be corrected in place.');
var instance = parse(item.components['dnd2024.item-instance'], 'The item role is not a valid physical item instance.');
var definitionData = parse(definition.components['dnd2024.item-definition'], 'The definition role lacks a valid item definition.');
if (instance.definitionId !== definition.id) throw new Error('The selected definition does not match the physical item instance.');
if (definitionData.stackPolicy !== 'fungible') throw new Error('The selected definition is not fungible.');

return { narration: item.name + ' is recorded as a stack of ' + ctx.input.count + '.', effects: [{ type: 'component.add', entityId: item.id, definitionId: 'dnd2024.item-quantity', data: JSON.stringify({ count: ctx.input.count, stackKey: definition.id }) }], data: { itemId: item.id, definitionId: definition.id, stackKey: definition.id, count: ctx.input.count } };
