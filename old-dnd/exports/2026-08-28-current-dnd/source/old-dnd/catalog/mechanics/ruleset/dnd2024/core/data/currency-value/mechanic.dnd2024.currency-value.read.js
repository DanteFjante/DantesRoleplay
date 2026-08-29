var root = ctx.roles.root;
var MAX = Number.MAX_SAFE_INTEGER;
var ORDER = ['cp', 'sp', 'ep', 'gp', 'pp'];

function empty(value) { return value !== null && !Array.isArray(value) && typeof value === 'object' && Object.keys(value).length === 0; }
function parse(raw, message) { try { var value = JSON.parse(raw); } catch (error) { throw new Error(message); } if (value === null || Array.isArray(value) || typeof value !== 'object') throw new Error(message); return value; }
function positive(value) { return typeof value === 'number' && Number.isSafeInteger(value) && value > 0; }
function add(left, right) { if (left > MAX - right) throw new Error('Currency value exceeds the supported exact range.'); return left + right; }
function multiply(left, right) { if (left > Math.floor(MAX / right)) throw new Error('Currency value exceeds the supported exact range.'); return left * right; }
function inspect(node) {
  var rawInstance = node.components && node.components['dnd2024.item-instance'];
  if (rawInstance) {
    var instance = parse(rawInstance, 'An item instance is corrupt.');
    if (typeof instance.definitionId !== 'string') throw new Error('An item instance lacks definitionId.');
    var referenced = ctx.references[instance.definitionId];
    if (!referenced || !referenced.components || !referenced.components['dnd2024.item-definition']) throw new Error('The item definition reference is unavailable.');
    var definition = parse(referenced.components['dnd2024.item-definition'], 'An item definition is corrupt.');
    if (definition.kind === 'currency') {
      var currency = definition.currency;
      if (definition.stackPolicy !== 'fungible' || !currency || ORDER.indexOf(currency.denomination) < 0 || !positive(currency.copperValue) || currency.coinsPerPound !== 50) throw new Error('A currency definition is invalid.');
      var rawQuantity = node.components['dnd2024.item-quantity'];
      if (!rawQuantity) throw new Error('A physical currency stack requires an explicit quantity.');
      var quantity = parse(rawQuantity, 'A currency quantity is corrupt.');
      if (!positive(quantity.count) || quantity.stackKey !== instance.definitionId) throw new Error('A currency quantity is incompatible with its immutable definition.');
      var denomination = currency.denomination;
      totals[denomination].count = add(totals[denomination].count, quantity.count);
      totals[denomination].copperValue = currency.copperValue;
      totalCoins = add(totalCoins, quantity.count);
      totalCopperValue = add(totalCopperValue, multiply(quantity.count, currency.copperValue));
    }
  }
  var children = node.contains || [];
  for (var i = 0; i < children.length; i++) inspect(children[i]);
}

if (!empty(ctx.input)) throw new Error('This action takes no input; select one custody root.');
var totals = {};
for (var j = 0; j < ORDER.length; j++) totals[ORDER[j]] = { denomination: ORDER[j], count: 0, copperValue: 0, totalCopperValue: 0 };
var totalCoins = 0;
var totalCopperValue = 0;
inspect(root);
var denominations = [];
for (var k = 0; k < ORDER.length; k++) {
  var row = totals[ORDER[k]];
  if (row.count > 0) { row.totalCopperValue = multiply(row.count, row.copperValue); denominations.push(row); }
}
return { narration: 'Physical currency beneath ' + root.name + ' is worth ' + totalCopperValue + ' copper pieces.', data: { rootId: root.id, coinCount: totalCoins, copperValue: totalCopperValue, denominations: denominations, boundedDepth: 4 }, effects: [] };
