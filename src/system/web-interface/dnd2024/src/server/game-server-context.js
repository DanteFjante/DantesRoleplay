import { createHubReadScope } from "./hub-read-scope.js";
import { readCompletePages } from "./complete-pagination.js";
import { readBoundedJson, readModelResponse } from "./read-model-response.js";
import { projectRegisteredPartyReferences, readRegisteredCampaignSummary } from "./campaign-summary.js";
import { contract as campaignContextContract } from "./campaign-context-contract.js";
import { contract as campaignDetailsContract } from "./campaign-details-contract.js";
import { contract as campaignLocationVisitsContract } from "./campaign-location-visits-contract.js";
import { contract as worldCampaignDirectoryContract } from "./world-campaign-directory-contract.js";
import { contract as characterSheetContract } from "./character-sheet-contract.js";
import { contract as characterDossierContract } from "./character-dossier-contract.js";
import { contract as factionDirectoryContract } from "./faction-directory-contract.js";

export { readRegisteredCampaignSummary } from "./campaign-summary.js";

const TOKEN_MAXIMUM = 200;
const LOCATION_COMPONENT_TYPE_ID = "game.core.world.location";
const WORLD_MAP_ANCHOR_COMPONENT_TYPE_ID = "game.core.world.map.anchor";
const WORLD_ROUTE_COMPONENT_TYPE_ID = "game.core.world.route";
const WORLD_ROUTE_AVAILABILITY_COMPONENT_TYPE_ID = "game.core.world.route.availability";
const WORLD_ROUTE_RELATIONSHIP_KINDS = {
  world: "game.core.world.route.in-world",
  origin: "game.core.world.route.from",
  destination: "game.core.world.route.to",
};
const CAMPAIGN_CURRENT_SCENE_COMPONENT_TYPE_ID = "game.core.campaign.current-scene";
const CAMPAIGN_SCENE_AFFORDANCES_COMPONENT_TYPE_ID =
  "game.core.campaign.scene-affordances";
const WORLD_INTERACTION_COMPONENT_TYPE_ID = "game.core.world.interaction";
const WORLD_INTERACTION_PARTICIPANT_RELATIONSHIP_KIND =
  "game.core.world.interaction.participant";
const ENCOUNTER_DEFINITION_COMPONENT_TYPE_ID = "dnd2024.encounter.definition";
const ENCOUNTER_PARTICIPATION_COMPONENT_TYPE_ID = "dnd2024.encounter.participation";
const COMBAT_INITIATIVE_COMPONENT_TYPE_ID = "dnd2024.combat.initiative";
const ENCOUNTER_ROUND_COMPONENT_TYPE_ID = "dnd2024.encounter.round";
const ENCOUNTER_TURN_COMPONENT_TYPE_ID = "dnd2024.encounter.turn";
const COMBAT_TURN_BUDGET_COMPONENT_TYPE_ID = "dnd2024.combat.turn-budget";
const ENCOUNTER_RELATIONSHIP_KINDS = {
  participants: "dnd2024.encounter.has-participation",
  actor: "dnd2024.encounter.participation.for-actor",
  activeRound: "dnd2024.encounter.active-round",
  activeTurn: "dnd2024.encounter.active-turn",
};
const WORLD_MOTIVE_COMPONENT_TYPE_ID = "game.core.world.motive";
const WORLD_FACTION_COMPONENT_TYPE_ID = "game.core.world.faction";
const WORLD_FACTION_RELATIONSHIP_KINDS = {
  members: "game.core.world.faction.member",
  controls: "game.core.world.faction.controls",
  territories: "game.core.world.faction.territory-controls",
  allies: "game.core.world.faction.allied-with",
  opponents: "game.core.world.faction.opposed-to",
};
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

