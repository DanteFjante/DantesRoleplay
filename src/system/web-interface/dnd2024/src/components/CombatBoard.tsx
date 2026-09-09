import type { Perspective, TacticalEncounterBoard, VisualMedia } from "../data/hub-types";
import { TacticalBoard } from "./TacticalBoard";

// Display-only placeholder, never a proposed board or a source of token placement/scale.
const EMPTY_GRID: TacticalEncounterBoard = {
  revision: 0, columns: 20, rows: 20, feetPerSquare: 5,
  terrain: [], obstacles: [], participants: [],
};

export function CombatBoard({ board, background }: {
  board?: TacticalEncounterBoard;
  perspective: Perspective;
  background?: VisualMedia;
}) {
  return <>
    {!board ? <aside className="tactical-board-fallback" aria-label="Board unavailable">
      <h2>No accepted board is available to this view</h2>
      <p>This empty grid is only a visual placeholder. It does not establish terrain, distances, obstacles, or combatant positions. Continue using the Initiative list.</p>
    </aside> : null}
    <TacticalBoard board={board ?? EMPTY_GRID} placeholder={!board} background={background} />
  </>;
}
