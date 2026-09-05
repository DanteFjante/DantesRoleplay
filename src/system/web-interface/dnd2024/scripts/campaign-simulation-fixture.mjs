import { readFile } from 'node:fs/promises';
import { join } from 'node:path';

export const ids = Object.fromEntries(['world', 'origin', 'destination', 'actor', 'guide', 'campaign',
  'route', 'terrain', 'visibility', 'loot', 'backpack', 'materials', 'supplies', 'downtime', 'encounter', 'turn', 'hazard', 'hazard-definition']
  .map(name => [name, `slice19-${name}`]));
export const definitionFingerprint = 'A'.repeat(64);
export const privateMarkers = ['GM_PRIVATE_FACT', 'GM_CURSE_KNOWLEDGE', 'OTHER_CHARACTER_PRIVATE_KNOWLEDGE'];
export const ref = entityId => ({ entityId });
export const stateSpaceId = 'dnd2024-slice19';

// Reviewed synthetic initial conditions, not copied from a running campaign. All later changes
// must go through the public action/planning contracts; this manifest is used only for setup.
export async function fixture(repo) {
  const entities = [];
  const relationships = [];
  const edge = (fromEntityId, toEntityId, qualifiedKind) => relationships.push({ fromEntityId, toEntityId, qualifiedKind, expectedRevision: 0, value: {} });
  const entity = (id, name, components, container = ids.world, slot = 'fixture') => {
    entities.push({ entityId: id, name, expectedRevision: id === ids.world ? 1 : 0,
      components: Object.entries(components).map(([qualifiedTypeId, value]) => ({ qualifiedTypeId, expectedRevision: 0, value })),
      ...(id === ids.world ? {} : { containment: { containerEntityId: container, slot, expectedRevision: 0 } }) });
  };
  entity(ids.world, 'Slice 19 disposable world', {
    'game.core.world.root': { status: 'active', summary: 'A synthetic road between a camp and a watchtower.', visibility: 'party' },
    'game.core.world.clock': { calendarId: 'slice19-calendar', currentMinute: 100, revision: 1 },
  });
  for (const [id, name] of [[ids.origin, 'Roadside camp'], [ids.destination, 'Watchtower']])
    entity(id, name, { 'game.core.world.location': { kind: 'site', status: 'active', summary: name, visibility: 'party' } }, ids.world, 'location');
  entity(ids.actor, 'Mira the traveller', {
    'game.core.world.traveller': { status: 'active' },
    'dnd2024.creature.hit-points': { current: 20, maximum: 20 },
    'dnd2024.creature.ability-scores': { scores: Object.fromEntries(['strength', 'dexterity', 'constitution', 'intelligence', 'wisdom', 'charisma'].map(name => [`dnd2024.vocabulary.ability.${name}`, 10])) },
    'dnd2024.creature.proficiencies': { entries: {}, recordedFamilies: ['saving-throw', 'skill', 'weapon', 'armor-training', 'tool'] },
    'dnd2024.creature.defenses': { armorClassSource: ref('dnd2024.content.defense.unarmored.v1'), damageResponses: [] },
    'dnd2024.conditions': { entries: [], sourceRef: { sourceId: 'dnd2024.source.srd-5.2.1', locator: 'Rules Glossary' } },
    'dnd2024.creature.movement': { speeds: { 'dnd2024.vocabulary.movement-mode.walk': {
      distance: { dimension: 'distance', value: { numerator: 1143, denominator: 125 }, unit: ref('dnd2024.vocabulary.distance-unit.meter') },
      enabled: true, sourceRefs: [ref('dnd2024.source.srd-5.2.1')],
    } } },
  }, ids.origin, 'presence');
  entity(ids.guide, 'Watchtower guide', {}, ids.destination, 'presence');
  entity('slice19-private-fact', 'Private fixture fact', {
    'game.core.world.fact': { status: 'active', summary: privateMarkers[0], provenance: 'Synthetic GM-only fixture fact.', visibility: 'gm' },
  });
  entity('slice19-private-knowledge', privateMarkers[1] + ' ' + privateMarkers[2], {
    'dnd2024.magic-item.knowledge': { knowledgeRelationship: { stateSpaceId,
      fromEntityId: ids.guide, toEntityId: ids.loot, qualifiedKind: 'dnd2024.magic-item.knowledge' },
      identityKnown: true, curseKnown: true, knownProperties: [] },
  }, ids.guide, 'knowledge');
  edge(ids.guide, ids.loot, 'dnd2024.magic-item.knowledge');
  entity(ids.campaign, 'The watchtower road', { 'game.core.campaign.current-scene': { location: ref(ids.origin) }, 'game.core.campaign.root': {
    status: 'active', title: 'The watchtower road', premise: 'Reach the tower, assist the guide, and recover at camp.',
    partyGoals: ['Secure the road'], toneAndBoundaries: ['Synthetic acceptance fixture only'],
    rulesetScope: 'dnd2024', creationMethod: 'manual', reviewFingerprint: definitionFingerprint.toLowerCase(),
  } });
  edge(ids.campaign, ids.world, 'game.core.campaign.in-world');
  entity(ids.terrain, 'Open plains', { 'dnd2024.exploration.terrain': {
    terrainType: ref('dnd2024.vocabulary.terrain-type.plains'), maximumPace: ref('dnd2024.vocabulary.travel-pace.fast'),
  } });
  entity(ids.visibility, 'Clear daylight', { 'dnd2024.exploration.visibility': { basis: ref('dnd2024.vocabulary.visibility.clear') } });
  entity(ids.route, 'The six-mile tower road', {
    'game.core.world.route': { status: 'active', summary: 'A six-mile road.', visibility: 'party', mode: 'on-foot', durationMinutes: 120 },
    'game.core.world.route.availability': { status: 'open' },
    'dnd2024.exploration.route-profile': { revision: 1, fingerprint: definitionFingerprint,
      world: ref(ids.world), origin: ref(ids.origin), destination: ref(ids.destination),
      distance: { dimension: 'distance', value: { numerator: 6, denominator: 1 }, unit: ref('dnd2024.vocabulary.distance-unit.mile') },
      allowedModes: [ref('dnd2024.vocabulary.movement-mode.walk')], terrain: ref(ids.terrain), visibility: ref(ids.visibility),
      terrainDurationMultiplier: { numerator: 1, denominator: 1 }, visibilityDurationMultiplier: { numerator: 1, denominator: 1 },
      navigation: { required: false }, exposure: { enabled: false }, arrivalPolicy: 'move-record-and-visit',
    },
  });
  const waterskin = JSON.parse(await readFile(join(repo, 'catalog/applications/dnd2024/content/entities/equipment/base/equipment.gear.waterskin.json'), 'utf8'));
  entity(waterskin.id, waterskin.name, waterskin.components);
  const backpack = JSON.parse(await readFile(join(repo, 'catalog/applications/dnd2024/content/entities/equipment/base/equipment.gear.backpack.json'), 'utf8'));
  entity(backpack.id, backpack.name, backpack.components);
  entity(ids.backpack, 'Mira backpack', { 'dnd2024.core.definition-link': { definition: ref(backpack.id) }, 'dnd2024.item.quantity': { current: 1 } }, ids.actor, 'inventory');
  entity(ids.loot, 'Tower waterskin', { 'dnd2024.core.definition-link': { definition: ref(waterskin.id) }, 'dnd2024.item.quantity': { current: 1 } }, ids.destination, 'loot');
  const policy = JSON.parse(await readFile(join(repo, 'catalog/applications/dnd2024/content/entities/character-creation/rest/dnd2024.content.rest-policy.standard.v1.json'), 'utf8'));
  entity(policy.id, policy.name, policy.components);
  entity(ids.downtime, 'One hour of agreed service', {
    'dnd2024.core.version': { revision: 1, status: 'active' },
    'dnd2024.downtime.definition': { revision: 1, fingerprint: definitionFingerprint, kind: 'service', totalMinutes: 60,
      prerequisiteKeys: [], reservations: [], cancellationPolicy: 'retain' },
  });
  entity(ids['hazard-definition'], 'Loose masonry consequence', {
    'dnd2024.core.version': { revision: 1, status: 'active' },
    'dnd2024.core.source': { citations: [{ sourceRef: ref('dnd2024.source.srd-5.2.1'), locator: 'Synthetic Slice 19 hazard fixture; damage and conditions use catalog mechanics.' }] },
    'dnd2024.hazard.trap': { category: ref('dnd2024.hazard.category.trap'), trigger: { event: 'slice19.masonry.disturbed', timing: 'when' },
      duration: { kind: 'instantaneous' }, triggerEffects: [
        { effect: ref('dnd2024.mechanic.hazard.damage.apply'), parameters: { amount: 3, damageType: ref('dnd2024.vocabulary.damage-type.bludgeoning'), successfulSaveBehavior: 'full' } },
        { effect: ref('dnd2024.mechanic.conditions.write'), parameters: { condition: 'prone', successfulSaveBehavior: 'full' } },
      ] },
  });
  entity(ids.hazard, 'Loose tower masonry', {
    'dnd2024.core.definition-link': { definition: ref(ids['hazard-definition']) },
    'dnd2024.hazard.trap-state': { phase: ref('dnd2024.hazard.trap-phase.armed'), activationCount: 0 },
  }, ids.destination, 'hazard');
  entity(ids.encounter, 'Watchtower defensive exercise', { 'dnd2024.encounter.board': {
    revision: 1, status: 'active', visibility: 'public', columns: 8, rows: 8, feetPerSquare: 5,
    terrain: [{ id: 'slice19-rubble', label: 'Rubble', area: { x: 2, y: 1, width: 1, height: 1 }, movementCost: 2, visibility: 'public' }],
    obstacles: [{ id: 'slice19-hidden-wall', label: 'GM_HIDDEN_WALL', area: { x: 7, y: 7, width: 1, height: 1 }, blocksMovement: true, visibility: 'dm' }],
  } });
  // The fixture starts with a reviewed, already-locked Initiative graph. No roll is fabricated.
  for (const [actor, result, tieBreakOrder] of [[ids.actor, 20, 0], [ids.guide, 10, 1]]) {
    const participation = actor + '-participation';
    entity(participation, actor + ' participation', {
      'dnd2024.encounter.participation': { membershipRelationship: { stateSpaceId, fromEntityId: ids.encounter,
        toEntityId: participation, qualifiedKind: 'dnd2024.encounter.has-participation' }, status: 'active' },
      'dnd2024.combat.initiative': { encounter: ref(ids.encounter), status: 'locked', result, tieBreakOrder },
    });
    edge(ids.encounter, participation, 'dnd2024.encounter.has-participation');
    edge(participation, actor, 'dnd2024.encounter.participation.for-actor');
  }
  return { entities, relationships, restPolicyId: policy.id, itemDefinitionId: waterskin.id };
}
