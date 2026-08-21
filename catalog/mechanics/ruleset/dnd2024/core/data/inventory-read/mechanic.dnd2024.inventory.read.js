var root = ctx.roles.root;

function empty(value) { return value !== null && !Array.isArray(value) && typeof value === 'object' && Object.keys(value).length === 0; }
function parse(raw, message) { try { var value = JSON.parse(raw); } catch (error) { throw new Error(message); } if (value === null || Array.isArray(value) || typeof value !== 'object') throw new Error(message); return value; }
function positive(value) { return typeof value === 'number' && Number.isSafeInteger(value) && value > 0; }
function state(raw) { if (!raw) return null; var value = parse(raw, 'An item equipment state is corrupt.'); if (Object.keys(value).length !== 1 || ['held', 'worn', 'unequipped'].indexOf(value.state) < 0) throw new Error('An item equipment state is corrupt.'); return value.state; }
function visit(node, depth) {
  var rawInstance = node.components && node.components['dnd2024.item-instance'];
  if (rawInstance) {
    var instance = parse(rawInstance, 'An item instance is corrupt.');
    if (typeof instance.definitionId !== 'string') throw new Error('An item instance lacks definitionId.');
    var referenced = ctx.references[instance.definitionId];
    if (!referenced || !referenced.components || !referenced.components['dnd2024.item-definition']) throw new Error('The item definition reference is unavailable.');
    var definition = parse(referenced.components['dnd2024.item-definition'], 'An item definition is corrupt.');
    if (typeof definition.kind !== 'string' || (definition.stackPolicy !== 'separate' && definition.stackPolicy !== 'fungible')) throw new Error('An item definition is invalid.');
    var quantity = null;
    var rawQuantity = node.components['dnd2024.item-quantity'];
    if (definition.stackPolicy === 'fungible') {
      if (!rawQuantity) throw new Error('A fungible item stack requires an explicit quantity.');
      var stack = parse(rawQuantity, 'An item quantity is corrupt.');
      if (!positive(stack.count) || stack.stackKey !== instance.definitionId) throw new Error('An item quantity is incompatible with its immutable definition.');
      quantity = stack.count;
    } else if (rawQuantity) throw new Error('A separate item definition cannot carry a stack quantity.');
    items.push({ itemId: node.id, name: node.name, definitionId: instance.definitionId, kind: definition.kind, quantity: quantity, equipmentState: state(node.components['dnd2024.equipment-state']), slot: node.slot || '', depth: depth });
  } else {
    unclassifiedContents.push({ entityId: node.id, name: node.name, slot: node.slot || '', depth: depth });
  }
  var children = node.contains || [];
  for (var i = 0; i < children.length; i++) visit(children[i], depth + 1);
}

if (!empty(ctx.input)) throw new Error('This action takes no input; select one custody root.');
var items = [];
var unclassifiedContents = [];
var roots = root.contains || [];
for (var j = 0; j < roots.length; j++) visit(roots[j], 1);
return { narration: 'Bounded physical inventory beneath ' + root.name + ' contains ' + items.length + ' visible items.', effects: [], data: { rootId: root.id, items: items, unclassifiedContents: unclassifiedContents, contentsDepth: 4, mayOmitDeeperContents: true } };
