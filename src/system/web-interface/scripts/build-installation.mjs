import { createHash } from "node:crypto";
import { mkdir, readFile, readdir, writeFile } from "node:fs/promises";
import { existsSync } from "node:fs";
import { dirname, relative, resolve, sep } from "node:path";
import { fileURLToPath } from "node:url";

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
const componentArrayFields = new Set(["components", "optionalComponents", "contentComponentIds",
  "contentFilterComponentIds", "targetComponentIds", "optionalTargetComponentIds", "componentIds", "effectComponentIds"]);
export function mechanicComponentIds(requirements) {
  const ids = new Set();
  function visit(value) {
    if (!value || typeof value !== "object") return;
    for (const [key, child] of Object.entries(value)) {
      // Input schemas and child-call payloads describe values, not host snapshot dependencies.
      if (["inputSchema", "input", "outputSchema"].includes(key)) continue;
      if (componentArrayFields.has(key)) {
        if (!Array.isArray(child) || child.some(id => typeof id !== "string" || !id.trim()))
          fail(`Invalid mechanic component declaration ${key}.`);
        for (const id of child) ids.add(id);
      } else if (key === "sourceComponentId") {
        if (typeof child !== "string" || !child.trim()) fail("Invalid component-reference source.");
        ids.add(child);
      } else visit(child);
    }
  }
  visit(requirements);
  return ids;
}
async function activeMechanicComponentIds(repository) {
  const ids = new Set();
  for (const file of await files(resolve(repository, "catalog/applications/dnd2024/mechanics"))) {
    if (!file.path.endsWith(".md")) continue;
    const source = file.content.toString("utf8").replace(/^\uFEFF/u, "");
    const frontmatter = source.match(/^---\s*\r?\n([\s\S]*?)\r?\n---/u)?.[1];
    if (!frontmatter || !/^status:\s*active\s*$/mu.test(frontmatter)) continue;
    const block = source.match(/^## Requirements\s*\r?\n\s*```json\s*\r?\n([\s\S]*?)\r?\n```/mu)?.[1];
    if (!block) fail(`Active mechanic ${file.path} has no readable Requirements contract.`);
    for (const id of mechanicComponentIds(JSON.parse(block))) {
      const qualified = id.includes(".") ? id : `dnd2024.${id}`;
      ids.add(qualified);
    }
  }
  return ids;
}
async function worldComponentIds(repository, template) {
  const ids = new Set();
  const add = components => {
    for (const component of components ?? []) {
      if (typeof component.qualifiedTypeId !== "string" || !component.qualifiedTypeId.trim())
        fail("World package component has no qualified type identity.");
      ids.add(component.qualifiedTypeId);
    }
  };
  for (const space of template.stateSpaces) {
    add(space.root?.components);
    for (const path of space.worldPackages) {
      if (!isSafeRelative(path)) fail(`Unsafe world package path ${path}.`);
      const world = JSON.parse(await readFile(resolve(repository, path), "utf8"));
      for (const entity of world.entities ?? []) add(entity.components);
    }
  }
  return ids;
}
function owner(qualifiedTypeId) {
  if (qualifiedTypeId.startsWith("game.")) return "game";
  if (qualifiedTypeId.startsWith("system.")) return "system";
  if (qualifiedTypeId.startsWith("dnd2024.")) return "dnd2024";
  fail(`No installation owner for component type ${qualifiedTypeId}.`);
}
function sourceSchemaPath(repository, qualifiedTypeId) {
  const paths = [
    ...(qualifiedTypeId.startsWith("dnd2024.")
      ? [`catalog/applications/dnd2024/components/${qualifiedTypeId}.schema.json`] : []),
    `catalog/components/${qualifiedTypeId.replaceAll(".", "/")}.schema.json`,
  ];
  const path = paths.find(candidate => existsSync(resolve(repository, candidate)));
  if (!path) fail(`Missing current schema for required component ${qualifiedTypeId}: ${paths.join(", ")}.`);
  return path;
}

async function requireComponentNamespace(repository, qualifiedTypeId) {
  const namespaceId = qualifiedTypeId.slice(0, qualifiedTypeId.lastIndexOf("."));
  const path = `catalog/namespaces/${namespaceId.replaceAll(".", "/")}/_namespace.json`;
  if (!existsSync(resolve(repository, path))) fail(`Missing namespace ${namespaceId} for required component ${qualifiedTypeId}.`);
  const namespace = JSON.parse(await readFile(resolve(repository, path), "utf8"));
  if (namespace.id !== namespaceId || namespace.enabled !== true || namespace.reviewStatus !== "reviewed" ||
      !namespace.allowedKinds?.includes("component-type"))
    fail(`Namespace ${namespaceId} must be enabled, reviewed, and allow component-type for required component ${qualifiedTypeId}.`);
  for (let index = namespaceId.lastIndexOf("."); index > 0; index = namespaceId.lastIndexOf(".", index - 1)) {
    const parentId = namespaceId.slice(0, index);
    const parent = JSON.parse(await readFile(resolve(repository, `catalog/namespaces/${parentId.replaceAll(".", "/")}/_namespace.json`), "utf8"));
    if (parent.enabled !== true) fail(`Ancestor namespace ${parentId} is disabled for required component ${qualifiedTypeId}.`);
  }
}

export async function componentTypes(repository, template) {
  const refs = await objectReferences(repository);
  const declared = new Set([...(await contentComponentIds(repository)), ...(await activeMechanicComponentIds(repository)),
    ...(await worldComponentIds(repository, template))]);
  for (const id of declared)
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
    await requireComponentNamespace(repository, id);
    const sourcePath = sourceSchemaPath(repository, id);
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

export async function packageMedia(repository, output, entries) {
  const result = [];
  for (const entry of entries) {
    exactKeys(entry, ["sourcePath", "sha256", "mediaType", "byteLength"], "Installation media");
    if (!isSafeRelative(entry.sourcePath)) fail(`Unsafe media source path ${entry.sourcePath}.`);
    const extension = { "image/png": "png", "image/jpeg": "jpg", "image/webp": "webp" }[entry.mediaType];
    if (!extension) fail(`Unsupported installation media type ${entry.mediaType}.`);
    const bytes = await readFile(resolve(repository, entry.sourcePath));
    if (sha256(bytes) !== entry.sha256.toUpperCase() || bytes.length !== entry.byteLength)
      fail(`Installation media bytes do not match reviewed metadata: ${entry.sourcePath}.`);
    const path = `media/${entry.sha256.toLowerCase()}.${extension}`;
    await mkdir(resolve(output, "media"), { recursive: true });
    await writeFile(resolve(output, path), bytes);
    result.push({ path, sha256: sha256(bytes), mediaType: entry.mediaType, byteLength: bytes.length });
  }
  return result;
}

export async function extensionPackages(repository, paths) {
  if (!Array.isArray(paths)) fail("Installation extensionPackages must be an array of source paths.");
  return Promise.all(paths.map(async path => {
    if (!isSafeRelative(path)) fail(`Unsafe extension package path ${path}.`);
    const bytes = await readFile(resolve(repository, path));
    return { path: portable(path), sha256: sha256(bytes), root: "source" };
  }));
}

async function main() {
  const repository = resolve(argument("--repository"));
  const output = resolve(argument("--output"));
  if (!existsSync(resolve(repository, "AGENTS.md")) || !existsSync(resolve(repository, "catalog/manifest.json")))
    fail("--repository is not a DantesRoleplay checkout.");
  if (existsSync(output)) fail("--output must name an absent directory.");
  const profileIndex = process.argv.indexOf("--profile");
  const profile = profileIndex < 0 ? null : process.argv[profileIndex + 1];
  if (profileIndex >= 0 && (typeof profile !== "string" || !/^[a-z0-9][a-z0-9-]*$/u.test(profile)))
    fail("--profile must name a repository installation profile.");
  const templatePath = resolve(repository, "catalog/applications/dnd2024/install", profile ?? "", "installation.template.json");
  const template = JSON.parse(await readFile(templatePath, "utf8"));
  exactKeys(template, expectedTemplateKeys, "Installation template");
  if (template.format !== "dantesroleplay.installation/1") fail("Unsupported installation template format.");
  const types = await componentTypes(repository, template);
  const extensions = template.application.extensionPackages === undefined ? undefined
    : await extensionPackages(repository, template.application.extensionPackages);
  await mkdir(resolve(output, "pages"), { recursive: true });
  await mkdir(resolve(output, "world"), { recursive: true });

  const dist = resolve(repository, "src/system/web-interface/dnd2024/server-dist");
  const distFiles = await files(dist);
  if (!distFiles.some(file => file.path === "index.html"))
    fail("DND server-dist is absent. Run npm ci and npm run build:server before packaging.");
  const dndBundle = zip(distFiles);
  const homeBytes = await readFile(resolve(repository, "src/system/web-interface/system-home/index.html"));
  const homeBundle = zip([{ path: "index.html", content: homeBytes }]);
  await writeFile(resolve(output, "pages/dnd2024-play.zip"), dndBundle);
  await writeFile(resolve(output, "pages/home.zip"), homeBundle);

  const manifest = structuredClone(template);
  if (extensions !== undefined) manifest.application.extensionPackages = extensions;
  manifest.componentTypes = types;
  manifest.media = await packageMedia(repository, output, template.media);
  manifest.stateSpaces = await Promise.all(manifest.stateSpaces.map(async space => ({
    ...space,
    worldPackages: await Promise.all(space.worldPackages.map(async path => {
      if (!isSafeRelative(path)) fail(`Unsafe world package path ${path}.`);
      const bytes = await readFile(resolve(repository, path));
      const packagePath = `world/${portable(relative(resolve(repository, "catalog/applications/dnd2024/install"), resolve(repository, path)))}`;
      if (!isSafeRelative(packagePath)) fail(`Unsafe packaged world path ${packagePath}.`);
      await mkdir(dirname(resolve(output, packagePath)), { recursive: true });
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

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) await main();
