import test from 'node:test';
import assert from 'node:assert/strict';
import {
  collectComponentReferences,
  planRegistrations,
  requireLoopbackOrigin,
} from '../scripts/reconcile-object-component-types.mjs';

test('reconciliation accepts only a credential-free loopback origin', () => {
  assert.equal(requireLoopbackOrigin('http://127.0.0.1:6217'), 'http://127.0.0.1:6217');
  for (const value of ['https://example.com', 'http://user@localhost:6217',
    'http://localhost:6217/path', 'file:///tmp/database']) {
    assert.throws(() => requireLoopbackOrigin(value));
  }
});

test('object component requirements are exact and conflicting hashes fail closed', () => {
  const documents = [{ sources: [{ component: { qualifiedId: 'fixture.note', version: 2,
    schemaHash: 'A'.repeat(64) } }, { component: { qualifiedId: 'base.note', version: 1,
    schemaHash: 'B'.repeat(64) } }] }];
  assert.deepEqual(collectComponentReferences(documents, 'fixture'), [{
    qualifiedId: 'fixture.note', version: 2, schemaHash: 'A'.repeat(64),
  }]);
  assert.throws(() => collectComponentReferences([...documents, { component: {
    qualifiedId: 'fixture.note', version: 2, schemaHash: 'C'.repeat(64),
  } }], 'fixture'));
});

test('registration plan requires a missing contiguous version and exact existing hash', () => {
  const reference = { qualifiedId: 'fixture.note', version: 2, schemaHash: 'B'.repeat(64) };
  const latest = new Map([['fixture.note', { version: 1, schemaHash: 'A'.repeat(64) }]]);
  assert.deepEqual(planRegistrations([reference], latest, new Map()), [{
    ...reference, expectedSchemaHash: 'A'.repeat(64),
  }]);
  assert.deepEqual(planRegistrations([reference], latest,
    new Map([['fixture.note@2', reference]])), []);
  assert.throws(() => planRegistrations([{ ...reference, version: 3 }], latest, new Map()));
  assert.throws(() => planRegistrations([reference], latest,
    new Map([['fixture.note@2', { ...reference, schemaHash: 'C'.repeat(64) }]])));
});
