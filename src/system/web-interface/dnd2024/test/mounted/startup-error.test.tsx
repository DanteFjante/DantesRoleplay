import assert from "node:assert/strict";
import test from "node:test";
import { JSDOM } from "jsdom";
import React, { act } from "react";
import { createRoot } from "react-dom/client";
import { ApplicationStartupError } from "../../src/components/ApplicationStartupError";

test("startup errors offer whole-application retry and never claim Rules are successfully loaded", async () => {
  const dom = new JSDOM("<div id='root'></div>", { url: "http://98.128.172.181/ui/dnd2024-play" });
  const previous = { window: globalThis.window, document: globalThis.document };
  Object.assign(globalThis, { React, window: dom.window, document: dom.window.document, IS_REACT_ACT_ENVIRONMENT: true });
  const root = createRoot(document.getElementById("root")!);
  try {
    let retries = 0;
    for (const kind of ["connection", "unavailable", "denied"] as const) {
      await act(async () => root.render(<ApplicationStartupError kind={kind} message="Fixture service response"
        onRetry={() => { retries++; }} />));
      assert.equal(document.querySelector('[role="alert"]')?.getAttribute("data-reason-code"), kind);
      assert.doesNotMatch(document.body.textContent!, /Private campaign views remain locked|Reference available|Actor binding required/);
      await act(async () => document.querySelector("button")!.click());
    }
    assert.equal(retries, 3);
  } finally {
    await act(async () => root.unmount());
    Object.assign(globalThis, previous);
    dom.window.close();
  }
});
