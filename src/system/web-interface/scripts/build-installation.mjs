import { createHash } from "node:crypto";
import { mkdir, readFile, readdir, writeFile } from "node:fs/promises";
import { existsSync } from "node:fs";
import { dirname, relative, resolve, sep } from "node:path";
import { fileURLToPath } from "node:url";

const scriptRoot = dirname(fileURLToPath(import.meta.url));
const expectedTemplateKeys = [
  "application", "baseApplications", "catalogRoot", "format", "media", "pages", "runtime", "source", "stateSpaces",
];

function fail(message) { throw new Error(message); }
function sha256(bytes) { return createHash("sha256").update(bytes).digest("hex").toUpperCase(); }
function portable(path) { return path.split(sep).join("/"); }
function argument(name) {
  const index = process.argv.indexOf(name);
  if (index < 0 || index + 1 >= process.argv.length) fail(`Missing required ${name} argument.`);
  return process.argv[index + 1];
}
function exactKeys(value, expected, label) {
  const actual = Object.keys(value).sort();
  const wanted = [...expected].sort();
  if (JSON.stringify(actual) !== JSON.stringify(wanted))
    fail(`${label} has unexpected fields: ${actual.join(", ")}.`);
}
function isSafeRelative(path) {
  return typeof path === "string" && path.length > 0 && !/^[A-Za-z]:/.test(path) && !path.startsWith("/")
    && path.split(/[\\/]/).every(segment => segment && segment !== "." && segment !== "..");
}

function crc32(bytes) {
  let value = 0xffffffff;
  for (const byte of bytes) {
    value ^= byte;
    for (let bit = 0; bit < 8; bit++) value = (value >>> 1) ^ (0xedb88320 & -(value & 1));
  }
  return (value ^ 0xffffffff) >>> 0;
}
function zip(entries) {
  const local = [];
  const central = [];
  let offset = 0;
  for (const entry of entries.sort((left, right) => left.path.localeCompare(right.path))) {
    if (!isSafeRelative(entry.path) || entry.path.includes("\\")) fail(`Unsafe ZIP entry ${entry.path}.`);
    const name = Buffer.from(entry.path, "utf8");
    const content = Buffer.from(entry.content);
    const checksum = crc32(content);
    const header = Buffer.alloc(30);
    header.writeUInt32LE(0x04034b50, 0); header.writeUInt16LE(20, 4); header.writeUInt16LE(0x0800, 6);
    header.writeUInt16LE(0, 8); header.writeUInt16LE(0, 10); header.writeUInt16LE(0x0021, 12);
    header.writeUInt32LE(checksum, 14); header.writeUInt32LE(content.length, 18); header.writeUInt32LE(content.length, 22);
    header.writeUInt16LE(name.length, 26); header.writeUInt16LE(0, 28);
    local.push(header, name, content);
    const directory = Buffer.alloc(46);
    directory.writeUInt32LE(0x02014b50, 0); directory.writeUInt16LE(20, 4); directory.writeUInt16LE(20, 6);
    directory.writeUInt16LE(0x0800, 8); directory.writeUInt16LE(0, 10); directory.writeUInt16LE(0, 12);
    directory.writeUInt16LE(0x0021, 14); directory.writeUInt32LE(checksum, 16);
    directory.writeUInt32LE(content.length, 20); directory.writeUInt32LE(content.length, 24);
    directory.writeUInt16LE(name.length, 28); directory.writeUInt16LE(0, 30); directory.writeUInt16LE(0, 32);
    directory.writeUInt16LE(0, 34); directory.writeUInt16LE(0, 36); directory.writeUInt32LE(0, 38);
    directory.writeUInt32LE(offset, 42); central.push(directory, name);
    offset += header.length + name.length + content.length;
  }
  const centralBytes = Buffer.concat(central);
  const end = Buffer.alloc(22);
  end.writeUInt32LE(0x06054b50, 0); end.writeUInt16LE(0, 4); end.writeUInt16LE(0, 6);
  end.writeUInt16LE(entries.length, 8); end.writeUInt16LE(entries.length, 10);
  end.writeUInt32LE(centralBytes.length, 12); end.writeUInt32LE(offset, 16); end.writeUInt16LE(0, 20);
  return Buffer.concat([...local, centralBytes, end]);
}

async function files(root, current = root) {
  const result = [];
  for (const entry of await readdir(current, { withFileTypes: true })) {
    const path = resolve(current, entry.name);
    if (entry.isDirectory()) result.push(...await files(root, path));
    else if (entry.isFile()) result.push({ path: portable(relative(root, path)), content: await readFile(path) });
  }
  return result;
}
async function objectReferences(root) {
  const references = new Map();
  const objectRoot = resolve(root, "catalog/applications/dnd2024/objects");
  function visit(value) {
    if (!value || typeof value !== "object") return;
    if (typeof value.qualifiedId === "string" && Number.isInteger(value.version) && value.version > 0
      && typeof value.schemaHash === "string" && /^[A-F0-9]{64}$/.test(value.schemaHash)) {
      const key = `${value.qualifiedId}@${value.version}`;
      const previous = references.get(key);
      if (previous && previous !== value.schemaHash) fail(`Object contracts disagree about ${key}.`);
      references.set(key, value.schemaHash);
    }
    for (const child of Object.values(value)) visit(child);
  }
  for (const file of await files(objectRoot)) if (file.path.endsWith(".json")) visit(JSON.parse(file.content));
  return references;
}
async function contentComponentIds(root) {
  const ids = new Set();
  const contentRoot = resolve(root, "catalog/applications/dnd2024/content");
  for (const file of await files(contentRoot)) {
    if (!file.path.endsWith(".json")) continue;
    const document = JSON.parse(file.content);
    if (!document.components || typeof document.components !== "object" || Array.isArray(document.components)) continue;
    for (const id of Object.keys(document.components)) ids.add(id);
  }
  return ids;
}
function owner(qualifiedTypeId) {
  if (qualifiedTypeId.startsWith("game.")) return "game";
  if (qualifiedTypeId.startsWith("system.")) return "system";
  if (qualifiedTypeId.startsWith("dnd2024.")) return "dnd2024";
  fail(`No installation owner for component type ${qualifiedTypeId}.`);
}
function sourceSchemaPath(qualifiedTypeId) {
  if (qualifiedTypeId.startsWith("dnd2024."))
    return `catalog/applications/dnd2024/components/${qualifiedTypeId}.schema.json`;
  return `catalog/components/${qualifiedTypeId.replaceAll(".", "/")}.schema.json`;
}

