import test from 'node:test';
import assert from 'node:assert/strict';
import Ajv from 'ajv/dist/2020.js';
import { readFile } from 'node:fs/promises';
import { resolve, dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { fixture, ids } from '../scripts/campaign-simulation-fixture.mjs';

const repo = resolve(dirname(fileURLToPath(import.meta.url)), '../../../../..');
test('disposable campaign fixture conforms to existing component schemas and has closed containment', async () => {
  const initial = await fixture(repo);
  const known = new Set(initial.entities.map(entity => entity.entityId));
  assert.equal(known.size, initial.entities.length);
  assert.ok(known.has(ids.world));
  assert.ok(initial.entities.length <= 64);
  const ajv = new Ajv({ strict: false, allErrors: true });
  for (const entity of initial.entities) {
    if (entity.entityId !== ids.world) assert.ok(known.has(entity.containment.containerEntityId));
    for (const component of entity.components) {
      const id = component.qualifiedTypeId;
      const file = id.startsWith('game.') ? join(repo, 'catalog/components', id.replaceAll('.', '/') + '.schema.json')
        : join(repo, 'catalog/applications/dnd2024/components', id + '.schema.json');
      const validate = ajv.compile(JSON.parse(await readFile(file, 'utf8')));
      assert.ok(validate(component.value), `${entity.entityId}/${id}: ${JSON.stringify(validate.errors)}`);
    }
  }
});
