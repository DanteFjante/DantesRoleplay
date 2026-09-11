import { isVisualMedia, validTacticalBoard } from "../state.js";

const RECORDED_KINDS = new Set(["out-of-character", "conversation", "combat", "exploration",
  "investigation", "travel", "rest", "downtime", "other"]);
const SCENE_KINDS = new Set(["exploration", "conversation", "combat"]);
const absentScene = () => ({ status: "unavailable", coverage: "partial", unavailableFields: ["scene"],
  message: "No authoritative current scene is available." });
const object = (value) => value && typeof value === "object" && !Array.isArray(value) ? value : null;
const own = (value, key) => value && Object.hasOwn(value, key) ? value[key] : undefined;
const text = (value, max = 400) => typeof value === "string" && value.length <= max ? value : null;
const id = (value) => {
  const candidate = text(value, 200);
  return candidate && /\S/u.test(candidate) ? candidate : null;
};

function availability(source) {
  const fields = new Set(Array.isArray(own(source, "unavailableFields"))
    ? own(source, "unavailableFields").filter((field) => typeof field === "string" && field.length <= 80).slice(0, 32)
    : []);
  let partial = own(source, "coverage") === "partial" || fields.size > 0;
  return {
    withhold(field) { partial = true; fields.add(field); },
    apply(value) {
      if (partial) value.coverage = "partial";
      if (fields.size) value.unavailableFields = [...fields];
      return value;
    },
  };
}

function media(value, partial, field) {
  const candidate = object(value);
  if (candidate && ["imageUrl", "alt", "width", "height"].every((key) => Object.hasOwn(candidate, key)) &&
      isVisualMedia(candidate) && candidate.imageUrl.startsWith("/api/applications/") && candidate.imageUrl.endsWith("/content")) {
    return { imageUrl: candidate.imageUrl, alt: candidate.alt, width: candidate.width, height: candidate.height };
  }
  if (value !== undefined) partial.withhold(field);
  return undefined;
}

function tacticalBoard(value, partial) {
  const source = object(value);
  if (!source) return null;
  const board = {};
  for (const key of ["revision", "columns", "rows", "feetPerSquare", "terrain", "obstacles", "participants", "turn", "backgroundMediaOrder"])
    if (Object.hasOwn(source, key)) board[key] = source[key];
  if (!validTacticalBoard(board)) return null;
  let coverage = own(source, "coverage") === "partial" || own(source, "coverage") === "complete" ? own(source, "coverage") : undefined;
  const rawNotices = own(source, "notices");
  const notices = Array.isArray(rawNotices) && rawNotices.length <= 8
    ? [...new Set(rawNotices.filter((notice) => typeof notice === "string" && notice.length > 0 && notice.length <= 1_000))]
    : [];
  const invalidCoverage = own(source, "coverage") !== undefined && !coverage;
  const invalidNotices = rawNotices !== undefined && (!Array.isArray(rawNotices) || notices.length !== rawNotices.length);
  if (invalidCoverage) partial.withhold("combat.board.coverage");
  if (invalidNotices) partial.withhold("combat.board.notices");
  if (invalidCoverage || invalidNotices || notices.length) coverage = "partial";
  if (invalidCoverage || invalidNotices) notices.splice(7, notices.length, "Some board coverage information is unavailable.");
  return { ...board, ...(coverage ? { coverage } : {}), ...(notices.length ? { notices } : {}) };
}

function uniqueRows(value, maximum, partial, field, project) {
  if (!Array.isArray(value) || value.length > maximum) {
    partial.withhold(field);
    return [];
  }
  const counts = new Map();
  for (const raw of value) {
    const rowId = id(own(object(raw), "id"));
    if (rowId) counts.set(rowId, (counts.get(rowId) ?? 0) + 1);
  }
  const rows = [];
  for (const raw of value) {
    const row = object(raw);
    const rowId = id(own(row, "id"));
    if (!rowId || counts.get(rowId) !== 1) {
      partial.withhold(field);
      continue;
    }
    const projected = project(row, rowId);
    if (projected) rows.push(projected);
    else partial.withhold(field);
  }
  return rows;
}

