import assert from "node:assert/strict";
import test from "node:test";
import { createHash } from "node:crypto";
import { mkdir, mkdtemp, readFile, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { dirname, resolve, sep } from "node:path";
import { fileURLToPath } from "node:url";
import { componentTypes, extensionPackages, packageMedia } from "../../scripts/build-installation.mjs";

const manualTypes = ["game.core.world.root", "game.core.world.location", "game.core.campaign.root",
  "game.core.campaign.current-scene", "game.core.campaign.character-participation", "system.web.page", "system.web.index-page"];
const schema = { type: "object", additionalProperties: false, properties: {} };

async function fixture(t) {
  const root = await mkdtemp(resolve(tmpdir(), "roleplay-installation-test-"));
  t.after(async () => {
    assert.ok(resolve(root).startsWith(resolve(tmpdir()) + sep));
    await rm(root, { recursive: true, force: true });
  });
  async function write(path, value) {
    await mkdir(dirname(resolve(root, path)), { recursive: true });
    await writeFile(resolve(root, path), typeof value === "string" || Buffer.isBuffer(value) ? value : JSON.stringify(value));
  }
  async function type(id, legacy = false) {
    const source = id.startsWith("dnd2024.") && !legacy
      ? `catalog/applications/dnd2024/components/${id}.schema.json`
      : `catalog/components/${id.replaceAll(".", "/")}.schema.json`;
    await write(source, schema);
    const parts = id.split(".");
    for (let length = 1; length < parts.length; length++) {
      const namespaceId = parts.slice(0, length).join(".");
      await write(`catalog/namespaces/${parts.slice(0, length).join("/")}/_namespace.json`, {
        id: namespaceId, owner: parts[0], allowedKinds: ["component-type"], enabled: true, reviewStatus: "reviewed",
      });
    }
  }
  for (const directory of ["objects", "content", "mechanics"])
    await mkdir(resolve(root, "catalog/applications/dnd2024", directory), { recursive: true });
  for (const id of manualTypes) await type(id);
  async function mechanic(name, requirements, status = "active") {
    await write(`catalog/applications/dnd2024/mechanics/${name}.md`,
      `---\nid: dnd2024.mechanic.${name}\nstatus: ${status}\n---\n\n## Requirements\n\n\`\`\`json\n${JSON.stringify(requirements)}\n\`\`\`\n`);
  }
  return { root, write, type, mechanic };
}

test("installation includes optional, referenced, contained and child mechanic types before any entity uses them", async t => {
  const f = await fixture(t);
  const ids = ["dnd2024.required", "dnd2024.optional", "dnd2024.contained", "dnd2024.target", "dnd2024.optional-target",
    "dnd2024.related", "dnd2024.event", "dnd2024.effect", "dnd2024.graph", "dnd2024.graph-step", "dnd2024.child-only"];
  for (const id of ids) await f.type(id);
  await f.mechanic("parent", {
    roles: { subject: { components: ["required"], optionalComponents: ["dnd2024.optional"],
      contentComponentIds: ["dnd2024.contained"], contentFilterComponentIds: ["dnd2024.contained"],
      componentReferences: [{ sourceComponentId: "dnd2024.required", targetComponentIds: ["dnd2024.target"],
        optionalTargetComponentIds: ["dnd2024.optional-target"] }],
      relationshipComponents: [{ targetComponentIds: ["dnd2024.related"] }] } },
    event: { components: ["dnd2024.event"] }, effectComponentIds: ["dnd2024.effect"],
    graphSnapshots: { nearby: { componentIds: ["dnd2024.graph"], steps: [{ componentIds: ["dnd2024.graph-step"] }] } },
    children: { child: { mechanicId: "dnd2024.mechanic.child" } },
    inputSchema: { properties: { components: { type: "array" } } },
  });
  await f.mechanic("child", { roles: { subject: { optionalComponents: ["dnd2024.child-only"] } } });
  await f.mechanic("retired", { roles: { subject: { components: ["dnd2024.absent-retired"] } } }, "deprecated");
  const result = await componentTypes(f.root, { stateSpaces: [] });
  assert.deepEqual(result.map(row => row.qualifiedTypeId).sort(), [...manualTypes, ...ids].sort());
  assert.ok(result.every(row => row.version === 1 && /^[A-F0-9]{64}$/u.test(row.sha256)));
});

test("installation preserves object schema pins and includes profile roots, world content and global legacy schemas", async t => {
  const f = await fixture(t);
  for (const id of ["dnd2024.pinned", "dnd2024.world-only", "dnd2024.root-only", "dnd2024.content-only"])
    await f.type(id);
  await f.type("dnd2024.legacy", true);
  await f.write("catalog/applications/dnd2024/objects/record.json", { component: { qualifiedId: "dnd2024.pinned", version: 3, schemaHash: "A".repeat(64) } });
  await f.write("catalog/applications/dnd2024/content/content.json", { components: { "dnd2024.content-only": {} } });
  await f.write("world.json", { entities: [{ components: [{ qualifiedTypeId: "dnd2024.world-only", value: {} },
    { qualifiedTypeId: "dnd2024.legacy", value: {} }] }] });
  const result = await componentTypes(f.root, { stateSpaces: [{ root: { components: [{ qualifiedTypeId: "dnd2024.root-only" }] }, worldPackages: ["world.json"] }] });
  assert.equal(result.find(row => row.qualifiedTypeId === "dnd2024.pinned").version, 3);
  assert.equal(result.find(row => row.qualifiedTypeId === "dnd2024.pinned").schemaHash, "A".repeat(64));
  assert.equal(result.find(row => row.qualifiedTypeId === "dnd2024.legacy").schemaPath, "catalog/components/dnd2024/legacy.schema.json");
  for (const id of ["dnd2024.world-only", "dnd2024.root-only", "dnd2024.content-only"])
    assert.ok(result.some(row => row.qualifiedTypeId === id), id);
});

test("installation fails explicitly for absent required schemas and forbidden or unreviewed namespaces", async t => {
  const f = await fixture(t);
  await f.type("dnd2024.optional");
  await f.mechanic("required-view", { roles: { subject: { optionalComponents: ["dnd2024.optional"] } } });
  await f.write("catalog/namespaces/dnd2024/_namespace.json", { id: "dnd2024", enabled: true, reviewStatus: "reviewed", allowedKinds: ["event-type"] });
  await assert.rejects(componentTypes(f.root, { stateSpaces: [] }), /Namespace dnd2024 must.*component-type.*dnd2024.optional/u);
  await f.write("catalog/namespaces/dnd2024/_namespace.json", { id: "dnd2024", enabled: true, reviewStatus: "pending", allowedKinds: ["component-type"] });
  await assert.rejects(componentTypes(f.root, { stateSpaces: [] }), /Namespace dnd2024 must be enabled, reviewed/u);
  await f.type("dnd2024.optional");
  await f.mechanic("missing-view", { roles: { subject: { optionalComponents: ["dnd2024.absent"] } } });
  await assert.rejects(componentTypes(f.root, { stateSpaces: [] }), /Missing current schema for required component dnd2024.absent/u);
});

test("installation media uses reviewed hashes and lengths with collision-free package paths", async t => {
  const f = await fixture(t);
  const bytes = Buffer.from("reviewed media bytes");
  await f.write("source/image.bin", bytes);
  const hash = createHash("sha256").update(bytes).digest("hex");
  const entry = { sourcePath: "source/image.bin", sha256: hash, mediaType: "image/png", byteLength: bytes.length };
  const result = await packageMedia(f.root, resolve(f.root, "output"), [entry]);
  assert.equal(result[0].path, `media/${hash}.png`);
  assert.deepEqual(await readFile(resolve(f.root, "output", result[0].path)), bytes);
  assert.equal(Object.hasOwn(result[0], "sourcePath"), false);
  await assert.rejects(packageMedia(f.root, resolve(f.root, "output"), [{ ...entry, byteLength: bytes.length + 1 }]), /do not match reviewed metadata/u);
  await assert.rejects(packageMedia(f.root, resolve(f.root, "output"), [{ ...entry, sourcePath: "../foreign.png" }]), /Unsafe media source path/u);
});

test("installation pins declared extension packages to the reviewed source bytes", async t => {
  const f = await fixture(t);
  const path = "catalog/extensions/dnd2024/local/extension-package.json";
  const bytes = Buffer.from('{"extensionId":"local"}\n');
  await f.write(path, bytes);
  assert.deepEqual(await extensionPackages(f.root, [path]), [{
    path, root: "source", sha256: createHash("sha256").update(bytes).digest("hex").toUpperCase(),
  }]);
  await assert.rejects(extensionPackages(f.root, ["../extension-package.json"]), /Unsafe extension package path/u);
  await assert.rejects(extensionPackages(f.root, [resolve(f.root, path)]), /Unsafe extension package path/u);
  await assert.rejects(extensionPackages(f.root, path), /must be an array/u);
});

test("the authored installation closes optional dossier and campaign dependencies", async () => {
  const repository = resolve(dirname(fileURLToPath(import.meta.url)), "../../../../..");
  const template = JSON.parse(await readFile(resolve(repository, "catalog/applications/dnd2024/install/installation.template.json"), "utf8"));
  const result = await componentTypes(repository, template);
  for (const id of ["dnd2024.item.armor", "dnd2024.character.magic-initiate-configuration",
    "dnd2024.character.starting-equipment-adoption", "dnd2024.spellcasting.access", "game.core.campaign.session-recap",
    "dnd2024.downtime.activity", "dnd2024.downtime.definition"])
    assert.ok(result.some(row => row.qualifiedTypeId === id), `Missing live-view dependency ${id}`);
});

test("the source schema retains exact constrained source values without unsupported format annotations", async () => {
  const source = JSON.parse(await readFile(new URL("../../../../../catalog/components/dnd2024/source.schema.json", import.meta.url), "utf8"));
  for (const [value, expected] of [
    [source.properties.publishedOn, "2025-05-01"],
    [source.properties.canonicalUrl, "https://www.dndbeyond.com/srd"],
    [source.properties.documentUrl, "https://media.dndbeyond.com/compendium-images/srd/5.2/SRD_CC_v5.2.pdf"],
    [source.properties.license.properties.url, "https://creativecommons.org/licenses/by/4.0/legalcode"],
  ]) {
    assert.equal(value.const, expected);
    assert.equal(value.type, "string");
    assert.equal(Object.hasOwn(value, "format"), false);
  }
  for (const required of ["publishedOn", "canonicalUrl", "documentUrl", "license"])
    assert.ok(source.required.includes(required));
  assert.ok(source.properties.license.required.includes("url"));
});
