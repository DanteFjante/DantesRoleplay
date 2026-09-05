import assert from "node:assert/strict";
import test from "node:test";
import React, { act } from "react";
import { JSDOM } from "jsdom";
import { BoardDraftWorkshop } from "../../src/components/BoardDraftWorkshop";
import { prepareBoard, validateDraftProjection, type DraftProjection } from "../../src/server/board-draft";
import query from "../../../../../../catalog/applications/dnd2024/queries/combat/dnd2024.query.encounter-board-draft.json" with { type: "json" };

const scope = { applicationId: "dnd2024", stateSpaceId: "dnd2024-main", campaignId: "campaign.1", encounterId: "encounter.1" };
const projection = () => ({ ...scope, qualifiedQueryId: query.id, outputSchemaHash: query.projection.outputSchemaHash,
  stateSpaceFingerprint: "A".repeat(64), resolutionFingerprint: "B".repeat(64), resultFingerprint: "C".repeat(64), sourceRevisionFingerprint: "D".repeat(64),
  data: { version: 1, campaignId: scope.campaignId, encounterId: scope.encounterId, locationId: "location.1", expectedBoardRevision: null,
    board: { revision: 1, status: "active", visibility: "public", columns: 12, rows: 12, feetPerSquare: 5, terrain: [], obstacles: [] },
    backgroundRequest: { prompt: "PRIVATE_PROMPT_CANARY", width: 768, height: 768, mimeType: "image/png" },
    provider: "catalog-deterministic", model: "square-layout-v1", seed: 1 } });
function preparation(input: unknown, roleBindings: unknown) {
  return { ready: true, requiresConfirmation: true, proposalFingerprint: "E".repeat(64), receipt: { id: "receipt.1" },
    proposal: { command: "propose", steps: [{ kind: "action", qualifiedId: "dnd2024.mechanic.encounter.board.accept", dependsOn: [], input, roleBindings }] } };
}

test("draft responses bind exact query, campaign, fingerprints and image alignment", () => {
  assert.equal(validateDraftProjection(projection(), scope), true);
  for (const change of [
    (value: ReturnType<typeof projection>) => { value.data.campaignId = "campaign.foreign"; },
    (value: ReturnType<typeof projection>) => { value.sourceRevisionFingerprint = "invalid"; },
    (value: ReturnType<typeof projection>) => { value.data.backgroundRequest.width = 500; },
  ]) { const value = projection(); change(value); assert.equal(validateDraftProjection(value, scope), false); }
});

test("preparation refuses a substituted board or role and never executes", async () => {
  const previous = globalThis.fetch;
  try {
    for (const tamper of ["board", "role"]) {
      globalThis.fetch = async (url, init) => {
        assert.match(String(url), /\/prepare$/u);
        const body = JSON.parse(String(init?.body));
        if (tamper === "board") body.input.board.columns = 13;
        else body.roleEntityIds.encounter = "encounter.foreign";
        return Response.json(preparation(body.input, body.roleEntityIds));
      };
      await assert.rejects(prepareBoard(scope, projection() as DraftProjection, null, new AbortController().signal), /exact board/u);
    }
  } finally { globalThis.fetch = previous; }
});

test("mounted workshop stays inert until separate review, checkbox and Accept; discard never writes", async () => {
  const dom = new JSDOM('<div id="root"></div>', { url: "http://localhost/" });
  const previous = { window: globalThis.window, document: globalThis.document, fetch: globalThis.fetch };
  Object.assign(globalThis, { window: dom.window, document: dom.window.document, IS_REACT_ACT_ENVIRONMENT: true });
  const requests: string[] = []; let accepted = 0;
  globalThis.fetch = async (url, init) => {
    requests.push(`${init?.method ?? "GET"} ${String(url).split("?")[0]}`);
    if (String(url).includes("/read-models/")) return Response.json(projection());
    if (String(url).endsWith("/prepare")) {
      const body = JSON.parse(String(init?.body)); return Response.json(preparation(body.input, body.roleEntityIds));
    }
    assert.match(String(url), /\/execute$/u); return Response.json({ successful: true });
  };
  const { createRoot } = await import("react-dom/client");
  const container = dom.window.document.querySelector("#root")!; const root = createRoot(container);
  const button = (label: string) => {
    const found = [...container.querySelectorAll("button")].find(node => node.textContent === label);
    assert.ok(found, label); return found;
  };
  const click = async (label: string) => { await act(async () => button(label).click()); };
  try {
    await act(async () => root.render(<BoardDraftWorkshop scope={scope} onAccepted={() => accepted++} />));
    assert.equal(requests.length, 0);
    await click("Generate combat map");
    assert.equal(requests.length, 1); assert.match(requests[0], /^GET /u);
    await click("Discard draft"); assert.equal(requests.length, 1);
    assert.equal(container.textContent?.includes("PRIVATE_PROMPT_CANARY"), false);
    await click("Generate combat map"); await click("Review acceptance");
    assert.equal(button("Accept reviewed board").disabled, true);
    assert.equal(requests.some(request => request.endsWith("/execute")), false);
    await act(async () => (container.querySelector('input[type="checkbox"]') as HTMLInputElement).click());
    await click("Accept reviewed board");
    assert.equal(requests.filter(request => request.endsWith("/execute")).length, 1);
    assert.equal(accepted, 1); assert.equal(button("Accept reviewed board").disabled, true);
  } finally {
    await act(async () => root.unmount()); dom.window.close(); Object.assign(globalThis, previous);
    delete (globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT;
  }
});
