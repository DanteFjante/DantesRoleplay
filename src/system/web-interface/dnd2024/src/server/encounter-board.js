import validate from "./encounter-board-validator.js";
import query from "../../../../../../catalog/applications/dnd2024/queries/combat/dnd2024.query.encounter-board.json" with { type: "json" };

const fingerprint = (value) => typeof value === "string" && /^[a-f0-9]{64}$/iu.test(value);

// The catalog owns tactical rules and audience filtering. This adapter only validates the
// closed response, binds it to the requested encounter/perspective, and formats its view.
export async function readEncounterBoard({ fetchImpl, origin, entityRoot, encounterId, perspective }) {
  try {
    const response = await fetchImpl(new URL(`${entityRoot}/${encodeURIComponent(encounterId)}/read-models/${query.id}?perspective=${perspective}`, origin), {
      headers: { Accept: "application/json" }, cache: "no-store",
    });
    if (!response.ok) return null;
    const envelope = await response.json();
    const data = envelope.data;
    const scope = new URL(entityRoot, origin).pathname.match(/^\/api\/applications\/([^/]+)\/state-spaces\/([^/]+)\/entities$/u);
    if (!scope || envelope.applicationId !== decodeURIComponent(scope[1]) || envelope.stateSpaceId !== decodeURIComponent(scope[2]) ||
        envelope.qualifiedQueryId !== query.id ||
        envelope.outputSchemaHash !== query.projection.outputSchemaHash ||
        ![envelope.stateSpaceFingerprint, envelope.resolutionFingerprint, envelope.resultFingerprint, envelope.sourceRevisionFingerprint].every(fingerprint) ||
        !validate(data) || data.encounter.id !== encounterId || data.perspective !== perspective) return null;
    const contained = (area) => area.x + area.width <= data.board.columns && area.y + area.height <= data.board.rows;
    const areas = [...data.terrain, ...data.obstacles];
    if (areas.some((entry) => !contained(entry.area) || (perspective === "player" && entry.visibility !== "public")) ||
        data.participants.some((entry) => !contained(entry.position)) ||
        new Set(areas.map((entry) => entry.id)).size !== areas.length ||
        new Set(data.participants.map((entry) => entry.participationId)).size !== data.participants.length) return null;
    const active = data.turn && data.participants.find((entry) => entry.participationId === data.turn.participationId);
    if ((data.turn && !active) || data.participants.some((entry) => entry.activeTurn !== (entry === active))) return null;
    return {
      ...data.board,
      terrain: data.terrain.map(({ id, label, area, movementCost }) => ({ id, label, area, movementCost })),
      obstacles: data.obstacles.map(({ id, label, area }) => ({ id, label, area })),
      participants: data.participants.map((entry) => ({ id: entry.participationId, name: entry.name,
        initiative: entry.initiative, active: entry.activeTurn, position: entry.position })),
      ...(active ? { turn: { ...data.turn, actorName: active.name } } : {}),
    };
  } catch (error) {
    if (error?.name === "AbortError") throw error;
    return null;
  }
}
