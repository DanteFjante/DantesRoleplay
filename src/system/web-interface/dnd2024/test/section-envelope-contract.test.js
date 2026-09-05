import assert from "node:assert/strict";
import test from "node:test";
import { isReadyHubEnvelope } from "../src/state.js";
import { projectHubEnvelope } from "../src/server/hub-envelope.js";
import { resolveAudience } from "../src/server/audience-policy.js";
import { hubSource, HUB_SOURCE_REVISION } from "../src/server/hub-source.js";

test("hub validation accepts declared revision failures without rejecting the whole view", () => {
  const envelope = projectHubEnvelope(hubSource, HUB_SOURCE_REVISION, resolveAudience({
    authenticatedUserId: "player.fixture", authenticatedUserEmail: "", requestedPerspective: "player", dmPrincipalIds: [],
  }));
  envelope.applicationId = "dnd2024";
  envelope.stateSpaceId = "campaign.fixture";
  assert.equal(isReadyHubEnvelope(envelope), true);
  for (const status of ["error", "stale"]) {
    const state = {
      status, data: status === "stale" ? [] : null,
      ...(status === "stale" ? { source: "canonical" } : {}),
      failureCategory: "stale-data", diagnosticId: "revision-conflict",
      errorCode: "READ_MODEL_STATE_SPACE_STALE", httpStatus: 409,
    };
    envelope.party[0].sheetState = state;
    envelope.party[0].inventoryState = state;
    assert.equal(isReadyHubEnvelope(envelope), true, status);
    for (const invalidCategory of ["authorization", "made-up-category"]) {
      state.failureCategory = invalidCategory;
      assert.equal(isReadyHubEnvelope(envelope), false, `${status}: ${invalidCategory}`);
    }
  }
});
