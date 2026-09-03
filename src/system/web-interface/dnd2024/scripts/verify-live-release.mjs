import { readFile, writeFile } from "node:fs/promises";
import { resolve } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";
import { extractAssetReferences, findRetiredMapSignatures, sha256 } from "./create-release-manifest.mjs";

const webRoot = resolve(fileURLToPath(new URL("..", import.meta.url)));

async function fetchBytes(url) {
  const response = await fetch(url, { cache: "no-store" });
  const bytes = Buffer.from(await response.arrayBuffer());
  if (!response.ok) throw new Error(`${url} returned ${response.status}.`);
  return { bytes, cacheControl: response.headers.get("cache-control") ?? "" };
}

export async function verifyLiveRelease({ manifestPath, baseUrl, output }) {
  const manifest = JSON.parse(await readFile(manifestPath, "utf8"));
  const results = [];
  let bundleText = "";
  for (const file of manifest.files) {
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
  const report = { verifiedAtUtc: new Date().toISOString(), baseUrl, pageId: manifest.pageId, files: results, retiredSignatureMatches };
  if (output) await writeFile(output, `${JSON.stringify(report, null, 2)}\n`, "utf8");
  return report;
}

function argument(name, fallback) {
  const position = process.argv.indexOf(name);
  return position < 0 ? fallback : process.argv[position + 1];
}

if (import.meta.url === pathToFileURL(process.argv[1]).href) {
  const manifestPath = resolve(argument("--manifest", resolve(webRoot, ".tmp/release-manifest.json")));
  const output = resolve(argument("--output", resolve(webRoot, ".tmp/live-release-verification.json")));
  const baseUrl = argument("--base-url", "http://localhost:6217").replace(/\/$/, "");
  const report = await verifyLiveRelease({ manifestPath, baseUrl, output });
  process.stdout.write(`${JSON.stringify({ output, pageId: report.pageId, files: report.files.length }, null, 2)}\n`);
}
