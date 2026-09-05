import { readFile, writeFile } from "node:fs/promises";
import { resolve } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";
import { extractAssetReferences, findRetiredMapSignatures, sha256 } from "./create-release-manifest.mjs";
import assert from 'node:assert/strict';
import { verifyManifest, canonicalJson } from './release-signature.mjs';
import { probeRuntime, verifyRuntimeTarget, verifyBrowserEvidence } from './release-runtime-verification.mjs';

const webRoot = resolve(fileURLToPath(new URL("..", import.meta.url)));

async function fetchBytes(url) {
  const response = await fetch(url, { cache: "no-store", redirect: 'error', signal: AbortSignal.timeout(15000) });
  const bytes = Buffer.from(await response.arrayBuffer());
  if (!response.ok) throw new Error(`${url} returned ${response.status}.`);
  return { bytes, cacheControl: response.headers.get("cache-control") ?? "" };
}

export async function verifyLiveRelease({ manifestPath, baseUrl, output, trustedPublicKey, browserEvidence }) {
  const manifest = JSON.parse(await readFile(manifestPath, "utf8"));
  verifyManifest(manifest, trustedPublicKey);
  assert.equal(manifest.schemaVersion, 2);
  const origin = new URL(baseUrl);
  assert.ok(!origin.username && !origin.password && !origin.search && !origin.hash && origin.pathname === '/');
  assert.ok(origin.protocol === 'https:' || origin.protocol === 'http:' && ['localhost', '127.0.0.1', '[::1]'].includes(origin.hostname));
  baseUrl = origin.origin;
  const json = async path => JSON.parse((await fetchBytes(baseUrl + path)).bytes.toString('utf8'));
  const readiness = await probeRuntime(manifest.expectedRuntime, json);
  const results = [];
  const paths = new Set();
  let bundleText = "";
  for (const file of manifest.files) {
    assert.ok(file.path === 'index.html' || /^assets\/[A-Za-z0-9_./-]+$/.test(file.path) && !file.path.includes('..'));
    assert.ok(!paths.has(file.path), 'Duplicate manifest asset'); paths.add(file.path);
    const url = file.path === "index.html"
      ? `${baseUrl}/ui/${manifest.pageId}`
      : `${baseUrl}/ui/${manifest.pageId}/${file.path}`;
    const live = await fetchBytes(url);
    const actual = { path: file.path, length: live.bytes.length, sha256: sha256(live.bytes), cacheControl: live.cacheControl };
    if (actual.length !== file.length || actual.sha256 !== file.sha256) {
      throw new Error(`Live drift for ${file.path}.`);
    }
    if (file.path === "index.html" && !/private/.test(live.cacheControl) ||
        file.path === "index.html" && !/no-store/.test(live.cacheControl)) {
      throw new Error("Live HTML is not private, no-store.");
    }
    if (file.path.startsWith("assets/") && !/private/.test(live.cacheControl) ||
        file.path.startsWith("assets/") && !/immutable/.test(live.cacheControl)) {
      throw new Error(`${file.path} is not private and immutable.`);
    }
    if (/\.(?:html|js|css)$/.test(file.path)) bundleText += live.bytes.toString("utf8");
    results.push(actual);
  }
  const liveHtml = (await fetchBytes(`${baseUrl}/ui/${manifest.pageId}`)).bytes.toString("utf8");
  const liveReferences = extractAssetReferences(liveHtml);
  if (JSON.stringify(liveReferences) !== JSON.stringify(manifest.assetReferences)) {
    throw new Error("Live HTML asset references differ from the release manifest.");
  }
  const retiredSignatureMatches = findRetiredMapSignatures(bundleText);
  if (retiredSignatureMatches.length) throw new Error("Live bundle contains a retired map signature.");
  assert.ok(paths.has('index.html'));
  verifyBrowserEvidence(manifest, baseUrl, browserEvidence);
  verifyRuntimeTarget(manifest.expectedRuntime, await json(`/api/readiness/applications/${encodeURIComponent(manifest.expectedRuntime.applicationId)}`), await json('/api/audience-context'));
  const report = { status: 'passed', verifiedAtUtc: new Date().toISOString(), baseUrl, pageId: manifest.pageId,
    manifestFingerprint: sha256(Buffer.from(canonicalJson(manifest))), readiness, files: results, retiredSignatureMatches, browserEvidence };
  if (output) await writeFile(output, `${JSON.stringify(report, null, 2)}\n`, "utf8");
  return report;
}

function argument(name, fallback) {
  const position = process.argv.indexOf(name);
  return position < 0 ? fallback : process.argv[position + 1];
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const manifestPath = resolve(argument("--manifest", resolve(webRoot, ".tmp/release-manifest.json")));
  const output = resolve(argument("--output", resolve(webRoot, ".tmp/live-release-verification.json")));
  const baseUrl = argument("--base-url", "http://localhost:6217").replace(/\/$/, "");
  const key = argument('--trusted-public-key');
  const browser = argument('--browser-evidence');
  assert.ok(key && browser, '--trusted-public-key and --browser-evidence are required');
  const report = await verifyLiveRelease({ manifestPath, baseUrl, output,
    trustedPublicKey: await readFile(key, 'utf8'), browserEvidence: JSON.parse(await readFile(browser, 'utf8')) });
  process.stdout.write(`${JSON.stringify({ output, pageId: report.pageId, files: report.files.length }, null, 2)}\n`);
}
