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
const CAMPAIGN_ROOT_COMPONENT_TYPE_ID = "game.core.campaign.root";
const CAMPAIGN_CURRENT_SCENE_COMPONENT_TYPE_ID = "game.core.campaign.current-scene";
const CAMPAIGN_SCENE_AFFORDANCES_COMPONENT_TYPE_ID =
  "game.core.campaign.scene-affordances";
const CAMPAIGN_CHAPTER_COMPONENT_TYPE_ID = "game.core.campaign.chapter";
const CAMPAIGN_ARC_COMPONENT_TYPE_ID = "game.core.campaign.arc";
const CAMPAIGN_SESSION_COMPONENT_TYPE_ID = "game.core.campaign.session";
const CAMPAIGN_SESSION_RECAP_COMPONENT_TYPE_ID = "game.core.campaign.session-recap";
const CAMPAIGN_LOCATION_VISIT_COMPONENT_TYPE_ID = "game.core.campaign.location-visit";
const CAMPAIGN_HAS_SESSION_RELATIONSHIP_KIND = "game.core.campaign.has-session";
const CAMPAIGN_HAS_LOCATION_VISIT_RELATIONSHIP_KIND =
  "game.core.campaign.has-location-visit";
const CAMPAIGN_LOCATION_VISIT_AT_LOCATION_RELATIONSHIP_KIND =
  "game.core.campaign.location-visit.at-location";
const CAMPAIGN_RECORD_WORLD_REFERENCE_RELATIONSHIP_KIND =
  "game.core.campaign.record.references-world-entity";
const CAMPAIGN_PARTICIPATION_COMPONENT_TYPE_ID = "game.core.campaign.character-participation";
const CAMPAIGN_HAS_PARTICIPATION_RELATIONSHIP_KIND =
  "game.core.campaign.has-character-participation";
const CAMPAIGN_PARTICIPATION_ACTOR_RELATIONSHIP_KIND =
  "game.core.campaign.character-participation.for-actor";
const PLAYTEST_CHARACTER_RECORD_COMPONENT_TYPE_ID = "dnd2024.playtest-character-record";
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

function characterDetails(value) {
  const entries = Array.isArray(value?.entries) && value.entries.length <= 200
    ? value.entries.map((entry) => {
      const kind = token(entry?.kind);
      const key = token(entry?.key);
      const label = text(entry?.label, 160);
      const details = entry?.details === undefined ? null : text(entry.details, 2_000);
      return kind && key && label && (entry?.details === undefined || details)
        ? { kind, key, label, ...(details ? { details } : {}) }
        : null;
    }).filter(Boolean)
    : [];
  return { state: text(value?.state, 32), entries };
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

export async function readCanonicalCharacter({ fetchImpl, origin, applicationId, stateSpaceId, actorId, perspective }) {
  const applicationRoot = `/api/applications/${encodeURIComponent(applicationId)}` +
    `/state-spaces/${encodeURIComponent(stateSpaceId)}`;
  const entityRoot = `${applicationRoot}/entities`;
  const headers = { Accept: "application/json" };
  try {
    const response = await fetchImpl(url(origin, `${entityRoot}/${encodeURIComponent(actorId)}` +
      `/read-models/${encodeURIComponent("dnd2024.query.character-dossier-v1")}` +
      (perspective ? `?perspective=${encodeURIComponent(perspective)}` : "")), {
      headers,
      cache: "no-store",
    });
    if (!response?.ok) {
      const failure = await json(response);
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
    const payload = await json(response);
    const projected = payload?.data;
    if (!validCharacterDossier(projected, actorId) ||
        token(payload?.qualifiedQueryId) !== "dnd2024.query.character-dossier-v1" ||
        !token(payload?.stateSpaceFingerprint) || !token(payload?.resolutionFingerprint) ||
        !token(payload?.resultFingerprint) || !token(payload?.sourceRevisionFingerprint) ||
        !Array.isArray(projected.sheet?.inventory?.items)) return {
      status: "error",
      data: null,
      failureCategory: "incompatible-data",
      diagnosticId: canonicalCharacterDiagnosticId(response, actorId, "incompatible-data"),
    };
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
      diagnosticId: canonicalCharacterDiagnosticId(response, actorId, "ready"),
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
        stateSpaceFingerprint: payload.stateSpaceFingerprint,
        resolutionFingerprint: payload.resolutionFingerprint,
        resultFingerprint: payload.resultFingerprint,
        sourceRevisionFingerprint: payload.sourceRevisionFingerprint,
      },
      },
    };
  } catch {
    return {
      status: "error",
      data: null,
      failureCategory: "transport",
      diagnosticId: canonicalCharacterDiagnosticId(null, actorId, "transport"),
    };
  }
}

