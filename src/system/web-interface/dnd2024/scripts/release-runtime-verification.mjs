import assert from 'node:assert/strict';
import { sha256 } from './create-release-manifest.mjs';
import { canonicalJson } from './release-signature.mjs';

const requiredOwners = ['database', 'application-registration', 'active-catalog-snapshot',
  'catalog-materialization', 'extension-resolution', 'query-callability', 'web-page-release', 'audience-binding'];

export function verifyRuntimeTarget(expected, readiness, audience) {
  assert.ok(expected?.applicationId && expected.stateSpaceId && expected.campaignId, 'A reviewed runtime target is required');
  assert.equal(readiness.status, 'ready', 'Application readiness must pass');
  assert.equal(readiness.applicationId, expected.applicationId);
  for (const name of requiredOwners) {
    const matches = readiness.checks.filter(check => check.name === name);
    assert.equal(matches.length, 1, `Missing or duplicate readiness owner ${name}`);
    const check = matches[0];
    assert.equal(check.status, 'ready', `${name} is not ready`);
    const pin = expected.checks?.[name];
    assert.ok(pin, `The manifest must pin ${name}`);
    assert.equal(check.code, pin.code, `${name} readiness code drift`);
    for (const field of ['revision', 'fingerprint'])
      assert.equal(check.evidence?.[field] ?? null, pin[field] ?? null, `${name} ${field} drift`);
  }
  for (const name of ['database', 'application-registration', 'active-catalog-snapshot', 'extension-resolution', 'web-page-release'])
    assert.match(expected.checks[name].fingerprint, /^[0-9A-F]{64}$/, `${name} needs an exact fingerprint`);
  assert.equal(audience.status, 'bound');
  for (const field of ['applicationId', 'stateSpaceId', 'campaignId', 'role', 'actorId'])
    assert.equal(audience[field] ?? null, expected[field] ?? null, `Audience ${field} drift`);
  for (const field of ['policyRevision', 'bindingRevision']) {
    assert.match(expected[field], /^[0-9A-F]{64}$/, `Audience ${field} must be pinned`);
    assert.equal(audience[field], expected[field], `Audience ${field} drift`);
  }
  if (expected.role === 'actor') assert.match(expected.participationRevision, /^[0-9A-F]{64}$/);
  else assert.equal(expected.participationRevision ?? null, null);
  assert.equal(audience.participationRevision ?? null, expected.participationRevision ?? null, 'Audience participationRevision drift');
  assert.ok(['actor', 'game-master'].includes(expected.role));
}

export function verifyBrowserEvidence(manifest, baseUrl, evidence) {
  assert.ok(evidence, 'Live browser evidence is required, not inferred from a source build');
  assert.equal(evidence.manifestFingerprint, sha256(Buffer.from(canonicalJson(manifest))));
  assert.equal(evidence.url, `${baseUrl}/ui/${manifest.pageId}`);
  assert.equal(evidence.role, manifest.expectedRuntime.role);
  for (const name of ['no-wheel-zoom', 'ganji-dossier', 'player-dm-boundary'])
    assert.equal(evidence.checks?.[name], 'passed', `Missing live browser check: ${name}`);
  assert.ok(Array.isArray(evidence.observations) && evidence.observations.length > 0, 'Browser observations must be retained');
  const itemView = manifest.expectedRuntime.itemView;
  if (itemView) {
    assert.ok(itemView.observerId && itemView.itemIds?.length && itemView.perspectives?.length);
    for (const itemId of itemView.itemIds) for (const perspective of itemView.perspectives) {
      assert.ok(['player', 'dm'].includes(perspective));
      const matches = evidence.itemViews?.filter(row => row.itemId === itemId && row.perspective === perspective && row.observerId === itemView.observerId) ?? [];
      assert.equal(matches.length, 1, 'Each signed item selection needs one browser observation');
      for (const tab of ['details', 'recipes', 'uses']) {
        assert.ok(['ready', 'partial', 'empty'].includes(matches[0].tabs?.[tab]), `Item ${tab} did not render successfully`);
        assert.equal(manifest.expectedRuntime.readModels.filter(probe => probe.entityId === itemView.observerId &&
          probe.input?.itemId === itemId && probe.perspective === perspective && probe.campaignId === manifest.expectedRuntime.campaignId &&
          probe.queryId === `dnd2024.query.inventory-item-${tab}`).length, 1, 'Item browser proof requires a matching signed server probe');
      }
    }
    for (const name of ['item-return', 'item-request-budget', 'item-knowledge-boundary'])
      assert.equal(evidence.checks?.[name], 'passed', `Missing item browser check: ${name}`);
    assert.ok(itemView.widths?.length);
    for (const width of itemView.widths) {
      const layouts = evidence.itemLayouts?.filter(row => row.width === width) ?? [];
      assert.equal(layouts.length, 1, 'Each signed width needs a browser measurement');
      assert.ok(layouts[0].clientWidth > 0 && layouts[0].clientWidth <= width);
      assert.ok(layouts[0].scrollWidth <= layouts[0].clientWidth, 'Item page overflows');
    }
  }
}

export async function probeRuntime(expected, json) {
  const app = encodeURIComponent(expected?.applicationId);
  const readiness = await json(`/api/readiness/applications/${app}`);
  verifyRuntimeTarget(expected, readiness, await json('/api/audience-context'));
  assert.ok(expected.readModels?.length && expected.actions?.length, 'Signed query and action probes are required');
  const prefix = `/api/applications/${app}/state-spaces/${encodeURIComponent(expected.stateSpaceId)}`;
  for (const probe of expected.readModels) {
    const parameters = new URLSearchParams();
    if (probe.input !== undefined) {
      assert.ok(probe.input && typeof probe.input === 'object' && !Array.isArray(probe.input));
      parameters.set('input', JSON.stringify(probe.input));
    }
    if (probe.perspective !== undefined) {
      assert.ok(['player', 'dm'].includes(probe.perspective));
      parameters.set('perspective', probe.perspective);
    }
    if (probe.campaignId !== undefined) {
      assert.equal(probe.campaignId, expected.campaignId);
      parameters.set('campaignId', probe.campaignId);
    }
    const suffix = parameters.size ? '?' + parameters : '';
    const data = await json(`${prefix}/entities/${encodeURIComponent(probe.entityId)}/read-models/${encodeURIComponent(probe.queryId)}${suffix}`);
    for (const field of ['resultFingerprint', 'sourceRevisionFingerprint']) {
      assert.match(probe[field], /^[0-9A-F]{64}$/);
      assert.equal(data[field], probe[field], `Live query ${field} drift`);
    }
    assert.ok(data.data && typeof data.data === 'object');
  }
  for (const probe of expected.actions) {
    const data = await json(`${prefix}/mechanics/${encodeURIComponent(probe.mechanicId)}`);
    assert.match(probe.contentFingerprint, /^[0-9A-F]{64}$/);
    assert.equal(data.version, probe.version);
    assert.equal(data.contentFingerprint, probe.contentFingerprint, 'Live action contract drift');
  }
  return readiness;
}
