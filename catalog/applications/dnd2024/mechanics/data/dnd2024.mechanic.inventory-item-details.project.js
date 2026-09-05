// Only declared, authored values are formatted here. Derived rules keep their existing owners.
var observer = ctx.authorizedObserver, subject = ctx.roles.subject, campaign = ctx.roles.campaign;
function object(value) { return value !== null && typeof value === 'object' && !Array.isArray(value); }
function fail() { throw new Error('Item details source is unavailable.'); }
function text(value, limit) { if (typeof value !== 'string' || !value.trim() || value.length > limit) fail(); return value; }
function read(components, key) {
    if (!components || !Object.prototype.hasOwnProperty.call(components, key)) return null;
    var value = JSON.parse(components[key]);
    if (!object(value)) fail();
    return value;
}
function ref(value) { return object(value) && typeof value.entityId === 'string' && value.entityId.length > 0 && value.entityId.length <= 200; }
if (!observer || observer.version !== 1 || !subject || !campaign || observer.observerId !== subject.id ||
    observer.campaignId !== campaign.id || !ctx.audience || observer.perspective !== ctx.audience.perspective ||
    !object(ctx.input) || Object.keys(ctx.input).join(',') !== 'itemId') fail();
var itemId = text(ctx.input.itemId, 200), dm = observer.perspective === 'dm';
if (['player', 'dm'].indexOf(observer.perspective) < 0) fail();
var item = null, parent = null, count = 0;
function find(nodes, owner, depth) {
    if (!Array.isArray(nodes)) fail();
    for (var i = 0; i < nodes.length; i++) {
        if (++count > 512 || depth > 4) fail();
        if (nodes[i].id === itemId) { if (item) fail(); item = nodes[i]; parent = owner; }
        find(nodes[i].contains || [], nodes[i], depth + 1);
    }
}
find(subject.contains || [], subject, 1);
if (!item) fail();
var states = Object.create(null);
if (!Array.isArray(observer.knowledge) || !observer.knowledgeComplete) fail();
observer.knowledge.forEach(function (entry) {
    if (Object.prototype.hasOwnProperty.call(states, entry.knowledgeId)) fail();
    states[entry.knowledgeId] = entry.state;
});
function state(id) { return states[id] || 'unknown'; }
var link = read(item.components, 'dnd2024.core.definition-link'), definition = null, definitionId = null;
if (link) {
    if (!ref(link.definition)) fail();
    definitionId = link.definition.entityId;
    definition = ctx.references && ctx.references[definitionId];
    if (!definition || definition.id !== definitionId) fail();
}
var discovery = read(item.components, 'dnd2024.magic-item.knowledge');
// Discovery context must have been observer-validated by the host. This boolean can
// restrict identity, but knowing an identity alone never reveals every property.
var identity = dm || state(itemId) === 'known' && (!discovery || discovery.identityKnown === true);
var definitionVisible = dm || identity && state(definitionId) === 'known';
var result = { version: 1, observerId: subject.id, itemId: itemId, perspective: observer.perspective,
    state: 'ready', name: identity ? text(item.name, 160) : 'Item', description: null,
    definitionId: definitionVisible ? definitionId : null, quantity: null, container: null,
    equipmentSlots: [], properties: [], sources: [], media: [], reasons: [],
    observerKnowledge: dm ? state(itemId) : null };
function reason(value) { if (result.reasons.indexOf(value) < 0) result.reasons.push(value); }
function source(label, knowledgeState) { return { label: label, knowledgeState: knowledgeState || 'known' }; }
function property(label, value, unit, origin, comparison) {
    if (value === undefined || value === null) return;
    if (typeof value === 'string') text(value, 512);
    else if (typeof value !== 'boolean' && (typeof value !== 'number' || !Number.isFinite(value))) fail();
    if (result.properties.length >= 32) { reason('page-limit'); return; }
    result.properties.push({ label: label, value: value, unit: unit || null, sources: [origin],
        observerKnowledge: dm ? comparison || 'unknown' : null });
}
function integer(value, minimum) { if (!Number.isSafeInteger(value) || value < minimum) fail(); return value; }
function fraction(value) {
    if (!object(value)) fail();
    var numerator = integer(value.numerator, 0), denominator = integer(value.denominator, 1);
    // Retain exact authored fractions rather than rounding a measurement.
    return denominator === 1 ? numerator : String(numerator) + '/' + String(denominator);
}
var recorded = source('Item record'), defined = source('Item definition');
var quantity = read(item.components, 'dnd2024.item.quantity');
if (quantity) result.quantity = integer(quantity.current, 1);
else reason('source-incomplete');
if (!link) reason('source-incomplete');
if (!observer.inventoryComplete) reason('inventory-bound');
// These references are known to this observer, but do not themselves supply a
// displayable property/curse description. Do not present that dependent view as complete.
if (discovery && (discovery.curseKnown === true || Array.isArray(discovery.knownProperties) && discovery.knownProperties.length > 0))
    reason('dependency-unavailable');
