var source = ctx.roles.source;
var target = ctx.roles.target;
var definition = ctx.roles.definition;

function empty(value) { return value !== null && !Array.isArray(value) && typeof value === 'object' && Object.keys(value).length === 0; }
function positive(value) { return typeof value === 'number' && Number.isSafeInteger(value) && value > 0; }
function parse(raw, message) { try { var value = JSON.parse(raw); } catch (error) { throw new Error(message); } if (value === null || Array.isArray(value) || typeof value !== 'object') throw new Error(message); return value; }
function stack(role, label) { var instance = parse(role.components['dnd2024.item-instance'], 'The ' + label + ' role is not a valid physical item instance.'); var quantity = parse(role.components['dnd2024.item-quantity'], 'The ' + label + ' role lacks a valid item quantity.'); if (!positive(quantity.count) || instance.definitionId !== definition.id || quantity.stackKey !== definition.id) throw new Error('The ' + label + ' stack does not match the selected immutable definition.'); if (role.contains && role.contains.length > 0) throw new Error('A stack with direct contents cannot be merged.'); return quantity; }

if (!empty(ctx.input)) throw new Error('This action takes no input; select source, target, and immutable definition roles.');
if (source.id === target.id) throw new Error('Source and target must be distinct stacks.');
var definitionData = parse(definition.components['dnd2024.item-definition'], 'The definition role lacks a valid item definition.');
if (definitionData.stackPolicy !== 'fungible') throw new Error('The selected definition is not fungible.');
var sourceQuantity = stack(source, 'source');
var targetQuantity = stack(target, 'target');
if ((source.containerId || null) !== (target.containerId || null)) throw new Error('Stacks must share the same direct container before merging.');
if (!positive(sourceQuantity.count + targetQuantity.count)) throw new Error('The merged count exceeds the supported safe-integer range.');

return { narration: source.name + ' is merged into ' + target.name + '.', effects: [
  { type: 'component.set', entityId: target.id, definitionId: 'dnd2024.item-quantity', data: JSON.stringify({ count: sourceQuantity.count + targetQuantity.count, stackKey: definition.id }) },
  { type: 'entity.delete', entityId: source.id }
], data: { sourceItemId: source.id, targetItemId: target.id, definitionId: definition.id, stackKey: definition.id, previousSourceCount: sourceQuantity.count, previousTargetCount: targetQuantity.count, count: sourceQuantity.count + targetQuantity.count } };
