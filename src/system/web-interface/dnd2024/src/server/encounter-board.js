import { readModelResponse } from "./read-model-response.js";
import query from "../../../../../../catalog/applications/dnd2024/queries/combat/dnd2024.query.encounter-board.json" with { type: "json" };

const ID = /^[a-zA-Z0-9][a-zA-Z0-9._:-]{0,199}$/u;
const text = (value, maximum) => typeof value === "string" && value.length > 0 && value.length <= maximum
  && value.trim() === value && !/[\u0000-\u001F\u007F]/u.test(value) ? value : null;
const integer = (value, minimum, maximum) => Number.isSafeInteger(value) && value >= minimum && value <= maximum ? value : null;
const object = (value) => value !== null && typeof value === "object" && !Array.isArray(value) ? value : null;
const own = (value, key) => { const item = object(value); return item && Object.hasOwn(item, key) ? item[key] : undefined; };
const identity = (value) => { const candidate = text(value, 200); return candidate && ID.test(candidate) ? candidate : null; };

function area(value, columns, rows, position = false) {
  const input = object(value);
  const x = integer(own(input, "x"), 0, 63), y = integer(own(input, "y"), 0, 63);
  const width = integer(own(input, "width"), 1, position ? 8 : 64);
  const height = integer(own(input, "height"), 1, position ? 8 : 64);
  if (x === null || y === null || width === null || height === null || x + width > columns || y + height > rows) return null;
  if (!position) return { x, y, width, height };
  const elevationFeet = integer(own(input, "elevationFeet"), -1000, 1000);
  const revision = integer(own(input, "revision"), 1, 2_147_483_647);
  return elevationFeet === null || revision === null ? null : { x, y, width, height, elevationFeet, revision };
}

