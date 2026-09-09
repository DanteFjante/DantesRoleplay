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
import { contract as inventoryContainerContract } from "./inventory-container-contract.js";
import { contract as inventoryWalletContract } from "./inventory-wallet-contract.js";
import { contract as factionDirectoryContract } from "./faction-directory-contract.js";
import { contract as worldLocationScopeContract } from "./world-location-scope-contract.js";
import { contract as worldLocationScopePageContract } from "./world-location-scope-page-contract.js";
import { contract as worldPeopleHoldingsContract } from "./world-people-holdings-contract.js";
import { contract as worldPeopleHoldingsPageContract } from "./world-people-holdings-page-contract.js";
import { contract as currentSceneContract } from "./current-scene-contract.js";
import { contract as campaignResumeContract } from "./campaign-resume-contract.js";

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

function projectLocationMediaBatch(value, applicationId, stateSpaceId, recordIds) {
  const allowed = new Set(recordIds);
  if (value?.applicationId !== applicationId || value.stateSpaceId !== stateSpaceId ||
      !Array.isArray(value.items) || value.items.length > allowed.size ||
      new Set(value.items.map((item) => item?.entityId)).size !== value.items.length ||
      !value.items.every((item) => allowed.has(item?.entityId))) return null;
  const projected = new Map();
  for (const item of value.items) {
    if (!hasExactKeys(item, ["entityId", "attachments"]) || !Array.isArray(item.attachments)) return null;
    const visual = projectMediaVisual(item);
    if (!visual && item.attachments.length !== 0) return null;
    if (visual) projected.set(item.entityId, visual);
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

function validInventoryContainer(value, actorId) {
  if (!hasExactKeys(value, ["version", "container", "state", "reasons", "items", "limits"]) ||
      value.version !== 2 || !namedCharacterReference(value.container) || value.container.id !== actorId ||
      !["ready", "partial"].includes(value.state) || !Array.isArray(value.reasons) ||
      value.reasons.length > 1 || new Set(value.reasons).size !== value.reasons.length ||
      value.reasons.some((reason) => reason !== "unclassified-content") || !Array.isArray(value.items) ||
      value.items.length > 200 ||
      !hasExactKeys(value.limits, ["contentsDepth", "itemCount", "directComplete", "recursiveComplete"]) ||
      value.limits.contentsDepth !== 1 || value.limits.itemCount !== 200 ||
      value.limits.directComplete !== true || value.limits.recursiveComplete !== false ||
      (value.state === "ready") !== (value.reasons.length === 0)) return false;
  const ids = new Set();
  const positions = new Set();
  for (const item of value.items) {
    if (!hasExactKeys(item, ["id", "name", "definition", "quantity", "slot", "order", "equipmentSlots", "classification"]) ||
        !token(item.id) || !text(item.name, 400) ||
        !(item.definition === null || namedCharacterReference(item.definition)) ||
        !(item.quantity === null || boundedInteger(item.quantity, 1)) ||
        typeof item.slot !== "string" || item.slot.length > 200 || !boundedInteger(item.order, 0, 199) ||
        !namedCharacterReferences(item.equipmentSlots, 32) ||
        !["item", "unclassified"].includes(item.classification) || ids.has(item.id) ||
        item.id === actorId ||
        (item.classification === "item") !== (item.definition !== null && item.quantity !== null) ||
        (item.classification === "unclassified" && item.equipmentSlots.length > 0)) return false;
    ids.add(item.id);
    if (positions.has(item.order)) return false;
    positions.add(item.order);
  }
  return value.reasons.includes("unclassified-content") ===
    value.items.some((item) => item.classification === "unclassified");
}

function validInventoryWallet(value, actorId) {
  if (!hasExactKeys(value, ["version", "owner", "state", "reasons", "wallet", "limits"]) ||
      value.version !== 1 || !namedCharacterReference(value.owner) || value.owner.id !== actorId ||
      !["ready", "partial"].includes(value.state) || !Array.isArray(value.reasons) ||
      value.reasons.length > 1 || value.reasons.some((reason) => reason !== "depth-limit") ||
      !validCharacterWallet(value.wallet) || !hasExactKeys(value.limits, ["contentsDepth", "complete"]) ||
      value.limits.contentsDepth !== 4 || typeof value.limits.complete !== "boolean") return false;
  return (value.state === "ready") === value.limits.complete &&
    value.limits.complete === (value.reasons.length === 0);
}

function validWorldLocationScope(value, scopeId) {
  if (!hasExactKeys(value, ["version", "state", "scope", "locations", "limits"]) ||
      value.version !== 1 || !["ready", "forbidden"].includes(value.state) ||
      !Array.isArray(value.locations) || value.locations.length > 100 ||
      !hasExactKeys(value.limits, ["contentsDepth", "locationCount", "complete"]) ||
      value.limits.contentsDepth !== 1 || value.limits.locationCount !== 100 ||
      value.limits.complete !== true) return false;
  if (value.state === "forbidden") return value.scope === null && value.locations.length === 0;
  const validRecord = (record, allowWorld) => hasExactKeys(record,
    ["id", "name", "parentId", "slot", "kind", "status", "summary", "visibility", "mapAnchor"]) &&
    token(record.id) && text(record.name, 400) &&
    (record.parentId === null || token(record.parentId)) &&
    typeof record.slot === "string" && record.slot.length <= 200 &&
    (allowWorld
      ? ["world", "region", "settlement", "site", "interior"].includes(record.kind)
      : ["region", "settlement", "site", "interior"].includes(record.kind)) &&
    ["draft", "active"].includes(record.status) && ["public", "party", "gm"].includes(record.visibility) &&
    text(record.summary, 1_000) && (record.mapAnchor === null ||
      hasExactKeys(record.mapAnchor, ["x", "y"]) &&
      Number.isInteger(record.mapAnchor.x) && record.mapAnchor.x >= 0 && record.mapAnchor.x <= 1_000 &&
      Number.isInteger(record.mapAnchor.y) && record.mapAnchor.y >= 0 && record.mapAnchor.y <= 1_000);
  if (!validRecord(value.scope, true) || value.scope.id !== scopeId) return false;
  const ids = new Set();
  return value.locations.every((location) => validRecord(location, false) &&
    location.parentId === scopeId && !ids.has(location.id) && Boolean(ids.add(location.id)));
}

function validWorldLocationScopePage(value, scopeId, offset) {
  if (!hasExactKeys(value,
    ["version", "state", "scope", "locations", "totalCount", "complete", "nextCursor"]) ||
      value.version !== 1 || !["ready", "forbidden"].includes(value.state) ||
      !Array.isArray(value.locations) || value.locations.length > 100 ||
      !Number.isInteger(value.totalCount) || value.totalCount < value.locations.length || value.totalCount > 200 ||
      typeof value.complete !== "boolean" || !(value.nextCursor === null || value.nextCursor === "100") ||
      value.locations.length !== Math.max(0, Math.min(100, value.totalCount - offset)) ||
      value.nextCursor !== (offset === 0 && value.totalCount > 100 ? "100" : null) ||
      value.complete !== (value.nextCursor === null)) return false;
  if (value.state === "forbidden") return value.scope === null && value.locations.length === 0 &&
    value.totalCount === 0 && value.complete && value.nextCursor === null;
  const validRecord = (record, allowWorld) => hasExactKeys(record,
    ["id", "name", "parentId", "slot", "kind", "status", "summary", "visibility", "mapAnchor"]) &&
    token(record.id) && text(record.name, 400) &&
    (record.parentId === null || token(record.parentId)) &&
    typeof record.slot === "string" && record.slot.length <= 200 &&
    (allowWorld
      ? ["world", "region", "settlement", "site", "interior"].includes(record.kind)
      : ["region", "settlement", "site", "interior"].includes(record.kind)) &&
    ["draft", "active"].includes(record.status) && ["public", "party", "gm"].includes(record.visibility) &&
    text(record.summary, 1_000) && (record.mapAnchor === null ||
      hasExactKeys(record.mapAnchor, ["x", "y"]) &&
      Number.isInteger(record.mapAnchor.x) && record.mapAnchor.x >= 0 && record.mapAnchor.x <= 1_000 &&
      Number.isInteger(record.mapAnchor.y) && record.mapAnchor.y >= 0 && record.mapAnchor.y <= 1_000);
  if (!validRecord(value.scope, true) || value.scope.id !== scopeId) return false;
  const ids = new Set();
  return value.locations.every((location) => validRecord(location, false) &&
    location.parentId === scopeId && !ids.has(location.id) && Boolean(ids.add(location.id)));
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
    const records = [result.data.scope, ...result.data.locations];
    const mediaById = new Map();
    let mediaResolved = false;
    if (includeMedia && records.length > 0) {
      try {
        const response = await fetchImpl(url(origin, `${applicationRoot}/media-batch`), {
          method: "POST",
          headers: { "Content-Type": "application/json", Accept: "application/json" },
          cache: "no-store",
          body: JSON.stringify({ entityIds: records.map((record) => record.id), perspective }),
        });
        const media = response?.ok ? await json(response) : null;
        const projected = projectLocationMediaBatch(
          media, applicationId, stateSpaceId, records.map((record) => record.id),
        );
        if (projected) {
          for (const [entityId, visual] of projected) mediaById.set(entityId, visual);
          mediaResolved = true;
        }
      } catch (error) { if (error?.name === "AbortError") throw error; }
    }
    const items = records.map((record, index) => {
      const selectedMedia = mediaById.get(record.id) ?? null;
      const selectedMap = selectedMedia?.map ?? null;
      const { map: _, ...entityMedia } = selectedMedia ?? {};
      return {
        id: record.id,
        name: record.name,
        kind: record.kind,
        summary: record.summary,
        ...(record.parentId ? { containerId: record.parentId } : {}),
        ...(record.slot ? { containmentSlot: record.slot } : {}),
        ...(record.mapAnchor ? { mapAnchor: record.mapAnchor } : {}),
        ...(index === 0 && record.kind === "world" ? { isWorldRoot: true } : {}),
        mapVisualState: selectedMap ? "ready" : mediaResolved ? "absent" : "unavailable",
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
    const records = [result.data.scope, ...result.data.locations];
    const mediaById = new Map();
    let mediaResolved = false;
    if (includeMedia && records.length > 0) {
      try {
        const response = await fetchImpl(url(origin, `${applicationRoot}/media-batch`), {
          method: "POST",
          headers: { "Content-Type": "application/json", Accept: "application/json" },
          cache: "no-store",
          body: JSON.stringify({ entityIds: records.map((record) => record.id), perspective }),
        });
        const media = response?.ok ? await json(response) : null;
        const projected = projectLocationMediaBatch(
          media, applicationId, stateSpaceId, records.map((record) => record.id),
        );
        if (projected) {
          for (const [entityId, visual] of projected) mediaById.set(entityId, visual);
          mediaResolved = true;
        }
      } catch (error) { if (error?.name === "AbortError") throw error; }
    }
    const items = records.map((record, index) => {
      const selectedMedia = mediaById.get(record.id) ?? null;
      const selectedMap = selectedMedia?.map ?? null;
      const { map: _, ...entityMedia } = selectedMedia ?? {};
      return {
        id: record.id, name: record.name, kind: record.kind, summary: record.summary,
        ...(record.parentId ? { containerId: record.parentId } : {}),
        ...(record.slot ? { containmentSlot: record.slot } : {}),
        ...(record.mapAnchor ? { mapAnchor: record.mapAnchor } : {}),
        ...(index === 0 && record.kind === "world" ? { isWorldRoot: true } : {}),
        mapVisualState: selectedMap ? "ready" : mediaResolved ? "absent" : "unavailable",
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
    totalCount: Math.max(0, legacy.items.length - 1),
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

function validCurrentAffordances(value) {
  if (!Array.isArray(value) || value.length > 24) return false;
  const keys = new Set();
  return value.every((item) => hasExactKeys(item, ["key", "label", "summary"]) &&
    token(item.key) && !keys.has(item.key) && Boolean(keys.add(item.key)) &&
    text(item.label, 500) && text(item.summary, 2_000));
}

function validCurrentSceneProjection(value) {
  if (!hasExactKeys(value, ["version", "kind", "location", "conversationId", "encounterId", "affordances"]) ||
      value.version !== 1 || !["exploration", "conversation", "combat"].includes(value.kind) ||
      !hasExactKeys(value.location, ["id", "kind", "summary", "visibility"]) ||
      !token(value.location.id) || !["region", "settlement", "site", "interior"].includes(value.location.kind) ||
      !text(value.location.summary, 2_000) || !["public", "party"].includes(value.location.visibility) ||
      !(value.conversationId === null || token(value.conversationId)) ||
      !(value.encounterId === null || token(value.encounterId)) || !validCurrentAffordances(value.affordances)) return false;
  const expectedKind = value.encounterId ? "combat" : value.conversationId ? "conversation" : "exploration";
  return value.kind === expectedKind;
}

function validResumeNamed(value, extraKeys, validateExtra) {
  return hasExactKeys(value, ["id", "name", ...extraKeys]) && token(value.id) && text(value.name, 500) &&
    validateExtra(value);
}

function validCampaignResumeProjection(value, campaignId) {
  if (!hasExactKeys(value, ["version", "campaign", "party", "scene", "activeArc", "activeChapter",
    "activeSession", "latestRecap", "affordances"]) || value.version !== 1 ||
    !hasExactKeys(value.campaign, ["id", "name", "title", "premise", "partyGoals", "toneAndBoundaries"]) ||
    value.campaign.id !== campaignId || !token(value.campaign.id) || !text(value.campaign.name, 500) ||
    !text(value.campaign.title, 500) || !text(value.campaign.premise, 2_000) ||
    !Array.isArray(value.campaign.partyGoals) || value.campaign.partyGoals.length < 1 ||
    value.campaign.partyGoals.length > 3 || !value.campaign.partyGoals.every((item) => text(item, 2_000)) ||
    !Array.isArray(value.campaign.toneAndBoundaries) || value.campaign.toneAndBoundaries.length < 1 ||
    value.campaign.toneAndBoundaries.length > 8 ||
    !value.campaign.toneAndBoundaries.every((item) => text(item, 2_000)) ||
    !hasExactKeys(value.party, ["activeMemberCount"]) ||
    !boundedInteger(value.party.activeMemberCount, 0, 1_000_000) || !validCurrentAffordances(value.affordances)) return false;
  if (value.scene !== null && (!hasExactKeys(value.scene, ["locationId", "conversationId", "encounterId"]) ||
      !token(value.scene.locationId) || !(value.scene.conversationId === null || token(value.scene.conversationId)) ||
      !(value.scene.encounterId === null || token(value.scene.encounterId)))) return false;
  if (value.activeArc !== null && !validResumeNamed(value.activeArc, ["title", "partyStake"],
    (record) => text(record.title, 500) && text(record.partyStake, 2_000))) return false;
  if (value.activeChapter !== null && !validResumeNamed(value.activeChapter, ["title", "partyQuestion"],
    (record) => text(record.title, 500) && text(record.partyQuestion, 2_000))) return false;
  if (value.activeSession !== null && !validResumeNamed(value.activeSession, ["ordinal", "status"],
    (record) => Number.isInteger(record.ordinal) && record.ordinal >= 1 && record.status === "active")) return false;
  if (value.latestRecap !== null) {
    const recap = value.latestRecap;
    if (!hasExactKeys(recap, ["sessionId", "ordinal", "chapter", "arc", "milestones"]) ||
        !token(recap.sessionId) || !Number.isInteger(recap.ordinal) || recap.ordinal < 1 ||
        !hasExactKeys(recap.chapter, ["id", "title", "partyQuestion"]) || !token(recap.chapter.id) ||
        !text(recap.chapter.title, 500) || !text(recap.chapter.partyQuestion, 2_000) ||
        !hasExactKeys(recap.arc, ["id", "title", "partyStake"]) || !token(recap.arc.id) ||
        !text(recap.arc.title, 500) || !text(recap.arc.partyStake, 2_000) ||
        !Array.isArray(recap.milestones) || recap.milestones.length > 5 ||
        !recap.milestones.every((item) => hasExactKeys(item,
          ["chapterId", "title", "closingSummary", "timestamp", "sequence"]) && token(item.chapterId) &&
          text(item.title, 500) && text(item.closingSummary, 2_000) && text(item.timestamp, 100) &&
          Number.isInteger(item.sequence) && item.sequence >= 0)) return false;
  }
  return true;
}

function currentProjectionEvidence(result) {
  return {
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
  const parameters = new URLSearchParams({ perspective, campaignId });
  try {
    const resume = await readModelResponse({
      fetchImpl,
      resource: url(origin, `${root}${encodeURIComponent(campaignResumeContract.id)}?${parameters}`),
      init: { headers: { Accept: "application/json" }, cache: "no-store" },
      applicationId, stateSpaceId, query: campaignResumeContract,
      maximumBodyBytes: 270_000, maximumDataBytes: 262_144,
      statusPolicy: { ready: [200], forbidden: [403], stale: [409], unavailable: "remaining" },
      validate: (value) => validCampaignResumeProjection(value, campaignId),
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
      validate: validCurrentSceneProjection,
    });
    if (scene.status !== "ready") return { status: scene.status === "forbidden" ? "forbidden" : "error" };
    const resumeScene = resume.data.scene;
    if (scene.data.location.id !== resumeScene.locationId ||
        scene.data.conversationId !== resumeScene.conversationId ||
        scene.data.encounterId !== resumeScene.encounterId ||
        JSON.stringify(scene.data.affordances) !== JSON.stringify(resume.data.affordances)) return { status: "stale" };
    return {
      status: "ready", resume: resume.data, scene: scene.data,
      projection: { resume: currentProjectionEvidence(resume), scene: currentProjectionEvidence(scene) },
    };
  } catch (error) {
    if (error?.name === "AbortError") throw error;
    return { status: "error" };
  }
}

function validWorldPeopleHoldings(value, worldId) {
  if (!hasExactKeys(value, ["version", "state", "world", "locations", "people", "holdings", "limits"]) ||
      value.version !== 1 || !["ready", "forbidden"].includes(value.state) ||
      !Array.isArray(value.locations) || !Array.isArray(value.people) || !Array.isArray(value.holdings) ||
      value.locations.length + value.people.length + value.holdings.length > 200 ||
      !hasExactKeys(value.limits, ["contentsDepth", "recordCount", "complete"]) ||
      value.limits.contentsDepth !== 4 || value.limits.recordCount !== 200 ||
      value.limits.complete !== true) return false;
  if (value.state === "forbidden") return value.world === null && value.locations.length === 0 &&
    value.people.length === 0 && value.holdings.length === 0;
  if (!hasExactKeys(value.world, ["id", "name"]) || value.world.id !== worldId ||
      !text(value.world.name, 400)) return false;
  const locationIds = new Set();
  for (const location of value.locations) {
    if (!hasExactKeys(location, ["id", "name", "parentId", "kind", "status", "summary"]) ||
        !token(location.id) || locationIds.has(location.id) || !text(location.name, 400) ||
        !token(location.parentId) || !["region", "settlement", "site", "interior"].includes(location.kind) ||
        !["draft", "active"].includes(location.status) || !text(location.summary, 1_000)) return false;
    locationIds.add(location.id);
  }
  if (value.locations.some((location) => location.parentId !== worldId && !locationIds.has(location.parentId)))
    return false;
  const recordIds = new Set(locationIds);
  const validMotive = (motive) => motive === null ||
    hasExactKeys(motive, ["status", "summary", "visibility"]) &&
    ["draft", "active"].includes(motive.status) && text(motive.summary, 1_000) &&
    ["public", "party", "gm"].includes(motive.visibility);
  for (const person of value.people) {
    if (!hasExactKeys(person, ["id", "name", "locationId", "kind", "motive"]) ||
        !token(person.id) || recordIds.has(person.id) || !text(person.name, 400) ||
        !locationIds.has(person.locationId) || !["NPC", "Creature"].includes(person.kind) ||
        !validMotive(person.motive)) return false;
    recordIds.add(person.id);
  }
  for (const holding of value.holdings) {
    if (!hasExactKeys(holding, ["id", "name", "locationId", "kind"]) ||
        !token(holding.id) || recordIds.has(holding.id) || !text(holding.name, 400) ||
        !locationIds.has(holding.locationId) ||
        !["Item", "Conveyance", "Aerial conveyance", "Teleport gate"].includes(holding.kind)) return false;
    recordIds.add(holding.id);
  }
  return true;
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
    const media = await readAuthorizedMediaBatch({
      fetchImpl, origin, applicationId, stateSpaceId,
      entityIds: result.data.people.map((person) => person.id), perspective: "dm",
    });
    return {
      status: "ready",
      locations: result.data.locations.map((location) => ({
        id: location.id, name: location.name, kind: location.kind, summary: location.summary,
        containerId: location.parentId,
      })),
      people: result.data.people.map((person) => {
        const { motive, ...identity } = person;
        return {
          ...identity,
          ...(motive ? { motive } : {}),
          ...(media.has(person.id) ? { media: media.get(person.id) } : {}),
        };
      }),
      holdings: result.data.holdings,
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
  if (!hasExactKeys(value, ["version", "state", "world", "locations", "people", "holdings",
    "totalCount", "complete", "nextCursor", "limits"]) ||
      value.version !== 1 || !["ready", "forbidden"].includes(value.state) ||
      !Array.isArray(value.locations) || value.locations.length > 800 ||
      !Array.isArray(value.people) || !Array.isArray(value.holdings) ||
      value.people.length + value.holdings.length > 50 ||
      !Number.isInteger(value.totalCount) || value.totalCount < 0 || value.totalCount > 2_000 ||
      typeof value.complete !== "boolean" ||
      !(value.nextCursor === null || /^(?:[1-9][0-9]{1,3})$/u.test(value.nextCursor)) ||
      !hasExactKeys(value.limits, ["contentsDepth", "recordCount", "pageSize", "hierarchyComplete"]) ||
      value.limits.contentsDepth !== 16 || value.limits.recordCount !== 2_000 ||
      value.limits.pageSize !== 50 || typeof value.limits.hierarchyComplete !== "boolean") return false;
  if (value.state === "forbidden") return value.world === null && value.locations.length === 0 &&
    value.people.length === 0 && value.holdings.length === 0 && value.totalCount === 0 &&
    value.complete === true && value.nextCursor === null;
  if (!hasExactKeys(value.world, ["id", "name"]) || value.world.id !== worldId ||
      !text(value.world.name, 400)) return false;
  const pageCount = value.people.length + value.holdings.length;
  if (value.totalCount < offset + pageCount || value.complete !== (value.nextCursor === null) ||
      (value.complete && offset + pageCount !== value.totalCount) ||
      (!value.complete && (pageCount !== 50 || value.nextCursor !== String(offset + 50)))) return false;
  const locationIds = new Set();
  for (const location of value.locations) {
    if (!hasExactKeys(location, ["id", "name", "parentId", "kind"]) ||
        !token(location.id) || locationIds.has(location.id) || !text(location.name, 400) ||
        !token(location.parentId) ||
        !["region", "settlement", "site", "interior"].includes(location.kind)) return false;
    locationIds.add(location.id);
  }
  if (value.locations.some((location) => location.parentId !== worldId && !locationIds.has(location.parentId)))
    return false;
  const recordIds = new Set(locationIds);
  const validMotive = (motive) => motive === null ||
    hasExactKeys(motive, ["status", "summary", "visibility"]) &&
    ["draft", "active"].includes(motive.status) && text(motive.summary, 1_000) &&
    ["public", "party", "gm"].includes(motive.visibility);
  for (const person of value.people) {
    if (!hasExactKeys(person, ["id", "name", "locationId", "kind", "motive"]) ||
        !token(person.id) || recordIds.has(person.id) || !text(person.name, 400) ||
        !locationIds.has(person.locationId) || !["NPC", "Creature"].includes(person.kind) ||
        !validMotive(person.motive)) return false;
    recordIds.add(person.id);
  }
  for (const holding of value.holdings) {
    if (!hasExactKeys(holding, ["id", "name", "locationId", "kind"]) ||
        !token(holding.id) || recordIds.has(holding.id) || !text(holding.name, 400) ||
        !locationIds.has(holding.locationId) ||
        !["Item", "Conveyance", "Aerial conveyance", "Teleport gate"].includes(holding.kind)) return false;
    recordIds.add(holding.id);
  }
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
    const media = await readAuthorizedMediaBatch({
      fetchImpl, origin, applicationId, stateSpaceId,
      entityIds: result.data.people.map((person) => person.id), perspective: "dm",
    });
    return {
      status: "ready",
      world: result.data.world,
      locations: result.data.locations.map((location) => ({
        id: location.id, name: location.name, kind: location.kind,
        summary: `${location.name} is part of ${result.data.world.name}.`,
        containerId: location.parentId,
      })),
      people: result.data.people.map((person) => {
        const { motive, ...identity } = person;
        return {
          ...identity,
          ...(motive ? { motive } : {}),
          ...(media.has(person.id) ? { media: media.get(person.id) } : {}),
        };
      }),
      holdings: result.data.holdings,
      totalCount: result.data.totalCount,
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
      validate: (value) => validInventoryContainer(value, scopeId),
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
      validate: (value) => validInventoryWallet(value, actorId),
    });
    return result.status === "ready" ? { status: "ready", data: result.data }
      : { status: "unavailable", data: null };
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
  if (page.status !== "ready") return page;
  const wallet = walletResult.status === "ready" ? walletResult.data : null;
  return {
    ...page,
    data: {
      ...page.data,
      items: page.data.items.map((item) => ({
        ...item, parentItemId: null, depth: 1, childCount: null, deeperContentsOmitted: true,
      })),
      wallet: wallet?.wallet ?? null,
      walletState: wallet ? {
        status: wallet.limits.complete ? "complete" : "partial",
        reason: wallet.limits.complete ? null : "depth-limit",
      } : { status: "unavailable", reason: "read-failed" },
    },
  };
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

async function readAuthorizedMediaBatch({
  fetchImpl, origin, applicationId, stateSpaceId, entityIds, perspective,
}) {
  const ids = [...new Set(entityIds.map(token).filter(Boolean))];
  if (ids.length === 0) return new Map();
  if (ids.length > 256) return new Map();
  try {
    const applicationRoot = `/api/applications/${encodeURIComponent(applicationId)}` +
      `/state-spaces/${encodeURIComponent(stateSpaceId)}`;
    const response = await fetchImpl(url(origin, `${applicationRoot}/media-batch`), {
      method: "POST",
      headers: { "Content-Type": "application/json", Accept: "application/json" },
      cache: "no-store",
      body: JSON.stringify({ entityIds: ids, perspective }),
    });
    const payload = response?.ok ? await json(response) : null;
    const allowed = new Set(ids);
    if (payload?.applicationId !== applicationId || payload.stateSpaceId !== stateSpaceId ||
        !Array.isArray(payload.items) || payload.items.length > ids.length ||
        new Set(payload.items.map((item) => item?.entityId)).size !== payload.items.length ||
        !payload.items.every((item) => allowed.has(token(item?.entityId)))) return new Map();
    const result = new Map();
    for (const item of payload.items) {
      const visual = projectMediaVisual(item);
      if (visual) result.set(item.entityId, visual);
    }
    return result;
  } catch (error) {
    if (error?.name === "AbortError") throw error;
    return new Map();
  }
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
  const locationById = new Map(locationDirectory.map((location) => [location.id, location]));
  const admittedSubjectIds = new Set();
  const subjectEntries = new Map();
  for (const entry of projectedKnowledge.entries) {
    const subjectId = token(entry?.subject?.id);
    if (subjectId && entry.stance !== "familiar") admittedSubjectIds.add(subjectId);
    // Locations and the World itself are already classified by their authorized resources.
    // They cannot be route entities, so probing each one for a route component only creates a
    // large fan-out of expected 404s on lore-heavy worlds.
    if (!subjectId || entry.stance === "familiar" || subjectId === worldId || locationById.has(subjectId)) continue;
    const values = subjectEntries.get(subjectId) ?? [];
    values.push(entry);
    subjectEntries.set(subjectId, values);
  }
  if (subjectEntries.size === 0) return [];

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
    if (!destination || (perspective === "player" && !admittedSubjectIds.has(destinationId))) return null;
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
  fetchImpl, origin, entityRoot, conversationId, perspective, authorizedActorIds,
}) {
  const [conversation, interaction, participantIds] = await Promise.all([
    readNamedEntity(fetchImpl, origin, entityRoot, conversationId),
    readExactComponent(fetchImpl, origin, entityRoot, conversationId, WORLD_INTERACTION_COMPONENT_TYPE_ID),
    readExactRelationshipTargets(
      fetchImpl, origin, entityRoot, conversationId, WORLD_INTERACTION_PARTICIPANT_RELATIONSHIP_KIND,
    ),
  ]);
  if (!conversation || !validInteraction(interaction) || participantIds === null || participantIds.length > 32)
    return null;
  const visibleIds = perspective === "dm"
    ? participantIds
    : participantIds.filter((id) => authorizedActorIds.has(id));
  const participants = (await Promise.all(visibleIds.map((id) =>
    readNamedEntity(fetchImpl, origin, entityRoot, id)))).filter(Boolean);
  if (participants.length !== visibleIds.length) return null;
  const scope = new URL(entityRoot, origin).pathname
    .match(/^\/api\/applications\/([^/]+)\/state-spaces\/([^/]+)\/entities$/u);
  const media = scope ? await readAuthorizedMediaBatch({
    fetchImpl, origin, applicationId: decodeURIComponent(scope[1]), stateSpaceId: decodeURIComponent(scope[2]),
    entityIds: [conversationId, ...visibleIds], perspective,
  }) : new Map();
  return {
    status: "ready",
    kind: "conversation",
    ...(media.get(conversationId)?.scene ? { scene: media.get(conversationId).scene } : {}),
    conversation: {
      id: conversation.id,
      name: conversation.name,
      participants: participants.map((participant) => ({
        ...participant,
        ...(media.get(participant.id)?.portrait ? { portrait: media.get(participant.id).portrait } : {}),
      })),
      ...(perspective === "dm" ? { summary: interaction.summary } : {}),
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
  const hasBoundActor = binding.status === "bound" && binding.role === "actor";
  // A development preference or requested perspective can never promote a server-bound actor.
  const serverRole = binding;
  const isGameMaster = serverRole.role === "game-master";
  // Shared website authority is independent of stale Player preferences and old deep links.
  // Do not offer a character-knowledge preview until it has an explicit observer selection.
  const sharedWebsite = response.headers.get("X-Website-Access") === "shared";
  const contextAudience = isGameMaster
    ? {
        seat: "dm",
        perspective: sharedWebsite ? "dm" : normalizePerspective(normalizedRequestedPerspective),
        allowedPerspectives: sharedWebsite ? ["dm"] : ["dm", "player"],
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
    : isGameMaster ? registeredCampaign.party.filter((entry) => entry.status === "active").map((entry) => ({
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

export async function readAuthorizedWorldHistory({ fetchImpl = fetch, origin, source }) {
  const { applicationId } = source;
  const campaignId = source.campaign.id;
  const perspective = source.audience.perspective ?? "player";
  const response = await fetchImpl(url(origin, `/api/applications/${encodeURIComponent(applicationId)}` +
    `/campaigns/${encodeURIComponent(campaignId)}/chronology?perspective=${encodeURIComponent(perspective)}`),
  { headers: { Accept: "application/json" }, cache: "no-store" });
  const result = response.ok ? chronology(await json(response), perspective) : null;
  if (!result || result.status === "unavailable") throw new Error("The history view is unavailable.");
  return { chronology: result };
}

export async function readAuthorizedWorldLore({ fetchImpl = fetch, origin, source }) {
  const { applicationId, stateSpaceId } = source;
  const campaignId = source.campaign.id;
  const perspective = source.audience.perspective ?? "player";
  const preview = source.audience.seat === "dm" && perspective === "player";
  if (preview)
    throw new Error("Campaign knowledge is unavailable in Player preview; an Actor binding is required.");
  const response = await fetchImpl(url(origin, `/api/applications/${encodeURIComponent(applicationId)}` +
    `/campaigns/${encodeURIComponent(campaignId)}/knowledge?perspective=${encodeURIComponent(perspective)}`),
  { headers: { Accept: "application/json" }, cache: "no-store" });
  const result = response.ok ? knowledge(await json(response)) : null;
  if (!result || result.status === "unavailable") throw new Error("The lore view is unavailable.");
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
  const recordIds = new Set();
  const seenCursors = new Set();
  let cursor = null;
  let expectedSourceRevision = null;
  let expectedTotalCount = null;
  let world = null;
  let hierarchyComplete = true;
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
    for (const location of page.locations) {
      const previous = locations.get(location.id);
      if (previous && JSON.stringify(previous) !== JSON.stringify(location))
        return { status: "stale", locations: [], people: [], holdings: [] };
      locations.set(location.id, location);
    }
    for (const [key, target] of [["people", people], ["holdings", holdings]]) {
      for (const item of page[key]) {
        if (recordIds.has(item.id) || recordIds.size >= 2_000)
          return { status: "error", locations: [], people: [], holdings: [] };
        recordIds.add(item.id);
        target.push(item);
      }
    }
    if (page.nextCursor === null) {
      if (recordIds.size !== expectedTotalCount) return {
        status: "stale", locations: [], people: [], holdings: [],
      };
      return {
        status: "ready", world, locations: [...locations.values()], people, holdings,
        totalCount: expectedTotalCount, hierarchyComplete,
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
  const preview = source.audience.seat === "dm" && perspective === "player";
  if (preview)
    throw new Error("The people directory is unavailable in Player preview; an Actor binding is required.");
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
      totalCount: legacy.people.length + legacy.holdings.length,
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
      directoryRecordCount: result.totalCount,
      peopleSourceRevisionFingerprint: result.sourceRevisionFingerprint,
    },
  };
}

/** Composes registered play projections and only the bounded adapters listed in the release contract. */
export async function readCurrentViewPatch({ fetchImpl = fetch, origin, source }) {
  const { applicationId, stateSpaceId } = source;
  const campaignId = source.campaign.id;
  const perspective = source.audience.perspective ?? "player";
  const preview = source.audience.seat === "dm" && perspective === "player";
  const current = await readRegisteredCurrentPlay({
    fetchImpl, origin, applicationId, stateSpaceId, campaignId, perspective,
  });
  if (current.status === "forbidden") throw new Error("The current scene is unavailable to this audience.");
  if (!["ready", "empty"].includes(current.status))
    throw new Error(current.status === "stale" ? "The current scene changed while it was loading." :
      "The current scene could not be read safely.");

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
  let projectedKnowledge = source.knowledge;

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
      knownRoutes: [],
    };
  }

  const scene = current.scene;
  let selectedLocation = locations.find((item) => item.id === scene.location.id) ?? null;
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
    currentSituation = combat ? { ...combat, locationId: scene.location.id, affordances: scene.affordances }
      : { status: "unavailable", locationId: scene.location.id,
        message: "The current encounter could not be read safely." };
  } else if (scene.kind === "conversation") {
    const conversation = await readConversationCurrentScene({
      fetchImpl, origin, entityRoot, conversationId: scene.conversationId,
      perspective, authorizedActorIds,
    });
    currentSituation = conversation
      ? { ...conversation, locationId: scene.location.id, affordances: scene.affordances }
      : { status: "unavailable", locationId: scene.location.id,
        message: "The current conversation could not be read safely." };
  } else {
    currentSituation = {
      status: "ready", kind: "exploration", locationId: scene.location.id, affordances: scene.affordances,
    };
  }
  if (currentSituation.status === "ready" && currentSituation.kind === "exploration" &&
      !preview && projectedKnowledge.status === "unavailable") {
    const lore = await readAuthorizedWorldLore({ fetchImpl, origin, source });
    projectedKnowledge = lore.knowledge;
    patch.knowledge = projectedKnowledge;
  }
  const knownRoutes = currentSituation.status === "ready" && currentSituation.kind === "exploration"
    ? await readKnownOpenRoutes({
      fetchImpl, origin, entityRoot, worldId: source.contextSelection.selectedWorldId,
      currentLocationId: scene.location.id, perspective, projectedKnowledge, locationDirectory: locations,
    }) : [];
  return {
    ...patch,
    locationDirectory: locations,
    locationDirectoryAudience: perspective,
    currentSituation,
    currentLocationId: scene.location.id,
    knownRoutes,
  };
}

/**
 * Completes only the requested deferred view. The existing private endpoints remain the
 * authorization boundary; no ambient DM knowledge or media is requested for Player preview.
 * Registered collection adapters remain bounded and follow every continuation. The named-record
 * Current/Play adapters are retained only where the catalog does not yet expose a closed query.
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
  } else if (section === "history") {
    Object.assign(patch, await readAuthorizedWorldHistory({ fetchImpl: read, origin, source }));
  } else if (section === "lore") {
    Object.assign(patch, await readAuthorizedWorldLore({ fetchImpl: read, origin, source }));
  } else if (section === "locations") {
    const worldId = token(source.contextSelection?.selectedWorldId);
    if (!worldId) throw new Error("The selected World identity is unavailable.");
    Object.assign(patch, await readWorldLocationScopePatch({
      fetchImpl: read, origin, source, scopeId: worldId, cursor: null,
    }));
  } else if (section === "people") {
    Object.assign(patch, await readWorldPeopleHoldings({ fetchImpl: read, origin, source }));
  } else if (section === "current") {
    Object.assign(patch, await readCurrentViewPatch({ fetchImpl: read, origin, source }));
  }
  if (failure || scope.failure) throw new Error(failure ?? scope.failure);
  return patch;
}
