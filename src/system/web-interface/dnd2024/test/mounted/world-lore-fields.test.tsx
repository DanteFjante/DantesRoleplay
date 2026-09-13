import assert from "node:assert/strict";
import test from "node:test";
import React from "react";
import { renderToStaticMarkup } from "react-dom/server";

import { WorldLore } from "../../src/components/WorldLore";
import type { WorldReadModel } from "../../src/data/hub-types";

function render(lore: WorldReadModel["lore"], loading = false) {
  const world = { id: "world.fixture", name: "Fixture", lore, loreCoverage: "partial" } as WorldReadModel;
  return renderToStaticMarkup(<WorldLore world={world} loading={loading} onOpenLocation={() => {}}
    onOpenFaction={() => {}} onOpenHistory={() => {}} />);
}

test("partial Lore still renders its usable entry and a local coverage notice", () => {
  const markup = render([{
    id: "lore.fixture", title: "A useful tale", category: "World lore", status: "Known",
    summary: "The quay bell rings at noon.", body: "A readable fact.",
    linkedLocations: [], linkedPeople: [], linkedFactions: [], linkedHistory: [],
  }]);
  assert.match(markup, /A useful tale/);
  assert.match(markup, /Some lore fields or records are unavailable/);
  assert.doesNotMatch(markup, /No lore matches/);
});

test("a loading Lore prefix never claims its partial count is the full collection", () => {
  const markup = render([], true);
  assert.match(markup, /0 shown · 0 loaded so far/);
  assert.match(markup, /Loading more lore/);
  assert.match(markup, /aria-busy="true"/);
  assert.doesNotMatch(markup, /0 of 0 visible|Lore unavailable|Some lore fields or records are unavailable|No lore matches/);
});

test("partial Lore with no usable entries never presents a confirmed empty collection", () => {
  const markup = render([]);
  assert.match(markup, /Lore unavailable/);
  assert.match(markup, /does not establish an empty lore collection/);
  assert.doesNotMatch(markup, /No lore matches/);
});

test("shared Lore displays members' actual stances without inventing a party belief", () => {
  const markup = render([{
    id: "knowledge.fixture", title: "A disputed tale", category: "World lore", status: "mixed",
    summary: "The quay bell rings at noon.", body: "A readable fact.",
    linkedLocations: [], linkedPeople: [], linkedFactions: [], linkedHistory: [],
    admissions: [{ actorId: "one", actorName: "Ganji", stance: "known", source: "explicit" },
      { actorId: "two", actorName: "Orban", stance: "doubted", source: "baseline" }],
  }]);
  assert.match(markup, /Party knowledge sources/);
  assert.match(markup, /Ganji<\/strong>: known/);
  assert.match(markup, /Orban<\/strong>: doubted/);
  assert.doesNotMatch(markup, /The party believes/);
});
