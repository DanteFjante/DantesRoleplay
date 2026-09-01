import assert from "node:assert/strict";
import test from "node:test";

import { readInstalledContent } from "../src/server/effective-content.ts";

function response(payload) {
  return { ok: true, json: async () => payload };
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
      resolvedWinners: [{
        record: {
          qualifiedId: "dnd2024.extension.caldris.content.species.half-elf.v1",
          name: "Half-Elf",
          description: "Caldris species.",
          kind: "entity",
          path: "entities/character-creation/species",
        },
        ownerId: "caldris-homebrew",
        sourceLabel: "Caldris Homebrew",
        classification: "homebrew",
        presentationRoles: ["entity", "character-creation", "species"],
        isAdditive: true,
      }],
      additiveExtensionContent: [],
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
  assert.equal(requested.length, 1);
  assert.match(requested[0], /\/api\/applications\/dnd2024\/content\?limit=100/u);
  assert.doesNotMatch(requested[0], /extensionId|overlay/u);
});
