import assert from "node:assert/strict";
import test from "node:test";

import {
  filterCampaignClues,
  filterCampaignQuests,
  filterCampaignThreads,
} from "../src/data/campaign-filters.ts";

const links = { locations: [], people: [], factions: [] };

test("Campaign pursuit filters are deterministic and search only projected fields", () => {
  const quests = [
    { id: "reliquary", sortOrder: 20, kind: "Main quest", status: "Active", title: "Open the reliquary", summary: "A ward waits.", nextStep: "Speak the oath.", objectives: [{ id: "oath", status: "Active", text: "Speak the second oath." }], links },
    { id: "warden", sortOrder: 10, kind: "Faction quest", status: "Open", title: "Choose a route", summary: "Wardens are watching.", nextStep: "Ask Hale.", objectives: [], links },
  ];
  const threads = [
    { id: "oath", sortOrder: 20, category: "Threat", status: "Unresolved", pressure: "Dawn approaches", title: "The second oath", summary: "The ward wants a promise.", lastChanged: "Session 2", links },
    { id: "name", sortOrder: 10, category: "Mystery", status: "Open", pressure: "Quiet", title: "An older name", summary: "The beacon spoke.", lastChanged: "Session 1", links },
  ];
  const clues = [
    { id: "intent", sortOrder: 20, mystery: "Reliquary", status: "Established", title: "The ward answered intent", detail: "A promise woke it.", partyConclusion: "Promises matter.", discoveredAt: "Session 2", links },
    { id: "name", sortOrder: 10, mystery: "Oath-keepers", status: "Lead", title: "The beacon named Seraphine", detail: "An older name sounded.", partyConclusion: "A family connection exists.", discoveredAt: "Session 1", links },
  ];

  assert.deepEqual(filterCampaignQuests(quests).map(({ id }) => id), ["reliquary", "warden"]);
  assert.deepEqual(filterCampaignQuests(quests, { kind: "Faction quest" }).map(({ id }) => id), ["warden"]);
  assert.deepEqual(filterCampaignQuests(quests, { query: "second oath" }).map(({ id }) => id), ["reliquary"]);
  assert.deepEqual(filterCampaignThreads(threads, { category: "Mystery" }).map(({ id }) => id), ["name"]);
  assert.deepEqual(filterCampaignThreads(threads, { query: "dawn" }).map(({ id }) => id), ["oath"]);
  assert.deepEqual(filterCampaignClues(clues, { mystery: "Reliquary" }).map(({ id }) => id), ["intent"]);
  assert.deepEqual(filterCampaignClues(clues, { query: "family" }).map(({ id }) => id), ["name"]);
});
