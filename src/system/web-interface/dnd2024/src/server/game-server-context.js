import { resolveMediaAssetUrl } from "../data/media-assets.js";

const TOKEN_MAXIMUM = 200;
const LOCATION_COMPONENT_TYPE_ID = "dnd2024.game.core.world.location";
const WORLD_MAP_ANCHOR_COMPONENT_TYPE_ID = "dnd2024.game.core.world.map.anchor";
const WORLD_MAP_VISUAL_COMPONENT_TYPE_ID = "dnd2024.game.core.world.map.visual";
const WORLD_MEDIA_VISUAL_COMPONENT_TYPE_ID = "dnd2024.game.core.world.media.visual";
const WORLD_ROUTE_COMPONENT_TYPE_ID = "dnd2024.game.core.world.route";
const WORLD_ROUTE_AVAILABILITY_COMPONENT_TYPE_ID = "dnd2024.game.core.world.route.availability";
const WORLD_ROUTE_RELATIONSHIP_KINDS = {
  world: "dnd2024.game.core.world.route.in-world",
  origin: "dnd2024.game.core.world.route.from",
  destination: "dnd2024.game.core.world.route.to",
};
const CAMPAIGN_ROOT_COMPONENT_TYPE_ID = "dnd2024.game.core.campaign.root";
const CAMPAIGN_CURRENT_SCENE_COMPONENT_TYPE_ID = "dnd2024.game.core.campaign.current-scene";
const CAMPAIGN_SCENE_AFFORDANCES_COMPONENT_TYPE_ID =
  "dnd2024.game.core.campaign.scene-affordances";
const CAMPAIGN_CHAPTER_COMPONENT_TYPE_ID = "dnd2024.game.core.campaign.chapter";
const CAMPAIGN_ARC_COMPONENT_TYPE_ID = "dnd2024.game.core.campaign.arc";
const CAMPAIGN_SESSION_COMPONENT_TYPE_ID = "dnd2024.game.core.campaign.session";
const CAMPAIGN_SESSION_RECAP_COMPONENT_TYPE_ID = "dnd2024.game.core.campaign.session-recap";
const CAMPAIGN_LOCATION_VISIT_COMPONENT_TYPE_ID = "dnd2024.game.core.campaign.location-visit";
const CAMPAIGN_HAS_SESSION_RELATIONSHIP_KIND = "dnd2024.game.core.campaign.has-session";
const CAMPAIGN_HAS_LOCATION_VISIT_RELATIONSHIP_KIND =
  "dnd2024.game.core.campaign.has-location-visit";
const CAMPAIGN_LOCATION_VISIT_AT_LOCATION_RELATIONSHIP_KIND =
  "dnd2024.game.core.campaign.location-visit.at-location";
const CAMPAIGN_RECORD_WORLD_REFERENCE_RELATIONSHIP_KIND =
  "dnd2024.game.core.campaign.record.references-world-entity";
const CAMPAIGN_PARTICIPATION_COMPONENT_TYPE_ID = "dnd2024.game.core.campaign.character-participation";
const CAMPAIGN_HAS_PARTICIPATION_RELATIONSHIP_KIND =
  "dnd2024.game.core.campaign.has-character-participation";
const CAMPAIGN_PARTICIPATION_ACTOR_RELATIONSHIP_KIND =
  "dnd2024.game.core.campaign.character-participation.for-actor";
