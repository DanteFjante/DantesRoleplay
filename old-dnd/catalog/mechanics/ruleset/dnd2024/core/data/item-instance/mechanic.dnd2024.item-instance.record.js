var item = ctx.roles.item;
var definition = ctx.roles.definition;

function isEmptyObject(value) {
  return value !== null && !Array.isArray(value) && typeof value === 'object' && Object.keys(value).length === 0;
}

if (!isEmptyObject(ctx.input)) throw new Error('This action takes no input; select the item entity and its immutable definition role.');
if (item.components['dnd2024.item-definition']) throw new Error('A catalog definition entity cannot become a physical item instance.');
if (item.components['dnd2024.item-instance']) throw new Error('This entity is already a physical item instance; definition identity cannot be corrected in place.');
if (!definition.components['dnd2024.item-definition']) throw new Error('The definition role lacks a valid item definition.');

return {
  narration: item.name + ' is recorded as an instance of ' + definition.name + '.',
  effects: [{ type: 'component.add', entityId: item.id, definitionId: 'dnd2024.item-instance', data: JSON.stringify({ definitionId: definition.id }) }],
  data: { itemId: item.id, definitionId: definition.id }
};
