// Validates unregistered IV00 contract drafts; does not execute a game projection.
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createRequire } from 'node:module';

const require = createRequire(new URL('../../../src/system/web-interface/dnd2024/package.json', import.meta.url));
const Ajv2020 = require('ajv/dist/2020.js').default;
const ajv = new Ajv2020({ strict: false, allErrors: true });
const read = name => JSON.parse(readFileSync(new URL(name, import.meta.url), 'utf8'));
const clone = value => structuredClone(value);
let checks = 0;
function check(condition, message) { assert.ok(condition, message); checks++; }
function valid(validator, value, message) {
  check(validator(value), `${message}: ${ajv.errorsText(validator.errors)}`);
}
function invalid(validator, value, message) {
  check(!validator(value), message);
}
function closed(node, path = '$') {
  if (!node || typeof node !== 'object') return;
  if (node.type === 'object') {
    check(node.additionalProperties === false, `${path}: object must be closed`);
  }
  check(!Array.isArray(node.type), `${path}: use anyOf for union types`);
  for (const [key, value] of Object.entries(node)) closed(value, `${path}.${key}`);
}

const examples = read('examples.json');
for (const kind of ['details', 'recipes', 'uses']) {
  const draft = read(`${kind}.query.draft.json`);
  const req = read(`${kind}.requirements.draft.json`);
  const query = draft.query;
  check(draft.registrationReady === false && req.registrationReady === false, 'Drafts must stay unregistered');
  check(query.projection.contentHash === null && query.projection.outputSchemaHash === null, 'No fabricated registration hashes');
  check(query.exposure === 'binding-only', 'No model action exposure');
  assert.deepEqual(Object.keys(query.roles).sort(), ['campaign', 'subject']); checks++;
  assert.deepEqual(req.existingRequirementFields.inputSchema, query.inputSchema); checks++;
  assert.deepEqual(req.existingRequirementFields.effectComponentIds, []); checks++;
  assert.deepEqual(req.executionPostcondition, { effects: [], events: [], notifications: [] }); checks++;
  closed(query.inputSchema); closed(query.outputSchema);
  const input = ajv.compile(query.inputSchema);
  const output = ajv.compile(query.outputSchema);
  const request = { itemId: 'fixture.item' };
  if (kind === 'recipes') Object.assign(request, { makesOffset: 0, usesOffset: 0, expectedSourceRevision: null });
  if (kind === 'uses') Object.assign(request, { offset: 0, expectedSourceRevision: null });
  valid(input, request, `${kind}: initial input`);
  invalid(input, { ...request, observerId: 'fixture.other' }, `${kind}: reject observer injection`);
  invalid(input, { ...request, perspective: 'dm' }, `${kind}: reject audience injection`);
  invalid(input, { ...request, itemId: '' }, `${kind}: reject empty item selection`);
  valid(output, examples[kind], `${kind}: positive output`);
  invalid(output, { ...examples[kind], secretCount: 1 }, `${kind}: reject undeclared output`);
  check(Buffer.byteLength(JSON.stringify(examples[kind]), 'utf8') <= 65536, `${kind}: sample byte limit`);
  const nestedExtra = clone(examples[kind]);
  if (kind === 'details') nestedExtra.properties[0].rawComponent = {};
  else nestedExtra.uses.entries[0].rawComponent = {};
  invalid(output, nestedExtra, `${kind}: reject extra nested field`);
  const comparison = clone(examples[kind]);
  if (kind === 'details') comparison.observerKnowledge = 'unknown';
  else comparison.uses.entries[0].observerKnowledge = 'unknown';
  invalid(output, comparison, `${kind}: reject DM comparison in Player output`);
  comparison.perspective = 'dm';
  valid(output, comparison, `${kind}: allow DM observer comparison`);
  if (kind === 'details') {
    const partial = clone(examples[kind]);
    partial.state = 'partial';
    invalid(output, partial, 'Details partial must include a reason');
    partial.reasons = ['source-incomplete'];
    valid(output, partial, 'Details partial with explicit reason');
    partial.quantity = null;
    valid(output, partial, 'Unavailable quantity is null');
    partial.quantity = -1;
    invalid(output, partial, 'Negative quantity is invalid');
    continue;
  }
  const offset = kind === 'recipes' ? 'makesOffset' : 'offset';
  invalid(input, { ...request, [offset]: 1 }, `${kind}: continuation requires revision`);
  valid(input, { ...request, [offset]: 1, expectedSourceRevision: 'A'.repeat(64) }, `${kind}: valid continuation`);
  invalid(input, { ...request, [offset]: -1 }, `${kind}: negative offset`);
  invalid(input, { ...request, [offset]: 10001 }, `${kind}: oversized offset`);
  const empty = clone(examples[kind]);
  empty.uses = { state: 'empty', entries: [], nextOffset: null, reasons: [] };
  valid(output, empty, `${kind}: independently empty group`);
  empty.uses.entries = clone(examples[kind].uses.entries);
  invalid(output, empty, `${kind}: empty cannot carry entries`);
  const partial = clone(examples[kind]);
  partial.uses.state = 'partial';
  invalid(output, partial, `${kind}: partial requires reason`);
  partial.uses.reasons = ['page-limit'];
  partial.uses.nextOffset = 1;
  valid(output, partial, `${kind}: authorized continuation shape`);
  partial.uses.state = 'ready';
  invalid(output, partial, `${kind}: ready cannot carry continuation`);
  const unknown = clone(examples[kind]);
  unknown.uses.entries[0].knowledgeState = 'unknown';
  invalid(output, unknown, `${kind}: unknown is not displayable assertion certainty`);
  const overflow = clone(examples[kind]);
  overflow.uses.entries = Array.from({ length: kind === 'recipes' ? 17 : 33 }, () => clone(examples[kind].uses.entries[0]));
  invalid(output, overflow, `${kind}: page count bound`);
}

