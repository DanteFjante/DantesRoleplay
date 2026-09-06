import { gzipSync } from 'node:zlib';

/**
 * @param {Record<string, {type: string, isEntry?: boolean, code?: string, imports?: string[], modules?: Record<string, unknown>}>} bundle
 * @param {{mandatoryModuleSuffixes?: string[]}} [options]
 */
export function measureJavaScriptBundle(bundle, options = {}) {
  const initial = new Set();
  function visit(file, destination, label) {
    if (destination.has(file)) return;
    const chunk = bundle[file];
    if (!chunk || chunk.type !== 'chunk' || typeof chunk.code !== 'string')
      throw new Error(`${label} JavaScript dependency is unavailable: ${file}`);
    destination.add(file);
    for (const dependency of chunk.imports ?? []) visit(dependency, destination, label);
  }
  const chunks = Object.entries(bundle).filter(([, value]) => value.type === 'chunk');
  const entries = chunks.filter(([, value]) => value.isEntry);
  if (!entries.length) throw new Error('No JavaScript entry was produced.');
  for (const [file] of entries) visit(file, initial, 'Initial');
  const firstReady = new Set(initial);
  for (const suffix of options.mandatoryModuleSuffixes ?? []) {
    const normalizedSuffix = suffix.replaceAll('\\', '/');
    const match = chunks.find(([, value]) => Object.keys(value.modules ?? {})
      .some(moduleId => moduleId.replaceAll('\\', '/').endsWith(normalizedSuffix)));
    if (!match) throw new Error(`Mandatory first-ready-view module is unavailable: ${suffix}`);
    visit(match[0], firstReady, 'First-ready-view');
  }
  const sizes = chunks.map(([file, value]) => ({
    file,
    gzipBytes: gzipSync(value.code ?? '').byteLength,
    initial: initial.has(file),
    firstReady: firstReady.has(file),
  }));
  return {
    initialGzipBytes: sizes.filter(value => value.initial).reduce((sum, value) => sum + value.gzipBytes, 0),
    mandatoryFeatureGzipBytes: sizes.filter(value => value.firstReady && !value.initial)
      .reduce((sum, value) => sum + value.gzipBytes, 0),
    firstReadyViewGzipBytes: sizes.filter(value => value.firstReady).reduce((sum, value) => sum + value.gzipBytes, 0),
    totalGzipBytes: sizes.reduce((sum, value) => sum + value.gzipBytes, 0),
    chunks: sizes,
  };
}
