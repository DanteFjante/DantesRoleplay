var definition = ctx.roles.definition;
var destination = ctx.roles.destination;

function hasOnly(value, keys) {
  if (value === null || Array.isArray(value) || typeof value !== 'object') return false;
  var actual = Object.keys(value).sort();
  if (actual.length !== keys.length) return false;
  for (var i = 0; i < keys.length; i++) if (actual[i] !== keys[i]) return false;
  return true;
}

function validId(value) { return typeof value === 'string' && /^[a-z][a-z0-9.-]{0,199}$/.test(value); }
function validText(value, maximum) { return typeof value === 'string' && value.trim() === value && value.length > 0 && value.length <= maximum; }

if (!hasOnly(ctx.input, ['itemId', 'name', 'slot'])) throw new Error('Input must contain exactly itemId, name, and slot.');
if (!validId(ctx.input.itemId)) throw new Error('itemId must be a permanent lower-case dotted or hyphenated identifier of at most 200 characters.');
if (!validText(ctx.input.name, 400)) throw new Error('name must be trimmed, non-empty, and at most 400 characters.');
if (typeof ctx.input.slot !== 'string' || ctx.input.slot.trim() !== ctx.input.slot || ctx.input.slot.length > 100) throw new Error('slot must be a trimmed string of at most 100 characters.');
if (!definition.components['dnd2024.item-definition']) throw new Error('The definition role lacks a valid item definition.');

return {
  narration: ctx.input.name + ' is created and placed in ' + destination.name + '.',
  effects: [
    { type: 'entity.create', entityId: ctx.input.itemId, name: ctx.input.name },
    { type: 'component.add', entityId: ctx.input.itemId, definitionId: 'dnd2024.item-instance', data: JSON.stringify({ definitionId: definition.id }) },
    { type: 'containment.move', entityId: ctx.input.itemId, toEntityId: destination.id, slot: ctx.input.slot }
  ],
  data: { itemId: ctx.input.itemId, definitionId: definition.id, destinationId: destination.id, slot: ctx.input.slot }
};
