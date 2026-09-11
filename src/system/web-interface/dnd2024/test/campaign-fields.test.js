import assert from "node:assert/strict";
import test from "node:test";

import { contract as campaignDetailsContract } from "../src/server/campaign-details-contract.js";
import { contract as campaignContextContract } from "../src/server/campaign-context-contract.js";
import { contract as campaignLocationVisitsContract } from "../src/server/campaign-location-visits-contract.js";
import { contract as campaignSummaryContract } from "../src/server/campaign-summary-contract.js";
import { readRegisteredCampaignSummary } from "../src/server/campaign-summary.js";
import {
  readDeferredCampaignDetails,
  readRegisteredCampaignContext,
  readRegisteredCampaignDetails,
  readRegisteredCampaignVisitPage,
} from "../src/server/game-server-context.js";

const applicationId = "dnd2024";
const stateSpaceId = "dnd2024-main";
const campaignId = "campaign.fixture";
const evidence = {
  applicationId,
  stateSpaceId,
  stateSpaceFingerprint: "1".repeat(64),
  resolutionFingerprint: "2".repeat(64),
  resultFingerprint: "3".repeat(64),
};

test("Campaign context retains exact authority identities independently of missing display names", async () => {
  const read = (worldId) => readRegisteredCampaignContext({
    origin: "http://localhost:6219", applicationId, stateSpaceId, campaignId, perspective: "dm",
    fetchImpl: async (input) => {
      const request = new URL(input);
      assert.equal(request.searchParams.get("campaignId"), null);
      return response(200, envelope(campaignContextContract, {
      version: 900, campaignId, worldId: "world.fixture", extra: true,
      campaign: { id: campaignId, name: 2 }, world: { id: worldId, other: "ignored" },
      }));
    },
  });
  const result = await read("world.fixture");
  assert.equal(result.campaign.name, "Name unavailable");
  assert.equal(result.world.name, "Name unavailable");
  assert.equal(result.campaignId, campaignId);
  assert.equal(await read("world.foreign"), null, "unrelated IDs never pass the authority boundary");
});

function response(status, value) {
  return new Response(JSON.stringify(value), { status, headers: { "Content-Type": "application/json" } });
}

function envelope(contract, data, sourceRevisionFingerprint = "A".repeat(64)) {
  return { ...evidence, qualifiedQueryId: contract.id, outputSchemaHash: contract.outputSchemaHash,
    sourceRevisionFingerprint, data };
}

function detailRead(data, perspective = "dm") {
  return readRegisteredCampaignDetails({
    origin: "http://localhost:6219", applicationId, stateSpaceId, campaignId, perspective,
    fetchImpl: async () => response(200, envelope(campaignDetailsContract, data)),
  });
}

test("Campaign summary uses only the generic route entity and paging parameters", async () => {
  for (const perspective of ["dm", "player"]) {
    let requests = 0;
    const result = await readRegisteredCampaignSummary({
      origin: "http://localhost:6219", applicationId, stateSpaceId, campaignId, perspective,
      fetchImpl: async (input) => {
        requests++;
        const request = new URL(input);
        assert.equal(request.pathname, `/api/applications/${applicationId}/state-spaces/${stateSpaceId}/entities/${campaignId}/read-models/${campaignSummaryContract.id}`);
        assert.deepEqual([...request.searchParams], [["perspective", perspective], ["limit", "20"]]);
        return response(200, envelope(campaignSummaryContract, { status: "active", title: "Fixture", premise: "Still readable",
          party: [], partyGoals: [], toneAndBoundaries: [], complete: true, totalCount: 0, nextCursor: null }));
      },
    });
    assert.equal(requests, 1); assert.equal(result.premise, "Still readable");
  }
});

function visit(id, locationId, extra = {}) {
  return {
    id, name: `${id} name`, firstVisitedMinute: 5, lastVisitedMinute: 6, visitCount: 1,
    status: "departed", summary: `${id} summary`, memory: `${id} memory`,
    locations: [{ id: locationId, name: `${locationId} name` }], ...extra,
  };
}

test("Campaign details keep valid field records when versions, extra fields, and neighboring rows evolve", async () => {
  const details = await detailRead({
    version: 97, campaignId, futureRootField: { revision: 12 },
    chapters: [
      { id: "chapter.valid", status: "active", title: "Valid chapter", partyQuestion: "Where next?", future: true },
      { id: "chapter.partial", status: "active", title: "Still usable", partyQuestion: 9 },
      { id: "chapter.duplicate", status: "active", title: "Duplicate one", partyQuestion: "Ignored" },
      { id: "chapter.duplicate", status: "active", title: "Duplicate two", partyQuestion: "Ignored" },
    ],
    arcs: [{ id: "arc.closed", status: "resolved", title: "Closed arc", partyStake: "Protect the bridge" }],
    sessions: [{ id: "session.ended", status: "ended", ordinal: 3,
      recap: { protocolVersion: "future-session-format", extra: true } }],
  });

  assert.deepEqual(details.chapters, [
    { id: "chapter.valid", status: "active", title: "Valid chapter", partyQuestion: "Where next?" },
    { id: "chapter.partial", status: "active", title: "Still usable", unavailableFields: ["partyQuestion"] },
  ]);
  assert.deepEqual(details.arcs, [{ id: "arc.closed", status: "resolved", title: "Closed arc", partyStake: "Protect the bridge" }]);
  assert.deepEqual(details.sessions, [{ id: "session.ended", status: "ended", ordinal: 3, unavailableFields: ["recap"] }]);
  assert.deepEqual(details.detailFields, { chapters: "partial", arcs: "ready", sessions: "partial" });
});

