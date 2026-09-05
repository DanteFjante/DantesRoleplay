import { gzipSync } from 'node:zlib';

/** @param {Record<string, {type: string, isEntry?: boolean, code?: string, imports?: string[]}>} bundle */
export function measureJavaScriptBundle(bundle) {
  const initial = new Set();
  function visit(file) {
    if (initial.has(file)) return;
    const chunk = bundle[file];
    if (!chunk || chunk.type !== 'chunk' || typeof chunk.code !== 'string')
      throw new Error(`Initial JavaScript dependency is unavailable: ${file}`);
    initial.add(file);
    for (const dependency of chunk.imports ?? []) visit(dependency);
  }
  const chunks = Object.entries(bundle).filter(([, value]) => value.type === 'chunk');
  const entries = chunks.filter(([, value]) => value.isEntry);
  if (!entries.length) throw new Error('No JavaScript entry was produced.');
  for (const [file] of entries) visit(file);
  const sizes = chunks.map(([file, value]) => ({ file, gzipBytes: gzipSync(value.code ?? '').byteLength, initial: initial.has(file) }));
  return {
    initialGzipBytes: sizes.filter(value => value.initial).reduce((sum, value) => sum + value.gzipBytes, 0),
    totalGzipBytes: sizes.reduce((sum, value) => sum + value.gzipBytes, 0),
    chunks: sizes,
  };
}
