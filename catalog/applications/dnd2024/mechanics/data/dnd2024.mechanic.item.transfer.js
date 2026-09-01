var item = ctx.roles.item;
var source = ctx.roles.source;
var destination = ctx.roles.destination;
var LINK = 'dnd2024.core.definition-link';
var QUANTITY = 'dnd2024.item.quantity';
var EQUIPMENT = 'dnd2024.item.equipment';
var PHYSICAL = 'dnd2024.item.physical';
var CONTAINER = 'dnd2024.item.container';
var KILOGRAM = 'dnd2024.vocabulary.mass-unit.kilogram';
var MAX = Number.MAX_SAFE_INTEGER;

function object(value) {
    return value !== null && !Array.isArray(value) && typeof value === 'object';
}

function parse(raw, label) {
    try {
        var value = JSON.parse(raw);
        if (!object(value)) throw 0;
        return value;
    } catch (error) {
        throw new Error(label + ' is invalid.');
    }
}

function reference(value) {
    if (!object(value)
        || typeof value.entityId !== 'string'
        || value.entityId.length < 1
        || value.entityId.length > 200) return false;
    var keys = Object.keys(value).sort().join(',');
    return keys === 'entityId'
        || keys === 'entityId,expectedArchetype' && typeof value.expectedArchetype === 'string';
}

function definitionId(node) {
    var value = parse(node.components[LINK], 'Definition link');
    var keys = Object.keys(value).sort().join(',');
    if ((keys !== 'definition' && keys !== 'definition,definitionRevision')
        || !reference(value.definition)
        || value.definitionRevision !== undefined
            && (!Number.isSafeInteger(value.definitionRevision) || value.definitionRevision < 1)) {
        throw new Error('Definition link is invalid.');
    }
    return value.definition.entityId;
}

function rational(value, label) {
    if (!object(value)
        || !Number.isSafeInteger(value.numerator)
        || value.numerator < 0
        || !Number.isSafeInteger(value.denominator)
        || value.denominator < 1) throw new Error(label + ' is invalid.');
    var divisor = gcd(value.numerator, value.denominator);
    return { numerator: value.numerator / divisor, denominator: value.denominator / divisor };
}

function gcd(left, right) {
    while (right) {
        var next = left % right;
        left = right;
        right = next;
    }
    return left;
}

function safeMultiply(left, right) {
    if (left !== 0 && right > Math.floor(MAX / left)) {
        throw new Error('Capacity arithmetic exceeds the safe range.');
    }
    return left * right;
}

function mass(value, label) {
    if (!object(value)
        || !object(value.weight)
        || value.weight.dimension !== 'mass'
        || !reference(value.weight.unit)
        || value.weight.unit.entityId !== KILOGRAM
        || Object.keys(value.weight).sort().join(',') !== 'dimension,unit,value') {
        throw new Error(label + ' must provide one canonical kilogram weight.');
    }
    return rational(value.weight.value, label + ' weight');
}

function multiply(value, count) {
    var divisor = gcd(count, value.denominator);
    return rational({
        numerator: safeMultiply(value.numerator, count / divisor),
        denominator: value.denominator / divisor
    }, 'Item mass');
}

function add(left, right) {
    var common = gcd(left.denominator, right.denominator);
    var leftFactor = right.denominator / common;
    var rightFactor = left.denominator / common;
    var leftNumerator = safeMultiply(left.numerator, leftFactor);
    var rightNumerator = safeMultiply(right.numerator, rightFactor);
    if (rightNumerator > MAX - leftNumerator) {
        throw new Error('Capacity arithmetic exceeds the safe range.');
    }
    return rational({
        numerator: leftNumerator + rightNumerator,
        denominator: safeMultiply(left.denominator, leftFactor)
    }, 'Combined item mass');
}

function exceeds(left, right) {
    var leftProduct = left.numerator * right.denominator;
    var rightProduct = right.numerator * left.denominator;
    if (!Number.isSafeInteger(leftProduct) || !Number.isSafeInteger(rightProduct)) {
        throw new Error('Capacity arithmetic exceeds the safe range.');
    }
    return leftProduct > rightProduct;
}

function contains(node, entityId) {
    var children = node.contains || [];
    for (var index = 0; index < children.length; index++) {
        if (children[index].id === entityId || contains(children[index], entityId)) return true;
    }
    return false;
}

function facts(node) {
    var linkedId = definitionId(node);
    var definition = ctx.references && ctx.references[linkedId];
    if (!definition || definition.id !== linkedId || !definition.components) {
        throw new Error('Item definition is unavailable.');
    }
    var quantity = parse(node.components[QUANTITY], 'Item quantity');
    if (Object.keys(quantity).length !== 1
        || !Number.isSafeInteger(quantity.current)
        || quantity.current < 1) throw new Error('Item quantity is invalid.');
    var unitMass = mass(parse(definition.components[PHYSICAL], 'Item physical definition'),
        'Item physical definition');
    return {
        definitionId: linkedId,
        definition: definition,
        quantity: quantity.current,
        mass: multiply(unitMass, quantity.current)
    };
}

if (!object(ctx.input)
    || Object.keys(ctx.input).length !== 1
    || typeof ctx.input.slot !== 'string'
    || ctx.input.slot.trim() !== ctx.input.slot
    || ctx.input.slot.length < 1
    || ctx.input.slot.length > 100) {
    throw new Error('Input must contain exactly a trimmed slot.');
}
if (item.containerId !== source.id) throw new Error('Source does not directly contain item.');
if (item.id === destination.id || source.id === destination.id || contains(item, destination.id)) {
    throw new Error('Transfer destination is invalid.');
}
if (Object.prototype.hasOwnProperty.call(item.components, EQUIPMENT)) {
    throw new Error('Equipped item must be unequipped before transfer.');
}

var moving = facts(item);
if (Object.prototype.hasOwnProperty.call(destination.components, LINK)) {
    var destinationFacts = facts(destination);
    if (!Object.prototype.hasOwnProperty.call(destinationFacts.definition.components, CONTAINER)) {
        throw new Error('Destination is not an ordinary container.');
    }
    var container = parse(destinationFacts.definition.components[CONTAINER],
        'Destination container definition');
    if (container.contentRequirement
        || Array.isArray(container.containmentEffects) && container.containmentEffects.length > 0) {
        throw new Error('Destination requires unsupported containment rule evaluation.');
    }
    var totalMass = { numerator: 0, denominator: 1 };
    var children = destination.contains || [];
    for (var childIndex = 0; childIndex < children.length; childIndex++) {
        if (children[childIndex].id !== item.id) {
            totalMass = add(totalMass, facts(children[childIndex]).mass);
        }
    }
    totalMass = add(totalMass, moving.mass);
    if (container.maximumWeight) {
        var maximum = mass({ weight: container.maximumWeight }, 'Container maximum weight');
        if (exceeds(totalMass, maximum)) throw new Error('Destination weight capacity exceeded.');
    }
}

return {
    narration: item.name + ' is transferred from ' + source.name + ' to ' + destination.name + '.',
    effects: [{
        type: 'containment.move',
        entityId: item.id,
        toEntityId: destination.id,
        slot: ctx.input.slot
    }],
    events: [],
    notifications: [],
    data: {
        test: 'item-transfer',
        itemId: item.id,
        definitionId: moving.definitionId,
        sourceId: source.id,
        destinationId: destination.id,
        slot: ctx.input.slot
    }
};
