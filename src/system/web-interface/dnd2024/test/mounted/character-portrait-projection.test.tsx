import assert from "node:assert/strict";
import test from "node:test";
import { projectCharacterSheet, projectCharacterDetails } from "../../src/features/character/project-character";

test("both character resources project the authorized portrait and clear stale portraits", () => {
  const portrait = { imageUrl: "/api/portrait", alt: "Character portrait", width: 1024, height: 1536 };
  const summary = { id: "actor.fixture", portrait: { ...portrait, imageUrl: "/stale" } } as any;
  const data = { classes: [], inventory: { items: [] }, dossier: { inventory: { definitions: [] } } };
  for (const project of [projectCharacterSheet, projectCharacterDetails]) {
    const ready = { status: "ready", data, media: { portrait }, failureCategory: null, diagnosticId: "fixture" } as any;
    assert.deepEqual(project(summary, ready).portrait, portrait);
    assert.equal(project(summary, { ...ready, media: undefined }).portrait, summary.portrait,
      "non-empty malformed media stays unavailable and preserves the same-scope confirmed portrait");
    assert.equal(project(summary, { ...ready, media: null }).portrait, undefined);
    assert.equal(project(summary, { status: "forbidden", data: null, failureCategory: "authorization",
      diagnosticId: "fixture" }).portrait, undefined);
    assert.equal(project(summary, { status: "error", data: null, failureCategory: "transport",
      diagnosticId: "fixture" }).portrait, summary.portrait,
      "an unknown same-scope media failure preserves the last confirmed portrait");
    assert.equal(project(summary, { status: "error", data: null, media: null, failureCategory: "authorization",
      diagnosticId: "fixture" }).portrait, undefined,
      "an authorization failure clears the old portrait rather than showing it privately");
    assert.deepEqual(project(summary, { status: "error", data: null, media: { portrait }, failureCategory: "http",
      diagnosticId: "fixture" }).portrait, portrait,
      "successful media remains usable even when the sheet/dossier read fails");
  }
});