test("Campaign detail coverage distinguishes authoritative empty fields from absent or invalid fields", async () => {
  const details = await detailRead({ version: 42, campaignId, chapters: [], arcs: "not-a-list" });
  assert.deepEqual(details.chapters, []);
  assert.deepEqual(details.arcs, []);
  assert.deepEqual(details.sessions, []);
  assert.deepEqual(details.detailFields, { chapters: "empty", arcs: "invalid", sessions: "absent" });
});

test("Campaign detail identity and Player audience boundaries remain fail-closed", async () => {
  assert.equal(await detailRead({ version: 2, campaignId: "campaign.other", chapters: [], arcs: [], sessions: [] }), null);
  assert.equal(await detailRead({ version: 2, campaignId, chapters: [], arcs: [],
    sessions: [{ id: "session.private", status: "active", ordinal: 1 }] }, "player"), null);
  assert.equal(await detailRead({ version: 2, campaignId,
    chapters: [{ id: "chapter.leak", status: "active", title: "Visible", partyQuestion: "Now?", gmContext: "Private" }],
    arcs: [], sessions: [] }, "player"), null);
});

test("Campaign visit pages localize malformed rows without weakening pagination or source evidence", async () => {
  const page = await readRegisteredCampaignVisitPage({
    origin: "http://localhost:6219", applicationId, stateSpaceId, campaignId,
    fetchImpl: async (input) => {
      const request = new URL(input);
      assert.equal(request.searchParams.get("campaignId"), null);
      return response(200, envelope(campaignLocationVisitsContract, {
      version: 14, campaignTitle: 77, futureRootField: true,
      visits: [visit("visit.valid", "location.valid", { future: true }), visit("visit.bad", "location.bad", { locations: [] })],
      totalCount: 2, complete: true, nextCursor: null,
      }));
    },
  });
  assert.deepEqual(page.visits, [{ id: "visit.valid", firstVisitedMinute: 5, lastVisitedMinute: 6,
    visitCount: 1, status: "departed", summary: "visit.valid summary", memory: "visit.valid memory", locationId: "location.valid" }]);
  assert.equal(page.coverage, "partial");
  assert.equal(page.sourceRevisionFingerprint, "A".repeat(64));
});

test("Deferred Campaign visits retain unambiguous rows and exclude every paged duplicate identity", async () => {
  const source = { applicationId, stateSpaceId, campaign: { id: campaignId }, audience: { perspective: "dm" } };
  let visitPage = 0;
  const result = await readDeferredCampaignDetails({
    origin: "http://localhost:6219", source,
    fetchImpl: async (input) => {
      const request = new URL(input);
      if (request.pathname.endsWith(campaignDetailsContract.id)) return response(200, envelope(campaignDetailsContract, {
        version: 3, campaignId, chapters: [], arcs: [], sessions: [],
      }));
      assert.match(request.pathname, new RegExp(`${campaignLocationVisitsContract.id}$`, "u"));
      const index = visitPage++;
      assert.equal(index === 0 ? request.searchParams.get("cursor") : request.searchParams.get("cursor"), index === 0 ? null : "next");
      return response(200, envelope(campaignLocationVisitsContract, index === 0 ? {
        visits: [visit("visit.keep", "location.keep"), visit("visit.one", "location.one"), visit("visit.bad", "location.bad", { summary: null })],
        totalCount: 4, complete: false, nextCursor: "next",
      } : {
        visits: [visit("visit.one", "location.other")], totalCount: 4, complete: true, nextCursor: null,
      }));
    },
  });
  assert.deepEqual(result.visits.map((item) => item.id), ["visit.keep", "visit.bad"]);
  assert.deepEqual(result.visits[1], {
    id: "visit.bad", firstVisitedMinute: 5, lastVisitedMinute: 6, visitCount: 1,
    status: "departed", memory: "visit.bad memory", locationId: "location.bad", unavailableFields: ["summary"],
  });
  assert.deepEqual(result.detailFields, { chapters: "empty", arcs: "empty", sessions: "empty", visits: "partial" });
  assert.equal(visitPage, 2);
});

test("Deferred Campaign visits reject a changed source revision instead of mixing pages", async () => {
  const source = { applicationId, stateSpaceId, campaign: { id: campaignId }, audience: { perspective: "dm" } };
  let visitPage = 0;
  await assert.rejects(() => readDeferredCampaignDetails({
    origin: "http://localhost:6219", source,
    fetchImpl: async (input) => {
      const request = new URL(input);
      if (request.pathname.endsWith(campaignDetailsContract.id)) return response(200, envelope(campaignDetailsContract, {
        campaignId, chapters: [], arcs: [], sessions: [],
      }));
      const index = visitPage++;
      return response(200, envelope(campaignLocationVisitsContract, {
        visits: [visit(`visit.${index}`, `location.${index}`)], totalCount: 2,
        complete: index === 1, nextCursor: index === 0 ? "next" : null,
      }, index === 0 ? "A".repeat(64) : "B".repeat(64)));
    },
  }), /campaign details are incomplete/u);
});
