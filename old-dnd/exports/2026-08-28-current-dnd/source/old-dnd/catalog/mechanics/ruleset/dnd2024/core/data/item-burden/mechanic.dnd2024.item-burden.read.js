var root = ctx.roles.root;
var MAX = Number.MAX_SAFE_INTEGER;

function empty(value) { return value !== null && !Array.isArray(value) && typeof value === 'object' && Object.keys(value).length === 0; }
function parse(raw, message) { try { var value = JSON.parse(raw); } catch (error) { throw new Error(message); } if (value === null || Array.isArray(value) || typeof value !== 'object') throw new Error(message); return value; }
function integer(value, minimum) { return typeof value === 'number' && Number.isSafeInteger(value) && value >= minimum; }
function gcd(a, b) { while (b !== 0) { var next = a % b; a = b; b = next; } return a; }
function rational(n, d) { if (!integer(n, 0) || !integer(d, 1)) throw new Error('An item definition has an invalid physical mass.'); var divisor = gcd(n, d); return { numerator: n / divisor, denominator: d / divisor }; }
function add(a, b) { if (a.numerator > MAX / b.denominator || b.numerator > MAX / a.denominator || a.denominator > MAX / b.denominator) throw new Error('Burden arithmetic exceeds the supported exact range.'); return rational(a.numerator * b.denominator + b.numerator * a.denominator, a.denominator * b.denominator); }
function multiply(a, count) { if (a.numerator > MAX / count) throw new Error('Burden arithmetic exceeds the supported exact range.'); return rational(a.numerator * count, a.denominator); }
function measure(node, isRoot) {
  var total = rational(0, 1);
  var raw = node.components && node.components['dnd2024.item-instance'];
  if (raw) {
    var instance = parse(raw, 'An item instance is corrupt.');
    if (typeof instance.definitionId !== 'string') throw new Error('An item instance lacks definitionId.');
    var target = ctx.references[instance.definitionId];
    if (!target || !target.components || !target.components['dnd2024.item-definition']) throw new Error('The item definition reference is unavailable.');
    var definition = parse(target.components['dnd2024.item-definition'], 'An item definition is corrupt.');
    var mass = definition.massPounds;
    if (!mass || definition.stackPolicy !== 'separate' && definition.stackPolicy !== 'fungible') throw new Error('An item definition lacks a valid mass or stack policy.');
    var count = 1;
    var quantityRaw = node.components['dnd2024.item-quantity'];
    if (quantityRaw) {
      var quantity = parse(quantityRaw, 'An item quantity is corrupt.');
      if (definition.stackPolicy !== 'fungible' || !integer(quantity.count, 1) || quantity.stackKey !== instance.definitionId) throw new Error('An item quantity is incompatible with its immutable definition.');
      count = quantity.count;
    } else if (definition.stackPolicy !== 'separate') throw new Error('A fungible item stack requires an explicit quantity.');
    var own = multiply(rational(mass.numerator, mass.denominator), count);
    total = add(total, own);
    items.push({ itemId: node.id, definitionId: instance.definitionId, quantity: count, selfMassPounds: own });
  } else if (!isRoot) throw new Error('A contained entity without a physical item instance cannot be treated as zero burden.');
  var children = node.contains || [];
  for (var i = 0; i < children.length; i++) total = add(total, measure(children[i], false));
  return total;
}

if (!empty(ctx.input)) throw new Error('This action takes no input; select one root entity.');
var items = [];
var massPounds = measure(root, true);
return { narration: 'Physical burden for ' + root.name + ' is ' + massPounds.numerator + '/' + massPounds.denominator + ' pounds.', data: { rootId: root.id, massPounds: massPounds, items: items }, effects: [] };
