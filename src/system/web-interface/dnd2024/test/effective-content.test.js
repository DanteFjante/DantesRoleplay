import assert from "node:assert/strict";
import test from "node:test";

import { InstalledContentClient, readInstalledContent } from "../src/server/effective-content.ts";
import { ViewReadError } from "../src/data/view-read-client.ts";

function response(payload) {
  return new Response(JSON.stringify(payload), {
    status: 200,
    headers: { "Content-Type": "application/json" },
  });
}

test("installed content reads active extension provenance and keeps additive records", async () => {
  const requested = [];
  const fetchImpl = async (url) => {
    requested.push(String(url));
    return response({
      applicationId: "dnd2024",
      resolutionFingerprint: "A".repeat(64),
      activeExtensions: [{
        extensionId: "caldris-homebrew",
        displayName: "Caldris Homebrew",
        description: "Reviewed Caldris additions.",
        classification: "homebrew",
        sourceIds: ["dnd2024-extension.caldris-homebrew"],
        namespaceIds: ["dnd2024.extension.caldris"],
      }],
      resolvedWinners: Array.from({ length: 4 }, (_, index) => ({
        record: {
          qualifiedId: `dnd2024.extension.caldris.content.species.fixture-${index}.v1`,
          name: `Caldris contribution ${index + 1}`,
          description: "Caldris extension content.",
          kind: "entity",
          path: "entities/character-creation/species",
        },
        ownerId: "caldris-homebrew",
        sourceLabel: "Caldris Homebrew",
        classification: "homebrew",
        presentationRoles: ["entity", "character-creation", "species"],
        isAdditive: true,
      })),
      additiveExtensionContent: [],
      availableKinds: ["entity"],
      totalCount: 4,
      nextCursor: null,
    });
  };

  const result = await readInstalledContent({
    serverOrigin: "https://localhost:5144",
    applicationId: "dnd2024",
    fetchImpl,
  });

  assert.equal(result.extensions[0].displayName, "Caldris Homebrew");
  assert.equal(result.records[0].isAdditive, true);
  assert.equal(result.records.length, 4);
  assert.equal(result.totalCount, 4);
  assert.equal(requested.length, 1);
  assert.match(requested[0], /\/api\/applications\/dnd2024\/content\?limit=100&extensionsOnly=true/u);
  assert.doesNotMatch(requested[0], /extensionId|overlay/u);
});

test("installed content sends normalized server-side filters and one explicit continuation", async () => {
  let requested = "";
  const result = await readInstalledContent({
    serverOrigin: "https://localhost:5144",
    applicationId: "dnd2024",
    request: {
      ownerId: "caldris-homebrew",
      kinds: ["procedure", "entity", "entity"],
      query: "  spark  ",
      cursor: "next-page",
      expectedResolutionFingerprint: "A".repeat(64),
    },
    fetchImpl: async (url) => {
      requested = String(url);
      return response({
        applicationId: "dnd2024",
        resolutionFingerprint: "A".repeat(64),
        activeExtensions: [],
        resolvedWinners: [],
        additiveExtensionContent: [],
        availableKinds: ["entity", "procedure"],
        totalCount: 0,
        nextCursor: null,
      });
    },
  });

  assert.equal(result.totalCount, 0);
  const url = new URL(requested);
  assert.equal(url.searchParams.get("extensionsOnly"), "true");
  assert.equal(url.searchParams.get("owner"), "caldris-homebrew");
  assert.deepEqual(url.searchParams.getAll("kind"), ["entity", "procedure"]);
  assert.equal(url.searchParams.get("query"), "spark");
  assert.equal(url.searchParams.get("cursor"), "next-page");
});