function unavailable(message) {
  return { version: 1, status: "unavailable", message };
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
    : (value?.role === "actor" || actorId ? "actor" : null);
  if (value?.status !== "bound" || !applicationId || !stateSpaceId || !campaignId || !role) return null;
  if (role === "game-master") {
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

function containerId(value) {
  if (typeof value?.containment?.containerEntityId === "string") {
    return text(value.containment.containerEntityId, 220);
  }
  if (typeof value?.containerId === "string") {
    return text(value.containerId, 220);
  }
  if (typeof value?.container === "string") {
    return text(value.container, 220);
  }
  if (value?.container?.id && typeof value.container.id === "string") {
    return text(value.container.id, 220);
  }
  return null;
}

function containmentSlot(value) {
  return typeof value?.containment?.slot === "string"
    ? text(value.containment.slot, 100)
    : null;
}

export function resolvePresenceLocation(value, expectedActorId, authorizedLocationIds) {
  const edge = value?.containment;
  const containedEntityId = token(edge?.containedEntityId);
  const locationId = token(edge?.containerEntityId);
  if (containedEntityId !== expectedActorId || edge?.slot !== "presence" || !locationId) return null;
  return authorizedLocationIds.includes(locationId) ? locationId : null;
}

function hasExactKeys(value, keys) {
  return value && typeof value === "object" && !Array.isArray(value) &&
    Object.keys(value).length === keys.length && keys.every((key) => Object.hasOwn(value, key));
}

function exactEntityReference(value) {
  if (!hasExactKeys(value, ["entityId"])) return null;
  const entityId = token(value.entityId);
  return entityId ? { entityId } : null;
}

export function resolveCurrentSceneRecord(value, authorizedLocationIds) {
  const allowedKeys = ["location", "conversation", "encounter"];
  if (!value || typeof value !== "object" || Array.isArray(value) ||
      Object.keys(value).some((key) => !allowedKeys.includes(key)) ||
      !Object.hasOwn(value, "location")) return null;
  const location = exactEntityReference(value.location);
  const conversation = value.conversation === undefined ? null : exactEntityReference(value.conversation);
  const encounter = value.encounter === undefined ? null : exactEntityReference(value.encounter);
  if (!location || (value.conversation !== undefined && !conversation) ||
      (value.encounter !== undefined && !encounter) ||
      !authorizedLocationIds.includes(location.entityId)) return null;
  return {
    kind: encounter ? "combat" : (conversation ? "conversation" : "exploration"),
    locationId: location.entityId,
    ...(conversation ? { conversationId: conversation.entityId } : {}),
    ...(encounter ? { encounterId: encounter.entityId } : {}),
  };
}

/**
 * Projects only the audience-bound active play situation written by the play recorder. This is
 * deliberately a distinct read-model branch: it can describe continuity between clients without
 * pretending that model narration created an authoritative encounter, initiative, or ECS scene.
 */
export function resolveRecordedPlaySituation(value, authorizedLocationIds) {
  const situation = value?.currentSituation;
  const id = token(situation?.id);
  const kind = RECORDED_SITUATION_KINDS.has(situation?.kind) ? situation.kind : null;
  const summary = text(situation?.summary, 4_000);
  if (!id || situation?.status !== "active" || !kind || !summary ||
      !Array.isArray(situation.participants) || situation.participants.length > 32) return null;
  const participants = [];
  for (let index = 0; index < situation.participants.length; index++) {
    const participant = situation.participants[index];
    const name = text(participant?.name, 200);
    if (!name) return null;
    const entityId = participant.entityId === null || participant.entityId === undefined
      ? null
      : token(participant.entityId);
    if (participant.entityId !== null && participant.entityId !== undefined && !entityId) return null;
    participants.push({ id: entityId ?? `${id}.participant.${index + 1}`, name, ...(entityId ? { entityId } : {}) });
  }
  let location;
  let locationId;
  if (situation.location !== null && situation.location !== undefined) {
    const name = text(situation.location.name, 200);
    const entityId = situation.location.entityId === null || situation.location.entityId === undefined
      ? null
      : token(situation.location.entityId);
    if (!name || (situation.location.entityId !== null && situation.location.entityId !== undefined && !entityId)) {
      return null;
    }
    locationId = entityId && authorizedLocationIds.includes(entityId) ? entityId : undefined;
    location = { name, ...(locationId ? { id: locationId } : {}) };
  }
  if (!Array.isArray(value.recentMessages) || value.recentMessages.length > 64) return null;
  const interactions = [];
  for (const message of value.recentMessages.slice(-12)) {
    const messageId = token(message?.id);
    const role = message?.role === "player" || message?.role === "assistant" ? message.role : null;
    const messageText = typeof message?.text === "string" && message.text.length > 0 && message.text.length <= 8_000 &&
      ![...message.text].some(character => /[\p{Cc}]/u.test(character) && character !== "\r" && character !== "\n" && character !== "\t")
      ? message.text
      : null;
    if (!messageId || !role || !messageText || !Number.isInteger(message.ordinal) || message.ordinal < 1) return null;
    interactions.push({ id: messageId, ordinal: message.ordinal, role, text: messageText });
  }
  return {
    status: "ready",
    kind: "recorded",
    ...(locationId ? { locationId } : {}),
    recorded: { id, kind, summary, participants, interactions, ...(location ? { location } : {}) },
  };
}

export function resolveSceneAffordancesRecord(value, currentScene, perspective) {
  if (!hasExactKeys(value, ["scene", "items"]) || !currentScene ||
      !["player", "dm"].includes(perspective)) return null;
  const selector = resolveCurrentSceneRecord(value.scene, [currentScene.locationId]);
  if (!selector || selector.locationId !== currentScene.locationId ||
      selector.conversationId !== currentScene.conversationId ||
      selector.encounterId !== currentScene.encounterId ||
      !Array.isArray(value.items) || value.items.length > 24) return null;
  const keys = new Set();
  const items = [];
  for (const item of value.items) {
    if (!hasExactKeys(item, ["key", "label", "summary", "visibility"])) return null;
    const key = text(item.key, 64);
    const label = text(item.label, 120);
    const summary = text(item.summary, 500);
    if (!key || !/^[a-z][a-z0-9]*(?:-[a-z0-9]+)*$/u.test(key) ||
        !label || !/\S/u.test(label) || !summary || !/\S/u.test(summary) ||
        !["party", "gm"].includes(item.visibility) || keys.has(key)) return null;
    keys.add(key);
    if (item.visibility === "party" || perspective === "dm") items.push({ key, label, summary });
  }
  return items;
}

function mapAnchor(value, expectedEntityId) {
  const parsed = componentValue(value, expectedEntityId, WORLD_MAP_ANCHOR_COMPONENT_TYPE_ID);
  if (!hasExactKeys(parsed, ["x", "y"])) return null;
  return Number.isInteger(parsed.x) && parsed.x >= 0 && parsed.x <= 1000 &&
    Number.isInteger(parsed.y) && parsed.y >= 0 && parsed.y <= 1000
    ? { x: parsed.x, y: parsed.y }
    : null;
}

export function projectMediaVisual(value) {
  if (!value || typeof value !== "object" || Array.isArray(value) || !Array.isArray(value.attachments) ||
      value.attachments.length > 64) return null;
  const roles = new Set(["portrait", "setting", "map", "illustration", "icon", "scene", "handout"]);
  const projected = {};
  const gallery = [];
  const ordered = [...value.attachments].sort((left, right) =>
    (left?.order ?? Number.MAX_SAFE_INTEGER) - (right?.order ?? Number.MAX_SAFE_INTEGER) ||
    String(left?.mediaId ?? "").localeCompare(String(right?.mediaId ?? "")));
  for (const attachment of ordered) {
    if (!hasExactKeys(attachment, ["mediaId", "role", "mediaType", "width", "height", "alt", "caption", "order", "contentUrl"]) ||
        !token(attachment.mediaId) || !roles.has(attachment.role) ||
        !["image/png", "image/jpeg", "image/webp"].includes(attachment.mediaType) ||
        !Number.isInteger(attachment.width) || attachment.width < 1 || attachment.width > 10_000 ||
        !Number.isInteger(attachment.height) || attachment.height < 1 || attachment.height > 10_000 ||
        !text(attachment.alt, 500) || typeof attachment.caption !== "string" || attachment.caption.length > 1_000 ||
        !Number.isInteger(attachment.order) || attachment.order < 0 || attachment.order > 10_000 ||
        typeof attachment.contentUrl !== "string" ||
        !attachment.contentUrl.startsWith("/api/applications/") || !attachment.contentUrl.endsWith("/content")) return null;
    const visual = {
      imageUrl: attachment.contentUrl,
      alt: attachment.alt,
      width: attachment.width,
      height: attachment.height,
    };
    gallery.push({
      ...visual,
      mediaId: attachment.mediaId,
      role: attachment.role,
      caption: attachment.caption,
    });
    if (!projected[attachment.role]) projected[attachment.role] = visual;
  }
  if (gallery.length > 1) projected.gallery = gallery;
  return Object.keys(projected).length > 0 ? projected : null;
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

function mediaVisual(value) {
  return projectMediaVisual(value);
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

function campaignChapter(value, includeGmContext) {
  const status = value?.status === "active" || value?.status === "closed" ? value.status : null;
  const title = text(value?.title, 160);
  const partyQuestion = text(value?.partyQuestion, 500);
  const closingSummary = optionalText(value, "closingSummary", 1_000);
  if (!status || !title || !partyQuestion) return null;
  if (status === "active" && closingSummary !== undefined) return null;
  if (status === "closed" && closingSummary === undefined) return null;
  const gmContext = includeGmContext ? optionalText(value, "gmContext", 1_000) : undefined;
  return {
    status,
    title,
    partyQuestion,
    ...(closingSummary ? { closingSummary } : {}),
    ...(gmContext ? { gmContext } : {}),
  };
}

function campaignArc(value, includeGmContext) {
  const statuses = new Set(["active", "resolved", "abandoned"]);
  const status = statuses.has(value?.status) ? value.status : null;
  const title = text(value?.title, 160);
  const partyStake = text(value?.partyStake, 500);
  const closingSummary = optionalText(value, "closingSummary", 1_000);
  if (!status || !title || !partyStake) return null;
  if (status === "active" && closingSummary !== undefined) return null;
  if (status !== "active" && closingSummary === undefined) return null;
  const gmContext = includeGmContext ? optionalText(value, "gmContext", 1_000) : undefined;
  return {
    status,
    title,
    partyStake,
    ...(closingSummary ? { closingSummary } : {}),
    ...(gmContext ? { gmContext } : {}),
  };
}

function campaignSession(value) {
  const status = value?.status === "active" || value?.status === "ended" ? value.status : null;
  const ordinal = Number.isInteger(value?.ordinal) && value.ordinal >= 1 ? value.ordinal : null;
  return status && ordinal ? { status, ordinal } : null;
}

function campaignSessionRecap(value) {
  if (value?.protocolVersion !== "session.s0.c3-only.v1") return null;
  const chapterId = token(value?.chapter?.id);
  const chapterStatus = value?.chapter?.status === "active" ? "active" : null;
  const chapterTitle = text(value?.chapter?.title, 160);
  const partyQuestion = text(value?.chapter?.partyQuestion, 500);
  const arcId = token(value?.arc?.id);
  const arcStatus = value?.arc?.status === "active" ? "active" : null;
  const arcTitle = text(value?.arc?.title, 160);
  const partyStake = text(value?.arc?.partyStake, 500);
  if (!chapterId || !chapterStatus || !chapterTitle || !partyQuestion ||
      !arcId || !arcStatus || !arcTitle || !partyStake ||
      !Array.isArray(value?.milestones) || value.milestones.length > 5) return null;
  const milestones = value.milestones.map((milestone) => {
    const milestoneChapterId = token(milestone?.chapterId);
    const titleValue = text(milestone?.title, 160);
    const closingSummary = text(milestone?.closingSummary, 1_000);
    const timestamp = text(milestone?.timestamp, 64);
    const sequence = Number.isInteger(milestone?.sequence) && milestone.sequence >= 0
      ? milestone.sequence
      : null;
    return milestoneChapterId && titleValue && closingSummary && timestamp && sequence !== null
      ? { chapterId: milestoneChapterId, title: titleValue, closingSummary, timestamp, sequence }
      : null;
  });
  if (!milestones.every(Boolean)) return null;
  return {
    chapter: { id: chapterId, status: chapterStatus, title: chapterTitle, partyQuestion },
    arc: { id: arcId, status: arcStatus, title: arcTitle, partyStake },
    milestones,
  };
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
  const gmContext = includeGmContext ? optionalText(value, "gmContext", 2_000) : undefined;
  if (firstVisitedMinute === null || lastVisitedMinute === null ||
      lastVisitedMinute < firstVisitedMinute || visitCount === null || !status || !summary || !memory) {
    return null;
  }
  return {
    firstVisitedMinute,
    lastVisitedMinute,
    visitCount,
    status,
    summary,
    memory,
    ...(gmContext ? { gmContext } : {}),
  };
}

function worldMotive(value) {
  const status = new Set(["draft", "active", "archived"]).has(value?.status) ? value.status : null;
  const visibility = new Set(["public", "party", "gm"]).has(value?.visibility) ? value.visibility : null;
  const summary = text(value?.summary, 1_000);
  return status && visibility && summary ? { status, visibility, summary } : null;
}

function worldFaction(value) {
  const status = new Set(["draft", "active", "archived"]).has(value?.status) ? value.status : null;
  const visibility = new Set(["public", "party", "gm"]).has(value?.visibility) ? value.visibility : null;
  const summary = text(value?.summary, 1_000);
  const goals = textList(value?.goals, 5, 500);
  const methods = textList(value?.methods, 5, 500);
  const assets = textList(value?.assets, 10, 500);
  const agendaState = new Set(["ready", "advanced"]).has(value?.agenda?.state)
    ? value.agenda.state
    : null;
  const agendaSummary = text(value?.agenda?.summary, 1_000);
  if (!status || !visibility || !summary || goals.length === 0 || methods.length === 0 ||
      !agendaState || !agendaSummary) return null;
  return { status, visibility, summary, goals, methods, assets, agenda: { state: agendaState, summary: agendaSummary } };
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

function validCharacterSheetV2(value, actorId) {
  const allowed = new Set([
    "version", "subject", "identity", "origin", "experience", "classes", "level",
    "proficiencyBonus", "abilities", "savingThrows", "skills", "initiative", "hitPoints",
    "temporaryHitPoints", "armorClass", "body", "movement", "senses", "conditions",
    "proficiencies", "features", "resources", "spellcasting", "actions", "inventory", "wallet",
  ]);
  if (!value || typeof value !== "object" || Array.isArray(value) ||
      Object.keys(value).some((key) => !allowed.has(key)) || value.version !== 2 ||
      !namedCharacterReference(value.subject) || value.subject.id !== actorId ||
      !validCharacterInventory(value.inventory) || !validCharacterWallet(value.wallet)) return false;

  const optionalArrays = ["classes", "abilities", "savingThrows", "skills", "movement", "senses",
    "conditions", "proficiencies", "features", "resources", "spellcasting", "actions"];
  if (optionalArrays.some((key) => value[key] !== undefined && !Array.isArray(value[key]))) return false;
  if (value.abilities && (value.abilities.length !== 6 || value.abilities.some((entry) =>
    !namedCharacterReference(entry?.ability) || !boundedInteger(entry?.score, 1, 30) ||
    !boundedInteger(entry?.modifier, -1000, 1000)))) return false;
  if (value.savingThrows && (value.savingThrows.length !== 6 || value.savingThrows.some((entry) =>
    !namedCharacterReference(entry?.ability) || typeof entry?.proficient !== "boolean" ||
    !boundedInteger(entry?.modifier, -1000, 1000)))) return false;
  if (value.skills && (value.skills.length !== 18 || value.skills.some((entry) =>
    !namedCharacterReference(entry?.skill) || !namedCharacterReference(entry?.ability) ||
    typeof entry?.proficient !== "boolean" || typeof entry?.expertise !== "boolean" ||
    !boundedInteger(entry?.modifier, -1000, 1000)))) return false;
  if (value.classes && value.classes.some((entry) =>
    !token(entry?.id) || !text(entry?.name, 5_000) || !namedCharacterReference(entry?.class) ||
    !boundedInteger(entry?.level, 1, 20) || !(entry?.subclass === null || namedCharacterReference(entry?.subclass)))) return false;
  if (value.spellcasting && value.spellcasting.some((entry) =>
    !token(entry?.id) || !text(entry?.name, 5_000) || !namedCharacterReference(entry?.sourceDefinition) ||
    !namedCharacterReference(entry?.ability) || !namedCharacterReferences(entry?.preparedSpells, 2_048) ||
    !namedCharacterReferences(entry?.availableSpells, 2_048))) return false;
  if (value.actions && value.actions.some((entry) =>
    !token(entry?.id) || !text(entry?.name, 5_000) || !namedCharacterReferences(entry?.activities, 256))) return false;
  if (value.senses && (value.senses.length > 32 || value.senses.some((entry) =>
    !entry || !namedCharacterReference(entry.sense) ||
    Object.keys(entry).some((key) => !["sense", "numerator", "denominator", "unit"].includes(key)) ||
    !(entry.numerator === undefined && entry.denominator === undefined && entry.unit === undefined ||
      boundedInteger(entry.numerator, 0, Number.MAX_SAFE_INTEGER) &&
      boundedInteger(entry.denominator, 1, Number.MAX_SAFE_INTEGER) && namedCharacterReference(entry.unit))))) return false;
  return true;
}

function validDossierDefinition(value) {
  if (!hasExactKeys(value, ["id", "label", "canonicalName", "kind", "status", "summary", "source"]) ||
      !token(value.id) || !text(value.label, 5_000) || !text(value.canonicalName, 5_000) ||
      !token(value.kind) || !["active", "identity-only"].includes(value.status) ||
      !(value.summary === null || text(value.summary, 5_000))) return false;
  return value.source === null || (hasExactKeys(value.source, ["sourceId", "locator"]) &&
    token(value.source.sourceId) && text(value.source.locator, 5_000));
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

function validCharacterDossier(value, actorId) {
  if (!hasExactKeys(value, ["version", "sheet", "origin", "classes", "features", "inventory", "levelOneRules", "definitions", "provenance"]) ||
      value.version !== 1 || !validCharacterSheetV2(value.sheet, actorId) ||
      !hasExactKeys(value.origin, ["species", "background", "traits"]) ||
      !validDossierDefinition(value.origin.species) || !validDossierDefinition(value.origin.background) ||
      !Array.isArray(value.origin.traits) || value.origin.traits.length > 128 ||
      value.origin.traits.some((entry) => !hasExactKeys(entry, ["key", "label", "status", "reason", "mechanicId", "source"]) ||
        !token(entry.key) || !text(entry.label, 5_000) || !["active", "pending"].includes(entry.status) ||
        !(entry.reason === null || token(entry.reason)) || !(entry.mechanicId === null || token(entry.mechanicId)) ||
        !(entry.source === null || (hasExactKeys(entry.source, ["sourceId", "locator"]) && token(entry.source.sourceId) && text(entry.source.locator, 5_000)))) ||
      !Array.isArray(value.classes) || value.classes.length < 1 || value.classes.length > 20 ||
      value.classes.some((entry) => !hasExactKeys(entry, ["id", "name", "definition", "level", "subclass"]) ||
        !token(entry.id) || !text(entry.name, 5_000) || !validDossierDefinition(entry.definition) ||
        !boundedInteger(entry.level, 1, 20) || !(entry.subclass === null || namedCharacterReference(entry.subclass))) ||
      !Array.isArray(value.features) || value.features.length > 1_024 ||
      value.features.some((entry) => !hasExactKeys(entry, ["definition", "grantedBy", "grantKind", "classLevel", "configurationKey", "implementation"]) ||
        !validDossierDefinition(entry.definition) || !validDossierDefinition(entry.grantedBy) || !token(entry.grantKind) ||
        !(entry.classLevel === null || boundedInteger(entry.classLevel, 1, 20)) ||
        !(entry.configurationKey === null || token(entry.configurationKey)) ||
        !hasExactKeys(entry.implementation, ["status", "reason", "entitlementKey", "nextCapabilityId"]) ||
        !["recorded", "executable", "pending"].includes(entry.implementation.status) ||
        !(entry.implementation.reason === null || token(entry.implementation.reason)) ||
        !(entry.implementation.entitlementKey === null || token(entry.implementation.entitlementKey)) ||
        !(entry.implementation.nextCapabilityId === null || token(entry.implementation.nextCapabilityId))) ||
      !hasExactKeys(value.inventory, ["definitions", "contentsDepth", "mayOmitDeeperContents"]) ||
      !Array.isArray(value.inventory.definitions) || value.inventory.definitions.length > 512 ||
      value.inventory.definitions.some((entry) => !validDossierDefinition(entry)) ||
      value.inventory.contentsDepth !== 4 || value.inventory.mayOmitDeeperContents !== true ||
      !validLevelOneRules(value.levelOneRules, actorId) ||
      !Array.isArray(value.definitions) || value.definitions.length > 512 ||
      value.definitions.some((entry) => !validDossierDefinition(entry)) ||
      !hasExactKeys(value.provenance, ["sheetQueryId", "sheetProjectionId", "dossierProjectionId", "definitionCount", "inventoryDepth", "ruleTextPolicy"]) ||
      value.provenance.sheetQueryId !== "dnd2024.query.character-sheet-v2" ||
      value.provenance.sheetProjectionId !== "dnd2024.mechanic.character-sheet-v2.project" ||
      value.provenance.dossierProjectionId !== "dnd2024.mechanic.character-dossier-v1.project" ||
      value.provenance.definitionCount !== value.definitions.length || value.provenance.inventoryDepth !== 4 ||
      value.provenance.ruleTextPolicy !== "canonical-only") return false;
  return value.sheet.origin?.species?.id === value.origin.species.id &&
    value.sheet.origin?.background?.id === value.origin.background.id;
}

/** Reads only the calculated v2 sheet. Full dossier metadata remains a separate resource. */
export async function readCanonicalCharacterSheet({
  fetchImpl, origin, applicationId, stateSpaceId, actorId, perspective,
}) {
  const entityRoot = `/api/applications/${encodeURIComponent(applicationId)}` +
    `/state-spaces/${encodeURIComponent(stateSpaceId)}/entities`;
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
      validate: (value) => validCharacterSheetV2(value, actorId),
    });
    if (result.status !== "ready") {
      if (result.status === "incompatible") return {
        status: "error", data: null, failureCategory: "incompatible-data",
        diagnosticId: canonicalCharacterDiagnosticId(result.response, actorId, "incompatible-data"),
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
      };
    }
    return {
      status: "ready", failureCategory: null,
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
    return {
      status: "error", data: null, failureCategory: "transport",
      diagnosticId: canonicalCharacterDiagnosticId(null, actorId, "transport"),
    };
  }
}

export async function readCanonicalCharacter({ fetchImpl, origin, applicationId, stateSpaceId, actorId, perspective }) {
  const applicationRoot = `/api/applications/${encodeURIComponent(applicationId)}` +
    `/state-spaces/${encodeURIComponent(stateSpaceId)}`;
  const entityRoot = `${applicationRoot}/entities`;
  const headers = { Accept: "application/json" };
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
      validate: (value) => validCharacterDossier(value, actorId) &&
        Array.isArray(value.sheet?.inventory?.items),
    });
    if (result.status !== "ready") {
      if (result.status === "incompatible") return {
        status: "error",
        data: null,
        failureCategory: "incompatible-data",
        diagnosticId: canonicalCharacterDiagnosticId(result.response, actorId, "incompatible-data"),
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
      };
    }
    const projected = result.data;
    const mediaOwners = [...new Set(projected.sheet.inventory.items.flatMap(item => [item.id, item.definition.id]))];
    const inventoryMedia = new Map();
    if (perspective !== "player" && mediaOwners.length > 0 && mediaOwners.length <= 256) {
      try {
        const mediaResponse = await fetchImpl(url(origin, `${applicationRoot}/media-batch`), {
          method: "POST", headers: { "Content-Type": "application/json", Accept: "application/json" },
          cache: "no-store", body: JSON.stringify({ entityIds: mediaOwners, perspective: perspective ?? null }),
        });
        const media = mediaResponse?.ok ? await json(mediaResponse) : null;
        if (media?.applicationId === applicationId && media.stateSpaceId === stateSpaceId &&
            Array.isArray(media.items) && media.items.length <= mediaOwners.length &&
            new Set(media.items.map(item => item.entityId)).size === media.items.length &&
            media.items.every(item => mediaOwners.includes(item.entityId)))
          for (const item of media.items) inventoryMedia.set(item.entityId, projectMediaVisual(item));
      } catch (error) { if (error?.name === "AbortError") throw error; }
    }
    const inventory = projected.sheet.inventory.items.map((item) => {
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
      data: {
      ...projected.sheet,
      inventory: { ...projected.sheet.inventory, items: inventory },
      dossier: {
        origin: projected.origin,
        classes: projected.classes,
        features: projected.features,
        inventory: projected.inventory,
        levelOneRules: projected.levelOneRules,
        definitions: projected.definitions,
        provenance: projected.provenance,
      },
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
      status: "error",
      data: null,
      failureCategory: "transport",
      diagnosticId: canonicalCharacterDiagnosticId(null, actorId, "transport"),
    };
  }
}

function knowledgeEntries(value) {
  const rawEntries = value;
  if (!Array.isArray(rawEntries) || rawEntries.length > 200) {
    return null;
  }
  const entries = rawEntries.map((entry) => {
    const textValue = text(entry?.text, 1_500);
    const stance = token(entry?.stance);
    const presentationKind = token(entry?.presentationKind);
    const subjectId = stance === "familiar" ? null : token(entry?.subject?.id);
    const subjectName = stance === "familiar" ? null : text(entry?.subject?.name, 200);
    const mediaOwnerId = stance === "familiar" ? null : token(entry?.mediaOwnerId);
    return textValue && stance && presentationKind
      ? {
          text: textValue,
          stance,
          presentationKind,
          ...(mediaOwnerId ? { mediaOwnerId } : {}),
          ...(subjectId && subjectName ? { subject: { id: subjectId, name: subjectName } } : {}),
        }
      : null;
  });
  return entries.every(Boolean) ? entries : null;
}

function knowledge(value) {
  const entries = knowledgeEntries(value?.entries);
  if (!entries) return { status: "unavailable", entries: [], locations: [] };
  if (value?.status === "empty" && entries.length === 0) {
    return { status: "empty", entries: [], locations: [] };
  }
  if (value?.status !== "ready" || entries.length === 0) {
    return { status: "unavailable", entries: [], locations: [] };
  }

  // The locations field was added after the base identity-free notebook. A server that has not
  // yet restarted with that addition can still provide safe knowledge entries, but never a place.
  if (value.locations === undefined) return { status: "ready", entries, locations: [] };
  if (!Array.isArray(value.locations) || value.locations.length > 100) {
    return { status: "unavailable", entries: [], locations: [] };
  }
  const locations = value.locations.map((location) => {
    const name = text(location?.name, 160);
    const locationEntries = knowledgeEntries(location?.entries);
    return name && locationEntries && locationEntries.length > 0
      ? { name, entries: locationEntries }
      : null;
  });
  return locations.every(Boolean)
    ? { status: "ready", entries, locations }
    : { status: "unavailable", entries: [], locations: [] };
}

async function attachAuthorizedKnowledgeMedia({
  fetchImpl,
  origin,
  entityRoot,
  projectedKnowledge,
  perspective: _,
  mediaAssetBaseUrl: __,
}) {
  if (projectedKnowledge.status !== "ready") return projectedKnowledge;
  const cache = new Map();
  async function mediaFor(ownerId) {
    if (!cache.has(ownerId)) {
      cache.set(ownerId, readEntityMedia(fetchImpl, origin, entityRoot, ownerId)
        .then((value) => value ? projectMediaVisual(value) : null));
    }
    return cache.get(ownerId);
  }
  async function enrich(entries) {
    return Promise.all(entries.map(async (entry) => {
      const { mediaOwnerId, ...projectedEntry } = entry;
      const ownerId = token(mediaOwnerId);
      if (!ownerId) return projectedEntry;
      const media = await mediaFor(ownerId);
      return media ? { ...projectedEntry, media } : projectedEntry;
    }));
  }
  return {
    ...projectedKnowledge,
    entries: await enrich(projectedKnowledge.entries),
    locations: await Promise.all(projectedKnowledge.locations.map(async (location) => ({
      ...location,
      entries: await enrich(location.entries),
    }))),
  };
}

function chronology(value, expectedPerspective) {
  if (!hasExactKeys(value, ["status", "perspective", "entries"]) ||
      value.perspective !== expectedPerspective ||
      (value.status !== "ready" && value.status !== "empty") ||
      !Array.isArray(value.entries) || value.entries.length > 500) {
    return { status: "unavailable", perspective: expectedPerspective, entries: [] };
  }
  const includeSubjects = expectedPerspective === "dm";
  const entries = value.entries.map((entry) => {
    const expectedKeys = includeSubjects
      ? ["id", "occurredAtMinute", "dateLabel", "precision", "title", "summary", "subjects"]
      : ["id", "occurredAtMinute", "dateLabel", "precision", "title", "summary"];
    if (!hasExactKeys(entry, expectedKeys)) return null;
    const id = token(entry.id);
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
    if (!id || !dateLabel || !precision || !title || !summary || occurredAtMinute === null) return null;
    if (!includeSubjects) {
      return { id, occurredAtMinute, dateLabel, precision, title, summary };
    }
    if (!Array.isArray(entry.subjects) || entry.subjects.length > 10) return null;
    const subjects = entry.subjects.map((subject) => hasExactKeys(subject, ["id", "name"])
      ? { id: token(subject.id), name: text(subject.name, 200) }
      : null);
    if (!subjects.every((subject) => subject?.id && subject?.name) ||
        new Set(subjects.map((subject) => subject.id)).size !== subjects.length) return null;
    return { id, occurredAtMinute, dateLabel, precision, title, summary, subjects };
  });
  if (!entries.every(Boolean) ||
      (value.status === "empty" && entries.length !== 0) ||
      (value.status === "ready" && entries.length === 0) ||
      new Set(entries.map((entry) => entry.id)).size !== entries.length) {
    return { status: "unavailable", perspective: expectedPerspective, entries: [] };
  }
  return { status: value.status, perspective: expectedPerspective, entries };
}

function campaignWorldId(campaignId) {
  const parts = typeof campaignId === "string" ? campaignId.split(".") : [];
  return parts.length >= 3 && parts[0] === "campaign" ? token(parts[1]) : null;
}

function isLocationEntity(item, worldId) {
  const candidateId = typeof item?.entityId === "string" ? item.entityId : (typeof item?.id === "string" ? item.id : null);
  return typeof candidateId === "string" && typeof item?.name === "string" && typeof worldId === "string"
    ? candidateId.startsWith(`location.${worldId}.`) && candidateId.length <= 200 && item.name.length > 0
    : false;
}

async function readRawLocationDirectory({
  fetchImpl,
  origin,
  applicationId,
  stateSpaceId,
  worldId,
}) {
  if (!applicationId || !stateSpaceId || !worldId) return { status: "complete", items: [] };
  const listRoot = `/api/applications/${encodeURIComponent(applicationId)}` +
    `/state-spaces/${encodeURIComponent(stateSpaceId)}/entities`;
  const headers = { Accept: "application/json" };
  const entries = new Map();
  const directory = await readJsonPages({
    fetchImpl, origin, path: listRoot, maximumPages: 1_000, maximumItems: 100_000,
  });
  if (directory.status !== "complete") {
    return unavailableOnFirstPage(directory) ? { status: "complete", items: [] } : directory;
  }

  for (const item of directory.items) {
    if (!isLocationEntity(item, worldId)) continue;
    const locationId = typeof item.entityId === "string" ? item.entityId : item.id;
    const name = text(item.name, 200);
    if (name && locationId && !entries.has(locationId)) entries.set(locationId, name);
  }

  if (entries.size === 0) return { status: "complete", items: [] };

  const locationDirectory = await Promise.all(Array.from(entries.entries()).map(async ([id, name]) => {
    const containmentPath = `/api/applications/${encodeURIComponent(applicationId)}` +
      `/state-spaces/${encodeURIComponent(stateSpaceId)}/entities/${encodeURIComponent(id)}/containment`;
    const componentPath = `/api/applications/${encodeURIComponent(applicationId)}` +
      `/state-spaces/${encodeURIComponent(stateSpaceId)}/entities/${encodeURIComponent(id)}` +
      `/components/${LOCATION_COMPONENT_TYPE_ID}`;
    const anchorPath = `/api/applications/${encodeURIComponent(applicationId)}` +
      `/state-spaces/${encodeURIComponent(stateSpaceId)}/entities/${encodeURIComponent(id)}` +
      `/components/${WORLD_MAP_ANCHOR_COMPONENT_TYPE_ID}`;
    const mediaPath = `/api/applications/${encodeURIComponent(applicationId)}` +
      `/state-spaces/${encodeURIComponent(stateSpaceId)}/entities/${encodeURIComponent(id)}` +
      "/media";
    const [containmentResult, componentResult, anchorResult, mediaResult] = await Promise.allSettled([
      fetchImpl(url(origin, containmentPath), {
        headers: { Accept: "application/json" },
        cache: "no-store",
      }),
      fetchImpl(url(origin, componentPath), {
        headers: { Accept: "application/json" },
        cache: "no-store",
      }),
      fetchImpl(url(origin, anchorPath), {
        headers: { Accept: "application/json" },
        cache: "no-store",
      }),
      fetchImpl(url(origin, mediaPath), {
        headers: { Accept: "application/json" },
        cache: "no-store",
      }),
    ]);
    try {
      const containmentResponse = containmentResult.status === "fulfilled"
        ? containmentResult.value
        : null;
      const componentResponse = componentResult.status === "fulfilled"
        ? componentResult.value
        : null;
      const anchorResponse = anchorResult.status === "fulfilled" ? anchorResult.value : null;
      const mediaResponse = mediaResult.status === "fulfilled" ? mediaResult.value : null;
      const [containmentPayload, componentPayload, anchorPayload, mediaPayload] = await Promise.all([
        containmentResponse?.ok ? json(containmentResponse) : Promise.resolve(null),
        componentResponse?.ok ? json(componentResponse) : Promise.resolve(null),
        anchorResponse?.ok ? json(anchorResponse) : Promise.resolve(null),
        mediaResponse?.ok ? json(mediaResponse) : Promise.resolve(null),
      ]);
      const componentValueJson = componentValue(componentPayload, id, LOCATION_COMPONENT_TYPE_ID);
      const summary = componentValueJson ? text(componentValueJson.summary, 2000) : null;
      const kind = componentValueJson ? text(componentValueJson.kind, 100) : null;
      const discoveredContainerId = containerId(containmentPayload);
      const discoveredContainmentSlot = containmentSlot(containmentPayload);
      const discoveredMapAnchor = anchorPayload ? mapAnchor(anchorPayload, id) : null;
      return {
        id,
        name,
        visibility: componentValueJson ? text(componentValueJson.visibility, 100) : null,
        ...(kind ? { kind } : {}),
        ...(summary ? { summary } : {}),
        ...(discoveredContainerId ? { containerId: discoveredContainerId } : {}),
        ...(discoveredContainmentSlot ? { containmentSlot: discoveredContainmentSlot } : {}),
        ...(discoveredMapAnchor ? { mapAnchor: discoveredMapAnchor } : {}),
        mediaPayload,
      };
    } catch {
      return { id, name, visibility: null, mediaPayload: null };
    }
  }));
  return {
    status: "complete",
    items: locationDirectory
      .filter((entry) => entry && typeof entry.id === "string" && typeof entry.name === "string")
      .sort((left, right) => left.name.localeCompare(right.name)),
  };
}

const LOCATION_DIRECTORY_CACHE_MS = 10_000;
const locationDirectoryCaches = new WeakMap();

async function readLocationDirectory(options) {
  let locationDirectoryCache = locationDirectoryCaches.get(options.fetchImpl);
  if (!locationDirectoryCache) {
    locationDirectoryCache = new Map();
    locationDirectoryCaches.set(options.fetchImpl, locationDirectoryCache);
  }
  const key = [options.origin, options.applicationId, options.stateSpaceId, options.worldId].join("\u0000");
  const now = Date.now();
  let cached = locationDirectoryCache.get(key);
  if (!cached || now - cached.createdAt >= LOCATION_DIRECTORY_CACHE_MS) {
    cached = {
      createdAt: now,
      value: readRawLocationDirectory(options).catch(() => ({
        status: "incomplete", reason: "page-unavailable", items: [],
      })),
    };
    locationDirectoryCache.set(key, cached);
  }
  const rawDirectory = await cached.value;
  if (rawDirectory.status !== "complete") return rawDirectory;
  return {
    status: "complete",
    items: rawDirectory.items.flatMap((entry) => {
      if (options.perspective === "player" && entry.visibility !== "public") return [];
      const selectedMedia = entry.mediaPayload ? mediaVisual(entry.mediaPayload) : null;
      const selectedVisual = selectedMedia?.map ?? null;
      const { visibility: _, mediaPayload: __, ...safeEntry } = entry;
      const { map: ___, ...entityMedia } = selectedMedia ?? {};
      return [{
        ...safeEntry,
        ...(selectedVisual ? { mapVisual: { imageUrl: selectedVisual.imageUrl, alt: selectedVisual.alt } } : {}),
        ...(Object.keys(entityMedia).length > 0 ? { media: entityMedia } : {}),
      }];
    }),
  };
}

function isHoldingEntityId(entityId) {
  return ["holding.", "container.", "chest.", "item.", "equipment.", "weapon."]
    .some((prefix) => entityId.startsWith(prefix));
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

async function readEntityMedia(fetchImpl, origin, entityRoot, entityId, perspective) {
  try {
    const response = await fetchImpl(url(origin,
      `${entityRoot}/${encodeURIComponent(entityId)}/media${perspective ? `?perspective=${perspective}` : ""}`), {
      headers: { Accept: "application/json" }, cache: "no-store",
    });
    return response?.ok ? await json(response) : null;
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
  return hasExactKeys(value, ["status", "summary", "visibility", "mode", "durationMinutes"]) &&
    value.status === "active" && text(value.summary, 1_000) &&
    ["public", "party", "gm"].includes(value.visibility) && value.mode === "on-foot" &&
    Number.isInteger(value.durationMinutes) && value.durationMinutes >= 1 && value.durationMinutes <= 1_440;
}

function validOpenRouteAvailability(value) {
  return hasExactKeys(value, ["status"]) && value.status === "open";
}

function validActiveLocation(value) {
  return hasExactKeys(value, ["kind", "status", "summary", "visibility"]) &&
    ["region", "settlement", "site", "interior"].includes(value.kind) && value.status === "active" &&
    text(value.summary, 1_000) && ["public", "party", "gm"].includes(value.visibility);
}

/**
 * Resolves only exact, active, open, directed on-foot routes admitted by the authorized notebook.
 * Knowledge supplies candidate identity; canonical route/location state independently proves the
 * target and never lets descriptive visibility stand in for Player authorization.
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
}) {
  if (!worldId || !currentLocationId || projectedKnowledge?.status !== "ready") return [];
  const subjectEntries = new Map();
  for (const entry of projectedKnowledge.entries) {
    const subjectId = token(entry?.subject?.id);
    if (!subjectId || entry.stance === "familiar") continue;
    const values = subjectEntries.get(subjectId) ?? [];
    values.push(entry);
    subjectEntries.set(subjectId, values);
  }
  if (subjectEntries.size === 0) return [];

  const locationById = new Map(locationDirectory.map((location) => [location.id, location]));
  const candidates = await Promise.all([...subjectEntries.keys()].map(async (routeId) => {
    const route = await readExactComponent(
      fetchImpl, origin, entityRoot, routeId, WORLD_ROUTE_COMPONENT_TYPE_ID,
    );
    return validActiveRoute(route) ? { routeId, route } : null;
  }));

  const resolved = await Promise.all(candidates.filter(Boolean).map(async ({ routeId, route }) => {
    const [availability, routeWorldId, originId, destinationId] = await Promise.all([
      readExactComponent(
        fetchImpl, origin, entityRoot, routeId, WORLD_ROUTE_AVAILABILITY_COMPONENT_TYPE_ID,
      ),
      readSingleExactRelationshipTarget(
        fetchImpl, origin, entityRoot, routeId, WORLD_ROUTE_RELATIONSHIP_KINDS.world,
      ),
      readSingleExactRelationshipTarget(
        fetchImpl, origin, entityRoot, routeId, WORLD_ROUTE_RELATIONSHIP_KINDS.origin,
      ),
      readSingleExactRelationshipTarget(
        fetchImpl, origin, entityRoot, routeId, WORLD_ROUTE_RELATIONSHIP_KINDS.destination,
      ),
    ]);
    if (!validOpenRouteAvailability(availability) || routeWorldId !== worldId ||
        originId !== currentLocationId || !destinationId || destinationId === originId) return null;
    const destination = locationById.get(destinationId);
    if (!destination || (perspective === "player" && !subjectEntries.has(destinationId))) return null;
    const destinationState = await readExactComponent(
      fetchImpl, origin, entityRoot, destinationId, LOCATION_COMPONENT_TYPE_ID,
    );
    if (!validActiveLocation(destinationState)) return null;
    const admittedDetail = subjectEntries.get(routeId)?.map((entry) => text(entry.text, 1_500))
      .find(Boolean);
    const detail = perspective === "dm" ? route.summary : admittedDetail;
    if (!detail) return null;
    return {
      id: routeId,
      originId,
      destinationId,
      destinationName: destination.name,
      detail,
      mode: "on-foot",
      durationMinutes: route.durationMinutes,
    };
  }));
  return resolved.filter(Boolean).sort((left, right) =>
    left.destinationName.localeCompare(right.destinationName) || left.id.localeCompare(right.id));
}

function validInteraction(value) {
  return hasExactKeys(value, ["kind", "status", "summary"]) && value.kind === "conversation" &&
    value.status === "accepted" && text(value.summary, 1_000);
}

export async function readConversationCurrentScene({
  fetchImpl, origin, entityRoot, conversationId, perspective, authorizedActorIds, mediaAssetBaseUrl,
}) {
  const [conversation, interaction, participantIds, sceneMediaValue] = await Promise.all([
    readNamedEntity(fetchImpl, origin, entityRoot, conversationId),
    readExactComponent(fetchImpl, origin, entityRoot, conversationId, WORLD_INTERACTION_COMPONENT_TYPE_ID),
    readExactRelationshipTargets(
      fetchImpl, origin, entityRoot, conversationId, WORLD_INTERACTION_PARTICIPANT_RELATIONSHIP_KIND,
    ),
    readEntityMedia(fetchImpl, origin, entityRoot, conversationId),
  ]);
  if (!conversation || !validInteraction(interaction) || participantIds === null) return null;
  const visibleIds = perspective === "dm"
    ? participantIds
    : participantIds.filter((id) => authorizedActorIds.has(id));
  const participants = (await Promise.all(visibleIds.map(async (id) => {
    const [participant, mediaValue] = await Promise.all([
      readNamedEntity(fetchImpl, origin, entityRoot, id),
      readEntityMedia(fetchImpl, origin, entityRoot, id),
    ]);
    if (!participant) return null;
    const media = mediaValue ? projectMediaVisual(mediaValue) : null;
    return { ...participant, ...(media?.portrait ? { portrait: media.portrait } : {}) };
  }))).filter(Boolean);
  if (participants.length !== visibleIds.length) return null;
  return {
    status: "ready",
    kind: "conversation",
    ...(() => {
      const media = sceneMediaValue
        ? projectMediaVisual(sceneMediaValue)
        : null;
      return media?.scene ? { scene: media.scene } : {};
    })(),
    conversation: {
      id: conversation.id,
      name: conversation.name,
      participants,
      ...(perspective === "dm" ? { summary: interaction.summary } : {}),
    },
  };
}

function validRound(value, encounterId) {
  return hasExactKeys(value, ["encounter", "number", "status"]) &&
    exactEntityReference(value.encounter)?.entityId === encounterId &&
    Number.isInteger(value.number) && value.number > 0 && value.status === "active";
}

function validTurn(value, encounterId) {
  return hasExactKeys(value, ["encounter", "round", "participant", "ordinal", "status"]) &&
    exactEntityReference(value.encounter)?.entityId === encounterId && exactEntityReference(value.round) &&
    exactEntityReference(value.participant) && Number.isInteger(value.ordinal) && value.ordinal >= 0 &&
    value.status === "active";
}

function validInitiative(value, encounterId) {
  return hasExactKeys(value, ["encounter", "status", "result", "tieBreakOrder"]) &&
    exactEntityReference(value.encounter)?.entityId === encounterId && value.status === "locked" &&
    Number.isInteger(value.result) && Number.isInteger(value.tieBreakOrder) && value.tieBreakOrder >= 0;
}

function validParticipation(value, encounterId, participationId, stateSpaceId) {
  if (!hasExactKeys(value, ["membershipRelationship", "status"]) || value.status !== "active") return false;
  const membership = value.membershipRelationship;
  return hasExactKeys(membership, ["stateSpaceId", "fromEntityId", "toEntityId", "qualifiedKind"]) &&
    membership.stateSpaceId === stateSpaceId && membership.fromEntityId === encounterId &&
    membership.toEntityId === participationId &&
    membership.qualifiedKind === ENCOUNTER_RELATIONSHIP_KINDS.participants;
}

function normalizedTurnBudget(value, turnId) {
  if (!hasExactKeys(value, ["turn", "remaining", "movementSpent", "interactionsUsed"]) ||
      exactEntityReference(value.turn)?.entityId !== turnId ||
      !hasExactKeys(value.remaining, ["actions", "bonusActions", "reactions"]) ||
      ![value.remaining.actions, value.remaining.bonusActions, value.remaining.reactions, value.interactionsUsed]
        .every((count) => Number.isInteger(count) && count >= 0) || !Array.isArray(value.movementSpent)) return null;
  return {
    actions: value.remaining.actions,
    bonusActions: value.remaining.bonusActions,
    reactions: value.remaining.reactions,
  };
}


async function readCombatParticipant({
  fetchImpl, origin, entityRoot, encounterId, participationId, stateSpaceId,
}) {
  const [participation, initiative, actorIds] = await Promise.all([
    readExactComponent(fetchImpl, origin, entityRoot, participationId, ENCOUNTER_PARTICIPATION_COMPONENT_TYPE_ID),
    readExactComponent(fetchImpl, origin, entityRoot, participationId, COMBAT_INITIATIVE_COMPONENT_TYPE_ID),
    readExactRelationshipTargets(
      fetchImpl, origin, entityRoot, participationId, ENCOUNTER_RELATIONSHIP_KINDS.actor,
    ),
  ]);
  if (!validParticipation(participation, encounterId, participationId, stateSpaceId) ||
      !validInitiative(initiative, encounterId) || actorIds?.length !== 1) return null;
  const actor = await readNamedEntity(fetchImpl, origin, entityRoot, actorIds[0]);
  return actor ? {
    participationId,
    actor,
    initiative: initiative.result,
    order: initiative.tieBreakOrder,
  } : null;
}

export async function readCombatCurrentScene({
  fetchImpl, origin, entityRoot, encounterId, stateSpaceId, perspective, authorizedActorIds, campaignId,
  mediaAssetBaseUrl,
}) {
  const boardRead = import("./encounter-board.js")
    .then(({ readEncounterBoard }) => readEncounterBoard({ fetchImpl, origin, entityRoot, encounterId, perspective, campaignId }))
    .catch((error) => {
      if (error?.name === "AbortError") throw error;
      return null;
    });
  const [encounter, definition, projectedBoard, participantIds, activeRoundIds, activeTurnIds, sceneMediaValue] = await Promise.all([
    readNamedEntity(fetchImpl, origin, entityRoot, encounterId),
    readExactComponent(fetchImpl, origin, entityRoot, encounterId, ENCOUNTER_DEFINITION_COMPONENT_TYPE_ID),
    boardRead,
    readExactRelationshipTargets(fetchImpl, origin, entityRoot, encounterId, ENCOUNTER_RELATIONSHIP_KINDS.participants),
    readExactRelationshipTargets(fetchImpl, origin, entityRoot, encounterId, ENCOUNTER_RELATIONSHIP_KINDS.activeRound),
    readExactRelationshipTargets(fetchImpl, origin, entityRoot, encounterId, ENCOUNTER_RELATIONSHIP_KINDS.activeTurn),
    readEntityMedia(fetchImpl, origin, entityRoot, encounterId, perspective),
  ]);
  if (!encounter || !definition || participantIds === null || activeRoundIds === null || activeTurnIds === null ||
      activeRoundIds.length > 1 || activeTurnIds.length > 1) return null;
  const participantRows = await Promise.all(participantIds.map((participationId) => readCombatParticipant({
    fetchImpl, origin, entityRoot, encounterId, participationId, stateSpaceId,
  })));
  if (participantRows.some((row) => row === null)) return null;
  const orderedRows = participantRows.sort((left, right) => left.order - right.order);
  if (orderedRows.some((row, index) => row.order !== index)) return null;

  let round = null;
  if (activeRoundIds.length === 1) {
    const value = await readExactComponent(
      fetchImpl, origin, entityRoot, activeRoundIds[0], ENCOUNTER_ROUND_COMPONENT_TYPE_ID,
    );
    if (!validRound(value, encounterId)) return null;
    round = { id: activeRoundIds[0], number: value.number };
  }
  let turn = null;
  if (activeTurnIds.length === 1) {
    const turnId = activeTurnIds[0];
    const [value, budgetValue] = await Promise.all([
      readExactComponent(fetchImpl, origin, entityRoot, turnId, ENCOUNTER_TURN_COMPONENT_TYPE_ID),
      readExactComponent(fetchImpl, origin, entityRoot, turnId, COMBAT_TURN_BUDGET_COMPONENT_TYPE_ID),
    ]);
    if (!validTurn(value, encounterId) || (round && value.round.entityId !== round.id)) return null;
    const activeRow = orderedRows.find((row) => row.participationId === value.participant.entityId);
    if (!activeRow) return null;
    const budget = normalizedTurnBudget(budgetValue, turnId);
    turn = {
      id: turnId,
      participationId: activeRow.participationId,
      actorId: activeRow.actor.id,
      actorName: activeRow.actor.name,
      ordinal: value.ordinal,
      ...(budget && (perspective === "dm" || authorizedActorIds.has(activeRow.actor.id)) ? { budget } : {}),
    };
  }
  const visibleRows = perspective === "dm"
    ? orderedRows
    : orderedRows.filter((row) => authorizedActorIds.has(row.actor.id));
  const visibleParticipants = await Promise.all(visibleRows.map(async (row) => {
    const mediaValue = await readEntityMedia(fetchImpl, origin, entityRoot, row.actor.id);
    const media = mediaValue ? projectMediaVisual(mediaValue) : null;
    return {
      id: row.actor.id,
      name: row.actor.name,
      initiative: row.initiative,
      active: turn?.participationId === row.participationId,
      ...(media?.portrait ? { portrait: media.portrait } : {}),
    };
  }));
  const sceneMedia = sceneMediaValue
    ? projectMediaVisual(sceneMediaValue)
    : null;
  return {
    status: "ready",
    kind: "combat",
    ...(sceneMedia?.scene ? { scene: sceneMedia.scene } : {}),
    combat: {
      id: encounter.id,
      name: encounter.name,
      participants: visibleParticipants,
      ...(projectedBoard ? { board: projectedBoard } : {}),
      ...(projectedBoard?.backgroundMediaOrder != null && sceneMediaValue?.attachments ? {
        background: projectMediaVisual({ attachments: sceneMediaValue.attachments.filter((entry) =>
          entry.role === "map" && entry.order === projectedBoard.backgroundMediaOrder) })?.map,
      } : {}),
      ...(round ? { round } : {}),
      ...(turn && (perspective === "dm" || authorizedActorIds.has(turn.actorId)) ? { turn } : {}),
    },
  };
}

async function readWorldDirectory({
  fetchImpl,
  origin,
  applicationId,
  stateSpaceId,
  worldId,
  locationDirectory,
  perspective,
  mediaAssetBaseUrl,
  includeFactions = true,
}) {
  const empty = { people: [], factions: [], holdings: [] };
  const incomplete = { ...empty, incomplete: true };
  if (!applicationId || !stateSpaceId || !worldId || locationDirectory.length === 0) return empty;
  const listRoot = `/api/applications/${encodeURIComponent(applicationId)}` +
    `/state-spaces/${encodeURIComponent(stateSpaceId)}/entities`;
  const relationshipRoot = listRoot.replace(/\/entities$/u, "/relationships");
  const headers = { Accept: "application/json" };
  const entities = new Map();
  const directory = await readJsonPages({ fetchImpl, origin, path: listRoot });
  if (directory.status !== "complete") return incomplete;
  for (const item of directory.items) {
    const id = token(typeof item?.entityId === "string" ? item.entityId : item?.id);
    const name = text(item?.name, 200);
    // Catalog definitions must not exhaust the directory's retained-record budget
    // before later pages containing actual world factions, people and holdings.
    const relevant = id && (id === `faction.${worldId}` || id.startsWith(`faction.${worldId}.`) ||
      id.startsWith("actor.") || id.startsWith("creature.") || isHoldingEntityId(id));
    if (relevant && name) {
      if (!entities.has(id) && entities.size >= 1_000) return incomplete;
      entities.set(id, { id, name });
    }
  }

  const locationIds = new Set(locationDirectory.map((location) => location.id));
  const containedResults = await Promise.all(locationDirectory.map(async (location) => {
    const path = `${listRoot.replace(/\/entities$/u, "/containments")}` +
      `?containerEntityId=${encodeURIComponent(location.id)}`;
    const pages = await readJsonPages({
      fetchImpl, origin, path, maximumPages: 10, maximumItems: 1_000,
    });
    if (pages.status !== "complete") return unavailableOnFirstPage(pages) ? [] : null;
    const values = [];
    for (const item of pages.items) {
      const containedEntityId = token(item?.containedEntityId);
      const containerEntityId = token(item?.containerEntityId);
      if (!containedEntityId || containerEntityId !== location.id) return null;
      if (entities.has(containedEntityId)) {
        values.push({ entityId: containedEntityId, locationId: location.id });
      }
    }
    return values;
  }));
  if (containedResults.some((result) => result === null)) return incomplete;
  const contained = containedResults.flat();

  const people = await Promise.all(contained.flatMap((entry) => {
    const isActor = entry.entityId.startsWith("actor.");
    const isCreature = entry.entityId.startsWith("creature.");
    if (!isActor && !isCreature) return [];
    const record = entities.get(entry.entityId);
    return [Promise.resolve().then(async () => {
      let motive = null;
      let media = null;
      try {
        const [motiveResponse, mediaResponse] = await Promise.all([
          fetchImpl(url(origin,
            `${listRoot}/${encodeURIComponent(entry.entityId)}/components/${WORLD_MOTIVE_COMPONENT_TYPE_ID}`),
          { headers, cache: "no-store" }),
          fetchImpl(url(origin,
            `${listRoot}/${encodeURIComponent(entry.entityId)}/media`),
          { headers, cache: "no-store" }),
        ]);
        const [motivePayload, mediaPayload] = await Promise.all([
          motiveResponse?.ok ? json(motiveResponse) : Promise.resolve(null),
          mediaResponse?.ok ? json(mediaResponse) : Promise.resolve(null),
        ]);
        motive = worldMotive(componentValue(motivePayload, entry.entityId, WORLD_MOTIVE_COMPONENT_TYPE_ID));
        media = mediaPayload ? mediaVisual(mediaPayload) : null;
      } catch {
        motive = null;
        media = null;
      }
      return {
        id: entry.entityId,
        name: record.name,
        kind: isCreature ? "Creature" : "NPC",
        locationId: entry.locationId,
        ...(media ? { media } : {}),
        ...(motive ? { motive } : {}),
      };
    })];
  }));

  const holdings = contained.flatMap((entry) => {
    if (locationIds.has(entry.entityId) || !isHoldingEntityId(entry.entityId)) return [];
    const record = entities.get(entry.entityId);
    return record ? [{ ...record, locationId: entry.locationId, kind: entry.entityId.split(".")[0] }] : [];
  });

  const factionCandidates = includeFactions ? Array.from(entities.values()).filter((entry) =>
    entry.id === `faction.${worldId}` || entry.id.startsWith(`faction.${worldId}.`)) : [];
  const factionResults = await Promise.all(factionCandidates.map(async (entry) => {
    const componentPath = `${listRoot}/${encodeURIComponent(entry.id)}/components/${WORLD_FACTION_COMPONENT_TYPE_ID}`;
    try {
      const [componentResponse, ...relationshipTargets] = await Promise.all([
        fetchImpl(url(origin, componentPath), { headers, cache: "no-store" }),
        ...Object.values(WORLD_FACTION_RELATIONSHIP_KINDS).map((kind) =>
          readExactRelationshipTargets(fetchImpl, origin, listRoot, entry.id, kind,
            { unavailableFirstPageIsEmpty: true })),
      ]);
      if (!componentResponse?.ok) return null;
      const componentPayload = await json(componentResponse);
      const record = worldFaction(componentValue(componentPayload, entry.id, WORLD_FACTION_COMPONENT_TYPE_ID));
      if (!record || record.status !== "active") return null;
      if (relationshipTargets.some((targets) => targets === null)) return { incomplete: true };
      const byKind = Object.fromEntries(Object.entries(WORLD_FACTION_RELATIONSHIP_KINDS).map(
        ([key], index) => [key, relationshipTargets[index]],
      ));
      return {
        ...entry,
        ...record,
        memberIds: byKind.members,
        territoryIds: [...new Set([...byKind.controls, ...byKind.territories])],
        alliedIds: byKind.allies,
        opposedIds: byKind.opponents,
      };
    } catch {
      return null;
    }
  }));
  if (factionResults.some((result) => result?.incomplete)) return incomplete;
  const factions = factionResults.filter(Boolean);

  return {
    people: people.sort((left, right) => left.name.localeCompare(right.name) || left.id.localeCompare(right.id)),
    factions: factions.sort((left, right) => left.name.localeCompare(right.name) || left.id.localeCompare(right.id)),
    holdings: holdings.sort((left, right) => left.name.localeCompare(right.name) || left.id.localeCompare(right.id)),
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
    return unavailable("The configured game server could not be reached.");
  }

  const context = await json(response);
  if (response.status === 403 || context?.status === "denied") {
    return denied("The game server did not authorize a campaign for this local table.");
  }
  if (!response.ok) return unavailable("The game server audience binding is unavailable.");

  const binding = audience(context);
  if (!binding) return unavailable("The game server returned an invalid audience binding.");
  const hasBoundActor = binding.status === "bound" && binding.role === "actor";
  // A development preference or requested perspective can never promote a server-bound actor.
  const serverRole = binding;
  const isGameMaster = serverRole.role === "game-master";
  const contextAudience = isGameMaster
    ? {
        seat: "dm",
        perspective: normalizePerspective(normalizedRequestedPerspective),
        allowedPerspectives: ["dm", "player"],
      }
    : { seat: "player", perspective: "player", allowedPerspectives: ["player"] };
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
        endpointEntityId: shouldReadBoundActor ? binding.actorId : selectedCampaignId,
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
  const actorEntity = isGameMaster
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
    : isGameMaster ? registeredCampaign.party.map((entry) => ({
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
      name: registeredCampaign.title,
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


async function resolveCurrentSituation({
  fetchImpl, origin, root, selectedCampaignId, binding, serverRole, contextAudience,
  currentSceneComponentResponse, currentSceneComponent, playSessionResponse, playSessionEnvelope,
  locationDirectory, currentLocationId, party, selectedContext, projectedKnowledge, mediaAssetBaseUrl,
}) {
  const authorizedLocationIds = locationDirectory.map((location) => location.id);
  const sceneComponentWasReturned = currentSceneComponentResponse?.ok === true;
  const sceneRecord = sceneComponentWasReturned
    ? resolveCurrentSceneRecord(
      componentValue(currentSceneComponent, selectedCampaignId, CAMPAIGN_CURRENT_SCENE_COMPONENT_TYPE_ID),
      authorizedLocationIds,
    )
    : null;
  const recordedSituation = playSessionResponse?.ok === true
    ? resolveRecordedPlaySituation(playSessionEnvelope, authorizedLocationIds)
    : null;
  let currentSituation;
  if (sceneComponentWasReturned && (!sceneRecord ||
      (serverRole.role === "actor" && currentLocationId !== sceneRecord.locationId))) {
    currentSituation = {
      status: "unavailable",
      message: "The recorded current scene is unavailable to this seat.",
    };
  } else if (sceneRecord) {
    currentLocationId = sceneRecord.locationId;
    const authorizedActorIds = new Set([
      ...(serverRole.role === "actor" ? [binding.actorId] : []),
      ...(party ?? []).map((member) => member.id),
    ]);
    if (sceneRecord.kind === "combat") {
      const resolved = await readCombatCurrentScene({
        fetchImpl,
        origin,
        entityRoot: root,
        encounterId: sceneRecord.encounterId,
        campaignId: selectedCampaignId,
        stateSpaceId: binding.stateSpaceId,
        perspective: contextAudience.perspective,
        authorizedActorIds,
        mediaAssetBaseUrl,
      });
      currentSituation = resolved
        ? { ...resolved, locationId: sceneRecord.locationId }
        : { status: "unavailable", locationId: sceneRecord.locationId,
          message: "The current encounter could not be read safely." };
    } else if (sceneRecord.kind === "conversation") {
      const resolved = await readConversationCurrentScene({
        fetchImpl,
        origin,
        entityRoot: root,
        conversationId: sceneRecord.conversationId,
        perspective: contextAudience.perspective,
        authorizedActorIds,
        mediaAssetBaseUrl,
      });
      currentSituation = resolved
        ? { ...resolved, locationId: sceneRecord.locationId }
        : { status: "unavailable", locationId: sceneRecord.locationId,
          message: "The current conversation could not be read safely." };
    } else {
      currentSituation = { status: "ready", kind: "exploration", locationId: sceneRecord.locationId };
    }
  } else if (recordedSituation) {
    currentSituation = recordedSituation;
  } else if (serverRole.role === "actor" && currentLocationId) {
    currentSituation = { status: "ready", kind: "exploration", locationId: currentLocationId };
  } else {
    currentSituation = {
      status: "unavailable",
      message: "No authoritative current scene has been recorded for this campaign.",
    };
  }
  if (sceneRecord && currentSituation.status === "ready") {
    const affordanceRecord = await readExactComponent(
      fetchImpl,
      origin,
      root,
      selectedCampaignId,
      CAMPAIGN_SCENE_AFFORDANCES_COMPONENT_TYPE_ID,
    );
    const affordances = resolveSceneAffordancesRecord(
      affordanceRecord,
      sceneRecord,
      contextAudience.perspective,
    );
    if (affordances !== null) currentSituation = { ...currentSituation, affordances };
  }
  const knownRoutes = currentSituation.status === "ready" && currentSituation.kind === "exploration"
    ? await readKnownOpenRoutes({
      fetchImpl,
      origin,
      entityRoot: root,
      worldId: selectedContext.selectedWorldId,
      currentLocationId: currentSituation.locationId,
      perspective: contextAudience.perspective,
      projectedKnowledge,
      locationDirectory,
    })
    : [];
  return { currentSituation, currentLocationId, knownRoutes };
}


function registeredCampaignContext(data, projection, campaignId) {
  if (!hasExactKeys(data, ["version", "campaignId", "worldId", "campaign", "world"]) ||
      data.version !== 1 || token(data.campaignId) !== campaignId) return null;
  const worldId = token(data.worldId);
  const campaign = hasExactKeys(data.campaign, ["id", "name"])
    ? { id: token(data.campaign.id), name: text(data.campaign.name, 400) }
    : null;
  const world = hasExactKeys(data.world, ["id", "name"])
    ? { id: token(data.world.id), name: text(data.world.name, 400) }
    : null;
  return worldId && campaign?.id === campaignId && campaign.name && world?.id === worldId && world.name
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
  const parameters = new URLSearchParams({ perspective, campaignId });
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
  if (!hasExactKeys(data, ["version", "campaignId", "chapters", "arcs", "sessions"]) ||
      data.version !== 1 || token(data.campaignId) !== campaignId ||
      !Array.isArray(data.chapters) || data.chapters.length > 100 ||
      !Array.isArray(data.arcs) || data.arcs.length > 100 ||
      !Array.isArray(data.sessions) || data.sessions.length > 100 ||
      (perspective !== "dm" && data.sessions.length !== 0)) return null;
  const includeGmContext = perspective === "dm";
  const chapters = data.chapters.map((item) => {
    const optional = ["closingSummary", ...(includeGmContext ? ["gmContext"] : [])]
      .filter((key) => Object.hasOwn(item ?? {}, key));
    if (!hasExactKeys(item, ["id", "name", "status", "title", "partyQuestion", ...optional]) ||
        (!includeGmContext && Object.hasOwn(item, "gmContext"))) return null;
    const id = token(item.id);
    const name = text(item.name, 400);
    const record = campaignChapter(item, includeGmContext);
    return id && name && record ? { id, name, ...record } : null;
  });
  const arcs = data.arcs.map((item) => {
    const optional = ["closingSummary", ...(includeGmContext ? ["gmContext"] : [])]
      .filter((key) => Object.hasOwn(item ?? {}, key));
    if (!hasExactKeys(item, ["id", "name", "status", "title", "partyStake", ...optional]) ||
        (!includeGmContext && Object.hasOwn(item, "gmContext"))) return null;
    const id = token(item.id);
    const name = text(item.name, 400);
    const record = campaignArc(item, includeGmContext);
    return id && name && record ? { id, name, ...record } : null;
  });
  const sessions = data.sessions.map((item) => {
    const optional = Object.hasOwn(item ?? {}, "recap") ? ["recap"] : [];
    if (!hasExactKeys(item, ["id", "name", "status", "ordinal", ...optional])) return null;
    const id = token(item.id);
    const name = text(item.name, 400);
    const record = campaignSession(item);
    const recap = item.recap === undefined ? null : campaignSessionRecap(item.recap);
    if (!id || !name || !record || (item.recap !== undefined && !recap) ||
        (record.status === "ended" && !recap)) return null;
    return { id, name, ...record, ...(recap ? { recap } : {}) };
  });
  const all = [...chapters, ...arcs, ...sessions];
  if (all.some((item) => item === null) || new Set(all.map((item) => item.id)).size !== all.length)
    return null;
  return { version: 1, campaignId, chapters, arcs, sessions, projection };
}

/** Reads the bounded Campaign chapter, arc, and session read model. */
export async function readRegisteredCampaignDetails({
  fetchImpl = fetch, origin, applicationId, stateSpaceId, campaignId, perspective,
}) {
  const entityRoot = `/api/applications/${encodeURIComponent(applicationId)}` +
    `/state-spaces/${encodeURIComponent(stateSpaceId)}/entities`;
  const parameters = new URLSearchParams({ perspective, campaignId });
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
  if (!hasExactKeys(data, ["campaignTitle", "visits", "totalCount", "complete", "nextCursor"]) ||
      !text(data.campaignTitle, 160) || !Array.isArray(data.visits) || data.visits.length > 25 ||
      !Number.isInteger(data.totalCount) || data.totalCount < data.visits.length || data.totalCount > 256 ||
      typeof data.complete !== "boolean" || !(data.nextCursor === null || token(data.nextCursor)) ||
      data.complete !== (data.nextCursor === null)) return null;
  const visits = data.visits.map((item) => {
    const optional = Object.hasOwn(item ?? {}, "gmContext") ? ["gmContext"] : [];
    if (!hasExactKeys(item, ["id", "name", "firstVisitedMinute", "lastVisitedMinute", "visitCount",
      "status", "summary", "memory", "locations", ...optional]) ||
      !Array.isArray(item.locations) || item.locations.length !== 1 ||
      !hasExactKeys(item.locations[0], ["id", "name"])) return null;
    const id = token(item.id);
    const name = text(item.name, 400);
    const locationId = token(item.locations[0].id);
    const locationName = text(item.locations[0].name, 400);
    const record = campaignLocationVisit(item, true);
    return id && name && locationId && locationName && record
      ? { id, name, ...record, locationId, locationName }
      : null;
  });
  if (visits.some((item) => item === null) || new Set(visits.map((item) => item.id)).size !== visits.length)
    return null;
  return {
    visits,
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
  const parameters = new URLSearchParams({ perspective: "dm", campaignId, limit: "25" });
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

function registeredWorldCampaignPage(data, projection, worldId) {
  if (!hasExactKeys(data, ["worldSummary", "selectedWorld", "campaigns", "totalCount", "complete", "nextCursor"]) ||
      !text(data.worldSummary, 1_000) || !hasExactKeys(data.selectedWorld, ["id", "name"]) ||
      token(data.selectedWorld.id) !== worldId || !text(data.selectedWorld.name, 400) ||
      !Array.isArray(data.campaigns) || data.campaigns.length > 25 ||
      !Number.isInteger(data.totalCount) || data.totalCount < data.campaigns.length || data.totalCount > 256 ||
      typeof data.complete !== "boolean" || !(data.nextCursor === null || token(data.nextCursor)) ||
      data.complete !== (data.nextCursor === null)) return null;
  const campaigns = data.campaigns.map((item) => {
    if (!hasExactKeys(item, ["id", "name", "status", "title", "premise", "partyGoals",
      "toneAndBoundaries", "rulesetScope", "creationMethod", "reviewFingerprint"]) ||
      item.status !== "active" || item.rulesetScope !== "dnd2024" || item.creationMethod !== "manual" ||
      !/^[a-f0-9]{64}$/u.test(item.reviewFingerprint)) return null;
    const id = token(item.id);
    const name = text(item.name, 400);
    return id && name && text(item.title, 160) && text(item.premise, 1_000) &&
      textList(item.partyGoals, 3, 500).length > 0 &&
      textList(item.toneAndBoundaries, 8, 300).length > 0 ? { id, name } : null;
  });
  if (campaigns.some((item) => item === null) ||
      new Set(campaigns.map((item) => item.id)).size !== campaigns.length) return null;
  return {
    world: { id: worldId, name: data.selectedWorld.name }, campaigns,
    totalCount: data.totalCount, complete: data.complete, nextCursor: data.nextCursor,
    sourceRevisionFingerprint: projection?.sourceRevisionFingerprint ?? null, projection,
  };
}

export async function readRegisteredWorldCampaignPage({
  fetchImpl = fetch, origin, applicationId, stateSpaceId, campaignId, worldId, cursor = null,
  expectedSourceRevision = null,
}) {
  const entityRoot = `/api/applications/${encodeURIComponent(applicationId)}` +
    `/state-spaces/${encodeURIComponent(stateSpaceId)}/entities`;
  const parameters = new URLSearchParams({ perspective: "dm", campaignId, worldId, limit: "25" });
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
    validate: (value) => registeredWorldCampaignPage(value, null, worldId) !== null,
  });
  return result.status === "ready"
    ? registeredWorldCampaignPage(result.data, result.evidence, worldId)
    : null;
}

async function readAllRegisteredPages(readPage, itemKey) {
  const items = [];
  const seenIds = new Set();
  const seenCursors = new Set();
  let cursor = null;
  let expectedSourceRevision = null;
  let expectedTotalCount = null;
  let identity = null;
  for (let pageNumber = 0; pageNumber < 11; pageNumber += 1) {
    const page = await readPage(cursor, expectedSourceRevision);
    if (!page || (expectedTotalCount !== null && page.totalCount !== expectedTotalCount)) return null;
    expectedTotalCount = page.totalCount;
    expectedSourceRevision ??= page.sourceRevisionFingerprint;
    if (identity && page.world && (identity.id !== page.world.id || identity.name !== page.world.name)) return null;
    identity ??= page.world ?? null;
    for (const item of page[itemKey]) {
      if (seenIds.has(item.id) || items.length >= 256) return null;
      seenIds.add(item.id);
      items.push(item);
    }
    if (page.nextCursor === null) {
      return items.length === expectedTotalCount ? { items, identity, sourceRevisionFingerprint: expectedSourceRevision } : null;
    }
    if (seenCursors.has(page.nextCursor)) return null;
    seenCursors.add(page.nextCursor);
    cursor = page.nextCursor;
  }
  return null;
}

function registeredFactionDirectoryPage(data, projection) {
  if (!hasExactKeys(data, ["worldSummary", "items", "totalCount", "complete", "nextCursor"]) ||
      !text(data.worldSummary, 1_000) ||
      !Array.isArray(data.items) || data.items.length > 25 || !Number.isInteger(data.totalCount) ||
      data.totalCount < data.items.length || data.totalCount > 100 || typeof data.complete !== "boolean" ||
      !(data.nextCursor === null || token(data.nextCursor)) || data.complete !== (data.nextCursor === null)) return null;
  const factions = data.items.map((item) => {
    const id = token(item?.id);
    const name = text(item?.name, 400);
    const record = worldFaction(item);
    const references = (value) => {
      if (!Array.isArray(value) || value.length > 10) return null;
      const result = value.map((entry) => hasExactKeys(entry, ["id", "name"])
        ? { id: token(entry.id), name: text(entry.name, 400) } : null);
      return result.some((entry) => !entry?.id || !entry?.name) ||
        new Set(result.map((entry) => entry.id)).size !== result.length ? null : result;
    };
    const members = references(item?.members);
    const controlledSites = references(item?.controlledSites);
    const territories = references(item?.territories);
    const allies = references(item?.allies);
    const opponents = references(item?.opponents);
    if (!members || !controlledSites || !territories || !allies || !opponents) return null;
    const territoryReferences = [...controlledSites, ...territories].filter((entry, index, values) =>
      values.findIndex((candidate) => candidate.id === entry.id) === index);
    return id && name && record ? {
      id, name, ...record,
      memberIds: members.map((entry) => entry.id),
      territoryIds: territoryReferences.map((entry) => entry.id),
      alliedIds: allies.map((entry) => entry.id),
      opposedIds: opponents.map((entry) => entry.id),
      memberReferences: members,
      territoryReferences,
      alliedReferences: allies,
      opposedReferences: opponents,
    } : null;
  });
  if (factions.some((item) => item === null) || new Set(factions.map((item) => item.id)).size !== factions.length)
    return null;
  return {
    factions,
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
export async function readDeferredCampaignDetails({ fetchImpl = fetch, origin, source }) {
  const options = {
    fetchImpl, origin, applicationId: source.applicationId, stateSpaceId: source.stateSpaceId,
    campaignId: source.campaign.id, perspective: source.audience.perspective ?? "player",
  };
  const [details, visitPages] = await Promise.all([
    readRegisteredCampaignDetails(options),
    options.perspective === "dm"
      ? readAllRegisteredPages(
        (cursor, expectedSourceRevision) => readRegisteredCampaignVisitPage({
          ...options, cursor, expectedSourceRevision,
        }),
        "visits",
      )
      : Promise.resolve({ items: [] }),
  ]);
  if (!details || !visitPages) throw new Error("The campaign details are incomplete.");
  if (new Set(visitPages.items.map((visit) => visit.locationId)).size !== visitPages.items.length)
    throw new Error("The campaign location visits are ambiguous.");
  return {
    chapters: details.chapters,
    arcs: details.arcs,
    sessions: details.sessions,
    visits: visitPages.items,
  };
}

/**
 * Completes only the requested deferred view. The existing private endpoints remain the
 * authorization boundary; no ambient DM knowledge or media is requested for Player preview.
 * Legacy directory adapters remain bounded and follow every continuation. They are not a
 * claim that the complete-workload request budget has been met.
 * @param {{fetchImpl?: typeof fetch, origin: string, source: any, section: string}} options
 * @returns {Promise<any>}
 */
export async function readDeferredHubSection({ fetchImpl = fetch, origin, source, section }) {
  if (!["context", "history", "lore", "locations", "people", "current"].includes(section))
    throw new Error("Unknown deferred view.");
  let failure = null;
  let requests = 0;
  const preview = source.audience.seat === "dm" && source.audience.perspective === "player";
  const scope = createHubReadScope(async (input, init) => {
    const target = new URL(String(input));
    if (preview && target.pathname.endsWith("/media")) return new Response(null, { status: 404 });
    if (++requests > 2_000) {
      failure = "This view exceeds its bounded read budget.";
      throw new Error(failure);
    }
    try {
      const response = await fetchImpl(input, init);
      // A missing optional component is legitimate; transport failures and denied directories
      // must never be converted by a legacy adapter into a credible empty collection.
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
  const root = `/api/applications/${encodeURIComponent(applicationId)}` +
    `/state-spaces/${encodeURIComponent(stateSpaceId)}/entities`;
  const options = {
    fetchImpl: read, origin, applicationId, stateSpaceId, perspective,
    worldId: campaignWorldId(campaignId), mediaAssetBaseUrl: "/ui/dnd2024-play/assets/",
  };
  const patch = {};
  if (section === "context") {
    if (source.audience.seat !== "dm") {
      patch.contextSelection = source.contextSelection;
    } else {
      const directory = await readAllRegisteredPages(
        (cursor, expectedSourceRevision) => readRegisteredWorldCampaignPage({
          fetchImpl: read,
          origin,
          applicationId,
          stateSpaceId,
          campaignId,
          worldId: source.contextSelection.selectedWorldId,
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
        worlds: [{ ...directory.identity, campaigns: directory.items }],
      };
    }
  } else if (section === "history" || section === "lore") {
    if (section === "lore" && preview)
      throw new Error("Campaign knowledge is unavailable in Player preview; an Actor binding is required.");
    const endpoint = section === "history" ? "chronology" : "knowledge";
    const response = await read(url(origin, `/api/applications/${encodeURIComponent(applicationId)}` +
      `/campaigns/${encodeURIComponent(campaignId)}/${endpoint}?perspective=${perspective}`),
      { headers: { Accept: "application/json" }, cache: "no-store" });
    const result = response.ok ? (section === "history"
      ? chronology(await json(response), perspective) : knowledge(await json(response))) : null;
    if (!result || result.status === "unavailable") throw new Error(`The ${section} view is unavailable.`);
    if (section === "history") patch.chronology = result;
    else patch.knowledge = await attachAuthorizedKnowledgeMedia({ ...options, entityRoot: root,
      projectedKnowledge: result });
  } else {
    let locations = source.locationDirectory;
    if (!Array.isArray(locations)) {
      const result = await readLocationDirectory(options);
      if (result.status !== "complete") throw new Error("The location directory is incomplete.");
      locations = result.items;
      patch.locationDirectory = locations;
      patch.locationDirectoryAudience = perspective;
    }
    if (section === "people") {
      if (source.audience.seat === "dm" && perspective === "dm") {
        const directory = await readWorldDirectory({ ...options, locationDirectory: locations, includeFactions: false });
        if (directory.incomplete) throw new Error("The people directory is incomplete.");
        patch.worldDirectory = { ...directory, factions: source.worldDirectory?.factions ?? [] };
      } else {
        // Public people are derived only from this seat's knowledge, never from the DM directory.
        if (preview) throw new Error("The people directory is unavailable in Player preview; an Actor binding is required.");
        Object.assign(patch, await readDeferredHubSection({ fetchImpl: read, origin, source, section: "lore" }));
      }
    }
    if (section === "current") {
      if (!preview && source.knowledge.status === "unavailable") {
        const notebook = await read(url(origin, `/api/applications/${encodeURIComponent(applicationId)}` +
          `/campaigns/${encodeURIComponent(campaignId)}/knowledge`),
          { headers: { Accept: "application/json" }, cache: "no-store" });
        if (notebook.ok) {
          const projected = knowledge(await json(notebook));
          if (projected.status === "unavailable") throw new Error("Current scene knowledge is malformed.");
          patch.knowledge = await attachAuthorizedKnowledgeMedia({ ...options, entityRoot: root,
            projectedKnowledge: projected });
        }
      }
      const readOptional = (path) => read(url(origin, path), {
        headers: { Accept: "application/json" }, cache: "no-store",
      });
      const [sceneResponse, sessionResponse, presenceResponse] = await Promise.all([
        readOptional(`${root}/${encodeURIComponent(campaignId)}/components/${CAMPAIGN_CURRENT_SCENE_COMPONENT_TYPE_ID}`),
        readOptional(`/api/applications/${encodeURIComponent(applicationId)}/state-spaces/${encodeURIComponent(stateSpaceId)}` +
          `/play/sessions/${encodeURIComponent(campaignId)}`),
        source.audience.seat === "player"
          ? readOptional(`${root}/${encodeURIComponent(source.actor.id)}/containment`) : null,
      ]);
      const present = presenceResponse?.ok ? resolvePresenceLocation(await json(presenceResponse),
        source.actor.id, locations.map((location) => location.id)) : null;
      Object.assign(patch, await resolveCurrentSituation({
        ...options, root, selectedCampaignId: campaignId, binding: { actorId: source.actor.id },
        serverRole: { role: source.audience.seat === "dm" ? "game-master" : "actor" },
        contextAudience: source.audience, currentSceneComponentResponse: sceneResponse,
        currentSceneComponent: await json(sceneResponse), playSessionResponse: sessionResponse,
        playSessionEnvelope: await json(sessionResponse), locationDirectory: locations,
        currentLocationId: present, party: source.party, selectedContext: source.contextSelection,
        projectedKnowledge: patch.knowledge ?? source.knowledge,
      }));
    }
  }
  if (failure || scope.failure) throw new Error(failure ?? scope.failure);
  return patch;
}
