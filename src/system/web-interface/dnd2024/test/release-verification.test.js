import assert from 'node:assert/strict';
import test from 'node:test';
import { generateKeyPairSync } from 'node:crypto';
import { signManifest, verifyManifest, canonicalJson } from '../scripts/release-signature.mjs';
import { verifyRuntimeTarget, verifyBrowserEvidence, probeRuntime } from '../scripts/release-runtime-verification.mjs';
import { sha256 } from '../scripts/create-release-manifest.mjs';

const hash = 'A'.repeat(64);
function fixture() {
  const checks = ['database', 'application-registration', 'active-catalog-snapshot', 'catalog-materialization',
    'extension-resolution', 'query-callability', 'web-page-release', 'audience-binding']
    .map(name => ({ name, status: 'ready', code: name.toUpperCase(), evidence: { revision: '2', fingerprint: hash } }));
  const audience = { status: 'bound', applicationId: 'sample', stateSpaceId: 'sample-main', campaignId: 'campaign.fixture', role: 'actor', actorId: 'actor.fixture', policyRevision: hash, bindingRevision: hash, participationRevision: hash };
  return { audience, readiness: { status: 'ready', applicationId: 'sample', checks }, expected: {
    ...audience, checks: Object.fromEntries(checks.map(({ name, code, evidence }) => [name, { code, ...evidence }])),
    readModels: [{ entityId: 'actor.fixture', queryId: 'sample.query.sheet', resultFingerprint: hash, sourceRevisionFingerprint: hash }],
    actions: [{ mechanicId: 'sample.mechanic.inspect', version: 1, contentFingerprint: hash }]
  } };
}

test('release signing requires an independent trusted key and detects payload tampering', () => {
  const keys = generateKeyPairSync('ed25519');
  const publicKey = keys.publicKey.export({ type: 'spki', format: 'pem' });
  const manifest = signManifest({ schemaVersion: 2, expectedRuntime: fixture().expected, files: [] }, keys.privateKey);
  assert.equal(verifyManifest(manifest, publicKey).schemaVersion, 2);
  assert.throws(() => verifyManifest({ ...manifest, files: [{ path: 'other.js' }] }, publicKey));
  assert.throws(() => verifyManifest(manifest));
  assert.throws(() => verifyManifest({ schemaVersion: 2 }, publicKey));
  const stranger = generateKeyPairSync('ed25519').publicKey.export({ type: 'spki', format: 'pem' });
  assert.throws(() => verifyManifest(manifest, stranger));
});

test('release target rejects every owner fingerprint or revision drift and audience mismatch', () => {
  const { expected, readiness, audience } = fixture();
  verifyRuntimeTarget(expected, readiness, audience);
  const gm = { ...audience, role: 'game-master', actorId: null, participationRevision: undefined };
  verifyRuntimeTarget({ ...expected, ...gm }, readiness, gm);
  for (const original of readiness.checks) for (const field of ['revision', 'fingerprint']) {
    const altered = structuredClone(readiness);
    altered.checks.find(check => check.name === original.name).evidence[field] = 'changed';
    assert.throws(() => verifyRuntimeTarget(expected, altered, audience));
  }
  assert.throws(() => verifyRuntimeTarget(expected, { ...readiness, status: 'failed' }, audience));
  assert.throws(() => verifyRuntimeTarget(expected, readiness, { ...audience, role: 'game-master' }));
  assert.throws(() => verifyRuntimeTarget(expected, readiness, { ...audience, stateSpaceId: 'wrong' }));
  for (const field of ['policyRevision', 'bindingRevision', 'participationRevision']) {
    assert.throws(() => verifyRuntimeTarget(expected, readiness, { ...audience, [field]: 'B'.repeat(64) }));
    assert.throws(() => verifyRuntimeTarget({ ...expected, [field]: undefined }, readiness, audience));
  }
  assert.throws(() => verifyRuntimeTarget(expected, { ...readiness, checks: [] }, audience));
});

test('runtime probes compare actual callable query results and action contract fingerprints', async () => {
  const { expected, readiness, audience } = fixture();
  const response = path => path.includes('/readiness/') ? readiness : path === '/api/audience-context' ? audience
    : path.includes('/read-models/') ? { data: {}, resultFingerprint: hash, sourceRevisionFingerprint: hash }
      : { version: 1, contentFingerprint: hash };
  await probeRuntime(expected, async path => response(path));
  await assert.rejects(probeRuntime(expected, async path => path.includes('/read-models/')
    ? { ...response(path), resultFingerprint: 'B'.repeat(64) } : response(path)));
  await assert.rejects(probeRuntime(expected, async path => path.includes('/mechanics/')
    ? { ...response(path), version: 2 } : response(path)));
});

test('browser proof must bind the exact signed release, role, listener and completed checks', () => {
  const manifest = { pageId: 'dnd2024-play', expectedRuntime: fixture().expected };
  const baseUrl = 'http://localhost:6217';
  const evidence = { manifestFingerprint: sha256(Buffer.from(canonicalJson(manifest))), url: baseUrl + '/ui/dnd2024-play',
    role: 'actor', checks: { 'no-wheel-zoom': 'passed', 'ganji-dossier': 'passed', 'player-dm-boundary': 'passed' }, observations: ['Actual browser observation'] };
  verifyBrowserEvidence(manifest, baseUrl, evidence);
  assert.throws(() => verifyBrowserEvidence(manifest, baseUrl));
  assert.throws(() => verifyBrowserEvidence(manifest, baseUrl, { ...evidence, checks: {} }));
  assert.throws(() => verifyBrowserEvidence(manifest, baseUrl, { ...evidence, role: 'game-master' }));
  assert.throws(() => verifyBrowserEvidence(manifest, baseUrl, { ...evidence, manifestFingerprint: 'B'.repeat(64) }));
});
