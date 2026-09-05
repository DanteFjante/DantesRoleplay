import test from 'node:test';
import assert from 'node:assert/strict';
import { compareCampaigns } from '../scripts/campaign-simulation.mjs';

test('campaign acceptance requires two successful measured flows and exact outcome parity', () => {
  const direct = { status: 'passed', outcome: { clock: { currentMinute: 340, revision: 4 }, hitPoints: { current: 17 }, inventory: ['waterskin'] } };
  assert.match(compareCampaigns(direct, structuredClone(direct)), /^[0-9A-F]{64}$/);
  for (const web of [null, { ...direct, status: 'blocked' }, { status: 'passed' },
    { ...direct, outcome: { ...direct.outcome, clock: { currentMinute: 400, revision: 5 } } },
    { ...direct, outcome: { ...direct.outcome, hitPoints: { current: 20 } } },
    { ...direct, outcome: { ...direct.outcome, inventory: [] } }]) {
    assert.throws(() => compareCampaigns(direct, web));
  }
});
