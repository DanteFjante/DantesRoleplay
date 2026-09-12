import { createHubReadScope } from "./hub-read-scope.js";
import { readCompletePages } from "./complete-pagination.js";
import { readBoundedJson, readModelResponse } from "./read-model-response.js";
import { readRegisteredPartyKnowledge } from "./party-knowledge.js";
import { projectRegisteredPartyReferences, readRegisteredCampaignSummary } from "./campaign-summary.js";
import { contract as campaignContextContract } from "./campaign-context-contract.js";
import { contract as campaignDetailsContract } from "./campaign-details-contract.js";
import { contract as campaignLocationVisitsContract } from "./campaign-location-visits-contract.js";
import { contract as worldCampaignDirectoryContract } from "./world-campaign-directory-contract.js";
import { contract as characterSheetContract } from "./character-sheet-contract.js";
import { contract as characterDossierContract } from "./character-dossier-contract.js";
import { contract as inventoryContainerContract } from "./inventory-container-contract.js";
import { contract as inventoryWalletContract } from "./inventory-wallet-contract.js";
import { contract as factionDirectoryContract } from "./faction-directory-contract.js";
import { contract as worldLocationScopeContract } from "./world-location-scope-contract.js";
import { contract as worldLocationScopePageContract } from "./world-location-scope-page-contract.js";
import { contract as worldPeopleHoldingsContract } from "./world-people-holdings-contract.js";
import { contract as worldPeopleHoldingsPageContract } from "./world-people-holdings-page-contract.js";
import { contract as currentSceneContract } from "./current-scene-contract.js";
import { contract as campaignResumeContract } from "./campaign-resume-contract.js";
import { CurrentViewAuthorizationError } from "../data/current-view-error.js";

export { readRegisteredCampaignSummary } from "./campaign-summary.js";

const TOKEN_MAXIMUM = 200;
const LOCATION_COMPONENT_TYPE_ID = "game.core.world.location";
const WORLD_ROUTE_COMPONENT_TYPE_ID = "game.core.world.route";
const WORLD_ROUTE_AVAILABILITY_COMPONENT_TYPE_ID = "game.core.world.route.availability";
const WORLD_ROUTE_RELATIONSHIP_KINDS = {
  world: "game.core.world.route.in-world",
  origin: "game.core.world.route.from",
  destination: "game.core.world.route.to",
};
const WORLD_INTERACTION_COMPONENT_TYPE_ID = "game.core.world.interaction";
const WORLD_INTERACTION_PARTICIPANT_RELATIONSHIP_KIND =
  "game.core.world.interaction.participant";
const RECORDED_SITUATION_KINDS = new Set([
  "out-of-character", "conversation", "combat", "exploration", "investigation",
  "travel", "rest", "downtime", "other",
]);

function token(value) {
  return typeof value === "string" && value.length > 0 && value.length <= TOKEN_MAXIMUM &&
    value === value.trim() && !/\s/u.test(value) ? value : null;
}

export function normalizeGameServerOrigin(value) {
  if (typeof value !== "string" || value.length === 0 || value.length > 500) return null;
  try {
    const url = new URL(value);
    if (url.protocol !== "http:" && url.protocol !== "https:") return null;
    if (url.username || url.password || url.search || url.hash || url.pathname !== "/") return null;
    return url.origin;
  } catch {
    return null;
  }
}

function url(origin, path) {
  return new URL(path, `${origin}/`).toString();
}

async function json(response) {
  if (!response) return null;
  try { return await response.json(); } catch { return null; }
}

async function readJsonPages({
  fetchImpl,
  origin,
  path,
  pageSize = 100,
  maximumPages = 100,
  maximumItems = 10_000,
}) {
  return readCompletePages({
    pageSize,
    maximumPages,
    maximumItems,
    fetchPage: async (cursor) => {
      const pageUrl = new URL(path, `${origin}/`);
      pageUrl.searchParams.set("limit", String(pageSize));
      if (cursor !== null) pageUrl.searchParams.set("cursor", cursor);
      const response = await fetchImpl(pageUrl.toString(), {
        headers: { Accept: "application/json" },
        cache: "no-store",
      });
      if (!response?.ok) throw new Error("Collection page unavailable.");
      return json(response);
    },
  });
}

function unavailableOnFirstPage(result) {
  return result?.status === "incomplete" && result.reason === "page-unavailable" && result.pagesRead === 0;
}

function denied(message) {
  return { version: 1, status: "denied", message };
}

function unavailable(message, reason) {
  return { version: 1, status: "unavailable", message, ...(reason ? { reason } : {}) };
}

function audience(value) {
  if (value?.status === "character-creation-required") {
    const applicationId = token(value.applicationId);
    const stateSpaceId = token(value.stateSpaceId);
    const campaignId = token(value.campaignId);
    const characterId = token(value.characterCreation?.characterId);
    return applicationId && stateSpaceId && campaignId && characterId
      ? { status: "character-creation-required", applicationId, stateSpaceId, campaignId, actorId: characterId }
      : null;
  }

  const applicationId = token(value?.applicationId);
  const stateSpaceId = token(value?.stateSpaceId);
  const campaignId = token(value?.campaignId);
  const actorId = token(value?.actorId);
  const role = value?.role === "game-master"
    ? "game-master"
    : value?.role === "player-group"
      ? "player-group"
      : (value?.role === "actor" || actorId ? "actor" : null);
  if (value?.status !== "bound" || !applicationId || !stateSpaceId || !campaignId || !role) return null;
  if (role === "game-master" || role === "player-group") {
    return actorId ? null : { status: "bound", applicationId, stateSpaceId, campaignId, role };
  }
  return actorId ? { status: "bound", applicationId, stateSpaceId, campaignId, actorId, role } : null;
}

const VALID_PERSPECTIVES = new Set(["player", "dm"]);

function normalizePerspective(value) {
  return VALID_PERSPECTIVES.has(value) ? value : "player";
}

function entity(value, expectedId) {
  const entityId = token(value?.entityId);
  const name = text(value?.name, 200);
  return entityId === expectedId && name ? { id: entityId, name } : null;
}

function componentValue(value, expectedEntityId, expectedTypeId) {
  if (token(value?.entityId) !== expectedEntityId || token(value?.qualifiedTypeId) !== expectedTypeId) {
    return null;
  }
  if (typeof value?.valueJson !== "string" || value.valueJson.length > 100_000) return null;
  try {
    const parsed = JSON.parse(value.valueJson);
    return parsed && typeof parsed === "object" && !Array.isArray(parsed) ? parsed : null;
  } catch {
    return null;
  }
}

function text(value, maximum = 2_000) {
  return typeof value === "string" && value.length > 0 && value.length <= maximum
    ? value
    : null;
}

function hasExactKeys(value, keys) {
  return value && typeof value === "object" && !Array.isArray(value) &&
    Object.keys(value).length === keys.length && keys.every((key) => Object.hasOwn(value, key));
}

/**
 * Projects only the audience-bound active play situation written by the play recorder. This is
 * deliberately a distinct read-model branch: it can describe continuity between clients without
 * pretending that model narration created an authoritative encounter, initiative, or ECS scene.
 */
export function resolveRecordedPlaySituation(value, authorizedLocationIds) {
  const situation = value?.currentSituation;
  const id = token(ownValue(situation, "id"));
  const situationKind = ownValue(situation, "kind");
  const kind = RECORDED_SITUATION_KINDS.has(situationKind) ? situationKind : null;
  const summary = text(ownValue(situation, "summary"), 4_000);
  const participantsValue = ownValue(situation, "participants");
  if (!id || ownValue(situation, "status") !== "active" || !kind) return null;
  const unavailableFields = [];
  if (!summary) unavailableFields.push("summary");
  const participantCounts = new Map();
  if (participantsValue !== undefined && participantsValue !== null && !Array.isArray(participantsValue)) {
    unavailableFields.push("participants");
  }
  if (Array.isArray(participantsValue) && participantsValue.length > 32) return null;
  const participantRows = Array.isArray(participantsValue) ? participantsValue : [];
  if (participantsValue === undefined || participantsValue === null) unavailableFields.push("participants");
  for (const participant of participantRows) {
    const entityId = participant && typeof participant === "object" && !Array.isArray(participant)
      ? ownValue(participant, "entityId") : undefined;
    const key = entityId === null || entityId === undefined ? null : token(entityId);
    if (key) participantCounts.set(key, (participantCounts.get(key) ?? 0) + 1);
  }
  const participants = [];
  for (let index = 0; index < participantRows.length; index++) {
    const participant = participantRows[index];
    const name = text(ownValue(participant, "name"), 200);
    const rawEntityId = ownValue(participant, "entityId");
    const entityId = rawEntityId === null || rawEntityId === undefined ? null : token(rawEntityId);
    if (!name || (rawEntityId !== null && rawEntityId !== undefined && !entityId) ||
        (entityId && participantCounts.get(entityId) !== 1)) {
      unavailableFields.push("participants");
      continue;
    }
    participants.push({ id: entityId ?? `${id}.participant.${index + 1}`, name, ...(entityId ? { entityId } : {}) });
  }
  let location;
  let locationId;
  const locationValue = ownValue(situation, "location");
  if (locationValue !== null && locationValue !== undefined) {
    const name = text(ownValue(locationValue, "name"), 200);
    const rawEntityId = ownValue(locationValue, "entityId");
    const entityId = rawEntityId === null || rawEntityId === undefined ? null : token(rawEntityId);
    if (!name || (rawEntityId !== null && rawEntityId !== undefined && !entityId)) {
      unavailableFields.push("location");
    } else {
      locationId = entityId && authorizedLocationIds.includes(entityId) ? entityId : undefined;
      location = { name, ...(locationId ? { id: locationId } : {}) };
    }
  }
  const recentMessages = ownValue(value, "recentMessages");
  if (recentMessages !== undefined && recentMessages !== null && !Array.isArray(recentMessages)) {
    unavailableFields.push("interactions");
  }
  if (Array.isArray(recentMessages) && recentMessages.length > 64) return null;
  const messageRows = Array.isArray(recentMessages) ? recentMessages : [];
  if (recentMessages === undefined || recentMessages === null) unavailableFields.push("interactions");
  const messageCounts = new Map();
  for (const message of messageRows) {
    const messageId = token(ownValue(message, "id"));
    if (messageId) messageCounts.set(messageId, (messageCounts.get(messageId) ?? 0) + 1);
  }
  const interactions = [];
  for (const message of messageRows.slice(-12)) {
    const messageId = token(ownValue(message, "id"));
    const roleValue = ownValue(message, "role");
    const role = roleValue === "player" || roleValue === "assistant" ? roleValue : null;
    const messageText = typeof ownValue(message, "text") === "string" && ownValue(message, "text").length > 0 && ownValue(message, "text").length <= 8_000 &&
      ![...ownValue(message, "text")].some(character => /[\p{Cc}]/u.test(character) && character !== "\r" && character !== "\n" && character !== "\t")
      ? ownValue(message, "text")
      : null;
    const ordinal = ownValue(message, "ordinal");
    if (!messageId || (messageCounts.get(messageId) ?? 0) !== 1 || !role || !messageText ||
        !Number.isInteger(ordinal) || ordinal < 1) {
      unavailableFields.push("interactions");
      continue;
    }
    interactions.push({ id: messageId, ordinal, role, text: messageText });
  }
  const coverage = unavailableFields.length ? "partial" : "complete";
  return {
    status: "ready",
    kind: "recorded",
    ...(coverage === "partial" ? { coverage, unavailableFields: [...new Set(unavailableFields)] } : {}),
    ...(locationId ? { locationId } : {}),
    recorded: {
      id, kind, participants, interactions,
      ...(summary ? { summary } : {}),
      ...(location ? { location } : {}),
    },
  };
}

export function projectMediaVisual(value) {
  const result = projectMediaAttachments(value);
  // Callers that replace an entire media record need confirmed absence semantics. A partial
  // discovery must not remove a previously confirmed role. Scoped callers can use valid peers.
  return result.complete ? result.visual : null;
}