async function componentTypes(repository, output) {
  const refs = await objectReferences(repository);
  for (const id of await contentComponentIds(repository))
    if (![...refs.keys()].some(key => key.startsWith(`${id}@`))) refs.set(`${id}@1`, null);
  const manual = [
    "game.core.world.root", "game.core.world.location", "game.core.campaign.root",
    "game.core.campaign.current-scene", "game.core.campaign.character-participation",
    "system.web.page", "system.web.index-page",
  ];
  for (const id of manual) if (![...refs.keys()].some(key => key.startsWith(`${id}@`))) refs.set(`${id}@1`, null);
  const result = [];
  for (const [key, targetHash] of [...refs].sort(([left], [right]) => left.localeCompare(right))) {
    const split = key.lastIndexOf("@");
    const id = key.slice(0, split);
    const targetVersion = Number(key.slice(split + 1));
    const sourcePath = sourceSchemaPath(id);
    const sourceAbsolute = resolve(repository, sourcePath);
    if (!existsSync(sourceAbsolute)) fail(`Missing current schema ${sourcePath}.`);
    const bytes = await readFile(sourceAbsolute);
    const record = { ownerApplicationId: owner(id), qualifiedTypeId: id, version: targetVersion,
      root: "source", schemaPath: sourcePath, sha256: sha256(bytes) };
    if (targetHash) record.schemaHash = targetHash;
    result.push(record);
  }
  return result;
}

async function main() {
  const repository = resolve(argument("--repository"));
  const output = resolve(argument("--output"));
  if (!existsSync(resolve(repository, "AGENTS.md")) || !existsSync(resolve(repository, "catalog/manifest.json")))
    fail("--repository is not a DantesRoleplay checkout.");
  if (existsSync(output)) fail("--output must name an absent directory.");
  await mkdir(resolve(output, "pages"), { recursive: true });
  await mkdir(resolve(output, "world"), { recursive: true });

  const templatePath = resolve(repository, "catalog/applications/dnd2024/install/installation.template.json");
  const template = JSON.parse(await readFile(templatePath, "utf8"));
  exactKeys(template, expectedTemplateKeys, "Installation template");
  if (template.format !== "dantesroleplay.installation/1") fail("Unsupported installation template format.");

  const dist = resolve(repository, "src/system/web-interface/dnd2024/server-dist");
  const distFiles = await files(dist);
  if (!distFiles.some(file => file.path === "index.html"))
    fail("DND server-dist is absent. Run npm ci and npm run build:server before packaging.");
  const dndBundle = zip(distFiles);
  const homeBytes = await readFile(resolve(repository, "src/system/web-interface/system-home/index.html"));
  const homeBundle = zip([{ path: "index.html", content: homeBytes }]);
  await writeFile(resolve(output, "pages/dnd2024-play.zip"), dndBundle);
  await writeFile(resolve(output, "pages/home.zip"), homeBundle);

  const types = await componentTypes(repository, output);

  const manifest = structuredClone(template);
  manifest.componentTypes = types;
  manifest.stateSpaces = await Promise.all(manifest.stateSpaces.map(async space => ({
    ...space,
    worldPackages: await Promise.all(space.worldPackages.map(async path => {
      if (!isSafeRelative(path)) fail(`Unsafe world package path ${path}.`);
      const bytes = await readFile(resolve(repository, path));
      const packagePath = `world/${portable(relative(resolve(repository, "catalog/applications/dnd2024/install"), resolve(repository, path)))}`;
      if (!isSafeRelative(packagePath)) fail(`Unsafe packaged world path ${packagePath}.`);
      await writeFile(resolve(output, packagePath), bytes);
      return { path: packagePath, sha256: sha256(bytes) };
    })),
  })));
  manifest.pages = manifest.pages.map(page => {
    if (!isSafeRelative(page.bundlePath)) fail(`Unsafe page bundle path ${page.bundlePath}.`);
    const bytes = page.owner === "system" ? homeBundle : dndBundle;
    const { bundlePath, ...metadata } = page;
    return { ...metadata, bundle: { path: bundlePath, sha256: sha256(bytes) } };
  });
  await writeFile(resolve(output, "installation.json"), `${JSON.stringify(manifest, null, 2)}\n`, "utf8");
  const installationBytes = await readFile(resolve(output, "installation.json"));
  process.stdout.write(`${JSON.stringify({ output, manifest: resolve(output, "installation.json"),
    manifestSha256: sha256(installationBytes),
    componentTypeVersions: manifest.componentTypes.length, bytes: installationBytes.length }, null, 2)}\n`);
}

await main();