// Other containers' identity is not selected-item knowledge. Never echo an unvalidated name.
if (dm || parent.id === subject.id) result.container = { itemId: parent.id, name: text(parent.name, 160), observerKnowledge: null };

// This finite presentation vocabulary is checked against the authored vocabulary fixtures.
var labels = {
  "dnd2024.equipment.armor-category.heavy": "Heavy",
  "dnd2024.equipment.armor-category.light": "Light",
  "dnd2024.equipment.armor-category.medium": "Medium",
  "dnd2024.equipment.armor-category.shield": "Shield",
  "dnd2024.equipment.weapon-category.martial": "Martial",
  "dnd2024.equipment.weapon-category.melee": "Melee",
  "dnd2024.equipment.weapon-category.ranged": "Ranged",
  "dnd2024.equipment.weapon-category.simple": "Simple",
  "dnd2024.equipment.weapon-mastery.cleave": "Cleave",
  "dnd2024.equipment.weapon-mastery.graze": "Graze",
  "dnd2024.equipment.weapon-mastery.nick": "Nick",
  "dnd2024.equipment.weapon-mastery.push": "Push",
  "dnd2024.equipment.weapon-mastery.sap": "Sap",
  "dnd2024.equipment.weapon-mastery.slow": "Slow",
  "dnd2024.equipment.weapon-mastery.topple": "Topple",
  "dnd2024.equipment.weapon-mastery.vex": "Vex",
  "dnd2024.equipment.weapon-property.ammunition": "Ammunition",
  "dnd2024.equipment.weapon-property.finesse": "Finesse",
  "dnd2024.equipment.weapon-property.heavy": "Heavy",
  "dnd2024.equipment.weapon-property.light": "Light",
  "dnd2024.equipment.weapon-property.loading": "Loading",
  "dnd2024.equipment.weapon-property.range": "Range",
  "dnd2024.equipment.weapon-property.reach": "Reach",
  "dnd2024.equipment.weapon-property.thrown": "Thrown",
  "dnd2024.equipment.weapon-property.two-handed": "Two-Handed",
  "dnd2024.equipment.weapon-property.versatile": "Versatile",
  "dnd2024.equipment-slot.main-hand": "Main Hand",
  "dnd2024.equipment.tool-category.artisan": "Artisan's Tools",
  "dnd2024.equipment.tool-category.gaming-set": "Gaming Set",
  "dnd2024.equipment.tool-category.musical-instrument": "Musical Instrument",
  "dnd2024.equipment.tool-category.other": "Other Tool",
  "dnd2024.vocabulary.ability.charisma": "Charisma",
  "dnd2024.vocabulary.ability.constitution": "Constitution",
  "dnd2024.vocabulary.ability.dexterity": "Dexterity",
  "dnd2024.vocabulary.ability.intelligence": "Intelligence",
  "dnd2024.vocabulary.ability.strength": "Strength",
  "dnd2024.vocabulary.ability.wisdom": "Wisdom",
  "dnd2024.vocabulary.distance-unit.foot": "Foot",
  "dnd2024.vocabulary.distance-unit.kilometer": "Kilometer",
  "dnd2024.vocabulary.distance-unit.meter": "Meter",
  "dnd2024.vocabulary.distance-unit.mile": "Mile",
  "dnd2024.vocabulary.mass-unit.kilogram": "Kilogram",
  "dnd2024.vocabulary.mass-unit.pound": "Pound",
  "dnd2024.vocabulary.volume-unit.liter": "Liter"
};
function label(value) {
    if (!ref(value)) fail();
    if (!Object.prototype.hasOwnProperty.call(labels, value.entityId)) { reason('source-incomplete'); return null; }
    return labels[value.entityId];
}
function referenceProperty(title, value, origin, comparison) {
    if (value !== undefined) property(title, label(value), null, origin, comparison);
}
function measure(title, value, dimension, origin, comparison) {
    if (value === undefined) return;
    if (!object(value) || value.dimension !== dimension) fail();
    var unit = label(value.unit);
    if (unit) property(title, fraction(value.value), unit, origin, comparison);
}
var equipment = read(item.components, 'dnd2024.item.equipment');
if (equipment) {
    if (!ref(equipment.equippedBy) || equipment.equippedBy.entityId !== subject.id || !Array.isArray(equipment.slots)) fail();
    equipment.slots.forEach(function (slot) {
        var value = label(slot);
        if (value && result.equipmentSlots.indexOf(value) < 0) {
            if (result.equipmentSlots.length >= 16) reason('page-limit'); else result.equipmentSlots.push(value);
        }
    });
}
if (identity) result.sources.push(recorded);
if (definition && definitionVisible) {
    var c = definition.components, comparison = state(definitionId);
    var version = read(c, 'dnd2024.core.version'), legacy = read(c, 'dnd2024.item-definition');
    if (version) {
        integer(version.revision, 1);
        if (['active', 'deprecated', 'superseded', 'archived'].indexOf(version.status) < 0) fail();
    } else if (!legacy || legacy.definitionVersion !== 1) fail();
    result.sources.push(defined);
    var physical = read(c, 'dnd2024.item.physical');
    if (physical) {
        measure('Weight per item', physical.weight, 'mass', defined, comparison);
        ['length', 'width', 'height'].forEach(function (key) {
            measure(key.charAt(0).toUpperCase() + key.slice(1), physical[key], 'distance', defined, comparison);
        });
        measure('Volume', physical.volume, 'volume', defined, comparison);
        referenceProperty('Material', physical.material, defined, comparison);
    } else if (legacy) {
        property('Weight per item', fraction(legacy.massPounds), 'Pound', defined, comparison);
        if (legacy.lengthFeet) property('Length', fraction(legacy.lengthFeet), 'Foot', defined, comparison);
    }
    if (legacy) {
        var kinds = { 'adventuring-gear': 'Adventuring gear', ammunition: 'Ammunition', weapon: 'Weapon', armor: 'Armor', shield: 'Shield', currency: 'Currency' };
        if (!Object.prototype.hasOwnProperty.call(kinds, legacy.kind)) fail();
        property('Kind', kinds[legacy.kind], null, defined, comparison);
        // Legacy armor metadata already owns these scalars; it is not a current AC calculation.
        if (!read(c, 'dnd2024.item.armor') && legacy.armorProfile) {
            var armor = legacy.armorProfile;
            property('Base armor class', armor.baseArmorClass, null, defined, comparison);
            property('Armor class bonus', armor.armorClassBonus, null, defined, comparison);
            property('Strength minimum', armor.strengthMinimum, null, defined, comparison);
            property('Stealth disadvantage', armor.stealthDisadvantage, null, defined, comparison);
        }
    }
    var weapon = read(c, 'dnd2024.item.weapon');
    if (weapon) {
        referenceProperty('Weapon category', weapon.category, defined, comparison);
        (weapon.properties || []).forEach(function (value) { referenceProperty('Weapon property', value, defined, comparison); });
        referenceProperty('Mastery property', weapon.masteryProperty, defined, comparison);
    }
    var armorDefinition = read(c, 'dnd2024.item.armor');
    if (armorDefinition) {
        referenceProperty('Armor category', armorDefinition.category, defined, comparison);
        // A mechanic binding is not a scalar armor-class value. Bounded child evaluation
        // is deliberately unavailable for authorized projections, so do not execute it.
        reason('dependency-unavailable');
    }
    var container = read(c, 'dnd2024.item.container');
    if (container) {
        measure('Maximum contents weight', container.maximumWeight, 'mass', defined, comparison);
        if (container.volumeCapacities) ['any', 'liquid', 'dryGoods'].forEach(function (key) {
            measure('Capacity (' + key + ')', container.volumeCapacities[key], 'volume', defined, comparison);
        });
        if (container.contentRequirement || container.containmentEffects) reason('dependency-unavailable');
    } else if (legacy && legacy.capacity) {
        if (legacy.capacity.weightPounds) property('Maximum contents weight', fraction(legacy.capacity.weightPounds), 'Pound', defined, comparison);
        if (legacy.capacity.volumeCubicFeet) property('Capacity', fraction(legacy.capacity.volumeCubicFeet), 'Cubic foot', defined, comparison);
        property('Maximum item count', legacy.capacity.itemCount, null, defined, comparison);
    }
    var equippable = read(c, 'dnd2024.item.equippable');
    if (equippable) {
        if (typeof equippable.requiredHands === 'number') property('Required hands', integer(equippable.requiredHands, 0), null, defined, comparison);
        else if (equippable.requiredHands) reason('dependency-unavailable');
        if (equippable.equipRequirement || equippable.equippedEffects) reason('dependency-unavailable');
    }
    var consumable = read(c, 'dnd2024.item.consumable'), ammunition = read(c, 'dnd2024.item.ammunition'), tool = read(c, 'dnd2024.item.tool');
    if (consumable) property('Units consumed per use', integer(consumable.unitsConsumedPerUse, 1), null, defined, comparison);
    if (ammunition) {
        property('Units per bundle', integer(ammunition.unitsPerBundle, 1), null, defined, comparison);
        reason('dependency-unavailable');
    }
    if (tool) {
        referenceProperty('Tool category', tool.category, defined, comparison);
        (tool.associatedAbilities || []).forEach(function (value) { referenceProperty('Associated ability', value, defined, comparison); });
    }
}
// Private state is never inferred from identity or a known statement. The host currently
// hydrates these raw components only for DM; selected-character facts below are independent.
if (dm) {
    var durability = read(item.components, 'dnd2024.object.durability');
    if (durability) {
        property('Current hit points', integer(durability.currentHitPoints, 0), null, recorded, state(itemId));
        property('Temporary hit points', durability.temporaryHitPoints, null, recorded, state(itemId));
        property('Destroyed', durability.destroyed, null, recorded, state(itemId));
    }
    var attunement = read(item.components, 'dnd2024.magic-item.attunement');
    if (attunement) property('Attunement active', attunement.active, null, recorded, 'unknown');
    if (read(item.components, 'dnd2024.magic-item.charges') || read(item.components, 'dnd2024.magic-item.curse')) reason('dependency-unavailable');
}
// Instance art wins for each role. Only illustration/icon roles may be inherited;
// a definition handout or scene is not automatically an image of the carried instance.
var mediaRoles = Object.create(null);
function addMedia(components, inherited) {
    var gallery = read(components, 'authorized-media');
    if (!gallery || !Array.isArray(gallery.attachments)) return;
    var inheritedRoles = { illustration: true, icon: true };
    gallery.attachments.forEach(function (image) {
        if (inherited && (!inheritedRoles[image.role] || mediaRoles[image.role])) return;
        if (typeof image.contentUrl !== 'string' || !/^\/api\/read-model-media\/[a-f0-9]{64}\/content$/.test(image.contentUrl) ||
            typeof image.alt !== 'string' || !image.alt.trim() || image.alt.length > 240 ||
            typeof image.caption !== 'string' || image.caption.length > 240) return;
        if (result.media.length >= 8) { reason('page-limit'); return; }
        result.media.push({ contentUrl: image.contentUrl, alt: image.alt, caption: image.caption || null });
        if (!inherited) mediaRoles[image.role] = true;
    });
}
if (identity) addMedia(item.components, false);
if (definition && definitionVisible) addMedia(definition.components, true);

