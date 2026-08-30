const TOKEN_MAXIMUM = 200;
const LOCATION_COMPONENT_TYPE_ID = "dnd2024.game.core.world.location";
const WORLD_MAP_ANCHOR_COMPONENT_TYPE_ID = "dnd2024.game.core.world.map.anchor";
const WORLD_MAP_VISUAL_COMPONENT_TYPE_ID = "dnd2024.game.core.world.map.visual";
const CAMPAIGN_ROOT_COMPONENT_TYPE_ID = "dnd2024.game.core.campaign.root";
const CAMPAIGN_CHAPTER_COMPONENT_TYPE_ID = "dnd2024.game.core.campaign.chapter";
const CAMPAIGN_ARC_COMPONENT_TYPE_ID = "dnd2024.game.core.campaign.arc";
const CAMPAIGN_SESSION_COMPONENT_TYPE_ID = "dnd2024.game.core.campaign.session";
const CAMPAIGN_SESSION_RECAP_COMPONENT_TYPE_ID = "dnd2024.game.core.campaign.session-recap";
const CAMPAIGN_HAS_SESSION_RELATIONSHIP_KIND = "dnd2024.game.core.campaign.has-session";
const CAMPAIGN_PARTICIPATION_COMPONENT_TYPE_ID = "dnd2024.game.core.campaign.character-participation";
const CAMPAIGN_HAS_PARTICIPATION_RELATIONSHIP_KIND =
  "dnd2024.game.core.campaign.has-character-participation";
const CAMPAIGN_PARTICIPATION_ACTOR_RELATIONSHIP_KIND =
  "dnd2024.game.core.campaign.character-participation.for-actor";
const PLAYTEST_CHARACTER_RECORD_COMPONENT_TYPE_ID = "dnd2024.playtest-character-record";
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
    return textValue && stance && presentationKind
      ? { text: textValue, stance, presentationKind }
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
    const [containmentResult, componentResult, anchorResult, visualResult] = await Promise.allSettled([
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
      const [containmentPayload, componentPayload, anchorPayload, visualPayload] = await Promise.all([
        containmentResponse?.ok ? json(containmentResponse) : Promise.resolve(null),
        componentResponse?.ok ? json(componentResponse) : Promise.resolve(null),
        anchorResponse?.ok ? json(anchorResponse) : Promise.resolve(null),
        visualResponse?.ok ? json(visualResponse) : Promise.resolve(null),
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
      };
    } catch {
      return { id, name, visibility: null, visualPayload: null };
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
    const { visibility: _, visualPayload: __, ...safeEntry } = entry;
    return [{
      ...safeEntry,
      ...(selectedVisual ? { mapVisual: selectedVisual } : {}),
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
}) {
  if (serverRole.role === "actor") {
    return boundActor
      ? [{ ...boundActor, ...boundActorDetails, current: true }]
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
      return {
        ...actor,
        ...characterDetails(recordPayload
          ? componentValue(recordPayload, actorId, PLAYTEST_CHARACTER_RECORD_COMPONENT_TYPE_ID)
          : null),
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
}) {
  const empty = { people: [], factions: [], holdings: [] };
  if (!applicationId || !stateSpaceId || !worldId || locationDirectory.length === 0) return empty;
  const listRoot = `/api/applications/${encodeURIComponent(applicationId)}` +
    `/state-spaces/${encodeURIComponent(stateSpaceId)}/entities`;
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
      try {
        const response = await fetchImpl(url(origin,
          `${listRoot}/${encodeURIComponent(entry.entityId)}/components/${WORLD_MOTIVE_COMPONENT_TYPE_ID}`),
        { headers, cache: "no-store" });
        const payload = response?.ok ? await json(response) : null;
        motive = worldMotive(componentValue(payload, entry.entityId, WORLD_MOTIVE_COMPONENT_TYPE_ID));
      } catch {
        motive = null;
      }
      return {
        id: entry.entityId,
        name: record.name,
        kind: isCreature ? "Creature" : "NPC",
        locationId: entry.locationId,
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
  const empty = { chapters: [], arcs: [], sessions: [] };
  if (!applicationId || !stateSpaceId || !campaignId) return empty;
  const listRoot = `/api/applications/${encodeURIComponent(applicationId)}` +
    `/state-spaces/${encodeURIComponent(stateSpaceId)}/entities`;
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
      return {
        kind: candidate.kind,
        record: {
          id: candidate.id,
          createdAtUtc: candidate.createdAtUtc,
          updatedAtUtc: text(payload?.updatedAtUtc, 64),
          ...value,
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
    const relationshipPath = `${listRoot.replace(/\/entities$/u, "/relationships")}` +
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
        return {
          id: sessionId,
          ...session,
          updatedAtUtc: text(sessionPayload?.updatedAtUtc, 64),
          ...(recap ? { recap } : {}),
        };
      } catch {
        return null;
      }
    }))).filter(Boolean).sort((left, right) => left.ordinal - right.ordinal || left.id.localeCompare(right.id));
  } catch {
    sessions = [];
  }
  return {
    chapters: records.filter((value) => value?.kind === "chapter").map((value) => value.record).sort(compare),
    arcs: records.filter((value) => value?.kind === "arc").map((value) => value.record).sort(compare),
    sessions,
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
  let actorComponentResponse;
  let knowledgeResponse;
  try {
    [campaignResponse, actorResponse, campaignComponentResponse, actorComponentResponse, knowledgeResponse] = await Promise.all([
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
    ]);
  } catch {
    return unavailable("The campaign binding was found, but the game state could not be read.");
  }

  const [campaign, actor, campaignComponent, actorComponent, knowledgeEnvelope] = await Promise.all([
    json(campaignResponse),
    json(actorResponse),
    json(campaignComponentResponse),
    json(actorComponentResponse),
    json(knowledgeResponse),
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
  const [locationDirectory, campaignStructure] = await Promise.all([
    readLocationDirectory({
      fetchImpl,
      origin,
      applicationId: binding.applicationId,
      stateSpaceId: binding.stateSpaceId,
      worldId: campaignWorldId(selectedCampaignId),
      perspective: contextAudience.perspective,
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
    })
    : null;
  const boundActorDetails = characterDetails(actorComponentResponse?.ok
    && serverRole.role === "actor"
    ? componentValue(actorComponent, binding.actorId, PLAYTEST_CHARACTER_RECORD_COMPONENT_TYPE_ID)
    : null);
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
  });

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
    party,
    knowledge: knowledgeResponse?.ok
      ? knowledge(knowledgeEnvelope)
      : { status: "unavailable", entries: [], locations: [] },
    ...(locationDirectory.length > 0 ? { locationDirectoryAudience: contextAudience.perspective } : {}),
    ...(locationDirectory.length > 0 ? { locationDirectory } : {}),
    ...(worldDirectory && (
      worldDirectory.people.length > 0 || worldDirectory.factions.length > 0 || worldDirectory.holdings.length > 0
    ) ? { worldDirectory } : {}),
  };
}
