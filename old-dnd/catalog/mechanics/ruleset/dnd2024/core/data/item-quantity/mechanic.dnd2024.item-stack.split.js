var source = ctx.roles.source;
var definition = ctx.roles.definition;

function only(value, keys) { if (value === null || Array.isArray(value) || typeof value !== 'object') return false; var actual = Object.keys(value).sort(); if (actual.length !== keys.length) return false; for (var i = 0; i < keys.length; i++) if (actual[i] !== keys[i]) return false; return true; }
function id(value) { return typeof value === 'string' && /^[a-z][a-z0-9.-]{0,199}$/.test(value); }
function text(value, maximum) { return typeof value === 'string' && value.trim() === value && value.length > 0 && value.length <= maximum; }
function positive(value) { return typeof value === 'number' && Number.isSafeInteger(value) && value > 0; }
function parse(raw, message) { try { var value = JSON.parse(raw); } catch (error) { throw new Error(message); } if (value === null || Array.isArray(value) || typeof value !== 'object') throw new Error(message); return value; }
function stack(role, expectedDefinition) { var instance = parse(role.components['dnd2024.item-instance'], 'The source role is not a valid physical item instance.'); var quantity = parse(role.components['dnd2024.item-quantity'], 'The source role lacks a valid item quantity.'); if (!positive(quantity.count) || quantity.stackKey !== expectedDefinition || instance.definitionId !== expectedDefinition) throw new Error('The source stack does not match the selected immutable definition.'); return quantity; }

if (!only(ctx.input, ['count', 'itemId', 'name'])) throw new Error('Input must contain exactly count, itemId, and name.');
if (!positive(ctx.input.count)) throw new Error('count must be a positive safe integer.');
if (!id(ctx.input.itemId)) throw new Error('itemId must be a permanent lower-case dotted or hyphenated identifier of at most 200 characters.');
if (!text(ctx.input.name, 400)) throw new Error('name must be trimmed, non-empty, and at most 400 characters.');
if (source.contains && source.contains.length > 0) throw new Error('A stack with direct contents cannot be split.');
var definitionData = parse(definition.components['dnd2024.item-definition'], 'The definition role lacks a valid item definition.');
if (definitionData.stackPolicy !== 'fungible') throw new Error('The selected definition is not fungible.');
var quantity = stack(source, definition.id);
if (ctx.input.count >= quantity.count) throw new Error('A split count must be smaller than the source count so neither stack becomes zero.');

var effects = [
  { type: 'component.set', entityId: source.id, definitionId: 'dnd2024.item-quantity', data: JSON.stringify({ count: quantity.count - ctx.input.count, stackKey: quantity.stackKey }) },
  { type: 'entity.create', entityId: ctx.input.itemId, name: ctx.input.name },
  { type: 'component.add', entityId: ctx.input.itemId, definitionId: 'dnd2024.item-instance', data: JSON.stringify({ definitionId: definition.id }) },
  { type: 'component.add', entityId: ctx.input.itemId, definitionId: 'dnd2024.item-quantity', data: JSON.stringify({ count: ctx.input.count, stackKey: quantity.stackKey }) }
];
if (source.containerId) effects.push({ type: 'containment.move', entityId: ctx.input.itemId, toEntityId: source.containerId, slot: source.containerSlot });
return { narration: source.name + ' is split into ' + source.name + ' and ' + ctx.input.name + '.', effects: effects, data: { sourceItemId: source.id, itemId: ctx.input.itemId, definitionId: definition.id, stackKey: quantity.stackKey, sourceCount: quantity.count - ctx.input.count, splitCount: ctx.input.count, containerId: source.containerId || null, slot: source.containerSlot } };