Object.keys(ctx.references || {}).sort().forEach(function (id) {
    var statement = read(ctx.references[id].components, 'authorized-knowledge');
    if (!statement) return;
    if (statement.subjectId !== itemId && statement.subjectId !== definitionId) fail();
    if (!dm && ['known', 'suspected', 'believed', 'doubted', 'disbelieved'].indexOf(state(id)) < 0) return;
    var certainty = dm ? 'known' : state(id);
    var origin = source(statement.presentationKind === 'rumour' ? 'Recorded rumour' : statement.presentationKind === 'evidence' ? 'Recorded evidence' : 'Recorded statement', certainty);
    var prose = text(statement.displayText, 2048);
    // Keep assertion provenance adjacent to prose; DM comparison does not turn rumour into fact.
    if (prose.length > 512) {
        if (result.description !== null) fail();
        result.description = prose;
        if (result.sources.length >= 8) fail();
        result.sources.push(origin);
    } else property('Recorded knowledge', prose, null, origin, state(id));
});
function bytes(value) {
    // Native encoding avoids spending sandbox statement budget once per text character.
    return encodeURIComponent(JSON.stringify(value)).replace(/%[0-9A-F]{2}/g, 'x').length;
}
if (result.reasons.length) result.state = 'partial';
while (bytes(result) > 65536 && result.properties.length) {
    result.properties.pop(); reason('byte-limit'); result.state = 'partial';
}
if (bytes(result) > 65536) fail();
return { narration: 'Authorized item details.', effects: [], events: [], notifications: [], data: result };
