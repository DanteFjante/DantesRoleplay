var item = ctx.roles.item;
if (ctx.input === null || Array.isArray(ctx.input) || typeof ctx.input !== 'object' || Object.keys(ctx.input).length !== 0) throw new Error('This action takes no input.');
var raw = item.components['dnd2024.item-instance'];
if (!raw) throw new Error('The item role is not a physical item instance.');
var instance;
try { instance = JSON.parse(raw); } catch (error) { throw new Error('The item instance component is corrupt.'); }
if (instance === null || Array.isArray(instance) || typeof instance !== 'object' || Object.keys(instance).length !== 1 || typeof instance.definitionId !== 'string') throw new Error('The item instance component is corrupt.');

return {
  narration: item.name + ' is an instance of ' + instance.definitionId + '.',
  effects: [],
  data: { itemId: item.id, definitionId: instance.definitionId, containerId: item.containerId || null, slot: item.containerSlot }
};