function projectBoard(value, { encounterId, perspective }) {
  const input = object(value);
  if (!input || own(input, "perspective") !== perspective) return null;
  const encounter = object(own(input, "encounter"));
  if (own(encounter, "id") !== encounterId) return null;
  const board = object(own(input, "board"));
  const revision = integer(own(board, "revision"), 1, 2_147_483_647);
  const columns = integer(own(board, "columns"), 1, 64);
  const rows = integer(own(board, "rows"), 1, 64);
  const feetPerSquare = integer(own(board, "feetPerSquare"), 1, 30);
  if (revision === null || columns === null || rows === null || feetPerSquare === null) return null;

  const backgroundMediaOrderValue = own(board, "backgroundMediaOrder");
  const backgroundMediaOrder = backgroundMediaOrderValue === null || backgroundMediaOrderValue === undefined
    ? null : integer(backgroundMediaOrderValue, 0, 10_000);
  const terrainInput = own(input, "terrain"), obstaclesInput = own(input, "obstacles"), participantsInput = own(input, "participants");
  if (Array.isArray(terrainInput) && terrainInput.length > 256 || Array.isArray(obstaclesInput) && obstaclesInput.length > 256 ||
      Array.isArray(participantsInput) && participantsInput.length > 100) return null;
  const terrainRows = Array.isArray(terrainInput) && terrainInput.length <= 256 ? terrainInput : [];
  const obstacleRows = Array.isArray(obstaclesInput) && obstaclesInput.length <= 256 ? obstaclesInput : [];
  const participantRows = Array.isArray(participantsInput) && participantsInput.length <= 100 ? participantsInput : [];
  const notices = [];
  if (!text(own(encounter, "name"), 400)) notices.push("Encounter name is unavailable.");
  if (!Array.isArray(terrainInput)) notices.push("Terrain data is incomplete; uninspected areas may contain terrain.");
  if (!Array.isArray(obstaclesInput)) notices.push("Obstacle data is incomplete; uninspected areas may contain obstacles.");
  if (!Array.isArray(participantsInput)) notices.push("Participant data is incomplete; uninspected combatants may be present.");
  const terrain = terrainRows.map((value) => {
    const item = object(value), id = identity(own(item, "id")), label = text(own(item, "label"), 200);
    const movementCost = integer(own(item, "movementCost"), 1, 4);
    const visibility = own(item, "visibility");
    const areaValue = area(own(item, "area"), columns, rows);
    return id && label && movementCost !== null && areaValue && (perspective === "dm" || visibility === "public")
      ? { id, label, area: areaValue, movementCost } : null;
  }).filter(Boolean);
  const obstacles = obstacleRows.map((value) => {
    const item = object(value), id = identity(own(item, "id")), label = text(own(item, "label"), 200);
    const areaValue = area(own(item, "area"), columns, rows);
    const visibility = own(item, "visibility");
    return id && label && areaValue && (perspective === "dm" || visibility === "public")
      ? { id, label, area: areaValue } : null;
  }).filter(Boolean);
  const areaCounts = new Map();
  for (const item of [...terrain, ...obstacles]) areaCounts.set(item.id, (areaCounts.get(item.id) ?? 0) + 1);
  const uniqueTerrain = terrain.filter((item) => areaCounts.get(item.id) === 1);
  const uniqueObstacles = obstacles.filter((item) => areaCounts.get(item.id) === 1);
  if (uniqueTerrain.length !== terrainRows.length || uniqueObstacles.length !== obstacleRows.length)
    notices.push("Some board geometry was omitted because its identity was ambiguous or malformed.");

  const participants = participantRows.map((value) => {
    const item = object(value), id = identity(own(item, "participationId"));
    const name = text(own(item, "name"), 400) ?? id;
    const initiative = integer(own(item, "initiative"), -1_000_000, 1_000_000);
    const position = area(own(item, "position"), columns, rows, true);
    if (!id || !name || initiative === null || !position) return null;
    const activeValue = own(item, "activeTurn");
    if (activeValue !== undefined && typeof activeValue !== "boolean") return null;
    return { id, name, initiative, activeSource: activeValue === undefined ? undefined : activeValue === true, position };
  }).filter(Boolean);
  const participantCounts = new Map();
  for (const item of participants) participantCounts.set(item.id, (participantCounts.get(item.id) ?? 0) + 1);
  const uniqueParticipants = participants.filter((item) => participantCounts.get(item.id) === 1);
  if (uniqueParticipants.length !== participants.length || uniqueParticipants.length !== participantRows.length)
    notices.push("Some participant positions were omitted because their identity or geometry was unavailable.");
  const turnInput = own(input, "turn");
  const turn = object(turnInput);
  const turnId = identity(own(turn, "id")), turnParticipationId = identity(own(turn, "participationId"));
  const ordinal = integer(own(turn, "ordinal"), 0, 99);
  const active = turnInput === null || turnInput === undefined ? null
    : turnId && turnParticipationId && ordinal !== null ? uniqueParticipants.find((item) => item.id === turnParticipationId) ?? null : null;
  const outputParticipants = uniqueParticipants.map(({ activeSource, ...item }) => ({ ...item, active: active === null ? false : item.id === active.id }));
  if (turnInput === undefined) notices.push("Active-turn data is incomplete.");
  else if (turnInput !== null && !active) notices.push("The active turn is unavailable for the visible participants.");
  if (uniqueParticipants.some((item) => item.activeSource === undefined)) notices.push("Some participant turn markers are unavailable.");
  const uniqueNotices = [...new Set(notices)].slice(0, 8);
  return {
    encounter: { id: encounterId, name: text(own(encounter, "name"), 400) ?? encounterId },
    board: { revision, ...(uniqueNotices.length ? { coverage: "partial", notices: uniqueNotices } : {}),
      ...(backgroundMediaOrderValue !== undefined && (backgroundMediaOrderValue === null || backgroundMediaOrder !== null)
      ? { backgroundMediaOrder } : {}), columns, rows, feetPerSquare,
      terrain: uniqueTerrain, obstacles: uniqueObstacles, participants: outputParticipants,
      ...(active ? { turn: { id: turnId, participationId: turnParticipationId, ordinal, actorName: active.name } } : {}) },
  };
}

// The catalog owns tactical rules and audience filtering. This adapter only validates the
// closed response, binds it to the requested encounter/perspective, and formats its view.
export async function readEncounterBoardProjection({ fetchImpl, origin, entityRoot, encounterId, perspective, campaignId }) {
  try {
    if (!identity(campaignId)) return null;
    const parameters = new URLSearchParams({ perspective, input: JSON.stringify({ selectionId: campaignId }) });
    const scope = new URL(entityRoot, origin).pathname.match(/^\/api\/applications\/([^/]+)\/state-spaces\/([^/]+)\/entities$/u);
    if (!scope) return null;
    const result = await readModelResponse({
      fetchImpl,
      resource: new URL(`${entityRoot}/${encodeURIComponent(encounterId)}/read-models/${query.id}?${parameters}`, origin),
      init: { headers: { Accept: "application/json" }, cache: "no-store" },
      applicationId: decodeURIComponent(scope[1]),
      stateSpaceId: decodeURIComponent(scope[2]),
      query: { id: query.id, outputSchemaHash: query.projection.outputSchemaHash },
      maximumBodyBytes: 262_144,
      statusPolicy: { ready: [200], unavailable: "remaining" },
      consume: (value) => projectBoard(value, { encounterId, perspective }),
    });
    if (result.status !== "ready") return null;
    return result.data;
  } catch (error) {
    if (error?.name === "AbortError") throw error;
    return null;
  }
}

export async function readEncounterBoard(options) {
  return (await readEncounterBoardProjection(options))?.board ?? null;
}
