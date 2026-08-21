var definition = ctx.roles.definition;
var destination = ctx.roles.destination;

function only(value, keys) { if (value === null || Array.isArray(value) || typeof value !== 'object') return false; var actual = Object.keys(value).sort(); if (actual.length !== keys.length) return false; for (var i = 0; i < keys.length; i++) if (actual[i] !== keys[i]) return false; return true; }
function id(value) { return typeof value === 'string' && /^[a-z][a-z0-9.-]{0,199}$/.test(value); }
function text(value, maximum) { return typeof value === 'string' && value.trim() === value && value.length > 0 && value.length <= maximum; }
function positive(value) { return typeof value === 'number' && Number.isSafeInteger(value) && value > 0; }
function definitionData(role) { var raw = role.components['dnd2024.item-definition']; if (!raw) throw new Error('The definition role lacks a valid item definition.'); try { var value = JSON.parse(raw); } catch (error) { throw new Error('The item definition component is corrupt.'); } if (value === null || Array.isArray(value) || typeof value !== 'object' || value.stackPolicy !== 'fungible') throw new Error('The selected definition is not fungible.'); return value; }

if (!only(ctx.input, ['count', 'itemId', 'name', 'slot'])) throw new Error('Input must contain exactly count, itemId, name, and slot.');
if (!positive(ctx.input.count)) throw new Error('count must be a positive safe integer.');
if (!id(ctx.input.itemId)) throw new Error('itemId must be a permanent lower-case dotted or hyphenated identifier of at most 200 characters.');
if (!text(ctx.input.name, 400)) throw new Error('name must be trimmed, non-empty, and at most 400 characters.');
if (typeof ctx.input.slot !== 'string' || ctx.input.slot.trim() !== ctx.input.slot || ctx.input.slot.length > 100) throw new Error('slot must be a trimmed string of at most 100 characters.');
definitionData(definition);

return { narration: ctx.input.name + ' stack is created and placed in ' + destination.name + '.', effects: [
  { type: 'entity.create', entityId: ctx.input.itemId, name: ctx.input.name },
  { type: 'component.add', entityId: ctx.input.itemId, definitionId: 'dnd2024.item-instance', data: JSON.stringify({ definitionId: definition.id }) },
  { type: 'component.add', entityId: ctx.input.itemId, definitionId: 'dnd2024.item-quantity', data: JSON.stringify({ count: ctx.input.count, stackKey: definition.id }) },
  { type: 'containment.move', entityId: ctx.input.itemId, toEntityId: destination.id, slot: ctx.input.slot }
], data: { itemId: ctx.input.itemId, definitionId: definition.id, stackKey: definition.id, count: ctx.input.count, destinationId: destination.id, slot: ctx.input.slot } };