function participants(value, partial, field, options = {}) {
  return uniqueRows(value, options.maximum ?? 32, partial, field, (row, rowId) => {
    const name = text(own(row, "name"));
    if (name === null) partial.withhold(`${field}.name`);
    if (options.combat && (!Number.isInteger(own(row, "initiative")) || typeof own(row, "active") !== "boolean")) return null;
    const result = { id: rowId, name: name ?? "Unnamed participant" };
    const entityId = id(own(row, "entityId"));
    if (options.entity && entityId) result.entityId = entityId;
    else if (options.entity && own(row, "entityId") !== undefined) partial.withhold(`${field}.entityId`);
    if (options.combat) {
      result.initiative = own(row, "initiative");
      result.active = own(row, "active");
    }
    if (options.portrait) {
      const portrait = media(own(row, "portrait"), partial, `${field}.portrait`);
      if (portrait) result.portrait = portrait;
    }
    return result;
  });
}

function affordances(value, partial) {
  if (!Array.isArray(value) || value.length > 24) {
    partial.withhold("affordances");
    return [];
  }
  const counts = new Map();
  for (const raw of value) {
    const key = text(own(object(raw), "key"), 64);
    if (key && /^[a-z][a-z0-9]*(?:-[a-z0-9]+)*$/u.test(key)) counts.set(key, (counts.get(key) ?? 0) + 1);
  }
  const result = [];
  for (const raw of value) {
    const row = object(raw);
    const key = text(own(row, "key"), 64);
    const label = text(own(row, "label"), 120);
    if (!key || !/^[a-z][a-z0-9]*(?:-[a-z0-9]+)*$/u.test(key) || counts.get(key) !== 1 || label === null || !/\S/u.test(label)) {
      partial.withhold("affordances");
      continue;
    }
    const summary = text(own(row, "summary"), 500);
    if (summary === null) partial.withhold("affordances");
    result.push({ key, label, ...(summary === null ? {} : { summary }) });
  }
  return result;
}

function recordedSituation(source, partial, locationId) {
  const recorded = object(own(source, "recorded"));
  const recordedId = id(own(recorded, "id"));
  const kind = own(recorded, "kind");
  if (!recorded || !recordedId || !RECORDED_KINDS.has(kind)) return null;
  const result = { status: "ready", kind: "recorded", ...(locationId ? { locationId } : {}), recorded: { id: recordedId, kind } };
  const summary = text(own(recorded, "summary"), 4_000);
  if (summary === null) partial.withhold("summary"); else result.recorded.summary = summary;
  result.recorded.participants = participants(own(recorded, "participants"), partial, "participants", { entity: true });
  result.recorded.interactions = uniqueRows(own(recorded, "interactions"), 12, partial, "interactions", (row, rowId) => {
    const role = own(row, "role");
    if (role !== "player" && role !== "assistant") return null;
    const message = text(own(row, "text"), 4_000);
    if (message === null) partial.withhold("interactions.text");
    const ordinal = own(row, "ordinal");
    if (!(Number.isInteger(ordinal) && ordinal > 0)) partial.withhold("interactions.ordinal");
    return { id: rowId, role, text: message ?? "Message text unavailable.", ...(Number.isInteger(ordinal) && ordinal > 0 ? { ordinal } : {}) };
  });
  const location = object(own(recorded, "location"));
  if (own(recorded, "location") !== undefined) {
    const locationName = text(own(location, "name"));
    const recordedLocationId = id(own(location, "id"));
    if (locationName === null || (recordedLocationId && locationId && recordedLocationId !== locationId)) partial.withhold("location");
    else result.recorded.location = { ...(recordedLocationId ? { id: recordedLocationId } : {}), name: locationName };
  }
  return result;
}

