import assert from 'node:assert/strict';
import { randomBytes } from 'node:crypto';
import { readFile, readdir } from 'node:fs/promises';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const webRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const repositoryRoot = resolve(webRoot, '../../../..');

export function requireLoopbackOrigin(value) {
  const origin = new URL(value);
  assert.ok(origin.protocol === 'http:' || origin.protocol === 'https:');
  assert.ok(['localhost', '127.0.0.1', '[::1]'].includes(origin.hostname));
  assert.ok(!origin.username && !origin.password && !origin.search && !origin.hash && origin.pathname === '/');
  return origin.origin;
}

async function files(root) {
  const result = [];
  for (const entry of await readdir(root, { withFileTypes: true })) {
    const path = join(root, entry.name);
    if (entry.isDirectory()) result.push(...await files(path));
    else if (entry.isFile() && entry.name.endsWith('.json')) result.push(path);
  }
  return result.sort();
}

export function collectComponentReferences(documents, applicationId) {
  const references = new Map();
  function visit(value) {
    if (!value || typeof value !== 'object') return;
    if (typeof value.qualifiedId === 'string' && value.qualifiedId.startsWith(`${applicationId}.`) &&
        Number.isInteger(value.version) && value.version > 0 && /^[A-F0-9]{64}$/.test(value.schemaHash ?? '')) {
      const key = `${value.qualifiedId}@${value.version}`;
      const previous = references.get(key);
      assert.ok(!previous || previous.schemaHash === value.schemaHash,
        `Object contracts disagree about ${key}.`);
      references.set(key, { qualifiedId: value.qualifiedId, version: value.version,
        schemaHash: value.schemaHash });
    }
    for (const child of Object.values(value)) visit(child);
  }
  for (const document of documents) visit(document);
  return [...references.values()].sort((left, right) =>
    left.qualifiedId.localeCompare(right.qualifiedId) || left.version - right.version);
}

export function planRegistrations(references, latestById, exactByKey) {
  const missing = [];
  for (const reference of references) {
    const exact = exactByKey.get(`${reference.qualifiedId}@${reference.version}`);
    if (exact) {
      assert.equal(exact.schemaHash, reference.schemaHash,
        `${reference.qualifiedId} v${reference.version} has a different registered schema.`);
      continue;
    }
    const latest = latestById.get(reference.qualifiedId);
    assert.equal(reference.version, (latest?.version ?? 0) + 1,
      `${reference.qualifiedId} v${reference.version} cannot be registered contiguously.`);
    missing.push({ ...reference, expectedSchemaHash: latest?.schemaHash ?? null });
  }
  return missing;
}

export function parseRpc(text) {
  const payload = text.trimStart().startsWith('{') ? text
    : text.split(/\r?\n/).filter(line => line.startsWith('data:'))
      .map(line => line.slice(5).trim()).join('\n');
  const message = JSON.parse(payload);
  if (message.error) throw new Error(JSON.stringify(message.error));
  const result = message.result;
  assert.ok(result && !result.isError, JSON.stringify(result));
  const envelope = JSON.parse(result.content.find(value => value.type === 'text').text);
  assert.ok(envelope.ok && !envelope.error, JSON.stringify(envelope));
  return envelope.data;
}

async function json(origin, path) {
  const response = await fetch(origin + path, { headers: { Accept: 'application/json' },
    signal: AbortSignal.timeout(30_000) });
  const text = await response.text();
  assert.ok(response.ok, `${path} returned ${response.status}: ${text.slice(0, 500)}`);
  return JSON.parse(text);
}

async function call(origin, name, args) {
  const response = await fetch(origin + '/mcp', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', Accept: 'application/json, text/event-stream' },
    body: JSON.stringify({ jsonrpc: '2.0', id: 1, method: 'tools/call',
      params: { name, arguments: args } }),
    signal: AbortSignal.timeout(120_000),
  });
  const text = await response.text();
  assert.ok(response.ok, `MCP returned ${response.status}: ${text.slice(0, 500)}`);
  return parseRpc(text);
}

async function currentTypes(origin, applicationId) {
  const latest = new Map();
  let cursor = null;
  do {
    const parameters = new URLSearchParams({ limit: '100' });
    if (cursor) parameters.set('cursor', cursor);
    const page = await json(origin,
      `/api/control/structure/applications/${encodeURIComponent(applicationId)}/component-types?${parameters}`);
    for (const item of page.items) latest.set(item.qualifiedId, item);
    cursor = page.nextCursor;
  } while (cursor);
  return latest;
}

async function reconcile({ origin, applicationId, apply }) {
  origin = requireLoopbackOrigin(origin);
  const objectRoot = join(repositoryRoot, 'catalog', 'applications', applicationId, 'objects');
  const documents = await Promise.all((await files(objectRoot)).map(async path =>
    JSON.parse(await readFile(path, 'utf8'))));
  const references = collectComponentReferences(documents, applicationId);
  const latest = await currentTypes(origin, applicationId);
  const exact = new Map();
  for (const reference of references) {
    if ((latest.get(reference.qualifiedId)?.version ?? 0) < reference.version) continue;
    const registered = await json(origin,
      `/api/control/structure/component-types/${encodeURIComponent(reference.qualifiedId)}` +
      `/versions/${reference.version}`);
    exact.set(`${reference.qualifiedId}@${reference.version}`, registered);
  }
  const planned = planRegistrations(references, latest, exact);
  if (!apply) return { status: planned.length ? 'registration-required' : 'ready', planned };

  const registered = [];
  for (const item of planned) {
    const schemaPath = join(repositoryRoot, 'catalog', 'applications', applicationId,
      'components', `${item.qualifiedId}.schema.json`);
    const schemaJson = await readFile(schemaPath, 'utf8');
    const payload = JSON.stringify({
      requestToken: randomBytes(16).toString('hex'), applicationId,
      qualifiedTypeId: item.qualifiedId, schemaJson,
      expectedSchemaHash: item.expectedSchemaHash,
    });
    const request = { kind: 'system.component-type.register',
      intent: `Register exact component schema required by ${applicationId} object contracts.`, payload };
    const preview = await call(origin, 'commit', { ...request, dryRun: true });
    assert.equal(preview.componentType.version, item.version);
    assert.equal(preview.componentType.schemaHash, item.schemaHash);
    const committed = await call(origin, 'commit', { ...request, dryRun: false });
    assert.equal(committed.componentType.version, item.version);
    assert.equal(committed.componentType.schemaHash, item.schemaHash);
    registered.push({ qualifiedId: item.qualifiedId, version: item.version,
      schemaHash: item.schemaHash, outcome: committed.outcome });
  }
  return { status: 'ready', registered };
}

function argument(name, fallback) {
  const index = process.argv.indexOf(name);
  return index < 0 ? fallback : process.argv[index + 1];
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const result = await reconcile({
    origin: argument('--origin', 'http://localhost:6217'),
    applicationId: argument('--application', 'dnd2024'),
    apply: process.argv.includes('--apply'),
  });
  process.stdout.write(`${JSON.stringify(result, null, 2)}\n`);
  if (result.status !== 'ready') process.exitCode = 1;
}