const PLAYTEST_CHARACTER_RECORD_COMPONENT_TYPE_ID = "dnd2024.playtest-character-record";
const WORLD_INTERACTION_COMPONENT_TYPE_ID = "dnd2024.game.core.world.interaction";
const WORLD_INTERACTION_PARTICIPANT_RELATIONSHIP_KIND =
  "dnd2024.game.core.world.interaction.participant";
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
const CHARACTER_IDENTITY_COMPONENT_TYPE_ID = "dnd2024.character.identity";
const CHARACTER_ORIGIN_COMPONENT_TYPE_ID = "dnd2024.character.origin-selections";
const CHARACTER_EXPERIENCE_COMPONENT_TYPE_ID = "dnd2024.character.experience";
const CHARACTER_CLASS_MEMBERSHIP_COMPONENT_TYPE_ID = "dnd2024.character.class-membership";
const CHARACTER_CLASS_MEMBERSHIP_RELATIONSHIP_KIND = "dnd2024.character.has-class-membership";
const CREATURE_ABILITY_SCORES_COMPONENT_TYPE_ID = "dnd2024.creature.ability-scores";
const CREATURE_HIT_POINTS_COMPONENT_TYPE_ID = "dnd2024.creature.hit-points";
const CREATURE_TEMPORARY_HIT_POINTS_COMPONENT_TYPE_ID = "dnd2024.creature.temporary-hit-points";
const CREATURE_BODY_COMPONENT_TYPE_ID = "dnd2024.creature.body";
const CREATURE_MOVEMENT_COMPONENT_TYPE_ID = "dnd2024.creature.movement";
const CREATURE_PROFICIENCIES_COMPONENT_TYPE_ID = "dnd2024.creature.proficiencies";
const ITEM_DEFINITION_LINK_COMPONENT_TYPE_ID = "dnd2024.core.definition-link";
const ITEM_QUANTITY_COMPONENT_TYPE_ID = "dnd2024.item.quantity";
const ITEM_EQUIPMENT_COMPONENT_TYPE_ID = "dnd2024.item.equipment";
const CANONICAL_CHARACTER_COMPONENT_IDS = [
  CHARACTER_IDENTITY_COMPONENT_TYPE_ID,
  CHARACTER_ORIGIN_COMPONENT_TYPE_ID,
  CHARACTER_EXPERIENCE_COMPONENT_TYPE_ID,
  CREATURE_ABILITY_SCORES_COMPONENT_TYPE_ID,
  CREATURE_HIT_POINTS_COMPONENT_TYPE_ID,
  CREATURE_TEMPORARY_HIT_POINTS_COMPONENT_TYPE_ID,
  CREATURE_BODY_COMPONENT_TYPE_ID,
  CREATURE_MOVEMENT_COMPONENT_TYPE_ID,
  CREATURE_PROFICIENCIES_COMPONENT_TYPE_ID,
];
const WORLD_MOTIVE_COMPONENT_TYPE_ID = "dnd2024.game.core.world.motive";
const WORLD_FACTION_COMPONENT_TYPE_ID = "dnd2024.game.core.world.faction";
const WORLD_FACTION_RELATIONSHIP_KINDS = {
  members: "dnd2024.game.core.world.faction.member",
  controls: "dnd2024.game.core.world.faction.controls",
  territories: "dnd2024.game.core.world.faction.territory-controls",
  allies: "dnd2024.game.core.world.faction.allied-with",
  opponents: "dnd2024.game.core.world.faction.opposed-to",
};
const DEVELOPMENT_SEAT = new Set(["player", "dm"]);

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

function normalizeSeat(value) {
  return typeof value === "string" && DEVELOPMENT_SEAT.has(value) ? value : null;
}

