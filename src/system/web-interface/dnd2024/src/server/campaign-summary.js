import { contract as campaignSummaryContract } from "./campaign-summary-contract.js";
import { readModelResponse } from "./read-model-response.js";

const token = (value) => typeof value === "string" && value.length > 0 && value.length <= 200 &&
  value === value.trim() && !/\s/u.test(value) ? value : null;
const text = (value, maximum) => typeof value === "string" && value.length > 0 && value.length <= maximum
  ? value : null;
const textList = (value, maximumItems, maximumLength) => {
  if (!Array.isArray(value) || value.length > maximumItems) return [];
  const values = value.map((item) => text(item, maximumLength));
  return values.every(Boolean) ? values : [];
};
const hasExactKeys = (value, keys) => value && typeof value === "object" && !Array.isArray(value) &&
  Object.keys(value).length === keys.length && keys.every((key) => Object.hasOwn(value, key));

export function validateRegisteredCampaignSummary(data, projection, perspective) {
  if (!hasExactKeys(data, ["status", "title", "premise", "partyGoals", "toneAndBoundaries", "party",
    "totalCount", "complete", "nextCursor"]) || data.status !== "active") return null;
  const title = text(data.title, 160);
  const premise = text(data.premise, 1_000);
  const partyGoals = textList(data.partyGoals, 3, 500);
  const toneAndBoundaries = textList(data.toneAndBoundaries, 8, 300);
  if (!title || !premise || partyGoals.length === 0 || toneAndBoundaries.length === 0 ||
      !Array.isArray(data.party) || data.party.length > 20 || !Number.isInteger(data.totalCount) ||
      data.totalCount < data.party.length || data.totalCount > 20 || typeof data.complete !== "boolean" ||
      !(data.nextCursor === null || token(data.nextCursor)) || data.complete !== (data.nextCursor === null)) return null;
  const party = data.party.map((entry) => {
    const fields = ["id", "name", "status", ...(Object.hasOwn(entry ?? {}, "actors") ? ["actors"] : [])];
    if (!hasExactKeys(entry, fields) || !token(entry.id) || !text(entry.name, 400) ||
        !["active", "withdrawn"].includes(entry.status)) return null;
    if (Object.hasOwn(entry, "actors") && (!Array.isArray(entry.actors) || entry.actors.length > 1 ||
        perspective !== "dm" && entry.actors.length !== 0 || entry.actors.some((actor) =>
          !hasExactKeys(actor, ["id", "name"]) || !token(actor.id) || !text(actor.name, 400)))) return null;
    return { id: entry.id, name: entry.name, status: entry.status,
      ...(Object.hasOwn(entry, "actors") ? { actors: entry.actors.map((actor) => ({ id: actor.id, name: actor.name })) } : {}) };
  });
  if (party.some((entry) => entry === null) || new Set(party.map((entry) => entry.id)).size !== party.length)
    return null;
  return { status: data.status, title, premise, partyGoals, toneAndBoundaries, party,
    totalCount: data.totalCount, complete: data.complete, nextCursor: data.nextCursor, projection };
}

export function projectRegisteredPartyReferences(party) {
  const actors = new Map();
  for (const entry of party.filter((value) => value.status === "active")) {
    if (!Array.isArray(entry.actors) || entry.actors.length !== 1) return null;
    const actor = entry.actors[0];
    if (actors.has(actor.id) && actors.get(actor.id).name !== actor.name) return null;
    actors.set(actor.id, actor);
  }
  return [...actors.values()].map((actor) => ({
    ...actor, state: "active", current: false, entries: [], detailsDeferred: true,
  }));
}

/** Shared Campaign/Party bootstrap contract. */
export async function readRegisteredCampaignSummary({
  fetchImpl = fetch, origin, applicationId, stateSpaceId, campaignId, perspective,
}) {
  const entityRoot = `/api/applications/${encodeURIComponent(applicationId)}` +
    `/state-spaces/${encodeURIComponent(stateSpaceId)}/entities`;
  const parameters = new URLSearchParams({ perspective, campaignId, limit: "20" });
  const result = await readModelResponse({
    fetchImpl,
    resource: new URL(`${entityRoot}/${encodeURIComponent(campaignId)}` +
      `/read-models/${encodeURIComponent(campaignSummaryContract.id)}?${parameters}`, `${origin}/`).toString(),
    init: { headers: { Accept: "application/json" }, cache: "no-store" },
    applicationId,
    stateSpaceId,
    query: campaignSummaryContract,
    maximumBodyBytes: 70_000,
    maximumDataBytes: 65_536,
    statusPolicy: { ready: [200], unavailable: "remaining" },
    validate: (value) => validateRegisteredCampaignSummary(value, null, perspective) !== null,
  });
  return result.status === "ready"
    ? validateRegisteredCampaignSummary(result.data, result.evidence, perspective)
    : null;
}