/** Field-local Current display projection. Never emits unknown scene/action/board values. */
export function normalizeCurrentSituation(value) {
  if (value === null) return absentScene();
  const source = object(value);
  if (!source || !Object.hasOwn(source, "status") || !["ready", "unavailable"].includes(source.status)) return null;
  const partial = availability(source);
  const locationId = id(own(source, "locationId"));
  if (source.status === "unavailable") {
    if (own(source, "locationId") !== undefined && !locationId) partial.withhold("locationId");
    const message = text(own(source, "message"), 1_000);
    if (message === null) partial.withhold("message");
    return partial.apply({ status: "unavailable", ...(locationId ? { locationId } : {}),
      message: message ?? "No authoritative current scene is available." });
  }
  if (own(source, "kind") === "recorded") {
    const recorded = recordedSituation(source, partial, locationId);
    return recorded ? partial.apply(recorded) : null;
  }
  const kind = own(source, "kind");
  if (!SCENE_KINDS.has(kind) || !locationId) return null;
  const scene = media(own(source, "scene"), partial, "scene");
  const base = { status: "ready", kind, locationId, ...(scene ? { scene } : {}), affordances: affordances(own(source, "affordances"), partial) };
  if (kind === "exploration") {
    const coverage = own(source, "routesCoverage");
    if (coverage === "complete" || coverage === "partial" || coverage === "unavailable") base.routesCoverage = coverage;
    else {
      partial.withhold("routes");
      base.routesCoverage = "unavailable";
    }
    return partial.apply(base);
  }
  if (kind === "conversation") {
    const conversation = object(own(source, "conversation"));
    const conversationId = id(own(conversation, "id"));
    if (!conversation || !conversationId) return null;
    const name = text(own(conversation, "name"));
    if (name === null) partial.withhold("conversation.name");
    const summary = text(own(conversation, "summary"), 4_000);
    if (own(conversation, "summary") !== undefined && summary === null) partial.withhold("conversation.summary");
    base.conversation = { id: conversationId, name: name ?? "Unnamed conversation",
      participants: participants(own(conversation, "participants"), partial, "conversation.participants", { portrait: true }) };
    if (summary !== null) base.conversation.summary = summary;
    return partial.apply(base);
  }
  const combat = object(own(source, "combat"));
  const combatId = id(own(combat, "id"));
  if (!combat || !combatId) return null;
  const name = text(own(combat, "name"));
  if (name === null) partial.withhold("combat.name");
  base.combat = { id: combatId, name: name ?? "Unnamed encounter",
    participants: participants(own(combat, "participants"), partial, "combat.participants", { combat: true, portrait: true, maximum: 64 }) };
  const round = object(own(combat, "round"));
  if (own(combat, "round") !== undefined) {
    const roundId = id(own(round, "id"));
    if (roundId && Number.isInteger(own(round, "number")) && own(round, "number") > 0) base.combat.round = { id: roundId, number: own(round, "number") };
    else partial.withhold("combat.round");
  }
  const turn = object(own(combat, "turn"));
  if (own(combat, "turn") !== undefined) {
    const turnId = id(own(turn, "id")); const participationId = id(own(turn, "participationId"));
    const rawActorId = own(turn, "actorId"); const actorId = rawActorId === undefined ? undefined : id(rawActorId);
    const actorName = text(own(turn, "actorName")); const ordinal = own(turn, "ordinal");
    const budget = object(own(turn, "budget"));
    const validBudget = own(turn, "budget") === undefined || (budget && ["actions", "bonusActions", "reactions"].every((key) => Object.hasOwn(budget, key) && Number.isInteger(budget[key]) && budget[key] >= 0));
    if (turnId && participationId && (rawActorId === undefined || actorId) && actorName !== null && Number.isInteger(ordinal) && ordinal >= 0) {
      base.combat.turn = { id: turnId, participationId, ...(actorId ? { actorId } : {}), actorName, ordinal,
        ...(validBudget && budget ? { budget: { actions: budget.actions, bonusActions: budget.bonusActions, reactions: budget.reactions } } : {}) };
      if (!validBudget) partial.withhold("combat.turn.budget");
    } else partial.withhold("combat.turn");
  }
  const board = own(combat, "board");
  if (board !== undefined) {
    const normalizedBoard = tacticalBoard(board, partial);
    if (normalizedBoard) base.combat.board = normalizedBoard; else partial.withhold("combat.board");
  }
  const background = media(own(combat, "background"), partial, "combat.background");
  if (background) base.combat.background = background;
  return partial.apply(base);
}

export const currentUnavailable = absentScene;