function entityMediaContentUrl(value) {
  if (typeof value !== "string") return null;
  try {
    const parsed = new URL(value, "http://media.invalid");
    if (parsed.origin !== "http://media.invalid" || parsed.hash) return null;
    if (/^\/api\/read-model-media\/[a-f0-9]{64}\/content$/u.test(parsed.pathname))
      return parsed.search === "" ? value : null;
    if (!/^\/api\/applications\/[^/?#\\\s]+\/state-spaces\/[^/?#\\\s]+\/(?:entities\/[^/?#\\\s]+\/)?media\/[^/?#\\\s]+\/content$/u.test(parsed.pathname) ||
        !(parsed.search === "" || /^\?perspective=(?:player|dm)$/u.test(parsed.search))) return null;
    return value;
  } catch { return null; }
}

function projectMediaAttachments(value) {
  if (!value || typeof value !== "object" || Array.isArray(value) || !Array.isArray(value.attachments) ||
      value.attachments.length > 64) return { visual: null, complete: false };
  const roles = new Set(["portrait", "setting", "map", "illustration", "icon", "scene", "handout"]);
  const projected = {};
  const gallery = [];
  const counts = new Map();
  for (const attachment of value.attachments) {
    const id = token(ownValue(attachment, "mediaId"));
    if (id) counts.set(id, (counts.get(id) ?? 0) + 1);
  }
  let complete = true;
  const order = (attachment) => boundedInteger(ownValue(attachment, "order"), 0, 10_000)
    ? attachment.order : Number.MAX_SAFE_INTEGER;
  const ordered = [...value.attachments].sort((left, right) =>
    order(left) - order(right) ||
    String(left?.mediaId ?? "").localeCompare(String(right?.mediaId ?? "")));
  for (const attachment of ordered) {
    if (!token(ownValue(attachment, "mediaId")) || counts.get(attachment.mediaId) !== 1 || !roles.has(attachment.role) ||
        !["image/png", "image/jpeg", "image/webp"].includes(attachment.mediaType) ||
        !Number.isInteger(attachment.width) || attachment.width < 1 || attachment.width > 10_000 ||
        !Number.isInteger(attachment.height) || attachment.height < 1 || attachment.height > 10_000 ||
        !entityMediaContentUrl(attachment.contentUrl)) {
      complete = false;
      continue;
    }
    const visual = {
      imageUrl: attachment.contentUrl,
      alt: text(attachment.alt, 500) ?? "Image",
      width: attachment.width,
      height: attachment.height,
    };
    gallery.push({
      ...visual,
      mediaId: attachment.mediaId,
      role: attachment.role,
      caption: text(attachment.caption, 1_000) ?? "",
    });
    if (!projected[attachment.role]) projected[attachment.role] = visual;
  }
  if (gallery.length > 1) projected.gallery = gallery;
  return { visual: Object.keys(projected).length > 0 ? projected : null, complete };
}

function projectReadModelOwnerMedia(value, ownerId) {
  return hasExactKeys(value, ["entityId", "attachments"]) && value.entityId === ownerId
    ? projectMediaAttachments(value)
    : null;
}

function projectLocationMediaBatch(value, applicationId, stateSpaceId, recordIds) {
  const allowed = new Set(recordIds);
  if (value?.applicationId !== applicationId || value.stateSpaceId !== stateSpaceId ||
      !Array.isArray(value.items) || value.items.length > allowed.size ||
      new Set(value.items.map((item) => item?.entityId)).size !== value.items.length ||
      !value.items.every((item) => allowed.has(item?.entityId))) return null;
  const projected = new Map();
  for (const item of value.items) {
    projected.set(item.entityId, projectMediaAttachments(item));
  }
  return projected;
}

export function inheritMediaVisual(instanceMedia, definitionMedia) {
  if (!instanceMedia && !definitionMedia) return null;
  const roles = ["portrait", "setting", "map", "illustration", "icon", "scene", "handout"];
  const result = {};
  for (const role of roles) {
    const visual = instanceMedia?.[role] ?? definitionMedia?.[role];
    if (visual) result[role] = visual;
  }
  const instanceGallery = Array.isArray(instanceMedia?.gallery) ? instanceMedia.gallery : [];
  const definitionGallery = Array.isArray(definitionMedia?.gallery) ? definitionMedia.gallery : [];
  const inherited = definitionGallery.filter(value => !instanceGallery.some(item => item.role === value.role));
  const gallery = [...instanceGallery, ...inherited];
  if (gallery.length > 0) result.gallery = gallery;
  return Object.keys(result).length > 0 ? result : null;
}

function textList(value, maximumItems, maximumLength) {
  if (!Array.isArray(value) || value.length > maximumItems) return [];
  const values = value.map((item) => text(item, maximumLength));
  return values.every(Boolean) ? values : [];
}

function campaignDetails(value) {
  return {
    status: text(value?.status, 32),
    premise: text(value?.premise, 1_000),
    partyGoals: textList(value?.partyGoals, 3, 500),
    toneAndBoundaries: textList(value?.toneAndBoundaries, 8, 300),
  };
}

function optionalText(value, key, maximum) {
  if (value?.[key] === undefined) return undefined;
  return text(value[key], maximum) ?? undefined;
}

function optionalCampaignText(value, key, maximum) {
  if (!Object.hasOwn(value ?? {}, key)) return { value: undefined, partial: false };
  const safe = text(value?.[key], maximum);
  return { value: safe ?? undefined, partial: safe === null };
}

function campaignChapter(value, includeGmContext) {
  const status = value?.status === "active" || value?.status === "closed" ? value.status : null;
  const title = text(value?.title, 160);
  const partyQuestion = text(value?.partyQuestion, 500);
  const closingSummary = optionalCampaignText(value, "closingSummary", 1_000);
  const gmContext = includeGmContext
    ? optionalCampaignText(value, "gmContext", 1_000)
    : { value: undefined, partial: false };
  const unavailableFields = [
    ...(status ? [] : ["status"]),
    ...(title ? [] : ["title"]),
    ...(partyQuestion ? [] : ["partyQuestion"]),
    ...(closingSummary.partial ? ["closingSummary"] : []),
    ...(gmContext.partial ? ["gmContext"] : []),
  ];
  return {
    value: {
      ...(status ? { status } : {}),
      ...(title ? { title } : {}),
      ...(partyQuestion ? { partyQuestion } : {}),
      ...(closingSummary.value ? { closingSummary: closingSummary.value } : {}),
      ...(gmContext.value ? { gmContext: gmContext.value } : {}),
      ...(unavailableFields.length ? { unavailableFields } : {}),
    },
    partial: unavailableFields.length > 0,
  };
}

function campaignArc(value, includeGmContext) {
  const statuses = new Set(["active", "resolved", "abandoned"]);
  const status = statuses.has(value?.status) ? value.status : null;
  const title = text(value?.title, 160);
  const partyStake = text(value?.partyStake, 500);
  const closingSummary = optionalCampaignText(value, "closingSummary", 1_000);
  const gmContext = includeGmContext
    ? optionalCampaignText(value, "gmContext", 1_000)
    : { value: undefined, partial: false };
  const unavailableFields = [
    ...(status ? [] : ["status"]),
    ...(title ? [] : ["title"]),
    ...(partyStake ? [] : ["partyStake"]),
    ...(closingSummary.partial ? ["closingSummary"] : []),
    ...(gmContext.partial ? ["gmContext"] : []),
  ];
  return {
    value: {
      ...(status ? { status } : {}),
      ...(title ? { title } : {}),
      ...(partyStake ? { partyStake } : {}),
      ...(closingSummary.value ? { closingSummary: closingSummary.value } : {}),
      ...(gmContext.value ? { gmContext: gmContext.value } : {}),
      ...(unavailableFields.length ? { unavailableFields } : {}),
    },
    partial: unavailableFields.length > 0,
  };
}

function campaignSession(value) {
  const status = value?.status === "active" || value?.status === "ended" ? value.status : null;
  const ordinal = Number.isInteger(value?.ordinal) && value.ordinal >= 1 ? value.ordinal : null;
  if (ordinal === null) return null;
  return {
    value: { ordinal, ...(status ? { status } : {}), ...(status ? {} : { unavailableFields: ["status"] }) },
    partial: status === null,
  };
}

function campaignSessionRecap(value) {
  if (value?.protocolVersion !== "session.s0.c3-only.v1") return null;
  const recapFacet = (item, titleKey) => {
    const id = token(item?.id);
    const status = item?.status === "active" ? "active" : null;
    const title = text(item?.title, 160);
    const detail = text(item?.[titleKey], 500);
    const unavailableFields = [
      ...(id ? [] : ["id"]), ...(status ? [] : ["status"]),
      ...(title ? [] : ["title"]), ...(detail ? [] : [titleKey]),
    ];
    return {
      ...(id ? { id } : {}), ...(status ? { status } : {}), ...(title ? { title } : {}),
      ...(detail ? { [titleKey]: detail } : {}),
      ...(unavailableFields.length ? { unavailableFields } : {}),
    };
  };
  const chapter = recapFacet(value?.chapter, "partyQuestion");
  const arc = recapFacet(value?.arc, "partyStake");
  let milestones = [];
  let milestonesPartial = false;
  if (!Array.isArray(value?.milestones) || value.milestones.length > 5) {
    milestonesPartial = true;
  } else {
    milestones = value.milestones.flatMap((milestone) => {
    const milestoneChapterId = token(milestone?.chapterId);
    const titleValue = text(milestone?.title, 160);
    const closingSummary = text(milestone?.closingSummary, 1_000);
    const timestamp = text(milestone?.timestamp, 64);
    const sequence = Number.isInteger(milestone?.sequence) && milestone.sequence >= 0
      ? milestone.sequence
      : null;
      if (!milestoneChapterId || !titleValue || !closingSummary || !timestamp || sequence === null) {
        milestonesPartial = true;
        return [];
      }
      return [{ chapterId: milestoneChapterId, title: titleValue, closingSummary, timestamp, sequence }];
    });
  }
  const unavailableFields = [
    ...(chapter.unavailableFields?.length ? ["chapter"] : []),
    ...(arc.unavailableFields?.length ? ["arc"] : []),
    ...(milestonesPartial ? ["milestones"] : []),
  ];
  return {
    chapter,
    arc,
    milestones,
    ...(unavailableFields.length ? { unavailableFields } : {}),
  };
}

function campaignFieldRows(data, key, maximum, project) {
  if (!Object.hasOwn(data ?? {}, key)) return { items: [], coverage: "absent" };
  const values = data[key];
  if (!Array.isArray(values) || values.length > maximum) return { items: [], coverage: "invalid" };
  if (values.length === 0) return { items: [], coverage: "empty" };
  const idCounts = new Map();
  for (const value of values) {
    const id = token(value?.id);
    if (id) idCounts.set(id, (idCounts.get(id) ?? 0) + 1);
  }
  const items = [];
  let partial = false;
  for (const value of values) {
    const id = token(value?.id);
    const projected = id && idCounts.get(id) === 1 ? project(value) : null;
    if (!id || !projected) {
      partial = true;
      continue;
    }
    items.push({ id, ...projected.value });
    partial ||= projected.partial === true;
  }
  return { items, coverage: partial ? "partial" : "ready" };
}

function campaignLocationVisit(value, includeGmContext) {
  const firstVisitedMinute = Number.isSafeInteger(value?.firstVisitedMinute) && value.firstVisitedMinute >= 0
    ? value.firstVisitedMinute
    : null;
  const lastVisitedMinute = Number.isSafeInteger(value?.lastVisitedMinute) && value.lastVisitedMinute >= 0
    ? value.lastVisitedMinute
    : null;
  const visitCount = Number.isInteger(value?.visitCount) && value.visitCount >= 1 && value.visitCount <= 1_000_000
    ? value.visitCount
    : null;
  const status = value?.status === "current" || value?.status === "departed" ? value.status : null;
  const summary = text(value?.summary, 1_000);
  const memory = text(value?.memory, 2_000);
  const gmContext = includeGmContext
    ? optionalCampaignText(value, "gmContext", 2_000)
    : { value: undefined, partial: false };
  if (firstVisitedMinute === null || lastVisitedMinute === null ||
      lastVisitedMinute < firstVisitedMinute || visitCount === null) {
    return null;
  }
  const unavailableFields = [
    ...(status ? [] : ["status"]),
    ...(summary ? [] : ["summary"]),
    ...(memory ? [] : ["memory"]),
    ...(gmContext.partial ? ["gmContext"] : []),
  ];
  return {
    value: {
      firstVisitedMinute,
      lastVisitedMinute,
      visitCount,
      ...(status ? { status } : {}),
      ...(summary ? { summary } : {}),
      ...(memory ? { memory } : {}),
      ...(gmContext.value ? { gmContext: gmContext.value } : {}),
      ...(unavailableFields.length ? { unavailableFields } : {}),
    },
    partial: unavailableFields.length > 0,
  };
}

function worldFaction(value) {
  const unavailableFields = [];
  const fieldText = (key, maximum, fallback) => {
    if (!Object.hasOwn(value ?? {}, key)) {
      unavailableFields.push(key);
      return fallback;
    }
    const safe = text(value[key], maximum);
    if (!safe) unavailableFields.push(key);
    return safe ?? fallback;
  };
  const fieldTextList = (key, maximumItems, maximumLength) => {
    if (!Object.hasOwn(value ?? {}, key)) {
      unavailableFields.push(key);
      return [];
    }
    const raw = value[key];
    if (!Array.isArray(raw) || raw.length > maximumItems) {
      unavailableFields.push(key);
      return [];
    }
    if (raw.length === 0) return [];
    const projected = raw.map((item) => text(item, maximumLength)).filter(Boolean);
    if (projected.length !== raw.length) unavailableFields.push(key);
    return projected;
  };
  const statusValue = new Set(["draft", "active", "archived"]).has(value?.status)
    ? value.status : null;
  if (!statusValue) unavailableFields.push("status");
  const visibilityValue = new Set(["public", "party", "gm"]).has(value?.visibility)
    ? value.visibility : null;
  if (!visibilityValue) unavailableFields.push("visibility");
  const summary = fieldText("summary", 1_000, "Summary unavailable.");
  const goals = fieldTextList("goals", 5, 500);
  const methods = fieldTextList("methods", 5, 500);
  const assets = fieldTextList("assets", 10, 500);
  const agendaState = new Set(["ready", "advanced"]).has(value?.agenda?.state)
    ? value.agenda.state : null;
  const agendaSummary = text(value?.agenda?.summary, 1_000);
  if (!agendaState || !agendaSummary) unavailableFields.push("agenda");
  const result = {
    status: statusValue ?? "Status unavailable",
    visibility: visibilityValue ?? "Visibility unavailable",
    summary,
    goals,
    methods,
    assets,
    agenda: {
      state: agendaState ?? "Agenda unavailable",
      summary: agendaSummary ?? "Agenda unavailable.",
    },
  };
  if (unavailableFields.length) result.unavailableFields = [...new Set(unavailableFields)];
  return result;
}

function referenceId(value) {
  if (!value || typeof value !== "object" || Array.isArray(value)) return null;
  const keys = Object.keys(value).sort().join(",");
  if (keys !== "entityId" && keys !== "entityId,expectedArchetype") return null;
  if (value.expectedArchetype !== undefined && !token(value.expectedArchetype)) return null;
  return token(value.entityId);
}

function canonicalCharacterDiagnosticId(response, actorId, category) {
  const requestId = text(response?.headers?.get?.("x-request-id"), 160);
  return requestId ?? `canonical-character:${actorId}:${category}`;
}

function canonicalCharacterFailureCategory(response, code) {
  if (response?.status === 401 || response?.status === 403) return "authorization";
  if (response?.status === 409 || (typeof code === "string" && code.includes("STALE"))) return "stale-data";
  return "http";
}

function boundedInteger(value, minimum, maximum = Number.MAX_SAFE_INTEGER) {
  return Number.isSafeInteger(value) && value >= minimum && value <= maximum;
}

function namedCharacterReference(value) {
  return hasExactKeys(value, ["id", "label"]) && token(value.id) && text(value.label, 5_000);
}

function namedCharacterReferences(value, maximum) {
  return Array.isArray(value) && value.length <= maximum && value.every(namedCharacterReference);
}

function validCharacterInventory(value) {
  if (!hasExactKeys(value, ["items", "contentsDepth", "mayOmitDeeperContents"]) ||
      value.contentsDepth !== 4 || value.mayOmitDeeperContents !== true ||
      !Array.isArray(value.items) || value.items.length > 512) return false;

  const ids = new Set();
  const positions = new Set();
  for (const item of value.items) {
    if (!hasExactKeys(item, ["id", "name", "definition", "quantity", "slot", "parentItemId",
      "order", "depth", "childCount", "deeperContentsOmitted", "equipmentSlots"]) ||
        !token(item.id) || !text(item.name, 5_000) || !namedCharacterReference(item.definition) ||
        !boundedInteger(item.quantity, 1) || typeof item.slot !== "string" || item.slot.length > 200 ||
        !(item.parentItemId === null || token(item.parentItemId)) ||
        !boundedInteger(item.order, 0, 99) || !boundedInteger(item.depth, 1, 4) ||
        !boundedInteger(item.childCount, 0, 100) || typeof item.deeperContentsOmitted !== "boolean" ||
        !namedCharacterReferences(item.equipmentSlots, 32) || ids.has(item.id)) return false;
    ids.add(item.id);
    const position = `${item.parentItemId ?? "root"}:${item.order}`;
    if (positions.has(position)) return false;
    positions.add(position);
  }

  const byId = new Map(value.items.map((item) => [item.id, item]));
  const actualChildren = new Map();
  for (const item of value.items) {
    if (item.parentItemId === null) {
      if (item.depth !== 1) return false;
    } else {
      const parent = byId.get(item.parentItemId);
      if (!parent || item.depth !== parent.depth + 1) return false;
      actualChildren.set(parent.id, (actualChildren.get(parent.id) ?? 0) + 1);
    }
    const visited = new Set([item.id]);
    let parentId = item.parentItemId;
    while (parentId !== null) {
      if (visited.has(parentId)) return false;
      visited.add(parentId);
      parentId = byId.get(parentId)?.parentItemId ?? null;
    }
  }
  return value.items.every((item) =>
    item.childCount === (actualChildren.get(item.id) ?? 0) &&
    (item.depth !== 4 || item.deeperContentsOmitted));
}

function validCharacterWallet(value) {
  const codes = new Set(["cp", "sp", "ep", "gp", "pp"]);
  const copperValues = new Set([1, 10, 50, 100, 1000]);
  if (!hasExactKeys(value, ["coinCount", "copperValue", "gpCount", "denominations"]) ||
      !boundedInteger(value.coinCount, 0) || !boundedInteger(value.copperValue, 0) ||
      !boundedInteger(value.gpCount, 0) || !Array.isArray(value.denominations) ||
      value.denominations.length > 5) return false;
  const seen = new Set();
  let coinCount = 0;
  let copperValue = 0;
  let gpCount = 0;
  for (const row of value.denominations) {
    if (!hasExactKeys(row, ["denomination", "code", "count", "copperValuePerCoin", "totalCopperValue"]) ||
        !namedCharacterReference(row.denomination) || !codes.has(row.code) || seen.has(row.code) ||
        !boundedInteger(row.count, 1) || !copperValues.has(row.copperValuePerCoin) ||
        !boundedInteger(row.totalCopperValue, 1) ||
        row.totalCopperValue !== row.count * row.copperValuePerCoin) return false;
    seen.add(row.code);
    coinCount += row.count;
    copperValue += row.totalCopperValue;
    if (row.code === "gp") gpCount = row.count;
  }
  return coinCount === value.coinCount && copperValue === value.copperValue && gpCount === value.gpCount;
}

function validLevelOneRules(value, actorId) {
  return hasExactKeys(value, ["test", "subjectId", "armorClass", "attacks", "senses", "savingThrowCircumstances", "spellAccess", "equipment", "entitlements"]) &&
    value.test === "character-level-one-rules-project" && value.subjectId === actorId &&
    Array.isArray(value.attacks) && value.attacks.length <= 32 &&
    Array.isArray(value.senses) && value.senses.length <= 32 &&
    Array.isArray(value.savingThrowCircumstances) && value.savingThrowCircumstances.length <= 32 &&
    Array.isArray(value.entitlements) && value.entitlements.length <= 64 &&
    value.entitlements.every((entry) => hasExactKeys(entry, ["ownerDefinitionId", "entitlementKey", "status", "reason", "mechanicId", "nextCapabilityId", "knownValues", "missingValues", "source"]) &&
      token(entry.ownerDefinitionId) && token(entry.entitlementKey) && ["active", "pending"].includes(entry.status) &&
      (entry.reason === null || token(entry.reason)) && (entry.mechanicId === null || token(entry.mechanicId)) &&
      (entry.nextCapabilityId === null || token(entry.nextCapabilityId)) && entry.knownValues && typeof entry.knownValues === "object" &&
      !Array.isArray(entry.knownValues) && Array.isArray(entry.missingValues) && entry.missingValues.length <= 16 &&
      entry.missingValues.every(token) && hasExactKeys(entry.source, ["sourceId", "locator"]) &&
      token(entry.source.sourceId) && text(entry.source.locator, 5_000));
}

function fieldReference(value) {
  const id = token(ownValue(value, "id"));
  const label = text(ownValue(value, "label"), 5_000);
  return id && label ? { id, label } : null;
}

// Character sheet reads are display projections, not executable rule records.  Keep the
// actor binding as the one required identity, then consume each independently useful section
// field-by-field.  A malformed optional section is omitted locally; it must not erase an
// otherwise useful sheet or cause the browser to invent a numeric value.
function projectCharacterReference(value) {
  if (!value || typeof value !== "object" || Array.isArray(value)) return null;
  const id = token(ownValue(value, "id"));
  const label = text(ownValue(value, "label"), 5_000);
  return id && label ? { id, label } : null;
}

function projectCharacterArray(value, maximum, project, identity = null) {
  if (!Array.isArray(value) || value.length > maximum) return undefined;
  // A duplicate identity is ambiguous even when one of the rows is malformed. Count
  // identities from the source rows before projection so a later malformed duplicate cannot
  // leave an apparently actionable first row behind.
  const identityCounts = new Map();
  if (identity) {
    for (const entry of value) {
      const key = identity(entry);
      if (key !== null && key !== undefined) identityCounts.set(key, (identityCounts.get(key) ?? 0) + 1);
    }
  }
  const result = [];
  for (const entry of value) {
    const projected = project(entry);
    if (!projected) continue;
    const key = identity ? identity(projected) : null;
    if (key !== null && key !== undefined && (identityCounts.get(key) ?? 0) > 1) continue;
    result.push(projected);
  }
  return result;
}

function projectCharacterSheetData(value, actorId) {
  if (!value || typeof value !== "object" || Array.isArray(value)) return null;
  // The response's version is evidence, not a display admission gate. Older/newer producers
  // may add or retain fields while this adapter still safely consumes the fields it knows.
  const subject = projectCharacterReference(ownValue(value, "subject"));
  if (!subject || subject.id !== actorId) return null;
  const projected = { version: 2, subject };
  const optionalTextFields = ["pronouns", "appearance", "biography", "playerNotes"];
  const identityValue = ownValue(value, "identity");
  if (identityValue && typeof identityValue === "object" && !Array.isArray(identityValue)) {
    const identity = {};
    for (const key of optionalTextFields) {
      if (Object.hasOwn(identityValue, key)) {
        const item = text(ownValue(identityValue, key), 10_000);
        if (item) identity[key] = item;
      }
    }
    if (Object.keys(identity).length) projected.identity = identity;
  }
  const originValue = ownValue(value, "origin");
  if (originValue && typeof originValue === "object" && !Array.isArray(originValue)) {
    const species = projectCharacterReference(ownValue(originValue, "species"));
    const background = projectCharacterReference(ownValue(originValue, "background"));
    if (species && background) projected.origin = { species, background };
  }
  const experienceValue = ownValue(value, "experience");
  const experienceTotal = ownValue(experienceValue, "total");
  if (experienceValue && typeof experienceValue === "object" && !Array.isArray(experienceValue) &&
      boundedInteger(experienceTotal, 0)) projected.experience = { total: experienceTotal };
  const level = ownValue(value, "level");
  if (boundedInteger(level, 1, 100)) projected.level = level;
  const proficiencyBonus = ownValue(value, "proficiencyBonus");
  if (boundedInteger(proficiencyBonus, -1000, 1000)) projected.proficiencyBonus = proficiencyBonus;

  const section = (key, maximum, project, identity = null) => {
    if (!Object.hasOwn(value, key)) return;
    const result = projectCharacterArray(value[key], maximum, project, identity);
    // An explicitly empty array is confirmed empty; a non-empty array that yields no safe
    // rows is omitted as locally unavailable instead of being misreported as empty.
    if (result && (value[key].length === 0 || result.length > 0)) projected[key] = result;
  };

  section("classes", 64, (entry) => {
    const id = token(ownValue(entry, "id")), name = text(ownValue(entry, "name"), 5_000), classRef = projectCharacterReference(ownValue(entry, "class"));
    const level = ownValue(entry, "level");
    const subclassValue = ownValue(entry, "subclass");
    const subclass = subclassValue === null ? null : projectCharacterReference(subclassValue);
    return id && name && classRef && boundedInteger(level, 1, 20) && (subclassValue === null || subclass)
      ? { id, name, class: classRef, level, subclass } : null;
  }, (entry) => token(ownValue(entry, "id")));
  section("abilities", 32, (entry) => {
    const ability = projectCharacterReference(ownValue(entry, "ability"));
    const score = ownValue(entry, "score"), modifier = ownValue(entry, "modifier");
    return ability && boundedInteger(score, 1, 30) && boundedInteger(modifier, -1000, 1000)
      ? { ability, score, modifier } : null;
  }, (entry) => token(ownValue(ownValue(entry, "ability"), "id")));
  section("savingThrows", 32, (entry) => {
    const ability = projectCharacterReference(ownValue(entry, "ability"));
    const proficient = ownValue(entry, "proficient"), modifier = ownValue(entry, "modifier");
    return ability && typeof proficient === "boolean" && boundedInteger(modifier, -1000, 1000)
      ? { ability, proficient, modifier } : null;
  }, (entry) => token(ownValue(ownValue(entry, "ability"), "id")));
  section("skills", 64, (entry) => {
    const skill = projectCharacterReference(ownValue(entry, "skill")), ability = projectCharacterReference(ownValue(entry, "ability"));
    const proficient = ownValue(entry, "proficient"), expertise = ownValue(entry, "expertise"), modifier = ownValue(entry, "modifier");
    return skill && ability && typeof proficient === "boolean" && typeof expertise === "boolean" && boundedInteger(modifier, -1000, 1000)
      ? { skill, ability, proficient, expertise, modifier } : null;
  }, (entry) => token(ownValue(ownValue(entry, "skill"), "id")));
  const initiativeValue = ownValue(value, "initiative");
  if (initiativeValue && typeof initiativeValue === "object" && !Array.isArray(initiativeValue)) {
    const ability = projectCharacterReference(ownValue(initiativeValue, "ability"));
    const modifier = ownValue(initiativeValue, "modifier");
    if (ability && boundedInteger(modifier, -1000, 1000)) projected.initiative = { ability, modifier };
  }
  const hitPointsValue = ownValue(value, "hitPoints");
  const current = ownValue(hitPointsValue, "current"), maximum = ownValue(hitPointsValue, "maximum"), maximumReduction = ownValue(hitPointsValue, "maximumReduction");
  if (hitPointsValue && typeof hitPointsValue === "object" && !Array.isArray(hitPointsValue) && boundedInteger(current, 0) && boundedInteger(maximum, 0) &&
      boundedInteger(maximumReduction, 0) && current <= maximum)
    projected.hitPoints = { current, maximum, maximumReduction };
  const temporaryValue = ownValue(value, "temporaryHitPoints");
  const temporaryAmount = ownValue(temporaryValue, "amount");
  if (temporaryValue && typeof temporaryValue === "object" && !Array.isArray(temporaryValue) && boundedInteger(temporaryAmount, 0)) projected.temporaryHitPoints = { amount: temporaryAmount };
  const armorValue = ownValue(value, "armorClass");
  const armorClass = ownValue(armorValue, "value");
  if (armorValue && typeof armorValue === "object" && !Array.isArray(armorValue) && boundedInteger(armorClass, 0, 1000))
    projected.armorClass = { value: armorClass };
  const bodyValue = ownValue(value, "body");
  if (bodyValue && typeof bodyValue === "object" && !Array.isArray(bodyValue)) {
    const size = projectCharacterReference(ownValue(bodyValue, "size"));
    if (size) projected.body = { size };
  }
  section("movement", 32, (entry) => {
    const kind = projectCharacterReference(ownValue(entry, "kind")), unit = projectCharacterReference(ownValue(entry, "unit"));
    const numerator = ownValue(entry, "numerator"), denominator = ownValue(entry, "denominator");
    return kind && unit && boundedInteger(numerator, 0) && boundedInteger(denominator, 1)
      ? { kind, numerator, denominator, unit } : null;
  }, (entry) => token(ownValue(ownValue(entry, "kind"), "id")));
  section("senses", 64, (entry) => {
    const sense = projectCharacterReference(ownValue(entry, "sense"));
    if (!sense) return null;
    const numerator = ownValue(entry, "numerator"), denominator = ownValue(entry, "denominator"), unitValue = ownValue(entry, "unit");
    const hasMeasurement = numerator !== undefined || denominator !== undefined || unitValue !== undefined;
    if (!hasMeasurement) return { sense };
    const unit = projectCharacterReference(unitValue);
    return boundedInteger(numerator, 0) && boundedInteger(denominator, 1) && unit
      ? { sense, numerator, denominator, unit } : null;
  }, (entry) => token(ownValue(ownValue(entry, "sense"), "id")));
  section("conditions", 128, (entry) => {
    const condition = projectCharacterReference(ownValue(entry, "condition"));
    const level = ownValue(entry, "level");
    return condition && (level === null || boundedInteger(level, 0, 100))
      ? { condition, level } : null;
  }, (entry) => token(ownValue(ownValue(entry, "condition"), "id")));
  section("proficiencies", 256, (entry) => {
    const proficiency = projectCharacterReference(ownValue(entry, "proficiency")), rank = projectCharacterReference(ownValue(entry, "rank"));
    return proficiency && rank ? { proficiency, rank } : null;
  }, (entry) => token(ownValue(ownValue(entry, "proficiency"), "id")));
  section("features", 2048, (entry) => {
    const feature = projectCharacterReference(ownValue(entry, "feature")), grantedBy = projectCharacterReference(ownValue(entry, "grantedBy")),
      grantKind = projectCharacterReference(ownValue(entry, "grantKind"));
    const classLevel = ownValue(entry, "classLevel");
    return feature && grantedBy && grantKind && (classLevel === null || boundedInteger(classLevel, 1, 20))
      ? { feature, grantedBy, grantKind, classLevel } : null;
  }, (entry) => token(ownValue(ownValue(entry, "feature"), "id")));
  section("resources", 256, (entry) => {
    const id = token(ownValue(entry, "id")), name = text(ownValue(entry, "name"), 5_000), definition = projectCharacterReference(ownValue(entry, "definition"));
    const expended = ownValue(entry, "expended");
    return id && name && definition && boundedInteger(expended, 0)
      ? { id, name, definition, expended } : null;
  }, (entry) => token(ownValue(entry, "id")));
  section("spellcasting", 64, (entry) => {
    const id = token(ownValue(entry, "id")), name = text(ownValue(entry, "name"), 5_000), sourceDefinition = projectCharacterReference(ownValue(entry, "sourceDefinition")),
      ability = projectCharacterReference(ownValue(entry, "ability"));
    const preparedSpells = projectCharacterArray(ownValue(entry, "preparedSpells"), 2048, projectCharacterReference, (entry) => token(ownValue(entry, "id")));
    const availableSpells = projectCharacterArray(ownValue(entry, "availableSpells"), 2048, projectCharacterReference, (entry) => token(ownValue(entry, "id")));
    return id && name && sourceDefinition && ability && preparedSpells && availableSpells
      ? { id, name, sourceDefinition, ability, preparedSpells, availableSpells } : null;
  }, (entry) => token(ownValue(entry, "id")));
  section("actions", 512, (entry) => {
    const id = token(ownValue(entry, "id")), name = text(ownValue(entry, "name"), 5_000);
    const activities = projectCharacterArray(ownValue(entry, "activities"), 256, projectCharacterReference, (item) => token(ownValue(item, "id")));
    return id && name && activities ? { id, name, activities } : null;
  }, (entry) => token(ownValue(entry, "id")));

  // Inventory and wallet are independently read by the field-based adapters. Strip additive
  // metadata before applying their own closed tree checks; malformed values must not poison hero
  // fields, while harmless producer additions remain inert.
  const inventoryValue = ownValue(value, "inventory");
  const inventoryItems = ownValue(inventoryValue, "items");
  const inventory = inventoryValue && typeof inventoryValue === "object" && !Array.isArray(inventoryValue) &&
    Array.isArray(inventoryItems)
    ? {
      items: inventoryItems.map((item) => item && typeof item === "object" && !Array.isArray(item) ? {
        id: ownValue(item, "id"), name: ownValue(item, "name"), definition: ownValue(item, "definition"), quantity: ownValue(item, "quantity"), slot: ownValue(item, "slot"),
        parentItemId: ownValue(item, "parentItemId"), order: ownValue(item, "order"), depth: ownValue(item, "depth"), childCount: ownValue(item, "childCount"),
        deeperContentsOmitted: ownValue(item, "deeperContentsOmitted"), equipmentSlots: ownValue(item, "equipmentSlots"),
      } : item),
      contentsDepth: ownValue(inventoryValue, "contentsDepth"),
      mayOmitDeeperContents: ownValue(inventoryValue, "mayOmitDeeperContents"),
    } : null;
  if (inventory && validCharacterInventory(inventory)) projected.inventory = inventory;
  const walletValue = ownValue(value, "wallet");
  const walletDenominations = ownValue(walletValue, "denominations");
  const wallet = walletValue && typeof walletValue === "object" && !Array.isArray(walletValue) &&
    Array.isArray(walletDenominations)
    ? {
      coinCount: ownValue(walletValue, "coinCount"), copperValue: ownValue(walletValue, "copperValue"), gpCount: ownValue(walletValue, "gpCount"),
      denominations: walletDenominations.map((row) => row && typeof row === "object" && !Array.isArray(row) ? {
        denomination: ownValue(row, "denomination"), code: ownValue(row, "code"), count: ownValue(row, "count"),
        copperValuePerCoin: ownValue(row, "copperValuePerCoin"), totalCopperValue: ownValue(row, "totalCopperValue"),
      } : row),
    } : null;
  if (wallet && validCharacterWallet(wallet)) projected.wallet = wallet;
  return projected;
}

function projectCharacterDossierData(value, actorId) {
  if (!value || typeof value !== "object" || Array.isArray(value)) return null;
  // Dossier revisions are evidence for the caller, not a reason to discard known sheet
  // fields. Accept either the nested sheet shape or a sheet-like response and project only
  // the fields this display uses.
  const sheet = projectCharacterSheetData(ownValue(value, "sheet") ?? value, actorId);
  if (!sheet) return null;
  const definition = (entry) => {
    if (!entry || typeof entry !== "object" || Array.isArray(entry)) return null;
    const id = token(ownValue(entry, "id")), label = text(ownValue(entry, "label"), 5_000);
    if (!id || !label) return null;
    const unavailableFields = [];
    const projected = { id, label };
    const canonicalName = text(ownValue(entry, "canonicalName"), 5_000);
    if (canonicalName) projected.canonicalName = canonicalName;
    else unavailableFields.push("canonicalName");
    const kind = token(ownValue(entry, "kind"));
    if (kind) projected.kind = kind;
    else unavailableFields.push("kind");
    const statusValue = ownValue(entry, "status");
    if (statusValue === "active" || statusValue === "identity-only") projected.status = statusValue;
    else unavailableFields.push("status");
    if (hasOwnValue(entry, "summary")) {
      const summaryValue = ownValue(entry, "summary");
      if (summaryValue === null) projected.summary = null;
      else {
        const summary = text(summaryValue, 5_000);
        if (summary) projected.summary = summary;
        else unavailableFields.push("summary");
      }
    } else unavailableFields.push("summary");
    if (hasOwnValue(entry, "source")) {
      const sourceValue = ownValue(entry, "source");
      if (sourceValue === null) projected.source = null;
      else if (sourceValue && typeof sourceValue === "object" && !Array.isArray(sourceValue) &&
          token(ownValue(sourceValue, "sourceId")) && text(ownValue(sourceValue, "locator"), 5_000)) {
        projected.source = { sourceId: ownValue(sourceValue, "sourceId"), locator: ownValue(sourceValue, "locator") };
      } else unavailableFields.push("source");
    } else unavailableFields.push("source");
    if (unavailableFields.length) projected.unavailableFields = unavailableFields;
    return projected;
  };
  const unavailableSections = new Set();
  const projectRows = (raw, maximum, project, identity) => {
    if (!Array.isArray(raw) || raw.length > maximum) return { rows: undefined, partial: true };
    const counts = new Map();
    let partial = false;
    for (const entry of raw) {
      const key = identity(entry);
      if (key === null) partial = true;
      else counts.set(key, (counts.get(key) ?? 0) + 1);
    }
    const rows = [];
    for (const entry of raw) {
      const projected = project(entry);
      const key = identity(entry);
      if (!projected || key === null || (counts.get(key) ?? 0) !== 1) {
        partial = true;
        continue;
      }
      if (projected.unavailableFields?.length) partial = true;
      rows.push(projected);
    }
    return { rows, partial };
  };
  const projectTrait = (entry) => {
    if (!entry || typeof entry !== "object" || Array.isArray(entry)) return null;
    const key = token(ownValue(entry, "key")), label = text(ownValue(entry, "label"), 5_000);
    if (!key || !label) return null;
    const unavailableFields = [];
    const projected = { key, label };
    const statusValue = ownValue(entry, "status");
    const status = statusValue === "active" || statusValue === "pending" ? statusValue : null;
    if (status) projected.status = status;
    else unavailableFields.push("status");
    for (const [field, target] of [["reason", "reason"], ["mechanicId", "mechanicId"]]) {
      if (!hasOwnValue(entry, field)) { unavailableFields.push(field); continue; }
      const fieldValue = ownValue(entry, field);
      if (fieldValue === null) projected[target] = null;
      else {
        const value = token(fieldValue);
        if (value) projected[target] = value;
        else unavailableFields.push(field);
      }
    }
    if (hasOwnValue(entry, "source")) {
      const sourceValue = ownValue(entry, "source");
      if (sourceValue === null) projected.source = null;
      else if (sourceValue && typeof sourceValue === "object" && !Array.isArray(sourceValue) &&
          token(ownValue(sourceValue, "sourceId")) && text(ownValue(sourceValue, "locator"), 5_000)) {
        projected.source = { sourceId: ownValue(sourceValue, "sourceId"), locator: ownValue(sourceValue, "locator") };
      } else unavailableFields.push("source");
    } else unavailableFields.push("source");
    if (unavailableFields.length) projected.unavailableFields = unavailableFields;
    return projected;
  };
  const originValue = ownValue(value, "origin");
  let origin;
  if (originValue && typeof originValue === "object" && !Array.isArray(originValue)) {
    const species = definition(ownValue(originValue, "species"));
    const background = definition(ownValue(originValue, "background"));
    let traits;
    if (hasOwnValue(originValue, "traits")) {
      const projectedTraits = projectRows(ownValue(originValue, "traits"), 128, projectTrait,
        (entry) => token(ownValue(entry, "key")));
      traits = projectedTraits.rows;
      if (projectedTraits.partial) unavailableSections.add("origin.traits");
    }
    if (hasOwnValue(originValue, "species") && (!species || species.unavailableFields?.length)) unavailableSections.add("origin.species");
    if (hasOwnValue(originValue, "background") && (!background || background.unavailableFields?.length)) unavailableSections.add("origin.background");
    origin = {};
    if (species) origin.species = species;
    if (background) origin.background = background;
    if (traits) origin.traits = traits;
    if (Object.keys(origin).length === 0) origin = undefined;
  } else if (hasOwnValue(value, "origin")) unavailableSections.add("origin");

  const classesValue = ownValue(value, "classes");
  let classes;
  if (hasOwnValue(value, "classes")) {
    const projectedClasses = projectRows(classesValue, 20, (entry) => {
      if (!entry || typeof entry !== "object" || Array.isArray(entry)) return null;
      const id = token(ownValue(entry, "id")), name = text(ownValue(entry, "name"), 5_000), level = ownValue(entry, "level");
      if (!id || !name || !boundedInteger(level, 1, 20)) return null;
      const unavailableFields = [];
      const projected = { id, name, level };
      if (hasOwnValue(entry, "definition")) {
        const detail = definition(ownValue(entry, "definition"));
        if (detail) {
          projected.definition = detail;
          if (detail.unavailableFields?.length) unavailableFields.push("definition");
        } else unavailableFields.push("definition");
      } else unavailableFields.push("definition");
      if (hasOwnValue(entry, "subclass")) {
        const subclassValue = ownValue(entry, "subclass");
        if (subclassValue === null) projected.subclass = null;
        else {
          const subclass = projectCharacterReference(subclassValue);
          if (subclass) projected.subclass = subclass;
          else unavailableFields.push("subclass");
        }
      } else unavailableFields.push("subclass");
      if (unavailableFields.length) projected.unavailableFields = unavailableFields;
      return projected;
    }, (entry) => token(ownValue(entry, "id")));
    classes = projectedClasses.rows;
    if (projectedClasses.partial) unavailableSections.add("classes");
  }
  const definitionsValue = ownValue(value, "definitions");
  let definitions;
  if (hasOwnValue(value, "definitions")) {
    const projectedDefinitions = projectRows(definitionsValue, 512, definition,
      (entry) => token(ownValue(entry, "id")));
    definitions = projectedDefinitions.rows;
    if (projectedDefinitions.partial) unavailableSections.add("definitions");
  }
  const featuresValue = ownValue(value, "features");
  let features;
  if (hasOwnValue(value, "features")) {
    const projectedFeatures = projectRows(featuresValue, 1_024, (entry) => {
      if (!entry || typeof entry !== "object" || Array.isArray(entry)) return null;
      const featureDefinition = definition(ownValue(entry, "definition")), grantedBy = definition(ownValue(entry, "grantedBy"));
      if (!featureDefinition || !grantedBy) return null;
      const unavailableFields = [];
      const projected = { definition: featureDefinition, grantedBy };
      if (featureDefinition.unavailableFields?.length) unavailableFields.push("definition");
      if (grantedBy.unavailableFields?.length) unavailableFields.push("grantedBy");
      const grantKind = token(ownValue(entry, "grantKind"));
      if (grantKind) projected.grantKind = grantKind;
      else unavailableFields.push("grantKind");
      if (hasOwnValue(entry, "classLevel")) {
        const classLevelValue = ownValue(entry, "classLevel");
        if (classLevelValue === null) projected.classLevel = null;
        else if (boundedInteger(classLevelValue, 1, 20)) projected.classLevel = classLevelValue;
        else unavailableFields.push("classLevel");
      } else unavailableFields.push("classLevel");
      if (hasOwnValue(entry, "configurationKey")) {
        const configurationValue = ownValue(entry, "configurationKey");
        if (configurationValue === null) projected.configurationKey = null;
        else {
          const configurationKey = token(configurationValue);
          if (configurationKey) projected.configurationKey = configurationKey;
          else unavailableFields.push("configurationKey");
        }
      } else unavailableFields.push("configurationKey");
      const implementation = ownValue(entry, "implementation");
      if (implementation && typeof implementation === "object" && !Array.isArray(implementation)) {
        const implementationStatus = ownValue(implementation, "status");
        const implementationReasonValue = ownValue(implementation, "reason");
        const implementationReason = implementationReasonValue === null ? null : token(implementationReasonValue);
        const entitlementValue = ownValue(implementation, "entitlementKey");
        const entitlementKey = entitlementValue === null ? null : token(entitlementValue);
        const nextCapabilityValue = ownValue(implementation, "nextCapabilityId");
        const nextCapabilityId = nextCapabilityValue === null ? null : token(nextCapabilityValue);
        if (["recorded", "executable", "pending"].includes(implementationStatus) &&
            implementationReason !== undefined && entitlementKey !== undefined && nextCapabilityId !== undefined) {
          projected.implementation = { status: implementationStatus, reason: implementationReason, entitlementKey, nextCapabilityId };
        } else unavailableFields.push("implementation");
      } else unavailableFields.push("implementation");
      if (unavailableFields.length) projected.unavailableFields = [...new Set(unavailableFields)];
      return projected;
    }, (entry) => {
      const feature = ownValue(entry, "definition");
      const grantedBy = ownValue(entry, "grantedBy");
      const featureId = token(ownValue(feature, "id"));
      const grantedById = token(ownValue(grantedBy, "id"));
      return featureId && grantedById ? `${featureId}\u0000${grantedById}` : null;
    });
    features = projectedFeatures.rows;
    if (projectedFeatures.partial) unavailableSections.add("features");
  }
  const provenanceValue = ownValue(value, "provenance");
  const provenance = provenanceValue && typeof provenanceValue === "object" && !Array.isArray(provenanceValue) &&
    ownValue(provenanceValue, "sheetQueryId") === "dnd2024.query.character-sheet-v2" &&
    ownValue(provenanceValue, "sheetProjectionId") === "dnd2024.mechanic.character-sheet-v2.project" &&
    ownValue(provenanceValue, "dossierProjectionId") === "dnd2024.mechanic.character-dossier-v1.project" &&
    boundedInteger(ownValue(provenanceValue, "definitionCount"), 0, 512) && ownValue(provenanceValue, "inventoryDepth") === 4 &&
    ownValue(provenanceValue, "ruleTextPolicy") === "canonical-only"
    ? {
      sheetQueryId: ownValue(provenanceValue, "sheetQueryId"),
      sheetProjectionId: ownValue(provenanceValue, "sheetProjectionId"),
      dossierProjectionId: ownValue(provenanceValue, "dossierProjectionId"),
      definitionCount: ownValue(provenanceValue, "definitionCount"),
      inventoryDepth: ownValue(provenanceValue, "inventoryDepth"),
      ruleTextPolicy: ownValue(provenanceValue, "ruleTextPolicy"),
    } : null;
  if (hasOwnValue(value, "provenance") && !provenance) unavailableSections.add("provenance");
  const inventoryValue = ownValue(value, "inventory");
  const inventoryDefinitionsValue = ownValue(inventoryValue, "definitions");
  let inventory;
  if (hasOwnValue(value, "inventory")) {
    const projectedInventory = projectRows(inventoryDefinitionsValue, 512, definition,
      (entry) => token(ownValue(entry, "id")));
    if (projectedInventory.partial) unavailableSections.add("inventory.definitions");
    if (projectedInventory.rows && ownValue(inventoryValue, "contentsDepth") === 4 &&
        ownValue(inventoryValue, "mayOmitDeeperContents") === true) {
      inventory = { definitions: projectedInventory.rows, contentsDepth: 4, mayOmitDeeperContents: true };
    } else if (!projectedInventory.rows || ownValue(inventoryValue, "contentsDepth") !== 4 ||
        ownValue(inventoryValue, "mayOmitDeeperContents") !== true) unavailableSections.add("inventory");
  }
  let levelOneRules;
  if (hasOwnValue(value, "levelOneRules")) {
    if (validLevelOneRules(ownValue(value, "levelOneRules"), actorId)) levelOneRules = ownValue(value, "levelOneRules");
    else unavailableSections.add("levelOneRules");
  }
  const hasDossierFields = ["origin", "classes", "features", "inventory", "levelOneRules", "definitions", "provenance"]
    .some((key) => hasOwnValue(value, key));
  if (!hasDossierFields) return sheet;
  for (const key of ["origin", "classes", "features", "inventory", "levelOneRules", "definitions", "provenance"])
    if (!hasOwnValue(value, key)) unavailableSections.add(key);
  const dossier = {
    ...(origin ? { origin } : {}),
    ...(classes ? { classes } : {}),
    ...(features ? { features } : {}),
    ...(inventory ? { inventory } : {}),
    ...(levelOneRules ? { levelOneRules } : {}),
    ...(definitions ? { definitions } : {}),
    ...(provenance ? { provenance } : {}),
    ...(unavailableSections.size ? {
      coverage: "partial",
      unavailableSections: [...unavailableSections].sort(),
    } : { coverage: "complete" }),
  };
  return { ...sheet, dossier };
}

function ownValue(value, key) {
  return hasOwnValue(value, key)
    ? value[key] : undefined;
}

function hasOwnValue(value, key) {
  return Boolean(value && typeof value === "object" && !Array.isArray(value) && Object.hasOwn(value, key));
}

// Inventory rows can open the item read routes, whose address grammar is narrower than generic
// ECS entity IDs. Do not render an identity that downstream detail actions cannot address safely.
function addressableInventoryItemId(value) {
  const id = token(value);
  return id && /^[a-zA-Z0-9][a-zA-Z0-9._:-]{0,199}$/u.test(id) ? id : null;
}

function inventoryReasons(value) {
  return Array.isArray(value)
    ? [...new Set(value.filter((reason) => reason === "unclassified-content"))]
    : [];
}

/**
 * Projects one inventory page field by field. The container identity is a required scope binding;
 * item identity is required for a row to be actionable. Everything else degrades locally.
 */
function projectInventoryContainer(value, actorId) {
  if (!value || typeof value !== "object" || Array.isArray(value)) return null;
  if (ownValue(value, "state") === "forbidden" || ownValue(value, "state") === "denied") return null;
  const containerValue = ownValue(value, "container");
  const containerId = token(ownValue(containerValue, "id"));
  const allItems = ownValue(value, "items");
  if (!containerId || containerId !== actorId || !Array.isArray(allItems)) return null;
  const container = { id: containerId, label: text(ownValue(containerValue, "label"), 5_000) ?? "Inventory owner unavailable" };

  const notices = new Set();
  const reasons = inventoryReasons(ownValue(value, "reasons"));
  if (reasons.length) notices.add("source-incomplete");
  if (ownValue(value, "state") !== "ready" || ownValue(ownValue(value, "limits"), "directComplete") !== true)
    notices.add("source-incomplete");
  const sourceItems = allItems.slice(0, 200);
  if (allItems.length > 200) notices.add("item-bound");
  const ids = new Map();
  // Scan the bounded response, not just displayed rows: a duplicate after the display limit
  // must not leave its earlier twin usable for navigation or a future action.
  for (const item of allItems) {
    const id = addressableInventoryItemId(ownValue(item, "id"));
    if (!id || id === actorId) {
      notices.add("invalid-item-identity");
      continue;
    }
    ids.set(id, (ids.get(id) ?? 0) + 1);
  }

  const items = [];
  for (let index = 0; index < sourceItems.length; index++) {
    const item = sourceItems[index];
    const id = addressableInventoryItemId(ownValue(item, "id"));
    if (!id || id === actorId || ids.get(id) !== 1) {
      if (id && ids.get(id) !== 1) notices.add("duplicate-item-identity");
      continue;
    }
    const itemNotices = [];
    const name = text(ownValue(item, "name"), 400);
    if (!name) itemNotices.push("name");
    const definitionValue = ownValue(item, "definition");
    const definition = fieldReference(definitionValue);
    if (definitionValue === undefined || (definitionValue !== null && !definition)) itemNotices.push("definition");
    const hasQuantity = hasOwnValue(item, "quantity");
    const quantityValue = ownValue(item, "quantity");
    const quantity = quantityValue === null || quantityValue === undefined
      ? null
      : boundedInteger(quantityValue, 0) ? quantityValue : null;
    const quantityState = !hasQuantity ? "absent"
      : quantityValue === null ? "null"
        : quantity === null ? "invalid" : "value";
    if (quantityState === "absent" || quantityState === "invalid") itemNotices.push("quantity");
    const slotValue = ownValue(item, "slot");
    const slot = typeof slotValue === "string" && slotValue.length <= 200 ? slotValue : "";
    if (slotValue === undefined || typeof slotValue !== "string" || slotValue.length > 200) itemNotices.push("slot");
    const orderValue = ownValue(item, "order");
    const order = boundedInteger(orderValue, 0, 199) ? orderValue : index;
    if (orderValue === undefined || (order === index && orderValue !== index)) itemNotices.push("order");
    const equipmentValue = ownValue(item, "equipmentSlots");
    const equipmentSlots = Array.isArray(equipmentValue)
      ? equipmentValue.map(fieldReference).filter(Boolean).slice(0, 32)
      : [];
    const equipmentSlotsKnown = Array.isArray(equipmentValue) &&
      equipmentValue.length <= 32 && equipmentSlots.length === equipmentValue.length;
    if (!equipmentSlotsKnown) itemNotices.push("equipment");
    const hasContainer = hasOwnValue(item, "isContainer");
    const containerValue = ownValue(item, "isContainer");
    const isContainer = typeof containerValue === "boolean" ? containerValue : null;
    const containerState = !hasContainer ? "absent" : isContainer === null ? "invalid" : "value";
    if (containerState !== "value") itemNotices.push("container");
    if (itemNotices.length) notices.add("item-fields-unavailable");
    items.push({
      id,
      name: name ?? "Name unavailable",
      definition,
      quantity,
      quantityState,
      slot,
      order,
      equipmentSlots,
      equipmentSlotsKnown,
      classification: ["item", "unclassified"].includes(ownValue(item, "classification"))
        ? ownValue(item, "classification") : "unknown",
      isContainer,
      containerState,
      unavailableFields: itemNotices,
    });
  }
  return {
    container,
    // An unproven direct-completeness claim must not turn an empty projected list into a
    // statement that the container is empty.
    state: notices.size ? "partial" : "ready",
    reasons,
    notices: [...notices],
    items,
    limits: {
      contentsDepth: boundedInteger(ownValue(ownValue(value, "limits"), "contentsDepth"), 1, 16)
        ? ownValue(ownValue(value, "limits"), "contentsDepth") : null,
      directComplete: ownValue(ownValue(value, "limits"), "directComplete") === true ? true : null,
      recursiveComplete: typeof ownValue(ownValue(value, "limits"), "recursiveComplete") === "boolean"
        ? ownValue(ownValue(value, "limits"), "recursiveComplete") : null,
    },
  };
}

/** Projects wallet fields independently from inventory rows and never fabricates a missing total. */
function projectInventoryWallet(value, actorId) {
  if (!value || typeof value !== "object" || Array.isArray(value)) return null;
  if (ownValue(value, "state") === "forbidden" || ownValue(value, "state") === "denied") return null;
  const ownerId = token(ownValue(ownValue(value, "owner"), "id"));
  const wallet = ownValue(value, "wallet");
  if (!ownerId || ownerId !== actorId || !wallet || typeof wallet !== "object" || Array.isArray(wallet)) return null;
  const notices = [];
  const total = (key) => boundedInteger(ownValue(wallet, key), 0) ? ownValue(wallet, key) : null;
  const coinCount = total("coinCount");
  const copperValue = total("copperValue");
  const gpCount = total("gpCount");
  if (coinCount === null || copperValue === null || gpCount === null) notices.push("totals");
  const denominationCodes = new Set(["cp", "sp", "ep", "gp", "pp"]);
  const seen = new Set();
  const denominationRows = ownValue(wallet, "denominations");
  const denominations = Array.isArray(denominationRows) ? denominationRows.flatMap((row) => {
    const denomination = fieldReference(ownValue(row, "denomination"));
    const code = ownValue(row, "code");
    const count = ownValue(row, "count");
    if (!denomination || !denominationCodes.has(code) || seen.has(code) ||
        !boundedInteger(count, 1)) {
      notices.push("denominations");
      return [];
    }
    seen.add(code);
    return [{ denomination, code, count }];
  }) : [];
  if (!Array.isArray(denominationRows)) notices.push("denominations");
  const complete = ownValue(value, "state") === "ready" &&
    ownValue(ownValue(value, "limits"), "complete") === true && notices.length === 0;
  return {
    wallet: { coinCount, copperValue, gpCount, denominations },
    complete,
    notices: [...new Set(notices)],
  };
}

function validWorldLocationScope(value, scopeId) {
  if (!["ready", "forbidden"].includes(ownValue(value, "state")) ||
      !Array.isArray(value.locations) || value.locations.length > 100 ||
      ownValue(value.limits, "contentsDepth") !== 1 || ownValue(value.limits, "locationCount") !== 100 ||
      ownValue(value.limits, "complete") !== true) return false;
  if (value.state === "forbidden") return value.scope === null && value.locations.length === 0;
  return validWorldLocationMembership(value, scopeId);
}

function validWorldLocationScopePage(value, scopeId, offset) {
  if (!["ready", "forbidden"].includes(ownValue(value, "state")) ||
      !Array.isArray(value.locations) || value.locations.length > 100 ||
      !Number.isInteger(value.totalCount) || value.totalCount < value.locations.length || value.totalCount > 200 ||
      typeof value.complete !== "boolean" || !(value.nextCursor === null || value.nextCursor === "100") ||
      value.locations.length !== Math.max(0, Math.min(100, value.totalCount - offset)) ||
      value.nextCursor !== (offset === 0 && value.totalCount > 100 ? "100" : null) ||
      value.complete !== (value.nextCursor === null)) return false;
  if (value.state === "forbidden") return value.scope === null && value.locations.length === 0 &&
    value.totalCount === 0 && value.complete && value.nextCursor === null;
  return validWorldLocationMembership(value, scopeId);
}

function validWorldLocationMembership(value, scopeId) {
  // Authorization, scope and containment are transport prerequisites, not display fields.
  if (token(ownValue(value.scope, "id")) !== scopeId ||
      !(ownValue(value.scope, "parentId") === null || token(ownValue(value.scope, "parentId")))) return false;
  return value.locations.every((location) => !token(ownValue(location, "id")) ||
    (location.id !== scopeId && ownValue(location, "parentId") === scopeId));
}

function projectWorldLocationRecords(value) {
  const counts = new Map();
  for (const record of value.locations) {
    const id = token(ownValue(record, "id"));
    if (id) counts.set(id, (counts.get(id) ?? 0) + 1);
  }
  const members = value.locations.filter((record) => {
    const id = token(ownValue(record, "id"));
    return id && counts.get(id) === 1;
  });
  const records = [value.scope, ...members].map((record) => {
    const unavailableFields = [];
    const readText = (field, maximum) => {
      const result = text(ownValue(record, field), maximum);
      if (!result) unavailableFields.push(field);
      return result;
    };
    const name = readText("name", 400) ?? "Name unavailable";
    const kind = readText("kind", 200);
    const summary = readText("summary", 1_000);
    const anchor = ownValue(record, "mapAnchor");
    const mapAnchor = Number.isInteger(ownValue(anchor, "x")) && anchor.x >= 0 && anchor.x <= 1_000 &&
      Number.isInteger(ownValue(anchor, "y")) && anchor.y >= 0 && anchor.y <= 1_000
      ? { x: anchor.x, y: anchor.y } : null;
    if (anchor !== null && !mapAnchor) unavailableFields.push("mapAnchor");
    const slot = text(ownValue(record, "slot"), 200);
    return {
      id: record.id, parentId: record.parentId, name,
      ...(kind ? { kind } : {}), ...(summary ? { summary } : {}),
      ...(slot ? { slot } : {}), ...(mapAnchor ? { mapAnchor } : {}),
      ...(ownValue(record, "childScopeKnownEmpty") === true ? { childScopeKnownEmpty: true } : {}),
      ...(unavailableFields.length ? { unavailableFields } : {}),
    };
  });
  return {
    records,
    coverage: members.length !== value.locations.length || records.some((record) => record.unavailableFields)
      ? "partial" : "complete",
  };
}

function worldLocationDiagnosticId(scopeId, category) {
  const safe = String(scopeId).replace(/[^a-zA-Z0-9.-]/gu, "-").slice(0, 80) || "unknown";
  return `world-location-scope:${safe}:${category}`;
}

/** Reads one exact authorized containment scope; it never lists the world entity directory. */
export async function readRegisteredWorldLocationScope({
  fetchImpl, origin, applicationId, stateSpaceId, scopeId, perspective, includeMedia = true,
}) {
  const applicationRoot = `/api/applications/${encodeURIComponent(applicationId)}` +
    `/state-spaces/${encodeURIComponent(stateSpaceId)}`;
  const resource = `${applicationRoot}/entities/${encodeURIComponent(scopeId)}` +
    `/read-models/${encodeURIComponent(worldLocationScopeContract.id)}` +
    `?perspective=${encodeURIComponent(perspective)}`;
  try {
    const result = await readModelResponse({
      fetchImpl,
      resource: url(origin, resource),
      init: { headers: { Accept: "application/json" }, cache: "no-store" },
      applicationId,
      stateSpaceId,
      query: worldLocationScopeContract,
      maximumBodyBytes: 270_000,
      maximumDataBytes: 262_144,
      statusPolicy: { ready: [200], forbidden: [403], stale: [409], unavailable: "remaining" },
      validate: (value) => validWorldLocationScope(value, scopeId),
    });
    if (result.status !== "ready") return {
      status: result.status === "forbidden" ? "forbidden" : "error",
      items: [],
      diagnosticId: worldLocationDiagnosticId(scopeId, result.status),
    };
    if (result.data.state === "forbidden") return {
      status: "forbidden", items: [],
      diagnosticId: worldLocationDiagnosticId(scopeId, "audience"),
    };
    const { records, coverage } = projectWorldLocationRecords(result.data);
    const mediaById = new Map();
    const ownerMedia = includeMedia
      ? projectReadModelOwnerMedia(result.media, scopeId)
      : null;
    if (ownerMedia) mediaById.set(scopeId, ownerMedia);
    const pendingMediaIds = records
      .map((record) => record.id)
      .filter((recordId) => recordId !== scopeId || ownerMedia?.complete !== true);
    if (includeMedia && pendingMediaIds.length > 0) {
      try {
        const response = await fetchImpl(url(origin, `${applicationRoot}/media-batch`), {
          method: "POST",
          headers: { "Content-Type": "application/json", Accept: "application/json" },
          cache: "no-store",
          body: JSON.stringify({ entityIds: pendingMediaIds, perspective }),
        });
        const decoded = response?.ok ? await readBoundedJson(response, 4 * 1024 * 1024) : null;
        const media = decoded?.status === "ready" ? decoded.value : null;
        const projected = projectLocationMediaBatch(
          media, applicationId, stateSpaceId, pendingMediaIds,
        );
        if (projected) {
          for (const [entityId, visual] of projected) mediaById.set(entityId, visual);
        }
      } catch (error) { if (error?.name === "AbortError") throw error; }
    }
    const items = records.map((record, index) => {
      const selectedMedia = mediaById.get(record.id)?.visual ?? null;
      const selectedMap = selectedMedia?.map ?? null;
      const { map: _, ...entityMedia } = selectedMedia ?? {};
      return {
        id: record.id,
        name: record.name,
        kind: record.kind,
        summary: record.summary,
        ...(record.unavailableFields ? { unavailableFields: record.unavailableFields } : {}),
        ...(record.parentId ? { containerId: record.parentId } : {}),
        ...(record.slot ? { containmentSlot: record.slot } : {}),
        ...(record.mapAnchor ? { mapAnchor: record.mapAnchor } : {}),
        ...(record.childScopeKnownEmpty === true ? { childScopeKnownEmpty: true } : {}),
        ...(index === 0 && record.kind === "world" ? { isWorldRoot: true } : {}),
        mapVisualState: selectedMap ? "ready" : mediaById.get(record.id)?.complete ? "absent" : "unavailable",
        ...(selectedMap ? { mapVisual: {
          imageUrl: selectedMap.imageUrl, alt: selectedMap.alt,
          width: selectedMap.width, height: selectedMap.height,
        } } : {}),
        ...(Object.keys(entityMedia).length > 0 ? { media: entityMedia } : {}),
      };
    });
    return {
      status: "ready",
      items,
      coverage,
      totalCount: result.data.locations.length,
      diagnosticId: worldLocationDiagnosticId(scopeId, "ready"),
      projection: {
        stateSpaceFingerprint: result.evidence.stateSpaceFingerprint,
        resolutionFingerprint: result.evidence.resolutionFingerprint,
        resultFingerprint: result.evidence.resultFingerprint,
        sourceRevisionFingerprint: result.evidence.sourceRevisionFingerprint,
      },
    };
  } catch (error) {
    if (error?.name === "AbortError") throw error;
    return { status: "error", items: [], diagnosticId: worldLocationDiagnosticId(scopeId, "transport") };
  }
}

/** Reads one source-bound page of an exact authorized containment scope. */
export async function readRegisteredWorldLocationScopePage({
  fetchImpl, origin, applicationId, stateSpaceId, scopeId, perspective, includeMedia = true,
  cursor = null, expectedSourceRevision = null,
}) {
  if (!(cursor === null || cursor === "100") ||
      !(expectedSourceRevision === null || /^[0-9A-F]{64}$/u.test(expectedSourceRevision))) {
    return { status: "error", items: [], diagnosticId: worldLocationDiagnosticId(scopeId, "continuation") };
  }
  const applicationRoot = `/api/applications/${encodeURIComponent(applicationId)}` +
    `/state-spaces/${encodeURIComponent(stateSpaceId)}`;
  const parameters = new URLSearchParams({
    perspective,
    input: JSON.stringify({ offset: cursor === null ? 0 : 100, expectedSourceRevision }),
  });
  const resource = `${applicationRoot}/entities/${encodeURIComponent(scopeId)}` +
    `/read-models/${encodeURIComponent(worldLocationScopePageContract.id)}?${parameters}`;
  try {
    const result = await readModelResponse({
      fetchImpl,
      resource: url(origin, resource),
      init: { headers: { Accept: "application/json" }, cache: "no-store" },
      applicationId,
      stateSpaceId,
      query: worldLocationScopePageContract,
      maximumBodyBytes: 270_000,
      maximumDataBytes: 262_144,
      statusPolicy: { ready: [200], forbidden: [403], stale: [409], unavailable: "remaining" },
      validate: (value) => validWorldLocationScopePage(value, scopeId, cursor === null ? 0 : 100),
      expectedSourceRevision,
    });
    if (result.status !== "ready") return {
      status: result.status === "forbidden" ? "forbidden" : result.status === "stale" ? "stale" : "error",
      items: [], diagnosticId: worldLocationDiagnosticId(scopeId, result.status),
    };
    if (result.data.state === "forbidden") return {
      status: "forbidden", items: [], diagnosticId: worldLocationDiagnosticId(scopeId, "audience"),
    };
    const { records, coverage } = projectWorldLocationRecords(result.data);
    const mediaById = new Map();
    const ownerMedia = includeMedia
      ? projectReadModelOwnerMedia(result.media, scopeId)
      : null;
    if (ownerMedia) mediaById.set(scopeId, ownerMedia);
    const pendingMediaIds = records
      .map((record) => record.id)
      .filter((recordId) => recordId !== scopeId || ownerMedia?.complete !== true);
    if (includeMedia && pendingMediaIds.length > 0) {
      try {
        const response = await fetchImpl(url(origin, `${applicationRoot}/media-batch`), {
          method: "POST",
          headers: { "Content-Type": "application/json", Accept: "application/json" },
          cache: "no-store",
          body: JSON.stringify({ entityIds: pendingMediaIds, perspective }),
        });
        const decoded = response?.ok ? await readBoundedJson(response, 4 * 1024 * 1024) : null;
        const media = decoded?.status === "ready" ? decoded.value : null;
        const projected = projectLocationMediaBatch(
          media, applicationId, stateSpaceId, pendingMediaIds,
        );
        if (projected) {
          for (const [entityId, visual] of projected) mediaById.set(entityId, visual);
        }
      } catch (error) { if (error?.name === "AbortError") throw error; }
    }
    const items = records.map((record, index) => {
      const selectedMedia = mediaById.get(record.id)?.visual ?? null;
      const selectedMap = selectedMedia?.map ?? null;
      const { map: _, ...entityMedia } = selectedMedia ?? {};
      return {
        id: record.id, name: record.name, kind: record.kind, summary: record.summary,
        ...(record.unavailableFields ? { unavailableFields: record.unavailableFields } : {}),
        ...(record.parentId ? { containerId: record.parentId } : {}),
        ...(record.slot ? { containmentSlot: record.slot } : {}),
        ...(record.mapAnchor ? { mapAnchor: record.mapAnchor } : {}),
        ...(record.childScopeKnownEmpty === true ? { childScopeKnownEmpty: true } : {}),
        ...(index === 0 && record.kind === "world" ? { isWorldRoot: true } : {}),
        mapVisualState: selectedMap ? "ready" : mediaById.get(record.id)?.complete ? "absent" : "unavailable",
        ...(selectedMap ? { mapVisual: {
          imageUrl: selectedMap.imageUrl, alt: selectedMap.alt,
          width: selectedMap.width, height: selectedMap.height,
        } } : {}),
        ...(Object.keys(entityMedia).length > 0 ? { media: entityMedia } : {}),
      };
    });
    return {
      status: "ready", scope: items[0], items: items.slice(1),
      totalCount: result.data.totalCount, complete: result.data.complete,
      coverage,
      nextCursor: result.data.nextCursor,
      diagnosticId: worldLocationDiagnosticId(scopeId, "ready"),
      projection: {
        stateSpaceFingerprint: result.evidence.stateSpaceFingerprint,
        resolutionFingerprint: result.evidence.resolutionFingerprint,
        resultFingerprint: result.evidence.resultFingerprint,
        sourceRevisionFingerprint: result.evidence.sourceRevisionFingerprint,
      },
    };
  } catch (error) {
    if (error?.name === "AbortError") throw error;
    return { status: "error", items: [], diagnosticId: worldLocationDiagnosticId(scopeId, "transport") };
  }
}

async function readCompatibleWorldLocationScopePage(options) {
  const paged = await readRegisteredWorldLocationScopePage(options);
  if (paged.status !== "error" || options.cursor != null) return paged;
  const legacy = await readRegisteredWorldLocationScope(options);
  if (legacy.status !== "ready") return legacy;
  return {
    ...legacy,
    scope: legacy.items[0],
    items: legacy.items.slice(1),
    totalCount: legacy.totalCount,
    complete: true,
    nextCursor: null,
  };
}

/** @param {{fetchImpl?: typeof fetch, origin: string, source: any, scopeId: string, cursor?: string | null}} options */
export async function readWorldLocationScopePatch({ fetchImpl = fetch, origin, source, scopeId, cursor = null }) {
  const exactScopeId = token(scopeId);
  const rootId = token(source?.contextSelection?.selectedWorldId);
  const known = Array.isArray(source?.locationDirectory) &&
    source.locationDirectory.some((entry) => entry.id === exactScopeId);
  if (!exactScopeId || (!known && exactScopeId !== rootId))
    throw new Error("That world location scope is not in the authorized map hierarchy.");
  const preview = source.audience.seat === "dm" && source.audience.perspective === "player";
  const previousScope = (source.locationScopes ?? []).find((entry) => entry.id === exactScopeId) ?? null;
  if (cursor !== null && (!previousScope || previousScope.nextCursor !== cursor ||
      !previousScope.sourceRevisionFingerprint))
    throw new Error("The world location continuation is stale. Refresh this location level.");
  const result = await readCompatibleWorldLocationScopePage({
    fetchImpl, origin, applicationId: source.applicationId, stateSpaceId: source.stateSpaceId,
    scopeId: exactScopeId, perspective: source.audience.perspective ?? "player", includeMedia: !preview,
    cursor, expectedSourceRevision: cursor === null ? null : previousScope.sourceRevisionFingerprint,
  });
  if (result.status === "forbidden") throw new Error("That world location scope is unavailable to this audience.");
  if (result.status === "stale") throw new Error("The world location level changed. Refresh it before continuing.");
  if (result.status !== "ready") throw new Error("The world location scope could not be read.");
  const merged = new Map((source.locationDirectory ?? []).map((entry) => [entry.id, entry]));
  merged.set(result.scope.id, result.scope);
  for (const entry of result.items) merged.set(entry.id, entry);
  const childIds = cursor === null
    ? result.items.map((entry) => entry.id)
    : [...previousScope.childIds, ...result.items.map((entry) => entry.id)];
  if (new Set(childIds).size !== childIds.length || childIds.length > result.totalCount)
    throw new Error("The world location continuation returned ambiguous membership.");
  const scopes = new Map((source.locationScopes ?? []).map((entry) => [entry.id, entry]));
  if (cursor === null) {
    const admittedHere = new Set(childIds);
    for (const [id, scope] of scopes) {
      if (id === exactScopeId || !scope.childIds.some((childId) => admittedHere.has(childId))) continue;
      const retained = scope.childIds.filter((childId) => !admittedHere.has(childId));
      scopes.set(id, {
        ...scope,
        childIds: retained,
        totalCount: Math.max(retained.length, scope.totalCount - (scope.childIds.length - retained.length)),
      });
    }
  }
  scopes.set(exactScopeId, {
    id: result.scope.id,
    name: result.scope.name,
    parentId: result.scope.containerId ?? null,
    childIds,
    totalCount: result.totalCount,
    complete: result.complete,
    ...(result.coverage === "partial" || (cursor !== null && previousScope.coverage === "partial")
      ? { coverage: "partial" } : {}),
    nextCursor: result.nextCursor,
    sourceRevisionFingerprint: result.projection?.sourceRevisionFingerprint ?? null,
  });
  const reachable = new Set();
  const rootScopeId = rootId;
  const visit = (id) => {
    if (reachable.has(id)) return;
    reachable.add(id);
    const member = scopes.get(id);
    for (const childId of member?.childIds ?? []) visit(childId);
  };
  visit(rootScopeId);
  for (const id of [...scopes.keys()]) if (!reachable.has(id)) scopes.delete(id);
  for (const id of [...merged.keys()]) if (!reachable.has(id)) merged.delete(id);
  return {
    locationDirectory: [...merged.values()].sort((left, right) =>
      left.name.localeCompare(right.name) || left.id.localeCompare(right.id)),
    locationScopes: [...scopes.values()],
    locationDirectoryAudience: source.audience.perspective ?? "player",
  };
}

function projectCurrentAffordances(value) {
  if (!Array.isArray(value) || value.length > 24) return { items: [], partial: true };
  const counts = new Map();
  for (const item of value) {
    const key = token(ownValue(item, "key"));
    if (key) counts.set(key, (counts.get(key) ?? 0) + 1);
  }
  let partial = false;
  const items = [];
  for (const item of value) {
    const key = token(ownValue(item, "key"));
    const label = text(ownValue(item, "label"), 500);
    const summary = text(ownValue(item, "summary"), 2_000);
    if (!key || (counts.get(key) ?? 0) !== 1 || !label) {
      partial = true;
      continue;
    }
    if (!summary) partial = true;
    items.push({ key, label, ...(summary ? { summary } : {}) });
  }
  return { items, partial };
}

function projectCurrentSceneProjection(value) {
  if (!value || typeof value !== "object" || Array.isArray(value)) return null;
  const kind = ownValue(value, "kind");
  const locationValue = ownValue(value, "location");
  const locationId = token(ownValue(locationValue, "id"));
  const locationKind = ownValue(locationValue, "kind");
  const visibility = ownValue(locationValue, "visibility");
  if (!["exploration", "conversation", "combat"].includes(kind) || !locationId || !token(locationKind) ||
      !["public", "party"].includes(visibility) ||
      !hasOwnValue(value, "conversationId") || !hasOwnValue(value, "encounterId") ||
      !(ownValue(value, "conversationId") === null || token(ownValue(value, "conversationId"))) ||
      !(ownValue(value, "encounterId") === null || token(ownValue(value, "encounterId")))) return null;
  const expectedKind = ownValue(value, "encounterId") ? "combat"
    : ownValue(value, "conversationId") ? "conversation" : "exploration";
  if (kind !== expectedKind) return null;
  const unavailableFields = [];
  const location = { id: locationId, kind: token(locationKind), visibility };
  const summary = text(ownValue(locationValue, "summary"), 2_000);
  if (summary) location.summary = summary;
  else unavailableFields.push("location.summary");
  const affordances = projectCurrentAffordances(ownValue(value, "affordances"));
  if (affordances.partial || !hasOwnValue(value, "affordances")) unavailableFields.push("affordances");
  return {
    kind, location, conversationId: ownValue(value, "conversationId"), encounterId: ownValue(value, "encounterId"),
    affordances: affordances.items,
    ...(unavailableFields.length ? { coverage: "partial", unavailableFields: [...new Set(unavailableFields)] } : {}),
  };
}

function validResumeNamed(value, extraKeys, validateExtra) {
  return hasExactKeys(value, ["id", "name", ...extraKeys]) && token(value.id) && text(value.name, 500) &&
    validateExtra(value);
}

function projectCampaignResumeProjection(value, campaignId) {
  if (!value || typeof value !== "object" || Array.isArray(value)) return null;
  const campaignValue = ownValue(value, "campaign");
  if (!campaignValue || typeof campaignValue !== "object" || Array.isArray(campaignValue) ||
      token(ownValue(campaignValue, "id")) !== campaignId || !hasOwnValue(value, "scene")) return null;
  const sceneValue = ownValue(value, "scene");
  let scene = null;
  if (sceneValue !== null) {
    const locationId = token(ownValue(sceneValue, "locationId"));
    const conversationId = ownValue(sceneValue, "conversationId");
    const encounterId = ownValue(sceneValue, "encounterId");
    if (!locationId || !hasOwnValue(sceneValue, "conversationId") || !hasOwnValue(sceneValue, "encounterId") ||
        !(conversationId === null || token(conversationId)) || !(encounterId === null || token(encounterId))) return null;
    scene = { locationId, conversationId, encounterId };
  }
  const unavailableFields = [];
  const campaign = { id: campaignId };
  for (const [field, maximum] of [["name", 500], ["title", 500], ["premise", 2_000]]) {
    const fieldValue = text(ownValue(campaignValue, field), maximum);
    if (fieldValue) campaign[field] = fieldValue;
    else unavailableFields.push(`campaign.${field}`);
  }
  for (const [field, maximum] of [["partyGoals", 3], ["toneAndBoundaries", 8]]) {
    const fieldValue = ownValue(campaignValue, field);
    if (Array.isArray(fieldValue) && fieldValue.length <= maximum && fieldValue.every((item) => text(item, 2_000)))
      campaign[field] = fieldValue;
    else unavailableFields.push(`campaign.${field}`);
  }
  const partyValue = ownValue(value, "party");
  const party = {};
  if (boundedInteger(ownValue(partyValue, "activeMemberCount"), 0, 1_000_000)) party.activeMemberCount = ownValue(partyValue, "activeMemberCount");
  else unavailableFields.push("party.activeMemberCount");
  const affordances = projectCurrentAffordances(ownValue(value, "affordances"));
  if (affordances.partial || !hasOwnValue(value, "affordances")) unavailableFields.push("affordances");
  return {
    campaign, party, scene, affordances: affordances.items,
    ...(unavailableFields.length ? { coverage: "partial", unavailableFields: [...new Set(unavailableFields)] } : {}),
  };
}

function currentProjectionEvidence(result) {
  return {
    qualifiedQueryId: result.evidence.qualifiedQueryId,
    outputSchemaHash: result.evidence.outputSchemaHash,
    stateSpaceFingerprint: result.evidence.stateSpaceFingerprint,
    resolutionFingerprint: result.evidence.resolutionFingerprint,
    resultFingerprint: result.evidence.resultFingerprint,
    sourceRevisionFingerprint: result.evidence.sourceRevisionFingerprint,
  };
}

/** Cross-checks Campaign Resume and Current Scene before any scene-specific resource is loaded. */
export async function readRegisteredCurrentPlay({
  fetchImpl = fetch, origin, applicationId, stateSpaceId, campaignId, perspective,
}) {
  const root = `/api/applications/${encodeURIComponent(applicationId)}` +
    `/state-spaces/${encodeURIComponent(stateSpaceId)}/entities/${encodeURIComponent(campaignId)}/read-models/`;
  const parameters = new URLSearchParams({ perspective });
  try {
    const resume = await readModelResponse({
      fetchImpl,
      resource: url(origin, `${root}${encodeURIComponent(campaignResumeContract.id)}?${parameters}`),
      init: { headers: { Accept: "application/json" }, cache: "no-store" },
      applicationId, stateSpaceId, query: campaignResumeContract,
      maximumBodyBytes: 270_000, maximumDataBytes: 262_144,
      statusPolicy: { ready: [200], forbidden: [403], stale: [409], unavailable: "remaining" },
      consume: (value) => projectCampaignResumeProjection(value, campaignId),
    });
    if (resume.status !== "ready") return { status: resume.status === "forbidden" ? "forbidden" : "error" };
    if (resume.data.scene === null) return {
      status: "empty", resume: resume.data, projection: { resume: currentProjectionEvidence(resume) },
    };
    const scene = await readModelResponse({
      fetchImpl,
      resource: url(origin, `${root}${encodeURIComponent(currentSceneContract.id)}?${parameters}`),
      init: { headers: { Accept: "application/json" }, cache: "no-store" },
      applicationId, stateSpaceId, query: currentSceneContract,
      maximumBodyBytes: 270_000, maximumDataBytes: 262_144,
      statusPolicy: { ready: [200], forbidden: [403], stale: [409], unavailable: "remaining" },
      consume: projectCurrentSceneProjection,
    });
    if (scene.status !== "ready") return { status: scene.status === "forbidden" ? "forbidden" : "error" };
    if (resume.evidence.stateSpaceFingerprint !== scene.evidence.stateSpaceFingerprint ||
        resume.evidence.resolutionFingerprint !== scene.evidence.resolutionFingerprint) return { status: "stale" };
    const resumeScene = resume.data.scene;
    if (scene.data.location.id !== resumeScene.locationId ||
        scene.data.conversationId !== resumeScene.conversationId ||
        scene.data.encounterId !== resumeScene.encounterId ||
        !scene.data.unavailableFields?.includes("affordances") &&
        !resume.data.unavailableFields?.includes("affordances") &&
        JSON.stringify(scene.data.affordances) !== JSON.stringify(resume.data.affordances)) return { status: "stale" };
    const unavailableFields = [...new Set([
      ...(resume.data.unavailableFields ?? []), ...(scene.data.unavailableFields ?? []),
    ])];
    return {
      status: "ready", resume: resume.data,
      scene: unavailableFields.length ? { ...scene.data, coverage: "partial", unavailableFields } : scene.data,
      projection: { resume: currentProjectionEvidence(resume), scene: currentProjectionEvidence(scene) },
    };
  } catch (error) {
    if (error?.name === "AbortError") throw error;
    return { status: "error" };
  }
}

function validWorldPeopleHoldings(value, worldId) {
  if (!["ready", "forbidden"].includes(ownValue(value, "state")) ||
      !Array.isArray(value.locations) || !Array.isArray(value.people) || !Array.isArray(value.holdings) ||
      value.locations.length + value.people.length + value.holdings.length > 200 ||
      ownValue(value.limits, "contentsDepth") !== 4 || value.limits.recordCount !== 200 ||
      value.limits.complete !== true) return false;
  if (value.state === "forbidden") return value.world === null && value.locations.length === 0 &&
    value.people.length === 0 && value.holdings.length === 0;
  return token(ownValue(value.world, "id")) === worldId;
}

function projectWorldPeopleDisplay(value) {
  const counts = new Map();
  const all = [...value.locations, ...value.people, ...value.holdings];
  for (const row of all) {
    const id = token(ownValue(row, "id"));
    if (id) counts.set(id, (counts.get(id) ?? 0) + 1);
  }
  const worldName = text(ownValue(value.world, "name"), 400);
  let partial = worldName === null;
  const unique = (row) => {
    const id = token(ownValue(row, "id"));
    return id && !["__proto__", "prototype", "constructor"].includes(id) &&
      id !== value.world.id && counts.get(id) === 1;
  };
  const candidates = new Map(value.locations.filter(unique).map((row) => [row.id, row]));
  const rooted = new Set([value.world.id]);
  const reachable = (id, path = new Set()) => {
    if (rooted.has(id)) return true;
    if (path.has(id) || path.size >= 16 || !candidates.has(id)) return false;
    path.add(id);
    if (!reachable(ownValue(candidates.get(id), "parentId"), path)) return false;
    rooted.add(id);
    return true;
  };
  const fields = (row) => {
    const unavailableFields = [];
    const name = text(ownValue(row, "name"), 400);
    const kind = text(ownValue(row, "kind"), 200);
    if (!name) unavailableFields.push("name");
    if (!kind) unavailableFields.push("kind");
    return { id: row.id, name: name ?? "Name unavailable", kind: kind ?? "Unknown", unavailableFields };
  };
  const finish = (row) => {
    partial ||= row.unavailableFields.length > 0;
    if (row.unavailableFields.length === 0) delete row.unavailableFields;
    return row;
  };
  const locations = value.locations.flatMap((row) => {
    if (!unique(row) || !reachable(row.id)) { partial = true; return []; }
    const projected = fields(row);
    const summary = text(ownValue(row, "summary"), 1_000);
    if (hasOwnValue(row, "summary") && !summary) projected.unavailableFields.push("summary");
    return [finish({ ...projected, parentId: row.parentId, ...(summary ? { summary } : {}) })];
  });
  const locationIds = new Set(locations.map((row) => row.id));
  const projectMembers = (rows, withMotive) => rows.flatMap((row) => {
    if (!unique(row) || !locationIds.has(ownValue(row, "locationId"))) { partial = true; return []; }
    const projected = { ...fields(row), locationId: row.locationId };
    if (withMotive && ownValue(row, "motive") !== null) {
      const summary = text(ownValue(row.motive, "summary"), 1_000);
      const status = text(ownValue(row.motive, "status"), 200);
      const visibility = text(ownValue(row.motive, "visibility"), 200);
      if (!summary || !status || !visibility) projected.unavailableFields.push("motive");
      if (summary) projected.motive = { summary, status: status ?? "unavailable", visibility: visibility ?? "unavailable" };
    }
    return [finish(projected)];
  });
  const people = projectMembers(value.people, true);
  const holdings = projectMembers(value.holdings, false);
  return { world: { id: value.world.id, name: worldName ?? "Name unavailable" },
    locations, people, holdings, coverage: partial ? "partial" : "complete" };
}

/** Reads the bounded DM directory without raw entity, containment, component, or per-person media reads. */
export async function readRegisteredWorldPeopleHoldings({
  fetchImpl = fetch, origin, applicationId, stateSpaceId, worldId,
}) {
  const applicationRoot = `/api/applications/${encodeURIComponent(applicationId)}` +
    `/state-spaces/${encodeURIComponent(stateSpaceId)}`;
  try {
    const result = await readModelResponse({
      fetchImpl,
      resource: url(origin, `${applicationRoot}/entities/${encodeURIComponent(worldId)}` +
        `/read-models/${encodeURIComponent(worldPeopleHoldingsContract.id)}?perspective=dm`),
      init: { headers: { Accept: "application/json" }, cache: "no-store" },
      applicationId,
      stateSpaceId,
      query: worldPeopleHoldingsContract,
      maximumBodyBytes: 270_000,
      maximumDataBytes: 262_144,
      statusPolicy: { ready: [200], forbidden: [403], stale: [409], unavailable: "remaining" },
      validate: (value) => validWorldPeopleHoldings(value, worldId),
    });
    if (result.status !== "ready" || result.data.state !== "ready") return {
      status: result.status === "forbidden" || result.data?.state === "forbidden" ? "forbidden" : "error",
      locations: [], people: [], holdings: [],
    };
    const display = projectWorldPeopleDisplay(result.data);
    const media = await readAuthorizedMediaBatch({
      fetchImpl, origin, applicationId, stateSpaceId,
      entityIds: display.people.map((person) => person.id), perspective: "dm",
    });
    return {
      status: "ready",
      coverage: display.coverage,
      totalCount: result.data.people.length + result.data.holdings.length,
      locations: display.locations.map((location) => ({
        id: location.id, name: location.name, kind: location.kind, summary: location.summary,
        containerId: location.parentId,
        ...(location.unavailableFields ? { unavailableFields: location.unavailableFields } : {}),
      })),
      people: display.people.map((person) => {
        const { motive, ...identity } = person;
        return {
          ...identity,
          ...(motive ? { motive } : {}),
          ...(media.has(person.id) ? { media: media.get(person.id) } : {}),
        };
      }),
      holdings: display.holdings,
      projection: {
        stateSpaceFingerprint: result.evidence.stateSpaceFingerprint,
        resolutionFingerprint: result.evidence.resolutionFingerprint,
        resultFingerprint: result.evidence.resultFingerprint,
        sourceRevisionFingerprint: result.evidence.sourceRevisionFingerprint,
      },
    };
  } catch (error) {
    if (error?.name === "AbortError") throw error;
    return { status: "error", locations: [], people: [], holdings: [] };
  }
}

function validWorldPeopleHoldingsPage(value, worldId, offset) {
  if (!["ready", "forbidden"].includes(ownValue(value, "state")) ||
      !Array.isArray(value.locations) || value.locations.length > 800 ||
      !Array.isArray(value.people) || !Array.isArray(value.holdings) ||
      value.people.length + value.holdings.length > 50 ||
      !Number.isInteger(value.totalCount) || value.totalCount < 0 || value.totalCount > 2_000 ||
      typeof value.complete !== "boolean" ||
      !(value.nextCursor === null || /^(?:[1-9][0-9]{1,3})$/u.test(value.nextCursor)) ||
      ownValue(value.limits, "contentsDepth") !== 16 || value.limits.recordCount !== 2_000 ||
      value.limits.pageSize !== 50 || typeof value.limits.hierarchyComplete !== "boolean") return false;
  if (value.state === "forbidden") return value.world === null && value.locations.length === 0 &&
    value.people.length === 0 && value.holdings.length === 0 && value.totalCount === 0 &&
    value.complete === true && value.nextCursor === null;
  if (token(ownValue(value.world, "id")) !== worldId) return false;
  const pageCount = value.people.length + value.holdings.length;
  if (value.totalCount < offset + pageCount || value.complete !== (value.nextCursor === null) ||
      (value.complete && offset + pageCount !== value.totalCount) ||
      (!value.complete && (pageCount !== 50 || value.nextCursor !== String(offset + 50)))) return false;
  return true;
}

/** Reads one source-bound DM page of classified people and holdings. */
export async function readRegisteredWorldPeopleHoldingsPage({
  fetchImpl = fetch, origin, applicationId, stateSpaceId, worldId,
  cursor = null, expectedSourceRevision = null,
}) {
  const offset = cursor === null ? 0 : Number(cursor);
  if (!Number.isInteger(offset) || offset < 0 || offset > 1_950 || offset % 50 !== 0 ||
      !(expectedSourceRevision === null || /^[0-9A-F]{64}$/u.test(expectedSourceRevision))) {
    return { status: "error", locations: [], people: [], holdings: [] };
  }
  const applicationRoot = `/api/applications/${encodeURIComponent(applicationId)}` +
    `/state-spaces/${encodeURIComponent(stateSpaceId)}`;
  const parameters = new URLSearchParams({
    perspective: "dm",
    input: JSON.stringify({ offset, expectedSourceRevision }),
  });
  try {
    const result = await readModelResponse({
      fetchImpl,
      resource: url(origin, `${applicationRoot}/entities/${encodeURIComponent(worldId)}` +
        `/read-models/${encodeURIComponent(worldPeopleHoldingsPageContract.id)}?${parameters}`),
      init: { headers: { Accept: "application/json" }, cache: "no-store" },
      applicationId,
      stateSpaceId,
      query: worldPeopleHoldingsPageContract,
      maximumBodyBytes: 1_200_000,
      maximumDataBytes: 1_100_000,
      statusPolicy: { ready: [200], forbidden: [403], stale: [409], unavailable: "remaining" },
      expectedSourceRevision,
      validate: (value) => validWorldPeopleHoldingsPage(value, worldId, offset),
    });
    if (result.status !== "ready" || result.data.state !== "ready") return {
      status: result.status === "forbidden" || result.data?.state === "forbidden" ? "forbidden"
        : result.status === "stale" ? "stale" : result.status === "unavailable" ? "unavailable" : "error",
      locations: [], people: [], holdings: [],
    };
    const display = projectWorldPeopleDisplay(result.data);
    const media = await readAuthorizedMediaBatch({
      fetchImpl, origin, applicationId, stateSpaceId,
      entityIds: display.people.map((person) => person.id), perspective: "dm",
    });
    return {
      status: "ready",
      world: display.world,
      coverage: display.coverage,
      locations: display.locations.map((location) => ({
        id: location.id, name: location.name, kind: location.kind,
        summary: location.summary,
        containerId: location.parentId,
        ...(location.unavailableFields ? { unavailableFields: location.unavailableFields } : {}),
      })),
      people: display.people.map((person) => {
        const { motive, ...identity } = person;
        return {
          ...identity,
          ...(motive ? { motive } : {}),
          ...(media.has(person.id) ? { media: media.get(person.id) } : {}),
        };
      }),
      holdings: display.holdings,
      totalCount: result.data.totalCount,
      // Paging counts describe the received source rows, including rows that cannot safely
      // enter the display index. Losing one row must not turn a complete read into a failure.
      pageRecordCount: result.data.people.length + result.data.holdings.length,
      sourceRecordIds: [...result.data.people, ...result.data.holdings]
        .map((row) => token(ownValue(row, "id"))).filter(Boolean),
      complete: result.data.complete,
      nextCursor: result.data.nextCursor,
      hierarchyComplete: result.data.limits.hierarchyComplete,
      sourceRevisionFingerprint: result.evidence.sourceRevisionFingerprint,
      projection: currentProjectionEvidence(result),
    };
  } catch (error) {
    if (error?.name === "AbortError") throw error;
    return { status: "error", locations: [], people: [], holdings: [] };
  }
}

/** Walks only already-authorized child scopes to build the complete visible location hierarchy. */
export async function readWorldLocationDirectory({ fetchImpl = fetch, origin, source }) {
  const perspective = source.audience.perspective ?? "player";
  const worldId = token(source.contextSelection?.selectedWorldId);
  if (!worldId || !["dm", "player"].includes(perspective))
    throw new Error("The selected World identity is unavailable.");
  const locations = new Map((source.locationDirectory ?? []).map((location) => [location.id, location]));
  const scopes = new Map((source.locationScopes ?? []).map((scope) => [scope.id, {
    ...scope, childIds: [...scope.childIds],
  }]));
  const queued = new Set([worldId]);
  const queue = [{ id: worldId, depth: 0 }];
  const visited = new Set();
  const cleanDirectoryEntry = (entry) => {
    const { mapVisualState: _mapVisualState, mapVisual: _mapVisual, media: _media, ...clean } = entry;
    return clean;
  };
  const mergeLocation = (entry) => {
    const clean = cleanDirectoryEntry(entry);
    const current = locations.get(clean.id);
    if (current?.containerId && clean.containerId && current.containerId !== clean.containerId)
      throw new Error("The world location hierarchy returned conflicting containment.");
    locations.set(clean.id, current ? { ...clean, ...current } : clean);
  };

  const loadScope = async (next) => {
    let scope = scopes.get(next.id) ?? null;
    if (!scope?.complete) {
      let cursor = scope?.nextCursor ?? null;
      let expectedSourceRevision = scope?.sourceRevisionFingerprint ?? null;
      const children = cursor === null ? [] : [...scope.childIds];
      let owner = locations.get(next.id) ?? null;
      do {
        const page = await readRegisteredWorldLocationScopePage({
          fetchImpl, origin, applicationId: source.applicationId, stateSpaceId: source.stateSpaceId,
          scopeId: next.id, perspective, includeMedia: false, cursor, expectedSourceRevision,
        });
        if (page.status !== "ready")
          throw new Error("The complete world location directory is unavailable.");
        owner = owner ?? page.scope;
        mergeLocation(page.scope);
        for (const location of page.items) {
          mergeLocation(location);
          children.push(location.id);
          if (location.childScopeKnownEmpty === true) {
            scopes.set(location.id, {
              id: location.id, name: location.name, parentId: location.containerId ?? next.id,
              childIds: [], totalCount: 0, complete: true, nextCursor: null,
              sourceRevisionFingerprint: null,
            });
          }
        }
        if (new Set(children).size !== children.length || children.length > page.totalCount)
          throw new Error("The world location hierarchy returned ambiguous membership.");
        expectedSourceRevision = page.projection?.sourceRevisionFingerprint ?? expectedSourceRevision;
        cursor = page.nextCursor;
        scope = {
          id: page.scope.id, name: page.scope.name, parentId: page.scope.containerId ?? null,
          childIds: [...children], totalCount: page.totalCount, complete: page.complete,
          nextCursor: page.nextCursor, sourceRevisionFingerprint: expectedSourceRevision,
        };
      } while (cursor !== null);
      if (!owner || !scope || !scope.complete || scope.childIds.length !== scope.totalCount)
        throw new Error("The complete world location directory is unavailable.");
      scopes.set(next.id, scope);
    }
    return { next, scope };
  };

  while (queue.length > 0) {
    const batch = queue.splice(0, 8).filter((next) => !visited.has(next.id));
    if (batch.some((next) => next.depth > 16) || visited.size + batch.length > 2_000)
      throw new Error("The world location hierarchy exceeds its supported bounds.");
    const loaded = await Promise.all(batch.map(loadScope));
    for (const { next, scope } of loaded) {
      visited.add(next.id);
      for (const childId of scope.childIds) {
        if (visited.has(childId) || queued.has(childId)) continue;
        queued.add(childId);
        queue.push({ id: childId, depth: next.depth + 1 });
      }
    }
  }
  for (const id of [...locations.keys()]) if (!visited.has(id)) locations.delete(id);
  for (const id of [...scopes.keys()]) if (!visited.has(id)) scopes.delete(id);
  return {
    locationDirectory: [...locations.values()].sort((left, right) =>
      left.name.localeCompare(right.name) || left.id.localeCompare(right.id)),
    locationScopes: [...scopes.values()],
    locationDirectoryAudience: perspective,
    locationDirectoryComplete: true,
  };
}

/** Reads the calculated v2 sheet and its optional authorized portrait, without dossier fan-out. */
export async function readCanonicalCharacterSheet({
  fetchImpl, origin, applicationId, stateSpaceId, actorId, perspective,
}) {
  const entityRoot = `/api/applications/${encodeURIComponent(applicationId)}` +
    `/state-spaces/${encodeURIComponent(stateSpaceId)}/entities`;
  // Portrait authorization is independent of sheet shape. Start it in the same scoped request;
  // player previews deliberately never use the ambient media grant.
  const portraitRead = startCharacterPortraitRead({
    fetchImpl, origin, applicationId, stateSpaceId, actorId, perspective,
  });
  try {
    const result = await readModelResponse({
      fetchImpl,
      resource: url(origin, `${entityRoot}/${encodeURIComponent(actorId)}` +
        `/read-models/${encodeURIComponent(characterSheetContract.id)}` +
        (perspective ? `?perspective=${encodeURIComponent(perspective)}` : "")),
      init: { headers: { Accept: "application/json" }, cache: "no-store" },
      applicationId,
      stateSpaceId,
      query: characterSheetContract,
      maximumBodyBytes: 1_060_000,
      maximumDataBytes: 1_048_576,
      statusPolicy: { ready: [200], forbidden: [403], stale: [409], unavailable: "remaining" },
      consume: (value) => projectCharacterSheetData(value, actorId),
    });
    if (result.status !== "ready") {
      const portraitOutcome = await settledPortraitRead(portraitRead);
      const portrait = portraitOutcome.status === "ready" ? portraitOutcome.media.get(actorId) ?? null
        : portraitOutcome.status === "forbidden" ? null : undefined;
      if (result.status === "incompatible") return {
        status: "error", data: null, failureCategory: "incompatible-data",
        diagnosticId: canonicalCharacterDiagnosticId(result.response, actorId, "incompatible-data"),
        ...(portrait === undefined ? {} : { media: portrait }),
      };
      const response = result.response;
      const failureBody = await readBoundedJson(response, 8_192);
      const failure = failureBody.status === "ready" ? failureBody.value : null;
      const errorCode = token(failure?.code);
      const category = canonicalCharacterFailureCategory(response, errorCode);
      return {
        status: category === "authorization" ? "forbidden" : "error",
        data: null,
        failureCategory: category,
        diagnosticId: canonicalCharacterDiagnosticId(response, actorId, category),
        ...(errorCode ? { errorCode } : {}),
        ...(Number.isInteger(response?.status) ? { httpStatus: response.status } : {}),
        ...(category === "authorization" ? { media: null } : portrait === undefined ? {} : { media: portrait }),
      };
    }
    const portraitOutcome = await settledPortraitRead(portraitRead);
    const media = portraitOutcome.status === "ready" ? portraitOutcome.media : null;
    return {
      status: "ready", failureCategory: null,
      media: perspective === "player" || portraitOutcome.status === "forbidden" ? null
        : media === null ? undefined : media.get(actorId) ?? null,
      diagnosticId: canonicalCharacterDiagnosticId(result.response, actorId, "ready"),
      data: {
        ...result.data,
        projection: {
          stateSpaceFingerprint: result.evidence.stateSpaceFingerprint,
          resolutionFingerprint: result.evidence.resolutionFingerprint,
          resultFingerprint: result.evidence.resultFingerprint,
          sourceRevisionFingerprint: result.evidence.sourceRevisionFingerprint,
        },
      },
    };
  } catch (error) {
    if (error?.name === "AbortError") throw error;
    const portraitOutcome = await settledPortraitRead(portraitRead);
    const portrait = portraitOutcome.status === "ready" ? portraitOutcome.media.get(actorId) ?? null
      : portraitOutcome.status === "forbidden" ? null : undefined;
    return {
      status: "error", data: null, failureCategory: "transport",
      diagnosticId: canonicalCharacterDiagnosticId(null, actorId, "transport"),
      ...(portrait === undefined ? {} : { media: portrait }),
    };
  }
}

async function inventoryReadFailure(result, id, label) {
  if (result.status === "incompatible") return {
    status: "error", data: null, failureCategory: "incompatible-data",
    diagnosticId: canonicalCharacterDiagnosticId(result.response, id, `${label}-incompatible`),
  };
  const response = result.response;
  const failureBody = await readBoundedJson(response, 8_192);
  const failure = failureBody.status === "ready" ? failureBody.value : null;
  const errorCode = token(failure?.code);
  const category = canonicalCharacterFailureCategory(response, errorCode);
  return {
    status: category === "authorization" ? "forbidden" : "error",
    data: null,
    failureCategory: category,
    diagnosticId: canonicalCharacterDiagnosticId(response, id, `${label}-${category}`),
    ...(errorCode ? { errorCode } : {}),
    ...(Number.isInteger(response?.status) ? { httpStatus: response.status } : {}),
  };
}

/** Reads one complete direct inventory scope; deeper scopes are separate cached resources. */
export async function readCanonicalInventoryPage({
  fetchImpl, origin, applicationId, stateSpaceId, scopeId, perspective,
}) {
  const entityRoot = `/api/applications/${encodeURIComponent(applicationId)}` +
    `/state-spaces/${encodeURIComponent(stateSpaceId)}/entities`;
  try {
    const result = await readModelResponse({
      fetchImpl,
      resource: url(origin, `${entityRoot}/${encodeURIComponent(scopeId)}` +
        `/read-models/${encodeURIComponent(inventoryContainerContract.id)}` +
        (perspective ? `?perspective=${encodeURIComponent(perspective)}` : "")),
      init: { headers: { Accept: "application/json" }, cache: "no-store" },
      applicationId,
      stateSpaceId,
      query: inventoryContainerContract,
      // The closed schema permits 200 rows with up to 32 named equipment slots each.
      // Keep the byte ceiling aligned with that valid worst case instead of silently
      // turning a contract-valid large container into an incompatible response.
      maximumBodyBytes: 5_300_000,
      maximumDataBytes: 5 * 1024 * 1024,
      statusPolicy: { ready: [200], forbidden: [403], stale: [409], unavailable: "remaining" },
      consume: (value) => projectInventoryContainer(value, scopeId),
    });
    if (result.status !== "ready") return inventoryReadFailure(result, scopeId, "inventory-container");
    return {
      status: "ready", failureCategory: null,
      diagnosticId: canonicalCharacterDiagnosticId(result.response, scopeId, "inventory-container-ready"),
      data: {
        ...result.data,
        projection: {
          stateSpaceFingerprint: result.evidence.stateSpaceFingerprint,
          resolutionFingerprint: result.evidence.resolutionFingerprint,
          resultFingerprint: result.evidence.resultFingerprint,
          sourceRevisionFingerprint: result.evidence.sourceRevisionFingerprint,
        },
      },
    };
  } catch (error) {
    if (error?.name === "AbortError") throw error;
    return {
      status: "error", data: null, failureCategory: "transport",
      diagnosticId: canonicalCharacterDiagnosticId(null, scopeId, "inventory-container-transport"),
    };
  }
}

async function readCanonicalInventoryWallet({
  fetchImpl, origin, applicationId, stateSpaceId, actorId, perspective,
}) {
  const resource = `/api/applications/${encodeURIComponent(applicationId)}` +
    `/state-spaces/${encodeURIComponent(stateSpaceId)}/entities/${encodeURIComponent(actorId)}` +
    `/read-models/${encodeURIComponent(inventoryWalletContract.id)}` +
    (perspective ? `?perspective=${encodeURIComponent(perspective)}` : "");
  try {
    const result = await readModelResponse({
      fetchImpl, resource: url(origin, resource), init: { headers: { Accept: "application/json" }, cache: "no-store" },
      applicationId, stateSpaceId, query: inventoryWalletContract,
      maximumBodyBytes: 80_000, maximumDataBytes: 65_536,
      statusPolicy: { ready: [200], forbidden: [403], stale: [409], unavailable: "remaining" },
      consume: (value) => projectInventoryWallet(value, actorId),
    });
    if (result.status === "ready") return { status: "ready", data: result.data };
    if (result.status === "forbidden") return { status: "forbidden", data: null };
    return { status: "unavailable", data: null };
  } catch (error) {
    if (error?.name === "AbortError") throw error;
    return { status: "unavailable", data: null };
  }
}

/** Reads the root container and wallet independently so a large wallet scan cannot hide inventory. */
export async function readCanonicalInventory(request) {
  const [page, walletResult] = await Promise.all([
    readCanonicalInventoryPage({ ...request, scopeId: request.actorId }),
    readCanonicalInventoryWallet(request),
  ]);
  const wallet = walletResult.status === "ready" ? walletResult.data : null;
  const walletState = wallet ? {
    status: wallet.complete ? "complete" : "partial",
    reason: wallet.complete ? null : wallet.notices.length ? "field-unavailable" : "depth-limit",
  } : walletResult.status === "forbidden"
    ? { status: "forbidden", reason: "authorization" }
    : { status: "unavailable", reason: "read-failed" };
  if (page.status !== "ready") return { ...page, wallet: wallet?.wallet ?? null, walletState };
  return {
    ...page,
    data: {
      ...page.data,
      items: page.data.items.map((item) => ({
        ...item,
        parentItemId: null,
        depth: 1,
        // An omitted container capability is unknown, never evidence of an empty non-container.
        childCount: item.isContainer === false ? 0 : null,
        deeperContentsOmitted: item.isContainer === true,
      })),
      wallet: wallet?.wallet ?? null,
      walletState,
    },
  };
}

export async function readCanonicalCharacter({ fetchImpl, origin, applicationId, stateSpaceId, actorId, perspective }) {
  const applicationRoot = `/api/applications/${encodeURIComponent(applicationId)}` +
    `/state-spaces/${encodeURIComponent(stateSpaceId)}`;
  const entityRoot = `${applicationRoot}/entities`;
  const headers = { Accept: "application/json" };
  const portraitRead = startCharacterPortraitRead({
    fetchImpl, origin, applicationId, stateSpaceId, actorId, perspective,
  });
  try {
    const result = await readModelResponse({
      fetchImpl,
      resource: url(origin, `${entityRoot}/${encodeURIComponent(actorId)}` +
      `/read-models/${encodeURIComponent(characterDossierContract.id)}` +
      (perspective ? `?perspective=${encodeURIComponent(perspective)}` : "")),
      init: { headers, cache: "no-store" },
      applicationId,
      stateSpaceId,
      query: characterDossierContract,
      maximumBodyBytes: 1_060_000,
      maximumDataBytes: 1_048_576,
      statusPolicy: { ready: [200], forbidden: [403], stale: [409], unavailable: "remaining" },
      consume: (value) => projectCharacterDossierData(value, actorId),
    });
    if (result.status !== "ready") {
      const portraitOutcome = await settledPortraitRead(portraitRead);
      const portrait = portraitOutcome.status === "ready" ? portraitOutcome.media.get(actorId) ?? null
        : portraitOutcome.status === "forbidden" ? null : undefined;
      if (result.status === "incompatible") return {
        status: "error",
        data: null,
        failureCategory: "incompatible-data",
        diagnosticId: canonicalCharacterDiagnosticId(result.response, actorId, "incompatible-data"),
        ...(portrait === undefined ? {} : { media: portrait }),
      };
      const response = result.response;
      const failureBody = await readBoundedJson(response, 8_192);
      const failure = failureBody.status === "ready" ? failureBody.value : null;
      const errorCode = token(failure?.code);
      const category = canonicalCharacterFailureCategory(response, errorCode);
      const forbidden = category === "authorization";
      return {
        status: forbidden ? "forbidden" : "error",
        data: null,
        failureCategory: category,
        diagnosticId: canonicalCharacterDiagnosticId(response, actorId, category),
        ...(errorCode ? { errorCode } : {}),
        ...(Number.isInteger(response?.status) ? { httpStatus: response.status } : {}),
        ...(forbidden ? { media: null } : portrait === undefined ? {} : { media: portrait }),
      };
    }
    const projected = result.data;
    const portraitOutcome = await settledPortraitRead(portraitRead);
    const mediaOwners = [...new Set((projected.inventory?.items ?? []).flatMap(item => [item.id, item.definition.id]))];
    const inventoryMedia = new Map();
    if (portraitOutcome.status === "ready") {
      const portrait = portraitOutcome.media.get(actorId);
      if (portrait) inventoryMedia.set(actorId, portrait);
    }
    if (perspective !== "player" && mediaOwners.length <= 257) {
      for (let offset = 0; offset < mediaOwners.length; offset += 256) {
        const batch = await readAuthorizedMediaBatch({
          fetchImpl, origin, applicationId, stateSpaceId,
          entityIds: mediaOwners.slice(offset, offset + 256), perspective,
        });
        for (const [id, media] of batch) inventoryMedia.set(id, media);
      }
    } else if (perspective !== "player") {
      const batch = await readAuthorizedMediaBatch({
        fetchImpl, origin, applicationId, stateSpaceId, entityIds: [actorId], perspective,
      });
      for (const [id, media] of batch) inventoryMedia.set(id, media);
    }
    const inventory = (projected.inventory?.items ?? []).map((item) => {
      // Preview data is filtered by the mechanic. Media still uses the ambient host grant,
      // so omit that optional enrichment when previewing a player's dossier as a GM.
      if (perspective === "player") return item;
      const itemId = token(item?.id);
      const definitionId = token(item?.definition?.id);
      if (!itemId || !definitionId) return null;
      const inheritedMedia = inheritMediaVisual(
        inventoryMedia.get(itemId), inventoryMedia.get(definitionId),
      );
      return inheritedMedia ? { ...item, media: inheritedMedia } : item;
    }).filter(Boolean);
    return {
      status: "ready",
      diagnosticId: canonicalCharacterDiagnosticId(result.response, actorId, "ready"),
      failureCategory: null,
      media: perspective === "player" || portraitOutcome.status === "forbidden" ? null
        : portraitOutcome.status === "ready" ? inventoryMedia.get(actorId) ?? null : undefined,
      data: {
      ...projected,
      ...(projected.inventory ? { inventory: { ...projected.inventory, items: inventory } } : {}),
      projection: {
        stateSpaceFingerprint: result.evidence.stateSpaceFingerprint,
        resolutionFingerprint: result.evidence.resolutionFingerprint,
        resultFingerprint: result.evidence.resultFingerprint,
        sourceRevisionFingerprint: result.evidence.sourceRevisionFingerprint,
      },
      },
    };
  } catch (error) {
    if (error?.name === "AbortError") throw error;
    const portraitOutcome = await settledPortraitRead(portraitRead);
    const portrait = portraitOutcome.status === "ready" ? portraitOutcome.media.get(actorId) ?? null
      : portraitOutcome.status === "forbidden" ? null : undefined;
    return {
      status: "error",
      data: null,
      failureCategory: "transport",
      diagnosticId: canonicalCharacterDiagnosticId(null, actorId, "transport"),
      ...(portrait === undefined ? {} : { media: portrait }),
    };
  }
}

async function readAuthorizedMediaBatchState({
  fetchImpl, origin, applicationId, stateSpaceId, entityIds, perspective, rejectForeignOwner = false,
}) {
  const ids = [...new Set(entityIds.map(token).filter(Boolean))];
  if (ids.length === 0) return { status: "ready", media: new Map() };
  if (ids.length > 256) return { status: "unavailable", media: new Map() };
  try {
    const applicationRoot = `/api/applications/${encodeURIComponent(applicationId)}` +
      `/state-spaces/${encodeURIComponent(stateSpaceId)}`;
    const response = await fetchImpl(url(origin, `${applicationRoot}/media-batch`), {
      method: "POST",
      headers: { "Content-Type": "application/json", Accept: "application/json" },
      cache: "no-store",
      body: JSON.stringify({ entityIds: ids, perspective }),
    });
    if (response?.status === 401 || response?.status === 403)
      return { status: "forbidden", media: new Map() };
    // Media discovery is display enrichment, but its body remains bounded independently of the
    // image blobs it describes. An oversized/malformed discovery response is unavailable, not
    // evidence that a previously confirmed portrait was removed.
    const decoded = response?.ok ? await readBoundedJson(response, 4 * 1024 * 1024) : null;
    const payload = decoded?.status === "ready" ? decoded.value : null;
    const allowed = new Set(ids);
    if (payload?.applicationId !== applicationId || payload.stateSpaceId !== stateSpaceId ||
        !Array.isArray(payload.items) || payload.items.length > ids.length ||
        new Set(payload.items.map((item) => item?.entityId)).size !== payload.items.length)
      return { status: "unavailable", media: new Map() };
    // A response naming an entity outside this authorized batch is a scope breach,
    // not a temporary malformed attachment. Clear prior media rather than retaining
    // a potentially cross-owner visual.
    if (!payload.items.every((item) => allowed.has(token(item?.entityId))))
      return { status: rejectForeignOwner ? "forbidden" : "unavailable", media: new Map() };
    const result = new Map();
    for (const item of payload.items) {
      if (!Array.isArray(item.attachments)) return { status: "unavailable", media: new Map() };
      const visual = projectMediaVisual(item);
      if (!visual && item.attachments.length !== 0) return { status: "unavailable", media: new Map() };
      if (visual) result.set(item.entityId, visual);
    }
    return { status: "ready", media: result };
  } catch (error) {
    if (error?.name === "AbortError") throw error;
    return { status: "unavailable", media: new Map() };
  }
}

async function readAuthorizedMediaBatch(request) {
  return (await readAuthorizedMediaBatchState(request)).media;
}

/** Starts an independent actor-media read without leaving an AbortError rejection unobserved. */
function startCharacterPortraitRead({
  fetchImpl, origin, applicationId, stateSpaceId, actorId, perspective,
}) {
  const request = perspective === "player"
    // This preview has no ambient media grant. Even a failed sheet must not retain a
    // portrait from the GM summary supplied to the projector.
    ? Promise.resolve({ status: "ready", media: new Map() })
    : readAuthorizedMediaBatchState({ fetchImpl, origin, applicationId, stateSpaceId, entityIds: [actorId], perspective });
  return request.catch((error) => error?.name === "AbortError"
    ? { status: "aborted", error }
    : { status: "unavailable", media: new Map() });
}

async function settledPortraitRead(read) {
  const outcome = await read;
  if (outcome.status === "aborted") throw outcome.error;
  return outcome;
}

async function attachAuthorizedKnowledgeMedia({
  fetchImpl,
  origin,
  applicationId,
  stateSpaceId,
  projectedKnowledge,
  perspective,
}) {
  if (projectedKnowledge.status !== "ready") return projectedKnowledge;
  const ownerIds = [...new Set([
    ...projectedKnowledge.entries,
    ...projectedKnowledge.locations.flatMap((location) => location.entries),
  ].map((entry) => token(entry.mediaOwnerId)).filter(Boolean))];
  const media = await readAuthorizedMediaBatch({
    fetchImpl, origin, applicationId, stateSpaceId, entityIds: ownerIds, perspective,
  });
  function enrich(entries) {
    return entries.map((entry) => {
      const { mediaOwnerId, ...projectedEntry } = entry;
      const ownerId = token(mediaOwnerId);
      if (!ownerId) return projectedEntry;
      const visual = media.get(ownerId);
      return visual ? { ...projectedEntry, media: visual } : projectedEntry;
    });
  }
  return {
    ...projectedKnowledge,
    entries: enrich(projectedKnowledge.entries),
    locations: projectedKnowledge.locations.map((location) => ({
      ...location,
      entries: enrich(location.entries),
    })),
  };
}

function chronology(value, expectedPerspective) {
  const sourceRevision = ownValue(value, "sourceRevision");
  const nextCursorValue = ownValue(value, "nextCursor");
  const nextCursor = nextCursorValue === undefined ? null : nextCursorValue;
  if (ownValue(value, "perspective") !== expectedPerspective ||
      (value.status !== "ready" && value.status !== "empty") ||
      !Array.isArray(value.entries) || value.entries.length > 40 ||
      (value.status === "empty") !== (value.entries.length === 0) ||
      !["complete", "partial"].includes(value.coverage) ||
      !["complete", "partial"].includes(value.fieldCoverage) ||
      typeof sourceRevision !== "string" || !/^[0-9A-F]{64}$/u.test(sourceRevision) ||
      (nextCursor !== null && (typeof nextCursor !== "string" || !/^[1-9][0-9]{0,2}$/.test(nextCursor) ||
        Number(nextCursor) > 500)) ||
      (nextCursor !== null && value.coverage !== "partial") ||
      (nextCursor === null && value.coverage !== value.fieldCoverage) ||
      (value.status === "empty" && nextCursor !== null)) {
    return { status: "unavailable", perspective: expectedPerspective, entries: [] };
  }
  const includeSubjects = expectedPerspective === "dm";
  const counts = new Map();
  for (const entry of value.entries) {
    const id = token(ownValue(entry, "id"));
    if (id) counts.set(id, (counts.get(id) ?? 0) + 1);
  }
  let partial = value.fieldCoverage === "partial";
  const entries = value.entries.flatMap((entry) => {
    const id = token(ownValue(entry, "id"));
    if (!id || counts.get(id) !== 1) { partial = true; return []; }
    const dateLabel = text(entry.dateLabel, 100);
    const precision = new Set(["exact", "approximate", "era"]).has(entry.precision)
      ? entry.precision
      : null;
    const title = text(entry.title, 160);
    const summary = text(entry.summary, 1_000);
    const occurredAtMinute = Number.isSafeInteger(entry.occurredAtMinute) &&
      entry.occurredAtMinute >= -1_000_000_000 && entry.occurredAtMinute <= 1_000_000_000
      ? entry.occurredAtMinute
      : null;
    const unavailableFields = [];
    for (const [field, available] of Object.entries({ dateLabel, precision, title, summary,
      occurredAtMinute: occurredAtMinute !== null })) if (!available) unavailableFields.push(field);
    const projected = {
      id, occurredAtMinute, dateLabel: dateLabel ?? "Date unavailable", precision: precision ?? "unavailable",
      title: title ?? "Title unavailable", summary: summary ?? "Description unavailable.",
    };
    if (!includeSubjects) {
      if (unavailableFields.length) partial = true;
      return [{ ...projected, ...(unavailableFields.length ? { unavailableFields } : {}) }];
    }
    const subjects = projectCharacterArray(entry.subjects, 10, (subject) => {
      const subjectId = token(ownValue(subject, "id"));
      const name = text(ownValue(subject, "name"), 400);
      return subjectId && name ? { id: subjectId, name } : null;
    }, (subject) => token(ownValue(subject, "id")));
    if (!subjects || subjects.length !== entry.subjects.length) unavailableFields.push("subjects");
    if (unavailableFields.length) partial = true;
    return [{ ...projected, subjects: subjects ?? [], ...(unavailableFields.length ? { unavailableFields } : {}) }];
  });
  return { status: value.status, perspective: expectedPerspective, entries,
    coverage: value.coverage === "partial" || partial ? "partial" : "complete",
    fieldCoverage: partial ? "partial" : "complete", sourceRevision, nextCursor,
    pageEntryCount: value.entries.length };
}

function relationshipTargetIds(value, expectedFromId, expectedKind) {
  if (!value || !Array.isArray(value.items)) return [];
  return value.items.flatMap((item) => {
    const fromEntityId = token(item?.fromEntityId);
    const toEntityId = token(item?.toEntityId);
    const qualifiedKind = token(item?.qualifiedKind);
    return fromEntityId === expectedFromId && qualifiedKind === expectedKind && toEntityId
      ? [toEntityId]
      : [];
  });
}

async function readNamedEntity(fetchImpl, origin, entityRoot, entityId) {
  try {
    const response = await fetchImpl(url(origin, `${entityRoot}/${encodeURIComponent(entityId)}`), {
      headers: { Accept: "application/json" }, cache: "no-store",
    });
    return response?.ok ? entity(await json(response), entityId) : null;
  } catch {
    return null;
  }
}

async function readExactComponent(fetchImpl, origin, entityRoot, entityId, componentTypeId) {
  try {
    const response = await fetchImpl(url(origin, `${entityRoot}/${encodeURIComponent(entityId)}` +
      `/components/${encodeURIComponent(componentTypeId)}`), {
      headers: { Accept: "application/json" }, cache: "no-store",
    });
    return response?.ok
      ? componentValue(await json(response), entityId, componentTypeId)
      : null;
  } catch {
    return null;
  }
}

async function readCompleteRelationshipTargetIds(
  fetchImpl,
  origin,
  entityRoot,
  fromEntityId,
  qualifiedKind,
  { unavailableFirstPageIsEmpty = false } = {},
) {
  const relationshipRoot = entityRoot.replace(/\/entities$/u, "/relationships");
  const path = `${relationshipRoot}?fromEntityId=${encodeURIComponent(fromEntityId)}` +
    `&qualifiedKind=${encodeURIComponent(qualifiedKind)}`;
  const pages = await readJsonPages({
    fetchImpl, origin, path, maximumPages: 10, maximumItems: 1_000,
  });
  if (pages.status !== "complete") {
    return unavailableFirstPageIsEmpty && unavailableOnFirstPage(pages) ? [] : null;
  }
  const payload = { items: pages.items };
  const targets = relationshipTargetIds(payload, fromEntityId, qualifiedKind);
  return pages.items.length === targets.length ? targets : null;
}

async function readExactRelationshipTargets(
  fetchImpl,
  origin,
  entityRoot,
  fromEntityId,
  qualifiedKind,
  options,
) {
  const targets = await readCompleteRelationshipTargetIds(
    fetchImpl, origin, entityRoot, fromEntityId, qualifiedKind, options,
  );
  return targets === null ? null : [...new Set(targets)];
}

async function readExactIncomingRelationshipSources(
  fetchImpl,
  origin,
  entityRoot,
  toEntityId,
  qualifiedKind,
) {
  const relationshipRoot = entityRoot.replace(/\/entities$/u, "/relationships");
  const path = `${relationshipRoot}?toEntityId=${encodeURIComponent(toEntityId)}` +
    `&qualifiedKind=${encodeURIComponent(qualifiedKind)}`;
  const pages = await readJsonPages({
    fetchImpl, origin, path, maximumPages: 10, maximumItems: 1_000,
  });
  if (pages.status !== "complete") return null;
  const sources = pages.items.map((item) =>
    token(item?.toEntityId) === toEntityId && token(item?.qualifiedKind) === qualifiedKind
      ? token(item?.fromEntityId)
      : null);
  return sources.every(Boolean) && new Set(sources).size === sources.length ? sources : null;
}


async function readSingleExactRelationshipTarget(
  fetchImpl,
  origin,
  entityRoot,
  fromEntityId,
  qualifiedKind,
) {
  const relationshipRoot = entityRoot.replace(/\/entities$/u, "/relationships");
  const path = `${relationshipRoot}?fromEntityId=${encodeURIComponent(fromEntityId)}` +
    `&qualifiedKind=${encodeURIComponent(qualifiedKind)}`;
  const pages = await readJsonPages({
    fetchImpl, origin, path, pageSize: 2, maximumPages: 1, maximumItems: 2,
  });
  if (pages.status !== "complete" || pages.items.length !== 1) return null;
  const item = pages.items[0];
  return token(item?.fromEntityId) === fromEntityId && token(item?.qualifiedKind) === qualifiedKind
    ? token(item?.toEntityId)
    : null;
}

function validActiveRoute(value) {
  return value && typeof value === "object" && !Array.isArray(value) &&
    ownValue(value, "status") === "active" &&
    ["public", "party", "gm"].includes(ownValue(value, "visibility")) &&
    ownValue(value, "mode") === "on-foot" &&
    Number.isInteger(ownValue(value, "durationMinutes")) && ownValue(value, "durationMinutes") >= 1 &&
    ownValue(value, "durationMinutes") <= 1_440;
}

function validOpenRouteAvailability(value) {
  return value && typeof value === "object" && !Array.isArray(value) && ownValue(value, "status") === "open";
}

function validActiveLocation(value) {
  return value && typeof value === "object" && !Array.isArray(value) &&
    ["region", "settlement", "site", "interior"].includes(ownValue(value, "kind")) &&
    ownValue(value, "status") === "active" &&
    ["public", "party", "gm"].includes(ownValue(value, "visibility"));
}

/**
 * Resolves only exact, active, open, directed on-foot routes from the current location. The generic
 * relationship index supplies candidate identities; canonical route/location state independently
 * proves each result and knowledge remains the Player authorization boundary.
 */
export async function readKnownOpenRoutes({
  fetchImpl,
  origin,
  entityRoot,
  worldId,
  currentLocationId,
  perspective,
  projectedKnowledge,
  locationDirectory,
  failOnUnavailable = false,
}) {
  if (!worldId || !currentLocationId ||
      (perspective === "player" && projectedKnowledge?.status !== "ready")) return [];
  const locationById = new Map(locationDirectory.map((location) => [location.id, location]));
  const admittedSubjectIds = new Set();
  const subjectEntries = new Map();
  for (const entry of projectedKnowledge?.entries ?? []) {
    const subjectId = token(entry?.subject?.id);
    if (subjectId && entry.stance !== "familiar") admittedSubjectIds.add(subjectId);
    if (!subjectId || entry.stance === "familiar") continue;
    const values = subjectEntries.get(subjectId) ?? [];
    values.push(entry);
    subjectEntries.set(subjectId, values);
  }
  const leavingRouteIds = await readExactIncomingRelationshipSources(
    fetchImpl, origin, entityRoot, currentLocationId, WORLD_ROUTE_RELATIONSHIP_KINDS.origin,
  );
  if (leavingRouteIds === null) {
    if (failOnUnavailable) throw new Error("The current route relationships are unavailable.");
    return [];
  }
  const authorizedRouteIds = perspective === "player"
    ? leavingRouteIds.filter((routeId) => subjectEntries.has(routeId))
    : leavingRouteIds;

  const candidates = await Promise.all(authorizedRouteIds.map(async (routeId) => {
    const route = await readExactComponent(
      fetchImpl, origin, entityRoot, routeId, WORLD_ROUTE_COMPONENT_TYPE_ID,
    );
    return validActiveRoute(route) ? { routeId, route } : null;
  }));

  const resolved = await Promise.all(candidates.filter(Boolean).map(async ({ routeId, route }) => {
    const [availability, routeWorldId, destinationId] = await Promise.all([
      readExactComponent(
        fetchImpl, origin, entityRoot, routeId, WORLD_ROUTE_AVAILABILITY_COMPONENT_TYPE_ID,
      ),
      readSingleExactRelationshipTarget(
        fetchImpl, origin, entityRoot, routeId, WORLD_ROUTE_RELATIONSHIP_KINDS.world,
      ),
      readSingleExactRelationshipTarget(
        fetchImpl, origin, entityRoot, routeId, WORLD_ROUTE_RELATIONSHIP_KINDS.destination,
      ),
    ]);
    if (!validOpenRouteAvailability(availability) || routeWorldId !== worldId ||
        !destinationId || destinationId === currentLocationId) return null;
    const destination = locationById.get(destinationId) ??
      await readNamedEntity(fetchImpl, origin, entityRoot, destinationId);
    if (!destination || (perspective === "player" && !admittedSubjectIds.has(destinationId))) return null;
    const destinationState = await readExactComponent(
      fetchImpl, origin, entityRoot, destinationId, LOCATION_COMPONENT_TYPE_ID,
    );
    if (!validActiveLocation(destinationState)) return null;
    const admittedDetail = subjectEntries.get(routeId)?.map((entry) => text(entry.text, 1_500))
      .find(Boolean);
    const detail = perspective === "dm" ? text(ownValue(route, "summary"), 1_000) : admittedDetail;
    return {
      id: routeId,
      originId: currentLocationId,
      destinationId,
      destinationName: destination.name,
      ...(detail ? { detail } : { unavailableFields: ["detail"] }),
      mode: "on-foot",
      durationMinutes: route.durationMinutes,
    };
  }));
  return resolved.filter(Boolean).sort((left, right) =>
    left.destinationName.localeCompare(right.destinationName) || left.id.localeCompare(right.id));
}

function projectInteraction(value) {
  if (!value || typeof value !== "object" || Array.isArray(value) ||
      ownValue(value, "kind") !== "conversation" || ownValue(value, "status") !== "accepted") return null;
  const summary = text(ownValue(value, "summary"), 1_000);
  return summary ? { kind: "conversation", status: "accepted", summary } : {
    kind: "conversation", status: "accepted", unavailableFields: ["summary"],
  };
}

export async function readConversationCurrentScene({
  fetchImpl, origin, entityRoot, conversationId, perspective, authorizedActorIds,
}) {
  const [conversation, interaction, participantIds] = await Promise.all([
    readNamedEntity(fetchImpl, origin, entityRoot, conversationId),
    readExactComponent(fetchImpl, origin, entityRoot, conversationId, WORLD_INTERACTION_COMPONENT_TYPE_ID),
    readExactRelationshipTargets(
      fetchImpl, origin, entityRoot, conversationId, WORLD_INTERACTION_PARTICIPANT_RELATIONSHIP_KIND,
    ),
  ]);
  const projectedInteraction = projectInteraction(interaction);
  if (!conversation || !projectedInteraction || participantIds === null || participantIds.length > 32)
    return null;
  const visibleIds = perspective === "dm"
    ? participantIds
    : participantIds.filter((id) => authorizedActorIds.has(id));
  const allParticipants = (await Promise.all(visibleIds.map((id) =>
    readNamedEntity(fetchImpl, origin, entityRoot, id)))).filter(Boolean);
  const participants = allParticipants;
  const unavailableFields = [
    ...(projectedInteraction.unavailableFields ?? []),
    ...(participants.length !== visibleIds.length ? ["participants"] : []),
  ];
  const scope = new URL(entityRoot, origin).pathname
    .match(/^\/api\/applications\/([^/]+)\/state-spaces\/([^/]+)\/entities$/u);
  const media = scope ? await readAuthorizedMediaBatch({
    fetchImpl, origin, applicationId: decodeURIComponent(scope[1]), stateSpaceId: decodeURIComponent(scope[2]),
    entityIds: [conversationId, ...visibleIds], perspective,
  }) : new Map();
  return {
    status: "ready",
    kind: "conversation",
    ...(unavailableFields.length ? { coverage: "partial", unavailableFields } : {}),
    ...(media.get(conversationId)?.scene ? { scene: media.get(conversationId).scene } : {}),
    conversation: {
      id: conversation.id,
      name: conversation.name,
      participants: participants.map((participant) => ({
        ...participant,
        ...(media.get(participant.id)?.portrait ? { portrait: media.get(participant.id).portrait } : {}),
      })),
      ...(perspective === "dm" && projectedInteraction.summary ? { summary: projectedInteraction.summary } : {}),
    },
  };
}

export async function readCombatCurrentScene({
  fetchImpl, origin, entityRoot, encounterId, perspective, campaignId,
}) {
  const projected = await import("./encounter-board.js")
    .then(({ readEncounterBoardProjection }) => readEncounterBoardProjection({
      fetchImpl, origin, entityRoot, encounterId, perspective, campaignId,
    }))
    .catch((error) => {
      if (error?.name === "AbortError") throw error;
      return null;
    });
  if (!projected) return null;
  const board = projected.board;
  return {
    status: "ready",
    kind: "combat",
    combat: {
      id: projected.encounter.id,
      name: projected.encounter.name,
      participants: board.participants.map((participant) => ({
        id: participant.id,
        name: participant.name,
        initiative: participant.initiative,
        active: participant.active,
      })),
      board,
      ...(board.turn ? {
        turn: {
          id: board.turn.id,
          participationId: board.turn.participationId,
          actorName: board.turn.actorName,
          ordinal: board.turn.ordinal,
        },
      } : {}),
    },
  };
}


/**
 * Reads the host-selected application/state-space/seat binding and a server-validated campaign
 * context. The browser may request a campaign token, but it is accepted only after this adapter
 * rediscovers an exact readable campaign root inside the already authorized state space.
 * @param {{
 *   serverOrigin: string,
 *   fetchImpl?: typeof fetch,
 *   requestedPerspective?: string | null,
 *   requestedCampaignId?: string | null,
 * }} options
 */
export async function readGameServerContext(options) {
  const scope = createHubReadScope(options.fetchImpl ?? fetch);
  const envelope = await readGameServerContextCore({ ...options, fetchImpl: scope.fetch });
  return scope.failure ? unavailable(scope.failure) : envelope;
}

async function readGameServerContextCore({
  serverOrigin,
  fetchImpl = fetch,
  requestedPerspective = "dm",
  requestedCampaignId = null,
}) {
  const normalizedRequestedPerspective = requestedPerspective === null ? "dm" : requestedPerspective;
  const origin = normalizeGameServerOrigin(serverOrigin);
  if (!origin) return unavailable("The game server connection is not configured.");

  let response;
  try {
    response = await fetchImpl(url(origin, "/api/audience-context"), {
      headers: { Accept: "application/json" },
      cache: "no-store",
    });
  } catch {
    return unavailable("The game server could not be reached. Check the connection and retry.", "connection");
  }

  const context = await json(response);
  if (response.status === 403 || context?.status === "denied") {
    return denied("The game server did not authorize a campaign for this local table.");
  }
  if (!response.ok) return unavailable("The game server audience binding is unavailable.");

  const binding = audience(context);
  if (!binding) return unavailable("The game server returned an invalid audience binding.");
  const websiteAccess = response.headers.get("X-Website-Access");
  const sharedAccess = websiteAccess === "shared";
  const publicAccess = websiteAccess === "public";
  const hasBoundActor = binding.status === "bound" && binding.role === "actor";
  // A development preference or requested perspective can never promote a server-bound actor.
  const serverRole = binding;
  if ((publicAccess && serverRole.role !== "player-group") ||
      (serverRole.role === "player-group" && !publicAccess) ||
      (sharedAccess && serverRole.role !== "game-master")) {
    return unavailable("The game server returned an audience binding that does not match this website.");
  }
  const isPartyPlayer = publicAccess && serverRole.role === "player-group";
  const isGameMaster = serverRole.role === "game-master" && !isPartyPlayer;
  // The server grants the seat; a preference changes only its presentation. The registered
  // party query filters Player knowledge on the server, including on the shared local table.
  const contextAudience = isGameMaster
    ? {
        seat: "dm",
        perspective: normalizePerspective(normalizedRequestedPerspective),
        allowedPerspectives: ["dm", "player"],
        ...(sharedAccess ? { websiteAccess: "shared" } : {}),
      }
    : { seat: "player", perspective: "player", allowedPerspectives: ["player"],
        ...(publicAccess ? { websiteAccess: "public" } : {}) };
  const effectivePerspective = contextAudience.perspective ?? "player";
  const shouldReadBoundActor = hasBoundActor && effectivePerspective === "player";
  if (binding.status === "character-creation-required") {
    return {
      version: 1,
      status: "character-creation-required",
      applicationId: binding.applicationId,
      stateSpaceId: binding.stateSpaceId,
      campaignId: binding.campaignId,
      characterId: binding.actorId,
      message: "Create your character before opening the campaign companion.",
    };
  }

  const requestedCampaign = requestedCampaignId === null ? null : token(requestedCampaignId);
  if (requestedCampaignId !== null && !requestedCampaign) {
    return denied("That campaign is not available to this local table.");
  }
  if (!isGameMaster && requestedCampaign && requestedCampaign !== binding.campaignId) {
    return denied("That campaign is not available to this local table.");
  }
  const selectedCampaignId = requestedCampaign ?? binding.campaignId;

  const root = `/api/applications/${encodeURIComponent(binding.applicationId)}` +
    `/state-spaces/${encodeURIComponent(binding.stateSpaceId)}/entities`;
  let actorResponse;
  let registeredCampaign;
  let campaignContextRead;
  try {
    [campaignContextRead, actorResponse, registeredCampaign] = await Promise.all([
      readRegisteredCampaignContext({
        fetchImpl, origin, applicationId: binding.applicationId, stateSpaceId: binding.stateSpaceId,
        campaignId: selectedCampaignId,
        endpointEntityId: selectedCampaignId,
        perspective: effectivePerspective,
      }),
      shouldReadBoundActor
        ? fetchImpl(url(origin, `${root}/${encodeURIComponent(binding.actorId)}`), {
          headers: { Accept: "application/json" }, cache: "no-store",
        })
        : Promise.resolve(null),
      readRegisteredCampaignSummary({
        fetchImpl, origin, applicationId: binding.applicationId, stateSpaceId: binding.stateSpaceId,
        campaignId: selectedCampaignId, perspective: effectivePerspective,
      }),
    ]);
  } catch {
    return unavailable("The campaign binding was found, but the game state could not be read.");
  }

  const actor = await json(actorResponse);
  const campaignEntity = campaignContextRead?.campaign ?? null;
  const selectedContext = campaignContextRead ? {
    selectedWorldId: campaignContextRead.world.id,
    selectedCampaignId,
    worlds: [{
      ...campaignContextRead.world,
      campaigns: [campaignContextRead.campaign],
    }],
  } : null;
  const boundActorEntity = shouldReadBoundActor && actorResponse?.ok
    ? entity(actor, binding.actorId)
    : null;
  const actorEntity = isPartyPlayer
    ? { id: "shared-party", name: "Player" }
    : serverRole.role === "game-master"
      ? { id: "local-game-master", name: "Dungeon Master" }
      : boundActorEntity;
  if (!campaignEntity || !selectedContext || !actorEntity || !registeredCampaign) {
    return unavailable("The campaign binding no longer matches readable game state.");
  }
  if (!registeredCampaign.complete || registeredCampaign.totalCount !== registeredCampaign.party.length) {
    return unavailable("The party roster could not be loaded completely. Please try again.");
  }
  const deferredParty = isGameMaster && effectivePerspective === "dm"
    ? projectRegisteredPartyReferences(registeredCampaign.party)
    : isGameMaster || isPartyPlayer ? registeredCampaign.party.filter((entry) => entry.status === "active").map((entry) => ({
      id: entry.id,
      name: entry.name,
      state: entry.status,
      current: false,
      entries: [],
      detailsDeferred: true,
    })) : [{
      ...actorEntity,
      state: null,
      current: true,
      entries: [],
      detailsDeferred: true,
    }];
  if (deferredParty === null) {
    return unavailable("The party roster could not be loaded completely. Please try again.");
  }
  return {
    version: 1,
    status: "connected",
    applicationId: binding.applicationId,
    stateSpaceId: binding.stateSpaceId,
    audience: contextAudience,
    contextSelection: selectedContext,
    campaign: {
      ...campaignEntity,
      // Campaign context independently binds the selected Campaign identity. A malformed
      // descriptive title must not erase that authorized binding.
      name: registeredCampaign.title ?? campaignEntity.name,
      ...registeredCampaign,
      chapters: [],
      arcs: [],
      sessions: [],
      visits: [],
    },
    actor: actorEntity,
    currentSituation: {
      status: "unavailable",
      message: "Open a play view to load the current scene.",
    },
    party: deferredParty,
    knowledge: { status: "unavailable", entries: [], locations: [] },
    chronology: { status: "unavailable", perspective: effectivePerspective, entries: [] },
  };
}

function registeredCampaignContext(data, projection, campaignId) {
  if (token(ownValue(data, "campaignId")) !== campaignId) return null;
  const worldId = token(data.worldId);
  const campaign = { id: token(ownValue(data.campaign, "id")),
    name: text(ownValue(data.campaign, "name"), 400) ?? "Name unavailable" };
  const world = { id: token(ownValue(data.world, "id")),
    name: text(ownValue(data.world, "name"), 400) ?? "Name unavailable" };
  return worldId && campaign.id === campaignId && world.id === worldId
    ? { version: 1, campaignId, worldId, campaign, world, projection }
    : null;
}

/** Reads the selected Campaign and its exact declared World from a registered read model. */
export async function readRegisteredCampaignContext({
  fetchImpl = fetch, origin, applicationId, stateSpaceId, campaignId,
  endpointEntityId = campaignId, perspective,
}) {
  const entityRoot = `/api/applications/${encodeURIComponent(applicationId)}` +
    `/state-spaces/${encodeURIComponent(stateSpaceId)}/entities`;
  const parameters = new URLSearchParams({ perspective });
  const result = await readModelResponse({
    fetchImpl,
    resource: url(origin, `${entityRoot}/${encodeURIComponent(endpointEntityId)}` +
      `/read-models/${encodeURIComponent(campaignContextContract.id)}?${parameters}`),
    init: { headers: { Accept: "application/json" }, cache: "no-store" },
    applicationId,
    stateSpaceId,
    query: campaignContextContract,
    maximumBodyBytes: 70_000,
    maximumDataBytes: 65_536,
    statusPolicy: { ready: [200], forbidden: [403], unavailable: "remaining" },
    validate: (value) => registeredCampaignContext(value, null, campaignId) !== null,
  });
  return result.status === "ready"
    ? registeredCampaignContext(result.data, result.evidence, campaignId)
    : null;
}

function registeredCampaignDetails(data, projection, campaignId, perspective) {
  if (!data || typeof data !== "object" || Array.isArray(data) || token(data.campaignId) !== campaignId) return null;
  // Perspective is an authorization boundary, not a display field: do not retain a Player
  // response that contains DM-only fields or session records, even when other rows are usable.
  if (perspective !== "dm" &&
      ((Array.isArray(data.sessions) && data.sessions.length !== 0) ||
       [data.chapters, data.arcs].some((rows) => Array.isArray(rows) &&
         rows.some((row) => Object.hasOwn(row ?? {}, "gmContext"))))) return null;
  const includeGmContext = perspective === "dm";
  const chapters = campaignFieldRows(data, "chapters", 100, (item) => campaignChapter(item, includeGmContext));
  const arcs = campaignFieldRows(data, "arcs", 100, (item) => campaignArc(item, includeGmContext));
  const sessions = campaignFieldRows(data, "sessions", 100, (item) => {
    const record = campaignSession(item);
    if (!record) return null;
    if (!Object.hasOwn(item ?? {}, "recap")) return record;
    const recap = campaignSessionRecap(item.recap);
    const unavailableFields = [
      ...(record.value.unavailableFields ?? []),
      ...(recap === null ? ["recap"] : []),
    ];
    return {
      value: {
        ...record.value,
        ...(recap ? { recap } : {}),
        ...(unavailableFields.length ? { unavailableFields } : {}),
      },
      partial: record.partial || recap === null,
    };
  });
  return {
    version: 1,
    campaignId,
    chapters: chapters.items,
    arcs: arcs.items,
    sessions: sessions.items,
    detailFields: { chapters: chapters.coverage, arcs: arcs.coverage, sessions: sessions.coverage },
    projection,
  };
}

/** Reads the bounded Campaign chapter, arc, and session read model. */
export async function readRegisteredCampaignDetails({
  fetchImpl = fetch, origin, applicationId, stateSpaceId, campaignId, perspective,
}) {
  const entityRoot = `/api/applications/${encodeURIComponent(applicationId)}` +
    `/state-spaces/${encodeURIComponent(stateSpaceId)}/entities`;
  const parameters = new URLSearchParams({ perspective });
  const result = await readModelResponse({
    fetchImpl,
    resource: url(origin, `${entityRoot}/${encodeURIComponent(campaignId)}` +
      `/read-models/${encodeURIComponent(campaignDetailsContract.id)}?${parameters}`),
    init: { headers: { Accept: "application/json" }, cache: "no-store" },
    applicationId,
    stateSpaceId,
    query: campaignDetailsContract,
    maximumBodyBytes: 524_288,
    maximumDataBytes: 500_000,
    statusPolicy: { ready: [200], forbidden: [403], unavailable: "remaining" },
    validate: (value) => registeredCampaignDetails(value, null, campaignId, perspective) !== null,
  });
  return result.status === "ready"
    ? registeredCampaignDetails(result.data, result.evidence, campaignId, perspective)
    : null;
}

function registeredCampaignVisitPage(data, projection) {
  if (!data || typeof data !== "object" || Array.isArray(data) ||
      !Array.isArray(data.visits) || data.visits.length > 25 ||
      !Number.isInteger(data.totalCount) || data.totalCount < data.visits.length || data.totalCount > 256 ||
      typeof data.complete !== "boolean" || !(data.nextCursor === null || token(data.nextCursor)) ||
      data.complete !== (data.nextCursor === null)) return null;
  const locationCounts = new Map();
  for (const item of data.visits) {
    const locations = item?.locations;
    const locationId = Array.isArray(locations) && locations.length === 1 ? token(locations[0]?.id) : null;
    if (locationId) locationCounts.set(locationId, (locationCounts.get(locationId) ?? 0) + 1);
  }
  const visits = campaignFieldRows(data, "visits", 25, (item) => {
    const locations = item?.locations;
    if (!Array.isArray(locations) || locations.length !== 1) return null;
    const locationId = token(locations[0]?.id);
    const record = campaignLocationVisit(item, true);
    return locationId && locationCounts.get(locationId) === 1 && record
      ? { value: { ...record.value, locationId }, partial: record.partial }
      : null;
  });
  return {
    visits: visits.items,
    coverage: visits.coverage,
    totalCount: data.totalCount,
    complete: data.complete,
    nextCursor: data.nextCursor,
    sourceRevisionFingerprint: projection?.sourceRevisionFingerprint ?? null,
    projection,
  };
}

export async function readRegisteredCampaignVisitPage({
  fetchImpl = fetch, origin, applicationId, stateSpaceId, campaignId, cursor = null,
  expectedSourceRevision = null,
}) {
  const entityRoot = `/api/applications/${encodeURIComponent(applicationId)}` +
    `/state-spaces/${encodeURIComponent(stateSpaceId)}/entities`;
  const parameters = new URLSearchParams({ perspective: "dm", limit: "25" });
  if (cursor) parameters.set("cursor", cursor);
  const result = await readModelResponse({
    fetchImpl,
    resource: url(origin, `${entityRoot}/${encodeURIComponent(campaignId)}` +
      `/read-models/${encodeURIComponent(campaignLocationVisitsContract.id)}?${parameters}`),
    init: { headers: { Accept: "application/json" }, cache: "no-store" },
    applicationId,
    stateSpaceId,
    query: campaignLocationVisitsContract,
    maximumBodyBytes: 300_000,
    maximumDataBytes: 262_144,
    statusPolicy: { ready: [200], forbidden: [403], stale: [409], unavailable: "remaining" },
    expectedSourceRevision,
    validate: (value) => registeredCampaignVisitPage(value, null) !== null,
  });
  return result.status === "ready" ? registeredCampaignVisitPage(result.data, result.evidence) : null;
}

function registeredWorldCampaignPage(data, projection, worldId, worldName = null) {
  const includesSelectedWorld = Object.hasOwn(data ?? {}, "selectedWorld");
  const selectedWorld = includesSelectedWorld ? data.selectedWorld : { id: worldId, name: worldName };
  if (token(ownValue(selectedWorld, "id")) !== worldId ||
      !Array.isArray(ownValue(data, "campaigns")) || data.campaigns.length > 25 ||
      !Number.isInteger(data.totalCount) || data.totalCount < data.campaigns.length || data.totalCount > 256 ||
      typeof data.complete !== "boolean" || !(data.nextCursor === null || token(data.nextCursor)) ||
      data.complete !== (data.nextCursor === null)) return null;
  const idCounts = new Map();
  for (const item of data.campaigns) {
    const id = token(ownValue(item, "id"));
    if (id) idCounts.set(id, (idCounts.get(id) ?? 0) + 1);
  }
  const name = text(ownValue(selectedWorld, "name"), 400);
  let partial = name === null;
  const campaigns = data.campaigns.flatMap((item) => {
    const id = token(ownValue(item, "id"));
    // Selection eligibility remains narrow and explicit. Descriptions, goals and unrelated
    // metadata cannot grant that eligibility and must not gate an otherwise valid name.
    if (!id || ["__proto__", "prototype", "constructor"].includes(id) || idCounts.get(id) !== 1 ||
        item.status !== "active" || item.rulesetScope !== "dnd2024" || item.creationMethod !== "manual" ||
        !/^[a-f0-9]{64}$/u.test(item.reviewFingerprint)) { partial = true; return []; }
    const campaignName = text(ownValue(item, "name"), 400);
    if (!campaignName) partial = true;
    return [{ id, name: campaignName ?? "Name unavailable" }];
  });
  return {
    world: { id: worldId, name: name ?? "Name unavailable" }, campaigns,
    coverage: partial ? "partial" : "complete", pageRecordCount: data.campaigns.length,
    sourceRecordIds: data.campaigns.map((row) => token(ownValue(row, "id"))).filter(Boolean),
    totalCount: data.totalCount, complete: data.complete, nextCursor: data.nextCursor,
    sourceRevisionFingerprint: projection?.sourceRevisionFingerprint ?? null, projection,
  };
}

export async function readRegisteredWorldCampaignPage({
  fetchImpl = fetch, origin, applicationId, stateSpaceId, campaignId, worldId, cursor = null,
  expectedSourceRevision = null, worldName = null,
}) {
  const entityRoot = `/api/applications/${encodeURIComponent(applicationId)}` +
    `/state-spaces/${encodeURIComponent(stateSpaceId)}/entities`;
  const parameters = new URLSearchParams({ perspective: "dm", limit: "25",
    input: JSON.stringify({ selectionId: campaignId }) });
  if (cursor) parameters.set("cursor", cursor);
  const result = await readModelResponse({
    fetchImpl,
    resource: url(origin, `${entityRoot}/${encodeURIComponent(worldId)}` +
      `/read-models/${encodeURIComponent(worldCampaignDirectoryContract.id)}?${parameters}`),
    init: { headers: { Accept: "application/json" }, cache: "no-store" },
    applicationId,
    stateSpaceId,
    query: worldCampaignDirectoryContract,
    maximumBodyBytes: 300_000,
    maximumDataBytes: 262_144,
    statusPolicy: { ready: [200], forbidden: [403], stale: [409], unavailable: "remaining" },
    expectedSourceRevision,
    validate: (value) => registeredWorldCampaignPage(value, null, worldId, worldName) !== null,
  });
  return result.status === "ready"
    ? registeredWorldCampaignPage(result.data, result.evidence, worldId, worldName)
    : null;
}

async function readAllRegisteredPages(readPage, itemKey) {
  const items = [];
  const idCounts = new Map();
  const seenCursors = new Set();
  let cursor = null;
  let expectedSourceRevision = null;
  let expectedTotalCount = null;
  let identity = null;
  let receivedRecordCount = 0;
  let partial = false;
  for (let pageNumber = 0; pageNumber < 11; pageNumber += 1) {
    const page = await readPage(cursor, expectedSourceRevision);
    if (!page || (expectedTotalCount !== null && page.totalCount !== expectedTotalCount) ||
        !/^[0-9A-F]{64}$/u.test(page.sourceRevisionFingerprint ?? "") ||
        (expectedSourceRevision !== null && page.sourceRevisionFingerprint !== expectedSourceRevision)) return null;
    expectedTotalCount = page.totalCount;
    expectedSourceRevision ??= page.sourceRevisionFingerprint;
    if (identity && page.world && (identity.id !== page.world.id || identity.name !== page.world.name)) return null;
    identity ??= page.world ?? null;
    partial ||= page.coverage === "partial";
    receivedRecordCount += page.pageRecordCount ?? page[itemKey].length;
    if (receivedRecordCount > expectedTotalCount) return null;
    for (const id of page.sourceRecordIds ?? page[itemKey].map((item) => item.id))
      idCounts.set(id, (idCounts.get(id) ?? 0) + 1);
    for (const item of page[itemKey]) {
      if (items.length >= 256) return null;
      items.push(item);
    }
    if (page.nextCursor === null) {
      const admitted = items.filter((item) => idCounts.get(item.id) === 1);
      return receivedRecordCount === expectedTotalCount ? {
        items: admitted, identity, coverage: partial || admitted.length !== receivedRecordCount ? "partial" : "complete",
        sourceRevisionFingerprint: expectedSourceRevision,
      } : null;
    }
    if (seenCursors.has(page.nextCursor)) return null;
    seenCursors.add(page.nextCursor);
    cursor = page.nextCursor;
  }
  return null;
}

function registeredFactionDirectoryPage(data, projection) {
  if (!Array.isArray(ownValue(data, "items")) || data.items.length > 25 || !Number.isInteger(data.totalCount) ||
      data.totalCount < data.items.length || data.totalCount > 100 || typeof data.complete !== "boolean" ||
      !(data.nextCursor === null || token(data.nextCursor)) || data.complete !== (data.nextCursor === null)) return null;
  const factionIdCounts = new Map();
  for (const item of data.items) {
    const id = token(item?.id);
    if (id) factionIdCounts.set(id, (factionIdCounts.get(id) ?? 0) + 1);
  }
  const factions = data.items.map((item) => {
    const id = token(item?.id);
    if (!id || ["__proto__", "prototype", "constructor"].includes(id) || factionIdCounts.get(id) !== 1) return null;
    const nameValue = text(item?.name, 400);
    const name = nameValue ?? "Name unavailable";
    const record = worldFaction(item);
    if (record && !nameValue)
      record.unavailableFields = [...new Set([...(record.unavailableFields ?? []), "name"])];
    const references = (value) => {
      if (!Array.isArray(value) || value.length > 10) return { items: [], partial: true };
      const counts = new Map();
      for (const entry of value) {
        const entryId = token(entry?.id);
        if (entryId) counts.set(entryId, (counts.get(entryId) ?? 0) + 1);
      }
      let partial = false;
      const result = [];
      for (const entry of value) {
        const entryId = token(entry?.id);
        if (!entryId || ["__proto__", "prototype", "constructor"].includes(entryId) || counts.get(entryId) !== 1) {
          partial = true;
          continue;
        }
        result.push({ id: entryId, name: text(entry?.name, 400) ?? "Name unavailable" });
        if (!text(entry?.name, 400)) partial = true;
      }
      return { items: result, partial };
    };
    const memberResult = references(item?.members);
    const controlledSiteResult = references(item?.controlledSites);
    const territoryResult = references(item?.territories);
    const allyResult = references(item?.allies);
    const opponentResult = references(item?.opponents);
    const members = memberResult.items;
    const controlledSites = controlledSiteResult.items;
    const territories = territoryResult.items;
    const allies = allyResult.items;
    const opponents = opponentResult.items;
    const territoryReferences = [...controlledSites, ...territories];
    const territoryCounts = new Map();
    for (const entry of territoryReferences)
      territoryCounts.set(entry.id, (territoryCounts.get(entry.id) ?? 0) + 1);
    const uniqueTerritories = territoryReferences.filter((entry) => territoryCounts.get(entry.id) === 1);
    const referenceUnavailableFields = [
      ...(memberResult.partial ? ["members"] : []),
      ...(controlledSiteResult.partial || territoryResult.partial || uniqueTerritories.length !== territoryReferences.length
        ? ["territories"] : []),
      ...(allyResult.partial ? ["allies"] : []),
      ...(opponentResult.partial ? ["opponents"] : []),
    ];
    if (record && referenceUnavailableFields.length)
      record.unavailableFields = [...new Set([...(record.unavailableFields ?? []), ...referenceUnavailableFields])];
    return id && record ? {
      id, name, ...record,
      memberIds: members.map((entry) => entry.id),
      territoryIds: uniqueTerritories.map((entry) => entry.id),
      alliedIds: allies.map((entry) => entry.id),
      opposedIds: opponents.map((entry) => entry.id),
      memberReferences: members,
      territoryReferences: uniqueTerritories,
      alliedReferences: allies,
      opposedReferences: opponents,
    } : null;
  });
  return {
    factions: factions.filter(Boolean),
    coverage: factions.some((faction) => !faction || faction.unavailableFields?.length) ? "partial" : "complete",
    totalCount: data.totalCount,
    complete: data.complete,
    nextCursor: data.nextCursor,
    sourceRevisionFingerprint: projection?.sourceRevisionFingerprint ?? null,
    projection,
  };
}

/** Reads one registered GM faction page without loading the entity directory, party inventories, or knowledge. */
/** @param {{fetchImpl?: typeof fetch, origin: string, applicationId: string, stateSpaceId: string, worldId: string, cursor?: string | null}} options */
export async function readRegisteredFactionDirectoryPage({
  fetchImpl = fetch,
  origin,
  applicationId,
  stateSpaceId,
  worldId,
  cursor = null,
}) {
  const entityRoot = `/api/applications/${encodeURIComponent(applicationId)}` +
    `/state-spaces/${encodeURIComponent(stateSpaceId)}/entities`;
  const parameters = new URLSearchParams({ perspective: "dm", limit: "25" });
  if (cursor) parameters.set("cursor", cursor);
  const result = await readModelResponse({
    fetchImpl,
    resource: url(origin, `${entityRoot}/${encodeURIComponent(worldId)}` +
      `/read-models/${encodeURIComponent(factionDirectoryContract.id)}?${parameters}`),
    init: { headers: { Accept: "application/json" }, cache: "no-store" },
    applicationId,
    stateSpaceId,
    query: factionDirectoryContract,
    maximumBodyBytes: 524_288,
    maximumDataBytes: 500_000,
    statusPolicy: { ready: [200], forbidden: [403], unavailable: "remaining" },
    validate: (value) => registeredFactionDirectoryPage(value, null) !== null,
  });
  return result.status === "ready"
    ? registeredFactionDirectoryPage(result.data, result.evidence)
    : null;
}

/** Loads Campaign narrative records from registered read models only. */
/** @param {{fetchImpl?: typeof fetch, origin: string, source: any}} options */
/** @returns {Promise<any>} */
async function readDeferredCampaignVisitPages(readPage) {
  const candidates = [];
  const seenCursors = new Set();
  let cursor = null;
  let expectedSourceRevision = null;
  let expectedTotalCount = null;
  let partial = false;
  for (let pageNumber = 0; pageNumber < 11; pageNumber += 1) {
    const page = await readPage(cursor, expectedSourceRevision);
    if (!page || (expectedTotalCount !== null && page.totalCount !== expectedTotalCount) ||
        !/^[0-9A-F]{64}$/u.test(page.sourceRevisionFingerprint ?? "") ||
        (expectedSourceRevision !== null && page.sourceRevisionFingerprint !== expectedSourceRevision)) return null;
    expectedTotalCount ??= page.totalCount;
    expectedSourceRevision ??= page.sourceRevisionFingerprint;
    partial ||= page.coverage === "partial" || page.coverage === "invalid" || page.coverage === "absent";
    candidates.push(...page.visits);
    if (page.nextCursor === null) {
      const idCounts = new Map();
      const locationCounts = new Map();
      for (const item of candidates) {
        idCounts.set(item.id, (idCounts.get(item.id) ?? 0) + 1);
        locationCounts.set(item.locationId, (locationCounts.get(item.locationId) ?? 0) + 1);
      }
      const items = candidates.filter((item) => idCounts.get(item.id) === 1 && locationCounts.get(item.locationId) === 1);
      partial ||= items.length !== candidates.length || items.length > 256;
      if (items.length !== expectedTotalCount) partial = true;
      return {
        items: items.slice(0, 256),
        coverage: expectedTotalCount === 0 && !partial ? "empty" : partial ? "partial" : "ready",
        sourceRevisionFingerprint: expectedSourceRevision,
      };
    }
    if (seenCursors.has(page.nextCursor)) return null;
    seenCursors.add(page.nextCursor);
    cursor = page.nextCursor;
  }
  return null;
}

export async function readDeferredCampaignDetails({ fetchImpl = fetch, origin, source }) {
  const options = {
    fetchImpl, origin, applicationId: source.applicationId, stateSpaceId: source.stateSpaceId,
    campaignId: source.campaign.id, perspective: source.audience.perspective ?? "player",
  };
  const [details, visitPages] = await Promise.all([
    readRegisteredCampaignDetails(options),
    options.perspective === "dm"
      ? readDeferredCampaignVisitPages(
        (cursor, expectedSourceRevision) => readRegisteredCampaignVisitPage({
          ...options, cursor, expectedSourceRevision,
        }),
      )
      : Promise.resolve({ items: [] }),
  ]);
  if (!details || !visitPages) throw new Error("The campaign details are incomplete.");
  return {
    chapters: details.chapters,
    arcs: details.arcs,
    sessions: details.sessions,
    visits: visitPages.items,
    detailFields: {
      ...details.detailFields,
      visits: options.perspective === "dm" ? visitPages.coverage : "absent",
    },
  };
}

export async function readAuthorizedWorldHistory({ fetchImpl = fetch, origin, source }) {
  const { applicationId, stateSpaceId } = source;
  const campaignId = source.campaign.id;
  const perspective = source.audience.perspective ?? "player";
  const query = { id: "dnd2024.query.world-chronology" };
  const expectedResolution = source.campaign.projection?.resolutionFingerprint;
  const entries = [], entryIds = new Set(), cursors = new Set();
  let cursor = null, expectedEnvelopeSourceRevision = null, graphSourceRevision = null;
  let projection = null, fieldCoverage = "complete";
  for (let page = 0; page < 500; page += 1) {
    const input = cursor === null ? null : JSON.stringify({
      cursor,
      expectedSourceRevision: expectedEnvelopeSourceRevision,
      expectedGraphSourceRevision: graphSourceRevision,
    });
    const result = await readModelResponse({
      fetchImpl,
      resource: url(origin, `/api/applications/${encodeURIComponent(applicationId)}` +
        `/state-spaces/${encodeURIComponent(stateSpaceId)}/entities/${encodeURIComponent(campaignId)}` +
        `/read-models/${query.id}?perspective=${encodeURIComponent(perspective)}` +
        (input === null ? "" : `&input=${encodeURIComponent(input)}`)),
      init: { headers: { Accept: "application/json" }, cache: "no-store" },
      applicationId, stateSpaceId, query,
      maximumBodyBytes: 65_536, maximumDataBytes: 60_000,
      statusPolicy: { ready: [200], forbidden: [403], stale: [409], unavailable: "remaining" },
      expectedSourceRevision: expectedEnvelopeSourceRevision,
      consume: (value) => {
        const projected = chronology(value, perspective);
        return projected.status === "unavailable" ? null : projected;
      },
    });
    if (result.status !== "ready" ||
        (expectedResolution && result.evidence.resolutionFingerprint !== expectedResolution) ||
        (projection && ["stateSpaceFingerprint", "resolutionFingerprint", "outputSchemaHash"]
          .some((field) => result.evidence[field] !== projection[field])) ||
        (expectedEnvelopeSourceRevision &&
          result.evidence.sourceRevisionFingerprint !== expectedEnvelopeSourceRevision) ||
        (graphSourceRevision && result.data.sourceRevision !== graphSourceRevision))
      throw new Error("The history view is unavailable.");
    projection ??= result.evidence;
    expectedEnvelopeSourceRevision ??= result.evidence.sourceRevisionFingerprint;
    graphSourceRevision ??= result.data.sourceRevision;
    if (result.data.fieldCoverage === "partial") fieldCoverage = "partial";
    for (const entry of result.data.entries) {
      if (entryIds.has(entry.id)) throw new Error("The history view is unavailable.");
      entryIds.add(entry.id); entries.push(entry);
    }
    const nextCursor = result.data.nextCursor;
    if (nextCursor === null) {
      const status = entries.length ? "ready" : "empty";
      if (status !== result.data.status && !(status === "ready" && result.data.status === "ready"))
        throw new Error("The history view is unavailable.");
      return { chronology: { status, perspective, entries, coverage: fieldCoverage, projection } };
    }
    const offset = cursor === null ? 0 : Number(cursor);
    if (result.data.status !== "ready" || result.data.coverage !== "partial" ||
        Number(nextCursor) !== offset + result.data.pageEntryCount || cursors.has(nextCursor) ||
        Number(nextCursor) > 500)
      throw new Error("The history view is unavailable.");
    cursors.add(nextCursor);
    cursor = nextCursor;
  }
  throw new Error("The history view is unavailable.");
}

export async function readAuthorizedWorldLore({ fetchImpl = fetch, origin, source, onProgress }) {
  const { applicationId, stateSpaceId } = source;
  const campaignId = source.campaign.id;
  const perspective = source.audience.perspective ?? "player";
  const result = await readRegisteredPartyKnowledge({ fetchImpl, origin, applicationId, stateSpaceId,
    campaignId, worldId: source.contextSelection?.selectedWorldId, perspective,
    onPage: typeof onProgress === "function" ? (knowledge) => onProgress({ knowledge }) : undefined });
  // A shared union does not grant access through the old actor-specific media endpoint.
  // Media remains absent in Player view until it has an equally scoped registered reader.
  if (perspective !== "dm") return { knowledge: result };
  return {
    knowledge: await attachAuthorizedKnowledgeMedia({
      fetchImpl, origin, applicationId, stateSpaceId, perspective,
      projectedKnowledge: result,
    }),
  };
}

/** Loads the complete bounded directory while rejecting repeated cursors, duplicates, and mixed revisions. */
export async function readAllWorldPeopleHoldingsPages(readPage) {
  const locations = new Map();
  const people = [];
  const holdings = [];
  const recordIds = new Map();
  const seenCursors = new Set();
  let cursor = null;
  let expectedSourceRevision = null;
  let expectedTotalCount = null;
  let world = null;
  let hierarchyComplete = true;
  let coverage = "complete";
  let receivedRecordCount = 0;
  for (let pageNumber = 0; pageNumber < 40; pageNumber += 1) {
    const page = await readPage(cursor, expectedSourceRevision);
    if (!page || page.status !== "ready") return {
      status: page?.status ?? "error", locations: [], people: [], holdings: [],
      legacyCompatible: pageNumber === 0 && page?.status === "unavailable",
    };
    if (!/^[0-9A-F]{64}$/u.test(page.sourceRevisionFingerprint ?? "") ||
        (expectedSourceRevision !== null && page.sourceRevisionFingerprint !== expectedSourceRevision) ||
        (expectedTotalCount !== null && page.totalCount !== expectedTotalCount) ||
        (world && (world.id !== page.world?.id || world.name !== page.world?.name))) {
      return { status: "stale", locations: [], people: [], holdings: [] };
    }
    expectedSourceRevision ??= page.sourceRevisionFingerprint;
    expectedTotalCount ??= page.totalCount;
    world ??= page.world;
    hierarchyComplete = hierarchyComplete && page.hierarchyComplete;
    if (page.coverage === "partial") coverage = "partial";
    const pageRecordCount = page.pageRecordCount ?? page.people.length + page.holdings.length;
    if (!Number.isInteger(pageRecordCount) || pageRecordCount < page.people.length + page.holdings.length ||
        pageRecordCount > 50 || receivedRecordCount + pageRecordCount > expectedTotalCount)
      return { status: "error", locations: [], people: [], holdings: [] };
    receivedRecordCount += pageRecordCount;
    for (const id of page.sourceRecordIds ?? [...page.people, ...page.holdings].map((item) => item.id))
      recordIds.set(id, (recordIds.get(id) ?? 0) + 1);
    for (const location of page.locations) {
      const previous = locations.get(location.id);
      if (previous && JSON.stringify(previous) !== JSON.stringify(location))
        return { status: "stale", locations: [], people: [], holdings: [] };
      locations.set(location.id, location);
    }
    for (const [key, target] of [["people", people], ["holdings", holdings]]) {
      for (const item of page[key]) {
        if (people.length + holdings.length >= 2_000)
          return { status: "error", locations: [], people: [], holdings: [] };
        target.push(item);
      }
    }
    if (page.nextCursor === null) {
      if (receivedRecordCount !== expectedTotalCount) return {
        status: "stale", locations: [], people: [], holdings: [],
      };
      const admittedPeople = people.filter((item) => recordIds.get(item.id) === 1);
      const admittedHoldings = holdings.filter((item) => recordIds.get(item.id) === 1);
      if (admittedPeople.length + admittedHoldings.length !== receivedRecordCount) coverage = "partial";
      return {
        status: "ready", world, locations: [...locations.values()], people: admittedPeople, holdings: admittedHoldings,
        totalCount: expectedTotalCount, hierarchyComplete, coverage,
        sourceRevisionFingerprint: expectedSourceRevision,
        projection: page.projection,
      };
    }
    if (seenCursors.has(page.nextCursor))
      return { status: "stale", locations: [], people: [], holdings: [] };
    seenCursors.add(page.nextCursor);
    cursor = page.nextCursor;
  }
  return { status: "error", locations: [], people: [], holdings: [] };
}

export async function readWorldPeopleHoldings({ fetchImpl = fetch, origin, source }) {
  const perspective = source.audience.perspective ?? "player";
  if (source.audience.seat !== "dm" || perspective !== "dm")
    return readAuthorizedWorldLore({ fetchImpl, origin, source });
  const worldId = token(source.contextSelection?.selectedWorldId);
  if (!worldId) throw new Error("The selected World identity is unavailable.");
  const options = {
    fetchImpl, origin, applicationId: source.applicationId, stateSpaceId: source.stateSpaceId, worldId,
  };
  let result = await readAllWorldPeopleHoldingsPages((cursor, expectedSourceRevision) =>
    readRegisteredWorldPeopleHoldingsPage({ ...options, cursor, expectedSourceRevision }));
  // Keep the pre-R12 read model as a one-request compatibility bridge while older catalogs are
  // upgraded. Never switch contracts after a continuation has begun.
  if (result.status === "unavailable" && result.legacyCompatible === true) {
    const legacy = await readRegisteredWorldPeopleHoldings(options);
    if (legacy.status === "ready") result = {
      ...legacy,
      totalCount: legacy.totalCount,
      hierarchyComplete: true,
      sourceRevisionFingerprint: legacy.projection?.sourceRevisionFingerprint ?? null,
    };
    else result = legacy;
  }
  if (result.status === "forbidden") throw new Error("The people directory is unavailable to this audience.");
  if (result.status !== "ready") throw new Error("The people directory is incomplete.");
  const locations = new Map(result.locations.map((location) => [location.id, location]));
  for (const location of source.locationDirectory ?? []) locations.set(location.id, location);
  return {
    locationDirectory: [...locations.values()].sort((left, right) =>
      left.name.localeCompare(right.name) || left.id.localeCompare(right.id)),
    locationDirectoryAudience: perspective,
    worldDirectory: {
      people: result.people,
      holdings: result.holdings,
      factions: source.worldDirectory?.factions ?? [],
      peopleHierarchyComplete: result.hierarchyComplete,
      peopleCoverage: result.coverage,
      directoryRecordCount: result.totalCount,
      peopleSourceRevisionFingerprint: result.sourceRevisionFingerprint,
    },
  };
}

/** Composes registered play projections and only the bounded adapters listed in the release contract. */
export async function readCurrentViewPatch({ fetchImpl = fetch, origin, source }) {
  // Match the already-authorized bootstrap projection when an older connected
  // source omits an explicit perspective. Downstream optional readers receive
  // this same audience, rather than each inventing its own fallback.
  source = { ...source, audience: { ...source.audience,
    perspective: source.audience.perspective ?? source.audience.seat } };
  const { applicationId, stateSpaceId } = source;
  const campaignId = source.campaign.id;
  const perspective = source.audience.perspective;
  const preview = source.audience.seat === "dm" && perspective === "player";
  const current = await readRegisteredCurrentPlay({
    fetchImpl, origin, applicationId, stateSpaceId, campaignId, perspective,
  });
  if (current.status === "forbidden") throw new CurrentViewAuthorizationError();
  if (!["ready", "empty"].includes(current.status))
    throw new Error(current.status === "stale" ? "The current scene changed while it was loading." :
      "The current scene could not be read safely.");
  const expectedResolution = source.campaign.projection?.resolutionFingerprint;
  if (expectedResolution && current.projection.resume.resolutionFingerprint !== expectedResolution)
    throw new Error("The Current source binding changed while it was loading.");

  let locations = Array.isArray(source.locationDirectory) ? source.locationDirectory : [];
  if (locations.length === 0) {
    const worldId = token(source.contextSelection?.selectedWorldId);
    if (worldId) {
      const scope = await readRegisteredWorldLocationScope({
        fetchImpl, origin, applicationId, stateSpaceId, scopeId: worldId, perspective,
        includeMedia: !preview,
      });
      if (scope.status === "ready") locations = scope.items;
    }
  }
  const patch = {};
  // Current is a scene-first read.  The World/party knowledge reader is an
  // independent deferred owner and must not become a prerequisite for the
  // scene (or its authorized location/media) to render.
  const projectedKnowledge = source.knowledge;

  if (current.status === "empty") {
    let recorded = null;
    try {
      const response = await fetchImpl(url(origin, `/api/applications/${encodeURIComponent(applicationId)}` +
        `/state-spaces/${encodeURIComponent(stateSpaceId)}/play/sessions/${encodeURIComponent(campaignId)}`),
      { headers: { Accept: "application/json" }, cache: "no-store" });
      if (response?.ok) recorded = resolveRecordedPlaySituation(await json(response), locations.map((item) => item.id));
    } catch (error) { if (error?.name === "AbortError") throw error; }
    return {
      ...patch,
      locationDirectory: locations,
      locationDirectoryAudience: perspective,
      currentSituation: recorded ?? {
        status: "unavailable",
        message: "No authoritative current scene has been recorded for this campaign.",
      },
      currentLocationId: recorded?.locationId ?? null,
      currentPlayProjection: current.projection,
      knownRoutes: [],
    };
  }

  const scene = current.scene;
  let selectedLocation = locations.find((item) => item.id === scene.location.id) ?? null;
  const locationWasMissing = !selectedLocation;
  const entityRoot = `/api/applications/${encodeURIComponent(applicationId)}` +
    `/state-spaces/${encodeURIComponent(stateSpaceId)}/entities`;
  if (!selectedLocation) {
    const identity = await readNamedEntity(fetchImpl, origin, entityRoot, scene.location.id);
    if (!identity) throw new Error("The current location identity could not be read safely.");
    selectedLocation = {
      id: identity.id, name: identity.name, kind: scene.location.kind, summary: scene.location.summary,
    };
    locations = [...locations, selectedLocation];
  }
  // The current place can be nested below the scopes loaded for the map/directory.
  // Refresh its own authorized illustration independently of which World tabs were visited.
  const locationMediaState = preview ? { status: "ready", media: new Map() } : await readAuthorizedMediaBatchState({
    fetchImpl, origin, applicationId, stateSpaceId, entityIds: [scene.location.id], perspective,
    rejectForeignOwner: true,
  });
  if (locationMediaState.status !== "unavailable") {
    const { media: _previousLocationMedia, ...locationWithoutMedia } = selectedLocation;
    const visual = locationMediaState.media.get(scene.location.id);
    selectedLocation = { ...locationWithoutMedia, ...(visual ? { media: visual } : {}) };
  }
  locations = locations.map((item) => item.id === scene.location.id ? selectedLocation : item);
  const authorizedActorIds = new Set([
    ...(source.audience.seat === "player" ? [source.actor.id] : []),
    ...(source.party ?? []).map((member) => member.id),
  ]);
  let currentSituation;
  if (scene.kind === "combat") {
    const combat = await readCombatCurrentScene({
      fetchImpl, origin, entityRoot, encounterId: scene.encounterId, campaignId,
      perspective,
    });
    currentSituation = combat ? { ...combat, locationId: scene.location.id, affordances: scene.affordances,
      ...(scene.unavailableFields ? { coverage: "partial", unavailableFields: scene.unavailableFields } : {}) }
      : { status: "unavailable", locationId: scene.location.id,
        message: "The current encounter could not be read safely." };
  } else if (scene.kind === "conversation") {
    const conversation = await readConversationCurrentScene({
      fetchImpl, origin, entityRoot, conversationId: scene.conversationId,
      perspective, authorizedActorIds,
    });
    currentSituation = conversation
      ? { ...conversation, locationId: scene.location.id, affordances: scene.affordances,
        ...(scene.unavailableFields ? { coverage: "partial", unavailableFields: [
          ...new Set([...(conversation.unavailableFields ?? []), ...scene.unavailableFields]),
        ] } : {}) }
      : { status: "unavailable", locationId: scene.location.id,
        message: "The current conversation could not be read safely." };
  } else {
    currentSituation = {
      status: "ready", kind: "exploration", locationId: scene.location.id, affordances: scene.affordances,
      ...(scene.unavailableFields ? { coverage: "partial", unavailableFields: scene.unavailableFields } : {}),
    };
  }
  let knownRoutes = [];
  let routesCoverage = locationWasMissing ? "partial" : "complete";
  if (currentSituation.status === "ready" && currentSituation.kind === "exploration") {
    const knowledgeStatus = projectedKnowledge?.status;
    if (!knowledgeStatus || knowledgeStatus === "unavailable" ||
        (perspective === "player" && knowledgeStatus !== "ready")) {
      routesCoverage = "unavailable";
    } else {
      try {
        knownRoutes = await readKnownOpenRoutes({
          fetchImpl, origin, entityRoot, worldId: source.contextSelection.selectedWorldId,
          currentLocationId: scene.location.id, perspective, projectedKnowledge, locationDirectory: locations,
          failOnUnavailable: true,
        });
        if (knownRoutes.some((route) => route.unavailableFields?.includes("detail"))) routesCoverage = "partial";
      } catch (error) {
        if (error?.name === "AbortError") throw error;
        routesCoverage = "unavailable";
      }
    }
  }
  if (currentSituation.status === "ready" && currentSituation.kind === "exploration")
    currentSituation.routesCoverage = routesCoverage;
  return {
    ...patch,
    locationDirectory: locations,
    locationDirectoryAudience: perspective,
    currentSituation,
    currentLocationId: scene.location.id,
    knownRoutes,
    currentPlayProjection: current.projection,
  };
}

/**
 * Completes only the requested deferred view. Registered party knowledge is server-filtered;
 * no ambient DM knowledge, private notebook, or media is requested for Player preview.
 * Registered collection adapters remain bounded and follow every continuation. The named-record
 * Current/Play adapters are retained only where the catalog does not yet expose a closed query.
 * @param {{fetchImpl?: typeof fetch, origin: string, source: any, section: string, onProgress?: (patch: any) => Promise<void>|void}} options
 * @returns {Promise<any>}
 */
export async function readDeferredHubSection({ fetchImpl = fetch, origin, source, section, onProgress }) {
  if (!["context", "history", "lore", "locations", "people", "current"].includes(section))
    throw new Error("Unknown deferred view.");
  let failure = null;
  let requestBudgetExceeded = false;
  let requests = 0;
  const preview = source.audience.seat === "dm" && source.audience.perspective === "player";
  const scope = createHubReadScope(async (input, init) => {
    const target = new URL(String(input));
    if (preview && target.pathname.endsWith("/media")) return new Response(null, { status: 404 });
    if (++requests > 2_000) {
      requestBudgetExceeded = true;
      failure = "This view exceeds its bounded read budget.";
      throw new Error(failure);
    }
    try {
      const response = await fetchImpl(input, init);
      // A missing optional component is legitimate; transport failures and denied directories
      // must never be converted by an adapter into a credible empty collection.
      if (response.status >= 500 || response.status === 429 ||
          (!response.ok && /\/(entities|containments)$/.test(target.pathname)))
        failure = "The view could not be read completely.";
      return response;
    } catch (error) {
      failure = "The view could not be read completely.";
      throw error;
    }
  });
  const read = scope.fetch;
  const { applicationId, stateSpaceId } = source;
  const campaignId = source.campaign.id;
  const perspective = source.audience.perspective ?? "player";
  const patch = {};
  if (section === "context") {
    if (source.audience.seat !== "dm") {
      patch.contextSelection = source.contextSelection;
    } else {
      const selectedWorldId = source.contextSelection.selectedWorldId;
      const selectedWorld = source.contextSelection.worlds?.find((world) => world.id === selectedWorldId)
        ?? (source.world?.id === selectedWorldId ? source.world : null);
      const directory = await readAllRegisteredPages(
        (cursor, expectedSourceRevision) => readRegisteredWorldCampaignPage({
          fetchImpl: read,
          origin,
          applicationId,
          stateSpaceId,
          campaignId,
          worldId: selectedWorldId,
          worldName: selectedWorld?.name ?? null,
          cursor,
          expectedSourceRevision,
        }),
        "campaigns",
      );
      if (!directory?.identity || !directory.items.some((item) => item.id === campaignId))
        throw new Error("The campaign directory is incomplete.");
      patch.contextSelection = {
        selectedWorldId: directory.identity.id,
        selectedCampaignId: campaignId,
        coverage: directory.coverage,
        worlds: [{ ...directory.identity, campaigns: directory.items }],
      };
    }
  } else if (section === "history") {
    Object.assign(patch, await readAuthorizedWorldHistory({ fetchImpl: read, origin, source }));
  } else if (section === "lore") {
    Object.assign(patch, await readAuthorizedWorldLore({ fetchImpl: read, origin, source, onProgress }));
  } else if (section === "locations") {
    const worldId = token(source.contextSelection?.selectedWorldId);
    if (!worldId) throw new Error("The selected World identity is unavailable.");
    const root = await readWorldLocationScopePatch({
      fetchImpl: read, origin, source, scopeId: worldId, cursor: null,
    });
    Object.assign(patch, root, await readWorldLocationDirectory({
      fetchImpl: read, origin, source: { ...source, ...root },
    }));
  } else if (section === "people") {
    Object.assign(patch, await readWorldPeopleHoldings({ fetchImpl: read, origin, source }));
  } else if (section === "current") {
    Object.assign(patch, await readCurrentViewPatch({ fetchImpl: read, origin, source }));
  }
  // Current composes an exact scene with optional routes, lore, and media. Those
  // optional reads record local coverage on the returned situation; a 5xx or
  // transport failure there must not discard the independently bound scene.
  // Global read-budget failures remain hard boundaries for every section.
  const hardBudgetFailure = requestBudgetExceeded
    ? "This view exceeds its bounded read budget."
    : scope.budgetFailure ?? null;
  const currentPatchCanStandAlone = section === "current" &&
    patch.currentSituation && typeof patch.currentSituation === "object" &&
    ["ready", "unavailable"].includes(patch.currentSituation.status);
  if (hardBudgetFailure || (failure || scope.failure) && !currentPatchCanStandAlone)
    throw new Error(hardBudgetFailure ?? failure ?? scope.failure);
  return patch;
}
