import test from 'node:test';
import assert from 'node:assert/strict';
import { acceptanceStatus, parseRpc, preflightExitCode, requireIsolatedOrigin } from '../scripts/campaign-simulation-preflight.mjs';

test('simulation refuses live listeners, remote hosts and decorated URLs', () => {
  for (const origin of ['http://localhost:6217', 'http://127.0.0.1:6217',
    'https://127.0.0.1:5144', 'http://127.0.0.1:5144', 'http://example.com:45678',
    'http://127.0.0.1', 'http://127.0.0.1:45678/mcp',
    'http://user@127.0.0.1:45678', 'http://127.0.0.1:45678?target=live']) {
    assert.throws(() => requireIsolatedOrigin(origin), origin);
  }
  assert.equal(requireIsolatedOrigin('http://127.0.0.1:45678'), 'http://127.0.0.1:45678');
});

test('MCP parser accepts JSON and SSE without swallowing protocol errors', () => {
  const result = { content: [{ type: 'text', text: '{"ok":true}' }] };
  const message = JSON.stringify({ jsonrpc: '2.0', id: 1, result });
  assert.deepEqual(parseRpc(message), result);
  assert.deepEqual(parseRpc(`event: message\r\ndata: ${message}\r\n\r\n`), result);
  assert.throws(() => parseRpc('{"error":{"code":-32603,"message":"failure"}}'), /failure/);
  assert.throws(() => parseRpc('{"jsonrpc":"2.0"}'), /Missing/);
  assert.throws(() => parseRpc('not a protocol envelope'));
});

test('bootstrap alone cannot satisfy campaign acceptance', () => {
  assert.equal(acceptanceStatus({}), 'incomplete');
  assert.equal(acceptanceStatus({ bootstrap: 'passed' }), 'incomplete');
  assert.equal(acceptanceStatus({ bootstrap: 'failed' }), 'blocked');
  const complete = Object.fromEntries(['bootstrap', 'exploration', 'travel-hazard', 'conversation-social',
    'combat-movement', 'loot-inventory', 'rest', 'downtime', 'restart-resume', 'web-parity', 'audience-isolation']
    .map(stage => [stage, 'passed']));
  assert.equal(acceptanceStatus(complete), 'passed');
  assert.equal(acceptanceStatus({ ...complete, 'audience-isolation': 'not-run' }), 'incomplete');
  assert.equal(acceptanceStatus({ ...complete, 'restart-resume': 'failed' }), 'blocked');
});

test('successful preflight exits cleanly without claiming campaign acceptance', () => {
  assert.equal(preflightExitCode({ bootstrap: 'passed' }), 0);
  assert.equal(acceptanceStatus({ bootstrap: 'passed' }), 'incomplete');
  assert.equal(preflightExitCode({ bootstrap: 'failed' }), 1);
  assert.equal(preflightExitCode({ bootstrap: 'not-run' }), 1);
  assert.equal(preflightExitCode({}), 1);
});
