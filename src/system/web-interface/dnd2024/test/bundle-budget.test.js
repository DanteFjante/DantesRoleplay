import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import { gzipSync } from 'node:zlib';
import { measureJavaScriptBundle } from '../scripts/bundle-budget.mjs';

const entrySource = readFileSync(new URL('../src/server-host/main.tsx', import.meta.url), 'utf8');
const hubSource = readFileSync(new URL('../src/components/DndInformationHub.tsx', import.meta.url), 'utf8');
const itemFeatureSource = readFileSync(new URL('../src/components/items/ItemWorkspaceFeature.tsx', import.meta.url), 'utf8');
const characterFeatureSource = readFileSync(new URL('../src/components/character/CharacterWorkspaceFeature.tsx', import.meta.url), 'utf8');
const previewFeatureSource = readFileSync(new URL('../src/components/PreviewViewsFeature.tsx', import.meta.url), 'utf8');
const sharedStyles = readFileSync(new URL('../src/styles.css', import.meta.url), 'utf8');

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

test('feature styles are awaited inside their existing lazy view boundaries', () => {
  assert.match(entrySource, /import "\.\.\/styles\.css";/u);
  assert.doesNotMatch(entrySource, /character-page\.css|item-page\.css|board-draft\.css/u);
  assert.doesNotMatch(sharedStyles, /@import\s+["']\.\/item-page\.css/u);

  const itemBoundary = hubSource.slice(
    hubSource.indexOf('const ItemWorkspace'),
    hubSource.indexOf('const PlayConversationPanel'),
  );
  assert.match(itemBoundary, /import\("\.\/items\/ItemWorkspaceFeature"\)/u);
  assert.match(itemFeatureSource, /import "\.\.\/\.\.\/item-page\.css";/u);
  assert.match(itemFeatureSource, /export \{ ItemWorkspace \} from "\.\/ItemWorkspace";/u);
  assert.match(characterFeatureSource, /import "\.\.\/\.\.\/character-page\.css";/u);
  assert.match(characterFeatureSource, /export \{ CharacterWorkspace \} from "\.\.\/PartyView";/u);

  const previewBoundary = hubSource.slice(
    hubSource.indexOf('const CurrentViewPreview'),
    hubSource.indexOf('const RulesView'),
  );
  assert.match(previewBoundary, /import\("\.\/PreviewViewsFeature"\)/u);
  assert.match(previewFeatureSource, /import "\.\.\/board-draft\.css";/u);
  assert.match(previewFeatureSource, /export \{ CurrentViewPreview \} from "\.\/PreviewViews";/u);
});
