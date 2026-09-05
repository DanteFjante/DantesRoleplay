import query from "../../../../../../catalog/applications/dnd2024/queries/combat/dnd2024.query.encounter-board.json" with { type: "json" };

export function boardEnvelope() {
  return {
    applicationId: "dnd2024", stateSpaceId: "dnd2024-main",
    qualifiedQueryId: query.id, outputSchemaHash: query.projection.outputSchemaHash,
    stateSpaceFingerprint: "a".repeat(64), resolutionFingerprint: "b".repeat(64),
    resultFingerprint: "c".repeat(64), sourceRevisionFingerprint: "d".repeat(64),
    data: {
      version: 1, perspective: "player",
      encounter: { id: "encounter.brackenford.ambush", name: "Brackenford ambush" },
      board: { revision: 7, columns: 12, rows: 8, feetPerSquare: 5 },
      terrain: [{ id: "terrain.rubble", label: "Rubble", area: { x: 4, y: 2, width: 2, height: 1 }, movementCost: 2, visibility: "public" }],
      obstacles: [{ id: "obstacle.wall", label: "Wall", area: { x: 6, y: 1, width: 1, height: 3 }, blocksMovement: true, visibility: "public" }],
      participants: [{ participationId: "participation.brackenford.hero", name: "Hero", initiative: 17,
        tieBreakOrder: 0, status: "active", activeTurn: false,
        position: { x: 2, y: 3, width: 2, height: 1, elevationFeet: 5, revision: 4 } }],
      turn: null,
    },
  };
}
