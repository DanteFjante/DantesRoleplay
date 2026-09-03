import { createHash } from "node:crypto";
import { execFileSync } from "node:child_process";
import { mkdir, readFile, readdir, stat, writeFile } from "node:fs/promises";
import { basename, dirname, relative, resolve, sep } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const webRoot = resolve(fileURLToPath(new URL("..", import.meta.url)));
const repositoryRoot = resolve(webRoot, "../../../../");
const retiredMapSignatures = [
  "pinch, scroll, or use + and − to zoom",
];

export function sha256(bytes) {
  return createHash("sha256").update(bytes).digest("hex").toUpperCase();
}

export function extractAssetReferences(html) {
  return [...html.matchAll(/(?:src|href)=["']\/ui\/dnd2024-play\/(assets\/[^"']+)["']/g)]
    .map((match) => match[1])
    .sort();
}

export function isContentAddressedAsset(path) {
  const fileName = basename(path).replace(/\.[^.]+$/, "");
  return /-[A-Za-z0-9_-]{8,64}$/.test(fileName);
}

export function findRetiredMapSignatures(text) {
  return retiredMapSignatures.filter((signature) => text.includes(signature));
}

async function walk(root, current = root) {
  const entries = await readdir(current, { withFileTypes: true });
  const files = [];
  for (const entry of entries) {
    const fullPath = resolve(current, entry.name);
    if (entry.isDirectory()) files.push(...await walk(root, fullPath));
    else if (entry.isFile()) files.push(fullPath);
  }
  return files.sort();
}

async function sourceFingerprint() {
  const ignored = new Set(["node_modules", "server-dist", "baseline"]);
  const inputs = [];
  async function visit(current) {
    for (const entry of await readdir(current, { withFileTypes: true })) {
      if (entry.isDirectory() && ignored.has(entry.name)) continue;
      const fullPath = resolve(current, entry.name);
      if (entry.isDirectory()) await visit(fullPath);
      else if (entry.isFile()) {
        const bytes = await readFile(fullPath);
        inputs.push(`${relative(webRoot, fullPath).split(sep).join("/")}\0${sha256(bytes)}`);
      }
    }
  }
  await visit(webRoot);
  return sha256(Buffer.from(inputs.sort().join("\n")));
}

function git(...args) {
  return execFileSync("git", args, { cwd: repositoryRoot, encoding: "utf8" }).trim();
}

export async function createReleaseManifest({ dist = resolve(webRoot, "server-dist"), output }) {
  const source = await readFile(resolve(webRoot, "src/components/MapCanvas.tsx"), "utf8");
  if (/onWheel=/.test(source) || /addEventListener\(\s*["']wheel["']/.test(source)) {
    throw new Error("MapCanvas still installs a wheel zoom handler.");
  }
  if (!source.includes("Page scrolling and browser gestures never change map zoom.")) {
    throw new Error("MapCanvas help does not state the wheel behavior contract.");
  }

  const filePaths = await walk(dist);
  if (!filePaths.some((path) => basename(path) === "index.html")) {
    throw new Error("The production build has no index.html.");
  }
  const files = [];
  let bundleText = "";
  for (const filePath of filePaths) {
    const bytes = await readFile(filePath);
    const path = relative(dist, filePath).split(sep).join("/");
    files.push({ path, length: bytes.length, sha256: sha256(bytes) });
    if (/\.(?:html|js|css)$/.test(path)) bundleText += bytes.toString("utf8");
  }
  const retiredSignatureMatches = findRetiredMapSignatures(bundleText);
  if (retiredSignatureMatches.length) {
    throw new Error(`The bundle contains retired map behavior: ${retiredSignatureMatches.join(", ")}`);
  }
  const unhashedAssets = files.filter((file) => file.path.startsWith("assets/") && !isContentAddressedAsset(file.path));
  if (unhashedAssets.length) {
    throw new Error(`Assets are not content-addressed: ${unhashedAssets.map((file) => file.path).join(", ")}`);
  }

  const html = await readFile(resolve(dist, "index.html"), "utf8");
  const assetReferences = extractAssetReferences(html);
  for (const reference of assetReferences) {
    if (!files.some((file) => file.path === reference)) throw new Error(`HTML references missing asset ${reference}.`);
  }
  const packageDocument = JSON.parse(await readFile(resolve(webRoot, "package.json"), "utf8"));
  const status = git("status", "--porcelain=v1", "--", "src/system/web-interface/dnd2024");
  const manifest = {
    schemaVersion: 1,
    pageId: "dnd2024-play",
    generatedAtUtc: new Date().toISOString(),
    source: {
      commit: git("rev-parse", "HEAD"),
      dirty: status.length > 0,
      workingTreeFingerprint: await sourceFingerprint(),
    },
    tools: {
      node: process.version,
      typescript: packageDocument.devDependencies.typescript,
      vite: packageDocument.devDependencies.vite,
    },
    expectedMapContract: {
      ordinaryWheelChangesZoom: false,
      ordinaryWheelPreventsDefault: false,
      zoomControls: ["zoom-out", "zoom-in", "fit", "reset", "focus-selected"],
      keyboardPan: ["ArrowLeft", "ArrowRight", "ArrowUp", "ArrowDown"],
      touchAction: "pan-y pinch-zoom",
    },
    assetReferences,
    retiredSignatureMatches,
    files,
  };
  await mkdir(dirname(output), { recursive: true });
  await writeFile(output, `${JSON.stringify(manifest, null, 2)}\n`, "utf8");
  return manifest;
}

function argument(name, fallback) {
  const position = process.argv.indexOf(name);
  return position < 0 ? fallback : process.argv[position + 1];
}

if (import.meta.url === pathToFileURL(process.argv[1]).href) {
  const output = resolve(argument("--output", resolve(webRoot, ".tmp/release-manifest.json")));
  const dist = resolve(argument("--dist", resolve(webRoot, "server-dist")));
  const manifest = await createReleaseManifest({ dist, output });
  process.stdout.write(`${JSON.stringify({ output, files: manifest.files.length, source: manifest.source }, null, 2)}\n`);
}
