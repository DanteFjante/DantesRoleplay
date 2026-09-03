import assert from "node:assert/strict";
import test from "node:test";

import { preserveLastGoodPartyData } from "../src/data/section-state.ts";

function member(state) {
  const data = [{ id: "sheet-1", kind: "class", title: "Bard", detail: "Canonical bard." }];
  return {
    id: "actor.one",
    initials: "AO",
    name: "Actor One",
    detail: "Bard",
    status: "Active",
    isCurrent: true,
    recordStatus: state.status === "ready" ? "Canonical character state" : "Canonical character unavailable",
    sheetStatus: state.status === "ready" ? "canonical" : "unavailable",
    inventoryStatus: state.status === "ready" ? "empty" : "unavailable",
    sheetState: state.status === "ready"
      ? { status: "ready", source: "canonical", data }
      : state,
    inventoryState: state.status === "ready"
      ? { status: "empty", source: "canonical", data: [] }
      : state,
    sheet: state.status === "ready" ? data : [],
    inventory: [],
    knowledge: [],
    backstory: [],
    origin: [],
    ...(state.status === "ready" ? { characterSheet: { version: 1, subject: { id: "actor.one", name: "Actor One" } } } : {}),
  };
}

function envelope(party, perspective = "player", campaignId = "campaign.one", options = {}) {
  return {
    status: "ready",
    version: 1,
    applicationId: options.applicationId ?? "dnd2024",
    stateSpaceId: options.stateSpaceId ?? "campaign.fixture",
    revision: options.revision ?? `live:dnd2024:campaign.fixture:${campaignId}`,
    audience: {
      seat: options.seat ?? "player",
      perspective,
      allowedPerspectives: options.allowedPerspectives ?? ["player"],
    },
    contextSelection: { selectedCampaignId: campaignId },
    party,
  };
}

test("refresh failure preserves canonical data as stale and a later success restores ready", () => {
  const ready = envelope([member({ status: "ready" })]);
  const failedState = {
    status: "error",
    data: null,
    failureCategory: "transport",
    diagnosticId: "request-500",
  };
  const failed = envelope([member(failedState)]);

  const stale = preserveLastGoodPartyData(ready, failed);
  assert.equal(stale.party[0].sheetState.status, "stale");
  assert.equal(stale.party[0].sheetState.diagnosticId, "request-500");
  assert.equal(stale.party[0].sheet[0].title, "Bard");
  assert.equal(stale.party[0].recordStatus, "Canonical character state is stale");

  const recovered = preserveLastGoodPartyData(stale, ready);
  assert.equal(recovered.party[0].sheetState.status, "ready");
  assert.equal(recovered.party[0].recordStatus, "Canonical character state");
});

test("first failure stays error because no last-good canonical data exists", () => {
  const failedState = {
    status: "error",
    data: null,
    failureCategory: "incompatible-data",
    diagnosticId: "malformed-1",
  };
  const failed = envelope([member(failedState)]);
  const result = preserveLastGoodPartyData(envelope([]), failed);
  assert.equal(result.party[0].sheetState.status, "error");
  assert.deepEqual(result.party[0].sheet, []);
});

test("authorization loss never preserves previously readable character data", () => {
  const ready = envelope([member({ status: "ready" })]);
  const forbiddenState = {
    status: "forbidden",
    data: null,
    failureCategory: "authorization",
    diagnosticId: "authorization-revoked",
  };
  const forbidden = envelope([member(forbiddenState)]);

  const result = preserveLastGoodPartyData(ready, forbidden);
  assert.equal(result.party[0].sheetState.status, "forbidden");
  assert.deepEqual(result.party[0].sheet, []);
  assert.equal(result.party[0].characterSheet, undefined);
});

test("non-transient catalog and incompatible-data failures never reuse last-good values", () => {
  const ready = envelope([member({ status: "ready" })]);
  for (const failedState of [
    {
      status: "error",
      data: null,
      failureCategory: "http",
      diagnosticId: "catalog-422",
      httpStatus: 422,
    },
    {
      status: "error",
      data: null,
      failureCategory: "incompatible-data",
      diagnosticId: "malformed-projection",
    },
  ]) {
    const result = preserveLastGoodPartyData(ready, envelope([member(failedState)]));
    assert.equal(result.party[0].sheetState.status, "error");
    assert.deepEqual(result.party[0].sheet, []);
    assert.equal(result.party[0].characterSheet, undefined);
  }
});

test("transient HTTP failures may reuse canonical data and retain the response status", () => {
  const ready = envelope([member({ status: "ready" })]);
  const failedState = {
    status: "error",
    data: null,
    failureCategory: "http",
    diagnosticId: "temporary-503",
    httpStatus: 503,
  };
  const result = preserveLastGoodPartyData(ready, envelope([member(failedState)]));
  assert.equal(result.party[0].sheetState.status, "stale");
  assert.equal(result.party[0].sheetState.httpStatus, 503);
  assert.equal(result.party[0].sheet[0].title, "Bard");
});

test("last-good data is isolated by campaign, perspective, audience, and source revision", () => {
  const ready = envelope([member({ status: "ready" })], "player", "campaign.one");
  const failure = member({
    status: "error",
    data: null,
    failureCategory: "transport",
    diagnosticId: "temporary-network-failure",
  });
  const changedBoundaries = [
    envelope([failure], "player", "campaign.two"),
    envelope([failure], "dm", "campaign.one", { seat: "dm", allowedPerspectives: ["dm", "player"] }),
    envelope([failure], "player", "campaign.one", { seat: "dm", allowedPerspectives: ["dm", "player"] }),
    envelope([failure], "player", "campaign.one", { revision: "live:dnd2024:campaign.fixture:revision-two" }),
    envelope([failure], "player", "campaign.one", { stateSpaceId: "campaign.other" }),
  ];

  for (const next of changedBoundaries) {
    const result = preserveLastGoodPartyData(ready, next);
    assert.equal(result.party[0].sheetState.status, "error");
    assert.deepEqual(result.party[0].sheet, []);
  }
});