function activeCharacterParticipation(value, expectedEntityId) {
  const parsed = componentValue(value, expectedEntityId, CAMPAIGN_PARTICIPATION_COMPONENT_TYPE_ID);
  return hasExactKeys(parsed, ["status"]) && parsed.status === "active";
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
      !Array.isArray(value.entries) || value.entries.length > 100) {
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

function titleFromSlug(value) {
  if (typeof value !== "string") return null;
  const words = value.split(/[-_]/u).map((word) => word.trim()).filter(Boolean);
  if (words.length === 0) return null;
  return words.map((word) => `${word[0]?.toUpperCase() ?? ""}${word.slice(1).toLowerCase()}`).join(" ");
}

function worldIdentityForCampaign(campaignId) {
  const worldKey = campaignWorldId(campaignId);
  return worldKey
    ? { id: `world.${worldKey}`, name: titleFromSlug(worldKey) ?? worldKey }
    : null;
}

function fallbackContextSelection(campaignId) {
  const world = worldIdentityForCampaign(campaignId);
  if (!world) return null;
  return {
    selectedWorldId: world.id,
    selectedCampaignId: campaignId,
    worlds: [{
      ...world,
      campaigns: [{ id: campaignId, name: titleFromSlug(campaignId.split(".").at(-1)) ?? campaignId }],
    }],
  };
}

function selectContext(selection, campaignId) {
  if (!selection || !campaignId) return null;
  const world = selection.worlds.find((candidate) =>
    candidate.campaigns.some((campaign) => campaign.id === campaignId));
  return world ? { ...selection, selectedWorldId: world.id, selectedCampaignId: campaignId } : null;
}

function updateSelectedCampaignName(selection, campaign) {
  if (!selection || !campaign) return selection;
  return {
    ...selection,
    worlds: selection.worlds.map((world) => ({
      ...world,
      campaigns: world.campaigns.map((candidate) =>
        candidate.id === campaign.id ? { ...candidate, name: campaign.name } : candidate),
    })),
  };
}

async function readContextSelection({
  fetchImpl,
  origin,
  applicationId,
  stateSpaceId,
  boundCampaignId,
  isGameMaster,
}) {
  const fallback = fallbackContextSelection(boundCampaignId);
  if (!fallback || !isGameMaster) return fallback;

  const listRoot = `/api/applications/${encodeURIComponent(applicationId)}` +
    `/state-spaces/${encodeURIComponent(stateSpaceId)}/entities`;
  const headers = { Accept: "application/json" };
  const campaignCandidates = new Map();
  const worldNames = new Map();
  const seenCursors = new Set();
  let nextCursor = null;

  do {
    const pageUrl = listRoot + (
      nextCursor === null ? "?limit=100" : `?cursor=${encodeURIComponent(nextCursor)}&limit=100`
    );
    let response;
    try {
      response = await fetchImpl(url(origin, pageUrl), { headers, cache: "no-store" });
    } catch {
      return fallback;
    }
    if (!response?.ok) return fallback;
    const payload = await json(response);
    if (!payload || !Array.isArray(payload.items)) return fallback;

    for (const item of payload.items) {
      const id = token(typeof item?.entityId === "string" ? item.entityId : item?.id);
      const name = text(item?.name, 200);
      if (!id || !name) continue;
      if (id.startsWith("world.")) worldNames.set(id, name);
      if (id.startsWith("campaign.") && campaignCandidates.size < 50) {
        campaignCandidates.set(id, { id, name });
      }
    }

    nextCursor = typeof payload.nextCursor === "string" && payload.nextCursor.length > 0
      ? payload.nextCursor
      : null;
    if (nextCursor && seenCursors.has(nextCursor)) {
      nextCursor = null;
    } else if (nextCursor) {
      seenCursors.add(nextCursor);
    }
  } while (nextCursor);

  const verified = await Promise.all(Array.from(campaignCandidates.values()).map(async (candidate) => {
    const componentPath = `${listRoot}/${encodeURIComponent(candidate.id)}` +
      `/components/${CAMPAIGN_ROOT_COMPONENT_TYPE_ID}`;
    try {
      const response = await fetchImpl(url(origin, componentPath), { headers, cache: "no-store" });
      if (!response?.ok) return null;
      const payload = await json(response);
      return componentValue(payload, candidate.id, CAMPAIGN_ROOT_COMPONENT_TYPE_ID)
        ? candidate
        : null;
    } catch {
      return null;
    }
  }));

  const readableCampaigns = verified.filter(Boolean);
  if (!readableCampaigns.some((campaign) => campaign.id === boundCampaignId)) {
    const bound = fallback.worlds[0].campaigns[0];
    readableCampaigns.push(bound);
  }

  const worlds = new Map();
  for (const campaign of readableCampaigns) {
    const identity = worldIdentityForCampaign(campaign.id);
    if (!identity) continue;
    const current = worlds.get(identity.id) ?? {
      id: identity.id,
      name: worldNames.get(identity.id) ?? identity.name,
      campaigns: [],
    };
    if (!current.campaigns.some((candidate) => candidate.id === campaign.id)) {
      current.campaigns.push({ id: campaign.id, name: campaign.name });
    }
    worlds.set(identity.id, current);
  }

  const orderedWorlds = Array.from(worlds.values())
    .map((world) => ({
      ...world,
      campaigns: world.campaigns.sort((left, right) =>
        left.name.localeCompare(right.name) || left.id.localeCompare(right.id)),
    }))
    .sort((left, right) => left.name.localeCompare(right.name) || left.id.localeCompare(right.id));
  const selectedWorld = orderedWorlds.find((world) =>
    world.campaigns.some((campaign) => campaign.id === boundCampaignId));
  return selectedWorld
    ? { selectedWorldId: selectedWorld.id, selectedCampaignId: boundCampaignId, worlds: orderedWorlds }
    : fallback;
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
  if (!applicationId || !stateSpaceId || !worldId) return [];
  const listRoot = `/api/applications/${encodeURIComponent(applicationId)}` +
    `/state-spaces/${encodeURIComponent(stateSpaceId)}/entities`;
  const headers = { Accept: "application/json" };
  const entries = new Map();
  const seenCursors = new Set();
  let nextCursor = null;

  do {
    const pageUrl = listRoot + (
      nextCursor === null ? "?limit=100" : `?cursor=${encodeURIComponent(nextCursor)}&limit=100`
    );
    let response;
    try {
      response = await fetchImpl(url(origin, pageUrl), { headers, cache: "no-store" });
    } catch {
      break;
    }
    if (!response?.ok) {
      break;
    }
    const payload = await json(response);
    if (!payload || !Array.isArray(payload.items)) {
      break;
    }

    for (const item of payload.items) {
      if (!isLocationEntity(item, worldId)) continue;
      const locationId = typeof item.entityId === "string" ? item.entityId : item.id;
      const name = text(item.name, 200);
      if (name && locationId && !entries.has(locationId)) {
        entries.set(locationId, name);
      }
    }

    nextCursor = typeof payload.nextCursor === "string" && payload.nextCursor.length > 0
      ? payload.nextCursor
      : null;
    if (nextCursor && seenCursors.has(nextCursor)) {
      nextCursor = null;
    } else if (nextCursor) {
      seenCursors.add(nextCursor);
    }
  } while (nextCursor);

  if (entries.size === 0) return [];

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
  return locationDirectory
    .filter((entry) => entry && typeof entry.id === "string" && typeof entry.name === "string")
    .sort((left, right) => left.name.localeCompare(right.name));
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
      value: readRawLocationDirectory(options).catch(() => []),
    };
    locationDirectoryCache.set(key, cached);
  }
  const rawDirectory = await cached.value;
  return rawDirectory.flatMap((entry) => {
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
  });
}

