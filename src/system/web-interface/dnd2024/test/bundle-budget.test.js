import assert from 'node:assert/strict';
import test from 'node:test';
import { gzipSync } from 'node:zlib';
import { measureJavaScriptBundle } from '../scripts/bundle-budget.mjs';

test('initial bundle budget includes shared static imports once but tracks lazy chunks separately', () => {
  const report = measureJavaScriptBundle({
    'entry.js': { type: 'chunk', isEntry: true, code: 'entry', imports: ['shared.js', 'second.js'] },
    'second.js': { type: 'chunk', code: 'second', imports: ['shared.js'] },
    'shared.js': { type: 'chunk', code: 'shared', imports: ['entry.js'] },
    'lazy.js': { type: 'chunk', code: 'lazy', imports: [] },
    'style.css': { type: 'asset' },
  });
  assert.equal(report.initialGzipBytes, ['entry', 'second', 'shared'].reduce((sum, code) => sum + gzipSync(code).byteLength, 0));
  assert.equal(report.totalGzipBytes - report.initialGzipBytes, gzipSync('lazy').byteLength);
  assert.equal(report.chunks.length, 4);
});

test('bundle accounting fails closed on missing entries or static dependencies', () => {
  assert.throws(() => measureJavaScriptBundle({}), /No JavaScript entry/);
  assert.throws(() => measureJavaScriptBundle({ 'entry.js': { type: 'chunk', isEntry: true, code: 'entry', imports: ['missing.js'] } }), /unavailable/);
});

test('first-ready-view accounting includes mandatory lazy modules and their shared imports once', () => {
  const report = measureJavaScriptBundle({
    'entry.js': { type: 'chunk', isEntry: true, code: 'entry', imports: ['shared.js'], modules: { '/src/main.tsx': {} } },
    'shared.js': { type: 'chunk', code: 'shared', imports: [], modules: {} },
    'hub.js': { type: 'chunk', code: 'hub', imports: ['shared.js'], modules: { 'C:\\repo\\src\\components\\Hub.tsx': {} } },
    'optional.js': { type: 'chunk', code: 'optional', imports: [], modules: { '/src/components/Optional.tsx': {} } },
  }, { mandatoryModuleSuffixes: ['/src/components/Hub.tsx'] });
  assert.equal(report.mandatoryFeatureGzipBytes, gzipSync('hub').byteLength);
  assert.equal(report.firstReadyViewGzipBytes,
    ['entry', 'shared', 'hub'].reduce((sum, code) => sum + gzipSync(code).byteLength, 0));
  assert.equal(report.chunks.find(chunk => chunk.file === 'optional.js').firstReady, false);
  assert.throws(() => measureJavaScriptBundle({
    'entry.js': { type: 'chunk', isEntry: true, code: 'entry', imports: [], modules: {} },
  }, { mandatoryModuleSuffixes: ['/missing.ts'] }), /Mandatory first-ready-view module is unavailable/);
});
