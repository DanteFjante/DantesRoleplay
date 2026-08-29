var item = ctx.roles.item;
var destination = ctx.roles.destination;

if (ctx.input === null || Array.isArray(ctx.input) || typeof ctx.input !== 'object' || Object.keys(ctx.input).length !== 1 || typeof ctx.input.slot !== 'string' || ctx.input.slot.trim() !== ctx.input.slot || ctx.input.slot.length > 100) {
  throw new Error('Input must contain exactly a trimmed slot string of at most 100 characters.');
}
var raw = item.components['dnd2024.item-instance'];
if (!raw) throw new Error('The item role is not a physical item instance.');
var instance;
try { instance = JSON.parse(raw); } catch (error) { throw new Error('The item instance component is corrupt.'); }
if (instance === null || Array.isArray(instance) || typeof instance !== 'object' || Object.keys(instance).length !== 1 || typeof instance.definitionId !== 'string') throw new Error('The item instance component is corrupt.');

return {
  narration: item.name + ' is moved to ' + destination.name + '.',
  effects: [{ type: 'containment.move', entityId: item.id, toEntityId: destination.id, slot: ctx.input.slot }],
  data: { itemId: item.id, definitionId: instance.definitionId, previousContainerId: item.containerId || null, destinationId: destination.id, slot: ctx.input.slot }
};