function isHoldingEntityId(entityId) {
  return ["holding.", "container.", "chest.", "item.", "equipment.", "weapon."]
    .some((prefix) => entityId.startsWith(prefix));
}

function relationshipTargetIds(value, expectedFromId, expectedKind) {
  if (!value || !Array.isArray(value.items) || value.items.length > 100) return [];
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

async function readExactRelationshipTargets(fetchImpl, origin, entityRoot, fromEntityId, qualifiedKind) {
  try {
    const relationshipRoot = entityRoot.replace(/\/entities$/u, "/relationships");
    const response = await fetchImpl(url(origin, `${relationshipRoot}` +
      `?fromEntityId=${encodeURIComponent(fromEntityId)}` +
      `&qualifiedKind=${encodeURIComponent(qualifiedKind)}&limit=100`), {
      headers: { Accept: "application/json" }, cache: "no-store",
    });
    if (!response?.ok) return null;
    const payload = await json(response);
    const targets = relationshipTargetIds(payload, fromEntityId, qualifiedKind);
    return payload?.items?.length === targets.length ? [...new Set(targets)] : null;
  } catch {
    return null;
  }
}

async function readSingleExactRelationshipTarget(
  fetchImpl,
  origin,
  entityRoot,
  fromEntityId,
  qualifiedKind,
) {
  try {
    const relationshipRoot = entityRoot.replace(/\/entities$/u, "/relationships");
    const response = await fetchImpl(url(origin, `${relationshipRoot}` +
      `?fromEntityId=${encodeURIComponent(fromEntityId)}` +
      `&qualifiedKind=${encodeURIComponent(qualifiedKind)}&limit=100`), {
      headers: { Accept: "application/json" }, cache: "no-store",
    });
    if (!response?.ok) return null;
    const payload = await json(response);
    if (!payload || !Array.isArray(payload.items) || payload.items.length !== 1) return null;
    const item = payload.items[0];
    return token(item?.fromEntityId) === fromEntityId && token(item?.qualifiedKind) === qualifiedKind
      ? token(item?.toEntityId)
      : null;
  } catch {
    return null;
  }
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

async function campaignRecordWorldEntityIds({ fetchImpl, origin, relationshipRoot, recordId }) {
  try {
    const path = `${relationshipRoot}?fromEntityId=${encodeURIComponent(recordId)}` +
      `&qualifiedKind=${encodeURIComponent(CAMPAIGN_RECORD_WORLD_REFERENCE_RELATIONSHIP_KIND)}&limit=100`;
    const response = await fetchImpl(url(origin, path), {
      headers: { Accept: "application/json" },
      cache: "no-store",
    });
    const payload = response?.ok ? await json(response) : null;
    return [...new Set(relationshipTargetIds(
      payload,
      recordId,
      CAMPAIGN_RECORD_WORLD_REFERENCE_RELATIONSHIP_KIND,
    ))];
  } catch {
    return [];
  }
}

async function readCampaignLocationVisits({
  fetchImpl,
  origin,
  listRoot,
  relationshipRoot,
  campaignId,
  includeGmContext,
}) {
  if (!includeGmContext) return [];
  try {
    const ownershipPath = `${relationshipRoot}?fromEntityId=${encodeURIComponent(campaignId)}` +
      `&qualifiedKind=${encodeURIComponent(CAMPAIGN_HAS_LOCATION_VISIT_RELATIONSHIP_KIND)}&limit=100`;
    const ownershipResponse = await fetchImpl(url(origin, ownershipPath), {
      headers: { Accept: "application/json" }, cache: "no-store",
    });
    const ownershipPayload = ownershipResponse?.ok ? await json(ownershipResponse) : null;
    const visitIds = [...new Set(relationshipTargetIds(
      ownershipPayload,
      campaignId,
      CAMPAIGN_HAS_LOCATION_VISIT_RELATIONSHIP_KIND,
    ))];
    const visits = (await Promise.all(visitIds.map(async (visitId) => {
      const componentPath = `${listRoot}/${encodeURIComponent(visitId)}` +
        `/components/${CAMPAIGN_LOCATION_VISIT_COMPONENT_TYPE_ID}`;
      const targetPath = `${relationshipRoot}?fromEntityId=${encodeURIComponent(visitId)}` +
        `&qualifiedKind=${encodeURIComponent(CAMPAIGN_LOCATION_VISIT_AT_LOCATION_RELATIONSHIP_KIND)}&limit=2`;
      try {
        const [componentResponse, targetResponse] = await Promise.all([
          fetchImpl(url(origin, componentPath), { headers: { Accept: "application/json" }, cache: "no-store" }),
          fetchImpl(url(origin, targetPath), { headers: { Accept: "application/json" }, cache: "no-store" }),
        ]);
        if (!componentResponse?.ok || !targetResponse?.ok) return null;
        const [componentPayload, targetPayload] = await Promise.all([
          json(componentResponse), json(targetResponse),
        ]);
        const value = campaignLocationVisit(componentValue(
          componentPayload,
          visitId,
          CAMPAIGN_LOCATION_VISIT_COMPONENT_TYPE_ID,
        ), includeGmContext);
        const locationIds = [...new Set(relationshipTargetIds(
          targetPayload,
          visitId,
          CAMPAIGN_LOCATION_VISIT_AT_LOCATION_RELATIONSHIP_KIND,
        ))];
        return value && locationIds.length === 1
          ? { id: visitId, locationId: locationIds[0], ...value }
          : null;
      } catch {
        return null;
      }
    }))).filter(Boolean);
    const counts = new Map();
    for (const visit of visits) counts.set(visit.locationId, (counts.get(visit.locationId) ?? 0) + 1);
    return visits.filter((visit) => counts.get(visit.locationId) === 1)
      .sort((left, right) => right.lastVisitedMinute - left.lastVisitedMinute || left.id.localeCompare(right.id));
  } catch {
    return [];
  }
}

async function readPartyRoster({
  fetchImpl,
  origin,
  applicationId,
  stateSpaceId,
  campaignId,
  serverRole,
  perspective,
  boundActor,
  boundActorDetails,
  boundCanonicalResult,
  deferCharacterDetails = false,
}) {
  if (perspective === "player" && boundActor) {
    const entityRoot = `/api/applications/${encodeURIComponent(applicationId)}` +
      `/state-spaces/${encodeURIComponent(stateSpaceId)}/entities`;
    const mediaValue = await readEntityMedia(fetchImpl, origin, entityRoot, boundActor.id);
    const media = mediaValue ? projectMediaVisual(mediaValue) : null;
    return [{
      ...boundActor,
      ...boundActorDetails,
      canonicalResult: boundCanonicalResult,
      ...(deferCharacterDetails ? { detailsDeferred: true } : {}),
      ...(boundCanonicalResult?.status === "ready" ? { canonical: boundCanonicalResult.data } : {}),
      ...(media ? { media } : {}),
      current: true,
    }];
  }
  if (serverRole.role === "actor") return [];

  const applicationRoot = `/api/applications/${encodeURIComponent(applicationId)}` +
    `/state-spaces/${encodeURIComponent(stateSpaceId)}`;
  const entityRoot = `${applicationRoot}/entities`;
  const relationshipRoot = `${applicationRoot}/relationships`;
  const headers = { Accept: "application/json" };
  let campaignRelationships;
  try {
    const response = await fetchImpl(url(origin, `${relationshipRoot}` +
      `?fromEntityId=${encodeURIComponent(campaignId)}` +
      `&qualifiedKind=${encodeURIComponent(CAMPAIGN_HAS_PARTICIPATION_RELATIONSHIP_KIND)}` +
      "&limit=100"), { headers, cache: "no-store" });
    if (!response?.ok) return [];
    campaignRelationships = await json(response);
  } catch {
    return [];
  }

  const rawParticipationIds = relationshipTargetIds(
    campaignRelationships,
    campaignId,
    CAMPAIGN_HAS_PARTICIPATION_RELATIONSHIP_KIND,
  );
  const occurrenceCount = new Map();
  for (const id of rawParticipationIds) {
    occurrenceCount.set(id, (occurrenceCount.get(id) ?? 0) + 1);
  }
  const participationIds = rawParticipationIds.filter((id) => occurrenceCount.get(id) === 1);

  const members = await Promise.all(participationIds.map(async (participationId) => {
    try {
      const [componentResponse, actorRelationshipResponse] = await Promise.all([
        fetchImpl(url(origin, `${entityRoot}/${encodeURIComponent(participationId)}` +
          `/components/${CAMPAIGN_PARTICIPATION_COMPONENT_TYPE_ID}`), {
          headers,
          cache: "no-store",
        }),
        fetchImpl(url(origin, `${relationshipRoot}` +
          `?fromEntityId=${encodeURIComponent(participationId)}` +
          `&qualifiedKind=${encodeURIComponent(CAMPAIGN_PARTICIPATION_ACTOR_RELATIONSHIP_KIND)}` +
          "&limit=100"), { headers, cache: "no-store" }),
      ]);
      if (!componentResponse?.ok || !actorRelationshipResponse?.ok) return null;
      const [componentPayload, relationshipPayload] = await Promise.all([
        json(componentResponse),
        json(actorRelationshipResponse),
      ]);
      if (!activeCharacterParticipation(componentPayload, participationId)) return null;
      const actorIds = relationshipTargetIds(
        relationshipPayload,
        participationId,
        CAMPAIGN_PARTICIPATION_ACTOR_RELATIONSHIP_KIND,
      );
      if (actorIds.length !== 1) return null;
      const actorId = actorIds[0];
      const [actorResponse, recordResponse] = await Promise.all([
        fetchImpl(url(origin, `${entityRoot}/${encodeURIComponent(actorId)}`), {
          headers,
          cache: "no-store",
        }),
        perspective === "dm" && !deferCharacterDetails
          ? fetchImpl(url(origin, `${entityRoot}/${encodeURIComponent(actorId)}` +
            `/components/${PLAYTEST_CHARACTER_RECORD_COMPONENT_TYPE_ID}`), {
            headers,
            cache: "no-store",
          }).catch(() => null)
          : Promise.resolve(null),
      ]);
      if (!actorResponse?.ok) return null;
      const [actorPayload, recordPayload] = await Promise.all([
        json(actorResponse),
        recordResponse?.ok ? json(recordResponse) : Promise.resolve(null),
      ]);
      const actor = entity(actorPayload, actorId);
      if (!actor) return null;
      if (deferCharacterDetails) return { ...actor, state: null, entries: [], detailsDeferred: true, current: false };
      // A GM's Player preview retains identity only; the selected dossier is read separately.
      if (perspective === "player") return {
        ...actor, state: actor.state ?? null, entries: actor.entries ?? [], current: false,
      };
      const [canonicalResult, mediaValue] = await Promise.all([
        readCanonicalCharacter({ fetchImpl, origin, applicationId, stateSpaceId, actorId }),
        readEntityMedia(fetchImpl, origin, entityRoot, actorId),
      ]);
      const media = mediaValue ? projectMediaVisual(mediaValue) : null;
      return {
        ...actor,
        ...characterDetails(recordPayload
          ? componentValue(recordPayload, actorId, PLAYTEST_CHARACTER_RECORD_COMPONENT_TYPE_ID)
          : null),
        canonicalResult,
        ...(canonicalResult.status === "ready" ? { canonical: canonicalResult.data } : {}),
        ...(media ? { media } : {}),
        current: false,
      };
    } catch {
      return null;
    }
  }));

  const validMembers = members.filter(Boolean);
  const actorOccurrenceCount = new Map();
  for (const member of validMembers) {
    actorOccurrenceCount.set(member.id, (actorOccurrenceCount.get(member.id) ?? 0) + 1);
  }
  return validMembers
    .filter((member) => actorOccurrenceCount.get(member.id) === 1)
    .sort((left, right) => left.name.localeCompare(right.name) || left.id.localeCompare(right.id));
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
}) {
  const empty = { people: [], factions: [], holdings: [] };
  if (!applicationId || !stateSpaceId || !worldId || locationDirectory.length === 0) return empty;
  const listRoot = `/api/applications/${encodeURIComponent(applicationId)}` +
    `/state-spaces/${encodeURIComponent(stateSpaceId)}/entities`;
  const relationshipRoot = listRoot.replace(/\/entities$/u, "/relationships");
  const headers = { Accept: "application/json" };
  const entities = new Map();
  const seenCursors = new Set();
  let nextCursor = null;

  do {
    const pageUrl = listRoot + (
      nextCursor === null ? "?limit=100" : `?cursor=${encodeURIComponent(nextCursor)}&limit=100`
    );
    let response;
    try {
      response = await fetchImpl(url(origin, pageUrl), { headers, cache: "no-store" });
    } catch {
      return empty;
    }
    if (!response?.ok) return empty;
    const payload = await json(response);
    if (!payload || !Array.isArray(payload.items)) return empty;
    for (const item of payload.items) {
      const id = token(typeof item?.entityId === "string" ? item.entityId : item?.id);
      const name = text(item?.name, 200);
      if (id && name && entities.size < 1_000) entities.set(id, { id, name });
    }
    nextCursor = typeof payload.nextCursor === "string" && payload.nextCursor.length > 0
      ? payload.nextCursor
      : null;
    if (nextCursor && seenCursors.has(nextCursor)) nextCursor = null;
    else if (nextCursor) seenCursors.add(nextCursor);
  } while (nextCursor && entities.size < 1_000);

  const locationIds = new Set(locationDirectory.map((location) => location.id));
  const containedResults = await Promise.all(locationDirectory.map(async (location) => {
    const path = `${listRoot.replace(/\/entities$/u, "/containments")}` +
      `?containerEntityId=${encodeURIComponent(location.id)}&limit=100`;
    try {
      const response = await fetchImpl(url(origin, path), { headers, cache: "no-store" });
      const payload = response?.ok ? await json(response) : null;
      if (!payload || !Array.isArray(payload.items) || payload.items.length > 100) return [];
      return payload.items.flatMap((item) => {
        const containedEntityId = token(item?.containedEntityId);
        const containerEntityId = token(item?.containerEntityId);
        return containedEntityId && containerEntityId === location.id && entities.has(containedEntityId)
          ? [{ entityId: containedEntityId, locationId: location.id }]
          : [];
      });
    } catch {
      return [];
    }
  }));
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

  const factionCandidates = Array.from(entities.values()).filter((entry) =>
    entry.id === `faction.${worldId}` || entry.id.startsWith(`faction.${worldId}.`));
  const factions = (await Promise.all(factionCandidates.map(async (entry) => {
    const componentPath = `${listRoot}/${encodeURIComponent(entry.id)}/components/${WORLD_FACTION_COMPONENT_TYPE_ID}`;
    const relationshipRoot = `${listRoot.replace(/\/entities$/u, "/relationships")}`;
    try {
      const [componentResponse, ...relationshipResponses] = await Promise.all([
        fetchImpl(url(origin, componentPath), { headers, cache: "no-store" }),
        ...Object.values(WORLD_FACTION_RELATIONSHIP_KINDS).map((kind) =>
          fetchImpl(url(origin, `${relationshipRoot}?fromEntityId=${encodeURIComponent(entry.id)}` +
            `&qualifiedKind=${encodeURIComponent(kind)}&limit=100`), { headers, cache: "no-store" })
            .catch(() => null)),
      ]);
      if (!componentResponse?.ok) return null;
      const componentPayload = await json(componentResponse);
      const record = worldFaction(componentValue(componentPayload, entry.id, WORLD_FACTION_COMPONENT_TYPE_ID));
      if (!record || record.status !== "active") return null;
      const relationshipPayloads = await Promise.all(relationshipResponses.map((response) =>
        response?.ok ? json(response) : Promise.resolve(null)));
      const byKind = Object.fromEntries(Object.entries(WORLD_FACTION_RELATIONSHIP_KINDS).map(
        ([key, kind], index) => [key, relationshipTargetIds(relationshipPayloads[index], entry.id, kind)],
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
  }))).filter(Boolean);

  return {
    people: people.sort((left, right) => left.name.localeCompare(right.name) || left.id.localeCompare(right.id)),
    factions: factions.sort((left, right) => left.name.localeCompare(right.name) || left.id.localeCompare(right.id)),
    holdings: holdings.sort((left, right) => left.name.localeCompare(right.name) || left.id.localeCompare(right.id)),
  };
}

function campaignChildType(entityId, campaignId) {
  if (entityId.startsWith(`${campaignId}.chapter.`)) return "chapter";
  if (entityId.startsWith(`${campaignId}.arc.`)) return "arc";
  return null;
}

async function readCampaignStructure({
  fetchImpl,
  origin,
  applicationId,
  stateSpaceId,
  campaignId,
  includeGmContext,
}) {
  const empty = { chapters: [], arcs: [], sessions: [], visits: [] };
  if (!applicationId || !stateSpaceId || !campaignId) return empty;
  const listRoot = `/api/applications/${encodeURIComponent(applicationId)}` +
    `/state-spaces/${encodeURIComponent(stateSpaceId)}/entities`;
  const relationshipRoot = listRoot.replace(/\/entities$/u, "/relationships");
  const headers = { Accept: "application/json" };
  const candidates = new Map();
  const seenCursors = new Set();
  let nextCursor = null;

  do {
    const pageUrl = listRoot + (
      nextCursor === null ? "?limit=100" : `?cursor=${encodeURIComponent(nextCursor)}&limit=100`
    );
    let response;
    try {
      response = await fetchImpl(url(origin, pageUrl), { headers, cache: "no-store" });
    } catch {
      return empty;
    }
    if (!response?.ok) return empty;
    const payload = await json(response);
    if (!payload || !Array.isArray(payload.items)) return empty;

    for (const item of payload.items) {
      const id = token(typeof item?.entityId === "string" ? item.entityId : item?.id);
      const kind = id ? campaignChildType(id, campaignId) : null;
      if (!id || !kind || candidates.size >= 100) continue;
      candidates.set(id, {
        id,
        kind,
        createdAtUtc: text(item?.createdAtUtc, 64),
      });
    }

    nextCursor = typeof payload.nextCursor === "string" && payload.nextCursor.length > 0
      ? payload.nextCursor
      : null;
    if (nextCursor && seenCursors.has(nextCursor)) {
      nextCursor = null;
    } else if (nextCursor) {
      seenCursors.add(nextCursor);
    }
  } while (nextCursor && candidates.size < 100);

  const records = await Promise.all(Array.from(candidates.values()).map(async (candidate) => {
    const typeId = candidate.kind === "chapter"
      ? CAMPAIGN_CHAPTER_COMPONENT_TYPE_ID
      : CAMPAIGN_ARC_COMPONENT_TYPE_ID;
    const componentPath = `${listRoot}/${encodeURIComponent(candidate.id)}` +
      `/components/${typeId}`;
    try {
      const response = await fetchImpl(url(origin, componentPath), { headers, cache: "no-store" });
      if (!response?.ok) return null;
      const payload = await json(response);
      const parsed = componentValue(payload, candidate.id, typeId);
      const value = candidate.kind === "chapter"
        ? campaignChapter(parsed, includeGmContext)
        : campaignArc(parsed, includeGmContext);
      if (!value) return null;
      const terminal = candidate.kind === "chapter"
        ? value.status === "closed"
        : value.status !== "active";
      const worldEntityIds = includeGmContext && terminal
        ? await campaignRecordWorldEntityIds({
            fetchImpl, origin, relationshipRoot, recordId: candidate.id,
          })
        : [];
      return {
        kind: candidate.kind,
        record: {
          id: candidate.id,
          createdAtUtc: candidate.createdAtUtc,
          updatedAtUtc: text(payload?.updatedAtUtc, 64),
          ...value,
          ...(worldEntityIds.length > 0 ? { worldEntityIds } : {}),
        },
      };
    } catch {
      return null;
    }
  }));

  const compare = (left, right) =>
    (left.createdAtUtc ?? "").localeCompare(right.createdAtUtc ?? "") || left.id.localeCompare(right.id);
  let sessions = [];
  if (includeGmContext) try {
    const relationshipPath = `${relationshipRoot}` +
      `?fromEntityId=${encodeURIComponent(campaignId)}` +
      `&qualifiedKind=${encodeURIComponent(CAMPAIGN_HAS_SESSION_RELATIONSHIP_KIND)}&limit=100`;
    const relationshipResponse = await fetchImpl(url(origin, relationshipPath), { headers, cache: "no-store" });
    const relationshipPayload = relationshipResponse?.ok ? await json(relationshipResponse) : null;
    const sessionIds = relationshipTargetIds(
      relationshipPayload,
      campaignId,
      CAMPAIGN_HAS_SESSION_RELATIONSHIP_KIND,
    );
    sessions = (await Promise.all(sessionIds.map(async (sessionId) => {
      const componentRoot = `${listRoot}/${encodeURIComponent(sessionId)}/components`;
      try {
        const [sessionResponse, recapResponse] = await Promise.all([
          fetchImpl(url(origin, `${componentRoot}/${CAMPAIGN_SESSION_COMPONENT_TYPE_ID}`),
            { headers, cache: "no-store" }),
          fetchImpl(url(origin, `${componentRoot}/${CAMPAIGN_SESSION_RECAP_COMPONENT_TYPE_ID}`),
            { headers, cache: "no-store" }).catch(() => null),
        ]);
        if (!sessionResponse?.ok) return null;
        const [sessionPayload, recapPayload] = await Promise.all([
          json(sessionResponse),
          recapResponse?.ok ? json(recapResponse) : Promise.resolve(null),
        ]);
        const session = campaignSession(componentValue(
          sessionPayload,
          sessionId,
          CAMPAIGN_SESSION_COMPONENT_TYPE_ID,
        ));
        if (!session) return null;
        const recap = session.status === "ended"
          ? campaignSessionRecap(componentValue(
            recapPayload,
            sessionId,
            CAMPAIGN_SESSION_RECAP_COMPONENT_TYPE_ID,
          ))
          : null;
        if (session.status === "ended" && !recap) return null;
        const worldEntityIds = session.status === "ended"
          ? await campaignRecordWorldEntityIds({ fetchImpl, origin, relationshipRoot, recordId: sessionId })
          : [];
        return {
          id: sessionId,
          ...session,
          updatedAtUtc: text(sessionPayload?.updatedAtUtc, 64),
          ...(recap ? { recap } : {}),
          ...(worldEntityIds.length > 0 ? { worldEntityIds } : {}),
        };
      } catch {
        return null;
      }
    }))).filter(Boolean).sort((left, right) => left.ordinal - right.ordinal || left.id.localeCompare(right.id));
  } catch {
    sessions = [];
  }
  const visits = await readCampaignLocationVisits({
    fetchImpl,
    origin,
    listRoot,
    relationshipRoot,
    campaignId,
    includeGmContext,
  });
  return {
    chapters: records.filter((value) => value?.kind === "chapter").map((value) => value.record).sort(compare),
    arcs: records.filter((value) => value?.kind === "arc").map((value) => value.record).sort(compare),
    sessions,
    visits,
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
 *   localSeat?: string, // Legacy option accepted but never used to override the server seat.
 *   mediaAssetBaseUrl?: string,
 *   deferCharacterDetails?: boolean,
 * }} options
 */
export async function readGameServerContext({
  serverOrigin,
  fetchImpl = fetch,
  requestedPerspective = "dm",
  requestedCampaignId = null,
  mediaAssetBaseUrl = "/ui/dnd2024-play/assets/",
  deferCharacterDetails = false,
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
  const canReadBoundKnowledge = !isGameMaster || contextAudience.perspective === "dm";
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
  const contextSelection = await readContextSelection({
    fetchImpl,
    origin,
    applicationId: binding.applicationId,
    stateSpaceId: binding.stateSpaceId,
    boundCampaignId: binding.campaignId,
    isGameMaster: serverRole.role === "game-master",
  });
  const selectedContext = selectContext(contextSelection, requestedCampaign ?? binding.campaignId);
  if (!selectedContext) {
    return denied("That campaign is not available to this local table.");
  }
  const selectedCampaignId = selectedContext.selectedCampaignId;

  const root = `/api/applications/${encodeURIComponent(binding.applicationId)}` +
    `/state-spaces/${encodeURIComponent(binding.stateSpaceId)}/entities`;
  let campaignResponse;
  let actorResponse;
  let campaignComponentResponse;
  let currentSceneComponentResponse;
  let actorComponentResponse;
  let knowledgeResponse;
  let chronologyResponse;
  let playSessionResponse;
  try {
    [campaignResponse, actorResponse, campaignComponentResponse, currentSceneComponentResponse,
      actorComponentResponse, knowledgeResponse, chronologyResponse, playSessionResponse] = await Promise.all([
      fetchImpl(url(origin, `${root}/${encodeURIComponent(selectedCampaignId)}`), {
        headers: { Accept: "application/json" }, cache: "no-store",
      }),
      shouldReadBoundActor
        ? fetchImpl(url(origin, `${root}/${encodeURIComponent(binding.actorId)}`), {
          headers: { Accept: "application/json" }, cache: "no-store",
        })
        : Promise.resolve(null),
      fetchImpl(url(origin, `${root}/${encodeURIComponent(selectedCampaignId)}` +
        `/components/${CAMPAIGN_ROOT_COMPONENT_TYPE_ID}`), {
        headers: { Accept: "application/json" }, cache: "no-store",
      }),
      fetchImpl(url(origin, `${root}/${encodeURIComponent(selectedCampaignId)}` +
        `/components/${CAMPAIGN_CURRENT_SCENE_COMPONENT_TYPE_ID}`), {
        headers: { Accept: "application/json" }, cache: "no-store",
      }).catch(() => null),
      shouldReadBoundActor && !deferCharacterDetails
        ? fetchImpl(url(origin, `${root}/${encodeURIComponent(binding.actorId)}` +
          `/components/${PLAYTEST_CHARACTER_RECORD_COMPONENT_TYPE_ID}`), {
          headers: { Accept: "application/json" }, cache: "no-store",
        })
        : Promise.resolve(null),
      canReadBoundKnowledge
        ? fetchImpl(url(origin, `/api/applications/${encodeURIComponent(binding.applicationId)}` +
          `/campaigns/${encodeURIComponent(selectedCampaignId)}/knowledge`), {
          headers: { Accept: "application/json" }, cache: "no-store",
        }).catch(() => null)
        : Promise.resolve(null),
      fetchImpl(url(origin, `/api/applications/${encodeURIComponent(binding.applicationId)}` +
        `/campaigns/${encodeURIComponent(selectedCampaignId)}/chronology` +
        `?perspective=${encodeURIComponent(contextAudience.perspective ?? "player")}`), {
        headers: { Accept: "application/json" }, cache: "no-store",
      }).catch(() => null),
      fetchImpl(url(origin, `/api/applications/${encodeURIComponent(binding.applicationId)}` +
        `/state-spaces/${encodeURIComponent(binding.stateSpaceId)}/play/sessions/` +
        encodeURIComponent(selectedCampaignId)), {
        headers: { Accept: "application/json" }, cache: "no-store",
      }).catch(() => null),
    ]);
  } catch {
    return unavailable("The campaign binding was found, but the game state could not be read.");
  }

  const [campaign, actor, campaignComponent, currentSceneComponent, actorComponent, knowledgeEnvelope,
    chronologyEnvelope, playSessionEnvelope] = await Promise.all([
    json(campaignResponse),
    json(actorResponse),
    json(campaignComponentResponse),
    json(currentSceneComponentResponse),
    json(actorComponentResponse),
    json(knowledgeResponse),
    json(chronologyResponse),
    json(playSessionResponse),
  ]);
  const campaignEntity = campaignResponse?.ok ? entity(campaign, selectedCampaignId) : null;
  const boundActorEntity = shouldReadBoundActor && actorResponse?.ok
    ? entity(actor, binding.actorId)
    : null;
  const actorEntity = isGameMaster
    ? { id: "local-game-master", name: "Dungeon Master" }
    : boundActorEntity;
  if (!campaignEntity || !actorEntity) {
    return unavailable("The campaign binding no longer matches readable game state.");
  }
  // Resolve the small, authoritative party graph before the optional World and knowledge
  // directories fan out into many reads. Otherwise a large DM projection can consume the shared
  // read budget first and make a valid active participant look like an empty roster.
  const boundActorDetails = characterDetails(actorComponentResponse?.ok
    && shouldReadBoundActor
    ? componentValue(actorComponent, binding.actorId, PLAYTEST_CHARACTER_RECORD_COMPONENT_TYPE_ID)
    : null);
  const boundCanonicalResult = shouldReadBoundActor && !deferCharacterDetails
    ? await readCanonicalCharacter({
      fetchImpl,
      origin,
      applicationId: binding.applicationId,
      stateSpaceId: binding.stateSpaceId,
      actorId: binding.actorId,
    })
    : null;
  const party = await readPartyRoster({
    fetchImpl,
    origin,
    applicationId: binding.applicationId,
    stateSpaceId: binding.stateSpaceId,
    campaignId: selectedCampaignId,
    serverRole,
    perspective: effectivePerspective,
    boundActor: boundActorEntity,
    boundActorDetails,
    boundCanonicalResult,
    deferCharacterDetails,
  });
  let projectedKnowledge = knowledgeResponse?.ok
    ? knowledge(knowledgeEnvelope)
    : { status: "unavailable", entries: [], locations: [] };
  projectedKnowledge = await attachAuthorizedKnowledgeMedia({
    fetchImpl,
    origin,
    entityRoot: root,
    projectedKnowledge,
    perspective: contextAudience.perspective ?? "player",
    mediaAssetBaseUrl,
  });
  const projectedChronology = chronologyResponse?.ok
    ? chronology(chronologyEnvelope, contextAudience.perspective ?? "player")
    : { status: "unavailable", perspective: contextAudience.perspective ?? "player", entries: [] };
  const [locationDirectory, campaignStructure] = await Promise.all([
    readLocationDirectory({
      fetchImpl,
      origin,
      applicationId: binding.applicationId,
      stateSpaceId: binding.stateSpaceId,
      worldId: campaignWorldId(selectedCampaignId),
      perspective: contextAudience.perspective,
      mediaAssetBaseUrl,
    }),
    readCampaignStructure({
      fetchImpl,
      origin,
      applicationId: binding.applicationId,
      stateSpaceId: binding.stateSpaceId,
      campaignId: selectedCampaignId,
      includeGmContext: contextAudience.perspective === "dm",
    }),
  ]);
  let currentLocationId = null;
  if (shouldReadBoundActor && locationDirectory.length > 0) {
    try {
      const containmentResponse = await fetchImpl(url(origin,
        `${root}/${encodeURIComponent(binding.actorId)}/containment`), {
        headers: { Accept: "application/json" }, cache: "no-store",
      });
      const containmentPayload = containmentResponse?.ok ? await json(containmentResponse) : null;
      currentLocationId = resolvePresenceLocation(
        containmentPayload,
        binding.actorId,
        locationDirectory.map((location) => location.id),
      );
    } catch {
      currentLocationId = null;
    }
  }
  const worldDirectory = contextAudience.seat === "dm" && contextAudience.perspective === "dm"
    ? await readWorldDirectory({
      fetchImpl,
      origin,
      applicationId: binding.applicationId,
      stateSpaceId: binding.stateSpaceId,
      worldId: campaignWorldId(selectedCampaignId),
      locationDirectory,
      perspective: contextAudience.perspective,
      mediaAssetBaseUrl,
    })
    : null;
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

  return {
    version: 1,
    status: "connected",
    applicationId: binding.applicationId,
    stateSpaceId: binding.stateSpaceId,
    audience: contextAudience,
    contextSelection: updateSelectedCampaignName(selectedContext, campaignEntity),
    campaign: {
      ...campaignEntity,
      ...campaignDetails(campaignComponentResponse.ok
        ? componentValue(campaignComponent, selectedCampaignId, CAMPAIGN_ROOT_COMPONENT_TYPE_ID)
        : null),
      ...campaignStructure,
    },
    actor: {
      ...actorEntity,
      ...boundActorDetails,
    },
    ...(currentLocationId ? { currentLocationId } : {}),
    currentSituation,
    ...(knownRoutes.length > 0 ? { knownRoutes } : {}),
    party,
    knowledge: projectedKnowledge,
    chronology: projectedChronology,
    ...(locationDirectory.length > 0 ? { locationDirectoryAudience: contextAudience.perspective } : {}),
    ...(locationDirectory.length > 0 ? { locationDirectory } : {}),
    ...(worldDirectory && (
      worldDirectory.people.length > 0 || worldDirectory.factions.length > 0 || worldDirectory.holdings.length > 0
    ) ? { worldDirectory } : {}),
  };
}
