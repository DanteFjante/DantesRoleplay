import test from 'node:test';
import assert from 'node:assert/strict';
import Ajv from 'ajv/dist/2020.js';
import { readFile, access } from 'node:fs/promises';
import { resolve, dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { createHash } from 'node:crypto';

const repo = resolve(dirname(fileURLToPath(import.meta.url)), '../../../../..');
const profile = 'catalog/applications/dnd2024/install/caldris';
const read = async path => JSON.parse(await readFile(join(repo, path), 'utf8'));
const template = await read(`${profile}/installation.template.json`);
const state = template.stateSpaces.find(value => value.scope === 'runtime-state-space');
const parts = await Promise.all(state.worldPackages.map(read));
const entities = [state.root, ...parts.flatMap(part => part.entities)];
const byId = new Map(entities.map(entity => [entity.entityId, entity]));
const components = entity => Object.fromEntries(entity.components.map(value => [value.qualifiedTypeId, value.value]));
const atlas = await read('docs/world/caldris/maps/lore-atlas/atlas-gm.json');
const packet = await read('catalog/applications/dnd2024/assets/caldris/measure-of-mercy/asset-import-manifest.json');
const opening = await read(`${profile}/opening-state.json`);
const reviewedMaps = await read(`${profile}/map-anchors.json`);

test('Caldris installation includes the complete authored world in ordered bounded packages', () => {
  assert.equal(byId.size, entities.length, 'Each identity is declared once');
  assert.ok(parts.length > 64 && parts.length <= 256, 'The complete world requires the expanded bounded package list');
  const existing = new Set([state.root.entityId]);
  for (const part of parts) {
    assert.ok(part.entities.length > 0 && part.entities.length <= 64);
    assert.ok(part.relationships.length <= 64);
    const effects = part.entities.reduce((total, entity) => total + 2 + entity.components.length, 0) + part.relationships.length;
    assert.ok(effects <= 128, `A sync remains within its existing effect bound: ${effects}`);
    const available = new Set([...existing, ...part.entities.map(entity => entity.entityId)]);
    for (const entity of part.entities) {
      assert.ok(available.has(entity.containment.containerEntityId), entity.entityId);
      assert.notEqual(entity.entityId, entity.containment.containerEntityId);
    }
    for (const edge of part.relationships) {
      assert.ok(available.has(edge.fromEntityId), edge.fromEntityId);
      assert.ok(available.has(edge.toEntityId), edge.toEntityId);
      assert.notEqual(edge.fromEntityId, edge.toEntityId);
    }
    for (const identity of available) existing.add(identity);
  }
  for (const place of Object.values(atlas.places)) assert.ok(byId.has(place.id), `Missing authored place ${place.id}`);
  assert.equal(Object.keys(atlas.places).length, 259);
  for (const identity of ['region.caldris.chalklands', 'location.caldris.button-hills', 'location.caldris.highmead',
    'location.caldris.house-of-the-ninth-angle', 'location.caldris.prediction-court']) assert.ok(!byId.has(identity));
  for (const prefix of ['actor.caldris.', 'fact.caldris.', 'secret.caldris.', 'chronology.caldris.', 'route.caldris.'])
    assert.ok(entities.filter(entity => entity.entityId.startsWith(prefix)).length > 50, prefix);
  assert.ok(components(state.root)['game.core.world.clock']);
  assert.equal(parts.flatMap(part => part.relationships).filter(edge => edge.qualifiedKind === 'game.core.world.faction.in-world').length, 35);
});

test('Caldris clean maps cover every direct child with anchors tied to the exact artwork', async () => {
  const mapAssets = packet.assets.filter(asset => asset.kind === 'map');
  const owners = new Set(mapAssets.map(asset => asset.ownerLocationId));
  assert.equal(owners.size, 8);
  assert.deepEqual(new Set(reviewedMaps.frames.map(frame => frame.ownerLocationId)), owners);
  assert.equal(reviewedMaps.frames.flatMap(frame => frame.anchors).length, 55);
  for (const asset of mapAssets) {
    const entity = byId.get(asset.ownerLocationId);
    assert.deepEqual(components(entity)[asset.bindingPlan.componentId], asset.bindingPlan.valueTemplate, asset.ownerLocationId);
    assert.equal(entity.containment.containerEntityId, asset.parentLocationId ?? state.root.entityId);
  }
  for (const location of packet.locationCreates) {
    const entity = byId.get(location.id);
    assert.equal(entity.containment.containerEntityId, location.container.id);
  }
  for (const asset of packet.assets) {
    const original = opening.entities.find(entity => entity.entityId === asset.ownerLocationId);
    assert.deepEqual(components(byId.get(asset.ownerLocationId))['game.core.world.location'],
      components(original)['game.core.world.location'], 'Opening descriptions remain bound to the new asset packet');
  }
  for (const frame of reviewedMaps.frames) {
    const imageBytes = await readFile(join(repo, frame.imagePath));
    assert.equal(createHash('sha256').update(imageBytes).digest('hex'), frame.imageSha256);
    for (const variant of Object.values(components(byId.get(frame.ownerLocationId))['game.core.world.map.visual'].variants))
      assert.equal(variant.sha256, frame.imageSha256, 'Anchors cannot silently move to a different image frame');
    const directChildren = entities.filter(entity => entity.containment?.containerEntityId === frame.ownerLocationId
      && components(entity)['game.core.world.location']).map(entity => entity.entityId);
    assert.equal(new Set(frame.anchors.map(anchor => anchor.locationId)).size, frame.anchors.length);
    assert.deepEqual(new Set(frame.anchors.map(anchor => anchor.locationId)), new Set(directChildren));
    for (const anchor of frame.anchors) {
      assert.deepEqual(components(byId.get(anchor.locationId))['game.core.world.map.anchor'], { x: anchor.x, y: anchor.y });
      assert.ok(anchor.basis.length > 0);
    }
  }
  const visuals = entities.map(entity => ({ id: entity.entityId, value: components(entity)['game.core.world.map.visual'] })).filter(entry => entry.value);
  assert.equal(visuals.filter(entry => entry.value.status === 'active').length, 8);
  assert.equal(visuals.filter(entry => entry.value.status === 'archived').length, 42);
  for (const entry of visuals.filter(entry => !owners.has(entry.id))) assert.equal(entry.value.status, 'archived', entry.id);
  assert.equal(components(byId.get('location.caldris.solasca'))['game.core.world.location'].status, 'active');
  assert.equal(entities.filter(entity => components(entity)['game.core.world.location']?.status === 'active').length, 275);
});

test('Caldris setup retains every committed authored record and selects its declared homebrew extension', async () => {
  const source = await readFile(join(repo, 'data/exports/current/tables/system_ecs_entity-0001.jsonl'), 'utf8');
  const baseline = source.trim().split('\n').map(line => JSON.parse(line)).filter(row =>
    row[1] === 'dnd2024-main' && row[6] === null && (row[2] === 'world.caldris' || row[2].includes('.caldris.'))
    && row[2] !== 'world.caldris.participation.actor.caldris.ganji');
  assert.ok(baseline.length > 2000);
  for (const row of baseline) assert.ok(byId.has(row[2]), `Missing committed authored record ${row[2]}`);
  const extension = await read('catalog/extensions/dnd2024/caldris-homebrew/extension-package.json');
  assert.deepEqual(template.application.selectedExtensionIds, [extension.extensionId]);
  assert.deepEqual(template.application.extensionPackages, ['catalog/extensions/dnd2024/caldris-homebrew/extension-package.json']);
  for (const sourceId of extension.sourceIds) {
    assert.ok(template.application.sources.some(source => source.id === sourceId));
    assert.ok(!template.application.selectedSourceIds.includes(sourceId), 'Extension sources are selected by their registration');
  }
});

test('Caldris authored state conforms to the current component schemas', async () => {
  const ajv = new Ajv({ strict: false, allErrors: true, validateFormats: false });
  const validators = new Map();
  const errors = [];
  for (const entity of entities) for (const component of entity.components) {
    const identity = component.qualifiedTypeId;
    if (!validators.has(identity)) {
      let path = identity.startsWith('game.') ? `catalog/components/${identity.replaceAll('.', '/')}.schema.json`
        : `catalog/applications/dnd2024/components/${identity}.schema.json`;
      try { await access(join(repo, path)); }
      catch { path = `catalog/components/${identity.replaceAll('.', '/')}.schema.json`; }
      validators.set(identity, ajv.compile(await read(path)));
    }
    const validate = validators.get(identity);
    if (!validate(component.value)) errors.push(`${entity.entityId}/${identity}: ${JSON.stringify(validate.errors)}`);
  }
  assert.deepEqual(errors, []);
  const feat = await read('catalog/applications/dnd2024/content/entities/character-options/feats/feat.magic-initiate.json');
  assert.deepEqual(components(byId.get(feat.id)), feat.components, 'Ganji origin feature uses the current authored definition');
});
