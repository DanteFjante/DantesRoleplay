import { contract as campaignSummaryContract } from "./campaign-summary-contract.js";
import { readModelResponse } from "./read-model-response.js";

const token = (value) => typeof value === "string" && value.length > 0 && value.length <= 200 &&
  value === value.trim() && !/\s/u.test(value) ? value : null;
const text = (value, maximum) => typeof value === "string" && value.length > 0 && value.length <= maximum
  ? value : null;
const own = (value, key) => Boolean(value && typeof value === "object" && !Array.isArray(value) &&
  Object.hasOwn(value, key));
const hasExactKeys = (value, keys) => value && typeof value === "object" && !Array.isArray(value) &&
  Object.keys(value).length === keys.length && keys.every((key) => Object.hasOwn(value, key));

function descriptiveText(data, key, maximum) {
  if (!own(data, key)) return { status: "absent", value: null };
  if (data[key] === null) return { status: "empty", value: null };
  const value = text(data[key], maximum);
  return value ? { status: "ready", value } : { status: "invalid", value: null };
}

function descriptiveList(data, key, maximumItems, maximumLength) {
  if (!own(data, key)) return { status: "absent", value: [] };
  if (data[key] === null) return { status: "empty", value: [] };
  if (!Array.isArray(data[key])) return { status: "invalid", value: [] };
  const source = data[key];
  const inspected = source.slice(0, maximumItems);
  const value = inspected.map((item) => text(item, maximumLength)).filter(Boolean);
  const partial = source.length > maximumItems || value.length !== inspected.length;
  if (value.length === 0) return partial ? { status: "invalid", value } : { status: "empty", value };
  return { status: partial ? "partial" : "ready", value };
}

function registeredParty(data, perspective) {
  if (!own(data, "party") || !Array.isArray(data.party) || data.party.length > 20) return null;
  const party = data.party.map((entry) => {
    if (!entry || typeof entry !== "object" || Array.isArray(entry) ||
        !own(entry, "id") || !own(entry, "status") || !token(entry.id) ||
        !["active", "withdrawn"].includes(entry.status)) return null;
    if (own(entry, "actors") && (!Array.isArray(entry.actors) || entry.actors.length > 1 ||
        perspective !== "dm" && entry.actors.length !== 0 || entry.actors.some((actor) =>
          !actor || typeof actor !== "object" || Array.isArray(actor) || !own(actor, "id") || !token(actor.id)))) return null;
    // IDs and status bind participation. A missing or malformed descriptive name is never a
    // reason to erase an otherwise authorized identity from the bootstrap.
    return { id: entry.id, name: text(entry.name, 400) ?? entry.id, status: entry.status,
      ...(own(entry, "actors") ? { actors: entry.actors.map((actor) => ({
        id: actor.id, name: text(actor.name, 400) ?? actor.id,
      })) } : {}) };
  });
  return party.some((entry) => entry === null) || new Set(party.map((entry) => entry.id)).size !== party.length
    ? null
    : party;
}

export function validateRegisteredCampaignSummary(data, projection, perspective) {
  if (!data || typeof data !== "object" || Array.isArray(data) || !own(data, "status") ||
      data.status !== "active" || !own(data, "totalCount") || !Number.isInteger(data.totalCount) ||
      data.totalCount < 0 || data.totalCount > 20 || !own(data, "complete") ||
      typeof data.complete !== "boolean" || !own(data, "nextCursor") ||
      !(data.nextCursor === null || token(data.nextCursor))) return null;
  const party = registeredParty(data, perspective);
  if (!party || data.totalCount < party.length || data.complete !== (data.nextCursor === null)) return null;
  const title = descriptiveText(data, "title", 160);
  const premise = descriptiveText(data, "premise", 1_000);
  const partyGoals = descriptiveList(data, "partyGoals", 3, 500);
  const toneAndBoundaries = descriptiveList(data, "toneAndBoundaries", 8, 300);
  return {
    status: data.status,
    title: title.value,
    premise: premise.value,
    partyGoals: partyGoals.value,
    toneAndBoundaries: toneAndBoundaries.value,
    descriptiveFields: {
      title: title.status,
      premise: premise.status,
      partyGoals: partyGoals.status,
      toneAndBoundaries: toneAndBoundaries.status,
    },
    party,
    totalCount: data.totalCount,
    complete: data.complete,
    nextCursor: data.nextCursor,
    projection,
  };
}

/** Strict, mapped acknowledgement parser for the Campaign premise write only. */
export function validateCampaignPremiseWriteAcknowledgement(data, projection) {
  if (!data || typeof data !== "object" || Array.isArray(data) || !own(data, "status") ||
      data.status !== "active" || !own(data, "premise")) return null;
  const premise = text(data.premise, 1_000);
  return premise ? { premise, projection } : null;
}

export function projectRegisteredPartyReferences(party) {
  const actors = new Map();
  for (const entry of party.filter((value) => value.status === "active")) {
    if (!Array.isArray(entry.actors) || entry.actors.length !== 1) return null;
    const actor = entry.actors[0];
    // A display label must not turn an already-bound actor identity into an incompatible roster.
    if (!actors.has(actor.id)) actors.set(actor.id, actor);
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
  const parameters = new URLSearchParams({ perspective, limit: "20" });
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