const observerSchema = read('authorized-observer.schema.draft.json');
closed(observerSchema);
const observer = ajv.compile(observerSchema);
const context = {
  version: 1, applicationId: 'fixture.app', stateSpaceId: 'fixture.state',
  campaignId: 'fixture.campaign', observerId: 'fixture.actor', perspective: 'player',
  policyRevision: 'A'.repeat(64), participationRevision: 'B'.repeat(64), bindingRevision: 'C'.repeat(64),
  inventoryRevision: 'D'.repeat(64), knowledgeRevision: 'E'.repeat(64), authorizedSourceRevision: 'F'.repeat(64),
  inventoryComplete: true, knowledgeComplete: true,
  knowledge: [{ knowledgeId: 'fixture.fact', state: 'unknown', sourceKind: 'fixture-source', sourceEntityId: null, revision: 'opaque-fixture-revision' }],
};
valid(observer, context, 'Host context can represent explicit unknown');
invalid(observer, { ...context, principalSecret: 'not-a-contract-field' }, 'Host context is closed');
invalid(observer, { ...context, policyRevision: 'not-a-fingerprint' }, 'Revision format is explicit');
const errorSchema = read('errors.schema.draft.json');
closed(errorSchema);
const error = ajv.compile(errorSchema);
for (const pair of errorSchema.oneOf) {
  const example = {
    status: pair.properties.status.const,
    body: Object.fromEntries(Object.entries(pair.properties.body.properties).map(([key, value]) => [key, value.const])),
  };
  valid(error, example, `Error ${example.status}: exact safe body`);
  invalid(error, { ...example, body: { ...example.body, itemId: 'fixture.hidden' } }, 'Error must not echo selection identity');
  invalid(error, { ...example, body: { ...example.body, message: 'Private source details' } }, 'Error message is fixed');
}
console.log(`IV00 draft validation passed: ${checks} checks. No runtime, authorization or catalog acceptance claimed.`);