test("installed content client coordinates only in-flight reads; Redux owns completed pages", async () => {
  let requests = 0;
  let responseBytes = 0;
  const client = new InstalledContentClient({
    serverOrigin: "https://localhost:5144",
    fetchImpl: async () => {
      requests += 1;
      const payload = {
        applicationId: "dnd2024",
        resolutionFingerprint: "A".repeat(64),
        activeExtensions: [],
        resolvedWinners: [],
        additiveExtensionContent: [],
        availableKinds: [],
        totalCount: 0,
        nextCursor: null,
      };
      responseBytes += new TextEncoder().encode(JSON.stringify(payload)).byteLength;
      return response(payload);
    },
  });
  const request = {
    ownerId: null, kinds: [], query: "", cursor: null, expectedResolutionFingerprint: null,
  };

  const cold = await client.load(request);
  await client.load(request);

  assert.ok(cold.records.length <= 100);
  assert.equal(requests, 2);
  assert.ok(responseBytes <= 2 * 524_288);
});

test("installed content retains a safe identity when optional display metadata is malformed", async () => {
  const result = await readInstalledContent({
    serverOrigin: "https://localhost:5144", applicationId: "dnd2024",
    fetchImpl: async () => response({
      applicationId: "dnd2024", resolutionFingerprint: "A".repeat(64),
      activeExtensions: [{ extensionId: "caldris-homebrew", displayName: "Caldris", description: null, classification: "unfamiliar" }],
      resolvedWinners: [{ record: { qualifiedId: "dnd2024.extension.caldris.content.fixture.v1", name: null,
        description: null, kind: null, path: null }, ownerId: "caldris-homebrew", sourceLabel: null,
        classification: "unfamiliar", presentationRoles: ["entity", null], isAdditive: null }],
      availableKinds: ["entity", "not a kind"], totalCount: "unknown", nextCursor: null,
    }),
  });
  assert.equal(result.records[0].id, "dnd2024.extension.caldris.content.fixture.v1");
  assert.equal(result.records[0].name, "Unnamed contribution");
  assert.equal(result.records[0].classification, "unknown");
  assert.equal(result.records[0].description, null);
  assert.equal(result.totalCount, null);
  assert.equal(result.coverage, "partial");
});

test("installed content rejects a changed resolution between pages", async () => {
  await assert.rejects(readInstalledContent({
    serverOrigin: "https://localhost:5144",
    applicationId: "dnd2024",
    request: {
      ownerId: null, kinds: [], query: "", cursor: "next",
      expectedResolutionFingerprint: "A".repeat(64),
    },
    fetchImpl: async () => response({
      applicationId: "dnd2024",
      resolutionFingerprint: "B".repeat(64),
      activeExtensions: [],
      resolvedWinners: [],
      additiveExtensionContent: [],
      availableKinds: [],
      totalCount: 0,
      nextCursor: null,
    }),
  }), (error) => error instanceof ViewReadError && error.category === "stale-data");
});

test("installed content passes cancellation to the network request", async () => {
  const controller = new AbortController();
  const pending = readInstalledContent({
    serverOrigin: "https://localhost:5144",
    applicationId: "dnd2024",
    signal: controller.signal,
    fetchImpl: async (_url, init) => new Promise((_resolve, reject) => {
      init.signal.addEventListener("abort", () => reject(new DOMException("aborted", "AbortError")), { once: true });
    }),
  });
  controller.abort();
  await assert.rejects(pending, (error) => error instanceof DOMException && error.name === "AbortError");
});

test("installed content exposes transport failures for local retry", async () => {
  await assert.rejects(readInstalledContent({
    serverOrigin: "https://localhost:5144",
    applicationId: "dnd2024",
    fetchImpl: async () => new Response(null, { status: 503 }),
  }), (error) => error instanceof ViewReadError && error.category === "transport");
});

test("installed content rejects an oversized page before retaining response data", async () => {
  await assert.rejects(readInstalledContent({
    serverOrigin: "https://localhost:5144",
    applicationId: "dnd2024",
    fetchImpl: async () => new Response("x", {
      status: 200,
      headers: { "Content-Length": "524289" },
    }),
  }), /invalid/u);
});