function overrideServerRole(binding, localSeatOverride) {
  if (localSeatOverride !== "dm") return binding;
  if (binding?.role !== "actor") return binding;
  return { status: binding.status, applicationId: binding.applicationId, stateSpaceId: binding.stateSpaceId, campaignId: binding.campaignId, role: "game-master" };
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

function mapVisual(value, expectedEntityId, perspective) {
  const parsed = componentValue(value, expectedEntityId, WORLD_MAP_VISUAL_COMPONENT_TYPE_ID);
  if (!hasExactKeys(parsed, ["status", "variants"]) || parsed.status !== "active") return null;
  if (!parsed.variants || typeof parsed.variants !== "object" || Array.isArray(parsed.variants)) return null;
  const allowedVariantKeys = new Set(["player", "dm"]);
  if (Object.keys(parsed.variants).some((key) => !allowedVariantKeys.has(key))) return null;
  const variant = parsed.variants[perspective];
  if (!hasExactKeys(variant, ["assetKey", "alt"])) return null;
  const assetKey = text(variant.assetKey, 200);
  const alt = text(variant.alt, 1000);
  return assetKey && /^[a-z0-9]+(?:[.-][a-z0-9]+)*$/u.test(assetKey) && alt
    ? { assetKey, alt }
    : null;
}

function validMediaVariant(value) {
  if (!hasExactKeys(value, ["assetKey", "alt", "mimeType", "width", "height", "sha256"])) return false;
  return text(value.assetKey, 128) && /^[a-z0-9]+(?:[.-][a-z0-9]+)*$/u.test(value.assetKey) &&
    text(value.alt, 500) && /\S/u.test(value.alt) &&
    ["image/png", "image/jpeg", "image/webp"].includes(value.mimeType) &&
    Number.isInteger(value.width) && value.width >= 1 && value.width <= 10_000 &&
    Number.isInteger(value.height) && value.height >= 1 && value.height <= 10_000 &&
    typeof value.sha256 === "string" && /^[a-f0-9]{64}$/u.test(value.sha256);
}

function validMediaProvenance(value) {
  return hasExactKeys(value, ["kind", "credit", "source", "reviewedOn", "version"]) &&
    ["generated", "original", "commissioned", "licensed"].includes(value.kind) &&
    text(value.credit, 500) && /\S/u.test(value.credit) &&
    text(value.source, 500) && /\S/u.test(value.source) &&
    typeof value.reviewedOn === "string" && /^\d{4}-\d{2}-\d{2}$/u.test(value.reviewedOn) &&
    Number.isInteger(value.version) && value.version >= 1 && value.version <= 1_000_000;
}

export function projectMediaVisual(value, perspective, assetBaseUrl = "/") {
  if (!hasExactKeys(value, ["status", "slots"]) || value.status !== "active" ||
      !["player", "dm"].includes(perspective) || !value.slots ||
      typeof value.slots !== "object" || Array.isArray(value.slots)) return null;
  const allowedSlots = new Set(["portrait", "setting", "scene", "handout"]);
  const slotEntries = Object.entries(value.slots);
  if (slotEntries.length === 0 || slotEntries.some(([slot]) => !allowedSlots.has(slot))) return null;
  const projected = {};
  for (const [slotName, slot] of slotEntries) {
    if (!hasExactKeys(slot, ["variants", "provenance"]) || !validMediaProvenance(slot.provenance) ||
        !slot.variants || typeof slot.variants !== "object" || Array.isArray(slot.variants)) return null;
    const variantEntries = Object.entries(slot.variants);
    if (variantEntries.length === 0 || variantEntries.some(([name, variant]) =>
      !["player", "dm"].includes(name) || !validMediaVariant(variant))) return null;
    const selected = slot.variants[perspective];
    if (!selected) continue;
    const imageUrl = resolveMediaAssetUrl(
      selected.assetKey,
      selected.sha256,
      selected.mimeType,
      assetBaseUrl,
    );
    if (!imageUrl) return null;
    projected[slotName] = {
      imageUrl,
      alt: selected.alt,
      width: selected.width,
      height: selected.height,
    };
  }
  return Object.keys(projected).length > 0 ? projected : null;
}

function mediaVisual(value, expectedEntityId, perspective, assetBaseUrl) {
  const parsed = componentValue(value, expectedEntityId, WORLD_MEDIA_VISUAL_COMPONENT_TYPE_ID);
  return parsed ? projectMediaVisual(parsed, perspective, assetBaseUrl) : null;
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

function canonicalIdentity(value) {
  if (!value || typeof value !== "object" || Array.isArray(value)) return null;
  const allowed = new Set(["pronouns", "appearance", "biography", "playerNotes"]);
  if (Object.keys(value).length === 0 || Object.keys(value).some((key) => !allowed.has(key))) return null;
  const result = {};
  for (const [key, maximum] of [["pronouns", 200], ["appearance", 5_000], ["biography", 20_000], ["playerNotes", 20_000]]) {
    if (value[key] === undefined) continue;
    const normalized = text(value[key], maximum);
    if (!normalized) return null;
    result[key] = normalized;
  }
  return Object.keys(result).length > 0 ? result : null;
}

function canonicalOrigin(value) {
  if (!hasExactKeys(value, ["backgroundRef", "speciesRef"])) return null;
  const speciesId = referenceId(value.speciesRef);
  const backgroundId = referenceId(value.backgroundRef);
  return speciesId && backgroundId ? { speciesId, backgroundId } : null;
}

function canonicalAbilityScores(value) {
  if (!hasExactKeys(value, ["scores"]) || !value.scores || typeof value.scores !== "object" || Array.isArray(value.scores)) return null;
  const entries = Object.entries(value.scores).flatMap(([id, score]) => {
    const abilityId = token(id);
    return abilityId && Number.isInteger(score) && score >= 0 && score <= 100
      ? [{ id: abilityId, score }]
      : [];
  });
  return entries.length === Object.keys(value.scores).length && entries.length > 0
    ? entries.sort((left, right) => left.id.localeCompare(right.id))
    : null;
}

function canonicalHitPoints(value) {
  if (!value || typeof value !== "object" || Array.isArray(value)) return null;
  const allowed = new Set(["current", "maximum", "maximumReduction"]);
  if (Object.keys(value).some((key) => !allowed.has(key)) ||
      !Number.isSafeInteger(value.current) || value.current < 0 ||
      !Number.isSafeInteger(value.maximum) || value.maximum < 1 || value.current > value.maximum) return null;
  if (value.maximumReduction !== undefined &&
      (!Number.isSafeInteger(value.maximumReduction) || value.maximumReduction < 0)) return null;
  return {
    current: value.current,
    maximum: value.maximum,
    ...(value.maximumReduction !== undefined ? { maximumReduction: value.maximumReduction } : {}),
  };
}

function canonicalTemporaryHitPoints(value) {
  if (!hasExactKeys(value, ["amount", "sourceRef"]) ||
      !Number.isSafeInteger(value.amount) || value.amount < 1 || !referenceId(value.sourceRef)) return null;
  return { amount: value.amount };
}

function canonicalBody(value) {
  if (!value || typeof value !== "object" || Array.isArray(value)) return null;
  const allowed = new Set(["sizeRef", "activeFormRef", "bodyStateRef"]);
  if (Object.keys(value).some((key) => !allowed.has(key))) return null;
  const sizeId = referenceId(value.sizeRef);
  return sizeId ? { sizeId } : null;
}

function canonicalMovement(value) {
  if (!hasExactKeys(value, ["speeds"]) || !value.speeds || typeof value.speeds !== "object" || Array.isArray(value.speeds)) return null;
  const parsed = Object.entries(value.speeds).map(([modeId, speed]) => {
    const id = token(modeId);
    const distance = speed?.distance;
    const numerator = distance?.value?.numerator;
    const denominator = distance?.value?.denominator;
    const unitId = referenceId(distance?.unit);
    if (!id || typeof speed?.enabled !== "boolean" || distance?.dimension !== "distance" ||
        !Number.isSafeInteger(numerator) || numerator < 0 ||
        !Number.isSafeInteger(denominator) || denominator < 1 || !unitId ||
        !Array.isArray(speed.sourceRefs) || speed.sourceRefs.length === 0 ||
        speed.sourceRefs.some((entry) => !referenceId(entry))) return null;
    return speed.enabled
      ? { id, numerator, denominator, unitId }
      : false;
  });
  if (parsed.some((entry) => entry === null)) return null;
  return parsed.filter(Boolean).sort((left, right) => left.id.localeCompare(right.id));
}

function canonicalProficiencies(value) {
  if (!value || typeof value !== "object" || Array.isArray(value) ||
      !value.entries || typeof value.entries !== "object" || Array.isArray(value.entries) ||
      !Array.isArray(value.recordedFamilies)) return null;
  const entries = Object.entries(value.entries).flatMap(([id, entry]) => {
    const proficiencyId = token(id);
    const rankId = referenceId(entry?.rankRef);
    return proficiencyId && rankId ? [{ id: proficiencyId, rankId }] : [];
  });
  return entries.length === Object.keys(value.entries).length
    ? entries.sort((left, right) => left.id.localeCompare(right.id))
    : null;
}

function canonicalExperience(value) {
  return hasExactKeys(value, ["total"]) && Number.isSafeInteger(value.total) && value.total >= 0
    ? { total: value.total }
    : null;
}

function canonicalClassMembership(value, id, name) {
  if (!value || typeof value !== "object" || Array.isArray(value)) return null;
  const allowed = new Set(["classRef", "level", "subclassRef"]);
  if (Object.keys(value).some((key) => !allowed.has(key))) return null;
  const classId = referenceId(value.classRef);
  const subclassId = value.subclassRef === undefined ? null : referenceId(value.subclassRef);
  if (!classId || (value.subclassRef !== undefined && !subclassId) ||
      !Number.isInteger(value.level) || value.level < 1 || value.level > 20) return null;
  return { id, name, classId, level: value.level, ...(subclassId ? { subclassId } : {}) };
}

function canonicalItem(linkValue, quantityValue, equipmentValue, containment, item) {
  if (!linkValue || typeof linkValue !== "object" || Array.isArray(linkValue)) return null;
  const linkKeys = Object.keys(linkValue).sort().join(",");
  const definitionId = (linkKeys === "definition" || linkKeys === "definition,definitionRevision")
    ? referenceId(linkValue.definition)
    : null;
  if (!definitionId || !hasExactKeys(quantityValue, ["current"]) ||
      !Number.isSafeInteger(quantityValue.current) || quantityValue.current < 1) return null;
  let equipmentSlots = [];
  if (equipmentValue !== null) {
    if (!equipmentValue || typeof equipmentValue !== "object" || Array.isArray(equipmentValue) ||
        Object.keys(equipmentValue).some((key) => !["equippedBy", "slots", "configuration"].includes(key)) ||
        !referenceId(equipmentValue.equippedBy) || !Array.isArray(equipmentValue.slots) ||
        equipmentValue.slots.length === 0) return null;
    equipmentSlots = equipmentValue.slots.map(referenceId);
    if (equipmentSlots.some((entry) => !entry) || new Set(equipmentSlots).size !== equipmentSlots.length) return null;
  }
  return {
    id: item.id,
    name: item.name,
    definitionId,
    quantity: quantityValue.current,
    slot: containment.slot,
    equipmentSlots,
  };
}

async function readCanonicalCharacter({ fetchImpl, origin, applicationId, stateSpaceId, actorId }) {
  const applicationRoot = `/api/applications/${encodeURIComponent(applicationId)}` +
    `/state-spaces/${encodeURIComponent(stateSpaceId)}`;
  const entityRoot = `${applicationRoot}/entities`;
  const headers = { Accept: "application/json" };
  let summaries;
  try {
    const response = await fetchImpl(url(origin, `${entityRoot}/${encodeURIComponent(actorId)}/components?limit=100`), {
      headers,
      cache: "no-store",
    });
    if (!response?.ok) return null;
    summaries = await json(response);
  } catch {
    return null;
  }
  if (!Array.isArray(summaries?.items) || summaries.items.length > 100) return null;
  const present = new Set(summaries.items.flatMap((item) => {
    const id = token(item?.qualifiedTypeId);
    return id ? [id] : [];
  }));
  const componentIds = CANONICAL_CHARACTER_COMPONENT_IDS.filter((id) => present.has(id));
  if (componentIds.length === 0) return null;

  const componentPairs = await Promise.all(componentIds.map(async (componentId) => {
    try {
      const response = await fetchImpl(url(origin, `${entityRoot}/${encodeURIComponent(actorId)}` +
        `/components/${encodeURIComponent(componentId)}`), { headers, cache: "no-store" });
      const payload = response?.ok ? await json(response) : null;
      return [componentId, payload ? componentValue(payload, actorId, componentId) : null];
    } catch {
      return [componentId, null];
    }
  }));
  const components = new Map(componentPairs);

  let classes = [];
  try {
    const relationshipResponse = await fetchImpl(url(origin, `${applicationRoot}/relationships` +
      `?fromEntityId=${encodeURIComponent(actorId)}` +
      `&qualifiedKind=${encodeURIComponent(CHARACTER_CLASS_MEMBERSHIP_RELATIONSHIP_KIND)}` +
      "&limit=100"), { headers, cache: "no-store" });
    const relationshipPayload = relationshipResponse?.ok ? await json(relationshipResponse) : null;
    const membershipIds = relationshipTargetIds(
      relationshipPayload,
      actorId,
      CHARACTER_CLASS_MEMBERSHIP_RELATIONSHIP_KIND,
    );
    const uniqueIds = [...new Set(membershipIds)];
    if (uniqueIds.length === membershipIds.length) {
      classes = (await Promise.all(uniqueIds.map(async (membershipId) => {
        try {
          const [entityResponse, componentResponse] = await Promise.all([
            fetchImpl(url(origin, `${entityRoot}/${encodeURIComponent(membershipId)}`), { headers, cache: "no-store" }),
            fetchImpl(url(origin, `${entityRoot}/${encodeURIComponent(membershipId)}` +
              `/components/${CHARACTER_CLASS_MEMBERSHIP_COMPONENT_TYPE_ID}`), { headers, cache: "no-store" }),
          ]);
          if (!entityResponse?.ok || !componentResponse?.ok) return null;
          const [entityPayload, componentPayload] = await Promise.all([json(entityResponse), json(componentResponse)]);
          const membershipEntity = entity(entityPayload, membershipId);
          const value = componentValue(componentPayload, membershipId, CHARACTER_CLASS_MEMBERSHIP_COMPONENT_TYPE_ID);
          return membershipEntity ? canonicalClassMembership(value, membershipId, membershipEntity.name) : null;
        } catch {
          return null;
        }
      }))).filter(Boolean).sort((left, right) => left.classId.localeCompare(right.classId));
    }
  } catch {
    classes = [];
  }

  let inventoryStatus = "unavailable";
  let inventory = [];
  try {
    const containmentResponse = await fetchImpl(url(origin, `${applicationRoot}/containments` +
      `?containerEntityId=${encodeURIComponent(actorId)}&limit=24`), { headers, cache: "no-store" });
    const containmentPayload = containmentResponse?.ok ? await json(containmentResponse) : null;
    if (Array.isArray(containmentPayload?.items) && containmentPayload.items.length <= 24) {
      inventoryStatus = "ready";
      inventory = (await Promise.all(containmentPayload.items.map(async (edge) => {
        const containedId = token(edge?.containedEntityId);
        const container = token(edge?.containerEntityId);
        const slot = text(edge?.slot, 100);
        if (!containedId || container !== actorId || !slot) return null;
        try {
          const [itemResponse, linkResponse, quantityResponse, equipmentResponse] = await Promise.all([
            fetchImpl(url(origin, `${entityRoot}/${encodeURIComponent(containedId)}`), { headers, cache: "no-store" }),
            fetchImpl(url(origin, `${entityRoot}/${encodeURIComponent(containedId)}/components/${ITEM_DEFINITION_LINK_COMPONENT_TYPE_ID}`), { headers, cache: "no-store" }),
            fetchImpl(url(origin, `${entityRoot}/${encodeURIComponent(containedId)}/components/${ITEM_QUANTITY_COMPONENT_TYPE_ID}`), { headers, cache: "no-store" }),
            fetchImpl(url(origin, `${entityRoot}/${encodeURIComponent(containedId)}/components/${ITEM_EQUIPMENT_COMPONENT_TYPE_ID}`), { headers, cache: "no-store" }).catch(() => null),
          ]);
          if (!itemResponse?.ok || !linkResponse?.ok || !quantityResponse?.ok) return null;
          const [itemPayload, linkPayload, quantityPayload, equipmentPayload] = await Promise.all([
            json(itemResponse),
            json(linkResponse),
            json(quantityResponse),
            equipmentResponse?.ok ? json(equipmentResponse) : Promise.resolve(null),
          ]);
          const itemEntity = entity(itemPayload, containedId);
          if (!itemEntity) return null;
          return canonicalItem(
            componentValue(linkPayload, containedId, ITEM_DEFINITION_LINK_COMPONENT_TYPE_ID),
            componentValue(quantityPayload, containedId, ITEM_QUANTITY_COMPONENT_TYPE_ID),
            equipmentPayload ? componentValue(equipmentPayload, containedId, ITEM_EQUIPMENT_COMPONENT_TYPE_ID) : null,
            { slot },
            itemEntity,
          );
        } catch {
          return null;
        }
      }))).filter(Boolean).sort((left, right) => left.name.localeCompare(right.name) || left.id.localeCompare(right.id));
    }
  } catch {
    inventoryStatus = "unavailable";
  }

  return {
    identity: canonicalIdentity(components.get(CHARACTER_IDENTITY_COMPONENT_TYPE_ID)),
    origin: canonicalOrigin(components.get(CHARACTER_ORIGIN_COMPONENT_TYPE_ID)),
    abilities: canonicalAbilityScores(components.get(CREATURE_ABILITY_SCORES_COMPONENT_TYPE_ID)),
    hitPoints: canonicalHitPoints(components.get(CREATURE_HIT_POINTS_COMPONENT_TYPE_ID)),
    temporaryHitPoints: canonicalTemporaryHitPoints(components.get(CREATURE_TEMPORARY_HIT_POINTS_COMPONENT_TYPE_ID)),
    body: canonicalBody(components.get(CREATURE_BODY_COMPONENT_TYPE_ID)),
    movement: canonicalMovement(components.get(CREATURE_MOVEMENT_COMPONENT_TYPE_ID)),
    proficiencies: canonicalProficiencies(components.get(CREATURE_PROFICIENCIES_COMPONENT_TYPE_ID)),
    experience: canonicalExperience(components.get(CHARACTER_EXPERIENCE_COMPONENT_TYPE_ID)),
    classes,
    inventoryStatus,
    inventory,
  };
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
  perspective,
  mediaAssetBaseUrl,
}) {
  if (projectedKnowledge.status !== "ready") return projectedKnowledge;
  const cache = new Map();
  async function mediaFor(ownerId) {
    if (!cache.has(ownerId)) {
      cache.set(ownerId, readExactComponent(
        fetchImpl, origin, entityRoot, ownerId, WORLD_MEDIA_VISUAL_COMPONENT_TYPE_ID,
      ).then((value) => value ? projectMediaVisual(value, perspective, mediaAssetBaseUrl) : null));
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
    const visualPath = `/api/applications/${encodeURIComponent(applicationId)}` +
      `/state-spaces/${encodeURIComponent(stateSpaceId)}/entities/${encodeURIComponent(id)}` +
      `/components/${WORLD_MAP_VISUAL_COMPONENT_TYPE_ID}`;
    const mediaPath = `/api/applications/${encodeURIComponent(applicationId)}` +
      `/state-spaces/${encodeURIComponent(stateSpaceId)}/entities/${encodeURIComponent(id)}` +
      `/components/${WORLD_MEDIA_VISUAL_COMPONENT_TYPE_ID}`;
    const [containmentResult, componentResult, anchorResult, visualResult, mediaResult] = await Promise.allSettled([
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
      fetchImpl(url(origin, visualPath), {
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
      const visualResponse = visualResult.status === "fulfilled" ? visualResult.value : null;
      const mediaResponse = mediaResult.status === "fulfilled" ? mediaResult.value : null;
      const [containmentPayload, componentPayload, anchorPayload, visualPayload, mediaPayload] = await Promise.all([
        containmentResponse?.ok ? json(containmentResponse) : Promise.resolve(null),
        componentResponse?.ok ? json(componentResponse) : Promise.resolve(null),
        anchorResponse?.ok ? json(anchorResponse) : Promise.resolve(null),
        visualResponse?.ok ? json(visualResponse) : Promise.resolve(null),
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
        visualPayload,
        mediaPayload,
      };
    } catch {
      return { id, name, visibility: null, visualPayload: null, mediaPayload: null };
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
    const selectedVisual = entry.visualPayload
      ? mapVisual(entry.visualPayload, entry.id, options.perspective)
      : null;
    const selectedMedia = entry.mediaPayload
      ? mediaVisual(entry.mediaPayload, entry.id, options.perspective, options.mediaAssetBaseUrl)
      : null;
    const { visibility: _, visualPayload: __, mediaPayload: ___, ...safeEntry } = entry;
    return [{
      ...safeEntry,
      ...(selectedVisual ? { mapVisual: selectedVisual } : {}),
      ...(selectedMedia ? { media: selectedMedia } : {}),
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
    readExactComponent(fetchImpl, origin, entityRoot, conversationId, WORLD_MEDIA_VISUAL_COMPONENT_TYPE_ID),
  ]);
  if (!conversation || !validInteraction(interaction) || participantIds === null) return null;
  const visibleIds = perspective === "dm"
    ? participantIds
    : participantIds.filter((id) => authorizedActorIds.has(id));
  const participants = (await Promise.all(visibleIds.map(async (id) => {
    const [participant, mediaValue] = await Promise.all([
      readNamedEntity(fetchImpl, origin, entityRoot, id),
      readExactComponent(fetchImpl, origin, entityRoot, id, WORLD_MEDIA_VISUAL_COMPONENT_TYPE_ID),
    ]);
    if (!participant) return null;
    const media = mediaValue ? projectMediaVisual(mediaValue, perspective, mediaAssetBaseUrl) : null;
    return { ...participant, ...(media?.portrait ? { portrait: media.portrait } : {}) };
  }))).filter(Boolean);
  if (participants.length !== visibleIds.length) return null;
  return {
    status: "ready",
    kind: "conversation",
    ...(() => {
      const media = sceneMediaValue
        ? projectMediaVisual(sceneMediaValue, perspective, mediaAssetBaseUrl)
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
  fetchImpl, origin, entityRoot, encounterId, stateSpaceId, perspective, authorizedActorIds,
  mediaAssetBaseUrl,
}) {
  const [encounter, definition, participantIds, activeRoundIds, activeTurnIds, sceneMediaValue] = await Promise.all([
    readNamedEntity(fetchImpl, origin, entityRoot, encounterId),
    readExactComponent(fetchImpl, origin, entityRoot, encounterId, ENCOUNTER_DEFINITION_COMPONENT_TYPE_ID),
    readExactRelationshipTargets(fetchImpl, origin, entityRoot, encounterId, ENCOUNTER_RELATIONSHIP_KINDS.participants),
    readExactRelationshipTargets(fetchImpl, origin, entityRoot, encounterId, ENCOUNTER_RELATIONSHIP_KINDS.activeRound),
    readExactRelationshipTargets(fetchImpl, origin, entityRoot, encounterId, ENCOUNTER_RELATIONSHIP_KINDS.activeTurn),
    readExactComponent(fetchImpl, origin, entityRoot, encounterId, WORLD_MEDIA_VISUAL_COMPONENT_TYPE_ID),
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
    const mediaValue = await readExactComponent(
      fetchImpl, origin, entityRoot, row.actor.id, WORLD_MEDIA_VISUAL_COMPONENT_TYPE_ID,
    );
    const media = mediaValue ? projectMediaVisual(mediaValue, perspective, mediaAssetBaseUrl) : null;
    return {
      id: row.actor.id,
      name: row.actor.name,
      initiative: row.initiative,
      active: turn?.participationId === row.participationId,
      ...(media?.portrait ? { portrait: media.portrait } : {}),
    };
  }));
  const sceneMedia = sceneMediaValue
    ? projectMediaVisual(sceneMediaValue, perspective, mediaAssetBaseUrl)
    : null;
  return {
    status: "ready",
    kind: "combat",
    ...(sceneMedia?.scene ? { scene: sceneMedia.scene } : {}),
    combat: {
      id: encounter.id,
      name: encounter.name,
      participants: visibleParticipants,
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
  boundCanonical,
}) {
  if (serverRole.role === "actor") {
    return boundActor
      ? [{ ...boundActor, ...boundActorDetails, ...(boundCanonical ? { canonical: boundCanonical } : {}), current: true }]
      : [];
  }
  if (perspective !== "dm") return [];

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
        fetchImpl(url(origin, `${entityRoot}/${encodeURIComponent(actorId)}` +
          `/components/${PLAYTEST_CHARACTER_RECORD_COMPONENT_TYPE_ID}`), {
          headers,
          cache: "no-store",
        }).catch(() => null),
      ]);
      if (!actorResponse?.ok) return null;
      const [actorPayload, recordPayload] = await Promise.all([
        json(actorResponse),
        recordResponse?.ok ? json(recordResponse) : Promise.resolve(null),
      ]);
      const actor = entity(actorPayload, actorId);
      if (!actor) return null;
      const canonical = await readCanonicalCharacter({
        fetchImpl,
        origin,
        applicationId,
        stateSpaceId,
        actorId,
      });
      return {
        ...actor,
        ...characterDetails(recordPayload
          ? componentValue(recordPayload, actorId, PLAYTEST_CHARACTER_RECORD_COMPONENT_TYPE_ID)
          : null),
        ...(canonical ? { canonical } : {}),
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
            `${listRoot}/${encodeURIComponent(entry.entityId)}/components/${WORLD_MEDIA_VISUAL_COMPONENT_TYPE_ID}`),
          { headers, cache: "no-store" }),
        ]);
        const [motivePayload, mediaPayload] = await Promise.all([
          motiveResponse?.ok ? json(motiveResponse) : Promise.resolve(null),
          mediaResponse?.ok ? json(mediaResponse) : Promise.resolve(null),
        ]);
        motive = worldMotive(componentValue(motivePayload, entry.entityId, WORLD_MOTIVE_COMPONENT_TYPE_ID));
        media = mediaPayload
          ? mediaVisual(mediaPayload, entry.entityId, perspective, mediaAssetBaseUrl)
          : null;
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
 */
export async function readGameServerContext({
  serverOrigin,
  fetchImpl = fetch,
  requestedPerspective = "dm",
  requestedCampaignId = null,
  localSeat,
  mediaAssetBaseUrl = "/ui/dnd2024-play/assets/",
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
  const serverRole = overrideServerRole(binding, normalizeSeat(localSeat));
  const isGameMaster = serverRole.role === "game-master";
  const contextAudience = isGameMaster
    ? {
        seat: "dm",
        perspective: normalizePerspective(normalizedRequestedPerspective),
        allowedPerspectives: ["dm", "player"],
      }
    : { seat: "player", allowedPerspectives: ["player"] };
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
  try {
    [campaignResponse, actorResponse, campaignComponentResponse, currentSceneComponentResponse,
      actorComponentResponse, knowledgeResponse, chronologyResponse] = await Promise.all([
      fetchImpl(url(origin, `${root}/${encodeURIComponent(selectedCampaignId)}`), {
        headers: { Accept: "application/json" }, cache: "no-store",
      }),
      serverRole.role === "actor"
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
      serverRole.role === "actor"
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
    ]);
  } catch {
    return unavailable("The campaign binding was found, but the game state could not be read.");
  }

  const [campaign, actor, campaignComponent, currentSceneComponent, actorComponent, knowledgeEnvelope,
    chronologyEnvelope] = await Promise.all([
    json(campaignResponse),
    json(actorResponse),
    json(campaignComponentResponse),
    json(currentSceneComponentResponse),
    json(actorComponentResponse),
    json(knowledgeResponse),
    json(chronologyResponse),
  ]);
  const campaignEntity = campaignResponse?.ok ? entity(campaign, selectedCampaignId) : null;
  const actorEntity = isGameMaster
    ? { id: "local-game-master", name: "Dungeon Master" }
    : (serverRole.role === "actor" && actorResponse?.ok
    ? entity(actor, binding.actorId)
    : null);
  if (!campaignEntity || !actorEntity) {
    return unavailable("The campaign binding no longer matches readable game state.");
  }
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
  if (serverRole.role === "actor" && locationDirectory.length > 0) {
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
  const boundActorDetails = characterDetails(actorComponentResponse?.ok
    && serverRole.role === "actor"
    ? componentValue(actorComponent, binding.actorId, PLAYTEST_CHARACTER_RECORD_COMPONENT_TYPE_ID)
    : null);
  const boundCanonical = serverRole.role === "actor"
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
    perspective: contextAudience.perspective,
    boundActor: serverRole.role === "actor" ? actorEntity : null,
    boundActorDetails,
    boundCanonical,
  });
  const authorizedLocationIds = locationDirectory.map((location) => location.id);
  const sceneComponentWasReturned = currentSceneComponentResponse?.ok === true;
  const sceneRecord = sceneComponentWasReturned
    ? resolveCurrentSceneRecord(
      componentValue(currentSceneComponent, selectedCampaignId, CAMPAIGN_CURRENT_SCENE_COMPONENT_TYPE_ID),
      authorizedLocationIds,
    )
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
